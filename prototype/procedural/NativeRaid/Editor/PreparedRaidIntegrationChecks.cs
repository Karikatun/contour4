using System;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using Contour4.Procedural.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using NavigationRuntime = Contour4.Procedural.Navigation.RaidNavigation;

namespace Contour4.NativeRaid.Editor
{
    public static class PreparedRaidIntegrationChecks
    {
        private const int SolidMask = 1;
        private const string Reference = "Reference geometry (r0.35 h1.80)";
        private const string Headman = "Headman body geometry (r0.86 h2.86)";
        private static readonly RaidEnemyProfile ReferenceBody = new(.35f, 1.80f, 1.40f, 5, 14, 100, 5, true, true);
        private static readonly RaidEnemyProfile HeadmanBody = new(.86f, 2.86f, 2.30f, 5, 14, 100, 5, true, false);

        public static void Run()
        {
            int rooms = 0, portals = 0, turnSamples = 0, referencePaths = 0, headmanPaths = 0, deckLinks = 0;
            string filter = Environment.GetEnvironmentVariable("CONTOUR4_PREPARED_MODULE");
            foreach (var module in ModuleCatalog.Rooms().Where(item => string.IsNullOrEmpty(filter) || filter == item.Id))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject("Prepared raid runtime fixture");
                try
                {
                    var raid = root.AddComponent<RaidAssembly>();
                    string asset = $"Assets/Contour4/PreparedModules/{module.Id}/{module.Id}.prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                    Require(prefab, $"missing prepared asset {asset}");
                    var pose = new Pose(new Vector3(31, 14, 39), Quaternion.Euler(21, 56, -17));
                    var plan = new RaidPlan.Room(module, pose, new Pose(Vector3.zero, Quaternion.identity), 0);
                    var room = raid.PlacePrepared(plan, 0, prefab);
                    Physics.SyncTransforms();
                    var pairs = module.InternalPortals.Select(pair => MakePair(room, pair.A, pair.B)).ToArray();
                    var decks = room.PreparedSurfaces.DeckPassages(SolidMask).ToArray();
                    var nav = root.AddComponent<NavigationRuntime>();
                    nav.Install(new RaidLayout(room.Floors, pairs.Select(pair => pair.Navigation), Array.Empty<RaidWalkPassage>(),
                        SolidMask, new Vector3(14000, 0, 14000), .86f, 2.86f, decks));
                    foreach (var pair in pairs)
                    {
                        CheckPortal(pair.Forward, nav);
                        CheckPortal(pair.Reverse, nav);
                        portals += 2;
                    }
                    int travel = room.PreparedSurfaces.Turns.Sum(turn => CheckTurn(room, turn));
                    if (module.Id == SphericalChamber.Id) CheckSphereFieldLimits(room);
                    foreach (var deck in decks) CheckDeck(nav, deck);
                    Require(decks.Length == room.PreparedSurfaces.Turns.Count * 2, $"{module.Id}: rotating deck links {decks.Length}");
                    deckLinks += decks.Length;
                    int small = CheckPaths(room, nav, Reference, ReferenceBody);
                    int large = CheckPaths(room, nav, Headman, HeadmanBody);
                    Require(small > 0 && large > 0, $"{module.Id}: runtime profile paths {small}/{large}");
                    turnSamples += travel;
                    referencePaths += small;
                    headmanPaths += large;
                    rooms++;
                    Debug.Log($"CONTOUR4 PREPARED RUNTIME {module.Id}: colliders={room.Colliders.Count}, floors={nav.BakedFloorCount}, " +
                        $"portal-directions={pairs.Length * 2}, rotating-samples={travel}, deck-links={decks.Length}, " +
                        $"reference-routes={small}, headman-routes={large}");
                }
                finally { Object.DestroyImmediate(root); }
            }
            Require(rooms > 0, "No module passed the filter.");
            Debug.Log($"CONTOUR4 PREPARED RUNTIME PASSED: prefabs={rooms}, portal-directions={portals}, " +
                $"rotating-samples={turnSamples}, deck-links={deckLinks}, reference-routes={referencePaths}, headman-routes={headmanPaths}");
        }

        private static (NativeRaidPortal Forward, NativeRaidPortal Reverse, RaidPortal Navigation) MakePair(BakedRoom room, string a, string b)
        {
            Dock Dock(string id) => room.Plan.Module.Docks.Single(dock => dock.Id == id);
            var from = new RaidPlan.End(room.Plan, Dock(a));
            var to = new RaidPlan.End(room.Plan, Dock(b));
            Require(!room.CollidersById[from.Dock.PlugId].gameObject.activeSelf &&
                !room.CollidersById[to.Dock.PlugId].gameObject.activeSelf, $"{room.Root.name}/{a}-{b}: sealed internal route");
            return RaidAssembly.PlacePortalPair(room.Root.name + "/" + a, from, room, to, room, SolidMask);
        }

        private static void CheckPortal(NativeRaidPortal portal, NavigationRuntime nav)
        {
            var source = portal.Entrance;
            float depth = portal.EntryKind == RaidPortalKind.Upper ? -.14f : portal.EntryKind == RaidPortalKind.Lower ? -.025f : -portal.DoorInset;
            Vector3 sensor = source.position + source.forward * depth;
            if (portal.EntryKind == RaidPortalKind.Door) sensor -= source.up * portal.HalfHeight;
            if (portal.EntryKind == RaidPortalKind.Upper) sensor -= portal.EntryFloor.Up * (RaidAssembly.Character.Height - .10f);
            var before = sensor - source.forward * .08f;
            var after = sensor + source.forward * .08f;
            var profile = RaidAssembly.Character;
            Require(portal.Crossed(before, after, profile.Radius, profile.Height) &&
                !portal.Crossed(after, before, profile.Radius, profile.Height), $"{portal.Id}: directional aperture sweep");
            Vector3 edge = source.right * (portal.HalfWidth - profile.Radius * .5f);
            Require(!portal.Crossed(before + edge, after + edge, profile.Radius, profile.Height), $"{portal.Id}: edge clearance");
            bool crossed = RaidAssembly.TryPassPlayer(nav, SolidMask, portal, after, out var landing, before);
            if (!crossed && portal.Door)
            {
                Vector3 sill = portal.Exit.position - portal.Exit.up * portal.HalfHeight;
                for (int i = 0; i < 6; i++)
                {
                    float distance = i * .12f;
                    Vector3 feet = sill - portal.Exit.forward * distance;
                    Vector3 up = portal.ExitFloor.Up;
                    var obstacles = Physics.OverlapCapsule(feet + up * (profile.Radius + .025f),
                        feet + up * (profile.Height - profile.Radius + .025f), profile.Radius, SolidMask,
                        QueryTriggerInteraction.Ignore);
                    Debug.Log($"CONTOUR4 PREPARED RUNTIME {portal.Id} depth={distance:F2} contacts={string.Join(",", obstacles.Select(c => c.name))}");
                }
            }
            Require(Vector3.Dot(portal.TravelRotation * portal.EntryFloor.Up, portal.ExitFloor.Up) > .999f &&
                crossed && nav.BodyClear(landing, portal.ExitFloor.Up, profile), $"{portal.Id}: clear landing in destination frame");
            Require(RaidAssembly.HasPortalSupport(portal.ExitFloor, portal.ExitPad, SolidMask), $"{portal.Id}: real collider support");
            if (!portal.Door) Require(!portal.AllowsCart, $"{portal.Id}: hatch is hand-cargo only");
            if (portal.EntryKind == RaidPortalKind.Lower && portal.ExitKind == RaidPortalKind.Lower &&
                Vector3.Dot(portal.EntryFloor.Up, portal.ExitFloor.Up) < -.999f)
            {
                Vector3 intoHatch = Vector3.ProjectOnPlane(source.position - portal.EntryPad, portal.EntryFloor.Up).normalized;
                Vector3 outOfHatch = Vector3.ProjectOnPlane(portal.ExitPad - portal.Exit.position, portal.ExitFloor.Up).normalized;
                Require(Vector3.Dot(portal.TravelRotation * intoHatch, outOfHatch) > .999f,
                    $"{portal.Id}: held forward must continue away from the exit hatch");
            }
            DrivePortal(portal, nav, before);
            GameObject obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                obstruction.transform.position = portal.ExitPad + portal.ExitFloor.Up * .80f;
                obstruction.transform.rotation = portal.ExitFloor.Frame.rotation;
                obstruction.transform.localScale = new Vector3(1.6f, 1.6f, 1.6f);
                Physics.SyncTransforms();
                Require(!RaidAssembly.TryPassPlayer(nav, SolidMask, portal, after, out _, before), $"{portal.Id}: blocked exit must refuse transit");
            }
            finally { Object.DestroyImmediate(obstruction); Physics.SyncTransforms(); }
        }

        private static void DrivePortal(NativeRaidPortal portal, NavigationRuntime nav, Vector3 sourceFeet)
        {
            var profile = RaidAssembly.Character;
            var root = nav.transform;
            var stage = new Pose(root.position, root.rotation);
            var previousSimulation = Physics.simulationMode;
            var avatar = new GameObject("PhysX character portal probe") { layer = 29 };
            try
            {
                avatar.transform.SetPositionAndRotation(sourceFeet, portal.EntryFloor.Frame.rotation);
                var capsule = avatar.AddComponent<CapsuleCollider>();
                capsule.radius = profile.Radius;
                capsule.height = profile.Height;
                capsule.center = Vector3.up * (profile.Height * .5f + .025f);
                var actor = avatar.AddComponent<Rigidbody>();
                actor.useGravity = false;
                actor.linearVelocity = portal.Entrance.forward * 1.8f;
                actor.constraints = RigidbodyConstraints.FreezeRotation;
                Physics.simulationMode = SimulationMode.Script;
                Physics.SyncTransforms();
                for (int tick = 0; tick < 10; tick++)
                {
                    Vector3 previous = actor.position;
                    Physics.Simulate(.01f);
                    if (!portal.Crossed(previous, actor.position, profile.Radius, profile.Height)) continue;
                    Require(RaidAssembly.TryPassPlayer(nav, SolidMask, portal, actor.position, out var destination, previous),
                        $"{portal.Id}: occupied real-body transit");
                    new RaidWorldRebase().Apply(root, actor, Array.Empty<Rigidbody>(), destination,
                        Quaternion.Inverse(portal.TravelRotation));
                    Require(Vector3.Dot(actor.transform.up, portal.ExitFloor.Up) > .999f &&
                        RaidAssembly.HasPortalSupport(portal.ExitFloor, actor.position, SolidMask) &&
                        nav.BodyClear(actor.position, portal.ExitFloor.Up, profile), $"{portal.Id}: supported rebased character");
                    return;
                }
                throw new InvalidOperationException($"{portal.Id}: the character could not reach the swept sensor under PhysX.");
            }
            finally
            {
                Physics.simulationMode = previousSimulation;
                Object.DestroyImmediate(avatar);
                root.SetPositionAndRotation(stage.position, stage.rotation);
                Physics.SyncTransforms();
            }
        }

        private static int CheckTurn(BakedRoom room, PreparedRaidTurn turn)
        {
            var track = turn.Samples;
            bool spherical = room.Plan.Module.Id == SphericalChamber.Id;
            int checkedSamples = 0;
            foreach (bool reverse in new[] { false, true })
            for (int i = 1; i < track.Count; i++)
            {
                int index = reverse ? track.Count - i : i;
                var a = track[index - 1];
                var b = track[index];
                if (Vector3.Dot(a.up, b.up) > .999999f) continue;
                float t = reverse ? .68f : .32f;
                var rotation = Quaternion.Slerp(a.rotation, b.rotation, t);
                var up = room.Root.TransformDirection(rotation * Vector3.up);
                Vector3 p = room.Root.TransformPoint(Vector3.Lerp(a.position, b.position, t)) + up * .10f;
                Require(turn.TryUp(p, SolidMask, true, out var sampled, out float height) &&
                    Vector3.Dot(up, sampled) > .994f && height < .17f,
                    $"{room.Root.name}/{turn.Route.Id}/{index}: supported rotating traversal, reverse={reverse}");
                if (spherical)
                {
                    Vector3 local = room.Root.InverseTransformPoint(p);
                    Require(SphericalGravityField.TryUp(Numeric(local), System.Numerics.Vector3.UnitY,
                        out var fieldUp, out bool onDeck) && onDeck &&
                        Vector3.Dot(up, room.Root.TransformDirection(RaidPlan.Vector(fieldUp))) > .994f,
                        $"{room.Root.name}/{turn.Route.Id}/{index}: dedicated field follows the rotating mesh");
                }
                else
                {
                    Require(!turn.TryUp(p + up * 5.5f, SolidMask, true, out _, out _),
                        $"{room.Root.name}/{turn.Route.Id}/{index}: vertical boundary");
                }
                if (turn.Route.Cargo == Cargo.Hand)
                    Require(!turn.TryUp(p, SolidMask, false, out _, out _), $"{room.Root.name}/{turn.Route.Id}: reject cart gravity");
                checkedSamples++;
            }
            Require(checkedSamples > 0, $"{room.Root.name}/{turn.Route.Id}: no rotating segment");
            return checkedSamples;
        }

        private static void CheckSphereFieldLimits(BakedRoom room)
        {
            foreach (var (edge, outward) in new[]
            {
                (new System.Numerics.Vector3(-3.6f, .7f, SphericalChamber.HalfWidth), System.Numerics.Vector3.UnitZ),
                (new System.Numerics.Vector3(-3.6f, .7f, -SphericalChamber.HalfWidth), -System.Numerics.Vector3.UnitZ),
                (new System.Numerics.Vector3(-SphericalChamber.ServiceBayCenter, .7f, SphericalChamber.ServiceBayEdge), System.Numerics.Vector3.UnitZ),
                (new System.Numerics.Vector3(SphericalChamber.ServiceBayCenter, .7f, -SphericalChamber.ServiceBayEdge), -System.Numerics.Vector3.UnitZ),
            })
            {
                var beyond = edge + outward * .01f;
                Require(Field(edge, out var edgeUp, out bool onDeck) && onDeck && edgeUp == System.Numerics.Vector3.UnitY &&
                    Field(beyond, out var radial, out onDeck) && !onDeck &&
                    System.Numerics.Vector3.Dot(radial, -System.Numerics.Vector3.Normalize(beyond)) > .9999f,
                    $"{room.Root.name}/{edge}: rotated straight-deck edge or radial fallback");
            }

            foreach (int side in new[] { 1, -1 })
            {
                var bend = SphericalChamber.BranchFrame(side, 0, .42f, 0);
                var center = bend.Position + bend.Up * .15f;
                foreach (int edge in new[] { 1, -1 })
                {
                    var near = center + (edge * (SphericalChamber.HalfWidth - .005f)) * System.Numerics.Vector3.UnitX;
                    var beyond = center + (edge * (SphericalChamber.HalfWidth + .005f)) * System.Numerics.Vector3.UnitX;
                    Require(Field(near, out var inward, out bool deck) && deck &&
                        System.Numerics.Vector3.Dot(inward, bend.Up) > .9999f &&
                        Field(beyond, out var radial, out deck) && !deck &&
                        System.Numerics.Vector3.Dot(radial, -System.Numerics.Vector3.Normalize(beyond)) > .9999f,
                        $"{room.Root.name}/{side}/{edge}: bridge width or radial fallback");
                }

                var collar = SphericalChamber.BranchFrame(side, SphericalChamber.BranchPhaseCount - 1, .85f, 0);
                var top = collar.Position + collar.Up * SphericalGravityField.DeckReach;
                Require(Field(top, out var collarUp, out bool onDeck) && onDeck &&
                    System.Numerics.Vector3.Dot(collarUp, collar.Up) > .9999f &&
                    !Field(top + collar.Up * .01f, out _, out _),
                    $"{room.Root.name}/{side}: collar's 2 m field boundary");
            }

            // Check the interior straight deck and all three curved phases near the
            // actual 2 m cutoff, through a rotated prepared room transform.
            CheckHeight(new System.Numerics.Vector3(-3.6f, 0, 0), System.Numerics.Vector3.UnitY);
            foreach (int side in new[] { 1, -1 })
            for (int phase = 0; phase < SphericalChamber.BranchPhaseCount; phase++)
            {
                var track = SphericalChamber.BranchFrame(side, phase, phase == 3 ? .12f : .42f, 0);
                CheckHeight(track.Position, track.Up);
            }
            Debug.Log($"CONTOUR4 PREPARED RUNTIME {room.Root.name}: 9 field boundaries checked at 1.99 and 2.01 m.");

            void CheckHeight(System.Numerics.Vector3 floor, System.Numerics.Vector3 expectedUp)
            {
                var inside = floor + expectedUp * 1.99f;
                var outside = floor + expectedUp * 2.01f;
                Require(Field(inside, out var deckUp, out bool onDeck) && onDeck &&
                    System.Numerics.Vector3.Dot(deckUp, expectedUp) > .9999f &&
                    Field(outside, out var radial, out onDeck) && !onDeck &&
                    System.Numerics.Vector3.Dot(radial, -System.Numerics.Vector3.Normalize(outside)) > .9999f,
                    $"{room.Root.name}/{floor}: the deck field must end 2 m above the surface.");
            }
            bool Field(System.Numerics.Vector3 local, out System.Numerics.Vector3 up, out bool deck) =>
                SphericalGravityField.TryUp(Numeric(room.Root.InverseTransformPoint(
                    room.Root.TransformPoint(RaidPlan.Vector(local)))), System.Numerics.Vector3.UnitY, out up, out deck);
        }

        private static System.Numerics.Vector3 Numeric(Vector3 value) => new(value.x, value.y, value.z);

        private static void CheckDeck(NavigationRuntime nav, RaidDeckPassage deck)
        {
            var edge = NavigationRuntime.Edge.ForDeck(deck);
            string id = $"{deck.From.RoomId}/{deck.From.FloorId}/{deck.From.IslandId}->{deck.To.FloorId}/{deck.To.IslandId}";
            if (!nav.TryTransition(edge, ReferenceBody, out var from, out var to))
            {
                var a = deck.World(0);
                var b = deck.World(deck.Count - 1);
                var sample = Enumerable.Range(0, deck.Count).Where(i => !nav.DeckIsClear(deck, deck.World(i), ReferenceBody))
                    .DefaultIfEmpty(-1).First();
                if (sample >= 0)
                {
                    var p = deck.World(sample);
                    var clear = nav.BodyClear(p.position, p.up, ReferenceBody);
                    bool contact = Physics.Raycast(p.position + p.up * .115f, -p.up, out var hit,
                        .26f, SolidMask, QueryTriggerInteraction.Ignore);
                    Debug.Log($"CONTOUR4 PREPARED RUNTIME {id}: blocked at {p.position}, up={p.up}, " +
                        $"body-clear={clear}, support={(contact ? hit.collider.name : "none")}, " +
                        $"normal={Vector3.Dot(p.up, hit.normal):F3}");
                }
                Require(false, $"{id}: reference endpoint samples={nav.TryStand(deck.From, a.position, ReferenceBody, out _)}/" +
                    $"{nav.TryStand(deck.To, b.position, ReferenceBody, out _)}, first blocked sample={sample} of {deck.Count}");
            }
            Require(nav.TryGetRouteKind(deck.From, from, deck.To, to, ReferenceBody, out var kind) &&
                kind is RaidRouteKind.OrientedDeck or RaidRouteKind.Portal, $"{id}: integrated reference route across deck");
            Require(!nav.TryTransition(edge, HeadmanBody, out _, out _), $"{id}: fixed-up headman cannot cross deck");
            GameObject obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var middle = deck.World(deck.Count / 2);
                obstruction.transform.SetPositionAndRotation(middle.position + middle.up * .65f, middle.rotation);
                Physics.SyncTransforms();
                Require(!nav.TryTransition(edge, ReferenceBody, out _, out _), $"{id}: occupied rotating route must fail");
            }
            finally { Object.DestroyImmediate(obstruction); Physics.SyncTransforms(); }
        }

        private static int CheckPaths(BakedRoom room, NavigationRuntime nav, string name, RaidEnemyProfile profile)
        {
            int count = 0;
            var marker = room.Root.Find("Monster paths/" + name);
            Require(marker, $"{room.Root.name}: missing {name}");
            foreach (var group in marker.GetComponentsInChildren<Transform>().Where(node => node.name.StartsWith("Path ", StringComparison.Ordinal)))
            {
                Vector3 from = group.GetChild(0).position, to = group.GetChild(group.childCount - 1).position;
                if (room.Floors.Any(floor => Vector3.Dot(floor.Up, group.GetChild(0).up) > .999f &&
                    nav.TryGetRouteKind(floor, from, floor, to, profile, out var kind) && kind == RaidRouteKind.Local)) count++;
            }
            return count;
        }

        private static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException("Prepared assembly check failed: " + detail);
        }
    }
}

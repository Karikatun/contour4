using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using Contour4.Procedural.Navigation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;
using PublishedNavigation = Contour4.NativeRaid.Runtime.RaidNavigation;

namespace Contour4.NativeRaid.Editor
{
    public static class SeededRaidChecks
    {
        private const string Log = "CONTOUR4 NATIVE RAID";
        private const int ObstacleLayer = 17;

        private static void CheckGraph(RaidPlan plan)
        {
            CheckCartAccess(plan);
            var root = plan.Entrance.Room;
            var rooms = plan.Rooms.Concat(plan.RingRoom != null ? new[] { plan.RingRoom } : Array.Empty<RaidPlan.Room>()).ToHashSet();
            var adjacencies = rooms.ToDictionary(room => room, _ => new HashSet<RaidPlan.Room>());
            void Join(RaidPlan.Room a, RaidPlan.Room b)
            {
                Require(adjacencies[a].Add(b) && adjacencies[b].Add(a), "Every room edge is unique and reversible");
            }
            foreach (var link in plan.Freight)
            {
                Join(link.From.Room, link.To.Room);
                Require(link.From.Dock.MaxCargo == Contour4.Procedural.Catalog.Cargo.Cart &&
                    link.To.Dock.MaxCargo == Contour4.Procedural.Catalog.Cargo.Cart, "Freight tree uses the cart aperture");
                Require((link.Fold == null) == RaidPlan.Parallel(link.From.Up, link.To.Up),
                    "Only a supported freight fold changes the room floor orientation");
                if (link.Fold == null) continue;
                var a = new RaidPlan.End(link.Fold, link.Fold.Module.Docks[0]);
                var b = new RaidPlan.End(link.Fold, link.Fold.Module.Docks[1]);
                Require(Vector3.Distance(a.Sill, link.From.Sill) < .001f && Vector3.Distance(b.Sill, link.To.Sill) < .001f &&
                    Vector3.Dot(a.Up, link.From.Up) > .999f && Vector3.Dot(b.Up, link.To.Up) > .999f,
                    "Both physical fold ends meet their dock at the matching support frame");
                CheckBridgeGeometry(link);
            }
            foreach (var (a, b) in plan.HandPassages) Join(a.Room, b.Room);
            var reached = new HashSet<RaidPlan.Room> { root };
            var queue = new Queue<RaidPlan.Room>();
            queue.Enqueue(root);
            while (queue.Count != 0)
                foreach (RaidPlan.Room room in adjacencies[queue.Dequeue()])
                    if (reached.Add(room)) queue.Enqueue(room);
            Require(rooms.SetEquals(reached), $"Every room has a physical path from the start (seed {plan.Seed})");
            foreach (var (a, b) in plan.Portals) Join(a.Room, b.Room);
            var samePlaneDocks = plan.Freight.Where(link => link.Fold == null).Select(link => (link.From, link.To))
                .Concat(plan.HandPassages).Concat(plan.Portals.Where(pair => RaidPlan.Parallel(pair.A.Up, pair.B.Up)))
                .SelectMany(pair => new[] { pair.Item1, pair.Item2 }).Append(plan.Entrance).ToArray();
            foreach (var dock in samePlaneDocks)
                Require(samePlaneDocks.Count(other => other.Room == dock.Room && other.Dock.FloorId == dock.Dock.FloorId &&
                    Vector3.Dot(other.Up, dock.Up) > .999f) == 1,
                    $"No three rooms share one oriented floor, including reverse paths and portals ({plan.Seed}, {dock.Room.Module.Id}/{dock.Dock.Id})");
            string Arm(RaidPlan.Room room)
            {
                var parent = plan.Freight.Single(edge => edge.To.Room == room).From;
                return parent.Room == root ? parent.Dock.Id : Arm(parent.Room);
            }
            var quotas = plan.Freight.Where(link => link.To.Room.Module.Landmarks.ContainsKey(RaidExtraction.Machine)).ToArray();
            Require(quotas.All(quota => quota.From.Room != root && adjacencies[quota.To.Room].Count == 1),
                "Every additional quota terminates a freight branch beyond the first fork");
            if (quotas.Length > 1)
                Require(quotas.Select(quota => Arm(quota.To.Room)).Distinct().Count() == 2,
                    "Stock quota spacing distributes points between the two first freight branches");
        }

        internal static void CheckCartAccess(RaidPlan plan)
        {
            var rooms = plan.Rooms.Concat(plan.RingRoom == null ? Array.Empty<RaidPlan.Room>() : new[] { plan.RingRoom });
            var edges = new List<((RaidPlan.Room Room, string Site) From, (RaidPlan.Room Room, string Site) To)>();
            foreach (var room in rooms)
                AddCatalogCartRoutes(room, edges);
            foreach (var link in plan.Freight)
            {
                Require(link.From.Dock.MaxCargo == Cargo.Cart && link.To.Dock.MaxCargo == Cargo.Cart,
                    $"A freight link uses a hand dock (seed {plan.Seed}).");
                var from = (link.From.Room, link.From.Dock.Id);
                var to = (link.To.Room, link.To.Dock.Id);
                if (link.Fold == null)
                {
                    edges.Add((from, to));
                    continue;
                }
                AddCatalogCartRoutes(link.Fold, edges);
                edges.Add((from, (link.Fold, link.Fold.Module.Docks[0].Id)));
                edges.Add(((link.Fold, link.Fold.Module.Docks[1].Id), to));
            }

            var neighbors = edges.SelectMany(edge => new[] { edge, (From: edge.To, To: edge.From) })
                .ToLookup(edge => edge.From, edge => edge.To);
            var reached = new HashSet<(RaidPlan.Room Room, string Site)> { (plan.Entrance.Room, plan.Entrance.Dock.Id) };
            var pending = new Queue<(RaidPlan.Room Room, string Site)>(reached);
            while (pending.TryDequeue(out var site))
                foreach (var neighbor in neighbors[site])
                    if (reached.Add(neighbor)) pending.Enqueue(neighbor);

            string context = $"seed {plan.Seed}, completed {plan.CompletedLevels}";
            int quotaStations = 0, cartStations = 0;
            foreach (var link in plan.Freight)
                Require(reached.Contains((link.From.Room, link.From.Dock.Id)) &&
                    reached.Contains((link.To.Room, link.To.Dock.Id)),
                    $"Cart link from {link.From.Room.Module.Id}/{link.From.Dock.Id} to " +
                    $"{link.To.Room.Module.Id}/{link.To.Dock.Id} has no cart path from the truck ({context}).");
            foreach (var room in rooms)
            {
                foreach (var floor in room.Module.Floors)
                {
                    bool expected = room.Module.ExternalDocks.Any(dock => dock.FloorId == floor.Id &&
                        dock.MaxCargo == Cargo.Cart && reached.Contains((room, dock.Id)));
                    Require(plan.HasCartAccess(room, floor.Id) == expected,
                        $"Cart access disagrees with the catalog graph at {room.Module.Id}/{floor.Id} ({context}).");
                }
                if (!room.Module.Landmarks.ContainsKey(RaidExtraction.Machine)) continue;
                quotaStations++;
                if (RaidExtraction.LoadingRoute(room.Module).Cargo == Cargo.Cart)
                {
                    cartStations++;
                    Require(reached.Contains((room, RaidExtraction.Machine)),
                        $"The quota in room {room.Index} has no cart route to its ramp ({context}).");
                }
                else
                    Require(reached.Contains((room, RaidExtractionBridge.CartStaging)) &&
                        !reached.Contains((room, RaidExtraction.Machine)),
                        $"The hand quota in room {room.Index} needs a reachable cart apron and a hand-only ramp ({context}).");
            }
            Require(quotaStations == 0 || cartStations > 0,
                $"A raid with additional quotas must still have a cart-accessible station ({context}).");
        }

        private static void AddCatalogCartRoutes(RaidPlan.Room room,
            List<((RaidPlan.Room Room, string Site) From, (RaidPlan.Room Room, string Site) To)> edges)
        {
            var routes = room.Module.Routes.Where(route => route.Cargo == Cargo.Cart && route.Flight == null).ToArray();
            foreach (var route in routes)
            {
                edges.Add(((room, route.From), (room, route.To)));
                // A named route endpoint can meet an intermediate knot of another route.
                foreach (string junction in routes.SelectMany(other => new[] { other.From, other.To }).Distinct())
                    if (room.Module.Landmarks.TryGetValue(junction, out var point) &&
                        route.Knots.Skip(1).SkipLast(1).Any(knot => knot.Position == point))
                        edges.Add(((room, route.From), (room, junction)));
            }
        }

        internal static void CheckPortalSignature(RaidPlan plan)
        {
            var portals = (List<(RaidPlan.End A, RaidPlan.End B)>)plan.Portals;
            Require(portals.Count > 0, $"Seed {plan.Seed} needs a selected portal pair for the determinism regression.");
            string before = Signature(plan);
            var removed = portals[^1];
            portals.RemoveAt(portals.Count - 1);
            try
            {
                Require(Signature(plan) != before, $"Removing a portal pair must alter the signature (seed {plan.Seed}).");
            }
            finally { portals.Add(removed); }

            portals[^1] = (removed.B, removed.A);
            try
            {
                Require(Signature(plan) != before,
                    $"Reordering the endpoints of a portal pair must alter the signature (seed {plan.Seed}).");
            }
            finally { portals[^1] = removed; }
        }

        private static void CheckBridgeGeometry(RaidPlan.FreightLink link)
        {
            var from = link.From;
            var to = link.To;
            var bridge = link.Fold.Module;
            Require(Mathf.Abs(Vector3.Dot(from.Up, to.Up)) < .001f, "A bridge connects orthogonal floors");
            if (bridge.Id == RaidFreightFold.ShortRise)
            {
                Vector3 a = from.Sill + from.Up * RaidFreightFold.UpperEdge(from.Room.Module, from.Dock);
                Vector3 b = to.Sill + to.Up * RaidFreightFold.UpperEdge(to.Room.Module, to.Dock);
                Vector3 acrossA = Vector3.Cross(from.Outward, from.Up);
                Vector3 acrossB = Vector3.Cross(-to.Outward, to.Up);
                float half = from.Dock.Opening.X * .5f;
                Require(Vector3.Distance(a - acrossA * half, b - acrossB * half) < .001f &&
                    Vector3.Distance(a + acrossA * half, b + acrossB * half) < .001f,
                    "Rising rooms meet along the entire exterior upper edge of their opening");
            }
            else if (bridge.Id == RaidFreightFold.ShortDrop)
            {
                var gap = (from.Outward - from.Up) * RaidFreightFold.Drop;
                Require(Vector3.Distance(from.Sill + gap, to.Sill) < .001f,
                    "The gap between drop thresholds is exactly the short supported bridge span");
            }
            else
                Require(IsLongSpan(bridge.Id) && Vector3.Dot(to.Sill - from.Sill, from.Up) < 0f,
                    "An extended connector is a descent");
        }

        private static bool IsLongSpan(string id) => id is RaidFreightFold.LongSpan or "quarter-turn-bridge";

        private static void CheckDenseComposition(RaidPlan plan)
        {
            var bridges = plan.Freight.Where(link => link.Fold != null).Select(link => link.Fold.Module).ToArray();
            int longSpans = bridges.Count(module => IsLongSpan(module.Id));
            Require(longSpans <= 1, "A raid has at most one enclosed long descent");
            if (plan.CompletedLevels != 6 || plan.Seed != 1499861616) return;

            int shortSpans = bridges.Length - longSpans;
            float bridgeDistance = bridges.Sum(module => module.Routes[0].Knots.Zip(
                module.Routes[0].Knots.Skip(1), (a, b) => System.Numerics.Vector3.Distance(a.Position, b.Position)).Sum());
            int serviceRooms = plan.Rooms.Count(room => room.Module.Id == FreightServiceRoom.Dispatch ||
                room.Module.Id == FreightServiceRoom.InstrumentService || room.Module.Id == FreightServiceRoom.SampleReceiving);
            Require(plan.Rooms.Count >= 15 && serviceRooms >= 7 &&
                plan.Rooms.All(room => room.Module.Id != "junction-corridor"), "The owner seed has staffed branch rooms");
            Require(shortSpans + plan.Portals.Count > (plan.Freight.Count + plan.Portals.Count) / 2 &&
                bridgeDistance < 125f, "Most links are short bridges or portals on the owner seed");
            var researchPair = plan.Portals.First(pair => pair.A.Dock.Id == FreightServiceRoom.ResearchDoor);
            var serviceDoors = plan.Rooms.SelectMany(room => room.Module.ExternalDocks
                .Where(dock => dock.Id == FreightServiceRoom.ResearchDoor).Select(dock => new RaidPlan.End(room, dock))).ToArray();
            var candidates = serviceDoors.SelectMany((a, index) => serviceDoors.Skip(index + 1)
                .Where(b => !RaidPlan.Parallel(a.Up, b.Up) && !plan.Freight.Any(link =>
                    (link.From.Room == a.Room && link.To.Room == b.Room) ||
                    (link.From.Room == b.Room && link.To.Room == a.Room)))
                .Select(b => plan.WalkingDistance(a, b))).Where(float.IsFinite).ToArray();
            float shortcut = plan.WalkingDistance(researchPair.A, researchPair.B);
            Require(shortcut > 30f && shortcut >= candidates.Max() - .001f,
                "The research shortcut skips the longest compatible walk through the physical tree");
            Debug.Log($"{Log} dense owner layout: {serviceRooms} service rooms, {shortSpans} short bridges, " +
                $"{longSpans} long descents, {plan.Portals.Count} portal pairs, {bridgeDistance:F2} m total bridge length, " +
                $"{shortcut:F2} m of physical travel skipped by the service portal.");
        }

        private static void CheckFreightRaid(int seed, Pose entrance, int completed)
        {
            var root = new GameObject("Folded procedural layout");
            try
            {
                var raid = root.AddComponent<RaidAssembly>();
                raid.Place(seed, entrance, 0, 1 << ObstacleLayer, catalogSites: true, completedLevels: completed);
                RaidCatalogSites.Validate(raid);
                CheckGraph(raid.Plan);
                foreach (var fold in raid.Folds) AssertFoldClearance(fold);
                foreach (var quota in raid.Plan.Rooms.Where(room => room.Module.Landmarks.ContainsKey(RaidExtraction.Machine)))
                    AssertQuotaClearance(raid.Plan, quota);

                raid.InstallNavigation();
                Require(raid.Context.Navigation.BakedFloorCount == raid.Plan.Rooms.Count, "Every room has a gravity-local query mesh");
                CheckCupboardSill(raid);
                foreach (var portal in raid.Context.Portals)
                {
                    Vector3 sill = portal.Entrance.position - portal.Entrance.up * portal.HalfHeight;
                    Require(raid.TryPassPlayer(portal, sill + portal.Entrance.forward * .12f, out var arrival) &&
                        raid.Context.TryGetFloor(arrival, out var floor) && floor == portal.ExitFloor, "Portal landing has live, local support");
                    var obstacle = Block(root.transform, portal.ExitPad + portal.Exit.up, new Vector3(1.3f, 2f, 1.3f));
                    obstacle.transform.rotation = portal.Exit.rotation;
                    Physics.SyncTransforms();
                    Require(!raid.TryPassPlayer(portal, sill + portal.Entrance.forward * .12f, out _),
                        "Rotated landing still blocks a foreign obstacle");
                    Object.DestroyImmediate(obstacle);
                    Physics.SyncTransforms();
                }
                AssertRebase(raid);
                foreach (var fold in raid.Folds.GroupBy(fold => fold.Link.Fold.Module.Id).Select(group => group.First()))
                {
                    AssertDynamicCart(raid, fold, false);
                    AssertDynamicCart(raid, fold, true);
                }
                raid.ReleaseNavigation();
                Require(PublishedNavigation.Current == null, "Teardown releases the live raid scope");
                Debug.Log($"{Log} seed={seed}, level={completed + 1}, rooms={raid.Plan.Rooms.Count}, folds={raid.Folds.Count}," +
                    $" extraQuotas={raid.Plan.QuotaCount}, cart supports, shafts, portal landing and root rebase verified.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void CheckCupboardSill(RaidAssembly raid)
        {
            var (shaft, cupboard) = raid.Plan.HandPassages.Single(link => link.From.Room.Module.Id == "shaft-plant" &&
                link.From.Dock.Id == "upper-exit");
            Require(raid.Room(shaft.Room).Root.Find(shaft.Dock.PlugId) == null &&
                raid.Room(cupboard.Room).Root.Find(cupboard.Dock.PlugId) == null, "Both hand doors are open in the baked layout");
            Physics.SyncTransforms();
            Quaternion frame = Quaternion.LookRotation(shaft.Outward, shaft.Up);
            for (int step = -15; step <= 15; step++)
            {
                Vector3 feet = shaft.Sill + shaft.Outward * (step / 10f);
                Require(!Physics.CheckBox(feet + shaft.Up * .955f, new Vector3(.395f, .925f, .445f), frame, 1,
                    QueryTriggerInteraction.Ignore), "Hand-cargo envelope clears both sides of the real upper sill");
                Require(Physics.Raycast(feet + shaft.Up * .06f, -shaft.Up, .16f, 1, QueryTriggerInteraction.Ignore),
                    "Both sides of the cupboard sill have continuous support");
            }
            var from = raid.Room(shaft.Room).Floor;
            var to = raid.Room(cupboard.Room).Floor;
            var navigation = raid.Context.Navigation;
            Require(navigation.TryGetRouteKind(from, shaft.Pad(1.4f), to, cupboard.Pad(1.4f), RaidAssembly.Character, out var outward) &&
                outward == RaidRouteKind.PhysicalPassage &&
                navigation.TryGetRouteKind(to, cupboard.Pad(1.4f), from, shaft.Pad(1.4f), RaidAssembly.Character, out var back) &&
                back == RaidRouteKind.PhysicalPassage, "The narrow sill has a reversible walking route for a compatible character");
        }

        private static void AssertFoldClearance(FreightFold fold)
        {
            foreach (var knot in fold.Link.Fold.Module.Routes[0].Samples(.115f))
            {
                var position = fold.Root.TransformPoint(RaidPlan.Vector(knot.Position));
                var up = fold.Root.TransformDirection(RaidPlan.Vector(knot.Up));
                var facing = fold.Root.TransformDirection(RaidPlan.Vector(knot.Forward));
                Require(fold.TryUp(position + up * .12f, out Vector3 sampled) && Vector3.Dot(up, sampled) > .993f,
                    "The local gravity field follows the actual swept deck");
                CheckCart(position, facing, up);
            }
        }

        private static void AssertQuotaClearance(RaidPlan plan, RaidPlan.Room quota)
        {
            var current = quota;
            var route = RaidExtraction.LoadingRoute(current.Module);
            foreach (var knot in route.Samples(.13f))
            {
                var point = current.Point(knot.Position);
                var direction = current.Direction(knot.Forward);
                var up = current.Direction(knot.Up);
                if (route.Cargo == Cargo.Cart) CheckCart(point, direction, up);
                else CheckHand(point, direction, up);
            }
            while (current != plan.Entrance.Room)
            {
                var edge = plan.Freight.Single(link => link.To.Room == current);
                current = edge.From.Room;
                var entry = plan.Arrival(current);
                route = RaidPlan.FreightRoute(current, entry.Dock, edge.From.Dock);
                Require(route != null, "Every quota has a proven cart route through its mandatory intervening room");
                foreach (var knot in route.Samples(.13f))
                {
                    Vector3 point = current.Point(knot.Position);
                    if (Vector3.Dot(point - entry.Sill, entry.Outward) > -1.3f ||
                        Vector3.Dot(point - edge.From.Sill, edge.From.Outward) > -1.3f) continue;
                    CheckCart(point, current.Direction(knot.Forward), current.Direction(knot.Up), checkWheels: true);
                }
            }
        }

        private static void CheckCart(Vector3 position, Vector3 forward, Vector3 up, bool checkWheels = true)
        {
            var facing = Quaternion.LookRotation(forward, up);
            var hits = Physics.OverlapBox(position + up * 1.20f, new Vector3(.825f, .95f, 1.25f),
                facing, 1, QueryTriggerInteraction.Ignore);
            Require(hits.Length == 0, "Loaded cart clearance at " + position + ": " + string.Join(",", hits.Select(hit => hit.name)));
            if (!checkWheels) return;
            foreach (float x in new[] { -.67f, .67f })
            foreach (float z in new[] { -.85f, .85f })
            {
                Vector3 wheel = position + facing * new Vector3(x, .55f, z);
                Require(Physics.Raycast(wheel, -up, out var contact, 1.1f, 1,
                    QueryTriggerInteraction.Ignore) && Vector3.Dot(contact.normal, up) > .6f,
                    "Every cart wheel has support at " + wheel);
            }
        }

        private static void CheckHand(Vector3 feet, Vector3 forward, Vector3 up)
        {
            Quaternion facing = Quaternion.LookRotation(forward, up);
            Require(!Physics.CheckBox(feet + up * .96f, new Vector3(.445f, .92f, .395f), facing,
                1, QueryTriggerInteraction.Ignore), "Hand cargo sweep must clear the bridge and approaches at " + feet);
            // A small contact disk models a foot on the staggered planks without treating a seam as a fall.
            Require(Physics.SphereCast(feet + up * .15f, .025f, -up, out var hit, .22f, 1,
                    QueryTriggerInteraction.Ignore) && Vector3.Dot(hit.normal, up) > .8f,
                "Hand cargo route must have continuous foot support at " + feet);
        }

        private static void AssertRebase(RaidAssembly raid)
        {
            NativeRaidPortal portal = raid.Context.Portals[0];
            var player = new GameObject("Unmoved player body");
            var cargo = new GameObject("External tracked cargo");
            try
            {
                var observer = player.AddComponent<Rigidbody>();
                observer.position = portal.EntryPad;
                var body = cargo.AddComponent<Rigidbody>();
                body.position = portal.ExitPad;
                body.linearVelocity = portal.Exit.up;
                var localCargo = raid.transform.InverseTransformPoint(body.position);
                var originalFeet = observer.position;
                new RaidWorldRebase().Apply(raid.transform, observer, new[] { body }, portal.ExitPad, Quaternion.Inverse(portal.Rotation));
                Require(Vector3.Distance(observer.position, originalFeet) < .001f &&
                    Vector3.Distance(body.position, raid.transform.TransformPoint(localCargo)) < .001f &&
                    Vector3.Dot(body.linearVelocity.normalized, portal.Exit.up) > .999f,
                    "Runtime rebase preserves player world pose and tracked cargo deck-relative pose and velocity");
                Require(Vector3.Distance(portal.ExitPad, observer.position) < .001f &&
                    raid.Context.Navigation.TryStand(portal.ExitFloor, observer.position, out _),
                    "The paired landing and gravity-local navigation track the rebased floor");
            }
            finally
            {
                Object.DestroyImmediate(cargo);
                Object.DestroyImmediate(player);
            }
        }

        private static void AssertDynamicCart(RaidAssembly raid, FreightFold fold, bool reverse)
        {
            var proxy = new GameObject("World-up controller pivot");
            var cart = new GameObject("Loaded cart collision proxy");
            var observer = proxy.AddComponent<Rigidbody>();
            observer.isKinematic = true;
            var body = cart.AddComponent<Rigidbody>();
            body.mass = 35f;
            body.freezeRotation = true;
            var deck = cart.AddComponent<BoxCollider>();
            deck.center = Vector3.up * .58f;
            deck.size = new Vector3(1.3f, .35f, 1.6f);
            var load = cart.AddComponent<BoxCollider>();
            load.center = Vector3.up * 1.24f;
            load.size = new Vector3(1.4f, 1f, 1.65f);
            foreach (float x in new[] { -.53f, .53f })
            foreach (float z in new[] { -.68f, .68f })
            {
                var wheel = cart.AddComponent<SphereCollider>();
                wheel.center = new Vector3(x, .18f, z);
                wheel.radius = .18f;
            }
            var before = new Pose(raid.transform.position, raid.transform.rotation);
            var simulation = Physics.simulationMode;
            try
            {
                var route = fold.Link.Fold.Module.Routes[0];
                var first = route.Knots[0];
                var last = route.Knots[route.Knots.Length - 1];
                var course = Enumerable.Range(1, 34).Reverse().Select(i =>
                    new Contour4.Procedural.ModulePose(first.Point(-i * .05f, 0), first.Forward, first.Up))
                    .Concat(route.Samples(.05f))
                    .Concat(Enumerable.Range(1, 34).Select(i =>
                        new Contour4.Procedural.ModulePose(last.Point(i * .05f, 0), last.Forward, last.Up))).ToArray();
                if (reverse) Array.Reverse(course);
                var start = fold.Root.TransformPoint(RaidPlan.Vector(course[0].Position));
                Vector3 initialUp = fold.Root.TransformDirection(RaidPlan.Vector(course[0].Up));
                body.position = start + initialUp * .055f;
                Physics.simulationMode = SimulationMode.Script;
                var stage = new RaidWorldRebase();
                const float step = .02f;
                float worstHeight = 0f;
                foreach (var knot in course)
                {
                    Vector3 planned = RaidPlan.Vector(knot.Position);
                    Vector3 desiredUp = fold.Root.TransformDirection(RaidPlan.Vector(knot.Up));
                    observer.position = fold.Root.TransformPoint(planned);
                    stage.Apply(raid.transform, observer, new[] { body }, observer.position,
                        Quaternion.FromToRotation(desiredUp, Vector3.up));
                    Vector3 position = fold.Root.TransformPoint(planned);
                    Vector3 direction = fold.Root.TransformDirection(RaidPlan.Vector(knot.Forward)) * (reverse ? -1f : 1f);
                    body.rotation = Quaternion.LookRotation(direction, Vector3.up);
                    // Apply horizontal steering as the grabber would. Vertical velocity comes solely from PhysX contact and gravity.
                    Vector3 steer = Vector3.ProjectOnPlane(position - body.position, Vector3.up);
                    body.linearVelocity = Vector3.ClampMagnitude(steer * 16f, 5f) + Vector3.up * body.linearVelocity.y;
                    Physics.Simulate(step);
                    worstHeight = Mathf.Max(worstHeight, Mathf.Abs(body.position.y - position.y));
                    Require(worstHeight < .52f && steer.magnitude < .53f,
                        $"Loaded dynamic proxy stays on its rotating freight deck ({(reverse ? "reverse" : "forward")}, fall={worstHeight:F2}, lag={steer.magnitude:F2})");
                }
                Debug.Log($"{Log} PhysX dynamic loaded proxy {(reverse ? "reverse" : "forward")}: {course.Length} ticks, " +
                    $"max deck height error={worstHeight:F3} m.");
            }
            finally
            {
                Physics.simulationMode = simulation;
                raid.transform.SetPositionAndRotation(before.position, before.rotation);
                Object.DestroyImmediate(cart);
                Object.DestroyImmediate(proxy);
                Physics.SyncTransforms();
            }
        }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in the dedicated batch project.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var entrance = new Pose(new Vector3(40, 0, -20), Quaternion.Euler(0, 90, 0));
            var transits = new HashSet<string>();
            bool catalogBridge = false;
            var catalogLayouts = new Dictionary<string, (int Seed, int Completed)>();
            foreach (int completed in new[] { 0, 1, 3, 5, 6, 9, 14 })
            {
                var plans = Enumerable.Range(-32, 64).Append(2133).Append(1499861616)
                    .Select(seed => RaidPlan.Generate(seed, entrance, completed,
                        maintenance: Contour4.Procedural.Catalog.MaintenanceTrench.Create)).ToArray();
                int variants = plans.Select(plan => string.Join(";", plan.Rooms.Select(RoomFrame))).Distinct().Count();
                Require(variants > 8, $"Level {completed + 1}: seed varies actual rooms and poses ({variants})");
                foreach (var plan in plans)
                {
                    CheckExtractionProgression(plan, allowFallback: true);
                    Require(plan.Rooms.Count >= RaidPlan.CatalogCapacity(completed) + plan.QuotaCount &&
                        plan.QuotaCount == RaidExtraction.AdditionalCount(RaidPlan.CatalogCapacity(completed)) &&
                        plan.Freight.Count + plan.HandPassages.Count == plan.Rooms.Count - 1,
                        "Progression budget fits in one connected physical tree");
                    foreach (var (from, to) in plan.Passages)
                    {
                        bool handOnly = plan.HandPassages.Contains((from, to));
                        Require(Vector3.Distance(from.Sill, to.Sill) < .001f && Vector3.Dot(from.Outward, to.Outward) < -.999f &&
                            Vector3.Dot(from.Up, to.Up) > .999f && (handOnly
                                ? RaidPlan.HandCompatible(from.Dock, to.Dock)
                                : from.Dock.Opening == to.Dock.Opening),
                            "Physical passages match world threshold, directions, up and their cargo profile");
                    }
                    var (shaft, cupboard) = plan.HandPassages.Single(link => link.From.Room.Module.Id == "shaft-plant" &&
                        link.From.Dock.Id == "upper-exit");
                    Require(cupboard.Room.Module.Id == "service-cupboard" && cupboard.Dock.Id == "service-entry" &&
                        shaft.Dock.MaxCargo == Contour4.Procedural.Catalog.Cargo.Hand &&
                        cupboard.Dock.MaxCargo == Contour4.Procedural.Catalog.Cargo.Hand &&
                        plan.Freight.All(link => link.To.Room != cupboard.Room && link.From.Room != cupboard.Room),
                        "The real upper dock connects a hand-only cupboard beyond the heavy-freight shaft route");
                    foreach (var spiral in plan.Rooms.Where(room => room.Module.Id == "spiral-corridor"))
                    {
                        var entry = plan.HandPassages.Single(link => link.To.Room == spiral);
                        var exit = plan.HandPassages.Single(link => link.From.Room == spiral);
                        Require(entry.To.Dock.Id == "flat-entry" && exit.From.Dock.Id == "inverted-exit" &&
                            Vector3.Dot(entry.To.Up, exit.From.Up) < -.999f &&
                            plan.Freight.All(link => link.From.Room != spiral && link.To.Room != spiral),
                            "The spiral reverses its support orientation along a hand-only branch outside the freight tree");
                    }
                    string transit = plan.ShaftTransit.Module.Id;
                    transits.Add(transit);
                    if (transit == "junction-corridor")
                        Require(plan.ShaftTransit.OpenPlugs.Count == 3, "The junction serves both shaft and calibration branches");
                    catalogLayouts.TryAdd(transit, (plan.Seed, completed));
                    if (plan.Rooms.Any(room => room.Module.Id == "quarter-turn-bridge") ||
                        plan.Freight.Any(link => link.Fold?.Module.Id == "quarter-turn-bridge"))
                    {
                        catalogBridge = true;
                        catalogLayouts.TryAdd("quarter-turn-bridge", (plan.Seed, completed));
                    }
                    CheckGraph(plan);
                    CheckDenseComposition(plan);
                    Require(Signature(plan) == Signature(RaidPlan.Generate(plan.Seed, entrance, completed,
                        maintenance: Contour4.Procedural.Catalog.MaintenanceTrench.Create)),
                        "Preserved seed and progression reproduce the complete plan");
                }
                Debug.Log($"{Log} level={completed + 1}, modules={plans[0].Rooms.Count}, " +
                    $"extraQuotas={plans[0].QuotaCount}, portalPairs={plans[0].Portals.Count}, " +
                    $"{variants} distinct layouts over {plans.Length} seeds.");
            }
            Require(new[] { "straight-corridor-short", "straight-corridor-long", "elbow-corridor", "junction-corridor" }
                .All(transits.Contains) && catalogBridge, "Every enabled authored transit occurs in the fixed corpus");
            Debug.Log($"{Log} authored transit layouts: " + string.Join(", ", catalogLayouts.Select(layout =>
                $"{layout.Key} seed={layout.Value.Seed} stage={layout.Value.Completed}")));
            Require(RaidPlan.CatalogCapacity(0) == 5 && RaidPlan.CatalogCapacity(6) == 10,
                "Stock-size expectation at level 1 and owner level 7");
            foreach (float yaw in new[] { 0f, 90f, 180f, 270f, 41f })
            foreach (int seed in new[] { 42, 2133, 1499861616 })
            {
                var rotatedEntrance = new Pose(entrance.position, Quaternion.Euler(0f, yaw, 0f));
                CheckGraph(RaidPlan.Generate(seed, rotatedEntrance, 6,
                    maintenance: Contour4.Procedural.Catalog.MaintenanceTrench.Create));
            }
            foreach (int level in new[] { 0, 6, 14 })
                foreach (int seed in new[] { 2133, 1499861616 })
                    CheckFreightRaid(Enumerable.Range(seed, 16).First(candidate => HasGrayPassageFrames(candidate, level)), entrance, level);
            foreach (var (seed, completed) in catalogLayouts.Values.Distinct())
                if (HasGrayPassageFrames(seed, completed)) CheckFreightRaid(seed, entrance, completed);

            Debug.Log($"{Log} PASSED ({Application.unityVersion}, PhysX and NavMeshBuilder)");

            // The gray fixture has one floor frame per room. PreparedModulePackageChecks loads the
            // shipped prefabs and checks passage frames for layouts with turned or inverted docks.
            bool HasGrayPassageFrames(int seed, int completed) => RaidPlan.Generate(seed, entrance, completed,
                    maintenance: Contour4.Procedural.Catalog.MaintenanceTrench.Create).Passages
                .All(passage => Vector3.Dot(passage.From.Room.Up, passage.To.Room.Up) > .999f);
        }

        internal static void CheckExtractionBridges(RaidRoomPrefabs prefabs, Action<RaidAssembly> checkMap)
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in the dedicated batch project.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var entrance = new Pose(new Vector3(40, 0, -20), Quaternion.Euler(0, 90, 0));
            CheckQuotaFallback(entrance, prefabs);
            foreach (int completed in new[] { 1, 4, 5, 8, 9, 14 })
            foreach (int seed in new[] { -32, 42, 1499861616 })
            {
                var stage = new GameObject("Live quota layout");
                try
                {
                    var raid = stage.AddComponent<RaidAssembly>();
                    raid.Place(seed, entrance, 0, 1 << ObstacleLayer, catalogSites: true, completedLevels: completed,
                        roomPrefabs: prefabs);
                    Physics.SyncTransforms();
                    CheckExtractionProgression(raid.Plan);
                    CheckCartAccess(raid.Plan);
                    var bridges = raid.Rooms.Where(candidate =>
                        candidate.Plan.Module.Id is RaidExtraction.StraightBridgeId or RaidExtraction.BentBridgeId).ToArray();
                    var hazards = bridges.Length > 0 ? RaidRoomInteractions.Attach(raid) : null;
                    bool checkNavigation = completed is 5 or 9 && seed == -32;
                    if (checkNavigation) raid.InstallNavigation();
                    foreach (var room in bridges)
                    {
                        var module = room.Plan.Module;
                        var ramp = RaidExtraction.LoadingRoute(module);
                        var last = ramp.Knots.Last();
                        var root = room.Root;
                        var cart = module.Routes.Single(route => route.To == RaidExtractionBridge.CartStaging);
                        Require(cart.Cargo == Cargo.Cart && ramp.Cargo == Cargo.Hand,
                            "The cart can enter the apron and the suspended route is classified for hand cargo.");
                        Vector3 parking = root.TransformPoint(RaidPlan.Vector(cart.Knots.Last().Position));
                        for (int angle = 0; angle < 360; angle += 15)
                            CheckCart(parking, Quaternion.AngleAxis(angle, root.up) * root.right, root.up);
                        foreach (var sample in ramp.Samples(.08f))
                        {
                            if (sample.Position.X < 1.3f) continue;
                            var point = root.TransformPoint(RaidPlan.Vector(sample.Position));
                            var forward = root.TransformDirection(RaidPlan.Vector(sample.Forward));
                            CheckHand(point, forward, root.up);
                            CheckHand(point, -forward, root.up);
                        }
                        bool bent = module.Id == RaidExtraction.BentBridgeId;
                        CheckTimber(room, bent);
                        Vector3 abyss = root.TransformPoint(new Vector3(7f, .1f, -3.85f));
                        Vector3 fallen = abyss - root.up * 6f;
                        Require(!Physics.Raycast(abyss, -root.up, 5f, 1, QueryTriggerInteraction.Ignore) &&
                            hazards.CatchesFall(abyss, fallen) && !hazards.CatchesFall(parking, parking + root.right),
                            "The open shaft is unsupported and catches a falling body while leaving the apron safe.");
                        CheckShaftWalls(room, bent);
                        Vector3 expected = bent ? Vector3.forward : Vector3.right;
                        Require(Vector3.Dot(RaidPlan.Vector(last.Forward), expected) > .999f &&
                            Vector3.Dot(RaidExtraction.FaceLoading(last, Vector3.left) * Vector3.left, -expected) > .999f,
                            "The native ramp faces the end of the actual approach");
                        Vector3 machine = RaidPlan.Vector(module.Landmarks[RaidExtraction.Machine]);
                        Vector3 final = RaidPlan.Vector(last.Position);
                        Require((machine - final - 4.6f * expected).sqrMagnitude < .0001f &&
                            Physics.Raycast(root.TransformPoint(machine) + root.up * .1f, -root.up, out var landing,
                                .2f, 1, QueryTriggerInteraction.Ignore) && landing.collider.name == "extraction-landing",
                            "The stock machine sits on the far landing, in loading range of its supported approach.");
                        if (checkNavigation)
                        {
                            var nav = raid.Context.Navigation;
                            Vector3 approach = root.TransformPoint(final);
                            Require(nav.TryGetRouteKind(room.Floor, parking, room.Floor, approach, RaidAssembly.Character,
                                    out var outward) && outward == RaidRouteKind.Local &&
                                nav.TryGetRouteKind(room.Floor, approach, room.Floor, parking, RaidAssembly.Character,
                                    out var back) && back == RaidRouteKind.Local,
                                "A small character has reversible navigation over the actual timber: " + module.Id);
                            Require(!nav.TryStand(room.Floor, abyss, RaidAssembly.Character, out _),
                                "Character navigation must leave the shaft open.");
                        }
                    }
                    if (bridges.Length > 0) checkMap(raid);
                    Debug.Log($"{Log} extraction bridges seed={seed} completed={completed}: " +
                        string.Join(", ", raid.Plan.Rooms.Where(room => room.Module.Landmarks.ContainsKey(RaidExtraction.Machine))
                            .Select(room => room.Module.Id)));
                }
                finally { Object.DestroyImmediate(stage); }
            }
            Debug.Log($"{Log} EXTRACTION BRIDGES PASSED");
        }

        private static void CheckTimber(BakedRoom room, bool bent)
        {
            Transform root = room.Root;
            var planks = room.Colliders.Where(part => part.name.StartsWith(RaidExtractionBridge.TimberPrefix,
                StringComparison.Ordinal)).ToArray();
            var fragments = room.Colliders.Where(part => part.name.StartsWith(RaidExtractionBridge.FracturePrefix,
                StringComparison.Ordinal)).ToArray();
            Require(planks.Length > 0 && planks.Concat(fragments).All(part => part is MeshCollider mesh &&
                !mesh.attachedRigidbody && mesh.TryGetComponent(out MeshFilter filter) && mesh.sharedMesh == filter.sharedMesh) &&
                fragments.Length >= 3 && !room.Colliders.Any(part =>
                    part.name.Contains("bridge-rail", StringComparison.Ordinal) || part.name.StartsWith("hanging-", StringComparison.Ordinal)),
                "Visible timber and ragged masonry use their actual meshes, without suspended fixtures over the pit.");
            CheckRows(bent ? "incoming" : "long", acrossX: false);
            // Check both physical edges, including points covered by the former broad turn pad.
            for (float x = 5.15f; x < (bent ? 10f : 12f); x += .34f)
                CheckWidth(new Vector3(x, 0, 0), Vector3.forward);
            if (bent)
            {
                CheckRows("outgoing", acrossX: true);
                for (float z = .95f; z < 6; z += .32f)
                    CheckWidth(new Vector3(RaidExtractionBridge.BentAxis, 0, z), Vector3.right);
                Require(!HitsDeck(new Vector3(8.2f, 0, 2.5f)) && HitsDeck(new Vector3(RaidExtractionBridge.BentAxis, 0, 0)),
                    "The bend is a tight elbow with no diagonal support across the old turn pad.");
            }

            bool HitsDeck(Vector3 local) =>
                Physics.Raycast(root.TransformPoint(local) + root.up * .3f, -root.up, .44f, 1, QueryTriggerInteraction.Ignore);

            void CheckRows(string span, bool acrossX)
            {
                var pieces = planks.Where(part => part.name.StartsWith(RaidExtractionBridge.TimberPrefix + span + "-",
                    StringComparison.Ordinal)).Cast<MeshCollider>().ToArray();
                var rows = pieces.GroupBy(part => part.name[part.name.Length - 1]).ToArray();
                Require(rows.Length == 2 && rows.All(row => row.Count() >= 2),
                    "Each span must contain exactly two rows of separate planks.");
                foreach (var part in pieces)
                {
                    Vector3 size = part.sharedMesh.bounds.size;
                    Require(Mathf.Abs((acrossX ? size.x : size.z) - .40f) < .010f,
                        "Each plank keeps its nominal 0.40 m width: " + part.name);
                }
            }

            void CheckWidth(Vector3 center, Vector3 across)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Require(HitsDeck(center + side * .35f * across),
                        "Timber remains walkable out to the visibly worn edge at " + center);
                    Require(!HitsDeck(center + side * .44f * across),
                        "The pit begins at the visible timber edge without concealed support at " + center);
                }
            }
        }

        private static void CheckQuotaFallback(Pose entrance, RaidRoomPrefabs prefabs)
        {
            const int seed = -32, completed = 9;
            bool CanPrepare(RaidPlan.Room room) => prefabs.TryGetPrepared(room, out var prefab) && prefab;
            RaidPlan ordinary = null;
            foreach (bool rotary in new[] { false, true })
            {
                var attempts = new List<QuotaLayout>();
                var plan = RaidPlan.GenerateWithQuotaFallback(seed, completed, rotary, selection =>
                {
                    attempts.Add(selection);
                    if (selection != QuotaLayout.Ordinary)
                        throw new RaidPlan.PlacementException("Forced advanced quota placement exhaustion.");
                    return ordinary ??= RaidPlan.GenerateLayoutWithReservedQuotas(seed, entrance, completed, null,
                        MaintenanceTrench.Create, CanPrepare, selection);
                });
                var expected = rotary
                    ? new[] { QuotaLayout.Rotary, QuotaLayout.Progressive, QuotaLayout.Ordinary }
                    : new[] { QuotaLayout.Progressive, QuotaLayout.Ordinary };
                Require(attempts.SequenceEqual(expected), "Exhausted quota layouts retry each applicable simpler layout once");
                Require(plan.CompletedLevels == completed && plan.Seed == seed &&
                    plan.QuotaCount == RaidExtraction.AdditionalCount(RaidPlan.CatalogCapacity(completed)) &&
                    plan.Rooms.Count(room => room.Module.Id == RaidExtraction.OrdinaryId) == plan.QuotaCount,
                    "Fallback generates an ordinary, full-size quota layout at the same run progression and seed");
                CheckGraph(plan);
            }
        }

        private static void CheckShaftWalls(BakedRoom room, bool bent)
        {
            float length = room.Plan.Module.Plan.Y;
            float far = bent ? RaidExtractionBridge.BentFarWall : RaidExtractionBridge.StraightFarWall;
            foreach (float height in new[] { -.2f, -2f, -4f, -6.9f })
            {
                var rays = new[]
                {
                    ("entry", new Vector3(.6f, height, 0), Vector3.left),
                    ("back", new Vector3(length - .6f, height, 0), Vector3.right),
                    ("near", new Vector3(8f, height, RaidExtractionBridge.NearWall + .6f), Vector3.back),
                    ("far", new Vector3(8f, height, far - .6f), Vector3.forward),
                };
                foreach (var (side, point, direction) in rays)
                {
                    Require(Physics.Raycast(room.Root.TransformPoint(point), room.Root.TransformDirection(direction),
                            out var hit, .55f, 1, QueryTriggerInteraction.Ignore) &&
                        hit.collider.name == "shaft-wall-" + side && hit.collider.transform.parent == room.Root,
                        "A falling body cannot leave the shaft through the " + side + " perimeter");
                }
            }
        }

        private static void CheckExtractionProgression(RaidPlan plan, bool allowFallback = false)
        {
            var stations = plan.Rooms.Where(room => room.Module.Landmarks.ContainsKey(RaidExtraction.Machine))
                .Select(room => room.Module.Id).ToArray();
            Require(stations.Length == RaidExtraction.AdditionalCount(RaidPlan.CatalogCapacity(plan.CompletedLevels)),
                "The stock-sized quota count stays the same across difficulty variants");
            if (allowFallback && stations.All(id => id == RaidExtraction.OrdinaryId)) return;
            Require(stations.Count(id => id == RaidExtraction.StraightBridgeId) ==
                (plan.CompletedLevels >= RaidExtraction.StraightBridgeUnlock ? 1 : 0), "The ruined bridge unlocks on the stated stage");
            Require(stations.Count(id => id == RaidExtraction.BentBridgeId) ==
                (plan.CompletedLevels >= RaidExtraction.BentBridgeUnlock ? 1 : 0), "The bent bridge unlocks on the stated stage");
            Require(stations.Length == 0 || stations.Contains(RaidExtraction.OrdinaryId),
                "Every generated raid with an extra quota preserves an ordinary station");
        }

        private static GameObject Block(Transform root, Vector3 position, Vector3 scale)
        {
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.transform.SetParent(root, false);
            obstacle.transform.position = position;
            obstacle.transform.localScale = scale;
            Physics.SyncTransforms();
            return obstacle;
        }

        internal static string Signature(RaidPlan plan)
        {
            var entries = new List<string>
            {
                $"attempt|{plan.Attempt}",
                $"entry|{EndKey(plan.Entrance)}",
                $"shaft|{plan.ShaftTransit.Index}",
                $"quotas|{plan.QuotaCount}",
                $"portal-count|{plan.PortalPairCount}",
            };
            foreach (var room in plan.Rooms) RecordRoom(room);
            if (plan.RingRoom != null) RecordRoom(plan.RingRoom);
            foreach (var link in plan.Freight)
            {
                entries.Add($"cart|{EndKey(link.From)}|{EndKey(link.To)}");
                if (link.Fold == null) continue;
                entries.Add($"fold|{RoomFrame(link.Fold)}");
                foreach (var route in link.Fold.Module.Routes)
                foreach (var knot in route.Knots)
                    entries.Add($"fold-knot|{VectorKey(RaidPlan.Vector(knot.Position))}|" +
                        $"{VectorKey(RaidPlan.Vector(knot.Forward))}|{VectorKey(RaidPlan.Vector(knot.Up))}");
            }
            foreach (var (from, to) in plan.HandPassages)
                entries.Add($"hand|{EndKey(from)}|{EndKey(to)}");
            foreach (var (a, b) in plan.Portals)
                entries.Add($"portal|{EndKey(a)}|{EndKey(b)}");

            if (plan.Planar != null)
            {
                RecordTabFloor(RaidPlanarLayout.Stock, RaidPlanarLayout.StockFloor);
                foreach (var room in plan.Rooms)
                foreach (var floor in room.Module.Floors)
                    RecordTabFloor(room.Index, floor.Id);
                if (plan.Branch is { } branch)
                    foreach (string floor in branch.Descriptor.Outline?.Floors.Select(floor => floor.Name) ?? new[] { "ground" })
                        RecordTabFloor(plan.RingRoom.Index, floor);
            }
            return string.Join("\n", entries);

            void RecordRoom(RaidPlan.Room room)
            {
                entries.Add($"room|{RoomFrame(room)}");
                foreach (var dock in room.Module.Docks)
                    entries.Add($"dock|{dock.Id}|{dock.FloorId}|{dock.PlugId}|{room.OpenPlugs.Contains(dock.PlugId)}");
                foreach (var (a, b) in room.Module.InternalPortals)
                    entries.Add($"internal-portal|{a}|{b}");
                if (room.Module.Landmarks.TryGetValue(RaidExtraction.Machine, out var quota))
                    entries.Add($"quota|{room.Index}|{VectorKey(room.Point(quota))}");
            }

            void RecordTabFloor(int room, string floor)
            {
                Require(plan.Planar.TryGetPose(room, floor, out var pose),
                    $"Missing Tab pose for {room}/{floor} (seed {plan.Seed}).");
                entries.Add($"tab|{room}|{floor}|{FloatKey(pose.Position.x)},{FloatKey(pose.Position.y)},{FloatKey(pose.Angle)}");
            }
        }

        private static string EndKey(RaidPlan.End end) => $"{end.Room.Index}/{end.Dock.Id}";

        private static string RoomFrame(RaidPlan.Room room)
        {
            Quaternion rotation = room.Frame.rotation;
            return $"{room.Index}/{room.Module.Id}|{VectorKey(room.Frame.position)}|" +
                $"{FloatKey(rotation.x)},{FloatKey(rotation.y)},{FloatKey(rotation.z)},{FloatKey(rotation.w)}";
        }

        private static string VectorKey(Vector3 vector) => $"{FloatKey(vector.x)},{FloatKey(vector.y)},{FloatKey(vector.z)}";

        private static string FloatKey(float value) => BitConverter.SingleToInt32Bits(value).ToString("X8");

        private static void Require(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException(message);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Contour4.NativeRaid.Editor;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using Contour4.Procedural.Navigation;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceJourney
{
    public static class PreparedModulePackageChecks
    {
        private const string Log = "CONTOUR4 PREPARED PACKAGE";
        private static readonly Pose Entrance = new(new Vector3(40, 0, -20), Quaternion.Euler(0, 90, 0));
        private static readonly RaidEnemyProfile Reference = new(.35f, 1.80f, 1.40f, 5, 14, 100, 5, true, true);
        private static readonly RaidEnemyProfile Headman = new(.86f, 2.86f, 2.30f, 5, 14, 100, 5, true, false);
        private static readonly (int Progress, int Seed)[] Witnesses =
        {
            (0, -32), (3, -32), (3, -31), (3, -30), (3, -20), (3, -19),
            (5, -32), (5, -31), (5, -30), (5, -29), (5, -27), (5, -26), (5, -25), (5, -22),
            (14, 0), (14, -59), (14, -58), (14, -57), (14, -16), (5, 12), (5, 17),
            (5, -63), (5, -60), (5, -59), (5, -56), (5, -62), (5, -47),
        };

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            using var bundle = RoomPrefabBundle.Load(Environment.GetEnvironmentVariable("CONTOUR4_ROOMS_BUNDLE"),
                Environment.GetEnvironmentVariable("CONTOUR4_PREPARED_BUNDLE"));
            foreach (int mask in SphericalChamber.VariantMasks)
                VerifyAuthoredGeometry(bundle.Prefabs, SphericalChamber.Create(mask),
                    instance => VerifySpherePrefab(instance, mask));
            VerifyAuthoredGeometry(bundle.Prefabs, MobiusHall.Create(), MobiusHallChecks.Verify);
            var expected = new HashSet<string>(ModuleCatalog.Rooms().Select(module => module.Id), StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var eastPortals = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (progress, seed) in Witnesses)
                Verify(bundle.Prefabs, expected, seen, eastPortals, progress, seed);
            var missing = expected.Where(id => !SphericalChamber.IsVariant(id) && !seen.Contains(id)).ToArray();
            Require(missing.Length == 0, "seeded raids did not instantiate: " + string.Join(", ", missing));
            Require(eastPortals.Contains(MobiusHall.Id) && eastPortals.Any(SphericalChamber.IsVariant),
                "seeded raids must exercise both compositions' east portals.");
            var sphereIds = seen.Where(SphericalChamber.IsVariant).OrderBy(SphericalChamber.GetMask).ToArray();
            foreach (int mask in SphericalChamber.VariantMasks.Where(mask => mask != SphericalChamber.FullMask))
                VerifySphereFixture(bundle.Prefabs, expected, mask);
            Debug.Log($"{Log} SPHERE VARIANTS {SphericalChamber.VariantMasks.Count} packed; {string.Join(", ", sphereIds)} natural");
            Debug.Log($"{Log} PASSED: {seen.Count} exact prefab IDs instantiated in {Witnesses.Length} natural raids, plus variant fixtures.");
        }

        public static void RunMobiusSmoke()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            using var bundle = RoomPrefabBundle.Load(Environment.GetEnvironmentVariable("CONTOUR4_ROOMS_BUNDLE"),
                Environment.GetEnvironmentVariable("CONTOUR4_PREPARED_BUNDLE"));
            var expected = new HashSet<string>(ModuleCatalog.Rooms().Select(module => module.Id), StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Verify(bundle.Prefabs, expected, seen, new HashSet<string>(StringComparer.Ordinal), 5, -32);
            Require(seen.Contains(MobiusHall.Id), "small smoke did not instantiate the Möbius hall at 5,-32.");
            Debug.Log($"{Log} MOBIUS SMOKE PASSED: exact installed scene 5,-32.");
        }

        public static void RunExtractionBridges()
        {
            using var bundle = RoomPrefabBundle.Load(Environment.GetEnvironmentVariable("CONTOUR4_ROOMS_BUNDLE"),
                Environment.GetEnvironmentVariable("CONTOUR4_PREPARED_BUNDLE"));
            Contour4.NativeRaid.Editor.SeededRaidChecks.CheckExtractionBridges(bundle.Prefabs);
        }

        public static void RunMobiusOutletWitnesses()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            using var bundle = RoomPrefabBundle.Load(Environment.GetEnvironmentVariable("CONTOUR4_ROOMS_BUNDLE"),
                Environment.GetEnvironmentVariable("CONTOUR4_PREPARED_BUNDLE"));
            var expected = new HashSet<string>(ModuleCatalog.Rooms().Select(module => module.Id), StringComparer.Ordinal);
            var plan = RaidPlan.Generate(-32, Entrance, 5, maintenance: MaintenanceTrench.Create,
                canPrepare: room => bundle.Prefabs.TryGetPrepared(room, out var prefab) && prefab);
            var hall = plan.Rooms.Single(room => room.Module.Id == MobiusHall.Id);
            var ceiling = hall.Dock("ceiling-east");
            Require(hall.OpenPlugs.Contains(ceiling.PlugId), "natural 5,-32 witness must select the ceiling outlet.");
            Verify(bundle.Prefabs, expected, new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal), 5, -32);

            // Constrained absence fixture: withdraw compatible free partners, then run the
            // unchanged production selector and production prepared-room cap installation.
            foreach (var (a, b) in plan.Portals)
            {
                a.Room.OpenPlugs.Remove(a.Dock.PlugId);
                b.Room.OpenPlugs.Remove(b.Dock.PlugId);
            }
            ((List<(RaidPlan.End A, RaidPlan.End B)>)plan.Portals).Clear();
            foreach (var room in plan.Rooms)
            for (int i = 0; i < room.Module.Docks.Count; i++)
            {
                var dock = room.Module.Docks[i];
                if (room == hall && dock.Id == ceiling.Id || room.OpenPlugs.Contains(dock.PlugId)) continue;
                room.Module.Docks[i] = new Dock(dock.Id, dock.FloorId, dock.Threshold,
                    dock.Use & ~DockUse.DoorPortal, dock.Opening, dock.MapPoint, dock.MaxCargo, dock.PlugId);
            }
            typeof(RaidPlan).GetMethod("SelectPortals", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(plan, null);
            Require(plan.Portals.Count == 0 && !hall.OpenPlugs.Contains(ceiling.PlugId) &&
                plan.PortalPairCount <= RaidPlan.MaximumPortalPairs,
                "production selector opened ceiling without a compatible available partner.");
            Require(bundle.Prefabs.TryGetPrepared(hall, out var exactPrefab), "missing exact hall prefab for absence fixture.");
            var fixtureRoot = new GameObject("Exact ceiling unavailable-partner fixture");
            try
            {
                var raid = fixtureRoot.AddComponent<RaidAssembly>();
                var placed = raid.PlacePrepared(hall, 0, exactPrefab);
                Physics.SyncTransforms();
                var end = new RaidPlan.End(hall, ceiling);
                var plug = placed.CollidersById[ceiling.PlugId];
                Vector3 center = end.Sill + end.Up * ceiling.Opening.Y * .5f;
                Require(plug.enabled && plug.gameObject.activeInHierarchy &&
                    plug.Raycast(new Ray(center - end.Outward, end.Outward), out _, 2f),
                    "production prepared placement lost the exact unavailable ceiling collision cap.");
            }
            finally { Object.DestroyImmediate(fixtureRoot); }
            Debug.Log($"{Log} MOBIUS OUTLET WITNESSES PASSED: natural paired 5,-32; constrained unavailable-partner selector and exact installed collision cap; no budget override.");
        }
        private static void VerifySphereFixture(RaidRoomPrefabs prefabs, HashSet<string> expected, int mask)
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            Type type = typeof(RaidRoomPrefabs);
            var prepared = (IReadOnlyDictionary<string, RaidRoomPrefabs.PreparedModule>)type.GetField("prepared", fields).GetValue(prefabs);
            var withResearch = (GameObject)type.GetField("withResearch", fields).GetValue(prefabs);
            var withoutResearch = (GameObject)type.GetField("withoutResearch", fields).GetValue(prefabs);
            string id = SphericalChamber.VariantId(mask);
            string sealedId = SphericalChamber.VariantId(mask & ~SphericalChamber.EastMask);
            var selected = prepared.Where(entry => !SphericalChamber.IsVariant(entry.Key) || entry.Key == id || entry.Key == sealedId)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
            var restricted = new RaidRoomPrefabs(withResearch, withoutResearch, selected);
            foreach (var (progress, seed) in new[] { (5, -31), (5, 12), (5, 17), (14, -16) })
            {
                var plan = RaidPlan.Generate(seed, Entrance, progress, maintenance: MaintenanceTrench.Create,
                    canPrepare: room => restricted.TryGetPrepared(room, out var prefab) && prefab);
                if (!plan.Rooms.Any(room => room.Module.Id == id)) continue;
                Debug.Log($"{Log} SPHERE FIXTURE restricted prefab set|{progress},{seed}|{id}");
                Verify(restricted, expected, new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<string>(StringComparer.Ordinal), progress, seed, fixture: id);
                return;
            }
            throw new InvalidOperationException($"{Log}: test fixture did not place the requested sphere {id}.");
        }

        private static void VerifySpherePrefab(GameObject instance, int mask)
        {
            SphericalChamberChecks.Verify(instance, mask);
            var root = instance.transform;
            var assets = root.Find("Loot candidates");
            var lamps = root.Find("Warm shell practicals");
            Require(lamps && assets, $"sphere {mask} is missing its loot markers or practical lights.");
            foreach (string side in new[] { "west", "east" })
            {
                var marker = assets.Find($"loot__{side}-shell-spares");
                var fixture = lamps.Find($"Loot lamp {side}-shell-spares");
                var light = fixture ? fixture.GetComponent<Light>() : null;
                Require(marker && light && light.isActiveAndEnabled &&
                    Vector3.Distance(marker.position, fixture.position) < 1.4f &&
                    Mathf.Abs(light.range - 10f) < .002f && Mathf.Abs(light.intensity - 2f) < .002f,
                    $"sphere {mask}/{side} lost its marked shell loot station or its .29 lighting.");
            }
            Require(assets.Find("loot__west-bay-spares") &&
                (bool)assets.Find("loot__east-bay-spares") == ((mask & SphericalChamber.EastMask) != 0) &&
                (bool)assets.Find("loot__upper-path-spares") == ((mask & SphericalChamber.UpperMask) != 0) &&
                (bool)assets.Find("loot__lower-path-spares") == ((mask & SphericalChamber.LowerMask) != 0),
                $"sphere {mask} loot markers differ from its surviving deck sections.");
            Debug.Log($"{Log} SPHERE PREFAB {mask}|{instance.name}|{root.Find("Docks").childCount} docks|{assets.childCount} loot sites");
        }

        private static void VerifyAuthoredGeometry(RaidRoomPrefabs prefabs, GrayModule module, Action<GameObject> verify)
        {
            var identity = new Pose(Vector3.zero, Quaternion.identity);
            var plan = new RaidPlan.Room(module, identity, identity, 0);
            Require(prefabs.TryGetPrepared(plan, out var prefab), $"{module.Id} not loaded from the prepared bundle.");
            var instance = Object.Instantiate(prefab, identity.position, identity.rotation);
            try
            {
                Physics.SyncTransforms();
                verify(instance);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        private static void Verify(RaidRoomPrefabs prefabs, HashSet<string> expected, HashSet<string> seen,
            HashSet<string> eastPortals, int progress, int seed, string fixture = null)
        {
            var root = new GameObject("Packed Contour-4 raid fixture");
            var raid = root.AddComponent<RaidAssembly>();
            try
            {
                raid.Place(seed, Entrance, 0, 0, catalogSites: true, completedLevels: progress, roomPrefabs: prefabs);
                VerifyTopology(raid.Plan, prefabs);
                VerifyPhysicalNetwork(raid.Plan);
                var rooms = raid.Rooms.Where(room => room.Prepared).ToArray();
                Require(raid.Rooms.All(room => room.Prepared == expected.Contains(room.Plan.Module.Id)),
                    "prepared selection differs from the generated module IDs.");
                Require(raid.Rooms.Where(room => room.Plan.Module.Id == FreightServiceRoom.SampleReceiving).All(room => room.Authored),
                    "sample receiving did not use its original bundle variant.");
                var candidates = RaidLootSites.Find(raid);
                bool newNavigation = rooms.Any(room => !seen.Contains(room.Plan.Module.Id) || IsComposition(room.Plan.Module.Id));
                if (newNavigation)
                {
                    raid.InstallNavigation();
                    VerifyHandPassages(raid);
                }
                int authoredCount = 0;
                foreach (var room in rooms)
                {
                    string id = room.Plan.Module.Id;
                    bool first = seen.Add(id);
                    Require(prefabs.TryGetPrepared(room.Plan, out var prefab) && prefab.name == id &&
                        room.Root.Find("Monster paths") && room.Root.Find("Monster paths").childCount > 0,
                        $"{id} missing its exact prefab or authored monster paths.");
                    Require(room.Root.GetComponentsInChildren<MeshRenderer>(true).All(renderer =>
                        renderer.sharedMaterial && renderer.sharedMaterial.shader.name != "Hidden/InternalErrorShader"),
                        $"{id} missing an authored material.");
                    foreach (var dock in room.Plan.Module.Docks)
                    {
                        var marker = room.Root.Find("Docks/" + dock.Id);
                        var a = marker.Find("Contact start").position;
                        var b = marker.Find("Contact end").position;
                        float height = Vector3.Distance(marker.position, marker.Find("Opening top").position);
                        Require(Vector3.Distance(marker.position, room.Plan.Point(dock.Threshold.Position)) < .002f &&
                            Vector3.Dot(marker.forward, room.Plan.Direction(dock.Threshold.Forward)) > .9999f &&
                            Vector3.Dot(marker.up, room.Plan.Direction(dock.Threshold.Up)) > .9999f &&
                            Mathf.Abs(Vector3.Distance(a, b) - dock.Opening.X) < .002f &&
                            Mathf.Abs(height - dock.Opening.Y) < .002f,
                            $"{id}/{dock.Id} prefab threshold differs from the assembled pose or dimensions.");
                    }
                    foreach (var cap in room.Plan.Module.Geometry.Where(block => block.Role == BlockRole.Plug))
                        Require(room.CollidersById.TryGetValue(cap.Id, out var collider) &&
                            collider.gameObject.activeInHierarchy == !room.Plan.OpenPlugs.Contains(cap.Id),
                            $"{id}/{cap.Id} collision cap differs from the assembled opening.");
                    var authored = prefabs.PreparedLoot(room);
                    var usable = candidates.Where(slot => slot.Room == room).ToArray();
                    Require(authored.Count == room.Root.Find("Loot candidates").childCount &&
                        (authored.Count == 0 ? usable.Length == 0 : usable.Length > 0),
                        $"{id} lost the prepared loot pool.");
                    foreach (var slot in usable)
                        VerifyLoot(raid, slot, authored);
                    authoredCount += authored.Count;
                    if (IsComposition(id) && VerifyCompositionDocks(raid, room)) eastPortals.Add(id);
                    if (SphericalChamber.IsVariant(id)) VerifySphereGravity(raid, room);
                    if (id == MobiusHall.Id)
                    {
                        VerifyMobiusGravity(raid, room);
                        VerifyMobiusCeilingOutlet(raid, room);
                    }
                    if (first) VerifyNavigation(raid, room);
                    Debug.Log($"{Log} ROOM {progress},{seed}|{id}|{room.Floors.Count()} floors|{room.Plan.Module.Docks.Count} docks|" +
                        $"{authored.Count} authored loot|{usable.Length} eligible loot");
                }

                RaidCatalogSites.Validate(raid);
                VerifyBudget(candidates, seed);
                VerifyMap(raid, rooms, progress, seed, fixture);
                Debug.Log($"{Log} RAID {progress},{seed}|{raid.Plan.Rooms.Count} rooms|{rooms.Length} prepared|" +
                    $"{authoredCount} authored loot|{candidates.Count} native candidate volumes");
            }
            finally
            {
                raid.ReleaseNavigation();
                Object.DestroyImmediate(root);
            }
        }

        private static void VerifyTopology(RaidPlan plan, RaidRoomPrefabs prefabs)
        {
            SeededRaidChecks.CheckCartAccess(plan);
            if ((plan.CompletedLevels, plan.Seed) is not ((14, -16) or (5, -32))) return;
            // Assembly opens the caps assigned to internal portal pairs. Compare two
            // generator outputs before the prepared prefab applies those openings.
            var generated = Generate();
            var repeated = Generate();
            Require(SeededRaidChecks.Signature(generated) == SeededRaidChecks.Signature(repeated),
                $"same seed and exact prefab set must reproduce the complete plan ({plan.CompletedLevels},{plan.Seed}).");
            Require(TabPoses(plan).SequenceEqual(TabPoses(repeated)),
                $"same seed and exact prefab set must reproduce every Tab floor pose ({plan.CompletedLevels},{plan.Seed}).");
            if (plan.Seed == -32) SeededRaidChecks.CheckPortalSignature(generated);

            RaidPlan Generate() => RaidPlan.Generate(plan.Seed, Entrance, plan.CompletedLevels,
                maintenance: MaintenanceTrench.Create, canPrepare: CanPrepare);

            bool CanPrepare(RaidPlan.Room room) => prefabs.TryGetPrepared(room, out var prefab) && prefab;

            IEnumerable<(string Floor, int X, int Y, int Angle)> TabPoses(RaidPlan candidate) =>
                RaidMapPlan.Create(candidate, preparedRooms: candidate.Rooms.Where(CanPrepare)).Floors.Select(floor =>
                    (floor.Floor.Id, BitConverter.SingleToInt32Bits(floor.Position.x),
                        BitConverter.SingleToInt32Bits(floor.Position.y), BitConverter.SingleToInt32Bits(floor.Angle)));
        }

        private static void VerifyPhysicalNetwork(RaidPlan plan)
        {
            var physical = plan.Freight.Select(link => (From: link.From.Room, To: link.To.Room))
                .Concat(plan.HandPassages.Select(link => (From: link.From.Room, To: link.To.Room)))
                .SelectMany(link => new[] { link, (From: link.To, To: link.From) })
                .ToLookup(link => link.From, link => link.To);
            var reachable = new HashSet<RaidPlan.Room> { plan.Entrance.Room };
            var pending = new Queue<RaidPlan.Room>(reachable);
            while (pending.TryDequeue(out var current))
                foreach (var neighbor in physical[current])
                    if (reachable.Add(neighbor)) pending.Enqueue(neighbor);
            Require(plan.Rooms.All(reachable.Contains), "rooms with no physical access from the truck: " +
                string.Join(", ", plan.Rooms.Where(room => !reachable.Contains(room)).Select(room => room.Module.Id)));
            Require(plan.Portals.All(pair => reachable.Contains(pair.A.Room) && reachable.Contains(pair.B.Room)),
                "portal endpoints must remain reachable via physical links.");
            Debug.Log($"{Log} PHYSICAL NETWORK {plan.CompletedLevels},{plan.Seed}|{reachable.Count}/{plan.Rooms.Count} rooms reachable from the truck");
        }

        private static bool IsComposition(string id) => SphericalChamber.IsVariant(id) || id == MobiusHall.Id;

        private static bool VerifyCompositionDocks(RaidAssembly raid, BakedRoom room)
        {
            string id = room.Plan.Module.Id;
            var west = new RaidPlan.End(room.Plan, room.Plan.Dock("west"));
            Require(west.Dock.MaxCargo == Cargo.Hand &&
                raid.Plan.HandPassages.Count(pass => pass.To.Room == room.Plan && pass.To.Dock == west.Dock) == 1 &&
                raid.Plan.SamePlaneNeighbors(west) == 1, $"{id} must use exactly one west hand passage.");

            if (SphericalChamber.IsVariant(id))
            {
                var planned = raid.Plan.Passages.SelectMany(pass => new[] { pass.From, pass.To })
                    .Concat(raid.Plan.Portals.SelectMany(pair => new[] { pair.A, pair.B }))
                    .Where(end => end.Room == room.Plan).Select(end => end.Dock.Id).ToArray();
                var authored = room.Plan.Module.ExternalDocks.Select(dock => dock.Id).ToArray();
                Require(planned.Length == authored.Length && new HashSet<string>(planned, StringComparer.Ordinal).SetEquals(authored) &&
                    room.Root.Find("Docks").childCount == planned.Length &&
                    room.Plan.Module.ExternalDocks.All(dock => room.Plan.OpenPlugs.Contains(dock.PlugId)),
                    $"{id} prefab sockets differ from its generated physical passages and portal endpoints.");
                Debug.Log($"{Log} SPHERE DOCKS {raid.Plan.CompletedLevels},{raid.Plan.Seed}|{id}|{string.Join(",", planned)}");
                if (!authored.Contains("east")) return false;
            }

            var east = new RaidPlan.End(room.Plan, room.Plan.Dock("east"));
            Require(east.Dock.MaxCargo == Cargo.Hand && raid.Plan.SamePlaneNeighbors(east) == 1,
                $"{id}/east must preserve its hand-only threshold and coplanar-neighbor count.");

            RaidFloor floor = room.DockFloor(east.Dock);
            var portals = raid.Context.Portals.Where(portal => portal.EntryFloor == floor || portal.ExitFloor == floor).ToArray();
            if (portals.Length == 0 && id == MobiusHall.Id)
            {
                Require(!room.Plan.OpenPlugs.Contains(east.Dock.PlugId) && room.CollidersById[east.Dock.PlugId].gameObject.activeInHierarchy,
                    $"{id}/east must retain its collision plug when no matching portal fits.");
                Debug.Log($"{Log} COMPOSITION DOCKS {raid.Plan.CompletedLevels},{raid.Plan.Seed}|{id}|west hand sill, sealed east");
                return false;
            }
            Require(portals.Length == 2 && portals[0].Entrance == portals[1].Exit && portals[0].Exit == portals[1].Entrance,
                $"{id}/east must have a reversible portal pair.");
            foreach (var portal in portals)
            {
                Require(portal.Door && !portal.AllowsCart && Mathf.Abs(portal.HalfWidth * 2 - east.Dock.Opening.X) < .002f &&
                    Mathf.Abs(portal.HalfHeight * 2 - east.Dock.Opening.Y) < .002f &&
                    Mathf.Abs(Vector3.Dot(portal.EntryFloor.Up, portal.ExitFloor.Up)) < .001f &&
                    Vector3.Dot(portal.TravelRotation * portal.EntryFloor.Up, portal.ExitFloor.Up) > .999f,
                    $"{id}/east must use the catalog opening for its orthogonal hand-only portal.");
                Vector3 sensor = portal.Entrance.position - portal.Entrance.up * portal.HalfHeight -
                    portal.Entrance.forward * portal.DoorInset;
                Vector3 before = sensor - portal.Entrance.forward * .08f;
                Vector3 after = sensor + portal.Entrance.forward * .08f;
                Require(portal.Crossed(before, after, Reference.Radius, Reference.Height) &&
                    raid.TryPassPlayer(portal, after, out _, before) && raid.HasPortalSupport(portal.ExitFloor, portal.ExitPad),
                    $"{id}/east, {portal.Id}: crossing or destination support failed.");
            }
            Debug.Log($"{Log} COMPOSITION DOCKS {raid.Plan.CompletedLevels},{raid.Plan.Seed}|{id}|west hand sill, two orthogonal east portal directions");
            return true;
        }

        private static void VerifyMobiusGravity(RaidAssembly raid, BakedRoom room)
        {
            var root = raid.transform;
            var original = new Pose(root.position, root.rotation);
            Collider ribbon = room.CollidersById[MobiusHall.Ribbon];
            float[] samples = { .125f, .375f, .625f, .875f, 1.125f, 1.375f, 1.625f, 1.875f };
            try
            {
                foreach (float turns in samples)
                {
                    root.SetPositionAndRotation(original.position, original.rotation);
                    var frame = MobiusHall.Surface(turns, .65f);
                    var contact = RaidPlan.Vector(frame.Position);
                    var inward = RaidPlan.Vector(frame.Up);
                    var pivot = room.Root.TransformPoint(contact);
                    Quaternion turn = Quaternion.FromToRotation(room.Root.TransformDirection(inward), Vector3.up);
                    root.SetPositionAndRotation(pivot + turn * (root.position - pivot), turn * root.rotation);
                    Physics.SyncTransforms();
                    Vector3 feet = room.Root.TransformPoint(contact + inward * .025f);
                    Require(raid.MobiusRoomAt(feet) == room, "Placed Möbius ribbon feet lost their prepared room.");
                    Require(ribbon.Raycast(new Ray(feet + Vector3.up * .15f, Vector3.down), out var hit, .34f),
                        $"Möbius ribbon section {turns:F3} lost its packed collision surface.");
                    var walk = new MobiusWalk(room.Root, ribbon);
                    walk.Observe(feet, hit.point, hit.normal);
                    Vector3 up = walk.Step(feet, Vector3.zero, ribbon, out var change);
                    Require(walk.Attached && change == MobiusWalk.Change.Landing && Vector3.Dot(up, Vector3.up) > .997f,
                        $"Möbius ribbon section {turns:F3} selected the wrong local up on contact.");
                    walk.Jump();
                    up = walk.Step(feet + Vector3.up * .5f, Vector3.up, null, out change);
                    Vector3 roomUp = room.Root.TransformDirection(RaidPlan.Vector(MobiusHall.RoomUpAt(frame.Position + frame.Up * .525f)));
                    Require(!walk.Attached && change == MobiusWalk.Change.Release && Vector3.Dot(up, roomUp) > .999f,
                        $"Möbius ribbon section {turns:F3} did not return to room gravity on jump.");
                }
            }
            finally
            {
                root.SetPositionAndRotation(original.position, original.rotation);
                Physics.SyncTransforms();
            }
            Debug.Log($"{Log} FIELD {raid.Plan.CompletedLevels},{raid.Plan.Seed}|{MobiusHall.Id}|{samples.Length} packed ribbon contacts, attachment and jump release");
        }

        private static void VerifyMobiusCeilingOutlet(RaidAssembly raid, BakedRoom room)
        {
            var lowerLanding = room.CollidersById[MobiusHall.Landing];
            var upperLanding = room.CollidersById[MobiusHall.CeilingLanding];
            Vector3 lowerCenter = room.Root.InverseTransformPoint(lowerLanding.bounds.center);
            Vector3 upperCenter = room.Root.InverseTransformPoint(upperLanding.bounds.center);
            Require(lowerLanding != upperLanding && lowerCenter.z * upperCenter.z < 0 &&
                Mathf.Abs(lowerCenter.z - upperCenter.z) > MobiusHall.HalfInteriorZ,
                "Floor and ceiling access must use separate landings on opposite spatial sides of the exact hall.");
            var end = new RaidPlan.End(room.Plan, room.Plan.Dock("ceiling-east"));
            var floor = room.DockFloor(end.Dock);
            Require(end.Dock.FloorId == "ceiling" && end.Dock.Use == DockUse.DoorPortal &&
                end.Dock.MaxCargo == Cargo.Hand && Vector3.Dot(end.Up, -room.Root.up) > .9999f,
                "Möbius ceiling outlet must use its separate inverted hand-only ceiling frame.");
            Require(float.IsFinite(raid.Plan.WalkingDistance(raid.Plan.Arrival(room.Plan), end)) &&
                raid.Plan.PortalPairCount <= RaidPlan.MaximumPortalPairs,
                "Ceiling outlet must have an authored physical route and obey the shared portal budget.");
            Vector3 center = end.Sill + end.Up * end.Dock.Opening.Y * .5f;
            var directions = raid.Context.Portals.Where(p =>
                (p.EntryFloor == floor && Vector3.Distance(p.Entrance.position, center) < .002f) ||
                (p.ExitFloor == floor && Vector3.Distance(p.Exit.position, center) < .002f)).ToArray();
            var plug = room.CollidersById[end.Dock.PlugId];
            bool selected = room.Plan.OpenPlugs.Contains(end.Dock.PlugId);
            if (!selected)
            {
                Require(directions.Length == 0 && plug.enabled && plug.gameObject.activeInHierarchy &&
                    plug.Raycast(new Ray(center - end.Outward, end.Outward), out _, 2f),
                    "Unpaired ceiling outlet lost its physical collision seal.");
                Debug.Log($"{Log} MOBIUS CEILING OUTLET {raid.Plan.CompletedLevels},{raid.Plan.Seed}|sealed, collision ray confirmed, shared budget {raid.Plan.PortalPairCount}");
                return;
            }
            Require(!plug.gameObject.activeInHierarchy && directions.Length == 2 &&
                directions[0].Entrance == directions[1].Exit && directions[0].Exit == directions[1].Entrance,
                "Selected ceiling outlet must remove only its plug and bind a reversible pair.");
            foreach (var portal in directions)
            {
                Require(portal.Door && !portal.AllowsCart &&
                    Mathf.Abs(portal.HalfWidth * 2 - end.Dock.Opening.X) < .002f &&
                    Mathf.Abs(portal.HalfHeight * 2 - end.Dock.Opening.Y) < .002f &&
                    Vector3.Dot(portal.TravelRotation * portal.EntryFloor.Up, portal.ExitFloor.Up) > .999f,
                    "Ceiling outlet aperture or travel orientation differs from its two physical floors.");
                Require(raid.HasPortalSupport(portal.EntryFloor, portal.EntryPad) &&
                    raid.HasPortalSupport(portal.ExitFloor, portal.ExitPad),
                    "Ceiling outlet lost physical support on an approach.");
                Vector3 sensor = portal.Entrance.position - portal.Entrance.up * portal.HalfHeight -
                    portal.Entrance.forward * portal.DoorInset;
                Vector3 before = sensor - portal.Entrance.forward * .08f;
                Vector3 after = sensor + portal.Entrance.forward * .08f;
                Require(portal.Crossed(before, after, Reference.Radius, Reference.Height) &&
                    raid.TryPassPlayer(portal, after, out _, before),
                    "Ceiling outlet crossing failed in one direction.");
                Require(raid.TryPlayerUp(portal.EntryPad, portal.EntryFloor.Up, out var entryUp) &&
                    raid.TryPlayerUp(portal.ExitPad, portal.ExitFloor.Up, out var exitUp) &&
                    Vector3.Dot(entryUp, portal.EntryFloor.Up) > .999f &&
                    Vector3.Dot(exitUp, portal.ExitFloor.Up) > .999f,
                    "Ceiling outlet approaches disagree with the actual runtime gravity field.");
            }
            Debug.Log($"{Log} MOBIUS CEILING OUTLET {raid.Plan.CompletedLevels},{raid.Plan.Seed}|paired, reciprocal frames, supported approaches, gravity and crossings both ways, shared budget {raid.Plan.PortalPairCount}");
        }

        private static void VerifySphereGravity(RaidAssembly raid, BakedRoom room)
        {
            int mask = SphericalChamber.GetMask(room.Plan.Module.Id);
            int probes = 0;
            foreach (int side in new[] { -1, 1 })
            {
                if (side < 0 || (mask & SphericalChamber.EastMask) != 0)
                    Check(new Vector3(side * (SphericalChamber.HalfReservation - .1f), 0, 0), Vector3.up, "flat sill");
                bool attached = (mask & (side > 0 ? SphericalChamber.UpperMask : SphericalChamber.LowerMask)) != 0;
                for (int phase = 0; phase < 3; phase++)
                {
                    var track = SphericalChamber.BranchFrame(side, phase, .5f, 0);
                    var contact = RaidPlan.Vector(track.Position);
                    var up = RaidPlan.Vector(track.Up);
                    if (attached) Check(contact, up, $"branch {side}, section {phase}");
                    else if (phase < 2)
                    {
                        var point = contact + up * .12f;
                        Require(raid.TryPlayerUp(room.Root.TransformPoint(point), room.Root.TransformDirection(up), out var actual) &&
                            Vector3.Dot(actual, room.Root.TransformDirection(-point.normalized)) > .991f,
                            $"sphere {mask} applies phantom branch gravity at {side}/{phase}.");
                        probes++;
                    }
                }
                float y = side * Mathf.Sqrt(SphericalChamber.Radius * SphericalChamber.Radius - 16);
                var shell = new Vector3(4, y, 0);
                Check(shell, -shell.normalized, $"shell {side}");
            }

            Debug.Log($"{Log} FIELD {raid.Plan.CompletedLevels},{raid.Plan.Seed}|{room.Plan.Module.Id}|{probes} transformed player gravity probes");

            void Check(Vector3 contact, Vector3 up, string point)
            {
                Vector3 worldUp = room.Root.TransformDirection(up);
                Vector3 worldFeet = room.Root.TransformPoint(contact + up * .12f);
                Require(raid.TryPlayerUp(worldFeet, worldUp, out var actual) && Vector3.Dot(worldUp, actual) > .991f,
                    $"{room.Plan.Module.Id}/{point} does not match the assembled player gravity frame.");
                probes++;
            }
        }

        private static void VerifyHandPassages(RaidAssembly raid)
        {
            foreach (var (from, to) in raid.Plan.HandPassages)
            {
                var fromFloor = raid.Room(from.Room).DockFloor(from.Dock);
                var toFloor = raid.Room(to.Room).DockFloor(to.Dock);
                string passage = $"{from.Room.Module.Id}/{from.Dock.Id} to {to.Room.Module.Id}/{to.Dock.Id}";
                Require(CanWalk(fromFloor, from, toFloor, to, Reference) && CanWalk(toFloor, to, fromFloor, from, Reference),
                    $"{passage} must admit the reference profile in both directions.");
                bool narrow = Math.Min(from.Dock.Opening.X, to.Dock.Opening.X) < 2 * Headman.Radius ||
                    Math.Min(from.Dock.Opening.Y, to.Dock.Opening.Y) < Headman.Height;
                if (narrow)
                    Require(!CanWalk(fromFloor, from, toFloor, to, Headman) && !CanWalk(toFloor, to, fromFloor, from, Headman),
                        $"{passage} must exclude the oversized profile in both directions.");
                Debug.Log($"{Log} HAND SILL {raid.Plan.CompletedLevels},{raid.Plan.Seed}: {passage}, reversible for reference" +
                    (narrow ? ", blocked for Headman geometry." : "."));
            }

            bool CanWalk(RaidFloor fromFloor, RaidPlan.End from, RaidFloor toFloor, RaidPlan.End to, RaidEnemyProfile profile) =>
                raid.Context.Navigation.TryGetRouteKind(fromFloor, from.Pad(1.4f), toFloor, to.Pad(1.4f), profile, out var kind) &&
                kind == RaidRouteKind.PhysicalPassage;
        }

        private static void VerifyNavigation(RaidAssembly raid, BakedRoom room)
        {
            int small = Paths("Reference geometry (r0.35 h1.80)", Reference);
            int large = Paths("Headman body geometry (r0.86 h2.86)", Headman);
            Require(small > 0 && large > 0,
                $"{room.Plan.Module.Id}: complete local paths, reference={small}, Headman={large}.");
            Debug.Log($"{Log} NAV {room.Plan.Module.Id}|{small} reference|{large} Headman geometry");

            int Paths(string name, RaidEnemyProfile profile) =>
                room.Root.Find("Monster paths/" + name).GetComponentsInChildren<Transform>()
                    .Where(track => track.name.StartsWith("Path ", StringComparison.Ordinal))
                    .Count(track => room.Floors.Any(floor =>
                        Vector3.Dot(floor.Up, track.GetChild(0).up) > .999f &&
                        raid.Context.Navigation.TryGetRouteKind(floor, track.GetChild(0).position, floor,
                            track.GetChild(track.childCount - 1).position, profile, out var kind) && kind == RaidRouteKind.Local));
        }

        private static void VerifyLoot(RaidAssembly raid, RaidLootSlot slot, IReadOnlyList<RaidLootSlot> authored)
        {
            string id = slot.Room.Plan.Module.Id;
            Require(slot.FloorId != null && slot.Room.TryFloor(slot.FloorId, out _) &&
                authored.Any(site => site.Pose.position == slot.Pose.position && site.Size == slot.Size &&
                    site.Kind == slot.Kind && site.Support == slot.Support), $"{id} registered a synthetic loot candidate.");
            Require(slot.Size <= RaidLootSize.Medium || raid.Plan.HasCartAccess(slot.Room.Plan, slot.FloorId),
                $"{id}/{slot.FloorId} has large loot without cart access.");
            Vector3 extent = RaidLootSites.Envelope(slot.Size);
            Vector3 bottom = slot.Room.Root.TransformPoint(slot.Pose.position +
                slot.Pose.rotation * new Vector3(0f, 0f, extent.z / 2f));
            Vector3 up = slot.Room.Root.TransformDirection(slot.Pose.rotation * Vector3.up);
            Require(slot.Support.Raycast(new Ray(bottom + up * .055f, -up), out var hit, .12f) &&
                Vector3.Dot(hit.normal, up) > .8f, $"{id}/{slot.Support.name} failed to support its exact loot pivot.");
        }

        private static void VerifyBudget(IReadOnlyList<RaidLootSlot> slots, int seed)
        {
            int[] caps = { 8, 7, 6, 5, 4, 3, 2 };
            int[] stockPrices = { 1000, 2500, 4500, 7500, 8500, 9500, 11000 };
            var candidates = slots.Select(slot => new RaidLootBudget.Candidate((int)slot.Size, slot.Room.Plan.Index, slot.Kind)).ToArray();
            const int countCap = 25, cashCap = 90000;
            var allocation = RaidLootBudget.Select(candidates, caps, countCap, cashCap, stockPrices, seed, preparedCatalog: true);
            Require(allocation.Count > 0 && allocation.Count <= countCap && allocation.Dollars <= cashCap &&
                allocation.Picks.Count == allocation.Count && allocation.Picks.Distinct().Count() == allocation.Count &&
                allocation.ContinueUntil(1, 1) == 1 &&
                allocation.Picks.Select(index => candidates[index]).GroupBy(candidate => candidate.Category)
                    .All(category => category.Count() <= caps[category.Key]), "prepared loot exceeds native capacity or quota.");
        }

        private static void VerifyMap(RaidAssembly raid, BakedRoom[] rooms, int progress, int seed, string fixture)
        {
            var map = RaidMapPlan.Create(raid.Plan, preparedRooms: rooms.Select(room => room.Plan));
            int internalPairs = rooms.Sum(room => room.Plan.Module.InternalPortals.Count);
            var pairs = map.Doors.Where(door => door.Portal).GroupBy(door => door.Color).ToArray();
            Require(map.Floors.Count == raid.Plan.Rooms.Sum(room => room.Module.Floors.Count) + 1 &&
                pairs.Length == raid.Plan.Portals.Count + internalPairs && pairs.All(pair => pair.Count() == 2) &&
                pairs.SelectMany(pair => pair).All(HasLocalStroke) &&
                map.Links.All(link => link.Path.Length > 0), "Tab floor, physical link or portal-pair count differs from the raid.");

            static bool HasLocalStroke(RaidMapPlan.Door door)
            {
                var (from, to) = door.PortalStroke();
                var end = door.End;
                return Mathf.Abs(Vector2.Distance(from, to) - end.Width) < .001f &&
                    Vector2.Distance((from + to) / 2f, end.Floor.Flat(end.Edge)) < .001f;
            }
            foreach (var room in rooms)
            {
                var active = raid.Plan.Arrival(room.Plan);
                var floor = room.Plan.Module.Floors.Single(entry => entry.Id == active.Dock.FloorId);
                Require(map.Project(active.Pad(1.7f), -active.Outward, true, out var shown, out var position, out var heading) &&
                    ReferenceEquals(shown.Floor, floor) && shown.Revealed && heading.sqrMagnitude > .001f &&
                    Mathf.Abs(map.NativePoint(position).y - map.Stock.Room.position.y) < .001f,
                    $"{room.Plan.Module.Id}/{active.Dock.Id} does not reveal its arriving player on the stock-plane map.");
                if (SphericalChamber.IsVariant(room.Plan.Module.Id))
                {
                    int mask = SphericalChamber.GetMask(room.Plan.Module.Id);
                    int branches = 0;
                    foreach (int side in new[] { -1, 1 })
                    {
                        float y = side * Mathf.Sqrt(SphericalChamber.Radius * SphericalChamber.Radius - 16);
                        var shell = new Vector3(4, y, 0);
                        CheckMapPoint(shell * .99f);
                        if ((mask & (side > 0 ? SphericalChamber.UpperMask : SphericalChamber.LowerMask)) != 0)
                        {
                            var branch = SphericalChamber.BranchFrame(side, 0, .5f, 0);
                            CheckMapPoint(RaidPlan.Vector(branch.Position + branch.Up * .12f));
                            branches++;
                        }
                    }
                    Require(shown.SurfaceRoutes.Length == branches && shown.SurfaceRoutes.All(route => route.Length >= 2),
                        $"{room.Plan.Module.Id} Tab overlay contains a phantom or missing bridge-to-shell branch.");
                    Debug.Log($"{Log} FIELD TAB {progress},{seed}|{room.Plan.Module.Id}|{branches} branch overlays plus shell marker");

                    void CheckMapPoint(Vector3 local)
                    {
                        Require(map.Project(room.Root.TransformPoint(local), room.Root.right, true,
                            out var projected, out _, out var direction) && projected == shown && direction.sqrMagnitude > .001f,
                            "Spherical chamber Tab marker lost its room or direction on the shell or branch.");
                    }
                }
                if (room.Plan.Module.Id == MobiusHall.Id)
                {
                    foreach (float turns in new[] { 0f, .5f, 1f, 1.5f })
                    {
                        var local = MobiusHall.Surface(turns, 0);
                        CheckMobiusLayer(RaidPlan.Vector(local.Position),
                            local.Position.Y >= MobiusHall.InteriorHeight / 2 ? "ceiling" : "ground");
                    }
                    foreach (float y in new[] { .1f, MobiusHall.InteriorHeight / 2 - .01f,
                        MobiusHall.InteriorHeight / 2 + .01f, MobiusHall.InteriorHeight - .1f })
                        CheckMobiusLayer(new Vector3(0, y, 0), y >= MobiusHall.InteriorHeight / 2 ? "ceiling" : "ground");
                    var ceilingDock = room.Plan.Dock("ceiling-east");
                    CheckMobiusLayer(RaidPlan.Vector(ceilingDock.Threshold.Position - ceilingDock.Threshold.Forward * 1.7f), "ceiling");
                    if (room.Plan.OpenPlugs.Contains(ceilingDock.PlugId))
                        Require(ReferenceEquals(Find("ceiling-east").End.Floor.Floor,
                            room.Plan.Module.Floors.Single(f => f.Id == "ceiling")),
                            "Ceiling portal glyph was assigned to the ground Tab layer.");
                    Debug.Log($"{Log} FIELD TAB {progress},{seed}|{MobiusHall.Id}|ribbon, split boundary, both floor layers and ceiling outlet glyph");

                    void CheckMobiusLayer(Vector3 local, string expectedLayer)
                    {
                        var expectedFloor = room.Plan.Module.Floors.Single(f => f.Id == expectedLayer);
                        Require(map.Project(room.Root.TransformPoint(local), room.Root.right, true,
                            out var projected, out _, out var direction) && ReferenceEquals(projected.Floor, expectedFloor) &&
                            direction.sqrMagnitude > .001f,
                            $"Möbius Tab point {local} lost its {expectedLayer} layer or direction.");
                    }
                }
                foreach (var (a, b) in room.Plan.Module.InternalPortals)
                {
                    var endpointA = Find(a);
                    var endpointB = Find(b);
                    Require(endpointA.Color.Equals(endpointB.Color), "internal portals lost their pair color.");
                }

                RaidMapPlan.Door Find(string id)
                {
                    var dock = room.Plan.Module.Docks.Single(entry => entry.Id == id);
                    var dockFloor = room.Plan.Module.Floors.Single(entry => entry.Id == dock.FloorId);
                    var point = new Vector2(dock.MapPoint.X, dock.MapPoint.Y);
                    bool interior = (dock.Use & (DockUse.UpperPortal | DockUse.LowerPortal)) != 0;
                    return map.Doors.Single(door => door.Portal && ReferenceEquals(door.End.Floor.Floor, dockFloor) &&
                        door.End.Interior == interior && Vector2.Distance(door.End.Point, point) < .001f &&
                        (!interior || Vector2.Distance(door.End.Edge, point) < .001f));
                }
            }
            string directory = Environment.GetEnvironmentVariable("CONTOUR4_PREPARED_EVIDENCE");
            if (!string.IsNullOrEmpty(directory) && (internalPairs > 0 || rooms.Any(room => IsComposition(room.Plan.Module.Id))))
            {
                var entrance = raid.Plan.Entrance;
                map.Project(entrance.Pad(1.7f), -entrance.Outward, true, out _, out var point, out var heading);
                string name = fixture == null ? $"tab-{progress}-{seed}" : $"tab-fixture-{fixture}-{progress}-{seed}";
                ReceivingPrefabChecks.DrawMap(map, point, heading, Path.Combine(directory, name + ".png"));
            }
            Debug.Log($"{Log} TAB {progress},{seed}|{map.Floors.Count} floors|{map.Links.Count} physical links|" +
                $"{pairs.Length} portal pairs|{internalPairs} internal pairs");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException($"{Log}: {message}");
        }
    }
}

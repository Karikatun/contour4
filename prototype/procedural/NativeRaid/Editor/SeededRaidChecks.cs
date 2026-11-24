using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.NativeRaid.Runtime;
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
            var route = current.Module.Routes.Single(route => route.To == RaidExtraction.Machine);
            foreach (var knot in route.Samples(.13f)) CheckCart(current.Point(knot.Position),
                current.Direction(knot.Forward), current.Direction(knot.Up), checkWheels: true);
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
            foreach (int completed in new[] { 0, 1, 3, 5, 6, 14 })
            {
                var plans = Enumerable.Range(-32, 64).Append(2133).Append(1499861616)
                    .Select(seed => RaidPlan.Generate(seed, entrance, completed,
                        maintenance: Contour4.Procedural.Catalog.MaintenanceTrench.Create)).ToArray();
                int variants = plans.Select(Signature).Distinct().Count();
                Require(variants > 8, $"Level {completed + 1}: seed varies actual rooms and poses ({variants})");
                foreach (var plan in plans)
                {
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
                        "Preserved seed and progression reproduce exact room choices and poses");
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

        private static GameObject Block(Transform root, Vector3 position, Vector3 scale)
        {
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.transform.SetParent(root, false);
            obstacle.transform.position = position;
            obstacle.transform.localScale = scale;
            Physics.SyncTransforms();
            return obstacle;
        }

        private static string Signature(RaidPlan plan) => string.Join(";", plan.Rooms.Select(room =>
            room.Module.Id + "@" + FormattableString.Invariant($"{room.Frame.position:F3}/{room.Frame.rotation:F3}")));

        private static void Require(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException(message);
        }
    }
}

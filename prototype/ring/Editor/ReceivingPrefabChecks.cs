using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceJourney
{
    public static class ReceivingPrefabChecks
    {
        private const string Log = "CONTOUR4 RECEIVING PREFAB";
        private static readonly Pose Entrance = new(new Vector3(40, 0, -20), Quaternion.Euler(0, 90, 0));

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            using var bundle = RoomPrefabBundle.Load(Environment.GetEnvironmentVariable("CONTOUR4_ROOMS_BUNDLE"),
                Environment.GetEnvironmentVariable("CONTOUR4_PREPARED_BUNDLE"));
            var prefabs = bundle.Prefabs;
            Verify(0, 0, prefabs, false);

            const int researchStage = 6;
            int researchSeed = Enumerable.Range(0, 64).First(seed => RaidPlan.Generate(seed, Entrance, researchStage,
                maintenance: MaintenanceTrench.Create).Rooms.Any(room => Receives(room) && HasResearch(room)));
            Verify(researchSeed, researchStage, prefabs, true);
            Debug.Log($"{Log} PASSED: both bundle variants, stock interaction geometry, loose cargo and Tab projection.");
        }

        public static void RunGrayReference()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Procedural gray receiving reference");
            try
            {
                var raid = root.AddComponent<RaidAssembly>();
                raid.Place(0, Entrance, 0, 0, catalogSites: true);
                RaidRoomInteractions.Attach(raid);
                VerifyRoute(raid);
                Debug.Log($"{Log} GRAY REFERENCE PASSED.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void Verify(int seed, int completed, RaidRoomPrefabs prefabs, bool withResearch)
        {
            var root = new GameObject("Procedural authored receiving check");
            try
            {
                var raid = root.AddComponent<RaidAssembly>();
                raid.Place(seed, Entrance, 0, 0, catalogSites: true, completedLevels: completed, roomPrefabs: prefabs);
                Physics.SyncTransforms();
                var authored = raid.Rooms.Where(room => Receives(room.Plan)).ToArray();
                Require(authored.Length > 0 && authored.All(room => room.Authored), "Every receiving instance must come from the bundle.");
                Require(raid.Rooms.All(room => Receives(room.Plan) == (room.Authored && !room.Prepared)),
                    "Receiving replacement scope mismatch.");
                var variant = authored.Where(room => HasResearch(room.Plan) == withResearch).ToArray();
                Require(variant.Length > 0, "Seed did not generate the expected receiving variant.");
                foreach (BakedRoom room in variant)
                {
                    Require(room.Root.GetComponentsInChildren<Light>().Length > 0 && !room.Root.Find("Graybox work light"),
                        "Authored lighting was not cloned.");
                    Require(room.Root.GetComponentsInChildren<MeshRenderer>().All(renderer => renderer.sharedMaterials.All(material =>
                        material && material.shader.name != "Hidden/InternalErrorShader")), "Packed room has a missing material or shader.");
                    Require(room.Colliders.All(collider => collider.enabled && collider.gameObject.activeInHierarchy) &&
                        room.Plan.OpenPlugs.All(id => room.Root.Find(id) && !room.Root.Find(id).gameObject.activeSelf),
                        "An opened plug or registered room collider has the wrong activation state.");
                    Require(room.Plan.OpenPlugs.All(id => prefabs.For(room.Plan).transform.Find(id).gameObject.activeSelf),
                        "Generating a room must preserve the prefab's plugs for future raids.");
                    Require(room.Floor.Support.Count > 0, "Authored deck was not registered as local support.");
                    foreach (var collider in room.Floor.Support)
                    {
                        var box = (BoxCollider)collider;
                        Vector3 contact = box.transform.TransformPoint(box.center + Vector3.up * box.size.y / 2);
                        Require(raid.HasPortalSupport(room.Floor, contact), "Registered support does not raycast on the placed prefab.");
                    }
                }

                var interactions = RaidRoomInteractions.Attach(raid);
                raid.InstallNavigation();
                foreach (var room in variant)
                {
                    var fixtures = interactions.Cabinets.Where(front => front.Body.transform.IsChildOf(room.Root)).ToArray();
                    Require(fixtures.Length > 0 && fixtures.All(front => front.Body.GetComponent<Joint>() && front.Parts.Count >= 2),
                        "Authored cabinet parts were not registered with their runtime joints.");
                    Require(raid.Context.Navigation.TryStand(room.Floor, raid.Plan.Arrival(room.Plan).Pad(1.7f), out _),
                        "The authored arrival pad is missing local navigation support.");
                }
                var slots = RaidLootSites.Find(raid);
                foreach (var room in variant)
                {
                    var candidates = slots.Where(slot => slot.Room == room).ToArray();
                    Require(candidates.Length > 0 && candidates.All(slot => room.Colliders.Contains(slot.Support)),
                        "Receiving loot must retain valid support on actual packed geometry.");
                    Debug.Log($"{Log} seed={seed}, stage={completed}, research={withResearch}, index={room.Plan.Index}, " +
                        $"solids={room.Colliders.Count}, supports={room.Floor.Support.Count}, loot={candidates.Length}, " +
                        $"drawers/doors={interactions.Cabinets.Count(front => front.Body.transform.IsChildOf(room.Root))}.");
                }

                if (!withResearch) VerifyRoute(raid);
                else VerifyResearchMap(raid);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void VerifyRoute(RaidAssembly raid)
        {
            var fold = raid.Folds.Single(f => f.Link.From.Room == raid.Plan.Entrance.Room && Receives(f.Link.To.Room));
            Require(Mathf.Abs(Vector3.Dot(fold.Link.From.Up, fold.Link.To.Up)) < .001f, "Expected a 90 degree physical fold.");
            VerifyCart(raid, fold, false);
            VerifyCart(raid, fold, true);
            VerifyMap(raid, fold);
        }

        private static void VerifyCart(RaidAssembly raid, FreightFold fold, bool reverse)
        {
            var observer = new GameObject("Offline gravity observer").AddComponent<Rigidbody>();
            observer.isKinematic = true;
            var cart = new GameObject("Open proxy cart").AddComponent<Rigidbody>();
            cart.mass = 35;
            cart.freezeRotation = true;
            const float bedTop = .64f;
            AddBox(cart, new Vector3(0, bedTop - .06f, 0), new Vector3(1.38f, .12f, 1.72f));
            foreach (float side in new[] { -1f, 1f })
            {
                AddBox(cart, new Vector3(side * .65f, .82f, 0), new Vector3(.08f, .36f, 1.72f));
                AddBox(cart, new Vector3(0, .82f, side * .82f), new Vector3(1.3f, .36f, .08f));
                foreach (float z in new[] { -.68f, .68f })
                {
                    var wheel = cart.gameObject.AddComponent<SphereCollider>();
                    wheel.center = new Vector3(side * .53f, .18f, z);
                    wheel.radius = .18f;
                }
            }
            var payload = new GameObject("Unsecured proxy cargo").AddComponent<Rigidbody>();
            payload.mass = 3f;
            payload.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var cargoCollider = AddBox(payload, Vector3.zero, new Vector3(.28f, .28f, .31f));
            var friction = new PhysicsMaterial("Proxy payload contact") { staticFriction = .65f, dynamicFriction = .58f };
            cargoCollider.sharedMaterial = friction;

            var previous = new Pose(raid.transform.position, raid.transform.rotation);
            SimulationMode simulation = Physics.simulationMode;
            var deckLayer = 1 << 18;
            cart.gameObject.layer = payload.gameObject.layer = 18;
            try
            {
                var course = Course(fold, reverse);
                Vector3 Point(ModulePose knot) => fold.Root.TransformPoint(RaidPlan.Vector(knot.Position));
                Vector3 Direction(System.Numerics.Vector3 direction) => fold.Root.TransformDirection(RaidPlan.Vector(direction));
                var stage = new RaidWorldRebase();
                Vector3 first = Point(course[0]);
                Vector3 up = Direction(course[0].Up);
                Quaternion facing = Quaternion.LookRotation(Direction(course[0].Forward) * (reverse ? -1f : 1f), up);
                cart.transform.SetPositionAndRotation(first + up * .045f, facing);
                payload.transform.SetPositionAndRotation(cart.transform.TransformPoint(new Vector3(.10f, bedTop + .15f, -.09f)), facing);
                observer.position = first;
                Rigidbody[] cargoBodies = { cart, payload };
                var actors = raid.GetComponentsInChildren<Rigidbody>().Concat(cargoBodies).ToArray();
                Physics.simulationMode = SimulationMode.Script;
                Physics.SyncTransforms();
                stage.Apply(raid.transform, observer, actors, first, Quaternion.FromToRotation(up, Vector3.up));

                float maxDeckError = 0f, maxPayloadTravel = 0f;
                int supported = 0, ticks = 0;
                Vector3 referenceCargo = cart.transform.InverseTransformPoint(payload.position);
                Require(Mathf.Abs(referenceCargo.y - (bedTop + .15f)) < .01f,
                    "The independent cargo body must start on top of the basket floor.");
                foreach (var knot in Enumerable.Repeat(course[0], 20).Concat(course).Concat(Enumerable.Repeat(course.Last(), 25)))
                {
                    Vector3 target = Point(knot);
                    observer.position = target;
                    stage.Apply(raid.transform, observer, actors, target, Quaternion.FromToRotation(Direction(knot.Up), Vector3.up));
                    cart.rotation = Quaternion.LookRotation(Direction(knot.Forward) * (reverse ? -1f : 1f), Vector3.up);
                    Vector3 steer = Vector3.ProjectOnPlane(Point(knot) - cart.position, Vector3.up);
                    cart.linearVelocity = Vector3.ClampMagnitude(steer * 16f, 3.0f) + Vector3.up * cart.linearVelocity.y;
                    foreach (var body in cargoBodies)
                        if (raid.TryUp(body.worldCenterOfMass, out Vector3 gravityUp))
                            body.AddForce(-gravityUp * Physics.gravity.magnitude - Physics.gravity, ForceMode.Acceleration);
                    Physics.Simulate(.02f);
                    float deckError = Mathf.Abs(cart.position.y - Point(knot).y);
                    maxDeckError = Mathf.Max(deckError, maxDeckError);
                    var localCargo = cart.transform.InverseTransformPoint(payload.position);
                    maxPayloadTravel = Mathf.Max(maxPayloadTravel, Vector3.Distance(referenceCargo, localCargo));
                    if (Physics.Raycast(cart.position + Vector3.up * .18f, Vector3.down, out var hit, .45f, ~deckLayer,
                        QueryTriggerInteraction.Ignore) && hit.collider.transform.IsChildOf(raid.transform)) supported++;
                    Require(deckError < .46f && steer.magnitude < .46f && localCargo.y >= bedTop - .02f &&
                        localCargo.y < 1.2f && Mathf.Abs(localCargo.x) < .67f && Mathf.Abs(localCargo.z) < .84f,
                        $"Cart or unsecured payload lost the deck at step {ticks} (reverse={reverse}, deck={deckError:F3}, cargo={localCargo}).");
                    ticks++;
                }
                Vector3 end = Point(course.Last());
                var arrival = raid.Room(reverse ? fold.Link.From.Room : fold.Link.To.Room);
                Require(supported >= ticks * .85f && Vector3.Distance(end, cart.position) < .16f &&
                    raid.TryUp(cart.position + Vector3.up * .3f, out Vector3 endUp) && Vector3.Dot(endUp, arrival.Floor.Up) > .999f,
                    "Cart did not settle on its destination floor.");
                Debug.Log($"{Log} PhysX cart {(reverse ? "return" : "outbound")}: {ticks} ticks, {supported} deck samples, " +
                    $"max support height offset {maxDeckError:F3} m, max independent cargo travel {maxPayloadTravel:F3} m.");
            }
            finally
            {
                Physics.simulationMode = simulation;
                raid.transform.SetPositionAndRotation(previous.position, previous.rotation);
                Object.DestroyImmediate(friction);
                Object.DestroyImmediate(payload.gameObject);
                Object.DestroyImmediate(cart.gameObject);
                Object.DestroyImmediate(observer.gameObject);
                Physics.SyncTransforms();
            }
        }

        private static ModulePose[] Course(FreightFold fold, bool reverse)
        {
            var route = fold.Link.Fold.Module.Routes[0];
            var entry = route.Knots[0];
            var exit = route.Knots[route.Knots.Length - 1];
            const float step = .038f;
            var course = Enumerable.Range(1, 47).Reverse().Select(i =>
                new ModulePose(entry.Point(-i * step, 0), entry.Forward, entry.Up)).Concat(route.Samples(step))
                .Concat(Enumerable.Range(1, 47).Select(i => new ModulePose(exit.Point(i * step, 0), exit.Forward, exit.Up))).ToArray();
            if (reverse) Array.Reverse(course);
            return course;
        }

        private static BoxCollider AddBox(Rigidbody body, Vector3 center, Vector3 size)
        {
            var box = body.gameObject.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
            return box;
        }

        private static void VerifyMap(RaidAssembly raid, FreightFold fold)
        {
            var map = RaidMapPlan.Create(raid.Plan);
            var dispatch = map.Floors.Single(floor => ReferenceEquals(floor.Floor, fold.Link.From.Room.Module.Floors[0]));
            var receiving = map.Floors.Single(floor => ReferenceEquals(floor.Floor, fold.Link.To.Room.Module.Floors[0]));
            var link = map.Links.Single(edge => edge.A.Floor == dispatch && edge.B.Floor == receiving);
            Require(!receiving.Revealed && !link.Visited && !map.Doors.Any(door => door.Portal && door.End.Floor == receiving),
                "Unvisited receiving room and its physical fold must start hidden and unpaired.");
            var corridor = map.Corridor(link).ToArray();
            Require(link.Transition.Length >= 10 && corridor.Length == 2 &&
                Vector2.Distance(corridor[0], dispatch.Flat(link.A.Edge)) < .001f &&
                Vector2.Distance(corridor[1], receiving.Flat(link.B.Edge)) < .001f,
                "The physical fold must unfold as one straight corridor between the two mouths.");

            Vector3 start = fold.Link.From.Pad(1.7f);
            Vector3 end = fold.Link.To.Pad(1.7f);
            Require(map.Project(start, fold.Link.From.Outward, true, out var startRoom, out _, out _) && startRoom == dispatch,
                "Dispatch reveal is on the wrong map floor.");
            var center = fold.Link.Fold.Module.Routes[0].Knots;
            var midpoint = center[center.Length / 2];
            Vector3 mid = fold.Root.TransformPoint(RaidPlan.Vector(midpoint.Position));
            Require(map.Project(mid, fold.Root.TransformDirection(RaidPlan.Vector(midpoint.Forward)), true, out _, out var bridgePoint, out _) &&
                link.Visited && Mathf.Abs((corridor[1].x - corridor[0].x) * (bridgePoint.y - corridor[0].y) -
                    (corridor[1].y - corridor[0].y) * (bridgePoint.x - corridor[0].x)) < .001f,
                "Walking the fold must visit its corridor in the native map plan.");
            Require(map.Project(end, -fold.Link.To.Outward, true, out var endRoom, out var position, out var heading) &&
                endRoom == receiving && receiving.Revealed && heading.sqrMagnitude > .01f && receiving.Bounds.Contains(position),
                "The rotated arrival must reveal and project into receiving, with a usable heading.");
            Require(Mathf.Abs(map.NativePoint(position).y - map.Stock.Room.position.y) < .001f &&
                Mathf.Abs(map.NativeVector(heading).y) < .001f, "Map output must remain anchored to the stock truck plane.");
            VerifyRebasedProjection(raid, fold, map, receiving, position, heading, end);
            DrawMap(map, position, heading);
            Debug.Log($"{Log} Tab projection: {map.Floors.Count} floors, {link.Transition.Length} fold knots, " +
                $"{corridor.Length} corridor mouths, arrival within receiving and stock-plane anchored.");
        }

        private static void VerifyRebasedProjection(RaidAssembly raid, FreightFold fold, RaidMapPlan map,
            RaidMapPlan.FloorView receiving, Vector2 position, Vector2 heading, Vector3 end)
        {
            var root = raid.transform;
            var originalPose = new Pose(root.position, root.rotation);
            Matrix4x4 originalRootToWorld = root.localToWorldMatrix;
            Vector3 localEnd = root.InverseTransformPoint(end);
            Vector3 localFacing = root.InverseTransformDirection(-fold.Link.To.Outward);
            var observer = new GameObject("Rebased map player").AddComponent<Rigidbody>();
            observer.isKinematic = true;
            observer.position = end;
            try
            {
                new RaidWorldRebase().Apply(root, observer, raid.GetComponentsInChildren<Rigidbody>(), end,
                    Quaternion.FromToRotation(fold.Link.To.Up, Vector3.up));
                var worldToPlan = originalRootToWorld * root.worldToLocalMatrix;
                Require(map.Project(worldToPlan.MultiplyPoint3x4(root.TransformPoint(localEnd)),
                        worldToPlan.MultiplyVector(root.TransformDirection(localFacing)), false,
                        out var floor, out var rebasedPosition, out var rebasedHeading) && floor == receiving &&
                    Vector2.Distance(position, rebasedPosition) < .001f && Vector2.Distance(heading, rebasedHeading) < .001f,
                    "Gravity rebasing changed the receiving marker's stock-map position or heading.");
            }
            finally
            {
                root.SetPositionAndRotation(originalPose.position, originalPose.rotation);
                Object.DestroyImmediate(observer.gameObject);
            }
        }

        private static void VerifyResearchMap(RaidAssembly raid)
        {
            var map = RaidMapPlan.Create(raid.Plan);
            var pairs = map.Doors.Where(door => door.Portal).GroupBy(door => door.Color).ToArray();
            Require(pairs.Length == raid.Plan.Portals.Count && pairs.All(pair => pair.Count() == 2),
                "Every portal pair must keep two endpoints with its own shared map color.");
            Debug.Log($"{Log} research Tab projection: {map.Floors.Count} floors, {pairs.Length} distinct portal pairs.");
        }

        internal static void DrawMap(RaidMapPlan map, Vector2 point, Vector2 heading, string output = null)
        {
            const int width = 960, height = 540;
            var points = map.Floors.SelectMany(floor => floor.Outline).ToArray();
            Vector2 low = new(points.Min(p => p.x), points.Min(p => p.y));
            Vector2 high = new(points.Max(p => p.x), points.Max(p => p.y));
            float scale = Mathf.Min((width - 80) / (high.x - low.x), (height - 80) / (high.y - low.y));
            Vector2 center = (high + low) / 2;
            var pixels = Enumerable.Repeat(new Color32(13, 22, 31, 255), width * height).ToArray();
            var shown = new Color32(114, 235, 252, 255);
            var hidden = new Color32(48, 67, 84, 255);
            var marker = new Color32(255, 188, 77, 255);
            Vector2Int Pixel(Vector2 p) => Vector2Int.RoundToInt((p - center) * scale + new Vector2(width / 2, height / 2));
            void Segment(Vector2 start, Vector2 end, Color32 color, int radius = 1)
            {
                Vector2Int a = Pixel(start), b = Pixel(end);
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
                for (int i = 0; i <= steps; i++)
                {
                    var pixel = Vector2Int.RoundToInt(Vector2.Lerp(a, b, i / (float)steps));
                    for (int y = -radius; y <= radius; y++)
                    for (int x = -radius; x <= radius; x++)
                        if ((uint)(pixel.x + x) < width && (uint)(pixel.y + y) < height)
                            pixels[(pixel.y + y) * width + pixel.x + x] = color;
                }
            }
            foreach (var floor in map.Floors)
                for (int i = 0; i < floor.Outline.Length; i++)
                    Segment(floor.Outline[i], floor.Outline[(i + 1) % floor.Outline.Length], floor.Revealed ? shown : hidden);
            foreach (var corridor in map.Links)
            {
                var path = map.Corridor(corridor).ToArray();
                for (int i = 1; i < path.Length; i++) Segment(path[i - 1], path[i], corridor.Visited ? shown : hidden);
            }
            foreach (var door in map.Doors.Where(door => door.Portal))
            {
                var (from, to) = door.PortalStroke();
                Segment(from, to, door.Color);
            }
            Segment(point, point, marker, 4);
            Segment(point, point + heading.normalized * 1.3f, marker, 2);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(output ?? Environment.GetEnvironmentVariable("CONTOUR4_MAP_PROJECTION"), texture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static bool Receives(RaidPlan.Room room) => room.Module.Id == FreightServiceRoom.SampleReceiving;
        private static bool HasResearch(RaidPlan.Room room) => room.Module.Docks.Any(dock => dock.Id == FreightServiceRoom.ResearchDoor);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException($"{Log}: {message}");
        }

    }

    // The native map projection accepts a ring branch, whose independent stock systems are outside this fixture.
    internal sealed class RingRuntime : MonoBehaviour
    {
        internal Transform[] Rooms => Array.Empty<Transform>();
        internal Transform Ring => transform;
        internal IReadOnlyList<(PortalEnd A, PortalEnd B)> Portals => Array.Empty<(PortalEnd, PortalEnd)>();
        internal static Transform Marker(Transform root, string name) => root.Find(name);
    }

    internal sealed class PortalEnd
    {
        internal Transform Room => null;
        internal Transform Marker => null;
        internal Quaternion Orientation => Quaternion.identity;
        internal Vector3 Center => Vector3.zero;
        internal float Height => 0f;
        internal float Width => 0f;
        internal bool Upper => false;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SpaceJourney
{
    public static class FurnitureAccessChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        // R.E.P.O. 0.5.2 level0 locomotion capsule and serialized crouch scale.
        // The larger witness supplies tolerance for steering and posture changes.
        private static readonly (float Radius, float Height) StockCrawl = (.20f, .425f);
        private static readonly (float Radius, float Height) Clearance = (.30f, .80f);
        private static readonly (float Radius, float Height) Standing = (.20f, 1.70f);
        private const float FootGap = .02f;
        private const float Step = .05f;

        private sealed class Shelter
        {
            internal readonly string BaselinePhysics, BaselineRender;
            internal readonly Vector3 Approach, Refuge, Eye, Aim;
            internal readonly float Offset;

            internal Shelter(Vector3 approach, Vector3 refuge, float offset,
                Vector3 eye, Vector3 aim, string baselinePhysics, string baselineRender)
            {
                Approach = approach;
                Refuge = refuge;
                Offset = offset;
                Eye = eye;
                Aim = aim;
                BaselinePhysics = baselinePhysics;
                BaselineRender = baselineRender;
            }
        }

        private static readonly Shelter Receiving = new(new(7.88f, 0, 2.04f), new(7.88f, 0, 3.59f), .26f,
            new(7.08f, .93f, 1.36f), new(7.88f, .88f, 3.59f), null, "Receiving open packing crate");
        private static readonly Shelter Workshop = new(new(1.34f, 0, -1.95f), new(1.34f, 0, -3.40f), .17f,
            new(.91f, .92f, -.94f), new(1.34f, .75f, -3.52f), "inspection-bench-seat", "Service opened instrument chassis");

        public static void Run()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string output = Environment.GetEnvironmentVariable("CONTOUR4_FURNITURE_OUTPUT");
            string selection = Environment.GetEnvironmentVariable("CONTOUR4_FURNITURE_ROOMS");
            bool baseline = Environment.GetEnvironmentVariable("CONTOUR4_FURNITURE_EXPECT_OBSTRUCTIONS") == "1";
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            using var bundles = RoomPrefabBundle.Load(Path.Combine(output, "contour4-rooms.bundle"),
                Path.Combine(output, "contour4-prepared-modules.bundle"), requireWear: true);
            var rows = new List<string> { "case\tcheck\tobservation" };
            var failures = new List<string>();
            if (selection is "receiving" or "both")
                foreach (bool research in new[] { true, false })
                    foreach (bool authored in new[] { true, false })
                        Probe(FreightServiceRoom.Create(FreightServiceRoom.SampleReceiving, research), Receiving, bundles.Prefabs,
                            authored, $"receiving-{(research ? "with" : "without")}-research-{(authored ? "bundled" : "procedural")}",
                            baseline, output, rows, failures);
            if (selection is "workshop" or "both")
                Probe(FreightServiceRoom.Create(FreightServiceRoom.InstrumentService, false), Workshop, bundles.Prefabs,
                    false, "workshop-procedural", baseline, output, rows, failures);
            File.WriteAllLines(Path.Combine(output, "furniture-access.tsv"), rows);
            if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
            Debug.Log($"CONTOUR4 FURNITURE ACCESS PASSED: {selection}, control={baseline}, {rows.Count - 1} observations.");
        }

        private static void Probe(GrayModule module, Shelter shelter, RaidRoomPrefabs prefabs, bool authored,
            string label, bool baseline, string output, List<string> rows, List<string> failures)
        {
            var host = new GameObject("Furniture access fixture");
            try
            {
                var raid = host.AddComponent<RaidAssembly>();
                var frame = new Pose(Vector3.zero, Quaternion.identity);
                var plan = new RaidPlan.Room(module, frame, frame, 0);
                foreach (Dock dock in module.Docks) plan.OpenPlugs.Add(dock.PlugId);
                RaidCatalogSites.Dress(module);
                RaidLootSites.Dress(module);
                var prefab = authored ? prefabs.For(plan) : null;
                var shader = Shader.Find("Standard");
                var (floor, wall) = ((Material, Material))Invoke(raid, "Finishes", shader);
                var shell = (Material)Invoke(raid, "Material", shader, new Color(.52f, .56f, .57f));
                var obstacle = (Material)Invoke(raid, "Material", shader, new Color(.39f, .33f, .25f));
                var sight = new RaidSightStrips(shader, Field<List<Material>>(raid, "materials"), Field<List<Mesh>>(raid, "meshes"));
                var fitout = (RaidInteriorFitout)Invoke(raid, "CreateFitout", shader);
                var room = (BakedRoom)Invoke(raid, "PlaceRoom", plan, 0, floor, wall, shell, obstacle, sight, prefab, fitout);
                Field<RoomBloodTraces>(raid, "bloodTraces").Apply(room);
                Physics.SyncTransforms();
                Check("placement", $"authored={room.Authored}; docks={string.Join(",", module.Docks.Select(dock => dock.Id))}", room.Authored == authored);

                Vector3 left = Vector3.left * shelter.Offset, right = Vector3.right * shelter.Offset;
                var paths = new[] {
                    new[] { shelter.Approach, shelter.Refuge, shelter.Approach },
                    new[] { shelter.Approach + left, shelter.Refuge + left, shelter.Refuge + right, shelter.Approach + right },
                    new[] { shelter.Approach + right, shelter.Refuge + right, shelter.Refuge + left, shelter.Approach + left },
                };
                Check("travel", $"approach leg {Vector3.Distance(shelter.Approach, shelter.Refuge):F3} m; " +
                    $"two offset routes ±{shelter.Offset:F3} m; front return", true);
                var physical = new HashSet<string>();
                for (int i = 0; i < paths.Length; i++)
                {
                    foreach (var (name, shape) in new[] { ("stock-crawl", StockCrawl), ("clearance", Clearance) })
                    {
                        var contacts = Trace(room.Root, paths[i], shape);
                        physical.UnionWith(contacts);
                        Check($"route-{i}-{name}-physics", ContactText(contacts), baseline && shelter.BaselinePhysics != null || contacts.Count == 0);
                    }
                    bool supported = paths[i].Zip(paths[i].Skip(1), (a, b) => Samples(a, b).All(point => Supported(room.Root, point))).All(ok => ok);
                    bool rises = At(room.Root, paths[i][0], Standing).Length == 0 && At(room.Root, paths[i][^1], Standing).Length == 0;
                    Check($"route-{i}-floor", supported ? "continuous" : "missing", supported);
                    Check($"route-{i}-stand-after-exit", rises ? "both ends clear" : "blocked", rises);
                }
                string topName = shelter == Receiving ? "receiving-sorting-rack-middle" : "instrument-service-inspection-bench-top";
                Bounds top = room.Colliders.Single(collider => collider.name == topName).bounds;
                bool footprint = new[] { shelter.Refuge + left, shelter.Refuge, shelter.Refuge + right }.All(refuge =>
                    refuge.x - Clearance.Radius >= top.min.x && refuge.x + Clearance.Radius <= top.max.x &&
                    refuge.z - Clearance.Radius >= top.min.z && refuge.z + Clearance.Radius <= top.max.z);
                var overhead = At(room.Root, shelter.Refuge, Standing);
                Check("overhead-cover", $"{topName}; underside {top.min.y:F3} m; full wide capsule under top: {footprint}",
                    footprint && overhead.Contains(topName));
                Vector3 from = shelter == Receiving ? new(6.26f, .68f, 3.58f) : new(3.03f, .68f, -3.38f);
                Vector3 to = shelter.Refuge + new Vector3(0, .68f, 0);
                string sideCover = shelter == Receiving ? "receiving-sorting-rack-side-cover" : "instrument-service-inspection-bay-screen";
                bool sided = Physics.Linecast(from, to, out var coverHit, ~0, QueryTriggerInteraction.Ignore) && coverHit.collider.name == sideCover;
                Check("side-cover", $"{from:F3} → {to:F3}; {(coverHit.collider ? coverHit.collider.name : "exposed")}", sided);
                if (shelter == Receiving)
                {
                    const BindingFlags LootPrivate = BindingFlags.Static | BindingFlags.NonPublic;
                    var sites = new List<RaidLootSlot>();
                    var supports = room.Colliders.OfType<BoxCollider>().ToLookup(collider => collider.name);
                    var routes = (IEnumerable<Bounds>)typeof(RaidLootSites).GetMethod("RouteSweeps", LootPrivate).Invoke(null, new object[] { room });
                    typeof(RaidLootSites).GetMethod("AddReceivingSites", LootPrivate).Invoke(null, new object[] { room, supports, routes.ToArray(), sites });
                    var valid = sites.Where(slot => slot.Placement(RaidLootPlacement.StructuralBounds(room))?.AllowsEnvelope(slot.WorldPose,
                        RaidLootSites.Envelope(slot.Size)) == true).ToArray();
                    Check("receiving-loot-sites", $"{valid.Length}; " + string.Join(" | ", valid.Select(slot =>
                        $"{slot.Support.name}:{slot.Size}:{slot.WorldPose.position:F3}")), valid.Length == SampleReceivingLoot.Sites.Length);
                }
                else
                {
                    var pocket = BrokenEquipmentLoot.Find(room);
                    Check("fractured-instrument-tiny-loot", pocket != null ? $"pivot={pocket.WorldPose.position:F3}; {pocket.Support.name}" : "missing", pocket != null);
                    if (pocket != null)
                        typeof(RoomEquipmentLootChecks).GetMethod("VerifyPocket", BindingFlags.Static | BindingFlags.NonPublic)
                            .Invoke(null, new object[] { room, pocket });
                }

                int cargoSamples = 0;
                var cargoHits = new HashSet<string>();
                foreach (var route in module.Routes.Where(route => route.Cargo == Cargo.Cart))
                    foreach (var pose in route.Samples(.14f))
                    {
                        if (pose.Position.X < .4f || pose.Position.X > 8.85f || Math.Abs(pose.Position.Z) > 3.975f) continue;
                        Vector3 center = RaidPlan.Vector(pose.Position + pose.Up * (.04f + route.Envelope.Y / 2));
                        var rotation = Quaternion.LookRotation(RaidPlan.Vector(pose.Right), RaidPlan.Vector(pose.Up));
                        foreach (var hit in Physics.OverlapBox(center, RaidPlan.Vector(route.Envelope) * .495f,
                            rotation, ~0, QueryTriggerInteraction.Ignore)) cargoHits.Add(hit.name);
                        cargoSamples++;
                    }
                Check("cargo-lanes", $"{cargoSamples} local samples; {ContactText(cargoHits)}", cargoSamples > 100 && cargoHits.Count == 0);

                // PhysX normally ignores these meshes. Give the visible detail a temporary query surface
                // so a renderer placed inside the usable opening can fail the same sweep.
                Bounds viewVolume = new(shelter.Refuge, Vector3.zero);
                viewVolume.Encapsulate(shelter.Approach);
                viewVolume.Expand(new Vector3(2 * (Clearance.Radius + shelter.Offset),
                    2 * (Clearance.Height + FootGap), 2 * Clearance.Radius));
                var visualSurfaces = room.Root.GetComponentsInChildren<MeshRenderer>()
                    .Where(renderer => renderer.enabled && !renderer.forceRenderingOff &&
                        renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly && renderer.bounds.Intersects(viewVolume) &&
                        !renderer.GetComponents<Collider>().Any(collider => collider.enabled))
                    .Select(renderer => renderer.GetComponent<MeshFilter>()).Where(filter => filter && filter.sharedMesh)
                    .Select(filter => { var proxy = filter.gameObject.AddComponent<MeshCollider>(); proxy.sharedMesh = filter.sharedMesh; return proxy; }).ToArray();
                Physics.SyncTransforms();
                foreach (var (name, shape) in new[] { ("stock-crawl", StockCrawl), ("clearance", Clearance) })
                {
                    var visible = new HashSet<string>();
                    foreach (Vector3[] path in paths) visible.UnionWith(Trace(room.Root, path, shape));
                    string[] artOnly = visible.Except(physical).OrderBy(contact => contact).ToArray();
                    Check(name + "-visible-mesh-sweep", $"{visualSurfaces.Length} query meshes; {ContactText(artOnly)}",
                        baseline ? artOnly.Any(contact => contact.Contains(shelter.BaselineRender)) : artOnly.Length == 0);
                }
                if (baseline && shelter.BaselinePhysics != null)
                    Check("physical-control", ContactText(physical), physical.Any(name => name.Contains(shelter.BaselinePhysics)));
                foreach (var proxy in visualSurfaces) Object.DestroyImmediate(proxy);

#if CONTOUR4_WORKROOM_TEST
                WorkroomClutterChecks.Probe(room, label, Check);
#endif
                Render(room.Root, shelter.Eye, shelter.Aim, Path.Combine(output, label + ".png"));
            }
            finally { Object.DestroyImmediate(host); }

            void Check(string name, string observation, bool passed)
            {
                rows.Add($"{label}\t{name}\t{observation}");
                if (!passed) failures.Add($"{label} / {name}: {observation}");
            }
        }

        private static HashSet<string> Trace(Transform room, Vector3[] points, (float Radius, float Height) shape)
        {
            var contacts = new HashSet<string>();
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 start = points[i - 1];
                Vector3 delta = room.TransformVector(points[i] - start);
                Capsule(room, start, shape, out var low, out var high);
                foreach (var hit in Physics.CapsuleCastAll(low, high, shape.Radius, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                    contacts.Add(ColliderPath(hit.collider, room));
                foreach (Vector3 sample in Samples(start, points[i])) contacts.UnionWith(At(room, sample, shape));
            }
            return contacts;
        }

        private static string[] At(Transform room, Vector3 position, (float Radius, float Height) shape)
        {
            Capsule(room, position, shape, out var low, out var high);
            return Physics.OverlapCapsule(low, high, shape.Radius, ~0, QueryTriggerInteraction.Ignore)
                .Select(hit => ColliderPath(hit, room)).ToArray();
        }

        private static void Capsule(Transform room, Vector3 position, (float Radius, float Height) shape, out Vector3 low, out Vector3 high)
        {
            low = room.TransformPoint(position + Vector3.up * (FootGap + shape.Radius));
            high = room.TransformPoint(position + Vector3.up * (FootGap + shape.Height - shape.Radius));
        }

        private static IEnumerable<Vector3> Samples(Vector3 start, Vector3 end)
        {
            int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) / Step));
            for (int i = 0; i <= count; i++) yield return Vector3.Lerp(start, end, i / (float)count);
        }

        private static bool Supported(Transform room, Vector3 point)
        {
            foreach (Vector3 offset in new[] { Vector3.zero, Vector3.left * .20f, Vector3.right * .20f,
                Vector3.forward * .20f, Vector3.back * .20f })
                if (!Physics.Raycast(room.TransformPoint(point + offset + Vector3.up * .04f), -room.up, out var hit,
                    .06f, ~0, QueryTriggerInteraction.Ignore) || hit.collider.name != "ground") return false;
            return true;
        }

        private static string ContactText(IEnumerable<string> contacts) => contacts.Any() ? string.Join(", ", contacts.OrderBy(name => name)) : "clear";

        private static string ColliderPath(Collider hit, Transform room)
        {
            Transform node = hit.transform;
            string path = node.name;
            while (node.parent && node.parent != room) { node = node.parent; path = node.name + "/" + path; }
            return path;
        }

        private static void Render(Transform room, Vector3 eye, Vector3 aim, string path)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.43f, .43f, .43f);
            RenderSettings.reflectionIntensity = 0;
            foreach (var light in room.GetComponentsInChildren<Light>()) light.enabled = false;
            var camera = new GameObject("Furniture access camera").AddComponent<Camera>();
            var key = new GameObject("Furniture access light").AddComponent<Light>();
            var frame = RenderTexture.GetTemporary(1080, 720, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(1080, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.transform.SetPositionAndRotation(room.TransformPoint(eye), Quaternion.LookRotation(aim - eye, room.up));
                camera.fieldOfView = 58;
                camera.targetTexture = frame;
                key.type = LightType.Directional;
                key.intensity = .75f;
                key.transform.rotation = Quaternion.LookRotation(camera.transform.forward - room.up * .7f + camera.transform.right * .43f);
                camera.Render();
                RenderTexture.active = frame;
                image.ReadPixels(new Rect(0, 0, 1080, 720), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(key.gameObject);
                Object.DestroyImmediate(image);
                RenderTexture.ReleaseTemporary(frame);
            }
        }

        private static T Field<T>(RaidAssembly raid, string name) => (T)typeof(RaidAssembly).GetField(name, Private).GetValue(raid);
        private static object Invoke(RaidAssembly raid, string name, params object[] args) => typeof(RaidAssembly).GetMethod(name, Private).Invoke(raid, args);
    }
}

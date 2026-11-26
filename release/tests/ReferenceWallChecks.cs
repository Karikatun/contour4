using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SpaceJourney
{
    public static class ReferenceWallChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int Finish = 8;

        public static void Run()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string output = Environment.GetEnvironmentVariable("CONTOUR4_WALL_OUTPUT");
            string bundleRoot = Environment.GetEnvironmentVariable("CONTOUR4_WALL_BUNDLES");
            bool candidate = Environment.GetEnvironmentVariable("CONTOUR4_WALL_EXPECT_FINISH") == "1";
            string selection = Environment.GetEnvironmentVariable("CONTOUR4_WALL_ROOMS") ?? "both";
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            using var bundles = RoomPrefabBundle.Load(Path.Combine(bundleRoot, "contour4-rooms.bundle"),
                Path.Combine(bundleRoot, "contour4-prepared-modules.bundle"), requireWear: true);
            var observations = new List<string>();
            if (selection is "receiving" or "both")
                foreach (bool research in new[] { true, false })
                    foreach (bool authored in new[] { true, false })
                        Probe(FreightServiceRoom.Create("sample-receiving", research), bundles.Prefabs, authored,
                            $"receiving-{(research ? "research" : "plain")}-{(authored ? "prefab" : "fallback")}",
                            candidate, output, observations);
            if (selection is "workshop" or "both")
            {
                Probe(FreightServiceRoom.Create("instrument-service", false), bundles.Prefabs, false,
                    "workshop", candidate, output, observations);
                Probe(FreightServiceRoom.Create("instrument-service", false), bundles.Prefabs, false,
                    "workshop-sealed-supply", candidate, output, observations);
            }
            if (selection == "both")
            {
                Probe(FreightServiceRoom.Create("freight-dispatch", true), bundles.Prefabs, false,
                    "dispatch", candidate, output, observations);
                Probe(FreightServiceRoom.Create("sample-receiving", true), bundles.Prefabs, true,
                    "receiving-rotated", candidate, output, observations,
                    new Pose(new Vector3(12f, 7f, -16f), Quaternion.Euler(35f, 74f, -38f)));
            }
#if CONTOUR4_WORKROOM_TEST
            if (selection is "receiving" or "both")
                Probe(FreightServiceRoom.Create("sample-receiving", true), bundles.Prefabs, true,
                    "receiving-west-entry", candidate, output, observations);
            if (selection is "workshop" or "both")
                Probe(FreightServiceRoom.Create("instrument-service", false), bundles.Prefabs, false,
                    "workshop-north-entry", candidate, output, observations);
#endif
            File.WriteAllLines(Path.Combine(output, "room-contract.tsv"), observations);
            Debug.Log($"CONTOUR4 REFERENCE WALLS PASSED: {selection}, {observations.Count} observations.");
        }

        private static void Probe(GrayModule module, RaidRoomPrefabs prefabs, bool authored, string label,
            bool candidate, string output, List<string> observations, Pose? pose = null)
        {
            var host = new GameObject("Reference walls fixture");
            try
            {
                var raid = host.AddComponent<RaidAssembly>();
                var frame = pose ?? new Pose(Vector3.zero, Quaternion.identity);
                var plan = new RaidPlan.Room(module, frame, frame, 0);
                // The inactive freight shunts become part of the walls. The research portal
                // remains open so the adjacent paint and its return are visible.
                bool keepWest = label is "receiving-rotated" or "receiving-west-entry";
                bool keepSupply = label == "workshop-sealed-supply";
                foreach (Dock dock in module.Docks)
                {
                    if (label == "workshop-north-entry")
                    {
                        if (dock.Id == "spur") plan.OpenPlugs.Add(dock.PlugId);
                        continue;
                    }
                    if (dock.Id is not ("east" or "spur") && !(keepWest && dock.Id == "west") &&
                        !(keepSupply && dock.Id == "supply")) plan.OpenPlugs.Add(dock.PlugId);
                }
                RaidCatalogSites.Dress(module);
                RaidLootSites.Dress(module);
                var prefab = authored ? prefabs.For(plan) : null;
                var shader = Shader.Find("Standard");
                var (floor, wall) = ((Material, Material))Invoke(raid, "Finishes", shader);
                var shell = (Material)Invoke(raid, "Material", shader, new Color(.52f, .56f, .57f));
                var obstacles = (Material)Invoke(raid, "Material", shader, new Color(.39f, .33f, .25f));
                var sight = new RaidSightStrips(shader, Field<List<Material>>(raid, "materials"), Field<List<Mesh>>(raid, "meshes"));
                var fitout = (RaidInteriorFitout)Invoke(raid, "CreateFitout", shader);
                var room = (BakedRoom)Invoke(raid, "PlaceRoom", plan, 0, floor, wall, shell, obstacles, sight, prefab, fitout);
                Field<RoomBloodTraces>(raid, "bloodTraces").Apply(room);
                Require(room.Authored == authored, $"{label}: wrong prefab path.");

                bool corrected = candidate;
                var expected = module.Geometry.Where(block => block.Role == BlockRole.Shell && block.Id.Contains("-wall",
                    StringComparison.Ordinal)).Select(block => block.Id).Concat(module.Docks
                    .Where(dock => dock.Use == DockUse.Physical && !plan.OpenPlugs.Contains(dock.PlugId))
                    .Select(dock => dock.PlugId)).ToHashSet(StringComparer.Ordinal);
                var active = new HashSet<string>(StringComparer.Ordinal);
                var mats = new HashSet<Material>();
                var mpb = new MaterialPropertyBlock();
                foreach (var renderer in room.Root.GetComponentsInChildren<MeshRenderer>())
                {
                    string path = PathOf(room.Root, renderer.transform);
                    bool target = renderer.transform.parent == room.Root && expected.Contains(renderer.name);
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        bool painted = material && material.HasProperty("_WearKind") && Mathf.Approximately(material.GetFloat("_WearKind"), Finish);
                        Require(painted == (target && corrected), $"{label}/{path}: paint={painted}, expected={target && corrected}.");
                        if (painted)
                        {
                            Require(renderer.enabled && material.shader.isSupported && material.shader.name == "Contour4/Room surface wear",
                                $"{label}/{path}: wall finish shader unavailable.");
                            active.Add(renderer.name);
                            mats.Add(material);
                            renderer.GetPropertyBlock(mpb);
                            Matrix4x4 matrix = mpb.GetMatrix("_WearFrame");
                            Matrix4x4 want = room.Root.worldToLocalMatrix * renderer.localToWorldMatrix;
                            Require((matrix.GetColumn(3) - want.GetColumn(3)).sqrMagnitude < .000001f &&
                                (matrix.GetColumn(1) - want.GetColumn(1)).sqrMagnitude < .000001f,
                                $"{label}/{path}: wall paint coordinates are detached from the room.");
                        }
                    }
                    bool oldWallOverlay = expected.Contains(renderer.transform.parent.name) &&
                        renderer.name is "Inset cladding" or "Lower bulkhead rail";
                    if (oldWallOverlay)
                        Require(renderer.enabled != corrected, $"{label}/{path}: legacy cladding visibility.");
                    string[] materials = renderer.sharedMaterials.Select(m => m ? m.name : "(none)").ToArray();
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    observations.Add($"{label}\tRENDER\t{path}\t{renderer.enabled}\t{mesh?.name}\t{mesh?.vertexCount}\t{mesh?.subMeshCount}\t{string.Join(";", materials)}");
                }
                Require(!corrected || expected.SetEquals(active), $"{label}: some catalog walls have no finish.");
                Require(mats.Count <= 1, $"{label}: wall finish was duplicated per source material.");
                foreach (var collider in room.Root.GetComponentsInChildren<Collider>())
                {
                    Transform part = collider.transform;
                    Vector3 local = room.Root.InverseTransformPoint(part.position);
                    var shape = collider is BoxCollider box ? $"box:{V(box.center)}:{V(box.size)}" :
                        collider is MeshCollider solid ? $"mesh:{solid.sharedMesh?.name}:{solid.sharedMesh?.vertexCount}" : collider.GetType().Name;
                    observations.Add($"{label}\tCOLLIDER\t{PathOf(room.Root, part)}\t{collider.enabled}\t{collider.isTrigger}\t{part.gameObject.layer}\t{V(local)}\t{V(part.localScale)}\t{shape}");
                }
                foreach (var light in room.Root.GetComponentsInChildren<Light>())
                    observations.Add($"{label}\tLIGHT\t{PathOf(room.Root, light.transform)}\t{light.enabled}\t{light.type}\t{V(room.Root.InverseTransformPoint(light.transform.position))}\t{light.intensity:F3}\t{light.range:F3}\t{light.color}\t{light.shadows}");
                observations.Add($"{label}\tWALLS\t{string.Join(",", expected.OrderBy(name => name))}\t{active.Count}\t{mats.Count}");

#if CONTOUR4_WORKROOM_TEST
                if (label == "receiving-west-entry")
                {
                    Render(room.Root, new(.22f, 1.62f, 0), new(6.45f, 1.33f, .10f),
                        output, "receiving-door", 62, observations);
                    Render(room.Root, new(8.87f, 1.65f, -1.55f), new(8.22f, .91f, -3.86f),
                        output, "receiving-pack", 57, observations);
                    Render(room.Root, new(5.25f, 1.62f, .13f), new(8.34f, 1.24f, .10f),
                        output, "receiving-aisle", 94, observations);
                    Render(room.Root, new(5.77f, 1.65f, .97f), new(7.60f, 1.60f, 3.95f),
                        output, "receiving-sort", 57, observations);
                    return;
                }
                if (label == "workshop-north-entry")
                {
                    Render(room.Root, new(4.36f, 1.62f, 4.18f), new(2.00f, 1.35f, -3.50f),
                        output, "workshop-door", 56, observations);
                    Render(room.Root, new(2.81f, 1.64f, -.49f), new(1.46f, 1.11f, -3.30f),
                        output, "workshop-repair", 50, observations);
                    return;
                }
#endif
                if (label == "dispatch") return;
                var (eye, aim) = module.Id == "instrument-service"
                    ? (new Vector3(.48f, 1.63f, -.35f), new Vector3(5.4f, 1.51f, -4.05f))
                    : (new Vector3(2.64f, 1.63f, .42f), new Vector3(5.80f, 1.48f, 4.04f));
                Render(room.Root, eye, aim, output, label + "-room", 55, observations);
                if (label == "receiving-research-prefab" || label == "receiving-rotated")
                {
                    Render(room.Root, new Vector3(3.38f, 1.44f, 1.58f), new Vector3(2.85f, 1.33f, 4.12f),
                        output, label + "-jamb", 53, observations);
                    Render(room.Root, new Vector3(3.79f, 1.43f, 1.80f), new Vector3(6.05f, 1.43f, -4.12f),
                        output, label + "-south", 53, observations);
                }
                if (label == "receiving-research-prefab")
                    Render(room.Root, new Vector3(.10f, 1.65f, -.34f), new Vector3(4.9f, 1.49f, 3.55f),
                        output, label + "-entry", 57, observations);
                if (label == "workshop")
                    Render(room.Root, new Vector3(3.8f, 1.61f, 1.61f), new Vector3(7.82f, 1.53f, 3.98f),
                        output, label + "-north", 54, observations);
            }
            finally { Object.DestroyImmediate(host); }
        }

        internal static void Render(Transform room, Vector3 eye, Vector3 aim, string output, string label,
            float fov, List<string> observations)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.54f, .54f, .54f);
            RenderSettings.reflectionIntensity = 0;
            foreach (var light in room.GetComponentsInChildren<Light>()) light.enabled = false;
            var camera = new GameObject("Matched wall inspection camera").AddComponent<Camera>();
            var key = new GameObject("Matched neutral inspection light").AddComponent<Light>();
            var frame = RenderTexture.GetTemporary(1280, 800, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.transform.SetPositionAndRotation(room.TransformPoint(eye),
                    room.rotation * Quaternion.LookRotation(aim - eye, Vector3.up));
                camera.fieldOfView = fov;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.085f, .105f, .115f);
                camera.targetTexture = frame;
                key.type = LightType.Directional;
                key.intensity = .85f;
                key.transform.rotation = Quaternion.LookRotation(camera.transform.forward - room.up * .64f + camera.transform.right * .38f);
                Physics.SyncTransforms();
                foreach (Vector2 uv in new Vector2[] { new(.24f, .58f), new(.70f, .55f), new(.36f, .41f), new(.81f, .42f) })
                    Witness(room, camera.ViewportPointToRay(new Vector3(uv.x, uv.y)), label, uv, observations);
                camera.Render();
                RenderTexture.active = frame;
                image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(output, label + ".png"), image.EncodeToPNG());
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

        private static void Witness(Transform room, Ray ray, string label, Vector2 uv, List<string> observations)
        {
            if (!Physics.Raycast(ray, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore)) return;
            var face = hit.collider.GetComponent<MeshRenderer>();
            float nearest = hit.distance + .002f;
            foreach (Transform child in hit.transform)
            {
                if (child.name is not ("Inset cladding" or "Lower bulkhead rail") ||
                    !child.TryGetComponent(out MeshRenderer cladding) || !cladding.enabled ||
                    !cladding.bounds.IntersectRay(ray, out float distance) || distance >= nearest) continue;
                nearest = distance;
                face = cladding;
            }
            observations.Add($"{label}\tRAY\t{uv.x:F2},{uv.y:F2}\t{PathOf(room, hit.transform)}\t" +
                $"{(face ? PathOf(room, face.transform) : "no mesh renderer")}\t{face?.sharedMaterial?.name}");
        }

        private static string V(Vector3 p) => $"{p.x:F4},{p.y:F4},{p.z:F4}";
        private static string PathOf(Transform root, Transform part) => part == root ? root.name :
            (part.parent == root ? "" : PathOf(root, part.parent) + "/") + part.name + "[" + part.GetSiblingIndex() + "]";
        private static T Field<T>(RaidAssembly raid, string name) => (T)typeof(RaidAssembly).GetField(name, Private).GetValue(raid);
        private static object Invoke(RaidAssembly raid, string name, params object[] args) =>
            typeof(RaidAssembly).GetMethod(name, Private).Invoke(raid, args);
        private static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }
    }
}

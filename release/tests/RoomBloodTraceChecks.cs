using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SpaceJourney
{
    public static class RoomBloodTraceChecks
    {
        private static readonly int Count = Shader.PropertyToID("_BloodCount");
        private static readonly int Centres = Shader.PropertyToID("_BloodCentres");
        private static readonly int Horizontal = Shader.PropertyToID("_BloodHorizontal");
        private static readonly int Vertical = Shader.PropertyToID("_BloodVertical");
        private static readonly (int Progress, int Seed)[] Layouts = { (0, -32), (3, -32) };
        private static readonly (string Module, string Part, int[] Kinds)[] Expected =
        {
            ("service-cupboard", "far-wall", new[] { 0, 1 }),
            ("service-cupboard", "ground", new[] { 2, 3, 2, 3, 2 }),
            (MaintenanceTrench.Id, "near-wall", new[] { 1, 0 }),
            (MaintenanceTrench.Id, "west-deck-near", new[] { 4, 5, 4, 5 })
        };

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.075f, .080f, .088f);
            RenderSettings.reflectionIntensity = 0;
            string output = Get("CONTOUR4_TRACE_EVIDENCE");
            string prepared = Get("CONTOUR4_PREPARED_BUNDLE");
            var baseline = Capture(Get("CONTOUR4_BASELINE_ROOMS_BUNDLE"), prepared, output, false);
            var candidate = Capture(Get("CONTOUR4_ROOMS_BUNDLE"), prepared, output, true);
            Require(candidate.Scene.SequenceEqual(baseline.Scene), "changed collider, renderer, marker or light signature");
            var metrics = new List<string>();
            foreach (var (name, before) in baseline.Images)
            {
                Color32[] after = candidate.Images[name];
                int marked = 0;
                for (int i = 0; i < before.Length; i++)
                {
                    int red = after[i].r - before[i].r;
                    int green = after[i].g - before[i].g;
                    int blue = after[i].b - before[i].b;
                    if (2 * red - green - blue > 17 && green < -3) marked++;
                }
                Require(name == "control" ? marked < 6 : marked > 65, $"{name} has {marked} red-shifted pixels");
                metrics.Add($"{name}\t{marked}");
            }
            File.WriteAllLines(Path.Combine(output, "scene-signatures.tsv"), candidate.Scene);
            File.WriteAllLines(Path.Combine(output, "mark-supports.tsv"), candidate.Supports);
            File.WriteAllLines(Path.Combine(output, "render-metrics.tsv"), metrics);
            Debug.Log($"CONTOUR4 TRACES PASSED: 13 shaped marks on four supports, {candidate.Scene.Count} matching layouts, " +
                $"{candidate.Images.Count} paired renders. " + string.Join("; ", metrics));
        }

        private static Comparison Capture(string path, string prepared, string output, bool candidate)
        {
            var result = new Comparison();
            using var bundle = RoomPrefabBundle.Load(path, prepared, requireWear: true);
            var witnesses = new HashSet<string>();
            foreach (var (progress, seed) in Layouts)
            {
                var fixture = new GameObject("Trace witness");
                try
                {
                    RaidAssembly raid = fixture.AddComponent<RaidAssembly>();
                    raid.Place(seed, new Pose(new Vector3(11, 2, -18), Quaternion.Euler(0, 90, 0)), 0, 0,
                        catalogSites: true, completedLevels: progress, roomPrefabs: bundle.Prefabs);
                    result.Scene.Add($"{progress}\t{seed}\t{Signature(fixture.transform)}");
                    foreach (BakedRoom room in raid.Rooms)
                    {
                        Inspect(room, candidate, result);
                        string id = room.Plan.Module.Id;
                        if (!witnesses.Add(id)) continue;
                        switch (id)
                        {
                            case "service-cupboard":
                                Shot(room.Root, "hands-cupboard", new(5.25f, 1.47f, 2.02f), new(5.26f, 1.19f, 3.51f), 45f);
                                Shot(room.Root, "soles-cupboard", new(3.38f, 2.23f, 1.37f), new(3.41f, .04f, 1.86f), 62f);
                                break;
                            case MaintenanceTrench.Id:
                                Shot(room.Root, "hands-trench", new(4.62f, 1.44f, -5.46f), new(4.68f, .99f, -6.77f), 45f);
                                Shot(room.Root, "drag-trench", new(3.69f, 2.24f, -5.49f), new(3.69f, .04f, -6.08f), 64f);
                                break;
                            case "straight-corridor-short":
                                Shot(room.Root, "control", new(1.6f, 1.6f, -.46f), new(5.2f, 1.14f, .8f), 57f);
                                break;
                        }
                    }
                }
                finally { Object.DestroyImmediate(fixture); }
            }
            Require(result.Images.Count == 5,
                "natural layouts missed a witness: " + string.Join(",", result.Images.Keys));
            return result;

            void Shot(Transform room, string name, Vector3 eye, Vector3 target, float fieldOfView)
            {
                string label = candidate ? "after" : "before";
                result.Images[name] = Render(room, eye, target, fieldOfView, Path.Combine(output, $"{name}-{label}.png"));
            }
        }

        private static void Inspect(BakedRoom room, bool candidate, Comparison result)
        {
            string id = room.Plan.Module.Id;
            var expected = Expected.Where(entry => entry.Module == id).ToArray();
            var actual = new HashSet<string>();
            var block = new MaterialPropertyBlock();
            foreach (var renderer in room.Root.GetComponentsInChildren<MeshRenderer>())
            {
                renderer.GetPropertyBlock(block);
                int count = Mathf.RoundToInt(block.GetFloat(Count));
                if (count == 0) continue;
                var match = expected.SingleOrDefault(entry => entry.Part == renderer.name);
                Require(candidate && match.Kinds != null && count == match.Kinds.Length &&
                    renderer.TryGetComponent<Collider>(out _), $"{id}/{renderer.name} has unexpected marks ({count})");
                var support = renderer.GetComponent<Collider>();
                Vector4[] centres = block.GetVectorArray(Centres);
                Vector4[] horizontal = block.GetVectorArray(Horizontal);
                Vector4[] vertical = block.GetVectorArray(Vertical);
                for (int index = 0; index < count; index++)
                {
                    var u = (Vector3)horizontal[index];
                    var v = (Vector3)vertical[index];
                    Require(Mathf.RoundToInt(vertical[index].w) == match.Kinds[index] && centres[index].w > .25f,
                        $"{id}/{renderer.name} has the wrong mark at {index}");
                    Vector3 normal = room.Root.TransformDirection(Vector3.Cross(u, v).normalized);
                    Vector3 point = room.Root.TransformPoint(centres[index]);
                    Vector3 extentU = room.Root.TransformVector(u / u.sqrMagnitude * .5f);
                    Vector3 extentV = room.Root.TransformVector(v / v.sqrMagnitude * .5f);
                    if (match.Kinds[index] is 2 or 3)
                    {
                        var covering = Physics.OverlapBox(point + normal * .075f,
                            new Vector3(extentU.magnitude, .055f, extentV.magnitude),
                            Quaternion.LookRotation(extentV, normal), ~0, QueryTriggerInteraction.Ignore);
                        Require(covering.Length == 0,
                            $"{id}/{renderer.name}/{index} is covered by {string.Join(",", covering.Select(collider => collider.name))}");
                    }
                    foreach (var sample in new[] { point, point + extentU + extentV, point - extentU + extentV,
                        point + extentU - extentV, point - extentU - extentV })
                    {
                        Require(support.Raycast(new Ray(sample + normal * .045f, -normal), out var hit, .09f) &&
                            Mathf.Abs(hit.distance - .045f) <= .012f && Vector3.Dot(hit.normal, normal) >= .995f,
                            $"{id}/{renderer.name}/{index} footprint has no coplanar support");
                    }
                }
                actual.Add(renderer.name);
                result.Supports.Add($"{id}\t{renderer.name}\t{count}\t{string.Join(",", match.Kinds)}");
            }
            if (candidate)
                Require(actual.SetEquals(expected.Select(entry => entry.Part)),
                    $"{id} support coverage: {string.Join(",", actual)} instead of {string.Join(",", expected.Select(e => e.Part))}");
        }

        private static string Signature(Transform root)
        {
            var records = root.GetComponentsInChildren<Collider>(true).Select(collider =>
                $"{PathOf(collider.transform, root)}|{collider.GetType().Name}|{collider.gameObject.activeSelf}|{collider.enabled}|" +
                $"{collider.isTrigger}|{collider.gameObject.layer}|{V(collider.bounds.center)}|{V(collider.bounds.size)}")
                .Concat(root.GetComponentsInChildren<Light>(true).Select(light =>
                    $"{PathOf(light.transform, root)}|{light.type}|{light.enabled}|{V(light.transform.position)}|{V(light.transform.forward)}|" +
                    $"{light.intensity:R}|{light.range:R}|{light.innerSpotAngle:R}|{light.spotAngle:R}|{light.shadows}|{light.color}"))
                .Concat(root.GetComponentsInChildren<MeshRenderer>(true).Select(renderer =>
                    $"{PathOf(renderer.transform, root)}|{renderer.enabled}|{renderer.shadowCastingMode}|{renderer.sharedMaterials.Length}"))
                .Concat(root.GetComponentsInChildren<Transform>(true).Where(t =>
                    t.parent && t.parent.name is "Docks" or "Floor frames" or "Loot candidates")
                    .Select(t => $"{PathOf(t, root)}|{V(t.position)}|{V(t.forward)}|{V(t.up)}"));
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", records)))).Replace("-", "");
        }

        private static Color32[] Render(Transform room, Vector3 eye, Vector3 target, float fieldOfView, string path)
        {
            const int width = 1000, height = 650;
            var node = new GameObject("Trace witness camera");
            var camera = node.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(room.TransformPoint(eye),
                Quaternion.LookRotation(room.TransformPoint(target) - room.TransformPoint(eye), room.up));
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 28f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.034f, .04f, .044f);
            var lamp = node.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.spotAngle = 86f;
            lamp.innerSpotAngle = 61f;
            lamp.range = 7.5f;
            lamp.intensity = 2.45f;
            lamp.color = new Color(.97f, .94f, .90f);
            lamp.renderMode = LightRenderMode.ForcePixel;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var capture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                capture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                capture.Apply();
                File.WriteAllBytes(path, capture.EncodeToPNG());
                return capture.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(capture);
                Object.DestroyImmediate(node);
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static string Get(string name) => Environment.GetEnvironmentVariable(name) ??
            throw new InvalidOperationException(name);
        private static string PathOf(Transform node, Transform root) =>
            node == root ? "" : PathOf(node.parent, root) + "/" + node.name;
        private static string V(Vector3 value) => string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}", value.x, value.y, value.z);
        private static void Require(bool condition, string error)
        {
            if (!condition) throw new InvalidOperationException("CONTOUR4 TRACES: " + error);
        }

        private sealed class Comparison
        {
            internal readonly Dictionary<string, Color32[]> Images = new();
            internal readonly List<string> Scene = new();
            internal readonly List<string> Supports = new();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Contour4.NativeRaid.Runtime;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SpaceJourney
{
    public static class RoomBrokenEquipmentChecks
    {
        private const string Instrument = "instrument-service-inspection-bench-instrument";
        private const string Cabinet = "Ladder maintenance cabinet";
        private const string Interior = "Exposed technical components";
        private static readonly int BloodCount = Shader.PropertyToID("_BloodCount");
        private static readonly (int Progress, int Seed)[] Layouts = { (0, -32), (3, -31) };
        private static readonly (string Id, string Room, string Part, Vector3 Eye, Vector3 Look, float Fov)[] Witnesses =
        {
            ("bench", "instrument-service", Instrument, new(1.64f,1.56f,-2.66f), new(1.74f,1.29f,-3.71f), 39f),
            ("cabinet", "shaft-plant", Cabinet, new(10.24f,1.59f,-1.55f), new(10.82f,.72f,-3.25f), 47f)
        };

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.074f,.080f,.089f);
            RenderSettings.reflectionIntensity = 0;
            string output = Get("CONTOUR4_DAMAGE_EVIDENCE");
            bool candidate = Get("CONTOUR4_DAMAGE_CANDIDATE") == "1";
            using var bundle = RoomPrefabBundle.Load(Get("CONTOUR4_ROOMS_BUNDLE"), Get("CONTOUR4_PREPARED_BUNDLE"), requireWear: true);
            var signatures = new List<string>();
            var models = new List<string>();
            var photographed = new HashSet<string>();
            foreach (var (progress, seed) in Layouts)
            {
                var root = new GameObject("Equipment inspection");
                try
                {
                    RaidAssembly raid = root.AddComponent<RaidAssembly>();
                    raid.Place(seed, new Pose(new Vector3(11,2,-18), Quaternion.Euler(0,90,0)), 0, 0,
                        catalogSites: true, completedLevels: progress, roomPrefabs: bundle.Prefabs);
                    signatures.Add($"{progress}\t{seed}\t{Signature(root.transform)}");
                    foreach (BakedRoom room in raid.Rooms)
                    {
                        var match = Witnesses.SingleOrDefault(part => part.Room == room.Plan.Module.Id);
                        int modelsHere = room.Root.GetComponentsInChildren<MeshRenderer>().Count(renderer => renderer.name.EndsWith(Interior));
                        Require(modelsHere == (match.Id != null && candidate ? 1 : 0),
                            $"unexpected model count {modelsHere} in {room.Plan.Module.Id}");
                        if (match.Id == null) continue;
                        models.Add(Verify(room, match.Id, match.Part, candidate));
                        if (!photographed.Add(match.Id)) continue;
                        Color32[] first = Render(room.Root, match.Eye, match.Look, match.Fov,
                            Path.Combine(output, match.Id + ".png"));
                        if (candidate)
                        {
                            Vector3 old = root.transform.position;
                            root.transform.position += new Vector3(64,-16,48);
                            try
                            {
                                var second = Render(room.Root, match.Eye, match.Look, match.Fov,
                                    Path.Combine(output, match.Id + "-rebased.png"));
                                double drift = first.Zip(second, (a,b) =>
                                    (Math.Abs(a.r-b.r) + Math.Abs(a.g-b.g) + Math.Abs(a.b-b.b)) / 3d).Average();
                                Require(drift < .62, $"{match.Id} differs after rebase by {drift:F4}/255");
                                models.Add($"{match.Id}\trebase\t{drift:F6}");
                            }
                            finally { root.transform.position = old; }
                        }
                    }
                }
                finally { Object.DestroyImmediate(root); }
            }
            Require(photographed.Count == Witnesses.Length, "one of the engine witnesses is absent");
            File.WriteAllLines(Path.Combine(output,"scene-signatures.tsv"), signatures);
            File.WriteAllLines(Path.Combine(output,"model-witnesses.tsv"), models);
            Debug.Log($"CONTOUR4 BROKEN EQUIPMENT PASSED {candidate}: {photographed.Count} objects in {signatures.Count} exact layouts.");
        }

        private static string Verify(BakedRoom room, string id, string part, bool candidate)
        {
            var collider = room.Colliders.Single(c => c.name == part);
            var body = collider.GetComponent<MeshFilter>();
            Bounds old = collider switch
            {
                BoxCollider box => new Bounds(box.center, box.size),
                MeshCollider mesh => mesh.sharedMesh.bounds,
                _ => throw new InvalidOperationException(part + " has no box-like support")
            };
            Mesh model = body.sharedMesh;
            Vector3 cast = old.center + new Vector3(old.extents.x*.10f,
                id == "cabinet" ? -old.extents.y*.32f : old.extents.y*.23f, old.extents.z + .30f);
            float hit = FirstDepth(model, cast);
            Require(candidate
                ? hit > .30f + old.size.z * .40f && !float.IsPositiveInfinity(hit)
                : Mathf.Abs(hit - .30f) < .004f, $"{part} visual gap or depth is invalid: {hit}");
            if (!candidate) return $"{id}\t{model.name}\t{hit:F4}";
            MeshRenderer renderer = room.Root.GetComponentsInChildren<MeshRenderer>().Single(r => r.name.EndsWith(Interior));
            MeshFilter opened = renderer.GetComponent<MeshFilter>();
            Matrix4x4 toBody = body.transform.worldToLocalMatrix * opened.transform.localToWorldMatrix;
            Bounds clearance = old;
            clearance.Expand(.009f);
            Require(!renderer.GetComponent<Collider>() && collider.enabled &&
                model.vertices.All(clearance.Contains) &&
                opened.sharedMesh.vertices.Select(toBody.MultiplyPoint3x4).All(clearance.Contains),
                $"{part} has any component extending beyond the original collision envelope");
            string[] names = renderer.sharedMaterials.Select(m => m.name).ToArray();
            Require(names.Any(name => name.EndsWith("Green circuit board")) &&
                names.Any(name => name.EndsWith("Darkened copper")) &&
                names.Any(name => name.EndsWith("Terminal ceramic")) &&
                names.Any(name => name.EndsWith("Slate blue insulation")) &&
                renderer.sharedMaterials.All(m => !m.IsKeywordEnabled("_EMISSION")),
                $"{part} is missing component groups or emits light: {string.Join(", ", names)}");
            return $"{id}\t{model.name}\t{hit:F4}\t{opened.sharedMesh.triangles.Length/3}\t{string.Join(",",names)}";
        }

        private static float FirstDepth(Mesh mesh, Vector3 origin)
        {
            float nearest = float.PositiveInfinity;
            int[] triangles = mesh.triangles;
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], ab = vertices[triangles[i+1]] - a, ac = vertices[triangles[i+2]] - a;
                Vector3 direction = Vector3.back;
                Vector3 p = Vector3.Cross(direction, ac);
                float det = Vector3.Dot(ab,p);
                if (Mathf.Abs(det) < .0000001f) continue;
                Vector3 offset = origin - a;
                float u = Vector3.Dot(offset,p) / det;
                if (u < 0 || u > 1) continue;
                Vector3 q = Vector3.Cross(offset,ab);
                float v = Vector3.Dot(direction,q) / det;
                if (v < 0 || u+v > 1) continue;
                float distance = Vector3.Dot(ac,q) / det;
                if (distance >= 0) nearest = Mathf.Min(nearest,distance);
            }
            return nearest;
        }

        private static string Signature(Transform root)
        {
            var records = root.GetComponentsInChildren<Collider>(true).Select(collider =>
                $"{PathOf(collider.transform,root)}|{collider.GetType().Name}|{collider.gameObject.activeSelf}|{collider.enabled}|" +
                $"{collider.isTrigger}|{collider.gameObject.layer}|{V(collider.bounds.center)}|{V(collider.bounds.size)}")
                .Concat(root.GetComponentsInChildren<Light>(true).Select(light =>
                    $"{PathOf(light.transform,root)}|{light.type}|{light.enabled}|{V(light.transform.position)}|{V(light.transform.forward)}|" +
                    $"{light.intensity:R}|{light.range:R}|{light.innerSpotAngle:R}|{light.spotAngle:R}|{light.shadows}|{light.color}"))
                .Concat(root.GetComponentsInChildren<Transform>(true).Where(t =>
                    t.parent && t.parent.name is "Docks" or "Floor frames" or "Loot candidates" or "Haul routes")
                    .Select(t => $"{PathOf(t,root)}|{V(t.position)}|{V(t.forward)}|{V(t.up)}"));
            var block = new MaterialPropertyBlock();
            var blood = root.GetComponentsInChildren<MeshRenderer>(true).Select(renderer =>
            {
                renderer.GetPropertyBlock(block);
                return (Path: PathOf(renderer.transform,root), Count: block.GetFloat(BloodCount));
            }).Where(entry => entry.Count > 0).Select(entry => $"{entry.Path}|{entry.Count:R}");
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", records.Concat(blood))))).Replace("-","");
        }

        private static Color32[] Render(Transform room, Vector3 eye, Vector3 target, float fov, string path)
        {
            const int width = 1080, height = 720;
            var node = new GameObject("Equipment witness camera");
            var camera = node.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(room.TransformPoint(eye),
                Quaternion.LookRotation(room.TransformPoint(target) - room.TransformPoint(eye), room.up));
            camera.fieldOfView = fov;
            camera.nearClipPlane = .04f;
            camera.farClipPlane = 38f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.034f,.04f,.044f);
            var lamp = node.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.spotAngle = 79f;
            lamp.innerSpotAngle = 53f;
            lamp.range = 7.3f;
            lamp.intensity = 2.15f;
            lamp.color = new Color(.95f,.94f,.89f);
            lamp.renderMode = LightRenderMode.ForcePixel;
            var rt = RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
            var capture = new Texture2D(width,height,TextureFormat.RGB24,false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                capture.ReadPixels(new Rect(0,0,width,height),0,0);
                capture.Apply();
                File.WriteAllBytes(path,capture.EncodeToPNG());
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

        private static string Get(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException(name);
        private static string PathOf(Transform node, Transform root) => node == root ? "" : PathOf(node.parent,root) + "/" + node.name;
        private static string V(Vector3 value) => string.Format(CultureInfo.InvariantCulture,"{0:R},{1:R},{2:R}",value.x,value.y,value.z);
        private static void Require(bool check,string message)
        {
            if (!check) throw new InvalidOperationException("CONTOUR4 BROKEN EQUIPMENT: " + message);
        }
    }
}

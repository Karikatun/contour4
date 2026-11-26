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
    public static class ReadableMaterialChecks
    {
        private static readonly int ContactCount = Shader.PropertyToID("_BloodCount");
        private static readonly (int Progress, int Seed)[] Layouts = { (0, -32), (3, -31), (5, -22) };
        private static readonly (string Id, string Room, Vector3 Eye, Vector3 Target, float Fov)[] Views =
        {
            ("workshop", "instrument-service", new(.65f,1.49f,-1.93f), new(1.67f,1.09f,-3.63f), 54),
            ("shaft-floor", "shaft-plant", new(3.1f,1.62f,-.50f), new(10.95f,.36f,2.22f), 61),
            ("shaft-steel", "shaft-plant", new(14.45f,1.63f,1.86f), new(17.31f,.94f,5.69f), 55),
            ("receiving", "sample-receiving", new(1.80f,1.64f,-.8f), new(6.72f,1.24f,2.22f), 60),
            ("passage", "straight-corridor-short", new(1.12f,1.56f,-.54f), new(5.65f,.75f,.52f), 58),
            ("sphere", SphericalChamber.Id, new(-7.4f,1.65f,.61f), new(2.4f,-7.0f,12.4f), 65)
        };

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.075f,.08f,.089f);
            RenderSettings.reflectionIntensity = 0;
            string output = Get("CONTOUR4_SURFACE_EVIDENCE");
            bool candidate = Get("CONTOUR4_SURFACE_CANDIDATE") == "1";
            using var bundle = RoomPrefabBundle.Load(Get("CONTOUR4_ROOMS_BUNDLE"), Get("CONTOUR4_PREPARED_BUNDLE"), requireWear: true);
            var captured = new HashSet<string>();
            var signatures = new List<string>();
            var inventory = new List<string>();
            foreach (var (progress, seed) in Layouts)
            {
                var root = new GameObject("Material room witness");
                try
                {
                    var raid = root.AddComponent<RaidAssembly>();
                    raid.Place(seed, new Pose(new Vector3(11,2,-18), Quaternion.Euler(0,90,0)), 0, 0,
                        catalogSites: true, completedLevels: progress, roomPrefabs: bundle.Prefabs);
                    signatures.Add($"{progress}\t{seed}\t{Physical(root.transform)}");
                    foreach (BakedRoom room in raid.Rooms)
                    {
                        CheckMaterials(room, candidate, inventory);
                        string viewModule = SphericalChamber.IsVariant(room.Plan.Module.Id) ?
                            SphericalChamber.Id : room.Plan.Module.Id;
                        foreach (var view in Views.Where(view => view.Room == viewModule))
                        {
                            if (!captured.Add(view.Id)) continue;
                            Render(room.Root, view.Eye, view.Target, view.Fov, Path.Combine(output, view.Id + ".png"));
                        }
                    }
                }
                finally { Object.DestroyImmediate(root); }
            }
            Require(captured.Count == Views.Length, "missing planned camera witnesses");
            File.WriteAllLines(Path.Combine(output,"scene-signatures.tsv"), signatures);
            File.WriteAllLines(Path.Combine(output,"material-inventory.tsv"), inventory);
            Debug.Log($"CONTOUR4 READABLE MATERIALS PASSED {candidate}: {captured.Count} authored-light cameras in {signatures.Count} exact layouts.");
        }

        private static void CheckMaterials(BakedRoom room, bool candidate, List<string> inventory)
        {
            string id = room.Plan.Module.Id;
            var renderers = room.Root.GetComponentsInChildren<MeshRenderer>();
            foreach (var renderer in renderers.Where(renderer =>
                renderer.name.EndsWith(" / Exposed technical components", StringComparison.Ordinal)))
                Require(renderer.sharedMaterials.All(material => !material || material.shader.name != RoomSurfaceWear.FinishName),
                    $"{id}/{renderer.name} received the generic material");
            foreach (var renderer in renderers.Where(renderer => renderer.name == "Institute surface details"))
                Require(renderer.sharedMaterials.Where(material => material &&
                    (material.name.Contains("ivory stencil") || material.name.Contains("warning ochre") ||
                     material.name.Contains("opal lens"))).All(material => material.shader.name != RoomSurfaceWear.FinishName),
                    $"{id}/{renderer.name} recolored a marking or light");
            if (id == "instrument-service")
            {
                RequireRole("instrument-service-inspection-bench-top", candidate ? 4 : 3);
                RequireRole("instrument-service-inspection-bench-instrument", 3);
                RequireRole("instrument-service-inspection-bench-screen", 0);
                RequireRole("ground", 1);
                var ply = renderers.Where(renderer => renderer.transform.parent.name == "Instrument repair bench" &&
                    renderer.name == "WoodEdge").ToArray();
                Require(ply.Length == 1 && Kind(ply[0].sharedMaterial) == (candidate ? 5 : 0), "pegboard ply finish");
            }
            else if (id == "shaft-plant")
            {
                RequireRole("near-wall",2);
                RequireRole("ground",1);
                RequireRole("Ladder maintenance cabinet",3);
            }
            else if (id == "straight-corridor-short")
                Require(renderers.Any(renderer => Kind(renderer.sharedMaterial)==1) &&
                    renderers.Any(renderer => Kind(renderer.sharedMaterial)==2), "corridor lacks floor/wall families");
            else if (SphericalChamber.IsVariant(id))
                Require(renderers.Any(renderer => renderer.name.StartsWith(SphericalChamber.Shell, StringComparison.Ordinal) &&
                    Kind(renderer.sharedMaterial) == 2), "the smoothed spherical shell lacks the plaster family");

            var counts = renderers.SelectMany(renderer => renderer.sharedMaterials)
                .GroupBy(Kind).OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}={entry.Count()}");
            inventory.Add($"{id}\t{string.Join(",", counts)}");

            void RequireRole(string part, int kind)
            {
                var target = renderers.SingleOrDefault(renderer => renderer.name == part);
                int actual = target ? Kind(target.sharedMaterial) : -1;
                Require(actual == kind, $"{id}/{part}: expected finish {kind}, got {actual}");
                inventory.Add($"{id}/{part}\t{actual}\t{target.sharedMaterial.name}");
            }
        }

        private static int Kind(Material material) =>
            material && material.shader.name == RoomSurfaceWear.FinishName ? Mathf.RoundToInt(material.GetFloat("_WearKind")) : 0;

        private static string Physical(Transform root)
        {
            var components = root.GetComponentsInChildren<Collider>(true).Select(collider =>
                $"{PathOf(collider.transform,root)}|{collider.GetType().Name}|{collider.gameObject.activeSelf}|{collider.enabled}|" +
                $"{collider.isTrigger}|{collider.gameObject.layer}|{V(collider.bounds.center)}|{V(collider.bounds.size)}")
                .Concat(root.GetComponentsInChildren<Light>(true).Select(light =>
                    $"{PathOf(light.transform,root)}|{light.type}|{light.enabled}|{V(light.transform.position)}|{V(light.transform.forward)}|" +
                    $"{light.intensity:R}|{light.range:R}|{light.innerSpotAngle:R}|{light.spotAngle:R}|{light.shadows}|{light.color}"))
                .Concat(root.GetComponentsInChildren<Transform>(true).Where(t =>
                    t.parent && t.parent.name is "Docks" or "Floor frames" or "Loot candidates" or "Haul routes")
                    .Select(t => $"{PathOf(t,root)}|{V(t.position)}|{V(t.forward)}|{V(t.up)}"));
            var block = new MaterialPropertyBlock();
            var protectedRenderers = root.GetComponentsInChildren<MeshRenderer>(true).Select(renderer =>
            {
                renderer.GetPropertyBlock(block);
                string suffix = renderer.name.EndsWith(" / Exposed technical components", StringComparison.Ordinal)
                    ? string.Join(",", renderer.sharedMaterials.Select(material => material.name)) : "";
                return $"{PathOf(renderer.transform,root)}|{renderer.enabled}|{renderer.shadowCastingMode}|{block.GetFloat(ContactCount):R}|{suffix}";
            });
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", components.Concat(protectedRenderers))))).Replace("-","");
        }

        private static void Render(Transform room, Vector3 eye, Vector3 target, float fov, string path)
        {
            const int width = 1080, height = 720;
            var node = new GameObject("Authored light material witness");
            var camera = node.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(room.TransformPoint(eye),
                Quaternion.LookRotation(room.TransformPoint(target) - room.TransformPoint(eye), room.up));
            camera.fieldOfView = fov;
            camera.nearClipPlane = .04f;
            camera.farClipPlane = 42f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.034f,.040f,.044f);
            var frame = RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
            var image = new Texture2D(width,height,TextureFormat.RGB24,false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = frame;
                camera.Render();
                RenderTexture.active = frame;
                image.ReadPixels(new Rect(0,0,width,height),0,0);
                image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(node);
                RenderTexture.ReleaseTemporary(frame);
            }
        }

        private static string Get(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException(name);
        private static string PathOf(Transform node, Transform root) => node == root ? "" : PathOf(node.parent,root) + "/" + node.name;
        private static string V(Vector3 value) => string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}",value.x,value.y,value.z);
        private static void Require(bool check,string error)
        {
            if (!check) throw new InvalidOperationException("CONTOUR4 READABLE MATERIALS: " + error);
        }
    }
}

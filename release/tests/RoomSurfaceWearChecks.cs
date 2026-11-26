using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Contour4.NativeRaid;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace SpaceJourney
{
    public static class RoomSurfaceWearChecks
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly Pose Origin = new(Vector3.zero, Quaternion.identity);
        private static readonly (int Progress, int Seed)[] Layouts =
        {
            (0, -32), (0, -31), (0, 0), (0, 7), (0, 8), (0, 42),
            (3, -32), (3, -31), (3, -30), (3, -20), (3, -19), (3, 42),
            (5, -32), (5, -31), (5, -30), (5, -29), (5, -27), (5, -26), (5, -25), (5, -22),
            (5, 12), (5, 17), (5, -63), (5, -60), (5, -59), (5, -56), (5, -62), (5, -47),
            (14, 0), (14, -59), (14, -58), (14, -57), (14, -16), (14, 1), (14, 7), (14, 42)
        };

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.075f, .080f, .088f);
            RenderSettings.reflectionIntensity = 0;
            string output = PathValue("CONTOUR4_WEAR_EVIDENCE");
            string prepared = PathValue("CONTOUR4_PREPARED_BUNDLE");
            string candidate = PathValue("CONTOUR4_ROOMS_BUNDLE");
            string baseline = PathValue("CONTOUR4_BASELINE_ROOMS_BUNDLE");
            Directory.CreateDirectory(output);
            var before = Capture(baseline, prepared, output, false);
            var after = Capture(candidate, prepared, output, true);
            Require(before.Signatures.SequenceEqual(after.Signatures), "physical/lighting signatures differ");
            Require(after.PatchedRooms >= before.PreparedRooms && after.StartBays == 2 && after.Webs >= 3 &&
                after.PatchedFurniture > 0 && before.PatchedRooms == 0, "wear coverage is incomplete");
            File.WriteAllLines(Path.Combine(output, "physical-parity.tsv"), after.Signatures);
            File.WriteAllLines(Path.Combine(output, "material-inventory.tsv"), after.Materials);
            Debug.Log($"CONTOUR4 WEAR PASSED: {after.Signatures.Count} identical collider/marker/light signatures, " +
                $"{before.PreparedRooms} prepared fixtures, {after.Layouts} layouts, {after.LayoutRooms} placed rooms, " +
                $"{after.PatchedFurniture} furniture/fixture renderers in prepared fixtures, {after.StartBays} mod-authored starts.");
        }

        private static Result Capture(string rooms, string prepared, string output, bool candidate)
        {
            var result = new Result();
            string label = candidate ? "after" : "before";
            using var bundle = RoomPrefabBundle.Load(rooms, prepared, requireWear: candidate);
            foreach (var module in ModuleCatalog.Rooms())
            {
                using var fixture = new Fixture();
                var plan = new RaidPlan.Room(module, Origin, Origin, 0);
                Require(bundle.Prefabs.TryGetPrepared(plan, out var prefab), "missing exact " + module.Id);
                var room = fixture.Raid.PlacePrepared(plan, 0, prefab);
                ReadFixture(room.Root, module.Id, result, candidate, room);
                result.PreparedRooms++;
                switch (module.Id)
                {
                    case "shaft-plant":
                        Shot(room.Root, "shaft", new(2.6f, 1.70f, -.90f), new(13.4f, 2.55f, 2.5f));
                        Shot(room.Root, "shaft-oil", new(8.7f, 1.65f, .50f), new(11.1f, .20f, 3.24f), torch: true);
                        break;
                    case "service-cupboard":
                        Shot(room.Root, "cupboard", new(1.1f, 1.70f, -.95f), new(5.36f, 1.3f, 2.7f));
                        Shot(room.Root, "cupboard-web", new(5.05f, 2.31f, 1.8f), new(7.32f, 2.60f, 3.58f), torch: true);
                        break;
                    case "observation-lab":
                        Shot(room.Root, "observation", new(1.55f, 1.65f, -.85f), new(8f, 1.55f, 2.2f));
                        break;
                }
            }
            foreach (bool service in new[] { false, true })
            {
                using var fixture = new Fixture();
                string id = service ? "service-garage" : "pressure-dock";
                var bay = fixture.Root.AddComponent<NativeRaidStartBay>();
                typeof(NativeRaidStartBay).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(bay, new object[] { service, 4.6f, 3.6f });
                ReadFixture(fixture.Root.transform, id, result, candidate);
                var camera = new Vector3(-.74f, 1.64f, -8.32f);
                var target = new Vector3(5.15f, 1.28f, -4.36f);
                Shot(fixture.Root.transform, id, camera, target);
                if (candidate && service)
                {
                    byte[] original = Shot(fixture.Root.transform, "garage-detail", new(3.20f, 1.57f, -7.86f),
                        new(6.36f, 1.12f, -5.55f));
                    fixture.Root.transform.position += new Vector3(64, -16, 48);
                    byte[] shifted = Shot(fixture.Root.transform, "garage-detail-rebased", new(3.20f, 1.57f, -7.86f),
                        new(6.36f, 1.12f, -5.55f));
                    double imageDifference = original.Zip(shifted, (a, b) => Math.Abs(a - b)).Average();
                    Require(imageDifference < 1.5, $"rebased wear shifted or changed ({imageDifference:F3}/255 per channel)");
                    Debug.Log($"CONTOUR4 WEAR REBASE mean channel difference {imageDifference:F4}/255.");
                }
                else if (service)
                    Shot(fixture.Root.transform, "garage-detail", new(3.20f, 1.57f, -7.86f), new(6.36f, 1.12f, -5.55f));
                result.StartBays++;
            }
            InspectRing(result, candidate);

            var timer = Stopwatch.StartNew();
            bool receivingCaptured = false;
            int limit = int.TryParse(Environment.GetEnvironmentVariable("CONTOUR4_WEAR_LAYOUTS"), out int value) ?
                Mathf.Clamp(value, 1, Layouts.Length) : Layouts.Length;
            foreach (var (progress, seed) in Layouts.Take(limit))
            {
                using var fixture = new Fixture();
                fixture.Raid.Place(seed, Origin, 0, 0, catalogSites: true, completedLevels: progress, roomPrefabs: bundle.Prefabs);
                var placed = fixture.Raid.Rooms.ToArray();
                result.Layouts++;
                result.LayoutRooms += placed.Length;
                string id = $"layout-{progress}-{seed}";
                ReadFixture(fixture.Root.transform, id, result, candidate);
                result.Signatures.Add(id + "\trooms\t" + string.Join(",", placed.Select(room => room.Plan.Module.Id)));
                var receiving = placed.FirstOrDefault(room => room.Plan.Module.Id == "sample-receiving");
                if (receiving == null || receivingCaptured) continue;
                // Keep the exact isolated placed room and its attached fixture lights in an open test scene.
                receiving.Root.SetParent(null, true);
                try
                {
                    fixture.Root.SetActive(false);
                    Shot(receiving.Root, "receiving", new(1.8f, 1.64f, -.8f), new(7.8f, 1.52f, 3.1f));
                    receivingCaptured = true;
                }
                finally { Object.DestroyImmediate(receiving.Root.gameObject); }
            }
            Debug.Log($"CONTOUR4 WEAR {label}: {result.LayoutRooms} placed in {timer.Elapsed.TotalSeconds:F2}s ({result.Layouts} offline layouts).");
            Require(receivingCaptured, "natural layouts did not supply an authored receiving room");
            return result;

            byte[] Shot(Transform room, string name, Vector3 eye, Vector3 look, bool torch = false) =>
                Render(room, eye, look, Path.Combine(output, $"{name}-{label}.png"), torch);
        }

        private static void InspectRing(Result result, bool candidate)
        {
            var ringBundle = AssetBundle.LoadFromFile(PathValue("CONTOUR4_RING_BUNDLE"));
            Require(ringBundle, "exact Ring bundle unavailable");
            var instance = Object.Instantiate(ringBundle.LoadAsset<GameObject>("Assets/Contour4Ring/Contour4 Ring.prefab"));
            var details = new RaidInteriorDetails();
            try
            {
                var ring = instance.transform;
                var concrete = ring.Find("Quota/Model/Quota/Quota__Geometry/Quota__Floor").GetComponent<MeshRenderer>().sharedMaterial;
                ring.Find("Loot2/Catalog sites/Upper gallery deck").GetComponent<MeshRenderer>().sharedMaterial = concrete;
                foreach (string name in new[] { "Quota", "Loot1", "Loot2", "Loot3" })
                {
                    Transform room = ring.Find(name);
                    details.RingRoom(room);
                    ReadFixture(room, "ring-" + name, result, candidate);
                }
                foreach (string name in new[] { "BridgeQuotaLoot1", "BridgeLoot1Loot2" })
                {
                    Transform bridge = ring.Find(name);
                    bridge.Find($"Model/{name}/{name}__Geometry/{name}__Walkable_Deck")
                        .GetComponent<MeshRenderer>().sharedMaterial = concrete;
                    details.RingBridge(bridge);
                    ReadFixture(bridge, "ring-" + name, result, candidate);
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
                details.Release();
                ringBundle.Unload(true);
            }
        }

        private static void ReadFixture(Transform root, string id, Result result, bool candidate, BakedRoom room = null)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            var lights = root.GetComponentsInChildren<Light>(true);
            string state = string.Join("\n", colliders.Select(collider =>
                $"{Hierarchy(collider.transform, root)}|{collider.GetType().Name}|{collider.gameObject.activeSelf}|" +
                $"{collider.enabled}|{collider.isTrigger}|{collider.gameObject.layer}|{V(collider.bounds.center)}|{V(collider.bounds.size)}")
                .Concat(lights.Select(light => $"{Hierarchy(light.transform, root)}|{light.type}|{light.enabled}|{V(light.transform.position)}|" +
                    $"{V(light.transform.forward)}|{light.intensity:R}|{light.range:R}|{light.innerSpotAngle:R}|{light.spotAngle:R}|{light.shadows}|{light.color}"))
                .Concat(root.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.parent && t.parent.name is "Docks" or "Floor frames" or "Loot candidates")
                    .Select(t => $"{Hierarchy(t, root)}|{V(t.position)}|{V(t.forward)}|{V(t.up)}")));
            using var sha = SHA256.Create();
            var digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(state))).Replace("-", "");
            result.Signatures.Add($"{id}\t{colliders.Length}\t{lights.Length}\t{digest}");
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            var wearing = renderers.Where(renderer => renderer.sharedMaterials.Any(material =>
                material && material.shader.name == RoomSurfaceWear.FinishName)).ToArray();
            var webs = renderers.Where(renderer => renderer.name == RoomSurfaceWear.WebName).ToArray();
            Require(webs.All(renderer => !renderer.GetComponent<Collider>() && renderer.shadowCastingMode == ShadowCastingMode.Off) &&
                wearing.All(renderer => renderer.name != RaidInteriorDetails.ObjectName), id + " changed guide graphics or web collides");
            bool trackRoom = room != null;
            if (candidate && (trackRoom || !id.StartsWith("layout-", StringComparison.Ordinal)))
                Require(wearing.Length > 0, id + " lacks wear");
            if (trackRoom && wearing.Length > 0)
            {
                result.PatchedRooms++;
                result.PatchedFurniture += wearing.Count(renderer =>
                    renderer.sharedMaterials.Any(material => material && material.HasProperty("_WearKind") &&
                        Mathf.Approximately(material.GetFloat("_WearKind"), 3)));
            }
            result.Webs += webs.Length;
            result.Materials.Add($"{id}\t{renderers.Length}\t{wearing.Length}\t{webs.Length}\t" +
                string.Join(",", wearing.Select(renderer => renderer.name).Take(100)));
        }

        private static byte[] Render(Transform room, Vector3 eye, Vector3 target, string path, bool torch)
        {
            const int width = 1000, height = 650;
            var cameraObject = new GameObject("Wear witness camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(room.TransformPoint(eye),
                Quaternion.LookRotation(room.TransformPoint(target) - room.TransformPoint(eye), room.up));
            camera.fieldOfView = 64f;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 95f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.034f, .040f, .044f);
            bool inspection = Environment.GetEnvironmentVariable("CONTOUR4_WEAR_INSPECTION_LIGHT") == "1";
            if (torch || inspection)
            {
                var light = cameraObject.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = inspection ? 106 : 92;
                light.innerSpotAngle = inspection ? 78 : 56;
                light.range = inspection ? 17 : 8;
                light.intensity = inspection ? 7.5f : 1.15f;
                light.color = new Color(.91f, .94f, 1f);
                light.renderMode = LightRenderMode.ForcePixel;
            }
            var targetTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = targetTexture;
                camera.Render();
                RenderTexture.active = targetTexture;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                return image.GetRawTextureData<byte>().ToArray();
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(cameraObject);
                RenderTexture.ReleaseTemporary(targetTexture);
            }
        }

        private static string Hierarchy(Transform part, Transform root) =>
            part == root ? "" : Hierarchy(part.parent, root) + "/" + part.name;
        private static string V(Vector3 value) => string.Format(Invariant, "{0:R},{1:R},{2:R}", value.x, value.y, value.z);
        private static string PathValue(string key) =>
            Environment.GetEnvironmentVariable(key) ?? throw new InvalidOperationException(key);
        private static void Require(bool check, string message)
        {
            if (!check) throw new InvalidOperationException("CONTOUR4 WEAR: " + message);
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject Root = new("Wear verification");
            internal readonly RaidAssembly Raid;
            internal Fixture() => Raid = Root.AddComponent<RaidAssembly>();
            public void Dispose() => Object.DestroyImmediate(Root);
        }

        private sealed class Result
        {
            internal readonly List<string> Signatures = new();
            internal readonly List<string> Materials = new();
            internal int PreparedRooms, PatchedRooms, PatchedFurniture, StartBays, Webs, LayoutRooms, Layouts;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Contour4.NativeRaid;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SpaceJourney
{
    public static class MaterialCoverageChecks
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly Pose Identity = new(Vector3.zero, Quaternion.identity);
        private static readonly string[] RingRooms = { "Quota", "Loot1", "Loot2", "Loot3" };
        private static readonly string[] RingBridges = { "BridgeQuotaLoot1", "BridgeLoot1Loot2" };
        private static readonly List<string> Slots = new();
        private static readonly List<string> Geometry = new();
        private static readonly List<string> Scenes = new();
        private static string output;

        public static void Run()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            output = Environment.GetEnvironmentVariable("CONTOUR4_COVERAGE_OUTPUT");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.42f,.42f,.42f);
            RenderSettings.reflectionIntensity = 0;
            using var bundles = RoomPrefabBundle.Load(Environment.GetEnvironmentVariable("CONTOUR4_ROOMS_BUNDLE"),
                Environment.GetEnvironmentVariable("CONTOUR4_PREPARED_BUNDLE"), requireWear: true);
            var catalog = ModuleCatalog.Rooms().ToArray();
            foreach (var module in catalog) PlaceModule(module, bundles.Prefabs, "prepared");
            foreach (var module in ModuleCatalog.Rooms()) PlaceModule(module, bundles.Prefabs, "forced-fallback");
            foreach (bool research in new[] { true, false })
            {
                string branch = research ? "with-research" : "without-research";
                var receiving = FreightServiceRoom.Create(FreightServiceRoom.SampleReceiving, research);
                Require(receiving.Docks.Any(dock => dock.Id == FreightServiceRoom.ResearchDoor) == research, branch);
                PlaceModule(receiving, bundles.Prefabs, "receiving-" + branch);
                PlaceModule(FreightServiceRoom.Create(FreightServiceRoom.Dispatch, research), bundles.Prefabs, "dispatch-" + branch);
            }
            PlaceModule(FreightServiceRoom.Create(FreightServiceRoom.InstrumentService, false), bundles.Prefabs, "service-native");
            PlaceModule(RaidExtraction.Create(), bundles.Prefabs, "extraction-native");
            foreach (var fold in new[] { RaidFreightFold.Rise(4.8f,4.8f), RaidFreightFold.Descend(), RaidFreightFold.LongDescent(5) })
                Fold(fold);
            foreach (var service in new[] { true, false }) StartBay(service);
            Ring();
            foreach (var (progress, seed) in new[] { (0, 2133), (3, -31), (5, -22) }) FullRaid(bundles.Prefabs, progress, seed);
            File.WriteAllLines(Path.Combine(output, "catalog.txt"), catalog.Select(module => module.Id));
            File.WriteAllLines(Path.Combine(output, "scenes.tsv"), Scenes);
            File.WriteAllLines(Path.Combine(output, "renderer-slots.tsv"), Slots);
            File.WriteAllLines(Path.Combine(output, "physical.tsv"), Geometry);
            Debug.Log($"CONTOUR4 MATERIAL COVERAGE PASSED: {catalog.Length} catalogue modules in both paths, " +
                $"{Scenes.Count} audited scenes, {Slots.Count} slot observations.");
        }

        private static void PlaceModule(GrayModule module, RaidRoomPrefabs prefabs, string route)
        {
            var root = new GameObject("Coverage witness");
            try
            {
                var raid = root.AddComponent<RaidAssembly>();
                var plan = new RaidPlan.Room(module, Identity, Identity, 0);
                var shader = Shader.Find("Standard");
                var fitout = CreateFitout(raid, shader);
                Material obstacles = Make(raid, shader, new Color(.39f,.33f,.25f));
                BakedRoom baked;
                if (route == "prepared")
                {
                    Require(prefabs.TryGetPrepared(plan, out var prefab) && prefab, "missing prepared asset " + module.Id);
                    Snapshot("packed-prefab", module.Id, prefab.transform, module);
                    baked = raid.PlacePrepared(plan, 0, prefab, obstacles, fitout);
                }
                else
                {
                    var sight = new RaidSightStrips(shader, Field<List<Material>>(raid,"materials"), Field<List<Mesh>>(raid,"meshes"));
                    var (floor, wall) = ((Material, Material))Method(raid, "Finishes").Invoke(raid, new object[] { shader });
                    var shell = Make(raid, shader, new Color(.52f,.56f,.57f));
                    var receiving = prefabs.For(plan);
                    if (receiving)
                        Snapshot("packed-" + route, module.Id, receiving.transform, module);
                    RaidCatalogSites.Dress(module);
                    RaidLootSites.Dress(module);
                    baked = (BakedRoom)Method(raid,"PlaceRoom").Invoke(raid,
                        new object[] { plan, 0, floor, wall, shell, obstacles, sight, receiving, fitout });
                }
                Physics.SyncTransforms();
                Field<RoomBloodTraces>(raid,"bloodTraces").Apply(baked);
                Snapshot(route, module.Id, baked.Root, module);
                if (route is "prepared" or "receiving-with-research" or "service-native") RenderModule(module.Id, baked.Root);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void FullRaid(RaidRoomPrefabs prefabs, int progress, int seed)
        {
            var root = new GameObject("Generated coverage witness");
            try
            {
                var raid = root.AddComponent<RaidAssembly>();
                raid.Place(seed, Identity, 0, 0, catalogSites: true, completedLevels: progress, roomPrefabs: prefabs);
                string stage = $"layout-{progress}-{seed}";
                var known = new HashSet<Transform>();
                foreach (var room in raid.Rooms)
                {
                    Snapshot(stage, room.Plan.Module.Id, room.Root, room.Plan.Module);
                    known.Add(room.Root);
                }
                foreach (var fold in raid.Folds)
                {
                    Snapshot(stage, fold.Link.Fold.Module.Id, fold.Root, fold.Link.Fold.Module);
                    known.Add(fold.Root);
                }
                foreach (Transform other in root.transform)
                    if (!known.Contains(other) && other.GetComponentsInChildren<Renderer>(true).Length != 0)
                        Snapshot(stage, other.name, other);
                if (progress == 0) Collar(raid);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void Collar(RaidAssembly raid)
        {
            var parent = RaidAssembly.Child(raid.transform, "Ring entrance collar witness");
            typeof(RaidAssembly).GetProperty("BranchRoot", Private).SetValue(raid, parent);
            var (floor, wall) = ((Material, Material))Method(raid,"Finishes").Invoke(raid, new object[] { Shader.Find("Standard") });
            Method(raid,"PlaceCollar").Invoke(raid, new object[] { raid.Plan.Entrance, .25f, 0, floor, wall });
            Snapshot("ring-entrance", "ring-entrance", parent);
        }

        private static void Fold(GrayModule module)
        {
            var root = new GameObject("Direct freight fold witness");
            try
            {
                var raid = root.AddComponent<RaidAssembly>();
                var shader = Shader.Find("Standard");
                var (floor, wall) = ((Material, Material))Method(raid, "Finishes").Invoke(raid, new object[] { shader });
                var shell = Make(raid, shader, new Color(.52f,.56f,.57f));
                var geometry = module.Geometry.Concat(RaidFreightFold.SeamDecks(module)).ToArray();
                var deckBlocks = geometry.Where(block => block.Role == BlockRole.Floor).ToArray();
                var shellBlocks = geometry.Where(block => block.Role != BlockRole.Floor).ToArray();
                bool open = module.Id != RaidFreightFold.LongSpan;
                var retainsShell = typeof(RaidAssembly).GetMethod("RetainsShellFinish", BindingFlags.Static | BindingFlags.NonPublic);
                foreach (var block in geometry)
                {
                    var surface = block.Role == BlockRole.Floor ? floor :
                        open || (bool)retainsShell.Invoke(null,new object[] { block.Id }) ? shell : wall;
                    Method(raid,"PlaceVisualPiece").Invoke(raid,new object[] { root.transform, block, 0, surface });
                }
                var place = Method(raid,"PlaceFoldCollider");
                var deck = (Collider)place.Invoke(raid,new object[] { root.transform, "Freight deck", 0, deckBlocks });
                var walls = (Collider)place.Invoke(raid,new object[] { root.transform, "Freight walls", 0, shellBlocks });
                place.Invoke(raid,new object[] { root.transform, "Invisible freight safety", 0, RaidFreightFold.SafetyBarriers(module) });
                var room = new RaidPlan.Room(module, Identity, Identity, 0);
                var link = new RaidPlan.FreightLink(new RaidPlan.End(room,module.ExternalDocks.First()),
                    new RaidPlan.End(room,module.ExternalDocks.Last()),room);
                var lighting = (RoomLighting)typeof(RaidAssembly).GetProperty("Lighting",Private).GetValue(raid);
                lighting.Fold(new FreightFold(link,root.transform,deck,walls));
                Field<RaidInteriorDetails>(raid,"details").Transit(root.transform,geometry);
                Physics.SyncTransforms();
                Snapshot("direct-freight-fold",module.Id,root.transform,module);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void StartBay(bool service)
        {
            string name = service ? "service-garage" : "pressure-dock";
            var root = new GameObject("Contour-4 start " + name);
            try
            {
                var bay = root.AddComponent<NativeRaidStartBay>();
                Method(bay,"Build").Invoke(bay, new object[] { service, 4.5f, 4.2f });
                Snapshot("starter", name, root.transform);
                if (service) Render(root.transform, name, new(0,1.55f,-7.2f), new(5,1.7f,-3.2f), 65);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void Ring()
        {
            using var bundle = new BundleScope(AssetBundle.LoadFromFile(Environment.GetEnvironmentVariable("CONTOUR4_COVERAGE_RING")));
            var prefab = bundle.Bundle.LoadAsset<GameObject>("Assets/Contour4Ring/Contour4 Ring.prefab");
            Require(prefab, "missing Ring");
            Snapshot("packed-ring", "ring", prefab.transform);
            var root = Object.Instantiate(prefab);
            var details = new RaidInteriorDetails();
            try
            {
                var finish = root.transform.Find("Quota/Model/Quota/Quota__Geometry/Quota__Floor").GetComponent<MeshRenderer>().sharedMaterial;
                foreach (string bridge in RingBridges)
                    root.transform.Find($"{bridge}/Model/{bridge}/{bridge}__Geometry/{bridge}__Walkable_Deck")
                        .GetComponent<MeshRenderer>().sharedMaterial = finish;
                root.transform.Find("Loot2/Catalog sites/Upper gallery deck").GetComponent<MeshRenderer>().sharedMaterial = finish;
                foreach (string room in RingRooms) details.RingRoom(root.transform.Find(room));
                foreach (string bridge in RingBridges) details.RingBridge(root.transform.Find(bridge));
                var remaining = details.GetType().GetMethod("RingRemainder", Private);
                remaining?.Invoke(details, new object[] { root.transform });
                Snapshot("assembled-ring", "ring", root.transform);
            }
            finally { Object.DestroyImmediate(root); details.Release(); }
        }

        private static void Snapshot(string stage, string id, Transform root, GrayModule module = null)
        {
            var roles = module?.Geometry.GroupBy(block => block.Id)
                .ToDictionary(group => group.Key, group => string.Join(",",group.Select(block => block.Role).Distinct()));
            int entries = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                for (int slot = 0; slot < Math.Max(materials.Length, mesh ? mesh.subMeshCount : 0); slot++)
                {
                    Material material = slot < materials.Length ? materials[slot] : null;
                    string role = roles != null && roles.TryGetValue(renderer.name, out var value) ? value : "";
                    string texture = material && material.mainTexture ? material.mainTexture.name : "";
                    string flags = material ? string.Join(",", material.shaderKeywords.OrderBy(value => value)) : "";
                    int kind = material && material.shader.name == RoomSurfaceWear.FinishName ?
                        Mathf.RoundToInt(material.GetFloat("_WearKind")) : 0;
                    bool shown = renderer.gameObject.activeInHierarchy && renderer.enabled &&
                        !renderer.forceRenderingOff && renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly;
                    Slots.Add($"{stage}	{id}	{PathOf(root,renderer.transform)}	{slot}	{shown}	{role}	" +
                        $"{material?.shader.name}	{material?.name}	{texture}	{flags}	{kind}	" +
                        $"{material?.renderQueue}	{mesh?.name}	{mesh?.subMeshCount}	{renderer.bounds.center:F3}	{renderer.bounds.size:F3}");
                    entries++;
                }
            }
            Require(entries > 0, $"no renderer observations for {stage}/{id}");
            Scenes.Add($"{stage}	{id}	{entries}");
            foreach (Collider part in root.GetComponentsInChildren<Collider>(true))
                Geometry.Add($"{stage}	{id}	{PathOf(root,part.transform)}	{part.GetType().Name}	{part.enabled}	" +
                    $"{part.isTrigger}	{part.bounds.center:F5}	{part.bounds.size:F5}");
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
                Geometry.Add($"{stage}	{id}	{PathOf(root,light.transform)}	Light	{light.enabled}	{light.type}	" +
                    $"{light.transform.position:F5}	{light.transform.forward:F5}	{light.intensity:R}	{light.range:R}");
        }

        private static void RenderModule(string module, Transform root)
        {
            switch (module)
            {
                case "shaft-plant": Render(root, module, new(14.45f,1.63f,1.86f), new(17.31f,.94f,5.69f), 55); break;
                case "instrument-service": Render(root, module, new(.65f,1.49f,-1.93f), new(1.67f,1.09f,-3.63f), 54); break;
                case "straight-corridor-short": Render(root, module, new(1.12f,1.56f,-.54f), new(5.65f,.75f,.52f), 58); break;
                case "sample-receiving": Render(root, module, new(1.8f,1.64f,-.8f), new(6.72f,1.24f,2.22f), 60); break;
                case SphericalChamber.Id: Render(root, module, new(-7.4f,1.65f,.61f), new(2.4f,-7f,12.4f), 65); break;
            }
        }

        private static void Render(Transform root, string id, Vector3 eye, Vector3 aim, float fov)
        {
            foreach (var light in root.GetComponentsInChildren<Light>()) light.enabled = false;
            var camera = new GameObject("Coverage camera").AddComponent<Camera>();
            var key = new GameObject("Coverage neutral light").AddComponent<Light>();
            var frame = RenderTexture.GetTemporary(1080, 720, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(1080,720,TextureFormat.RGB24,false);
            var prior = RenderTexture.active;
            try
            {
                camera.transform.SetPositionAndRotation(root.TransformPoint(eye),
                    Quaternion.LookRotation(root.TransformPoint(aim)-root.TransformPoint(eye),root.up));
                camera.fieldOfView = fov;
                camera.targetTexture = frame;
                key.type = LightType.Directional;
                key.intensity = .72f;
                key.transform.rotation = Quaternion.LookRotation(camera.transform.forward - root.up*.78f + camera.transform.right*.54f, root.up);
                if (id == "shaft-plant")
                {
                    Physics.SyncTransforms();
                    var ray = camera.ViewportPointToRay(new Vector3(.855f,.455f,0));
                    if (Physics.Raycast(ray,out var hit,32f))
                        File.WriteAllText(Path.Combine(output,"shaft-picked-renderer.txt"),
                            $"{PathOf(root,hit.collider.transform)}\n{hit.collider.GetComponent<Renderer>()?.sharedMaterial?.name}");
                }
                camera.Render();
                RenderTexture.active = frame;
                image.ReadPixels(new Rect(0,0,1080,720),0,0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(output,id+".png"),image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = prior;
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(key.gameObject);
                Object.DestroyImmediate(image);
                RenderTexture.ReleaseTemporary(frame);
            }
        }

        private static RaidInteriorFitout CreateFitout(RaidAssembly raid, Shader shader)
        {
            var factory = raid.GetType().GetMethod("CreateFitout", Private);
            if (factory != null) return (RaidInteriorFitout)factory.Invoke(raid, new object[] { shader });
            return new RaidInteriorFitout(Make(raid,shader,new(.24f,.34f,.34f)), Make(raid,shader,new(.28f,.39f,.43f)),
                Make(raid,shader,new(.43f,.47f,.43f)), Make(raid,shader,new(.63f,.55f,.32f)), Make(raid,shader,new(.11f,.16f,.17f)),
                Field<List<Material>>(raid,"materials"),Field<List<Mesh>>(raid,"meshes"),Field<BrokenEquipment>(raid,"brokenEquipment"));
        }

        private static Material Make(RaidAssembly raid, Shader shader, Color color) =>
            (Material)Method(raid,"Material").Invoke(raid,new object[] { shader,color });
        private static T Field<T>(object item,string name) => (T)item.GetType().GetField(name,Private).GetValue(item);
        private static MethodInfo Method(object item,string name) => item.GetType().GetMethod(name,Private);
        private static string PathOf(Transform root,Transform node) => root == node ? root.name :
            PathOf(root,node.parent) + "/" + node.name + "[" + node.GetSiblingIndex() + "]";
        private static void Require(bool condition,string message) { if (!condition) throw new InvalidOperationException(message); }

        private sealed class BundleScope : IDisposable
        {
            internal AssetBundle Bundle { get; }
            internal BundleScope(AssetBundle bundle) => Bundle = bundle;
            public void Dispose() { if (Bundle) Bundle.Unload(true); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Numerics = System.Numerics;
using Object = UnityEngine.Object;

namespace Contour4.Procedural.Catalog
{
    public static class BuildPreparedModules
    {
        private const string Output = "Assets/Contour4/PreparedModules";
        private const string Materials = Output + "/Materials";

        public static void Run()
        {
            Folder("Assets", "Contour4");
            Folder("Assets/Contour4", "PreparedModules");
            Folder(Output, "Materials");
            Folder(Output, SphericalChamber.Id);
            SceneDressing dressing = new(Materials);
            Material floor = MaterialFor("Deck", new Color(.32f, .36f, .40f));
            Material wall = MaterialFor("Shell", new Color(.52f, .56f, .57f));
            Material cargo = MaterialFor("Obstacle", new Color(.39f, .33f, .25f));
            Material dock = MaterialFor("Dock plug", new Color(.40f, .48f, .52f));
            Material gate = MaterialFor("Inspection shutter", new Color(.27f, .29f, .29f));
            Material sphereShell = null, sphereLeft = null, sphereRight = null;
            Material ribbon = null, ribbonAccess = null;
            string selection = Environment.GetEnvironmentVariable("CONTOUR4_PREPARED_MODULE");
            int count = 0;
            foreach (GrayModule module in ModuleCatalog.Rooms())
            {
                if (!Selected(selection, module.Id)) continue;
                bool spherical = SphericalChamber.IsVariant(module.Id);
                if (spherical)
                {
                    sphereShell = MaterialFor("Sphere porcelain shell", new Color(.36f, .50f, .53f));
                    sphereLeft = MaterialFor("Sphere amber rise", new Color(.68f, .44f, .19f));
                    sphereRight = MaterialFor("Sphere aquamarine descent", new Color(.19f, .52f, .48f));
                }
                if (module.Id == MobiusHall.Id)
                {
                    ribbon = MaterialFor("Mobius ribbon enamel", new Color(.20f, .51f, .58f));
                    ribbonAccess = MaterialFor("Mobius amber access", new Color(.63f, .43f, .21f));
                }
                Build(module, block => block.Id == MaintenanceTrench.Gate && module.Id == MaintenanceTrench.Id ? gate :
                    spherical && block.Id == SphericalChamber.Shell ? sphereShell :
                    spherical && block.Id == SphericalChamber.Left ? sphereLeft :
                    spherical && block.Id == SphericalChamber.Right ? sphereRight :
                    module.Id == MobiusHall.Id && block.Id == MobiusHall.Ribbon ? ribbon :
                    module.Id == MobiusHall.Id && block.Id is MobiusHall.Landing or MobiusHall.Ramp ? ribbonAccess :
                    block.Surface is DressingSurface surface ? dressing.MaterialFor(surface) : block.Role switch
                    {
                        BlockRole.Floor => floor,
                        BlockRole.Obstacle or BlockRole.Fixture => cargo,
                        BlockRole.Plug => dock,
                        _ => wall,
                    });
                count++;
            }
            if (count == 0) throw new InvalidOperationException("No prepared modules selected.");
            Debug.Log($"CONTOUR4 PREPARED UNITY PASSED: {count} prefabs");
        }

        public static void VerifyExisting()
        {
            string selection = Environment.GetEnvironmentVariable("CONTOUR4_PREPARED_MODULE");
            int count = 0;
            foreach (GrayModule module in ModuleCatalog.Rooms())
            {
                if (!Selected(selection, module.Id)) continue;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ReloadAndVerify(module, $"{Output}/{module.Id}/{module.Id}.prefab");
                count++;
            }
            if (count == 0) throw new InvalidOperationException("No prepared modules selected.");
            Debug.Log($"CONTOUR4 PREPARED UNITY PASSED: {count} prefabs");
        }

        private static bool Selected(string selection, string id) => string.IsNullOrEmpty(selection) || selection == id ||
            (selection == "spherical-chamber-variants" && SphericalChamber.IsVariant(id));

        private static void Build(GrayModule module, Func<Block, Material> materialFor)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string directory = $"{Output}/{module.Id}";
            Folder(Output, module.Id);
            string path = $"{directory}/{module.Id}";
            var meshAssets = new PreparedModuleMeshes(path + "-geometry.asset");
            var root = new GameObject(module.Id);
            try
            {
                Transform geometry = Child(root.transform, "Geometry");
                var groups = new Dictionary<BlockRole, Transform>
                {
                    [BlockRole.Floor] = Child(geometry, "Floors"),
                    [BlockRole.Shell] = Child(geometry, "Shell"),
                    [BlockRole.Obstacle] = Child(geometry, "Obstacles"),
                    [BlockRole.Fixture] = Child(geometry, "Fixtures"),
                    [BlockRole.Plug] = Child(geometry, "Conditional plugs"),
                };
                var parts = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (Block block in module.Geometry.Where(block => block.Role != BlockRole.Free))
                {
                    Mesh mesh = meshAssets.Save(block);
                    parts.Add(block.Id, Part(groups[block.Role], block, mesh, materialFor(block)).transform);
                }
                AddFrames(root.transform, module);
                AddMechanisms(root.transform, module, parts);
                foreach (RoomDressing dressing in module.Dressing)
                    SceneDressing.AddLights(dressing, root.transform);
                int sphereMask = SphericalChamber.GetMask(module.Id);
                if (sphereMask != 0) AddSphereLighting(root.transform, sphereMask);
                if (module.Id == MobiusHall.Id) { AddMobiusLighting(root.transform); MobiusHallDressing.Add(root.transform); }
                Physics.SyncTransforms();
                int loot = PreparedModuleLoot.Add(module, Child(root.transform, "Loot candidates"), out string lootDescription);
                if (sphereMask != 0) SphericalChamberDressing.Add(root.transform, sphereMask);
                int paths = PreparedModuleEnemyNav.Add(module, Child(root.transform, "Monster paths"));
                Expect(loot == root.transform.Find("Loot candidates").childCount && paths > 0,
                    $"{module.Id}: marker hooks reported an invalid result.");
                File.WriteAllText(path + "-loot.json", lootDescription + "\n");
                AssetDatabase.ImportAsset(path + "-loot.json", ImportAssetOptions.ForceSynchronousImport);
                PreparedModuleContract.Save(module, path + "-catalog.json");
                meshAssets.Finish();
                if (!PrefabUtility.SaveAsPrefabAsset(root, path + ".prefab"))
                    throw new InvalidOperationException($"Could not save {module.Id}.");
                AssetDatabase.SaveAssets();
            }
            finally { Object.DestroyImmediate(root); }

            ReloadAndVerify(module, path + ".prefab");
        }

        private static void ReloadAndVerify(GrayModule module, string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Expect(asset && PrefabUtility.GetPrefabAssetType(asset) == PrefabAssetType.Regular,
                $"{module.Id}: regular prefab failed to reload.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            try { PreparedModuleVerification.Verify(module, instance); }
            finally { Object.DestroyImmediate(instance); }
        }

        private static void AddFrames(Transform root, GrayModule module)
        {
            Transform floors = Child(root, "Floor frames");
            foreach (Floor floor in module.Floors) Frame(floors, floor.Id, floor.Pose);

            Transform docks = Child(root, "Docks");
            foreach (Dock dock in module.Docks)
            {
                Transform threshold = Frame(docks, dock.Id, dock.Threshold);
                // The catalog's lateral axis is forward × up. Unity's local +X is up × forward.
                Child(threshold, "Contact start").localPosition = Vector3.right * (dock.Opening.X / 2);
                Child(threshold, "Contact end").localPosition = Vector3.left * (dock.Opening.X / 2);
                Child(threshold, "Opening top").localPosition = Vector3.up * dock.Opening.Y;
            }

            Transform routes = Child(root, "Haul routes");
            foreach (HaulRoute route in module.Routes)
            {
                Transform track = Child(routes, route.Id);
                for (int i = 0; i < route.Knots.Length; i++) Frame(track, $"{i + 1:D3}", route.Knots[i]);
            }

            Transform landmarks = Child(root, "Landmarks");
            foreach (var landmark in module.Landmarks) Child(landmarks, landmark.Key).localPosition = V(landmark.Value);
        }

        private static void AddMechanisms(Transform root, GrayModule module, Dictionary<string, Transform> parts)
        {
            Transform mechanisms = Child(root, "Mechanisms");
            foreach (RoomDressing dressing in module.Dressing)
            foreach (RoomDressing.MovingFront front in dressing.MovingFronts)
            {
                string panel = dressing.Name(front.Panel);
                Transform pivot = Frame(mechanisms, panel + " pivot", new ModulePose(dressing.Point(front.Pivot),
                    dressing.Direction(Numerics.Vector3.UnitZ), dressing.Frame.Up));
                foreach (string part in new[] { panel, dressing.Name(front.Handle),
                             front.Tray == null ? null : dressing.Name(front.Tray) }.Where(id => id != null))
                {
                    parts[part].SetParent(pivot, true);
                    parts[part].GetComponent<MeshCollider>().convex = true;
                }
            }
            if (module.Id == MaintenanceTrench.Id)
            {
                Transform gate = parts[MaintenanceTrench.Gate];
                Transform pivot = Child(mechanisms, "Inspection shutter slide");
                pivot.localPosition = gate.localPosition;
                gate.SetParent(pivot, true);
                gate.GetComponent<MeshCollider>().convex = true;
                Child(pivot, "Closed travel endpoint").localPosition = Vector3.down * MaintenanceTrench.GateTravel;
            }
        }

        private static void AddSphereLighting(Transform root, int mask)
        {
            Transform lights = Child(root, "Sphere lighting");
            Add("Western access", new Vector3(-8.6f, 2.6f, 0), new Color(.74f, .89f, 1f), 2.4f, 9);
            if ((mask & SphericalChamber.EastMask) != 0)
                Add("Eastern access", new Vector3(8.6f, 2.6f, 0), new Color(.74f, .89f, 1f), 2.4f, 9);
            if ((mask & SphericalChamber.UpperMask) != 0)
                Add("Amber route", new Vector3(0, 5.5f, 5), new Color(1f, .76f, .46f), 2.7f, 13);
            if ((mask & SphericalChamber.LowerMask) != 0)
                Add("Aquamarine route", new Vector3(0, -4.2f, -6), new Color(.54f, .90f, .94f), 2.7f, 13);
            Add("Vault lighting", new Vector3(0, 4, -.5f), new Color(.79f, .90f, 1f), 2, 17);

            void Add(string id, Vector3 position, Color color, float intensity, float range)
            {
                Transform source = Child(lights, id);
                source.localPosition = position;
                Light lamp = source.gameObject.AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.color = color;
                lamp.intensity = intensity;
                lamp.range = range;
            }
        }

        internal static MeshCollider Part(Transform parent, Block block, Mesh mesh, Material material = null)
        {
            Transform part = Child(parent, block.Id);
            part.localPosition = Center(block);
            if (material)
            {
                part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                part.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
            var collider = part.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            return collider;
        }

        internal static Mesh CreateMesh(Block block)
        {
            Vector3 center = Center(block);
            bool welded = block.Id == MobiusHall.Ribbon;
            var mesh = new Mesh
            {
                name = block.Id,
                vertices = (welded ? block.Vertices : block.Faces.Select(i => block.Vertices[i]))
                    .Select(vertex => V(vertex) - center).ToArray(),
                triangles = welded ? block.Faces : Enumerable.Range(0, block.Faces.Length).ToArray(),
            };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static void AddMobiusLighting(Transform root)
        {
            Transform lights = Child(root, "Ribbon work lights");
            Add(new Vector3(0, 4.4f, 3.8f), new Color(1f, .77f, .47f), 2.2f, 9f);
            Add(new Vector3(-7f, 6.5f, -3.8f), new Color(.56f, .87f, 1f), 2.8f, 11f);
            Add(new Vector3(7f, 7.2f, 3.8f), new Color(.72f, .87f, 1f), 2.6f, 11f);
            Add(new Vector3(0, 3.8f, -3.8f), new Color(.82f, .9f, 1f), 2.2f, 9f);

            void Add(Vector3 position, Color color, float intensity, float range)
            {
                Transform source = Child(lights, "Fixture " + (lights.childCount + 1));
                source.localPosition = position;
                Light lamp = source.gameObject.AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.color = color;
                lamp.intensity = intensity;
                lamp.range = range;
            }
        }

        private static Vector3 Center(Block block)
        {
            var (min, max) = block.Bounds;
            return V((min + max) / 2);
        }

        private static void Folder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        internal static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        internal static Vector3 V(Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
        internal static Quaternion Rotation(ModulePose pose) => Quaternion.LookRotation(V(pose.Forward), V(pose.Up));
        internal static void Expect(bool success, string reason)
        {
            if (!success) throw new InvalidOperationException(reason);
        }

        private static Transform Frame(Transform parent, string name, ModulePose frame)
        {
            Transform marker = Child(parent, name);
            marker.SetLocalPositionAndRotation(V(frame.Position), Rotation(frame));
            return marker;
        }

        private static Material MaterialFor(string id, Color color)
        {
            string path = $"{Materials}/{id.ToLowerInvariant().Replace(' ', '-')}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            material = new Material(Shader.Find("Standard")) { name = id, color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private sealed class PreparedModuleMeshes
        {
            private readonly string path;
            private readonly Dictionary<string, Mesh> existing;
            private bool initialized;

            internal PreparedModuleMeshes(string path)
            {
                this.path = path;
                initialized = AssetDatabase.LoadMainAssetAtPath(path);
                existing = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>()
                    .ToDictionary(mesh => mesh.name, StringComparer.Ordinal);
            }

            internal Mesh Save(Block block)
            {
                Mesh mesh = CreateMesh(block);
                if (existing.Remove(block.Id, out Mesh previous))
                {
                    EditorUtility.CopySerialized(mesh, previous);
                    Object.DestroyImmediate(mesh);
                    EditorUtility.SetDirty(previous);
                    return previous;
                }
                if (initialized) AssetDatabase.AddObjectToAsset(mesh, path);
                else
                {
                    AssetDatabase.CreateAsset(mesh, path);
                    initialized = true;
                }
                return mesh;
            }

            internal void Finish()
            {
                foreach (Mesh retired in existing.Values) Object.DestroyImmediate(retired, allowDestroyingAssets: true);
            }
        }
    }
}

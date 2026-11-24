using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural.Catalog.Checks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Numerics = System.Numerics;

namespace Contour4.Procedural.Catalog
{
    public static class BuildCatalogScenes
    {
        private const string Output = "Assets/Contour4Catalog/Generated";

        public static void Run()
        {
            EnsureFolder(Output);
            Material shell = Material("Shell", new Color(.55f, .58f, .60f));
            Material deck = Material("Deck", new Color(.36f, .40f, .43f));
            Material cargo = Material("Cargo", new Color(.66f, .59f, .43f));
            var dressing = new SceneDressing(Output);
            string selected = Environment.GetEnvironmentVariable("CONTOUR4_CATALOG_MODULE");
            string renderPath = Environment.GetEnvironmentVariable("CONTOUR4_CATALOG_RENDER");
            int count = 0;
            foreach (GrayModule module in ModuleCatalog.Rooms())
            {
                if (!string.IsNullOrEmpty(selected) && selected != module.Id) continue;
                Build(module, shell, deck, cargo, dressing, renderPath);
                count++;
            }
            if (count == 0) throw new InvalidOperationException("No catalog modules selected.");
            AssetDatabase.SaveAssets();
            Debug.Log($"CONTOUR4 CATALOG UNITY PASSED: {count} scenes");
        }

        private static void Build(GrayModule module, Material shell, Material deck, Material cargo,
            SceneDressing dressing, string renderPath)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string path = $"{Output}/{module.Id}";
            string meshPath = $"{path}-meshes.asset";
            if (AssetDatabase.LoadMainAssetAtPath(meshPath)) AssetDatabase.DeleteAsset(meshPath);
            var root = new GameObject(module.Id);
            var mateRoot = new GameObject("Threshold test chambers");
            var fixture = new Dictionary<Collider, BlockRole>();
            var meshes = new List<Mesh>();
            var opened = module.Docks.Where(CatalogChecks.IsPhysical).Select(d => d.PlugId).ToHashSet();
            foreach (Block block in module.Geometry)
                AddBlock(block, root.transform, opened.Contains(block.Id));
            foreach (Dock dock in module.Docks)
            {
                var marker = new GameObject(dock.Id);
                marker.transform.SetParent(root.transform, false);
                marker.transform.SetPositionAndRotation(Vec(dock.Threshold.Position), Rotation(dock.Threshold));
                if (CatalogChecks.IsPhysical(dock))
                    foreach (Block block in CatalogChecks.Fixture(dock))
                        AddBlock(block, mateRoot.transform, false);
            }
            AssetDatabase.CreateAsset(meshes[0], meshPath);
            foreach (Mesh mesh in meshes.Skip(1)) AssetDatabase.AddObjectToAsset(mesh, meshPath);
            AssetDatabase.ImportAsset(meshPath);
            Physics.SyncTransforms();
            foreach (Dock dock in module.Docks.Where(CatalogChecks.IsPhysical))
            {
                ModulePose t = dock.Threshold;
                Verify(module.Id, new HaulRoute($"{dock.Id}/threshold", dock.Id, "mate", dock.MaxCargo,
                    new[] { new ModulePose(t.Point(-1.6f, 0), t.Forward, t.Up),
                        new ModulePose(t.Point(1.8f, 0), t.Forward, t.Up) }), fixture);
            }
            foreach (HaulRoute route in module.Routes)
                Verify(module.Id, route, fixture);
            VerifyVariant(module, fixture);
            if (module.Id == "upper-exit-corridor")
                DriveCatalogCart.Verify(module, fixture);
            SceneDressing.Ambient();
            foreach (RoomDressing set in module.Dressing)
            {
                SceneDressing.VerifyCover($"CONTOUR4 CATALOG {module.Id}", set, fixture.Keys,
                    collider => fixture.TryGetValue(collider, out BlockRole role) && role == BlockRole.Floor);
                SceneDressing.AddLights(set, root.transform);
            }
            AddPreview(module, deck, cargo);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, path + ".unity"))
                throw new InvalidOperationException($"Unity could not save {path}");
            if (!string.IsNullOrEmpty(renderPath)) CatalogRoomRenders.Save(module.Id, renderPath);
            Debug.Log($"CONTOUR4 CATALOG {module.Id}: built {meshes.Count} matching render/collider meshes; {module.Size}");
            Debug.Log($"CONTOUR4 CATALOG {module.Id}: {module.Geometry.Count(b => b.Role == BlockRole.Fixture)} fixed fixture parts; " +
                $"{module.Dressing.Sum(set => set.Lamps.Count)} local luminaires.");

            void AddBlock(Block block, Transform parent, bool hide)
            {
                var part = new GameObject(block.Id);
                part.transform.SetParent(parent, false);
                var mesh = new Mesh
                {
                    name = block.Id,
                    vertices = block.Faces.Select(i => Vec(block.Vertices[i])).ToArray(),
                    triangles = Enumerable.Range(0, block.Faces.Length).ToArray(),
                };
                mesh.RecalculateNormals();
                meshes.Add(mesh);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                part.AddComponent<MeshRenderer>().sharedMaterial = block.Surface.HasValue ? dressing.MaterialFor(block.Surface.Value) :
                    block.Role == BlockRole.Floor ? deck : block.Role == BlockRole.Obstacle ? cargo : shell;
                var collider = part.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                part.SetActive(!hide);
                if (!hide) fixture.Add(collider, block.Role);
            }
        }

        internal static void VerifyVariant(GrayModule module, IReadOnlyDictionary<Collider, BlockRole> geometry)
        {
            if (module.Id == "sample-storage-narrow")
            {
                Numerics.Vector3 envelope = CornerSlice.CartEnvelope;
                var center = new Vector3(8.5f, .03f + envelope.Y / 2, 0);
                var half = new Vector3(envelope.Z, envelope.Y, envelope.X) / 2;
                var rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
                bool blocked = Physics.OverlapBox(center, half, rotation, ~0, QueryTriggerInteraction.Ignore)
                    .Any(c => geometry.TryGetValue(c, out BlockRole role) && role == BlockRole.Obstacle);
                if (!blocked) throw new InvalidOperationException("Narrow shortcut unexpectedly fits the loaded cart.");
                Debug.Log("CONTOUR4 CATALOG sample-storage-narrow: PhysX blocked the loaded cart in the 1.30m shortcut.");
            }
            if (module.Id == "observation-lab-broken-floor")
            {
                Vector3 shaft = new Vector3(10.275f, .1f, 0);
                if (!Physics.Raycast(shaft, Vector3.down, out RaycastHit hit, 3, ~0, QueryTriggerInteraction.Ignore) ||
                    hit.collider.name != "shaft-bottom" || Mathf.Abs(hit.distance - 2.35f) > .01f)
                    throw new InvalidOperationException("The broken laboratory shaft must be open down to its bottom.");
                Debug.Log("CONTOUR4 CATALOG observation-lab-broken-floor: PhysX measured a 2.25m drop below the lower floor.");
            }
        }

        internal static void Verify(string module, HaulRoute route, IReadOnlyDictionary<Collider, BlockRole> geometry)
        {
            int count = 0;
            int supportPoses = 0;
            Vector3 half = new Vector3(route.Envelope.Z, route.Envelope.Y, route.Envelope.X) / 2;
            foreach (ModulePose pose in route.Samples(.065f))
            {
                Vector3 center = Vec(pose.Point(0, route.StepClearance + half.y));
                Collider blocker = Physics.OverlapBox(center, half, Rotation(pose), ~0, QueryTriggerInteraction.Ignore)
                    .FirstOrDefault(geometry.ContainsKey);
                if (blocker != null)
                    throw new InvalidOperationException($"{module}/{route.Id}: Unity overlap {blocker.name} at {pose.Position}");
                float[] longitude = route.Cargo == Cargo.Cart ? new[] { -.85f, .85f } : new[] { 0f };
                float[] latitude = route.Cargo == Cargo.Cart ? new[] { -.65f, .65f } : new[] { 0f };
                bool onDeck = route.NeedsSupport(pose);
                if (onDeck) supportPoses++;
                foreach (float x in onDeck ? longitude : Array.Empty<float>())
                foreach (float z in latitude)
                {
                    Vector3 origin = Vec(pose.Point(x, route.StepClearance, z));
                    Vector3 up = Vec(pose.Up);
                    float distance = route.StepClearance + (route.Cargo == Cargo.Cart ? .11f : .26f);
                    bool supports = Physics.RaycastAll(origin, -up, distance, ~0, QueryTriggerInteraction.Ignore)
                        .Any(hit => geometry.TryGetValue(hit.collider, out BlockRole role) &&
                            role == BlockRole.Floor && Vector3.Dot(up, hit.normal) > .65f);
                    if (!supports)
                        throw new InvalidOperationException($"{module}/{route.Id}: no Unity floor ray at {pose.Position} ({x}/{z})");
                }
                count++;
            }
            Debug.Log($"CONTOUR4 CATALOG {module}/{route.Id}: PhysX {count} poses clear, " +
                $"{supportPoses} supported ({route.Cargo})");
        }

        private static void AddPreview(GrayModule module, Material deck, Material cargo)
        {
            var preview = new GameObject("Catalog route preview").AddComponent<CatalogPreview>();
            preview.Routes = module.Routes.Select(route =>
            {
                ModulePose[] poses = route.Samples(.065f).ToArray();
                return new CatalogPreview.Route
                {
                    Label = route.Id,
                    Cart = route.Cargo == Cargo.Cart,
                    Positions = poses.Select(p => Vec(p.Point(0, route.StepClearance))).ToArray(),
                    Rotations = poses.Select(Rotation).ToArray(),
                };
            }).ToArray();
            preview.Load = new GameObject("Cargo fixture").transform;
            preview.RideCamera = new GameObject("Route camera").AddComponent<Camera>();
            var pose = module.Routes[0].Knots[0];
            preview.RideCamera.transform.SetPositionAndRotation(Vec(pose.Point(0, 2.6f)), Rotation(pose));
            preview.RideCamera.nearClipPlane = .04f;
            preview.RideCamera.clearFlags = CameraClearFlags.SolidColor;
            preview.RideCamera.backgroundColor = new Color(.08f, .10f, .12f);
            preview.CartFixture = new GameObject("Cart and secured load");
            preview.CartFixture.transform.SetParent(preview.Load, false);
            PreviewBox(preview.CartFixture.transform, "Cart", new Vector3(0, .3f, 0), new Vector3(1.8f, .60f, 2.6f), deck);
            PreviewBox(preview.CartFixture.transform, "Secured load", new Vector3(0, 1.31f, .1f),
                new Vector3(1.5f, 1.42f, 1.9f), cargo);
            preview.HandFixture = PreviewBox(preview.Load, "Hand cargo", new Vector3(0, 1.08f, .20f),
                new Vector3(.55f, .48f, .38f), cargo);
            preview.CartFixture.SetActive(false);
            preview.HandFixture.SetActive(false);
        }

        private static GameObject PreviewBox(Transform parent, string id, Vector3 center, Vector3 size, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = id;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = center;
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(box.GetComponent<Collider>());
            return box;
        }

        private static Material Material(string id, Color color)
        {
            string path = $"{Output}/{id}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var asset = new Material(Shader.Find("Standard")) { name = id, color = color };
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int edge = path.LastIndexOf('/');
            string parent = path.Substring(0, edge);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(edge + 1));
        }

        private static Vector3 Vec(Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
        private static Quaternion Rotation(ModulePose p) => Quaternion.LookRotation(Vec(p.Forward), Vec(p.Up));
    }
}

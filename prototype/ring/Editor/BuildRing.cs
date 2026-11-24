using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class BuildRing
{
    private const string Folder = "Assets/Contour4Ring";
    private const string RootName = "Contour4 Ring";
    private const float GridScale = 1.25f;
    private const float JoinTolerance = .015f;

    [Serializable]
    private sealed class Layout
    {
        public Asset[] assets;
        public Link[] links;
        public MapPosition[] map;
    }

    [Serializable]
    private sealed class Asset
    {
        public string name;
        public bool room;
        public float[] position;
        public float[] euler;
        public Anchor[] anchors;
        public Shape[] colliders;
        public string[] authoringOnly;
    }

    [Serializable]
    private sealed class Anchor
    {
        public string name;
        public string kind;
        public string target;
        public float[] position;
        public float[] forward;
        public float[] up;
        public float[] boxSize;
    }

    [Serializable]
    private sealed class Shape
    {
        public string @object;
        public string shape;
    }

    [Serializable]
    private sealed class Link
    {
        public string a;
        public string b;
        public string kind;
    }

    [Serializable]
    private sealed class MapPosition
    {
        public string name;
        public float[] position;
        public float yaw;
    }

    private readonly struct AxisMap
    {
        private readonly int x;
        private readonly int z;

        public AxisMap(int x, int z)
        {
            this.x = x;
            this.z = z;
        }

        public Vector3 Convert(Vector3 value) => new(x * value.x, value.z, z * value.y);
        public Vector3 Convert(float[] value) => Convert(new Vector3(value[0], value[1], value[2]));

        public Quaternion Rotation(float[] euler)
        {
            Quaternion local = Quaternion.AngleAxis(euler[2], Vector3.forward) *
                Quaternion.AngleAxis(euler[1], Vector3.up) * Quaternion.AngleAxis(euler[0], Vector3.right);
            return Quaternion.LookRotation(Convert(local * (Vector3.up * z)), Convert(local * Vector3.forward));
        }

        public override string ToString() => $"({x}*X, Z, {z}*Y)";
    }

    public static void Build()
    {
        var layout = JsonUtility.FromJson<Layout>(File.ReadAllText($"{Folder}/RingLayout.json"));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var root = new GameObject(RootName);
        var parts = new Dictionary<string, Transform>();
        var poses = new Dictionary<string, Dictionary<string, Transform>>();
        var lamps = MakeMaterial("Fixture", new Color(.12f, .13f, .15f), Color.black);
        var diffusers = MakeMaterial("Diffuser", new Color(.91f, .93f, .96f), new Color(1.15f, 1.25f, 1.4f));
        try
        {
            AxisMap axes = ReadAxisMap(layout.assets.First(asset => asset.name == "Loot1"), root.transform);
            Vector3 quotaOrigin = axes.Convert(layout.assets.First(asset => asset.name == "Quota").position);
            foreach (var asset in layout.assets)
            {
                var part = ImportPart(asset, root.transform, axes, quotaOrigin);
                parts.Add(asset.name, part);
                var anchors = PreparePart(asset, part, axes);
                poses.Add(asset.name, anchors);
                if (asset.room)
                    AddWallLights(part, lamps, diffusers);
            }

            VerifyLayout(layout, parts, poses);
            ValuableSites.Build(parts);
            foreach (var asset in layout.assets.Where(asset => asset.room))
            foreach (var anchor in asset.anchors.Where(anchor => anchor.name.Contains("__LootSpawn_")))
            {
                Vector3 size = axes.Convert(anchor.boxSize);
                size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * GridScale;
                ValuableSites.CheckOrdinaryVolume(poses[asset.name][anchor.name], size);
            }
            ValuableSites.CheckLensRoute(parts);
            var map = Child(root.transform, "Map positions", Vector3.zero);
            foreach (var position in layout.map)
            {
                var marker = Child(map, position.name, new Vector3(position.position[0], 0, position.position[1]));
                marker.localRotation = Quaternion.Euler(0, -position.yaw, 0);
            }
            var doorPositions = Child(root.transform, "Map doors", Vector3.zero);
            foreach (var link in layout.links.Where(link => link.kind == "door"))
            {
                Transform roomDoor = Port(layout, poses, link.a, link.b);
                Vector3 local = parts[link.a].InverseTransformPoint(roomDoor.position);
                Transform flatRoom = map.Find(link.a);
                var door = Child(doorPositions, link.a + "_" + link.b, flatRoom.localPosition + flatRoom.localRotation * local);
                door.localRotation = flatRoom.localRotation * Quaternion.Inverse(parts[link.a].rotation) * roomDoor.rotation;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Folder}/{RootName}.prefab");
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, $"{Folder}/{RootName}.unity");
            AssetDatabase.SaveAssets();
            string output = Path.GetFullPath("BundleOutput");
            Directory.CreateDirectory(output);
            var bundle = new AssetBundleBuild
            {
                assetBundleName = "contour4-ring.bundle",
                assetNames = new[] { AssetDatabase.GetAssetPath(prefab), $"{Folder}/Portal.shader", $"{Folder}/Map.shader" }
            };
            var loading = BuildLoadingArt.Definition();
            var manifest = BuildPipeline.BuildAssetBundles(output, new[] { bundle, loading },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);
            if (!manifest || !File.Exists(Path.Combine(output, bundle.assetBundleName)) ||
                !File.Exists(Path.Combine(output, loading.assetBundleName)))
                throw new InvalidOperationException("The ring or loading-art bundle was not created.");
            Debug.Log($"[Contour4Ring] Bundle built; axes {axes}; {parts.Count} parts, {root.GetComponentsInChildren<Light>().Length} wall lights.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static GameObject Model(Asset asset)
    {
        string path = $"{Folder}/{asset.name}.fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.preserveHierarchy = true;
        importer.bakeAxisConversion = true;
        importer.isReadable = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static AxisMap ReadAxisMap(Asset asset, Transform parent)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(Model(asset), parent);
        try
        {
            var nodes = ByName(instance.transform);
            var axes = from x in new[] { -1, 1 }
                       from z in new[] { -1, 1 }
                       let map = new AxisMap(x, z)
                       let error = asset.anchors.Max(anchor => Vector3.Distance(
                           instance.transform.InverseTransformPoint(nodes[anchor.name].position), map.Convert(anchor.position)))
                       orderby error
                       select (map, error);
            var best = axes.First();
            if (best.error > JoinTolerance)
                throw new InvalidOperationException($"FBX axes/scale do not match the authored layout: {best.map}, {best.error:F3} m.");
            return best.map;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static Transform ImportPart(Asset asset, Transform root, AxisMap axes, Vector3 origin)
    {
        var part = new GameObject(asset.name).transform;
        part.SetParent(root, false);
        part.localPosition = (axes.Convert(asset.position) - origin) * GridScale;
        part.localRotation = axes.Rotation(asset.euler);
        part.localScale = Vector3.one * GridScale;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(Model(asset), part);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = "Model";
        return part;
    }

    private static Dictionary<string, Transform> PreparePart(Asset asset, Transform part, AxisMap axes)
    {
        var nodes = ByName(part.Find("Model"));
        foreach (string name in asset.authoringOnly ?? Array.Empty<string>())
            Object.DestroyImmediate(nodes[name].gameObject);

        foreach (var shape in asset.colliders)
        {
            var mesh = nodes[shape.@object].GetComponent<MeshFilter>().sharedMesh;
            if (shape.shape == "box")
            {
                var box = nodes[shape.@object].gameObject.AddComponent<BoxCollider>();
                box.center = mesh.bounds.center;
                box.size = mesh.bounds.size;
            }
            else if (shape.shape == "mesh")
            {
                nodes[shape.@object].gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            else
            {
                throw new InvalidOperationException($"Unknown collider shape {shape.shape} for {shape.@object}.");
            }
        }

        var anchors = new Dictionary<string, Transform>();
        var markers = Child(part, "Markers", Vector3.zero);
        foreach (var anchor in asset.anchors)
        {
            Vector3 vector = axes.Convert(anchor.position);
            float error = Vector3.Distance(vector, part.InverseTransformPoint(nodes[anchor.name].position));
            if (error > JoinTolerance)
                throw new InvalidOperationException($"FBX anchor {anchor.name} offset by {error:F3} m.");
            var marker = Child(markers, anchor.name, vector);
            marker.localRotation = Quaternion.LookRotation(axes.Convert(anchor.forward), axes.Convert(anchor.up));
            anchors.Add(anchor.name, marker);
        }
        return anchors;
    }

    private static Dictionary<string, Transform> ByName(Transform root) =>
        root.GetComponentsInChildren<Transform>(true).Where(node => node.name.Contains("__")).ToDictionary(node => node.name);

    private static void AddWallLights(Transform room, Material body, Material diffuser)
    {
        var fixtures = Child(room, "Wall lights", Vector3.zero);
        foreach (var outwards in new[] { Vector3.left, Vector3.right, Vector3.back, Vector3.forward })
        {
            // The light is above the 3.2 m aperture, including doors with their centers offset.
            var lamp = Child(fixtures, $"Wall light {fixtures.childCount + 1}", outwards * 5.79f + Vector3.up * 3.45f);
            lamp.localRotation = Quaternion.LookRotation(-outwards, Vector3.up);
            MakeBox(lamp, "Housing", Vector3.zero, new Vector3(.82f, .25f, .18f), body);
            MakeBox(lamp, "Diffuser", Vector3.forward * .108f, new Vector3(.66f, .13f, .05f), diffuser);
            var light = Child(lamp, "Point light", Vector3.forward * .21f).gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(.91f, .95f, 1f);
            light.range = 10f;
            light.intensity = 2.6f;
            light.shadows = LightShadows.None;
        }
    }

    private static void VerifyLayout(Layout layout, Dictionary<string, Transform> parts,
        Dictionary<string, Dictionary<string, Transform>> poses)
    {
        Vector3 center = (parts["Quota"].position + parts["Loot2"].position) * .5f;
        foreach (var asset in layout.assets.Where(asset => asset.room))
        {
            Transform room = parts[asset.name];
            if (Vector3.Dot(room.up, (center - room.position).normalized) < .999f)
                throw new InvalidOperationException($"{asset.name} does not face the open center.");
            var floor = ByName(room.Find("Model"))[$"{asset.name}__Floor"].GetComponent<BoxCollider>();
            if (!floor || Mathf.Abs(floor.size.x * floor.transform.lossyScale.x - 15f) > JoinTolerance)
                throw new InvalidOperationException($"{asset.name} floor does not match the 15 m grid.");
            int spawnCount = poses[asset.name].Keys.Count(name => name.Contains("__LootSpawn_"));
            if (spawnCount != (asset.name == "Quota" ? 0 : 4))
                throw new InvalidOperationException($"{asset.name} has {spawnCount} loot volumes.");
            Debug.Log($"[Contour4Ring] Room {asset.name} pos={room.position:F3} up={room.up:F3} loot={spawnCount}");
        }

        foreach (var link in layout.links.Where(link => link.kind == "bridge"))
        {
            string bridge = "Bridge" + link.a + link.b;
            Transform start = poses[bridge][bridge + "__Start"];
            Transform end = poses[bridge][bridge + "__End"];
            VerifyJoin(Port(layout, poses, link.a, link.b), start);
            VerifyJoin(Port(layout, poses, link.b, link.a), end);
        }

        VerifyJoin(Port(layout, poses, "Quota", "Truck"), poses["TruckSocket"]["TruckSocket__ExitToQuota"]);
    }

    private static Transform Port(Layout layout, Dictionary<string, Dictionary<string, Transform>> poses,
        string room, string target)
    {
        var asset = layout.assets.Single(asset => asset.name == room);
        var anchor = asset.anchors.Single(anchor => anchor.target == target);
        return poses[room][anchor.name];
    }

    private static void VerifyJoin(Transform left, Transform right)
    {
        float gap = Vector3.Distance(left.position, right.position);
        if (gap > JoinTolerance || Vector3.Dot(left.up, right.up) < .999f)
            throw new InvalidOperationException($"Disconnected join {left.name}/{right.name}: {gap:F3}m; up {left.up:F3}/{right.up:F3}.");
        Debug.Log($"[Contour4Ring] Join {left.name}/{right.name}: {gap:F4} m");
    }

    private static Transform Child(Transform parent, string name, Vector3 position)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        child.localPosition = position;
        return child;
    }

    private static Material MakeMaterial(string name, Color color, Color emission)
    {
        string path = $"{Folder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material)
            return material;
        material = new Material(Shader.Find("Standard")) { color = color };
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", emission);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void MakeBox(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        var shape = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shape.name = name;
        shape.transform.SetParent(parent, false);
        shape.transform.localPosition = position;
        shape.transform.localScale = size;
        shape.GetComponent<MeshRenderer>().sharedMaterial = material;
        Object.DestroyImmediate(shape.GetComponent<Collider>());
    }
}

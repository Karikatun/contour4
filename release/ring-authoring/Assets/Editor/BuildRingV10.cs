using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class BuildRingV10
{
    private const string Folder = "Assets/Contour4Ring";
    private const string ScenePath = Folder + "/Furniture Prototype V10.unity";
    private const string PrefabPath = Folder + "/Contour4 Ring.prefab";
    private const string BundleName = "contour4-ring.bundle";
    private static readonly string[] Rooms = { "Quota", "Loot1", "Loot2", "Loot3" };
    private static readonly string[] Bridges = { "BridgeQuotaLoot1", "BridgeLoot1Loot2" };
    private static readonly string[] Closures =
    {
        "Shaft closure negative Z", "Shaft closure positive Z",
        "Bridge side lower negative Z", "Bridge side lower positive Z",
        "Bridge side upper negative Z", "Bridge side upper positive Z"
    };
    private static readonly (string Key, Vector3 Size, string Support)[] Catalog =
    {
        ("experiment-cassette", new Vector3(.24f, .08f, .16f), "Wooden operator worktop"),
        ("control-sample", new Vector3(.12f, .30f, .12f), "Sample shelf base"),
        ("calibration-weight", new Vector3(.33f, .30f, .30f), "Calibration pallet"),
        ("sensor-rod", new Vector3(.22f, 1.65f, .22f), "Open sensor rack base"),
        ("portable-gravimeter", new Vector3(.46f, .34f, .32f), "Continuous wooden worktop"),
        ("gimbal-gyroscope", new Vector3(1.18f, .76f, .55f), "Continuous wooden worktop"),
        ("gravity-lens", new Vector3(1.35f, .95f, .52f), "Lens low plinth"),
        ("mobius-strip", new Vector3(.52f, .43f, .22f), "Demonstration sample shelf")
    };

    public static void Probe()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = scene.GetRootGameObjects().Single(go => go.name == "Contour4 Ring").transform;
        try
        {
            using var report = new StreamWriter("../source-probe.txt");
            using var baseline = OpenBundle(BaselineBundlePath());
            var old = baseline.Prefab;
            Describe(old.transform, "Baseline 0.2.26", report);
            Describe(root, "Authored Ring V10", report);
            CompareRuntimeContract(old.transform, root, report);
            Physics.SyncTransforms();
            InspectCatalog(root, report);
            InspectRandomLoot(root, report);
        }
        finally
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }
    }

    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = scene.GetRootGameObjects().Single(go => go.name == "Contour4 Ring");
        Require(PrefabUtility.GetPrefabInstanceStatus(root) == PrefabInstanceStatus.NotAPrefab,
            "V10 scene root must be an authored standalone scene object.");
        Physics.SyncTransforms();
        using (var report = new StreamWriter("../catalog-composition.txt"))
        {
            RelocateObstructedSites(root.transform, report);
            Physics.SyncTransforms();
            Require(InspectCatalog(root.transform, report) == 0, "V10 catalog placement intersects the room.");
        }
        using (var report = new StreamWriter("../furniture-batch.txt"))
            RingV10StaticFurniture.Bake(root.transform, Rooms, report);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Require(prefab && prefab.name == "Contour4 Ring", "Could not build the runtime Ring prefab.");
        AssetDatabase.SaveAssets();
        string output = Path.GetFullPath("BundleOutput");
        Directory.CreateDirectory(output);
        File.WriteAllLines("../ring-source-dependencies.txt", AssetDatabase.GetDependencies(ScenePath, true).OrderBy(path => path));
        var definition = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = new[] { PrefabPath, Folder + "/Portal.shader", Folder + "/Map.shader" }
        };
        var manifest = BuildPipeline.BuildAssetBundles(output, new[] { definition },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
            BuildTarget.StandaloneWindows64);
        Require(manifest && File.Exists(Path.Combine(output, BundleName)), "Windows AssetBundle build failed.");
    }

    public static void Verify()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        using var report = new StreamWriter("../bundle-verification.txt");
        using var baseline = OpenBundle(BaselineBundlePath());
        // Unity refuses two bundles with the same internal name. Release the baseline after binding its instance.
        var old = Object.Instantiate(baseline.Prefab).transform;
        baseline.DisposeKeepAssets();
        using var candidate = OpenBundle(Environment.GetEnvironmentVariable("CONTOUR4_RING_VERIFY_BUNDLE") ??
            "BundleOutput/" + BundleName);
        var root = Object.Instantiate(candidate.Prefab).transform;
        try
        {
            Require(candidate.Prefab.name == "Contour4 Ring", "Unexpected Ring prefab root name.");
            report.WriteLine("ASSETS " + string.Join(", ", candidate.AssetNames));
            Require(candidate.AssetNames.Length == 3, "Ring must export exactly one prefab and two shaders.");
            CompareRuntimeContract(old, root, report);
            Describe(root, "Final Windows V10 prefab", report);
            old.gameObject.SetActive(false);
            Physics.SyncTransforms();
            VerifyFurniture(root, report);
            VerifyClosures(root, report);
            Require(InspectCatalog(root, report) == 0, "Catalog sites have unsupported or obstructed valuable envelopes.");
            InspectRandomLoot(root, report);
            RingV10PhysicsChecks.Verify(root, report);
            report.WriteLine("PASS prefab, runtime frames, joints, closures, catalog sites");
        }
        finally
        {
            Object.DestroyImmediate(old.gameObject);
            Object.DestroyImmediate(root.gameObject);
        }
    }

    private static void CompareRuntimeContract(Transform old, Transform candidate, TextWriter report)
    {
        var paths = new List<string> { "Map positions", "Map doors", "TruckSocket", "TruckSocket/Model", "TruckSocket/Markers" };
        foreach (string part in Rooms.Concat(Bridges))
        {
            paths.Add(part);
            paths.Add(part + "/Model");
            foreach (Transform node in old.Find(part + "/Markers").GetComponentsInChildren<Transform>(true))
                paths.Add(AnimationUtility.CalculateTransformPath(node, old));
        }
        foreach (string group in new[] { "Map positions", "Map doors" })
        foreach (Transform node in old.Find(group))
            paths.Add(AnimationUtility.CalculateTransformPath(node, old));
        foreach (string room in Rooms)
        {
            string path = room + "/Wall lights";
            paths.Add(path);
            Require(candidate.Find(path).GetComponentsInChildren<Light>().Length >=
                old.Find(path).GetComponentsInChildren<Light>().Length, room + " lost its wall lights.");
        }
        foreach (string path in paths.OrderBy(path => path))
        {
            var a = old.Find(path);
            var b = candidate.Find(path);
            Require(a && b && a.gameObject.activeSelf == b.gameObject.activeSelf && a.gameObject.layer == b.gameObject.layer,
                "Missing or active-state/layer mismatch at " + path);
            Require(Vector3.Distance(a.localScale, b.localScale) < .0001f &&
                Vector3.Distance(old.InverseTransformPoint(a.position), candidate.InverseTransformPoint(b.position)) < .0001f &&
                Quaternion.Angle(Quaternion.Inverse(old.rotation) * a.rotation, Quaternion.Inverse(candidate.rotation) * b.rotation) < .02f,
                "Frame mismatch at " + path);
        }
        foreach (string name in Rooms)
        {
            var floor = candidate.Find(name + "/Model").GetComponentsInChildren<BoxCollider>()
                .Single(c => c.name == name + "__Floor");
            Require(floor.enabled && !floor.isTrigger, "Disabled floor for " + name);
            var volume = candidate.Find(name + "/Markers/" + name + "__RoomVolume");
            Require(volume && !volume.GetComponent<Collider>(), "Room volume is installed by the runtime: " + name);
        }
        report.WriteLine($"UNCHANGED {paths.Count} truck socket, room, map, marker and bridge frames versus installed 0.2.26.");
        report.WriteLine("BASELINE collider bounds " + Measure(old));
        report.WriteLine("CANDIDATE collider bounds " + Measure(candidate));
    }

    private static Bounds Measure(Transform root)
    {
        var colliders = root.GetComponentsInChildren<Collider>()
            .Where(c => !c.isTrigger && !c.transform.IsChildOf(root.Find("TruckSocket")));
        using var e = colliders.GetEnumerator();
        Require(e.MoveNext(), "No branch colliders.");
        var bounds = e.Current.bounds;
        while (e.MoveNext()) bounds.Encapsulate(e.Current.bounds);
        return bounds;
    }

    private static void VerifyFurniture(Transform root, TextWriter report)
    {
        Require(root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 &&
            root.GetComponentsInChildren<Transform>(true).All(t =>
                GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), "Broken or unexpected scripts in Ring prefab.");
        var joints = root.GetComponentsInChildren<Joint>(true);
        Require(joints.Length == 27 && joints.OfType<ConfigurableJoint>().Count() == 26 &&
            joints.OfType<HingeJoint>().Count() == 1, "V10 furniture mechanisms were not preserved.");
        var anchors = new[] { "Quota", "Loot3" }
            .Select(room => root.Find(room + "/V6 room-local joint anchor").GetComponent<Rigidbody>()).ToArray();
        Require(anchors.All(a => a && a.isKinematic && !a.useGravity), "Room-local anchor configuration changed.");
        foreach (var joint in joints)
        {
            var body = joint.GetComponent<Rigidbody>();
            Require(body && !body.isKinematic && !body.useGravity &&
                anchors.Contains(joint.connectedBody) && joint.transform.IsChildOf(joint.connectedBody.transform.parent),
                "Broken body or room-local joint: " + joint.name);
            Require(body.GetComponentsInChildren<BoxCollider>()
                .Count(c => c.enabled && !c.isTrigger && c.attachedRigidbody == body) >= 2, "Missing solid grip: " + joint.name);
            if (joint is ConfigurableJoint slide)
                Require(slide.xMotion == ConfigurableJointMotion.Limited && Mathf.Abs(slide.linearLimit.limit - .17f) < .0001f,
                    "Changed drawer travel: " + joint.name);
            else if (joint is HingeJoint hinge)
                Require(hinge.useLimits && hinge.limits.min == -95f && hinge.limits.max == 0f, "Changed cupboard angle.");
        }
        report.WriteLine("FURNITURE 26 × .34 m drawer travel, 1 × 95° cupboard, two kinematic room-local anchors, solid grips.");
    }

    private static void VerifyClosures(Transform root, TextWriter report)
    {
        var group = root.Find("V10 shaft side closures");
        Require(group && group.childCount == Closures.Length, "Wrong number of V10 closures.");
        for (int i = 0; i < Closures.Length; i++)
        {
            var wall = group.Find(Closures[i]);
            var collider = wall.GetComponent<Collider>();
            Require(collider && collider.enabled && !collider.isTrigger, "Non-solid closure: " + wall.name);
            Require(i < 2 ? collider is BoxCollider : collider is MeshCollider mesh && mesh.convex && mesh.sharedMesh,
                "Unexpected collider for " + wall.name);
            Require(wall.GetComponent<Renderer>().sharedMaterial.name == "V2_plaster", "V10 wall plaster changed.");
        }
        foreach (float side in new[] { -1f, 1f })
        {
            var wall = group.GetChild(side < 0 ? 0 : 1).GetComponent<Collider>();
            int hits = 0;
            for (int ix = 0; ix <= 62; ix++)
            for (int iy = 0; iy <= 62; iy++)
            {
                var ray = new Ray(root.TransformPoint(new Vector3(-7.75f + ix * .25f, 4.5375f + iy * .25f, 0)),
                    root.TransformDirection(Vector3.forward * side));
                if (wall.Raycast(ray, out _, 10f)) hits++;
            }
            Require(hits == 3969, "Central closure ray grid found a gap.");
            report.WriteLine($"CLOSURE side {side} hits {hits}/3969");
        }
    }

    private static int InspectCatalog(Transform root, TextWriter report)
    {
        int issues = 0;
        foreach (var (key, size, support) in Catalog)
        {
            var marker = FindCatalogSite(root, key);
            var hits = Physics.OverlapBox(marker.position + marker.up * (size.y * .5f),
                size * .5f - Vector3.one * .002f, marker.rotation, ~0, QueryTriggerInteraction.Ignore);
            bool grounded = Physics.Raycast(marker.position + marker.up * .025f, -marker.up,
                out RaycastHit hit, .10f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.name == support;
            if (hits.Length != 0 || !grounded) issues++;
            report.WriteLine($"CATALOG {key} room={marker.parent.parent.name} support={(hit.collider ? hit.collider.name : "NONE")} " +
                $"gap={hit.distance - .025f:F4} solids={string.Join(", ", hits.Select(c => c.name))}");
        }
        return issues;
    }

    private static Transform FindCatalogSite(Transform root, string key) =>
        Rooms.Select(room => root.Find(room + "/Catalog sites/valuable__" + key)).Single(site => site);

    private static void RelocateObstructedSites(Transform root, TextWriter report)
    {
        foreach (var (key, _, supportName) in Catalog.Where(entry =>
            entry.Key is "experiment-cassette" or "portable-gravimeter" or "gimbal-gyroscope"))
        {
            var marker = FindCatalogSite(root, key);
            var room = marker.parent.parent;
            var supports = room.GetComponentsInChildren<BoxCollider>().Where(c => c.name == supportName);
            var surface = supports.Select(support =>
            {
                var ray = new Ray(marker.position + marker.up, -marker.up);
                return support.Raycast(ray, out var hit, 1.35f) ? hit : default;
            }).Single(hit => hit.collider);
            var old = marker.position;
            marker.position = surface.point + marker.up * .012f;
            report.WriteLine($"MOVED {key} along room up only: {Vector3.Dot(marker.position - old, marker.up):F4} m");
        }
    }

    private static void InspectRandomLoot(Transform root, TextWriter report)
    {
        foreach (string roomName in Rooms)
        foreach (Transform marker in root.Find(roomName + "/Markers"))
        {
            if (!marker.name.Contains("__LootSpawn_")) continue;
            bool blocked = Physics.CheckSphere(marker.position, .09f, ~0, QueryTriggerInteraction.Ignore);
            report.WriteLine($"ORDINARY {marker.name} centerBlocked={blocked}");
            Require(!blocked, "Blocked ordinary loot marker: " + marker.name);
        }
    }

    private static void Describe(Transform root, string title, TextWriter report)
    {
        report.WriteLine(title);
        report.WriteLine(string.Join(", ", root.Cast<Transform>().Select(node => node.name)));
        report.WriteLine($"renderers={root.GetComponentsInChildren<Renderer>(true).Length} " +
            $"colliders={root.GetComponentsInChildren<Collider>(true).Length} " +
            $"aperture markers={root.GetComponentsInChildren<Transform>(true).Count(t => t.parent && t.parent.name == "Markers")} " +
            $"joints={root.GetComponentsInChildren<Joint>(true).Length} lights={root.GetComponentsInChildren<Light>(true).Length}");
        report.Flush();
    }

    private static RingBundle OpenBundle(string path) => new RingBundle(Path.GetFullPath(path));

    private static string BaselineBundlePath() =>
        Environment.GetEnvironmentVariable("CONTOUR4_RING_BASELINE_BUNDLE") ?? "../baseline/" + BundleName;

    private sealed class RingBundle : IDisposable
    {
        private AssetBundle bundle;
        internal readonly GameObject Prefab;
        internal string[] AssetNames => bundle.GetAllAssetNames();

        internal RingBundle(string path)
        {
            bundle = AssetBundle.LoadFromFile(path);
            Require(bundle, "Cannot load AssetBundle " + path);
            Prefab = bundle.LoadAsset<GameObject>(PrefabPath);
            Require(Prefab, "Missing runtime prefab in " + path);
            foreach (string name in new[] { "Portal.shader", "Map.shader" })
                Require(bundle.LoadAsset<Shader>(Folder + "/" + name), "Missing Ring shader " + name);
        }

        internal void DisposeKeepAssets() { if (bundle) bundle.Unload(false); bundle = null; }
        public void Dispose() { if (bundle) bundle.Unload(true); bundle = null; }
    }

    private static void Require(bool result, string error)
    {
        if (!result) throw new InvalidOperationException(error);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Contour4.Procedural.Catalog;
using UnityEditor;
using UnityEngine;

public static class BuildRoomPrefabs
{
    private const string BundleName = "contour4-rooms.bundle";
    private const string PreparedBundleName = "contour4-prepared-modules.bundle";
    private static readonly string[] Rooms =
    {
        "Assets/Contour4/Rooms/sample-receiving.prefab",
        "Assets/Contour4/Rooms/sample-receiving-without-research.prefab",
    };

    public static void Build()
    {
        foreach (string path in Rooms)
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(path))
                throw new InvalidOperationException($"Missing authored room prefab {path}.");

        var bundle = BuildAndOpen(BundleName, Rooms);
        try
        {
            foreach (string path in Rooms)
            {
                var prefab = bundle.LoadAsset<GameObject>(path);
                if (!prefab || prefab.GetComponentsInChildren<BoxCollider>().Length == 0 ||
                    prefab.GetComponentsInChildren<Light>().Length == 0)
                    throw new InvalidOperationException($"Packed asset {path} has no authored colliders or lights.");
            }
        }
        finally { bundle.Unload(true); }
        Debug.Log("CONTOUR4 ROOMS BUNDLE PASSED: both authored prefab paths loaded from the standalone bundle.");
    }

    public static void BuildPrepared()
    {
        string[] ids = ModuleCatalog.Rooms().Select(module => module.Id).ToArray();
        if (ids.Length == 0 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidOperationException("Expected a nonempty catalog of distinct prepared modules.");
        string[] roots = ids.Select(id => $"Assets/Contour4/PreparedModules/{id}/{id}").ToArray();
        var paths = roots.SelectMany(root => new[] { root + ".prefab", root + "-loot.json", root + "-catalog.json" }).ToArray();
        var authored = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Contour4/PreparedModules" })
            .Select(AssetDatabase.GUIDToAssetPath);
        if (!new HashSet<string>(roots.Select(root => root + ".prefab"), StringComparer.Ordinal).SetEquals(authored))
            throw new InvalidOperationException("The prepared prefab inventory differs from the catalog.");
        foreach (string path in paths)
            if (!AssetDatabase.LoadMainAssetAtPath(path))
                throw new InvalidOperationException($"Missing authored prepared asset {path}.");

        var bundle = BuildAndOpen(PreparedBundleName, paths);
        try
        {
            foreach (string root in roots)
            {
                var prefab = bundle.LoadAsset<GameObject>(root + ".prefab");
                var loot = bundle.LoadAsset<TextAsset>(root + "-loot.json");
                var catalog = bundle.LoadAsset<TextAsset>(root + "-catalog.json");
                if (!prefab || !loot || !catalog || prefab.GetComponentsInChildren<MeshRenderer>(true)
                        .Any(renderer => !renderer.sharedMaterial || !renderer.GetComponent<MeshFilter>().sharedMesh) ||
                    prefab.GetComponentsInChildren<MeshCollider>(true).All(collider => !collider.sharedMesh))
                    throw new InvalidOperationException($"Prepared module {root} did not reload with authored dependencies.");
            }
        }
        finally { bundle.Unload(true); }
        Debug.Log($"CONTOUR4 PREPARED MODULES BUNDLE PASSED: {roots.Length} prefabs with both authored sidecars and dependencies.");
    }

    private static AssetBundle BuildAndOpen(string name, string[] paths)
    {
        string output = Path.GetFullPath("BundleOutput");
        Directory.CreateDirectory(output);
        var definition = new AssetBundleBuild { assetBundleName = name, assetNames = paths };
        var manifest = BuildPipeline.BuildAssetBundles(output, new[] { definition },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
            BuildTarget.StandaloneWindows64);
        if (!manifest || manifest.GetAllDependencies(name).Length != 0)
            throw new InvalidOperationException($"Expected a self-contained bundle: {name}.");
        var bundle = AssetBundle.LoadFromFile(Path.Combine(output, name));
        if (!bundle) throw new InvalidOperationException($"Unable to reopen {name}.");
        if (new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase).SetEquals(bundle.GetAllAssetNames()))
            return bundle;
        bundle.Unload(true);
        throw new InvalidOperationException($"{name} contains an unexpected set of entry paths.");
    }
}

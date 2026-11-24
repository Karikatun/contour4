using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class LoadedCartArtComposition
{
    private const string Prepared = "Assets/Contour4/PreparedModules";
    private const string Rooms = "Assets/Contour4/Rooms";

    public static void Build()
    {
        var layouts = Directory.GetFiles("layouts", "*.json").OrderBy(path => path).ToArray();
        if (layouts.Length != 22)
            throw new InvalidOperationException($"Expected 22 authored art layouts, got {layouts.Length}.");

        foreach (string file in layouts)
        {
            var layout = JsonUtility.FromJson<RoomRedesignBuilder.Layout>(File.ReadAllText(file));
            string id = layout.id;
            var legacy = $"{Rooms}/{id}.prefab";
            var prepared = $"{Prepared}/{id}/{id}.prefab";
            var paths = new[] { legacy, prepared }.Where(path => AssetDatabase.LoadAssetAtPath<GameObject>(path)).ToArray();
            if (paths.Length != 1)
                throw new InvalidOperationException($"Expected exactly one prefab for {id}; found {paths.Length}.");
            Apply(paths[0], root => RoomRedesignBuilder.ApplyDecorativeLayout(root.transform, layout), id);
        }

        Apply($"{Prepared}/calibration-sector/calibration-sector.prefab",
            root => RoomRedesignBuilder.ApplyCalibrationStyle(root.transform), "calibration-sector");
        AssetDatabase.SaveAssets();
        BuildRoomPrefabs.BuildPrepared();
        BuildRoomPrefabs.Build();
    }

    private static void Apply(string path, Action<GameObject> dress, string id)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var baseline = new ArtCompositionAudit(root);
            dress(root);
            baseline.Verify(root, id);
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved)
                throw new InvalidOperationException($"Could not save overlaid prefab {path}.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}

using System;
using System.IO;
using Contour4.NativeRaid;
using UnityEngine;

namespace SpaceJourney;

internal sealed class LoadingArt : IDisposable
{
    private const string Folder = "Assets/Contour4Loading/";
    private const string BundleName = "contour4-loading.bundle";
    private const string Building = Folder + "level-contour4-01-building.png";
    private const string Rocks = Folder + "level-contour4-02-rocks.png";
    private const string FarBackground = Folder + "level-contour4-03-far-background.png";

    private readonly AssetBundle bundle;

    private LoadingArt(AssetBundle bundle)
    {
        this.bundle = bundle;
    }

    internal static LoadingArt Load(string directory)
    {
        string bundlePath = Path.Combine(directory, BundleName);
        var bundle = AssetBundle.LoadFromFile(bundlePath);
        if (!bundle)
            throw new InvalidOperationException("Contour-4 could not open its loading-art bundle: " + bundlePath);

        try
        {
            foreach (string path in new[] { Building, Rocks, FarBackground })
            {
                if (!bundle.LoadAsset<Sprite>(path))
                    throw new InvalidOperationException("Contour-4 loading sprite is missing: " + path);
            }

            return new LoadingArt(bundle);
        }
        catch
        {
            bundle.Unload(true);
            throw;
        }
    }

    internal void Bind(Level level)
    {
        if (level.name != Contour4.LevelName && level.name != NativeRaidFeature.LevelName)
            throw new InvalidOperationException("Loading art belongs to a Contour-4 level: " + level.name);

        level.LoadingGraphic01 = Reference(Building);
        level.LoadingGraphic02 = Reference(Rocks);
        level.LoadingGraphic03 = Reference(FarBackground);
    }

    public void Dispose()
    {
        if (bundle)
            bundle.Unload(false);
    }

    private PrefabRef<Sprite> Reference(string path)
    {
        var reference = new PrefabRef<Sprite>();
        reference.SetPrefab(bundle, path);
        return reference;
    }
}

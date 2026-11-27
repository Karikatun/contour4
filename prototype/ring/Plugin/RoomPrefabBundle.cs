using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using UnityEngine;

namespace SpaceJourney
{
    internal sealed class RoomPrefabBundle : IDisposable
    {
        internal const string BundleName = "contour4-rooms.bundle";
        internal const string PreparedBundleName = "contour4-prepared-modules.bundle";
        internal const string TrenchBundleName = "contour4-maintenance-trench.bundle";
        internal RaidRoomPrefabs Prefabs { get; }
        private readonly AssetBundle bundle;
        private readonly AssetBundle preparedBundle;
        private readonly AssetBundle trenchBundle;

        private RoomPrefabBundle(AssetBundle bundle, AssetBundle preparedBundle, AssetBundle trenchBundle, RaidRoomPrefabs prefabs)
        {
            this.bundle = bundle;
            this.preparedBundle = preparedBundle;
            this.trenchBundle = trenchBundle;
            Prefabs = prefabs;
        }

        internal static RoomPrefabBundle Load(string path, string preparedPath = null, bool requireWear = false,
            string trenchPath = null)
        {
            if (trenchPath != null && preparedPath == null)
                throw new ArgumentException("The maintenance trench bundle requires the prepared modules bundle.", nameof(preparedPath));
            AssetBundle bundle = Open(path);
            AssetBundle preparedBundle = null;
            AssetBundle trenchBundle = null;
            try
            {
                GameObject receiving = Require<GameObject>(bundle, RaidRoomPrefabs.ReceivingAsset);
                GameObject receivingWithoutResearch = Require<GameObject>(bundle, RaidRoomPrefabs.ReceivingWithoutResearchAsset);
                Dictionary<string, RaidRoomPrefabs.PreparedModule> prepared = null;
                if (preparedPath != null)
                {
                    preparedBundle = Open(preparedPath);
                    if (trenchPath != null) trenchBundle = Open(trenchPath);
                    var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    prepared = new Dictionary<string, RaidRoomPrefabs.PreparedModule>(StringComparer.Ordinal);
                    foreach (var module in ModuleCatalog.Rooms())
                    {
                        string asset = RaidRoomPrefabs.PreparedAsset(module.Id);
                        string prefab = asset + ".prefab", catalog = asset + "-catalog.json", loot = asset + "-loot.json";
                        expected.Add(prefab);
                        expected.Add(catalog);
                        expected.Add(loot);
                        var source = preparedBundle;
                        if (module.Id == MaintenanceTrench.Id && trenchBundle)
                        {
                            var trenchAssets = new HashSet<string>(trenchBundle.GetAllAssetNames(), StringComparer.OrdinalIgnoreCase);
                            if (!trenchAssets.SetEquals(new[] { prefab, catalog, loot }))
                                throw new InvalidOperationException("The maintenance trench bundle must contain exactly the trench prefab and its two sidecars.");
                            source = trenchBundle;
                        }
                        prepared.Add(module.Id, new RaidRoomPrefabs.PreparedModule(module,
                            Require<GameObject>(source, prefab), Require<TextAsset>(source, catalog),
                            Require<TextAsset>(source, loot)));
                    }
                    if (!expected.SetEquals(preparedBundle.GetAllAssetNames()))
                        throw new InvalidOperationException("The prepared bundle entry list differs from the module catalog.");
                }
                var prefabs = new RaidRoomPrefabs(receiving, receivingWithoutResearch, prepared);
                var wear = bundle.LoadAsset<Shader>(RoomSurfaceWear.ShaderPath);
                if (wear)
                    RoomSurfaceWear.Configure(wear, Require<Texture2D>(bundle, RoomSurfaceWear.FloorPath),
                        Require<Texture2D>(bundle, RoomSurfaceWear.PlasterPath), Require<Texture2D>(bundle, RoomSurfaceWear.EnamelPath));
                else if (requireWear)
                    throw new InvalidOperationException($"{bundle.name} is missing {RoomSurfaceWear.ShaderPath}.");
                return new RoomPrefabBundle(bundle, preparedBundle, trenchBundle, prefabs);
            }
            catch
            {
                bundle.Unload(true);
                if (preparedBundle) preparedBundle.Unload(true);
                if (trenchBundle) trenchBundle.Unload(true);
                throw;
            }
        }

        private static AssetBundle Open(string path)
        {
            var bundle = AssetBundle.LoadFromFile(path);
            return bundle ? bundle : throw new InvalidOperationException($"Unable to load the authored Contour-4 rooms from {path}.");
        }

        private static T Require<T>(AssetBundle bundle, string assetName) where T : UnityEngine.Object
        {
            var asset = bundle.LoadAsset<T>(assetName);
            return asset ? asset : throw new InvalidOperationException($"{bundle.name} is missing {assetName}.");
        }

        public void Dispose()
        {
            RoomSurfaceWear.Configure(null, null, null, null);
            if (trenchBundle) trenchBundle.Unload(false);
            if (preparedBundle) preparedBundle.Unload(false);
            if (bundle) bundle.Unload(false);
        }
    }
}

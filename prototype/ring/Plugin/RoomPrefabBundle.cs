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
        internal RaidRoomPrefabs Prefabs { get; }
        private readonly AssetBundle bundle;
        private readonly AssetBundle preparedBundle;

        private RoomPrefabBundle(AssetBundle bundle, AssetBundle preparedBundle, RaidRoomPrefabs prefabs)
        {
            this.bundle = bundle;
            this.preparedBundle = preparedBundle;
            Prefabs = prefabs;
        }

        internal static RoomPrefabBundle Load(string path, string preparedPath = null)
        {
            AssetBundle bundle = Open(path);
            AssetBundle preparedBundle = null;
            try
            {
                GameObject receiving = Require<GameObject>(bundle, RaidRoomPrefabs.ReceivingAsset);
                GameObject receivingWithoutResearch = Require<GameObject>(bundle, RaidRoomPrefabs.ReceivingWithoutResearchAsset);
                Dictionary<string, RaidRoomPrefabs.PreparedModule> prepared = null;
                if (preparedPath != null)
                {
                    preparedBundle = Open(preparedPath);
                    var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    prepared = new Dictionary<string, RaidRoomPrefabs.PreparedModule>(StringComparer.Ordinal);
                    foreach (var module in ModuleCatalog.Rooms())
                    {
                        string asset = RaidRoomPrefabs.PreparedAsset(module.Id);
                        string prefab = asset + ".prefab", catalog = asset + "-catalog.json", loot = asset + "-loot.json";
                        expected.Add(prefab);
                        expected.Add(catalog);
                        expected.Add(loot);
                        prepared.Add(module.Id, new RaidRoomPrefabs.PreparedModule(module,
                            Require<GameObject>(preparedBundle, prefab), Require<TextAsset>(preparedBundle, catalog),
                            Require<TextAsset>(preparedBundle, loot)));
                    }
                    if (!expected.SetEquals(preparedBundle.GetAllAssetNames()))
                        throw new InvalidOperationException("The prepared bundle entry list differs from the module catalog.");
                }
                return new RoomPrefabBundle(bundle, preparedBundle, new RaidRoomPrefabs(receiving, receivingWithoutResearch, prepared));
            }
            catch
            {
                bundle.Unload(true);
                if (preparedBundle) preparedBundle.Unload(true);
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
            if (preparedBundle) preparedBundle.Unload(false);
            if (bundle) bundle.Unload(false);
        }
    }
}

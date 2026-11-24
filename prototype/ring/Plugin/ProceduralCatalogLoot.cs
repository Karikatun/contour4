using Contour4.NativeRaid;
using System;
using System.Collections.Generic;
using Contour4.NativeRaid.Runtime;
using Contour4Valuables;
using UnityEngine;

namespace SpaceJourney;

internal static class ProceduralCatalogLoot
{
    internal static void Prepare(RaidAssembly raid) => RaidCatalogSites.Validate(raid);

    internal static void Spawn(RaidAssembly raid)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
        Physics.SyncTransforms();
        var staged = new List<GameObject>();
        try
        {
            foreach (var site in RaidCatalogSites.Sites(raid))
            {
                string key = site.name.Substring("valuable__".Length);
                var placement = RaidCatalogSites.Placement(raid, site, key);
                if (!RaidLootPrefab.Fits(ValuableApi.GetPrefab(key), placement, new Pose(site.position, site.rotation)))
                    throw new InvalidOperationException("Procedural catalog prefab does not fit site " + site.name);
                var item = ValuableApi.Spawn(key, site.position, site.rotation);
                if (!item) throw new InvalidOperationException("Procedural catalog spawn failed: " + site.name);
                // Network-created props retain their native parent and gravity configuration.
                staged.Add(item);
                Debug.Log($"[Contour4 Procedural] Catalog spawn {site.name}; seed={raid.Plan.Seed}; position={site.position:F3}.");
            }
        }
        catch
        {
            foreach (var item in staged) if (item) UnityEngine.Object.Destroy(item);
            throw;
        }
    }
}

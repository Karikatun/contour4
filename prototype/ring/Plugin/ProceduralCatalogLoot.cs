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
        var staged = new List<GameObject>();
        try
        {
            foreach (var site in RaidCatalogSites.Sites(raid))
            {
                var item = ValuableApi.Spawn(site.name.Substring("valuable__".Length), site.position, site.rotation);
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

using Contour4.NativeRaid;
using System;
using System.Collections.Generic;
using Contour4Valuables;
using UnityEngine;

namespace SpaceJourney;

// Exact catalog sites are separate from the map's ordinary random loot volumes.
internal static class CatalogLoot
{
    internal static readonly string[] Keys =
        { "experiment-cassette", "control-sample", "sensor-rod", "calibration-weight",
          "portable-gravimeter", "gimbal-gyroscope", "gravity-lens", "mobius-strip" };

    internal static void Spawn(RingRuntime ring)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
        Physics.SyncTransforms();
        var staged = new List<GameObject>();
        try
        {
            foreach (string key in Keys)
            {
                Transform marker = null;
                foreach (var room in ring.Rooms)
                {
                    var site = room.Find("Catalog sites/valuable__" + key);
                    if (!site) continue;
                    if (marker) throw new InvalidOperationException("Duplicate catalog site " + key);
                    marker = site;
                }
                if (!marker) throw new InvalidOperationException("Missing catalog site " + key);
                var placement = ValuableSites.CatalogPlacement(marker, key);
                if (!RaidLootPrefab.Fits(ValuableApi.GetPrefab(key), placement, new Pose(marker.position, marker.rotation)))
                    throw new InvalidOperationException("Catalog prefab does not fit Ring site " + key);
                var instance = ValuableApi.Spawn(key, marker.position, marker.rotation);
                if (!instance) throw new InvalidOperationException("Required valuable failed to spawn: " + key);
                staged.Add(instance);
                ring.TrackLoot(instance.GetComponent<ValuableObject>(), marker);
                Debug.Log($"[Contour4Ring] Catalog spawn {key}; site={marker.parent.parent.name}; position={marker.position:F3}; up={marker.up:F3}.");
            }
        }
        catch
        {
            // The current ring is singleplayer-only; destroy only this transaction's instances.
            foreach (var instance in staged) if (instance) UnityEngine.Object.Destroy(instance);
            throw;
        }
    }
}

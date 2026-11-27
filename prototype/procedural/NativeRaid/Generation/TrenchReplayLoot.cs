using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal static class TrenchReplayLoot
    {
        internal const string Prefab = "Valuable Manor Goblet";
        internal const int Dollars = 600;

        internal static bool InTrench(RaidLootSlot slot) => slot.Room.Plan.Module.Id == MaintenanceTrench.Id;

        internal static RaidLootSlot[] Select(IEnumerable<RaidLootSlot> slots)
        {
            var trench = slots.Where(InTrench).ToArray();
            var sites = new[] { MaintenanceTrench.SampleSite, MaintenanceTrench.SampleReserveSite };
            return sites.Select(site => trench.SingleOrDefault(slot => slot.Size == RaidLootSize.Small &&
                slot.Kind == LootSurfaceKind.Floor &&
                Vector2.Distance(new Vector2(slot.LocalBounds.center.x, slot.LocalBounds.center.z), new Vector2(site.X, site.Z)) < .01f)
                ?? throw new InvalidOperationException($"Missing trench replay sample site at {site}.")).ToArray();
        }
    }
}

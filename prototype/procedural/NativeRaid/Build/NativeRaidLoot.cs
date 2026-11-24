using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using HarmonyLib;
using UnityEngine;

namespace Contour4.NativeRaid;

internal static class NativeRaidLoot
{
    private static readonly AccessTools.FieldRef<ValuableDirector, int> MaximumCount =
        AccessTools.FieldRefAccess<ValuableDirector, int>("totalMaxAmount");
    private static readonly AccessTools.FieldRef<ValuableDirector, float> MaximumValue =
        AccessTools.FieldRefAccess<ValuableDirector, float>("totalMaxValue");
    private static readonly AccessTools.FieldRef<ValuableDirector, int> SpawnCount =
        AccessTools.FieldRefAccess<ValuableDirector, int>("valuableTargetAmount");
    private static readonly AccessTools.FieldRef<ValuableDirector, float> SpawnValue =
        AccessTools.FieldRefAccess<ValuableDirector, float>("totalCurrentValue");
    private static readonly Dictionary<ValuableDirector, RaidLootBudget.Allocation> Populating = new();
    private static readonly Dictionary<ValuableDirector, (ValuableVolume Volume, int[] Limits, int[] Original)> PendingFloor = new();

    internal static Dictionary<ValuableVolume, RaidLootSlot> Install(RaidAssembly raid)
    {
        var volumes = new Dictionary<ValuableVolume, RaidLootSlot>();
        foreach (var slot in RaidLootSites.Find(raid))
        {
            var node = RaidAssembly.Child(slot.Room.Root, $"loot:{slot.Kind}:{slot.Size}:{volumes.Count:D3}");
            node.SetLocalPositionAndRotation(slot.Pose.position, slot.Pose.rotation);
            var volume = node.gameObject.AddComponent<ValuableVolume>();
            volume.VolumeType = (ValuableVolume.Type)slot.Size;
            volumes.Add(volume, slot);
        }
        return volumes;
    }

    internal static void Patch(Harmony harmony)
    {
        harmony.CreateClassProcessor(typeof(SetupHostPatch)).Patch();
        harmony.CreateClassProcessor(typeof(SpawnValuablePatch)).Patch();
    }

    [HarmonyPatch(typeof(ValuableDirector), "SetupHost")]
    private static class SetupHostPatch
    {
        private static void Postfix(ValuableDirector __instance, ref IEnumerator __result)
        {
            var generator = LevelGenerator.Instance;
            if (!generator || !generator.Level || generator.Level.name != NativeRaidFeature.LevelName ||
                GameManager.Multiplayer() || !generator.LevelParent)
                return;
            var module = generator.LevelParent.GetComponentInChildren<NativeRaidModule>();
            if (module && module.Loot != null)
                __result = Populate(__instance, __result, module);
        }
    }

    private static IEnumerator Populate(ValuableDirector director, IEnumerator routine, NativeRaidModule module)
    {
        try
        {
            // The audited stock 0.5.2 iterator calculates the curves, discovers the native
            // volumes, and yields PrefabsPrepare before selecting and spawning any item.
            if (!routine.MoveNext())
                throw new InvalidOperationException("The stock ValuableDirector skipped prefab preparation.");
            yield return routine.Current;
            Type iterator = routine.GetType();
            int[] maximums = ReadState<int[]>(iterator, routine, "_maxAmount");
            var volumes = ReadState<List<ValuableVolume>[]>(iterator, routine, "_volumes");
            var pools = ReadState<List<PrefabRef>[]>(iterator, routine, "_valuables");
            PlanPopulation(director, module, maximums, volumes, pools);

            while (routine.MoveNext())
                yield return routine.Current;
            if (Populating.TryGetValue(director, out var plan))
                Debug.Log($"[Contour4 Loot] seed={module.LootSeed}, actual={SpawnCount(director)}/{plan.Count}, " +
                    $"value=${SpawnValue(director) * 1000f:F0}, target=${plan.Dollars}, forecast=${plan.ExpectedDollars}.");
        }
        finally
        {
            RestoreFloorLimits(director);
            Populating.Remove(director);
            (routine as IDisposable)?.Dispose();
        }
    }

    private static void RestoreFloorLimits(ValuableDirector director)
    {
        if (!PendingFloor.TryGetValue(director, out var floor)) return;
        Array.Copy(floor.Original, floor.Limits, floor.Limits.Length);
        PendingFloor.Remove(director);
    }

    private static T ReadState<T>(Type iterator, IEnumerator routine, string name)
    {
        var field = iterator.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(field => field.Name.StartsWith($"<{name}>", StringComparison.Ordinal) && field.FieldType == typeof(T));
        return (T)field.GetValue(routine);
    }

    private static void PlanPopulation(ValuableDirector director, NativeRaidModule module, int[] maximums,
        List<ValuableVolume>[] volumes, List<PrefabRef>[] pools)
    {
        var candidates = new List<RaidLootBudget.Candidate>();
        var all = new List<ValuableVolume>();
        var averageDollars = new int[volumes.Length];
        for (int type = 0; type < volumes.Length; type++)
        {
            // Resolve only stock/REPOLib-approved refs already prepared by the native iterator.
            var values = pools[type].Select(prefab => prefab.Prefab)
                .Where(prefab => prefab && prefab.TryGetComponent<ValuableObject>(out _))
                .Select(prefab => prefab.GetComponent<ValuableObject>().valuePreset)
                .Where(preset => preset)
                .Select(preset => (preset.valueMin + preset.valueMax) * .5f).ToArray();
            if (values.Length == 0)
                maximums[type] = 0;
            else
                averageDollars[type] = Mathf.RoundToInt(values.Average());
            // Use a stable authored ordering for the procedural volumes. Keep the stock
            // starter and optional ring volumes in the native lists for their ordinary loot and rare cosmetics.
            foreach (var volume in volumes[type].OrderBy(volume => volume.name, StringComparer.Ordinal)
                .ThenBy(volume => volume.transform.position.x).ThenBy(volume => volume.transform.position.y)
                .ThenBy(volume => volume.transform.position.z))
            {
                all.Add(volume);
                bool authored = module.Loot.TryGetValue(volume, out var slot);
                bool sphereShell = authored && SphericalChamber.IsVariant(slot.Room.Plan.Module.Id) &&
                    slot.Support.name == SphericalChamber.Shell;
                candidates.Add(new RaidLootBudget.Candidate(type, authored ? slot.Room.Plan.Index : -1,
                    authored ? slot.Kind : LootSurfaceKind.Floor, sphereShell));
            }
        }
        bool preparedCatalog = module.Loot.Values.Any(slot => slot.FloorId != null);
        var plan = RaidLootBudget.Select(candidates, maximums, MaximumCount(director),
            Mathf.RoundToInt(MaximumValue(director) * 1000), averageDollars, module.LootSeed, preparedCatalog);
        var chosen = plan.Picks.Select(index => all[index]).ToLookup(volume => (int)volume.VolumeType);
        for (int type = 0; type < volumes.Length; type++)
        {
            var first = chosen[type].ToArray();
            maximums[type] = first.Length;
            // Native rare/cosmetic placement advances the same volume indices after this prefix slice.
            var rest = volumes[type].Except(first).ToArray();
            volumes[type].Clear();
            volumes[type].AddRange(first);
            volumes[type].AddRange(rest);
        }
        MaximumCount(director) = plan.Count;
        MaximumValue(director) = plan.Dollars / 1000.0f;
        Populating.Add(director, plan);
        var firstFloor = plan.Picks.Select(index => all[index])
            .FirstOrDefault(volume => module.Loot.TryGetValue(volume, out var slot) && slot.Kind == LootSurfaceKind.Floor);
        if (firstFloor)
        {
            var originalLimits = (int[])maximums.Clone();
            int floorCategory = (int)firstFloor.VolumeType;
            for (int type = 0; type < maximums.Length; type++)
                if (type != floorCategory) maximums[type] = 0;
            PendingFloor.Add(director, (firstFloor, maximums, originalLimits));
        }
        Debug.Log($"[Contour4 Loot] seed={module.LootSeed}, candidates={all.Count}, allocated={plan.Count}, " +
            $"capacity by size={string.Join("/", maximums)}, target=${plan.Dollars}.");
    }

    [HarmonyPatch(typeof(ValuableDirector), "SpawnValuable")]
    private static class SpawnValuablePatch
    {
        private static void Postfix(ValuableDirector __instance, ValuableVolume __1, ValuableObject __result)
        {
            // Stock advances the volume index even when the spawn returns null.
            if (PendingFloor.TryGetValue(__instance, out var floor) && floor.Volume == __1)
                RestoreFloorLimits(__instance);
            if (!__result || !Populating.TryGetValue(__instance, out var plan))
                return;
            NativeRaidGravity.TrackRibbonSpawn(__1, __result);
            ref int maximum = ref MaximumCount(__instance);
            maximum = plan.ContinueUntil(SpawnCount(__instance), maximum);
        }
    }
}

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
    private static readonly Dictionary<ValuableDirector, (RaidLootBudget.Allocation Plan, List<PrefabRef>[] Pools,
        Dictionary<ValuableVolume, ValuableVolume> EquipmentFallbacks)>
        Populating = new();
    private static readonly Dictionary<ValuableDirector, (ValuableVolume Volume, int[] Limits, int[] Original)> PendingFloor = new();

    internal static Dictionary<ValuableVolume, RaidLootSlot> Install(RaidAssembly raid)
    {
        var volumes = new Dictionary<ValuableVolume, RaidLootSlot>();
        var bounds = raid.Rooms.ToDictionary(room => room, RaidLootPlacement.StructuralBounds);
        var pockets = raid.Rooms.Select(BrokenEquipmentLoot.Find).Where(slot => slot != null);
        foreach (var slot in RaidLootSites.Find(raid).Concat(pockets))
        {
            string name = slot.EquipmentRetrieval.HasValue
                ? "loot:equipment:" + slot.Support.name
                : $"loot:{slot.Kind}:{slot.Size}:{volumes.Count:D3}";
            var node = RaidAssembly.Child(slot.Room.Root, name);
            node.SetLocalPositionAndRotation(slot.Pose.position, slot.Pose.rotation);
            var volume = AddVolume(node, slot.Size, slot.Placement(bounds[slot.Room]), slot.Kind, slot.Room.Plan.Index);
            volumes.Add(volume, slot);
        }
        return volumes;
    }

    internal static ValuableVolume AddVolume(Transform node, RaidLootSize size, RaidLootPlacement placement,
        LootSurfaceKind kind, int roomIndex, bool ringFloor = false)
    {
        var site = node.gameObject.AddComponent<RaidAuthoredLoot>();
        site.Placement = placement;
        site.Kind = kind;
        site.RoomIndex = roomIndex;
        site.RingFloor = ringFloor;
        var volume = node.gameObject.AddComponent<ValuableVolume>();
        volume.VolumeType = (ValuableVolume.Type)size;
        return volume;
    }

    internal static void Patch(Harmony harmony)
    {
        harmony.CreateClassProcessor(typeof(SetupHostPatch)).Patch();
        harmony.CreateClassProcessor(typeof(SpawnValuablePatch)).Patch();
        harmony.CreateClassProcessor(typeof(SpawnCosmeticPatch)).Patch();
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

    internal static IEnumerator UseStockPools(ValuableDirector director, IEnumerator routine) =>
        Populate(director, routine, null);

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
            var pools = ReadState<List<PrefabRef>[]>(iterator, routine, "_valuables");
            if (module)
            {
                var maximums = ReadState<int[]>(iterator, routine, "_maxAmount");
                var volumes = ReadState<List<ValuableVolume>[]>(iterator, routine, "_volumes");
                PlanPopulation(director, module, maximums, volumes, pools);
            }
            else
            {
                Populating.Add(director, (null, pools, null));
            }

            while (routine.MoveNext())
                yield return routine.Current;
            if (module && Populating.TryGetValue(director, out var population))
                Debug.Log($"[Contour4 Loot] seed={module.LootSeed}, actual={SpawnCount(director)}/{population.Plan.Count}, " +
                    $"value=${SpawnValue(director) * 1000f:F0}, target=${population.Plan.Dollars}, forecast=${population.Plan.ExpectedDollars}.");
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
        var raid = module.GetComponent<RaidAssembly>();
        int ringRoomBase = raid.Plan.Rooms.Max(room => room.Index) + 1;
        bool ringCartAccess = raid.Plan.RingRoom is { } ringRoom &&
            raid.Plan.HasCartAccess(ringRoom, raid.Plan.RingLink.To.Dock.FloorId);
        Physics.SyncTransforms();
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
            // Keep unused authored slots available to the native cosmetic pass.
            foreach (var volume in volumes[type].OrderBy(volume => volume.name, StringComparer.Ordinal)
                .ThenBy(volume => volume.transform.position.x).ThenBy(volume => volume.transform.position.y)
                .ThenBy(volume => volume.transform.position.z))
            {
                if (IsEquipmentPocket(volume)) continue;
                var authoredSite = volume.GetComponent<RaidAuthoredLoot>();
                if (authoredSite &&
                    (authoredSite.RingFloor && type > (int)RaidLootSize.Medium && !ringCartAccess ||
                        !pools[type].Any(prefab => Fits(volume, prefab))))
                    continue;
                all.Add(volume);
                bool authored = module.Loot.TryGetValue(volume, out var slot);
                bool sphereShell = authored && SphericalChamber.IsVariant(slot.Room.Plan.Module.Id) &&
                    slot.Support.name == SphericalChamber.Shell;
                int roomIndex = authoredSite
                    ? authoredSite.RoomIndex + (authoredSite.RingFloor ? ringRoomBase : 0)
                    : -1;
                candidates.Add(new RaidLootBudget.Candidate(type, roomIndex,
                    authoredSite ? authoredSite.Kind : LootSurfaceKind.Floor, sphereShell));
            }
        }
        bool preparedCatalog = module.Loot.Values.Any(slot => slot.FloorId != null);
        var plan = RaidLootBudget.Select(candidates, maximums, MaximumCount(director),
            Mathf.RoundToInt(MaximumValue(director) * 1000), averageDollars, module.LootSeed, preparedCatalog);
        var chosen = plan.Picks.Select(index => all[index]).ToLookup(volume => (int)volume.VolumeType);
        var equipmentFallbacks = new Dictionary<ValuableVolume, ValuableVolume>();
        for (int type = 0; type < volumes.Length; type++)
        {
            var ordinaryFirst = chosen[type].ToArray();
            var first = ReplaceWithEquipmentPockets(ordinaryFirst, volumes[type], pools[type], equipmentFallbacks, module.LootSeed);
            maximums[type] = first.Length;
            // Native rare/cosmetic placement advances the same volume indices after this prefix slice.
            var rest = volumes[type].Except(ordinaryFirst).Where(volume => !IsEquipmentPocket(volume)).ToArray();
            volumes[type].Clear();
            volumes[type].AddRange(first);
            volumes[type].AddRange(rest);
        }
        MaximumCount(director) = plan.Count;
        MaximumValue(director) = plan.Dollars / 1000.0f;
        Populating.Add(director, (plan, pools, equipmentFallbacks));
        var firstFloor = plan.Picks.Select(index => all[index])
            .FirstOrDefault(volume => volume.TryGetComponent<RaidAuthoredLoot>(out var site) && site.Kind == LootSurfaceKind.Floor);
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

    private static ValuableVolume[] ReplaceWithEquipmentPockets(ValuableVolume[] ordinary,
        List<ValuableVolume> volumes, List<PrefabRef> pool, Dictionary<ValuableVolume, ValuableVolume> fallbacks, int seed)
    {
        var equipment = volumes.Where(IsEquipmentPocket).ToArray();
        if (equipment.Length == 0 || ordinary.Length == 0) return ordinary;
        var result = (ValuableVolume[])ordinary.Clone();
        foreach (var pocket in equipment)
        {
            var site = pocket.GetComponent<RaidAuthoredLoot>();
            // An optional pocket takes a Tiny slot already allocated to a furnishing
            // in the same room. It never competes with the main floor or raises the cap.
            int index = Array.FindIndex(ordinary, volume =>
                volume.TryGetComponent<RaidAuthoredLoot>(out var original) && !original.RingFloor &&
                original.RoomIndex == site.RoomIndex && original.Kind != LootSurfaceKind.Floor);
            if (index < 0 || !pool.Any(prefab => Fits(pocket, prefab))) continue;
            result[index] = pocket;
            fallbacks.Add(pocket, ordinary[index]);
            Debug.Log($"[Contour4 Loot] seed={seed}, allocated equipment pocket {pocket.name}; replaces {ordinary[index].name}.");
        }
        return result;
    }

    private static bool IsEquipmentPocket(ValuableVolume volume) =>
        volume.TryGetComponent<RaidAuthoredLoot>(out var site) && site.Placement?.EquipmentPocket == true;

    private static bool Fits(ValuableVolume volume, PrefabRef prefab)
    {
        if (!volume.TryGetComponent<RaidAuthoredLoot>(out var site)) return true;
        return prefab != null && RaidLootPrefab.Fits(prefab.Prefab, site.Placement,
            new Pose(volume.transform.position, volume.transform.rotation));
    }

    [HarmonyPatch(typeof(ValuableDirector), "SpawnCosmeticWorldObject")]
    private static class SpawnCosmeticPatch
    {
        private static bool Prefix(ValuableDirector __instance, SemiFunc.Rarity __0, int[] __1,
            List<ValuableVolume>[] __2)
        {
            var setup = __instance.cosmeticWorldObjectSetups[(int)__0];
            int type = (int)setup.volume;
            var volumes = __2[type];
            int index = __1[type];
            if (index >= volumes.Count || !volumes[index].GetComponent<RaidAuthoredLoot>()) return true;

            Physics.SyncTransforms();
            int chosen = volumes.FindIndex(index, volume => !IsEquipmentPocket(volume) && Fits(volume, setup.prefab));
            if (chosen < 0) return false;
            if (chosen != index)
            {
                var volume = volumes[chosen];
                volumes.RemoveAt(chosen);
                volumes.Insert(index, volume);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ValuableDirector), "SpawnValuable")]
    private static class SpawnValuablePatch
    {
        private static bool Prefix(ValuableDirector __instance, ref PrefabRef __0, ref ValuableVolume __1)
        {
            if (!__1.GetComponent<RaidAuthoredLoot>()) return true;

            Physics.SyncTransforms();
            if (Fits(__1, __0)) return true;
            if (Populating.TryGetValue(__instance, out var population))
            {
                var pool = population.Pools[(int)__1.VolumeType];
                if (ChooseFittingPrefab(__1, pool, ref __0)) return true;
                if (population.EquipmentFallbacks != null &&
                    population.EquipmentFallbacks.TryGetValue(__1, out var fallback) &&
                    ChooseFittingPrefab(fallback, pool, ref __0))
                {
                    Debug.Log($"[Contour4 Loot] obstructed equipment pocket {__1.name}; using {fallback.name}.");
                    __1 = fallback;
                    return true;
                }
            }
            if (PendingFloor.TryGetValue(__instance, out var floor) && floor.Volume == __1)
                RestoreFloorLimits(__instance);
            return false;
        }

        private static bool ChooseFittingPrefab(ValuableVolume volume, List<PrefabRef> pool, ref PrefabRef selected)
        {
            if (Fits(volume, selected)) return true;
            var replacement = pool.Find(prefab => Fits(volume, prefab));
            if (replacement == null) return false;
            selected = replacement;
            return true;
        }
        private static void Postfix(ValuableDirector __instance, ValuableVolume __1, ValuableObject __result)
        {
            // Stock advances the volume index even when the spawn returns null.
            if (PendingFloor.TryGetValue(__instance, out var floor) && floor.Volume == __1)
                RestoreFloorLimits(__instance);
            if (!__result || !Populating.TryGetValue(__instance, out var population) || population.Plan == null)
                return;
            if (IsEquipmentPocket(__1))
                Debug.Log($"[Contour4 Loot] equipment spawn {__1.name}; prefab={__result.name}; position={__result.transform.position:F3}.");
            NativeRaidGravity.TrackRibbonSpawn(__1, __result);
            ref int maximum = ref MaximumCount(__instance);
            maximum = population.Plan.ContinueUntil(SpawnCount(__instance), maximum);
        }
    }
}

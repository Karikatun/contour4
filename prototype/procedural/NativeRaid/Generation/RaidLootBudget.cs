using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal static class RaidLootBudget
    {
        internal readonly struct Candidate
        {
            internal readonly int Category, Room;
            internal readonly LootSurfaceKind Kind;
            internal Candidate(int category, int room, LootSurfaceKind kind)
            {
                Category = category;
                Room = room;
                Kind = kind;
            }
        }

        internal sealed class Allocation
        {
            internal readonly List<int> Picks = new();
            internal readonly int Count, Dollars;
            internal int ExpectedDollars { get; set; }
            internal Allocation(int count, int dollars)
            {
                Count = count;
                Dollars = dollars;
            }

            internal int ContinueUntil(int actualCount, int currentLimit) =>
                actualCount < Count ? Count : currentLimit;
        }

        internal static Allocation Select(IReadOnlyList<Candidate> candidates, int[] stockLimits, int ceiling,
            int stockDollars, int[] averageDollars, int seed)
        {
            int usableRooms = candidates.Where(candidate => candidate.Room >= 0 && stockLimits[candidate.Category] > 0)
                .Select(candidate => candidate.Room).Distinct().Count();
            var typeCount = new int[stockLimits.Length];
            foreach (var candidate in candidates) typeCount[candidate.Category]++;

            // The stock total item ceiling is 50. Vanilla run difficulty changes the size
            // curves and cash allowance. Compact layouts use fewer of those slots.
            float density = Mathf.Clamp(.66f + .034f * usableRooms, .66f, 1f);
            int target = Mathf.Min(ceiling, Mathf.RoundToInt(stockLimits.Sum() * density));
            var limits = new int[stockLimits.Length];
            for (int i = 0; i < limits.Length; i++)
                limits[i] = Mathf.Min(CategoryLimit(i, stockLimits[i], target), typeCount[i]);
            int count = Mathf.Min(limits.Sum(), target);
            float cashScale = Mathf.Clamp(.90f + .025f * usableRooms, .90f, 1.15f);
            var result = new Allocation(count, Mathf.RoundToInt(stockDollars * cashScale / 100f) * 100);
            var random = new System.Random(seed ^ 0x682C4471);
            int[] order = Enumerable.Range(0, candidates.Count).OrderBy(_ => random.Next()).ToArray();
            var selected = new bool[candidates.Count];
            var byRoom = new Dictionary<int, int>();
            foreach (int room in candidates.Select(candidate => candidate.Room).Distinct())
                byRoom.Add(room, 0);

            bool Pick(LootSurfaceKind? role)
            {
                if (result.Picks.Count >= count) return false;
                float pace = (result.Dollars - result.ExpectedDollars) / (float)(count - result.Picks.Count);
                int best = -1;
                float score = float.PositiveInfinity;
                foreach (int i in order)
                {
                    var item = candidates[i];
                    if (selected[i] || limits[item.Category] == 0 ||
                        (role.HasValue && (item.Room < 0 || role.Value != item.Kind)))
                        continue;
                    float fit = Mathf.Abs(averageDollars[item.Category] - pace) + byRoom[item.Room] * 260;
                    if (fit >= score) continue;
                    best = i;
                    score = fit;
                }
                if (best < 0) return false;
                var pick = candidates[best];
                selected[best] = true;
                limits[pick.Category]--;
                byRoom[pick.Room]++;
                result.ExpectedDollars += averageDollars[pick.Category];
                result.Picks.Add(best);
                return true;
            }

            Pick(LootSurfaceKind.Cabinet);
            Pick(LootSurfaceKind.Table);
            Pick(LootSurfaceKind.Raised);
            while (Pick(null)) { }
            return result;
        }

        internal static int CategoryLimit(int category, int vanilla, int target)
        {
            // Shelves and benches can replace some of the late stock allowance for large
            // floor props. The same total cap still applies, and empty native pools stay empty.
            if (vanilla == 0 || category > (int)RaidLootSize.Small) return vanilla;
            return vanilla + Mathf.Max(0, target - 25) / 2;
        }
    }
}

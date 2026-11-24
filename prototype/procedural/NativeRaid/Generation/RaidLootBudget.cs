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
            internal readonly bool IsSphereShell;
            internal Candidate(int category, int room, LootSurfaceKind kind, bool isSphereShell = false)
            {
                Category = category;
                Room = room;
                Kind = kind;
                IsSphereShell = isSphereShell;
            }
        }

        internal sealed class Allocation
        {
            internal readonly List<int> Picks = new();
            internal readonly int Count, Dollars;
            private readonly bool keepStockCashLimit;
            internal int ExpectedDollars { get; set; }
            internal Allocation(int count, int dollars, bool keepStockCashLimit)
            {
                Count = count;
                Dollars = dollars;
                this.keepStockCashLimit = keepStockCashLimit;
            }

            internal int ContinueUntil(int actualCount, int currentLimit) =>
                !keepStockCashLimit && actualCount < Count ? Count : currentLimit;
        }

        internal static Allocation Select(IReadOnlyList<Candidate> candidates, int[] stockLimits, int ceiling,
            int stockDollars, int[] averageDollars, int seed, bool preparedCatalog = false)
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
                limits[i] = Mathf.Min(preparedCatalog ? stockLimits[i] : CategoryLimit(i, stockLimits[i], target), typeCount[i]);
            int count = Mathf.Min(limits.Sum(), target);
            float cashScale = Mathf.Clamp(.90f + .025f * usableRooms, .90f, 1.15f);
            int dollars = Mathf.RoundToInt(stockDollars * cashScale / 100f) * 100;
            var result = new Allocation(count, preparedCatalog ? Mathf.Min(dollars, stockDollars) : dollars, preparedCatalog);
            var random = new System.Random(seed ^ 0x682C4471);
            int[] order = Enumerable.Range(0, candidates.Count).OrderBy(_ => random.Next()).ToArray();
            var selected = new bool[candidates.Count];
            var byRoom = new Dictionary<int, int>();
            foreach (int room in candidates.Select(candidate => candidate.Room).Distinct())
                byRoom.Add(room, 0);

            bool Pick(LootSurfaceKind? role, int? category = null)
            {
                if (result.Picks.Count >= count) return false;
                float pace = (result.Dollars - result.ExpectedDollars) / (float)(count - result.Picks.Count);
                int best = -1;
                float score = float.PositiveInfinity;
                foreach (int i in order)
                {
                    var item = candidates[i];
                    if (selected[i] || limits[item.Category] == 0 || (category.HasValue && item.Category != category.Value) ||
                        (role.HasValue && (item.Room < 0 || role.Value != item.Kind)))
                        continue;
                    float fit = Mathf.Abs(averageDollars[item.Category] - pace) + byRoom[item.Room] * 260;
                    if (role == LootSurfaceKind.Floor) fit += item.Category * 100000f;
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

            Pick(LootSurfaceKind.Floor);
            // Keep a reachable floor site for each unlocked freight size before filling
            // the remaining budget by price. The stock limits still govern each pick.
            if (preparedCatalog)
                for (int category = (int)RaidLootSize.Big; category < limits.Length; category++)
                    if (limits[category] > 0 && !result.Picks.Any(index => candidates[index].Category == category))
                        Pick(LootSurfaceKind.Floor, category);
            Pick(LootSurfaceKind.Cabinet);
            Pick(LootSurfaceKind.Table);
            Pick(LootSurfaceKind.Raised);
            while (Pick(null)) { }
            if (preparedCatalog) PreferSphereShell(candidates, order, result.Picks);
            return result;
        }

        private static void PreferSphereShell(IReadOnlyList<Candidate> candidates, int[] order, List<int> picks)
        {
            const int small = (int)RaidLootSize.Small;
            int firstSmall = picks.FindIndex(index => candidates[index].Category == small);
            if (firstSmall < 0 || IsShell(picks[firstSmall])) return;

            // The stock director consumes each size in volume order and can stop on cash.
            // Give the shell the first already-budgeted Small spawn without changing size rolls.
            int pickedShell = picks.FindIndex(IsShell);
            if (pickedShell < 0)
            {
                int shellOrder = Array.FindIndex(order, IsShell);
                if (shellOrder < 0) return;

                // Prefer replacing an ordinary floor slot so dedicated shelf and cabinet picks remain.
                int floor = picks.FindLastIndex(index => candidates[index].Category == small &&
                    candidates[index].Kind == LootSurfaceKind.Floor);
                pickedShell = floor >= 0 ? floor : firstSmall;
                picks[pickedShell] = order[shellOrder];
            }
            (picks[firstSmall], picks[pickedShell]) = (picks[pickedShell], picks[firstSmall]);

            bool IsShell(int index) => candidates[index].Category == small && candidates[index].IsSphereShell;
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

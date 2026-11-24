using System;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Contour4.NativeRaid.Editor
{
    public static class RaidLootChecks
    {
        private const int OwnerFixtureSeed = 1499861616;
        private static readonly int[] Price = { 850, 1600, 3200, 7750, 21500, 21500, 21500 };

        public static void Run()
        {
            foreach (int seed in new[] { OwnerFixtureSeed, 2133, -17 })
            {
                var first = Check(seed, 0, 30000, new[] { 15, 10, 2, 1, 0, 0, 0 });
                var seventh = Check(seed, 6, 130000, new[] { 8, 10, 7, 10, 2, 2, 2 });
                var fifteenth = Check(seed, 14, 215000, new[] { 5, 10, 10, 15, 6, 6, 6 });
                Require(first.Count < seventh.Count && seventh.Count < fifteenth.Count &&
                    first.Dollars < seventh.Dollars && seventh.Dollars < fifteenth.Dollars, $"progression seed={seed}");
            }
            var empty = RaidLootBudget.Select(Array.Empty<RaidLootBudget.Candidate>(), new[] { 1 }, 50, 30000,
                new[] { 850 }, 0);
            Require(empty.Count == 0 && empty.Picks.Count == 0, "zero actual capacity");
            CheckStartingFreight();
            CheckLateFreight();
            Debug.Log("CONTOUR4 LOOT PASSED");
        }

        private static void CheckStartingFreight()
        {
            var candidates = Enumerable.Range(0, 7).SelectMany(room => new[]
            {
                new RaidLootBudget.Candidate((int)RaidLootSize.Tiny, room, LootSurfaceKind.Floor),
                new RaidLootBudget.Candidate((int)RaidLootSize.Tiny, room, LootSurfaceKind.Table),
                new RaidLootBudget.Candidate((int)RaidLootSize.Tiny, room, LootSurfaceKind.Raised),
                new RaidLootBudget.Candidate((int)RaidLootSize.Small, room, LootSurfaceKind.Cabinet),
                new RaidLootBudget.Candidate((int)RaidLootSize.Small, room, LootSurfaceKind.Raised),
                new RaidLootBudget.Candidate((int)RaidLootSize.Medium, room, LootSurfaceKind.Table),
                new RaidLootBudget.Candidate((int)RaidLootSize.Big, room, LootSurfaceKind.Floor),
                new RaidLootBudget.Candidate((int)RaidLootSize.Wide, room, LootSurfaceKind.Floor),
            }).ToArray();
            int[] stock = { 15, 10, 2, 1, 0, 0, 0 };
            var plan = RaidLootBudget.Select(candidates, stock, 50, 30000, Price, 5, preparedCatalog: true);
            var picks = plan.Picks.Select(i => candidates[i]).ToArray();
            int big = (int)RaidLootSize.Big;
            Require(plan.Count == 25 && picks.Length == plan.Count && plan.Dollars == 30000,
                "starting freight changes stock count or cash limits");
            Require(picks[0].Kind == LootSurfaceKind.Floor && picks[0].Category == (int)RaidLootSize.Tiny &&
                picks.Count(pick => pick.Category == big) == 1 && picks.All(pick => pick.Category <= big),
                "stock-permitted Big floor slot was dropped or a locked category allocated");
        }

        private static void CheckLateFreight()
        {
            var candidates = Enumerable.Range(0, 20).SelectMany(room => new[]
            {
                new RaidLootBudget.Candidate((int)RaidLootSize.Tiny, room, LootSurfaceKind.Floor),
                new RaidLootBudget.Candidate((int)RaidLootSize.Small, room, LootSurfaceKind.Cabinet),
                new RaidLootBudget.Candidate((int)RaidLootSize.Medium, room, LootSurfaceKind.Table),
                new RaidLootBudget.Candidate((int)RaidLootSize.Big, room, LootSurfaceKind.Floor),
                new RaidLootBudget.Candidate((int)RaidLootSize.Wide, room, LootSurfaceKind.Floor),
                new RaidLootBudget.Candidate((int)RaidLootSize.Tall, room, LootSurfaceKind.Floor),
                new RaidLootBudget.Candidate((int)RaidLootSize.VeryTall, room, LootSurfaceKind.Floor),
            }).ToArray();
            int[] caps = { 5, 10, 10, 15, 6, 6, 6 };
            int[] prices = (int[])Price.Clone();
            prices[(int)RaidLootSize.Wide] = 40000;
            var plan = RaidLootBudget.Select(candidates, caps, 50, 215000, prices, 5, preparedCatalog: true);
            var picks = plan.Picks.Select(index => candidates[index]).ToArray();
            Require(plan.Count == 50 && picks.Length == plan.Count && plan.Dollars == 215000 &&
                picks.GroupBy(pick => pick.Category).All(group => group.Count() <= caps[group.Key]),
                "late freight changes stock count, cash, or category limits");
            Require(picks[0].Kind == LootSurfaceKind.Floor && picks[0].Category == (int)RaidLootSize.Tiny &&
                Enumerable.Range((int)RaidLootSize.Big, 4).All(category =>
                    picks.Any(pick => pick.Kind == LootSurfaceKind.Floor && pick.Category == category)),
                "late freight drops a compatible stock size or changes the first floor pick");
        }

        private static bool HasGrayDockFrames(int seed, Pose entrance, int level)
        {
            // The loot-only gray fixture has one frame per room. The prepared package suite covers rotated sills.
            var plan = RaidPlan.Generate(seed, entrance, level, maintenance: MaintenanceTrench.Create);
            return plan.Passages.All(passage => Vector3.Dot(passage.From.Room.Up, passage.From.Up) > .999f &&
                Vector3.Dot(passage.To.Room.Up, passage.To.Up) > .999f);
        }

        private static RaidLootBudget.Allocation Check(int seed, int level, int cash, int[] caps)
        {
            var root = new GameObject("Loot fixture");
            try
            {
                var raid = root.AddComponent<RaidAssembly>();
                var entrance = new Pose(Vector3.zero, Quaternion.identity);
                int layoutSeed = Enumerable.Range(0, 16).Select(offset => unchecked(seed + offset))
                    .Concat(Enumerable.Range(OwnerFixtureSeed, 16)).Distinct()
                    .First(candidate => HasGrayDockFrames(candidate, entrance, level));
                if (layoutSeed != seed)
                    Debug.Log($"CONTOUR4 LOOT: seed {seed} needs prepared dock frames at level {level + 1}; gray fixture uses {layoutSeed}.");
                raid.Place(layoutSeed, entrance, 0, 1 << 30, true, level);
                var interactions = RaidRoomInteractions.Attach(raid);
                var slots = RaidLootSites.Find(raid).ToArray();
                var second = RaidLootSites.Find(raid).ToArray();
                Require(slots.Length == second.Length, "slot determinism [count]");
                var catalog = RaidCatalogSites.Sites(raid).Select(site => site.name).ToArray();
                Require(catalog.Length >= 7 && catalog.Distinct().Count() == catalog.Length && catalog.Contains("valuable__field-core"),
                    "distinct thematic sites including the lower shaft field core");
                for (int i = 0; i < slots.Length; i++)
                {
                    var slot = slots[i];
                    Require(slot.Pose.position == second[i].Pose.position && slot.Pose.rotation == second[i].Pose.rotation,
                        "slot determinism [pose]");
                    Require(!slots.Skip(i + 1).Any(other => other.Room == slot.Room &&
                        other.LocalBounds.Intersects(slot.LocalBounds)), "duplicate or overlapping loot capacity");
                    CheckItem(slot, root.transform);
                }
                var candidates = slots.Select(slot => new RaidLootBudget.Candidate((int)slot.Size,
                    slot.Room.Plan.Index, slot.Kind)).ToArray();
                var plan = RaidLootBudget.Select(candidates, caps, 50, cash, Price, seed);
                Require(plan.Count == plan.Picks.Count && plan.Picks.Distinct().Count() == plan.Count,
                    "allocated count differs from distinct usable volumes");
                Require(plan.Picks.SequenceEqual(RaidLootBudget.Select(candidates, caps, 50, cash, Price, seed).Picks),
                    "allocation determinism");
                foreach (var group in plan.Picks.GroupBy(index => candidates[index].Category))
                    Require(group.Count() <= RaidLootBudget.CategoryLimit(group.Key, caps[group.Key], 50),
                        "stock category cap plus portable allowance");
                Require(Mathf.Abs(plan.ExpectedDollars - plan.Dollars) < plan.Dollars * .12f,
                    $"stock price forecast {plan.ExpectedDollars} differs from budget {plan.Dollars}");
                var chosen = plan.Picks.Select(index => slots[index]).ToArray();
                foreach (var role in new[] { LootSurfaceKind.Cabinet, LootSurfaceKind.Table, LootSurfaceKind.Raised })
                    Require(chosen.Any(slot => slot.Kind == role), $"no selected {role}");
                Require(chosen.Any(slot => slot.Size >= RaidLootSize.Medium) &&
                    (level == 0 || chosen.Any(slot => slot.Size >= RaidLootSize.Big)), "no medium/large objects selected");
                for (int placed = 1; placed <= plan.Count; placed++)
                    Require(plan.ContinueUntil(placed, placed) == plan.Count, "native cash truncation skipped allocated items");
                foreach (var slot in slots)
                {
                    if (slot.Kind == LootSurfaceKind.Cabinet)
                    {
                        var forward = slot.Room.Root.TransformDirection(slot.Pose.rotation * Vector3.forward);
                        var start = slot.WorldCenter - forward * 1.10f;
                        Require(Physics.Raycast(start, forward, out var hit, 1.10f, 1, QueryTriggerInteraction.Ignore) &&
                            hit.collider.name.EndsWith("-door", StringComparison.Ordinal),
                            $"closed cabinet not concealing slot {slot.Support.name} at {slot.Pose.position}, hit={hit.collider}");
                    }
                    var loot = new GameObject("Loaded loot volume");
                    loot.layer = 30;
                    loot.transform.SetParent(root.transform);
                    loot.transform.SetPositionAndRotation(slot.WorldCenter, slot.Room.Root.rotation * slot.Pose.rotation);
                    loot.AddComponent<BoxCollider>().size = RaidLootSites.Envelope(slot.Size);
                }
                Physics.SyncTransforms();
                int routeSamples = CheckLoadedRoutes(raid);
                foreach (var front in interactions.Cabinets)
                {
                    var hinge = front.Body.GetComponent<HingeJoint>();
                    if (!hinge) continue;
                    var closed = front.Body.rotation;
                    float angle = hinge.limits.min != 0 ? hinge.limits.min : hinge.limits.max;
                    for (int sample = 1; sample <= 20; sample++)
                    {
                        var pose = closed * Quaternion.AngleAxis(angle * sample / 20f, Vector3.up);
                        front.Body.rotation = pose;
                        front.Body.transform.rotation = pose;
                        Physics.SyncTransforms();
                        foreach (var part in front.Parts)
                        {
                            Bounds shape = part is BoxCollider box ? new Bounds(box.center, box.size) :
                                ((MeshCollider)part).sharedMesh.bounds;
                            Require(!Physics.CheckBox(part.transform.TransformPoint(shape.center),
                                Vector3.Scale(shape.size, part.transform.lossyScale) * .5f, part.transform.rotation,
                                1 << 30, QueryTriggerInteraction.Ignore),
                                $"hinge swept through a shelf item at {part.name}");
                        }
                    }
                }
                foreach (var slot in slots.Where(slot => slot.Kind == LootSurfaceKind.Cabinet))
                {
                    var forward = slot.Room.Root.TransformDirection(slot.Pose.rotation * Vector3.forward);
                    Require(!Physics.Raycast(slot.WorldCenter - forward, forward, 1f, 1, QueryTriggerInteraction.Ignore),
                        $"shelf cannot be reached in open cabinet {slot.Support.name}");
                }
                string roles = string.Join("/", chosen.GroupBy(slot => slot.Kind)
                    .Select(group => group.Key + "=" + group.Count()));
                string sizes = string.Join("/", chosen.GroupBy(slot => slot.Size)
                    .Select(group => group.Key + "=" + group.Count()));
                Debug.Log($"[Contour4 Loot fixture] seed={seed}, L{level + 1}, slots={slots.Length}, count={plan.Count}, " +
                    $"rooms={slots.Select(slot => slot.Room).Distinct().Count()}, target={plan.Dollars}, " +
                    $"forecast={plan.ExpectedDollars}, path samples={routeSamples}, {roles}, {sizes}");
                return plan;
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static int CheckLoadedRoutes(RaidAssembly raid)
        {
            int count = 0;
            foreach (var room in raid.Rooms)
            foreach (var route in room.Plan.Module.Routes)
            foreach (var sample in route.Samples(.13f))
            {
                count++;
                var center = room.Root.TransformPoint(RaidPlan.Vector(sample.Position + sample.Up * (.04f + route.Envelope.Y / 2)));
                var orientation = room.Root.rotation * Quaternion.LookRotation(RaidPlan.Vector(sample.Right), RaidPlan.Vector(sample.Up));
                Require(!Physics.CheckBox(center, RaidPlan.Vector(route.Envelope) * .499f, orientation,
                    1 << 30, QueryTriggerInteraction.Ignore),
                    $"loaded site intersects {room.Root.name}/{route.Id} at {sample.Position}");
            }
            return count;
        }
        private static void CheckItem(RaidLootSlot slot, Transform fixture)
        {
            var root = slot.Room.Root;
            Vector3 size = RaidLootSites.Envelope(slot.Size);
            Quaternion yaw = slot.Pose.rotation;
            foreach (float x in new[] { -.48f, .48f })
            foreach (float z in new[] { .02f, .98f })
            {
                Vector3 foot = slot.Pose.position + yaw * new Vector3(x * size.x, .035f, z * size.z);
                Require(Physics.Raycast(root.TransformPoint(foot), -root.up, out var hit, .08f, 1,
                    QueryTriggerInteraction.Ignore) && hit.collider == slot.Support,
                    $"unsupported footprint of {slot.Size} on {slot.Support.name}");
            }
            var hits = Physics.OverlapBox(slot.WorldCenter, size / 2 - Vector3.one * .001f,
                root.rotation * yaw, 1, QueryTriggerInteraction.Ignore);
            Require(!hits.Any(hit => hit.transform.IsChildOf(fixture)), $"spawn overlaps {slot.Support.name}");
        }

        private static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException("Contour4 loot: " + detail);
        }
    }
}

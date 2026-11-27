using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal enum RaidLootSize { Tiny, Small, Medium, Big, Wide, Tall, VeryTall }

    internal sealed class RaidLootSlot
    {
        internal readonly BakedRoom Room;
        internal readonly Collider Support;
        internal readonly LootSurfaceKind Kind;
        internal readonly RaidLootSize Size;
        internal readonly Pose Pose;
        internal readonly Bounds LocalBounds;
        internal readonly string FloorId;
        internal readonly RaidLootPlacement.Retrieval? EquipmentRetrieval;

        internal RaidLootSlot(BakedRoom room, Collider support, LootSurfaceKind kind, RaidLootSize size,
            Pose pose, Bounds localBounds, string floorId = null, RaidLootPlacement.Retrieval? equipmentRetrieval = null)
        {
            Room = room;
            Support = support;
            Kind = kind;
            Size = size;
            Pose = pose;
            LocalBounds = localBounds;
            FloorId = floorId;
            EquipmentRetrieval = equipmentRetrieval;
        }

        internal Vector3 WorldCenter => Room.Root.TransformPoint(LocalBounds.center);
        internal Pose WorldPose => new(Room.Root.TransformPoint(Pose.position), Room.Root.rotation * Pose.rotation);
        internal RaidLootPlacement Placement(Bounds interior) =>
            Room.TryFloor(FloorId, out var floor) && floor != null
                ? new RaidLootPlacement(Room.Root, EquipmentRetrieval.HasValue ? LocalBounds : interior,
                    Support, floor.FloorId, EquipmentRetrieval)
                : null;
    }

    internal static class RaidLootSites
    {
        // R.E.P.O. 0.5.2 DebugValuableMeshes, width × height × depth. Its prefab pivots
        // are at the bottom/front and ValuableDirector instantiates the marker pose verbatim.
        private static readonly Vector3[] Envelopes = {
            new(.25f, .25f, .25f), new(.50f, .50f, .50f), new(.825f, 1f, .825f),
            new(1.25f, 1.50f, 1.25f), new(3f, 2.4f, 1.2f), new(2.4f, 3f, 1.2f), new(1.25f, 3.5f, 1.25f)
        };
        internal static Vector3 Envelope(RaidLootSize size) => Envelopes[(int)size];

        internal static void Dress(GrayModule module)
        {
            var work = new RoomDressing("loot-workstation", GrayBuilder.Level);
            if (module.Id == "calibration-sector")
            {
                work.LootBlock("gauge-block", 13.35f, 0, -6.54f, -1, 2.52f, 1.45f, 1.36f);
                work.LootBlock("fixture-block", 12.40f, 0, 6.55f, 1, 2.52f, 1.45f, 1.44f);
            }
            else if (module.Id.StartsWith("sample-storage", StringComparison.Ordinal))
                work.LootBlock("packing-block", 8.00f, 0, 6.54f, 1, 2.52f, 1.45f, 1.44f);
            else if (module.Id.StartsWith("observation-lab", StringComparison.Ordinal))
                work.LootBlock("instrument-block", 8.25f, 0, 6.54f, 1, 2.52f, 1.45f, 1.36f);
            else if (module.Id == MaintenanceTrench.Id)
                work.LootBlock("repair-block", 9.45f, 0, 6.00f, 1, 2.52f, 1.45f, 1.36f);

            if (work.LootSurfaces.Count == 0) return;
            module.Dressing.Add(work);
            foreach (var part in work.Parts)
                module.Geometry.Add(Block.FromFixture(work, part));
        }

        internal static IReadOnlyList<RaidLootSlot> Find(RaidAssembly raid)
        {
            Physics.SyncTransforms();
            var result = new List<RaidLootSlot>();
            var random = new System.Random(raid.Plan.Seed ^ 0x41A76E31);
            foreach (BakedRoom room in raid.Rooms)
            {
                var interior = RaidLootPlacement.StructuralBounds(room);
                bool Valid(RaidLootSlot slot) =>
                    slot.Placement(interior)?.AllowsEnvelope(slot.WorldPose, Envelope(slot.Size)) == true;

                if (room.Prepared)
                {
                    // The prefab and its checked sidecar own the site, support, floor and stock size.
                    // Large goods also need the assembled cart route to the stock entrance.
                    foreach (var authored in raid.RoomPrefabs.PreparedLoot(room))
                    {
                        var slot = RepositionCeilingLoot(authored);
                        if (slot != authored && !Valid(authored)) continue;
                        if (slot.Size > RaidLootSize.Medium && !raid.Plan.HasCartAccess(room.Plan, slot.FloorId))
                            continue;
                        if (RaidCatalogSites.OverlapsReservedLoot(slot) ||
                            !Valid(slot))
                            continue;
                        result.Add(slot);
                    }
                    continue;
                }
                if (room.Plan == raid.Plan.ShaftTransit) continue;
                var roomSlots = new List<RaidLootSlot>();
                var routes = room.Plan.Module.Dressing.Count == 0 ? Array.Empty<Bounds>() : RouteSweeps(room).ToArray();
                var supports = room.Colliders.OfType<BoxCollider>().ToLookup(box => box.name);
                if (room.Plan.Module.Id == FreightServiceRoom.SampleReceiving)
                {
                    AddReceivingSites(room, supports, routes, roomSlots);
                    result.AddRange(roomSlots.Where(Valid));
                    continue;
                }
                foreach (var dressing in room.Plan.Module.Dressing)
                foreach (var surface in dressing.LootSurfaces)
                {
                    BoxCollider support = supports[dressing.Name(surface.Support)].Single();
                    AddSurface(room, support, surface.Kind, surface.WallSide, random, routes, roomSlots);
                }
                if (room.Plan.Module.Id.StartsWith("sample-storage", StringComparison.Ordinal))
                    AddSurface(room, supports["large-containers"].Single(), LootSurfaceKind.Raised, -1, random, routes, roomSlots);
                AddFloorBays(room, routes, roomSlots);
                result.AddRange(roomSlots.Where(Valid));
            }
            return result;
        }

        private static RaidLootSlot RepositionCeilingLoot(RaidLootSlot slot)
        {
            if (slot.Room.Plan.Module.Id != MobiusHall.Id || slot.Support.name != "roof") return slot;

            // Set the two old roof sites inside the inverted outlet field, beside the hand route.
            float target = Mathf.Sign(slot.LocalBounds.center.z) * 1.60f;
            var shift = Vector3.forward * (target - slot.LocalBounds.center.z);
            var bounds = new Bounds(slot.LocalBounds.center + shift, slot.LocalBounds.size);
            return new RaidLootSlot(slot.Room, slot.Support, slot.Kind, slot.Size,
                new Pose(slot.Pose.position + shift, slot.Pose.rotation), bounds, slot.FloorId);
        }

        private static void AddReceivingSites(BakedRoom room, ILookup<string, BoxCollider> supports, Bounds[] routes,
            List<RaidLootSlot> result)
        {
            foreach (var site in SampleReceivingLoot.Sites)
            {
                var support = supports[site.Support].Single();
                Vector3 platform = support.transform.localPosition;
                Vector3 platformSize = support.transform.localScale;
                Vector3 size = Envelope(site.Size);
                Vector3 bottom = new(site.X, platform.y + platformSize.y / 2 + .008f, site.Z);
                Vector3 center = bottom + Vector3.up * (size.y / 2);
                Vector3 forward = Vector3.forward * site.Side;
                Quaternion orientation = Quaternion.LookRotation(forward, Vector3.up);
                var bounds = new Bounds(center, size);
                if (Mathf.Abs(bottom.x - platform.x) + size.x / 2 > platformSize.x / 2 ||
                    Mathf.Abs(bottom.z - platform.z) + size.z / 2 > platformSize.z / 2 ||
                    routes.Any(bounds.Intersects) || result.Any(slot => slot.LocalBounds.Intersects(bounds)) ||
                    !HasClearance(room, center, size, orientation))
                    continue;

                if (site.Kind == LootSurfaceKind.Cabinet)
                {
                    if (!BehindCabinetDoor(room, support, center, forward)) continue;
                }
                else if (Physics.Raycast(room.Root.TransformPoint(center - forward * .90f),
                    room.Root.TransformDirection(forward), .90f, ~0, QueryTriggerInteraction.Ignore))
                    continue;

                var pose = new Pose(bottom - forward * (size.z / 2), orientation);
                result.Add(new RaidLootSlot(room, support, site.Kind, site.Size, pose, bounds));
            }
        }

        private static void AddFloorBays(BakedRoom room, Bounds[] routes, List<RaidLootSlot> result)
        {
            string id = room.Plan.Module.Id;
            var bays = id switch
            {
                "calibration-sector" => new (float X, float Z, RaidLootSize Size)[] {
                    (1.8f, 6.53f, RaidLootSize.Wide), (5.2f, 6.51f, RaidLootSize.Tall),
                    (21.4f, 6.38f, RaidLootSize.Big), (1.35f, -6.54f, RaidLootSize.VeryTall) },
                "sample-storage" or "sample-storage-narrow" => new[] {
                    (1.30f, -6.52f, RaidLootSize.VeryTall), (11.02f, 6.60f, RaidLootSize.Tall),
                    (13.78f, -4.12f, RaidLootSize.Big) },
                "observation-lab" or "observation-lab-broken-floor" => new[] {
                    (10.82f, 6.51f, RaidLootSize.Tall), (12.95f, -2.66f, RaidLootSize.VeryTall) },
                MaintenanceTrench.Id => new[] {
                    (2.0f, 5.70f, RaidLootSize.Wide), (6.48f, 5.78f, RaidLootSize.Tall),
                    (13.45f, 5.68f, RaidLootSize.VeryTall),
                    (MaintenanceTrench.SampleSite.X, MaintenanceTrench.SampleSite.Z, RaidLootSize.Small),
                    (MaintenanceTrench.SampleReserveSite.X, MaintenanceTrench.SampleReserveSite.Z, RaidLootSize.Small) },
                "shaft-plant" => new[] { (17.65f, -.37f, RaidLootSize.Small) },
                _ => Array.Empty<(float, float, RaidLootSize)>(),
            };
            float elevation = id == "shaft-plant" ? room.Plan.Module.Landmarks["gallery-spares"].Y : 0;
            foreach (var (x, z, category) in bays)
            {
                Vector3 size = Envelope(category);
                Vector3 bottom = new(x, elevation + .008f, z);
                float wallSide = id == "shaft-plant" ? 1 : Mathf.Sign(z);
                Vector3 forward = id == MaintenanceTrench.Id && category == RaidLootSize.Small
                    ? Vector3.right : Vector3.forward * wallSide;
                var rotation = Quaternion.LookRotation(forward, Vector3.up);
                var bounds = new Bounds(bottom + Vector3.up * (size.y * .5f), size);
                if (!HasClearance(room, bounds.center, size, rotation) ||
                    result.Any(slot => slot.LocalBounds.Intersects(bounds)) || routes.Any(bounds.Intersects))
                    continue;
                BoxCollider support = null;
                bool continuous = true;
                foreach (float across in new[] { -.49f, .49f })
                foreach (float depth in new[] { -.49f, .49f })
                {
                    Vector3 sample = bottom + new Vector3(size.x * across, .04f, size.z * depth);
                    if (!Physics.Raycast(room.Root.TransformPoint(sample), -room.Root.up, out var hit, .10f, ~0,
                        QueryTriggerInteraction.Ignore) || !room.Floor.Support.Contains(hit.collider) ||
                        hit.collider is not BoxCollider box || (support && support != box))
                        continuous = false;
                    else
                        support = box;
                }
                if (!continuous || !support) continue;
                var pose = new Pose(bottom - rotation * Vector3.forward * (size.z / 2), rotation);
                result.Add(new RaidLootSlot(room, support, LootSurfaceKind.Floor, category, pose, bounds));
            }
        }

        private static bool HasClearance(BakedRoom room, Vector3 center, Vector3 size, Quaternion orientation) =>
            !Physics.CheckBox(room.Root.TransformPoint(center), size / 2 - Vector3.one * .001f,
                room.Root.rotation * orientation, ~0, QueryTriggerInteraction.Ignore);

        private static IEnumerable<Bounds> RouteSweeps(BakedRoom room)
        {
            foreach (var route in room.Plan.Module.Routes)
            {
                var half = RaidPlan.Vector(route.Envelope) / 2;
                foreach (var sample in route.Samples(.14f))
                {
                    Vector3 up = RaidPlan.Vector(sample.Up);
                    Vector3 extent = Abs(RaidPlan.Vector(sample.Forward)) * half.x +
                        Abs(up) * half.y + Abs(RaidPlan.Vector(sample.Right)) * half.z;
                    yield return new Bounds(RaidPlan.Vector(sample.Position) + up * (half.y + .04f), extent * 2);
                }
            }
        }

        private static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private static void AddSurface(BakedRoom room, BoxCollider support, LootSurfaceKind kind, int wallSide,
            System.Random random, Bounds[] routes, List<RaidLootSlot> result)
        {
            var part = support.transform;
            Vector3 top = part.localPosition + Vector3.up * (part.localScale.y * .5f);
            float left = top.x - part.localScale.x * .5f + .04f;
            float right = top.x + part.localScale.x * .5f - .04f;
            float halfDepth = part.localScale.z * .5f - .04f;
            Vector3 forward = Vector3.forward * wallSide;
            // Each cut gets at most one item-sized volume. Different categories never occupy
            // the same shelf or tabletop patch at once.
            var pattern = kind switch
            {
                LootSurfaceKind.Cabinet => new[] { RaidLootSize.Small, RaidLootSize.Tiny },
                LootSurfaceKind.Table => new[] { RaidLootSize.Medium, RaidLootSize.Small, RaidLootSize.Tiny },
                _ => new[] { RaidLootSize.Big, RaidLootSize.Medium, RaidLootSize.Small, RaidLootSize.Tiny },
            };
            bool reversed = random.Next(2) == 0;
            float cursor = reversed ? right : left;
            int direction = reversed ? -1 : 1;
            for (int cut = 0; cut < 12; cut++)
            {
                bool placed = false;
                for (int i = 0; i < pattern.Length && !placed; i++)
                {
                    RaidLootSize category = pattern[(cut + i) % pattern.Length];
                    Vector3 size = Envelope(category);
                    float turn = kind switch { LootSurfaceKind.Cabinet => 0, LootSurfaceKind.Table => 7, _ => 10 };
                    var orientation = Quaternion.AngleAxis((float)(random.NextDouble() * 2 - 1) * turn, Vector3.up) *
                        Quaternion.LookRotation(forward, Vector3.up);
                    Vector3 extents = HorizontalExtents(orientation, size / 2);
                    float x = cursor + extents.x * direction;
                    if (x - extents.x < left - .001f || x + extents.x > right + .001f || extents.z > halfDepth)
                        continue;
                    // A bench instrument can occupy the back edge. Bias narrow goods toward the aisle.
                    var down = forward * Mathf.Min(.15f, halfDepth - extents.z);
                    Vector3 bottom = new Vector3(x, top.y + .008f, top.z) - down;
                    Vector3 center = bottom + Vector3.up * (size.y * .5f);
                    var bounds = new Bounds(center, extents * 2 + Vector3.up * size.y);
                    if (result.Any(slot => slot.LocalBounds.Intersects(bounds)) || routes.Any(bounds.Intersects) ||
                        !HasClearance(room, center, size, orientation) ||
                        (kind == LootSurfaceKind.Cabinet && !BehindCabinetDoor(room, support, center, forward)))
                        continue;
                    var pose = new Pose(bottom - orientation * Vector3.forward * (size.z / 2), orientation);
                    result.Add(new RaidLootSlot(room, support, kind, category, pose, bounds));
                    cursor += direction * (extents.x * 2 + .035f);
                    placed = true;
                }
                if (!placed) cursor += direction * .18f;
                if (cursor < left || cursor > right) break;
            }
        }

        private static bool BehindCabinetDoor(BakedRoom room, BoxCollider shelf, Vector3 center, Vector3 forward)
        {
            // Split doors have a narrow visual seam. Keep the item behind one real leaf.
            string cabinet = shelf.name.Substring(0, shelf.name.LastIndexOf("-shelf-", StringComparison.Ordinal));
            return Physics.Raycast(room.Root.TransformPoint(center - forward * 1.10f),
                room.Root.TransformDirection(forward), out var hit, 1.10f, ~0, QueryTriggerInteraction.Ignore) &&
                hit.collider.name.StartsWith(cabinet + "-", StringComparison.Ordinal) &&
                hit.collider.name.EndsWith("-door", StringComparison.Ordinal);
        }

        private static Vector3 HorizontalExtents(Quaternion rotation, Vector3 half)
        {
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;
            return new Vector3(Mathf.Abs(right.x) * half.x + Mathf.Abs(forward.x) * half.z, 0,
                Mathf.Abs(right.z) * half.x + Mathf.Abs(forward.z) * half.z);
        }
    }
}

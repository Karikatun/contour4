using Contour4.Procedural;

namespace Contour4.NativeRaid.Runtime
{
    internal static class SampleReceivingLoot
    {
        // X/Z are the center of an envelope at the top of a named support. Side points
        // from the retrieval aisle into the furniture, as used by native spawn pivots.
        internal readonly struct Site
        {
            internal readonly string Support;
            internal readonly LootSurfaceKind Kind;
            internal readonly RaidLootSize Size;
            internal readonly float X, Z;
            internal readonly int Side;

            internal Site(string support, LootSurfaceKind kind, RaidLootSize size, float x, float z, int side = -1)
            {
                Support = SampleReceivingDressing.Prefix + "-" + support;
                Kind = kind;
                Size = size;
                X = x;
                Z = z;
                Side = side;
            }
        }

        internal static readonly Site[] Sites =
        {
            new("intake-bench-top", LootSurfaceKind.Table, RaidLootSize.Medium, .94f, -3.49f),
            new("intake-bench-top", LootSurfaceKind.Table, RaidLootSize.Tiny, 1.54f, -3.20f),
            new("manifest-drawers-body-top", LootSurfaceKind.Raised, RaidLootSize.Small, 3.16f, -3.70f),
            new("manifest-drawers-body-top", LootSurfaceKind.Raised, RaidLootSize.Small, 3.87f, -3.70f),
            new("cold-locker-shelf-1", LootSurfaceKind.Cabinet, RaidLootSize.Small, 5.10f, -3.66f),
            new("cold-locker-shelf-1", LootSurfaceKind.Cabinet, RaidLootSize.Tiny, 5.49f, -3.68f),
            new("cold-locker-shelf-1", LootSurfaceKind.Cabinet, RaidLootSize.Tiny, 5.77f, -3.66f),
            new("cold-locker-shelf-1", LootSurfaceKind.Cabinet, RaidLootSize.Small, 6.16f, -3.66f),
            new("cold-locker-shelf-2", LootSurfaceKind.Cabinet, RaidLootSize.Small, 5.10f, -3.67f),
            new("cold-locker-shelf-2", LootSurfaceKind.Cabinet, RaidLootSize.Tiny, 5.49f, -3.69f),
            new("cold-locker-shelf-2", LootSurfaceKind.Cabinet, RaidLootSize.Tiny, 5.77f, -3.64f),
            new("cold-locker-shelf-2", LootSurfaceKind.Cabinet, RaidLootSize.Small, 6.16f, -3.64f),
            new("cold-locker-body-top", LootSurfaceKind.Raised, RaidLootSize.Tiny, 6.24f, -3.56f),
            new("packing-table-top", LootSurfaceKind.Table, RaidLootSize.Medium, 7.48f, -3.49f),
            new("packing-table-top", LootSurfaceKind.Table, RaidLootSize.Small, 8.48f, -3.32f),
            new("assay-console", LootSurfaceKind.Raised, RaidLootSize.Small, 3.30f, SampleReceivingDressing.AssayZ - .07f),
            new("assay-console", LootSurfaceKind.Raised, RaidLootSize.Tiny, 3.79f, SampleReceivingDressing.AssayZ - .13f),
            new("return-rack-low", LootSurfaceKind.Raised, RaidLootSize.Small, SampleReceivingDressing.ReturnRackX, SampleReceivingDressing.ReturnRackZ),
            new("return-rack-middle", LootSurfaceKind.Raised, RaidLootSize.Small, SampleReceivingDressing.ReturnRackX, SampleReceivingDressing.ReturnRackZ),
            new("return-rack-high", LootSurfaceKind.Raised, RaidLootSize.Tiny, SampleReceivingDressing.ReturnRackX, SampleReceivingDressing.ReturnRackZ),
            new("sorting-rack-middle", LootSurfaceKind.Raised, RaidLootSize.Small, 7.32f, 3.63f, 1),
            new("sorting-rack-middle", LootSurfaceKind.Raised, RaidLootSize.Small, 7.87f, 3.63f, 1),
            new("sorting-rack-high", LootSurfaceKind.Raised, RaidLootSize.Small, 7.78f, 3.63f, 1),
            new("sorting-rack-high", LootSurfaceKind.Raised, RaidLootSize.Tiny, 7.32f, 3.55f, 1),
            new("sorting-rack-high", LootSurfaceKind.Raised, RaidLootSize.Small, 8.31f, 3.63f, 1),
        };
    }
}

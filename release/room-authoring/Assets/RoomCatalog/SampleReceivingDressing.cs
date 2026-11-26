using Contour4.Procedural;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.NativeRaid.Runtime
{
    internal static class SampleReceivingDressing
    {
        internal const string Prefix = "receiving";
        internal const float AssayZ = -1.77f;
        internal const float ReturnRackX = 6.56f;
        internal const float ReturnRackZ = -1.92f;

        internal static RoomDressing Create()
        {
            const float nearWall = -4.125f;
            var fixtures = new RoomDressing(Prefix, Level);
            fixtures.Bench("intake-bench", 1.46f, 0, nearWall, -1, 2.18f, FixedSeat.None, powered: true, active: true);
            fixtures.LowCabinet("manifest-drawers", 3.52f, 0, -3.72f, -1, 1.44f, .80f);
            fixtures.Cabinet("cold-locker", 5.63f, 0, nearWall, -1, 1.72f, .94f, powered: true);
            fixtures.Bench("packing-table", 7.98f, 0, nearWall, -1, 2.04f, FixedSeat.None);

            // Preserve clearance for the field-core carry envelope in the through aisle.
            fixtures.Box("assay-plinth", 3.48f, .13f, AssayZ, 1.04f, .26f, 1.02f, DressingSurface.Conduit);
            fixtures.Box("assay-housing", 3.48f, .91f, AssayZ, .96f, 1.40f, .98f);
            fixtures.Box("assay-console", 3.48f, 1.65f, AssayZ, 1.02f, .10f, 1.02f, DressingSurface.Worktop);
            fixtures.Box("assay-display", 3.48f, 1.44f, AssayZ + .50f, .53f, .22f, .025f, DressingSurface.ActiveLens);

            const float board = .07f;
            // Both lower slots take the full 0.50 m Small loot envelope.
            const float low = RoomDressing.RackShelterHeight + board / 2;
            const float tierPitch = board + .54f;
            Rack(fixtures, "return-rack", ReturnRackX, ReturnRackZ, 1.24f, .78f, 2.20f,
                low, low + tierPitch, low + 2 * tierPitch);
            fixtures.Box("return-rack-back", ReturnRackX, 1.065f, -2.245f, 1.10f, 2.13f, .05f);
            foreach (int side in new[] { -1, 1 })
                fixtures.Box($"return-rack-cheek-{side}", ReturnRackX + side * .585f, RoomDressing.RackShelterHeight / 2,
                    ReturnRackZ, .07f, RoomDressing.RackShelterHeight, .68f, DressingSurface.Conduit);
            fixtures.Box("return-rack-head", ReturnRackX, 2.11f, -2.19f, 1.24f, .14f, .09f, DressingSurface.Conduit);
            Rack(fixtures, "sorting-rack", 7.88f, 3.63f, 1.84f, .81f, 2.22f, null, 1.16f, 1.74f);
            fixtures.Box("sorting-rack-side-cover", 7.00f, .5625f, 3.63f, .065f, 1.125f, .71f);
            // The inner bay keeps the original crawl approach. Its low tray deck
            // and end casings leave room for two across while the upper sorting shelves stay in use.
            fixtures.Box("sorting-rack-shelter-roof", 7.88f, .875f, 3.63f, 1.71f, board, .71f, DressingSurface.Worktop);
            fixtures.Box("sorting-rack-left-casing", 7.16625f, .42f, 3.63f, .2675f, .84f, .71f, DressingSurface.Conduit);
            fixtures.Box("sorting-rack-right-casing", 8.64f, .42f, 3.63f, .36f, .84f, .71f, DressingSurface.Conduit);
            fixtures.Box("sorting-rack-back", 7.88f, 1.13f, 4.01f, 1.72f, 2.16f, .05f);
            fixtures.Box("sorting-rack-fascia", 7.88f, 2.13f, 3.26f, 1.84f, .18f, .065f, DressingSurface.Conduit);
            fixtures.Box("sorting-rack-crate", 8.44f, 1.33f, 3.75f, .56f, .27f, .43f);

            fixtures.TaskPendant("intake-task", 1.55f, Height, -3.01f, .48f, LampDuty.Work);
            fixtures.TaskPendant("cold-locker-task", 5.42f, Height, -3.0f, .43f, LampDuty.Standby);
            fixtures.TaskPendant("sorting-task", 7.85f, Height, 3.33f, .39f, LampDuty.Work);
            fixtures.WallLamp("west-guidance", .94f, 2.44f, nearWall, -1, LampDuty.Guidance, .28f);
            fixtures.AisleStrip("east-guidance", 8.15f, .39f, nearWall, -1, -.60f);
            return fixtures;
        }

        private static void Rack(RoomDressing fixtures, string id, float x, float z, float width, float depth,
            float height, float? low, float middle, float high)
        {
            foreach (int side in new[] { -1, 1 })
            foreach (int end in new[] { -1, 1 })
                fixtures.Box(id + "-post-" + side + "-" + end, x + side * (width - .07f) / 2, height / 2,
                    z + end * (depth - .07f) / 2, .07f, height, .07f, DressingSurface.SteelTrim);
            if (low.HasValue)
                fixtures.Box(id + "-low", x, low.Value, z, width - .12f, .07f, depth - .10f, DressingSurface.Worktop);
            fixtures.Box(id + "-middle", x, middle, z, width - .12f, .07f, depth - .10f, DressingSurface.Worktop);
            fixtures.Box(id + "-high", x, high, z, width - .12f, .07f, depth - .10f, DressingSurface.Worktop);
        }
    }
}

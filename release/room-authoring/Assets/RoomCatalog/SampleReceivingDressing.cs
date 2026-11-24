using Contour4.Procedural;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.NativeRaid.Runtime
{
    internal static class SampleReceivingDressing
    {
        internal const string Prefix = "receiving";
        internal const float AssayZ = -1.77f;
        internal const float ReturnRackX = 6.56f;

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

            Rack(fixtures, "return-rack", ReturnRackX, -1.82f, .76f, .62f, 1.90f, .35f, .93f, 1.53f);
            fixtures.Box("return-rack-back", ReturnRackX, .93f, -2.12f, .68f, 1.77f, .05f);
            fixtures.Box("return-rack-head", ReturnRackX, 1.79f, -2.07f, .77f, .14f, .09f, DressingSurface.Conduit);
            Rack(fixtures, "sorting-rack", 7.88f, 3.63f, 1.84f, .81f, 2.22f, null, 1.16f, 1.74f);
            fixtures.Box("sorting-rack-side-cover", 7.00f, .5625f, 3.63f, .065f, 1.125f, .71f);
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

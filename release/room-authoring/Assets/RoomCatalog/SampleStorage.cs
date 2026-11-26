using System.Linq;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class SampleStorage
    {
        internal const float RackStart = 6.80f;
        internal const float RackEnd = 10.20f;
        internal const float RackFront = 2.70f;
        internal const float RackShelterDepth = .68f;
        internal const float NearShelterX = 9.64f;
        internal const float FarShelterX = 8.50f;

        public static GrayModule Create(bool narrow = false)
        {
            var room = new GrayBuilder(narrow ? "sample-storage-narrow" : "sample-storage", 15, 15, narrow ? 1 : 0);
            room.Floor("ground", 0, 15, 0, -7.5f, 7.5f);
            room.RoofHatch("upper-hatch", "ground", 0, 15, Height, -7.5f, 7.5f, 10.5f, -6, 2.4f, 2.75f);
            var west = room.Door("main-entry", "ground", Pose(0, 0, 0, -Vector3.UnitX), Cargo.Cart);
            var east = room.Door("sample-door", "ground", Pose(15, 0, 3.9f), Cargo.Cart,
                DockUse.Physical | DockUse.DoorPortal);
            room.RectangleWalls(0, 15, 0, Height, -7.5f, 7.5f, west: west, east: east);
            float inner = narrow ? .65f : 1.1f;
            StorageRack("near-rack", -1, NearShelterX);
            StorageRack("far-rack", 1, FarShelterX);
            room.Box("large-containers", BlockRole.Obstacle, 10.8f, 12.3f, 0, 1.7f, -3.1f, -.7f);
            room.Module.Landmarks.Add("containers", new Vector3(10.25f, 0, 4.65f));
            room.Module.Landmarks.Add("hand-samples", new Vector3(10.8f, 0, 0));
            room.Module.Landmarks.Add("hatch-landing", new Vector3(11.7f, 0, -4.8f));
            room.Route("short-aisle-hand-cargo", west.Id, "hand-samples", Cargo.Hand,
                Pose(-1.8f, 0, 0), Pose(10.8f, 0, 0));
            room.Route("hatch-landing-approach", west.Id, "hatch-landing", Cargo.Hand,
                new[] { Pose(-1.8f, 0, 0) }.Concat(LaneChange(.4f, 6.4f, 0, -4.8f))
                .Append(Pose(11.7f, 0, -4.8f)).ToArray());
            var cart = new[] { Pose(-1.8f, 0, 0) }.Concat(LaneChange(.4f, 6.4f, 0, 4.65f))
                .Append(Pose(10.25f, 0, 4.65f)).ToArray();
            room.Route("load-containers", west.Id, "containers", Cargo.Cart, cart);
            room.Route("wide-aisle-cart", west.Id, east.Id, Cargo.Cart, cart
                .Concat(LaneChange(10.8f, 14.6f, 4.65f, 3.9f)).Append(Pose(16.8f, 0, 3.9f)).ToArray());
            var fitout = new RoomDressing("samples", Level);
            fitout.Bench("intake-inventory", 3.75f, 0, 7.25f, 1, 2.55f, FixedSeat.Chair, powered: true);
            fitout.Cabinet("cold-monitor-rack", 13.10f, 0, -7.25f, -1, 1.35f, .93f, powered: true);
            fitout.LowCabinet("specimen-drawers", 5.45f, 0, -6.46f, -1, 1.55f);
            fitout.LockerBank("sample-lockers", 8.4f, 0, -7.25f, -1, 3);
            fitout.PartsRack("packing-trays", 13.7f, 0, 7.25f, 1, 1.14f);
            fitout.WallLamp("inventory-standby", 3.9f, 3.15f, 7.25f, 1, LampDuty.Standby);
            fitout.WallLamp("freight-aisle-marker", 11.5f, 2.82f, 7.25f, 1, LampDuty.Guidance, .5f);
            fitout.WallLamp("hatch-approach-marker", 11.7f, 2.65f, -7.25f, -1, LampDuty.Guidance);
            fitout.TaskPendant("hand-aisle-standby", 8.75f, Height, 0, .52f, LampDuty.Standby);
            fitout.TaskPendant("archive-drawers-reserve", 4.26f, Height, -4.52f, .66f, LampDuty.Standby, -.62f);
            fitout.TaskPendant("archive-lockers-reserve", 8.3f, Height, -4.79f, .47f, LampDuty.Standby, -.84f);
            fitout.CeilingLamp("freight-intersection-safety", 6.85f, Height, 4.65f, LampDuty.Guidance);
            fitout.AisleStrip("intake-return-strip", 4.45f, .36f, -7.25f, -1, .80f);
            fitout.VerifyCover("cold-monitor-rack-body", new Vector3(11.70f, 0, -6.86f),
                new Vector3(14.38f, 0, -6.86f), new Vector3(0, 0, 1.09f));
            fitout.VerifyLowCover("specimen-drawers-body", new Vector3(4.07f, 0, -6.46f),
                new Vector3(6.90f, 0, -6.46f), new Vector3(0, 0, 1.04f), new Vector3(1.4f, 0, -1.6f));
            room.AddDressing(fitout);
            return room.Module;

            void StorageRack(string id, int side, float axis)
            {
                const float height = RoomDressing.RackShelterHeight;
                const float halfBay = RoomDressing.RackShelterWidth / 2;
                float backOfBay = RackFront - RackShelterDepth;
                RackPart("shelter-roof", RackStart, RackEnd, height, 2.4f, inner, RackFront);
                RackPart("base-left", RackStart, axis - halfBay, 0, height, inner, RackFront);
                RackPart("base-right", axis + halfBay, RackEnd, 0, height, inner, RackFront);
                RackPart("shelter-back", axis - halfBay, axis + halfBay, 0, height, inner, backOfBay);

                void RackPart(string part, float x0, float x1, float y0, float y1, float from, float to) =>
                    room.Box($"{id}-{part}", BlockRole.Obstacle, x0, x1, y0, y1,
                        side < 0 ? -to : from, side < 0 ? -from : to);
            }
        }
    }
}

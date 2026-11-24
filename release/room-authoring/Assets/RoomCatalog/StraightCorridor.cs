using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class StraightCorridor
    {
        public static GrayModule Create(bool longForm = false)
        {
            float length = longForm ? 15 : 7.5f;
            const float half = 2.75f;
            var corridor = new GrayBuilder(longForm ? "straight-corridor-long" : "straight-corridor-short", 2 * half, length);
            corridor.Floor("ground", 0, length, 0, -half, half);
            corridor.Roof(0, length, Height, -half, half);
            var west = corridor.Door("west", "ground", Pose(0, 0, 0, -Vector3.UnitX), Cargo.Cart);
            var east = corridor.Door("east", "ground", Pose(length, 0, 0), Cargo.Cart);
            corridor.RectangleWalls(0, length, 0, Height, -half, half, west: west, east: east);
            corridor.Route("through", west.Id, east.Id, Cargo.Cart, Pose(-1.8f, 0, 0), Pose(length + 1.8f, 0, 0));
            var fitout = new RoomDressing("transit", Level);
            fitout.AisleStrip("lower-aisle-start", 2.7f, .41f, -2.5f, -1, .72f);
            fitout.WallLamp("lower-aisle-end", length - 2.7f, 2.2f, 2.5f, 1, LampDuty.Guidance, -.62f);
            if (longForm)
                fitout.WallLamp("middle-standby", length / 2, 2.91f, -2.5f, -1, LampDuty.Standby);
            corridor.AddDressing(fitout);
            return corridor.Module;
        }
    }
}

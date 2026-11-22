using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class StorageCupboard
    {
        public static GrayModule Create()
        {
            var room = new GrayBuilder("service-cupboard", 7.5f, 7.5f);
            room.Floor("ground", 0, 7.5f, 0, -3.75f, 3.75f);
            room.Roof(0, 7.5f, Height, -3.75f, 3.75f);
            var door = room.Door("service-entry", "ground", Pose(0, 0, 0, -Vector3.UnitX),
                Cargo.Hand, width: Carry, height: 2.5f);
            room.RectangleWalls(0, 7.5f, 0, Height, -3.75f, 3.75f, west: door);
            room.Box("supplies", BlockRole.Obstacle, 5.8f, 7.25f, 0, 1.2f, -3f, 3f);
            room.Module.Landmarks.Add("hand-loot", new Vector3(5.2f, 0, 0));
            room.Route("retrieve-hand-loot", "service-entry", "hand-loot", Cargo.Hand,
                Pose(-1.8f, 0, 0), Pose(5.2f, 0, 0));
            var fitout = new RoomDressing("service", Level);
            fitout.Bench("maintenance-table", 3.9f, 0, -3.5f, -1, 2.1f, FixedSeat.Chair);
            fitout.Cabinet("tool-locker", 3.65f, 0, 3.5f, 1, 1.25f, 1.32f);
            fitout.LowCabinet("stacked-spares", 1.34f, 0, -3.06f, -1, 1.22f, stacked: true);
            fitout.WallLamp("battery-service-light", 3.6f, 2.95f, -3.5f, -1, LampDuty.Standby);
            fitout.TaskPendant("spare-drawers-battery-light", 1.62f, Height, -1.42f, .53f, LampDuty.Standby, -.53f);
            fitout.WallLamp("exit-light", 1.45f, 2.52f, 3.5f, 1, LampDuty.Guidance, -.36f);
            fitout.AisleStrip("battery-return-strip", 4.6f, .39f, -3.5f, -1, -.7f);
            fitout.VerifyCover("tool-locker-body", new Vector3(2.45f, 0, 2.85f), new Vector3(4.98f, 0, 2.85f),
                new Vector3(0, 0, -1.35f));
            room.AddDressing(fitout);
            return room.Module;
        }
    }
}

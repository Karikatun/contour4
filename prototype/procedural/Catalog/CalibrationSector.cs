using System.Linq;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class CalibrationSector
    {
        public static GrayModule Create()
        {
            var room = new GrayBuilder("calibration-sector", 15, 22.5f);
            room.Floor("ground", 0, 22.5f, 0, -7.5f, 7.5f);
            room.Roof(0, 22.5f, Height, -7.5f, 7.5f);
            var west = room.Door("main-entry", "ground", Pose(0, 0, 0, -Vector3.UnitX), Cargo.Cart);
            var east = room.Door("equipment-exit", "ground", Pose(22.5f, 0, 0), Cargo.Cart,
                DockUse.Physical | DockUse.DoorPortal);
            room.RectangleWalls(0, 22.5f, 0, Height, -7.5f, 7.5f, west: west, east: east);
            room.Box("calibration-stand", BlockRole.Obstacle, 8, 14.5f, 0, 2.5f, -2, 2);
            room.Module.Landmarks.Add("equipment", new Vector3(5.8f, 0, 0));
            room.Route("heavy-equipment-approach", west.Id, "equipment", Cargo.Cart,
                Pose(-1.8f, 0, 0), Pose(5.8f, 0, 0));
            foreach (float side in new[] { -1f, 1f })
            {
                float lane = 4.5f * side;
                room.Route(side < 0 ? "near-aisle" : "far-aisle", west.Id, east.Id, Cargo.Cart,
                    new[] { Pose(-1.8f, 0, 0) }.Concat(LaneChange(.8f, 7.25f, 0, lane))
                    .Concat(LaneChange(15.25f, 21.7f, lane, 0))
                    .Append(Pose(24.3f, 0, 0)).ToArray());
            }
            var fitout = new RoomDressing("calibration", Level);
            fitout.Bench("measurement-bench", 4.7f, 0, -7.25f, -1, 3.1f, FixedSeat.Chair,
                powered: true, active: true);
            fitout.Bench("removed-gyro-table", 18.6f, 0, 7.25f, 1, 2.9f, FixedSeat.None);
            fitout.Cabinet("meter-cabinet", 20.15f, 0, -7.25f, -1, 1.95f, 1.52f, powered: true);
            fitout.LowCabinet("gauge-drawers", 9.38f, 0, -6.72f, -1, 1.87f);
            fitout.PartsRack("removed-gyro-parts", 8.15f, 0, 7.25f, 1, 1.62f);
            // The isolation rack sits between the two freight aisles, behind the main apparatus.
            fitout.LootBlock("isolated-regulator-rack", 16.65f, 0, 0, 1, 2.15f, 1.54f, 1.30f);
            fitout.Box("regulator-bus", 16.65f, 1.42f, 0, 1.66f, .24f, .42f, DressingSurface.Conduit);
            fitout.Box("regulator-status", 16.9f, 1.29f, .781f, .58f, .12f, .018f,
                DressingSurface.StandbyLens);
            fitout.TaskPendant("isolator-inspection-light", 16.65f, Height, 0, .62f, LampDuty.Work);
            fitout.Box("field-stand-controller", 11.35f, 2.63f, 1.6f, 1.68f, .26f, .47f, DressingSurface.Worktop);
            fitout.Box("field-stand-indication", 11.7f, 2.64f, 1.842f, .50f, .095f, .025f, DressingSurface.StandbyLens);
            fitout.OverheadFeed("field-stand-control", 11.35f, 2.7f, 1.6f, Height, 7.25f, 1);
            fitout.TaskPendant("field-stand-inspection-west", 8.8f, Height, 0, .47f, LampDuty.Work);
            fitout.TaskPendant("field-stand-inspection-east", 13.4f, Height, 0, .47f, LampDuty.Work);
            fitout.CeilingLamp("active-approach-overhead", 4.55f, Height, -2.5f, LampDuty.Work);
            fitout.CeilingLamp("active-service-aisle-overhead", 12.3f, Height, -4.5f, LampDuty.Work);
            fitout.CeilingLamp("far-freight-aisle-overhead", 14.15f, Height, 4.45f, LampDuty.Work);
            fitout.WallLamp("instrument-task-light", 4.65f, 3.39f, -7.25f, -1, LampDuty.Work);
            fitout.WallLamp("closed-bench-marker", 18.65f, 2.9f, 7.25f, 1, LampDuty.Standby);
            fitout.WallLamp("reserve-aisle-east", 19.8f, 2.4f, -7.25f, -1, LampDuty.Guidance, .32f);
            fitout.TaskPendant("meter-cabinet-reserve", 20.25f, Height, -3.95f, .51f, LampDuty.Standby, -.71f);
            fitout.AisleStrip("gauge-service-strip", 11.5f, .39f, -7.25f, -1, -.88f);
            fitout.VerifyCover("meter-cabinet-body", new Vector3(18.62f, 0, -6.65f),
                new Vector3(21.68f, 0, -6.65f), new Vector3(0, 0, 1.47f));
            fitout.VerifyLowCover("gauge-drawers-body", new Vector3(7.78f, 0, -6.72f),
                new Vector3(11.04f, 0, -6.72f), new Vector3(0, 0, .96f), new Vector3(7.78f, 0, -4.5f));
            room.AddDressing(fitout);
            return room.Module;
        }
    }
}

using System.Linq;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class ShaftPlant
    {
        public static GrayModule Create() => Create(cupboardAnnex: false);

        internal static GrayModule Create(bool cupboardAnnex)
        {
            const float length = 22.5f;
            const float middleY = 3.6f;
            const float topY = 7.2f;
            const float ceiling = 11.2f;
            const float stairLine = -5.25f;
            var room = new GrayBuilder("shaft-plant", 15, length);
            room.Floor("ground", 0, length, 0, -7.5f, 7.5f);
            room.Floor("middle-landing", 9.75f, 11, middleY, -7.5f, -3.25f);
            room.Floor("upper-landing", 19, length, topY, -7.5f, -3.25f);
            room.Roof(0, length, ceiling, -7.5f, 7.5f);
            var west = room.Door("freight-entry", "ground", Pose(0, 0, 0, -Vector3.UnitX), Cargo.Cart);
            var upper = room.Door("upper-exit", "upper-landing", Pose(length, topY, stairLine),
                Cargo.Hand, width: cupboardAnnex ? Carry : 1.8f, height: 2.5f, depth: 2.5f);
            room.RectangleWalls(0, length, 0, ceiling, -7.5f, 7.5f, west: west, east: upper);
            if (cupboardAnnex)
            {
                // The walkable upper deck already fills the top of this wall segment.
                room.Module.Geometry.RemoveAll(block => block.Id == "east-wall-sill");
                room.Box("east-wall-sill", BlockRole.Shell, length - Skirt, length, 0, topY - Skirt,
                    stairLine - Carry / 2, stairLine + Carry / 2);
            }
            room.Box("shaft-apparatus", BlockRole.Obstacle, 7, 15, 0, 3.8f, 3.5f, 6.75f);
            room.Box("middle-guard", BlockRole.Shell, 9.75f, 11, middleY, middleY + 1, -3.45f, -3.25f);
            room.Box("upper-guard", BlockRole.Shell, 19, length - Skirt, topY, topY + 1, -3.45f, -3.25f);
            room.Module.Landmarks.Add("heavy-parts", new Vector3(14, 0, 1.35f));
            room.Module.Landmarks.Add("stairs-bottom", new Vector3(1.1f, 0, stairLine));
            room.Module.Landmarks.Add("intermediate-loot", new Vector3(10.35f, middleY, stairLine));
            room.Module.Landmarks.Add("upper-loot", new Vector3(20, topY, stairLine));
            var lowerStairs = room.Stairs("lower-stair", 1.75f, 9.75f, 0, middleY, -6.5f, -4, 20);
            var upperStairs = room.Stairs("upper-stair", 11, 19, middleY, topY, -6.5f, -4, 20);
            room.Route("freight-access", west.Id, "heavy-parts", Cargo.Cart,
                new[] { Pose(-1.8f, 0, 0) }.Concat(LaneChange(1, 6, 0, 1.35f)).Append(Pose(14, 0, 1.35f)).ToArray());
            room.Route("entry-to-stairs", west.Id, "stairs-bottom", Cargo.Hand, Pose(-1.8f, 0, 0),
                Pose(1.1f, 0, 0), Pose(1.1f, 0, 0, -Vector3.UnitZ),
                Pose(1.1f, 0, stairLine, -Vector3.UnitZ), Pose(1.1f, 0, stairLine));
            room.Module.Routes.Add(new HaulRoute("first-flight", "stairs-bottom", "intermediate-loot", Cargo.Hand,
                new[] { Pose(1.1f, 0, stairLine) }.Concat(lowerStairs).Append(Pose(10.35f, middleY, stairLine)).ToArray(),
                stepClearance: .39f));
            room.Module.Routes.Add(new HaulRoute("second-flight", "intermediate-loot", "upper-loot", Cargo.Hand,
                new[] { Pose(10.35f, middleY, stairLine) }.Concat(upperStairs).Append(Pose(20, topY, stairLine)).ToArray(),
                stepClearance: .39f));
            room.Route("upper-exit-traverse", "upper-loot", upper.Id, Cargo.Hand,
                Pose(20, topY, stairLine), Pose(24.3f, topY, stairLine));
            var fitout = new RoomDressing("plant", Level);
            fitout.Bench("service-workbench", 17.35f, 0, -7.25f, -1, 2.7f, FixedSeat.Chair,
                powered: true, active: true);
            fitout.Cabinet("feed-switchgear", 17.35f, 0, 7.25f, 1, 2.45f, 1.58f, powered: true);
            fitout.LowCabinet("pump-service-drawers", 4.82f, 0, 5.42f, 1, 1.77f);
            fitout.PartsRack("pump-spares-rack", 20.7f, 0, 7.25f, 1, 1.53f);
            fitout.Box("shaft-apparatus-meter", 10.8f, 3.56f, 6.82f, 1.18f, .43f, .18f);
            fitout.EquipmentFeed("shaft-apparatus-feed", 10.8f, 3.61f, 6.86f, 4.42f, 7.25f, 1);
            fitout.WallLamp("pump-task-west", 9.25f, 4.75f, 7.25f, 1, LampDuty.Work);
            fitout.WallLamp("pump-task-east", 15.9f, 4.2f, 7.25f, 1, LampDuty.Work, -.36f);
            fitout.WallLamp("bench-task", 17.2f, 2.62f, -7.25f, -1, LampDuty.Work);
            fitout.WallLamp("first-flight-marker", 4.2f, 2.4f, -7.25f, -1, LampDuty.Guidance, .55f);
            fitout.WallLamp("mid-landing-marker", 10.38f, middleY + 1.65f, -7.25f, -1, LampDuty.Guidance, .52f);
            fitout.WallLamp("second-flight-marker", 15.4f, middleY + 3.1f, -7.25f, -1, LampDuty.Guidance, .55f);
            fitout.AisleStrip("upper-exit-marker", 20.9f, topY + .44f, -7.25f, -1, .74f);
            fitout.AisleStrip("pump-service-strip", 4.65f, .41f, 7.25f, 1, .80f);
            fitout.VerifyCover("feed-switchgear-body", new Vector3(15.6f, 0, 6.78f),
                new Vector3(19.2f, 0, 6.78f), new Vector3(0, 0, -1.58f));
            fitout.VerifyLowCover("pump-service-drawers-body", new Vector3(3.32f, 0, 5.42f),
                new Vector3(6.36f, 0, 5.42f), new Vector3(0, 0, 1.02f), new Vector3(3.32f, 0, 1.35f));
            room.AddDressing(fitout);
            return room.Module;
        }
    }
}

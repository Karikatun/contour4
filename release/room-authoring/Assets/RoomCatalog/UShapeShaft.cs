using System.Linq;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class UShapeShaft
    {
        public static GrayModule Create()
        {
            const float halfWidth = 3.75f;
            var group = new GrayBuilder("u-shaft", halfWidth * 2, 26);
            var entry = group.Door("entry", "lower", Pose(0, 0, -halfWidth, -Vector3.UnitZ), Cargo.Cart);
            group.Floor("lower", -5, 5, 0, -halfWidth, halfWidth);
            group.Wall("entry-wall", entry.Threshold, 5, Height, entry);
            group.Box("lower-far-wall", BlockRole.Shell, -5, 5, 0, Height, halfWidth - Skirt, halfWidth);
            foreach (int side in new[] { -1, 1 })
                AddArm(group, entry, side);
            var lower = new RoomDressing("field-floor", Level);
            lower.Bench("comparison-console", 1.95f, 0, 3.5f, 1, 1.78f, FixedSeat.Chair,
                powered: true, active: true);
            lower.Cabinet("lower-feed-cabinet", -2.45f, 0, 3.5f, 1, 1.35f, .97f, powered: true);
            lower.WallLamp("comparison-console-light", 1.98f, 3.24f, 3.5f, 1, LampDuty.Work);
            lower.AisleStrip("floor-exit-marker", -.9f, .38f, 3.5f, 1, .82f);
            group.AddDressing(lower);
            group.Module.InternalPortals.Add(("west-door-portal", "east-door-portal"));
            return group.Module;
        }

        private static void AddArm(GrayBuilder group, Dock entry, int side)
        {
            const float r = 8;
            const float armLength = 8;
            const float armHalf = 3.75f;
            string id = side < 0 ? "west" : "east";
            Vector3 away = side * Vector3.UnitX;
            ModulePose[] bridge = Bend(Pose(5 * side, 0, 0, away), r, Vector3.UnitY, 36).ToArray();
            ModulePose arm = bridge.Last();
            ModulePose At(float forward, float up, float lateral = 0) =>
                new ModulePose(arm.Point(forward, up, lateral), arm.Forward, arm.Up);
            var endpoint = group.Door($"{id}-top", id, At(armLength, 0), Cargo.Cart,
                mapPoint: new Vector2(armLength, 0));
            var portal = group.Door($"{id}-door-portal", id, new ModulePose(arm.Point(5.1f, 0, armHalf), arm.Right, arm.Up),
                Cargo.Hand, DockUse.DoorPortal, Carry, 2.5f, mapPoint: new Vector2(5.1f, armHalf));
            group.SweptHall($"{id}-bridge", bridge, Wide, Height);
            group.MapFloor($"{id}-bridge", bridge[0], new Vector2(0, -Wide / 2), new Vector2(4 * System.MathF.PI, -Wide / 2),
                new Vector2(4 * System.MathF.PI, Wide / 2), new Vector2(0, Wide / 2));
            group.MapFloor(id, arm, new Vector2(0, -armHalf), new Vector2(armLength, -armHalf),
                new Vector2(armLength, armHalf), new Vector2(0, armHalf));
            group.Module.Geometry.Add(Block.Box($"{id}-deck", BlockRole.Floor, arm, 0, armLength, -Skirt, 0, -armHalf, armHalf));
            group.Wall($"{id}-upper-wall", At(armLength, 0), armHalf, Height, endpoint);
            group.Wall($"{id}-side-wall", new ModulePose(arm.Point((armLength - Skirt) / 2, 0, armHalf), arm.Right, arm.Up),
                (armLength - Skirt) / 2, Height, portal);
            group.Module.Geometry.Add(Block.Box($"{id}-back-wall", BlockRole.Shell, arm,
                0, armLength - Skirt, 0, Height, -armHalf, -armHalf + Skirt));
            foreach (int lateral in new[] { -1, 1 })
            {
                float z0 = lateral < 0 ? -armHalf + Skirt : Wide / 2;
                float z1 = lateral < 0 ? -Wide / 2 : armHalf - Skirt;
                group.Module.Geometry.Add(Block.Box($"{id}-bridge-jamb-{lateral}", BlockRole.Shell, arm, 0, Skirt,
                    0, Height, z0, z1));
            }
            var fromGround = new[] { Pose(0, 0, -5.55f, Vector3.UnitZ) }
                .Concat(Bend(Pose(0, 0, -3, Vector3.UnitZ), 3, away))
                .Concat(bridge).Append(At(armLength + 1.8f, 0)).ToArray();
            group.Module.Routes.Add(new HaulRoute($"entry-to-{id}-top", entry.Id, endpoint.Id, Cargo.Cart,
                fromGround, stepClearance: .14f));
            var view = new ModulePose(arm.Point(5.1f, 0), -arm.Forward, arm.Up);
            group.Route($"{id}-hand-portal-approach", endpoint.Id, portal.Id, Cargo.Hand,
                new ModulePose(arm.Point(armLength + 1.8f, 0), -arm.Forward, arm.Up), view,
                new ModulePose(view.Position, arm.Right, arm.Up),
                new ModulePose(arm.Point(5.1f, 0, armHalf - .70f), arm.Right, arm.Up));
            var fitout = new RoomDressing($"{id}-control", arm);
            fitout.Cabinet("relay-cabinet", 4.15f, 0, -3.5f, -1, 1.3f, .75f, powered: true);
            fitout.LockerBank("relay-spares", 6.24f, 0, -3.5f, -1, 2);
            fitout.WallLamp("arm-standby", 3.05f, 3.31f, -3.5f, -1, LampDuty.Standby);
            fitout.AisleStrip("exit-marker", 7.08f, .43f, -3.5f, -1, .74f);
            group.AddDressing(fitout);
            foreach (int markerIndex in new[] { 3, 16, 29, 36 })
            {
                var marker = new RoomDressing($"{id}-bend-{markerIndex:D2}", bridge[markerIndex]);
                marker.GuideFromWall("guard-recess", 2.07f, -2.17f, -1);
                group.AddDressing(marker);
            }
        }
    }
}

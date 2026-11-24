using System.Linq;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class UpperExitCorridor
    {
        public static GrayModule Create()
        {
            const float length = 24f;
            const float bridgeStart = 14;
            const float bridgeLine = -6.75f;
            // Loaded-cart clearance at the start of this concave turn sets the radius.
            const float radius = 7.5f;
            const float upperLeg = 3;
            var corridor = new GrayBuilder("upper-exit-corridor", 13, length);
            ModulePose[] curve = Bend(Pose(bridgeStart, 0, bridgeLine), radius, Vector3.UnitY, 36).ToArray();
            ModulePose bridgeEnd = curve.Last();
            var exit = new ModulePose(bridgeEnd.Point(upperLeg, 0), bridgeEnd.Forward, bridgeEnd.Up);
            var west = corridor.Door("lower-west", "lower", Pose(0, 0, 0, -Vector3.UnitX), Cargo.Cart);
            var east = corridor.Door("lower-east", "lower", Pose(length, 0, 0), Cargo.Cart);
            var upper = corridor.Door("upper", "upper-landing", exit, Cargo.Cart, depth: upperLeg,
                mapPoint: new Vector2(upperLeg, 0));

            corridor.Deck("lower-floor", 0, length, 0, -3.75f, 3.75f);
            corridor.Deck("branch-approach-floor", 4, bridgeStart, 0, -9.25f, -3.75f);
            corridor.MapFloor("lower", Level, new Vector2(0, -3.75f), new Vector2(4, -3.75f),
                new Vector2(4, -9.25f), new Vector2(bridgeStart, -9.25f), new Vector2(bridgeStart, -3.75f),
                new Vector2(length, -3.75f), new Vector2(length, 3.75f), new Vector2(0, 3.75f));
            corridor.Roof(0, length, Height, -3.75f, 3.75f);
            corridor.Box("branch-approach-roof", BlockRole.Shell, 4, bridgeStart, Height, Height + Skirt, -9.25f, -3.75f);
            corridor.Wall("west-wall", west.Threshold, 3.75f, Height, west);
            corridor.Wall("east-wall", east.Threshold, 3.75f, Height, east);
            corridor.Box("far-wall", BlockRole.Shell, Skirt, length - Skirt, 0, Height, 3.75f - Skirt, 3.75f);
            corridor.Box("near-west-wall", BlockRole.Shell, Skirt, 4 + Skirt, 0, Height, -3.75f, -3.75f + Skirt);
            corridor.Box("near-east-wall", BlockRole.Shell, bridgeStart, length - Skirt, 0, Height, -3.75f, -3.75f + Skirt);
            corridor.Box("branch-west-wall", BlockRole.Shell, 4, 4 + Skirt, 0, Height, -9.25f, -3.75f);
            corridor.Box("branch-side-wall", BlockRole.Shell, 4 + Skirt, bridgeStart - Skirt, 0, Height,
                -9.25f, -9.25f + Skirt);
            corridor.Box("branch-end-left-jamb", BlockRole.Shell, bridgeStart - Skirt, bridgeStart, 0, Height,
                -9.25f, -9);
            corridor.Box("branch-end-right-jamb", BlockRole.Shell, bridgeStart - Skirt, bridgeStart, 0, Height,
                -4.5f, -3.75f);
            corridor.SweptHall("turn", curve.Append(exit).ToArray(), Wide, Height);
            corridor.Module.Geometry.Add(Block.Box(upper.PlugId, BlockRole.Plug, exit, -Skirt, 0,
                0, Height, -Wide / 2, Wide / 2));
            float arcLength = radius * System.MathF.PI / 2;
            corridor.MapFloor("bridge", curve[0], new Vector2(0, -Wide / 2), new Vector2(arcLength, -Wide / 2),
                new Vector2(arcLength, Wide / 2), new Vector2(0, Wide / 2));
            corridor.MapFloor("upper-landing", bridgeEnd, new Vector2(0, -Wide / 2), new Vector2(upperLeg, -Wide / 2),
                new Vector2(upperLeg, Wide / 2), new Vector2(0, Wide / 2));
            corridor.Module.Approaches.Add(Block.Box("lower-free-lane", BlockRole.Free, Level,
                0, length, 0, Height, -Wide / 2, Wide / 2));
            corridor.Module.Approaches.Add(Block.Box("branch-approach-free", BlockRole.Free, Level,
                4 + Skirt, bridgeStart - Skirt, 0, Height, bridgeLine - Wide / 2, -Wide / 2));
            corridor.Module.Approaches.Add(Block.Box("branch-throat-free", BlockRole.Free, Level,
                bridgeStart - Skirt, bridgeStart, 0, Height, bridgeLine - Wide / 2, bridgeLine + Wide / 2));
            corridor.Module.Landmarks.Add("branch-approach", new Vector3(4, 0, 0));
            corridor.Route("lower-cart-through", west.Id, east.Id, Cargo.Cart,
                Pose(-1.8f, 0, 0), Pose(4, 0, 0), Pose(length + 1.8f, 0, 0));
            var branch = new[] { Pose(4, 0, 0) }.Concat(LaneChange(4, 12.5f, 0, bridgeLine)).Concat(curve)
                .Append(exit).Append(new ModulePose(exit.Point(1.8f, 0), exit.Forward, exit.Up)).ToArray();
            corridor.Module.Routes.Add(new HaulRoute("upper-cart-branch", "branch-approach", upper.Id, Cargo.Cart,
                branch, stepClearance: .13f));
            var lower = new RoomDressing("lower-transit", Level);
            lower.Cabinet("bridge-control", 13.35f, 0, 3.5f, 1, .95f, .62f, powered: true);
            lower.AisleStrip("lower-west-route", 3.6f, .38f, 3.5f, 1, .8f);
            lower.WallLamp("bridge-control-task", 11.65f, 3.45f, 3.5f, 1, LampDuty.Work, -.24f);
            lower.WallLamp("branch-mouth-repeater", 15.28f, 2.45f, -3.5f, -1, LampDuty.Guidance, -.56f);
            lower.AisleStrip("lower-east-route", 18.7f, .38f, 3.5f, 1, .85f);
            lower.WallLamp("lower-eastern-threshold", length - 2.6f, 2.35f, 3.5f, 1, LampDuty.Guidance, .59f);
            corridor.AddDressing(lower);
            var turnIn = new RoomDressing("bridge-approach", Pose(9.2f, 0, 0));
            turnIn.GuideFromWall("approach-strip", 2.58f, -8.94f, -1, .57f);
            corridor.AddDressing(turnIn);
            var throatLight = new RoomDressing("bridge-mouth", Pose(12.9f, 0, 0));
            throatLight.GuideFromWall("threshold-recess", 2.74f, -8.94f, -1, .28f);
            corridor.AddDressing(throatLight);
            foreach (int index in new[] { 2, 12, 22, 32, 36 })
            {
                var marker = new RoomDressing($"bridge-field-{index:D2}", curve[index]);
                marker.GuideFromWall("guard-recess", 2.07f, -2.17f, -1);
                corridor.AddDressing(marker);
            }
            return corridor.Module;
        }
    }
}

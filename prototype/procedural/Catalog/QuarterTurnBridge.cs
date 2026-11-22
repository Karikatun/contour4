using System.Linq;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class QuarterTurnBridge
    {
        public static GrayModule Create()
        {
            const float leg = 3;
            var bridge = new GrayBuilder("quarter-turn-bridge", Wide + 2 * Skirt,
                2 * leg + CornerSlice.ArcLength);
            var begin = Pose(-leg, 0, 0);
            ModulePose[] arc = Bend(Level, CornerSlice.Radius, -Vector3.UnitY, CornerSlice.Segments).ToArray();
            ModulePose exitFrame = arc.Last();
            var end = new ModulePose(exitFrame.Point(leg, 0), exitFrame.Forward, exitFrame.Up);
            ModulePose[] frames = new[] { begin }.Concat(arc).Append(end).ToArray();
            var start = bridge.Door("flat-entry", "bridge", new ModulePose(begin.Position, -begin.Forward, begin.Up),
                Cargo.Cart, mapPoint: Vector2.Zero);
            var finish = bridge.Door("turned-exit", "bridge", end, Cargo.Cart,
                mapPoint: new Vector2(bridge.Module.Plan.Y, 0));
            bridge.Seal(start);
            bridge.Seal(finish);
            bridge.SweptHall("bridge", frames, Wide, Height);
            bridge.MapFloor("bridge", begin, new Vector2(0, -Wide / 2), new Vector2(bridge.Module.Plan.Y, -Wide / 2),
                new Vector2(bridge.Module.Plan.Y, Wide / 2), new Vector2(0, Wide / 2));
            bridge.Route("quarter-turn", start.Id, finish.Id, Cargo.Cart, new[] { Pose(-leg - 1.8f, 0, 0) }
                .Concat(frames).Append(new ModulePose(end.Point(1.8f, 0), end.Forward, end.Up)).ToArray());
            for (int index = 1; index < arc.Length; index += 12)
            {
                var fitout = new RoomDressing($"field-{index:D2}", arc[index]);
                fitout.GuideFromWall("guard-recess", 2.07f, -2.17f, -1);
                bridge.AddDressing(fitout);
            }
            return bridge.Module;
        }
    }
}

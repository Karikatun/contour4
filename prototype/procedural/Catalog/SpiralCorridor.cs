using System;
using System.Linq;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class SpiralCorridor
    {
        public const float Length = 27;
        private const float Radius = 2.6f;
        private const float EndLead = 3;

        // The rotation axis is fixed. Straight end leads align each threshold with its mating floor.
        public static ModulePose FrameAt(float axial)
        {
            float t = Math.Clamp((axial - EndLead) / (Length - 2 * EndLead), 0, 1);
            float roll = MathF.PI * t * t * (3 - 2 * t);
            Vector3 up = t == 1 ? -Vector3.UnitY : new Vector3(0, MathF.Cos(roll), MathF.Sin(roll));
            return new ModulePose(new Vector3(axial, Radius, 0) - Radius * up, Vector3.UnitX, up);
        }

        public static GrayModule Create()
        {
            const int segments = 108;
            var hallway = new GrayBuilder("spiral-corridor", Wide + 2 * Skirt, Length);
            ModulePose[] frames = Enumerable.Range(0, segments + 1).Select(i => FrameAt(Length * i / segments)).ToArray();
            var begin = hallway.Door("flat-entry", "helix", new ModulePose(frames[0].Position,
                -frames[0].Forward, frames[0].Up), Cargo.Hand, mapPoint: Vector2.Zero);
            var finish = hallway.Door("inverted-exit", "helix", frames.Last(), Cargo.Hand, mapPoint: new Vector2(Length, 0));
            hallway.Seal(begin);
            hallway.Seal(finish);
            hallway.SweptHall("helix", frames, Wide, Height);
            hallway.MapFloor("helix", frames[0], new Vector2(0, -Wide / 2), new Vector2(Length, -Wide / 2),
                new Vector2(Length, Wide / 2), new Vector2(0, Wide / 2));
            hallway.Module.Routes.Add(new HaulRoute("hand-carry-through-helix", begin.Id, finish.Id, Cargo.Hand,
                new[] { FrameAt(-1.8f) }.Concat(frames).Append(FrameAt(Length + 1.8f)).ToArray(), stepClearance: .12f));
            for (int axial = 2; axial < Length; axial += 5)
            {
                var fitout = new RoomDressing($"helix-{axial:D2}", FrameAt(axial));
                fitout.GuideFromWall("guard-recess", 1.95f, -2.17f, -1);
                hallway.AddDressing(fitout);
            }
            return hallway.Module;
        }
    }
}

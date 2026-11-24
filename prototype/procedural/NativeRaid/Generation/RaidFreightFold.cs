using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.NativeRaid.Runtime
{
    internal static class RaidFreightFold
    {
        internal const string ShortRise = "freight-gravity-rise";
        internal const string ShortDrop = "freight-gravity-drop";
        internal const string LongSpan = "freight-gravity-span";
        internal const float Drop = Height + Skirt;
        private const float ShortRailHeight = .85f;
        private const int Segments = 24;

        internal static IEnumerable<Block> SeamDecks(GrayModule module)
        {
            var frames = module.Routes[0].Knots;
            foreach (var frame in new[] { frames[0], frames[frames.Length - 1] })
            {
                // The sill straddles the coincident room/deck edge. Both tops remain flush.
                yield return Block.Box("coincident-sill-support", BlockRole.Floor, frame,
                    -.055f, .055f, -.06f, -.001f, -Wide / 2, Wide / 2);
            }
        }

        internal static IEnumerable<Block> SafetyBarriers(GrayModule module)
        {
            if (module.Id != ShortRise && module.Id != ShortDrop) yield break;
            var frames = module.Routes[0].Knots;
            const float half = Wide / 2;
            const float thickness = .08f;
            const float barrierTop = 4f;
            for (int i = 1; i < frames.Length; i++)
            {
                var a = frames[i - 1];
                var b = frames[i];
                string name = $"invisible-bridge-{i:D2}";
                yield return Block.Span(name + "-left", BlockRole.Shell, a, b,
                    ShortRailHeight, barrierTop, -half - thickness, -half);
                yield return Block.Span(name + "-right", BlockRole.Shell, a, b,
                    ShortRailHeight, barrierTop, half, half + thickness);
                yield return Block.Span(name + "-ceiling", BlockRole.Shell, a, b,
                    barrierTop, barrierTop + thickness, -half - thickness, half + thickness);
            }
        }

        // The two exterior upper edges meet if the rise is the departure roof height and
        // the forward offset is the arrival roof height. Any straight part exists only to
        // make up the difference in the rooms' heights, while preserving the cart's turn radius.
        internal static GrayModule Rise(float departureTop, float arrivalTop)
        {
            float radius = MathF.Min(departureTop, arrivalTop);
            float forwardLead = arrivalTop - radius;
            var arc = Bend(Pose(forwardLead, 0f, 0f), radius, Vector3.UnitY, Segments);
            if (forwardLead > .005f) arc = arc.Prepend(Level);
            if (departureTop - radius > .005f)
                arc = arc.Append(new ModulePose(new Vector3(arrivalTop, departureTop, 0f), Vector3.UnitY, -Vector3.UnitX));
            return Create(ShortRise, arc.ToArray(), enclosed: false);
        }

        internal static GrayModule Descend() => Create(ShortDrop,
            Bend(Level, Drop, -Vector3.UnitY, Segments).ToArray(), enclosed: false);

        internal static GrayModule LongDescent(float entryLeg)
        {
            const float exitLeg = 3f;
            var start = Pose(-entryLeg, 0, 0);
            var curve = Bend(Level, CornerSlice.Radius, -Vector3.UnitY, Segments).ToArray();
            var last = curve[curve.Length - 1];
            var end = new ModulePose(last.Point(exitLeg, 0), last.Forward, last.Up);
            return Create(LongSpan, new[] { start }.Concat(curve).Append(end).ToArray(),
                enclosed: true);
        }

        private static GrayModule Create(string id, ModulePose[] frames, bool enclosed)
        {
            float length = frames.Zip(frames.Skip(1), (a, b) => Vector3.Distance(a.Position, b.Position)).Sum();
            var hall = new GrayBuilder(id, Wide + 2 * Skirt, length);
            ModulePose start = frames[0], end = frames[frames.Length - 1];
            var entry = hall.Door("approach", "fold", new ModulePose(start.Position, -start.Forward, start.Up),
                Cargo.Cart, depth: enclosed ? 2.8f : .15f);
            var exit = hall.Door("landing", "fold", end, Cargo.Cart, depth: enclosed ? 2.8f : .15f);
            if (enclosed)
                hall.SweptHall("freight-turn", frames, Wide, Height);
            else
                AddOpenDeck(hall, frames);
            hall.Route("freight-turn", entry.Id, exit.Id, Cargo.Cart, frames);
            return hall.Module;
        }

        private static void AddOpenDeck(GrayBuilder hall, ModulePose[] frames)
        {
            const float half = Wide / 2;
            for (int i = 1; i < frames.Length; i++)
            {
                ModulePose a = frames[i - 1], b = frames[i];
                string section = $"bridge-{i:D2}";
                hall.Module.Geometry.Add(Block.Span(section + "-deck", BlockRole.Floor, a, b,
                    -Skirt, 0f, -half, half));
                hall.Module.Geometry.Add(Block.Span(section + "-left", BlockRole.Shell, a, b,
                    -Skirt, ShortRailHeight, -half - Skirt, -half));
                hall.Module.Geometry.Add(Block.Span(section + "-right", BlockRole.Shell, a, b,
                    -Skirt, ShortRailHeight, half, half + Skirt));
                hall.Module.Approaches.Add(Block.Span(section + "-cargo", BlockRole.Free, a, b,
                    0f, 2.35f, -half, half));
            }
        }

        internal static float UpperEdge(GrayModule room, Dock dock)
        {
            const float tolerance = .005f;
            var face = dock.Threshold;
            float upper = 0f;
            foreach (Block block in room.Geometry.Where(block => block.Role == BlockRole.Shell))
            {
                var vertices = block.Vertices.Select(vertex => vertex - face.Position)
                    .Where(vertex => MathF.Abs(Vector3.Dot(vertex, face.Forward)) < tolerance).ToArray();
                if (vertices.Length == 0) continue;
                float top = vertices.Max(vertex => Vector3.Dot(vertex, face.Up));
                var edge = vertices.Where(vertex => MathF.Abs(Vector3.Dot(vertex, face.Up) - top) < tolerance)
                    .Select(vertex => Vector3.Dot(vertex, face.Right)).ToArray();
                if (edge.Min() <= -dock.Opening.X / 2 + tolerance &&
                    edge.Max() >= dock.Opening.X / 2 - tolerance)
                    upper = MathF.Max(upper, top);
            }
            return upper > dock.Opening.Y ? upper : 0f;
        }
    }
}

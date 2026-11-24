using System;
using System.Collections.Generic;
using System.Numerics;

namespace Contour4.Procedural.Catalog
{
    public static class SphericalGravityField
    {
        public const float DeckReach = 2f;
        private const float ContactTolerance = .015f;
        private const float CoordinateTolerance = .00002f;
        private const float ShellTolerance = .11f;
        private const float CenterTolerance = .04f;
        private static readonly DeckStrip[] UpperBranch = BuildStrips(1);
        private static readonly DeckStrip[] LowerBranch = BuildStrips(-1);

        public static bool TryUp(Vector3 point, Vector3 singularityUp, out Vector3 up, out bool onDeck) =>
            TryUp(point, singularityUp, out up, out onDeck, SphericalChamber.FullMask);

        public static bool TryUp(Vector3 point, Vector3 singularityUp, out Vector3 up, out bool onDeck, int mask)
        {
            up = Vector3.UnitY;
            onDeck = InHeight(point.Y) && SphericalChamber.IsStraightDeckFootprint(point, mask);
            float nearest = onDeck ? MathF.Abs(point.Y) : float.PositiveInfinity;
            if (MathF.Abs(point.X) <= SphericalChamber.HalfWidth + CoordinateTolerance)
            {
                if ((mask & SphericalChamber.UpperMask) != 0)
                    ApplyStrips(UpperBranch, point, ref nearest, ref up, ref onDeck);
                if ((mask & SphericalChamber.LowerMask) != 0)
                    ApplyStrips(LowerBranch, point, ref nearest, ref up, ref onDeck);
            }

            if (onDeck) return true;

            float distanceSquared = point.LengthSquared();
            const float shellExtent = SphericalChamber.Radius + ShellTolerance;
            if (distanceSquared > shellExtent * shellExtent) return false;
            if (distanceSquared > CenterTolerance * CenterTolerance)
                up = -point / MathF.Sqrt(distanceSquared);
            else if (singularityUp.LengthSquared() > .0001f)
                up = Vector3.Normalize(singularityUp);
            return true;
        }

        private static bool InHeight(float height) =>
            height >= -ContactTolerance && height <= DeckReach + CoordinateTolerance;

        private static void ApplyStrips(DeckStrip[] strips, Vector3 point, ref float nearest, ref Vector3 up, ref bool onDeck)
        {
            foreach (var strip in strips)
            {
                if (!strip.TryProject(point, out Vector3 deckUp, out float height) || MathF.Abs(height) >= nearest)
                    continue;
                nearest = MathF.Abs(height);
                up = deckUp;
                onDeck = true;
            }
        }

        private static DeckStrip[] BuildStrips(int side)
        {
            var strips = new List<DeckStrip>();
            ModulePose previous = SphericalChamber.BranchFrame(side, 0, 0, 0);
            for (int phase = 0; phase < SphericalChamber.BranchPhaseCount; phase++)
            {
                int steps = SphericalChamber.BranchSteps(phase);
                for (int row = 1; row <= steps; row++)
                {
                    ModulePose next = SphericalChamber.BranchFrame(side, phase, row / (float)steps, 0);
                    strips.Add(new DeckStrip(previous, next));
                    previous = next;
                }
            }
            return strips.ToArray();
        }

        // Each strip starts at a real collider row and ends at the next one. The
        // normal planes through those rows tile a bend without stretching the rails.
        private readonly struct DeckStrip
        {
            private readonly ModulePose start;
            private readonly ModulePose end;
            private readonly Vector3 min;
            private readonly Vector3 max;

            internal DeckStrip(ModulePose start, ModulePose end)
            {
                this.start = start;
                this.end = end;
                var startTop = start.Position + start.Up * DeckReach;
                var endTop = end.Position + end.Up * DeckReach;
                var padding = new Vector3(ContactTolerance + .01f);
                min = Vector3.Min(Vector3.Min(start.Position, end.Position), Vector3.Min(startTop, endTop)) - padding;
                max = Vector3.Max(Vector3.Max(start.Position, end.Position), Vector3.Max(startTop, endTop)) + padding;
            }

            internal bool TryProject(Vector3 point, out Vector3 up, out float height)
            {
                up = default;
                height = 0;
                if (point.Y < min.Y || point.Y > max.Y || point.Z < min.Z || point.Z > max.Z) return false;

                float fromStart = Vector3.Dot(point - start.Position, start.Forward);
                float fromEnd = Vector3.Dot(point - end.Position, end.Forward);
                if (fromStart < -CoordinateTolerance || fromEnd > CoordinateTolerance) return false;

                float progress = 0;
                if (fromEnd >= -CoordinateTolerance) progress = 1;
                else if (fromStart > CoordinateTolerance)
                {
                    float low = 0, high = 1;
                    for (int i = 0; i < 16; i++)
                    {
                        progress = (low + high) * .5f;
                        Vector3 offset = point - Vector3.Lerp(start.Position, end.Position, progress);
                        Vector3 forward = Vector3.Lerp(start.Forward, end.Forward, progress);
                        if (Vector3.Dot(offset, forward) > 0) low = progress;
                        else high = progress;
                    }
                }

                up = Vector3.Normalize(Vector3.Lerp(start.Up, end.Up, progress));
                height = Vector3.Dot(point - Vector3.Lerp(start.Position, end.Position, progress), up);
                return InHeight(height);
            }
        }
    }
}

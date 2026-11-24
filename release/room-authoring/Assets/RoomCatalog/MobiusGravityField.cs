using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Contour4.Procedural.Catalog
{
    internal sealed class MobiusGravityField
    {
        private const float ContactTolerance = .01f;
        private readonly float reach;
        private readonly Patch[] patches;
        private readonly Dictionary<(int x, int y, int z), int[]> cells;

        internal MobiusGravityField(Vector3[] vertices, Vector3[] surfaceNormals, Vector3[] edgeNormals,
            int[] triangles, int walkingIndexCount, float reach)
        {
            this.reach = reach;
            patches = new Patch[walkingIndexCount / 3];
            var buckets = new Dictionary<(int x, int y, int z), List<int>>();
            var margin = new Vector3(reach);
            for (int i = 0; i < walkingIndexCount; i += 3)
            {
                int ai = triangles[i], bi = triangles[i + 1], ci = triangles[i + 2];
                Vector3 a = vertices[ai], b = vertices[bi], c = vertices[ci];
                int patch = i / 3;
                patches[patch] = new Patch(a, b, c, surfaceNormals[ai], surfaceNormals[bi], surfaceNormals[ci],
                    edgeNormals[ai], edgeNormals[bi], edgeNormals[ci]);
                var min = Cell(Vector3.Min(a, Vector3.Min(b, c)) - margin);
                var max = Cell(Vector3.Max(a, Vector3.Max(b, c)) + margin);
                for (int x = min.x; x <= max.x; x++)
                for (int y = min.y; y <= max.y; y++)
                for (int z = min.z; z <= max.z; z++)
                {
                    var key = (x, y, z);
                    if (!buckets.TryGetValue(key, out List<int> indices)) buckets.Add(key, indices = new List<int>());
                    indices.Add(patch);
                }
            }
            cells = buckets.ToDictionary(cell => cell.Key, cell => cell.Value.ToArray());
        }

        internal bool TryProject(Vector3 point, out Vector3 up, Vector3 preferredFace = default)
        {
            up = default;
            if (!cells.TryGetValue(Cell(point), out var nearby)) return false;

            float nearest = reach * reach + .00001f;
            float nearestProjection = nearest;
            Vector3 projectedUp = default;
            bool offEdge = false;
            foreach (int index in nearby)
            {
                ref readonly var patch = ref patches[index];
                if (preferredFace != Vector3.Zero && Vector3.Dot(preferredFace, patch.Up) < .65f) continue;
                float height = Vector3.Dot(point - patch.Origin, patch.Up);
                float planeDistance = height * height;
                if (height < -ContactTolerance || planeDistance >= MathF.Max(nearest, nearestProjection)) continue;
                Vector3 nearestPoint = patch.ClosestPoint(point, out bool projected);
                if (projected && planeDistance < nearestProjection && !patch.ProjectsOffEdge(point, nearestPoint))
                {
                    nearestProjection = planeDistance;
                    projectedUp = patch.SmoothUp(nearestPoint);
                }
                float distance = Vector3.DistanceSquared(point, nearestPoint);
                if (distance >= nearest) continue;
                nearest = distance;
                offEdge = patch.ProjectsOffEdge(point, nearestPoint);
                up = patch.SmoothUp(nearestPoint);
            }
            if (offEdge) up = projectedUp;
            return up != Vector3.Zero;
        }

        private static (int x, int y, int z) Cell(Vector3 point) =>
            ((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y), (int)MathF.Floor(point.Z));

        private readonly struct Patch
        {
            internal readonly Vector3 Origin;
            internal readonly Vector3 Up;
            private readonly Vector3 b;
            private readonly Vector3 c;
            private readonly Vector3 upAtA;
            private readonly Vector3 upAtB;
            private readonly Vector3 upAtC;
            private readonly Vector3 edgeAtA;
            private readonly Vector3 edgeAtB;
            private readonly Vector3 edgeAtC;
            private readonly Vector3 uPerMetre;
            private readonly Vector3 vPerMetre;

            internal Patch(Vector3 a, Vector3 b, Vector3 c, Vector3 upAtA, Vector3 upAtB, Vector3 upAtC,
                Vector3 edgeAtA, Vector3 edgeAtB, Vector3 edgeAtC)
            {
                Origin = a;
                this.b = b;
                this.c = c;
                this.upAtA = upAtA;
                this.upAtB = upAtB;
                this.upAtC = upAtC;
                this.edgeAtA = edgeAtA;
                this.edgeAtB = edgeAtB;
                this.edgeAtC = edgeAtC;
                Vector3 first = b - a, second = c - a;
                Up = Vector3.Normalize(Vector3.Cross(first, second));
                Vector3 firstDual = Vector3.Cross(second, Up), secondDual = Vector3.Cross(Up, first);
                uPerMetre = firstDual / Vector3.Dot(first, firstDual);
                vPerMetre = secondDual / Vector3.Dot(second, secondDual);
            }

            internal Vector3 ClosestPoint(Vector3 point, out bool inside)
            {
                (float u, float v) = Coordinates(point);
                inside = u >= 0 && v >= 0 && u + v <= 1;
                if (inside) return Origin + u * (b - Origin) + v * (c - Origin);

                Vector3 ab = ClosestOnSegment(point, Origin, b);
                Vector3 bc = ClosestOnSegment(point, b, c);
                Vector3 ca = ClosestOnSegment(point, c, Origin);
                Vector3 nearest = Vector3.DistanceSquared(point, ab) < Vector3.DistanceSquared(point, bc) ? ab : bc;
                return Vector3.DistanceSquared(point, nearest) < Vector3.DistanceSquared(point, ca) ? nearest : ca;
            }

            internal bool ProjectsOffEdge(Vector3 point, Vector3 contact)
            {
                if (edgeAtA == Vector3.Zero && edgeAtB == Vector3.Zero && edgeAtC == Vector3.Zero) return false;
                (float u, float v) = Coordinates(contact);
                float w = 1 - u - v;
                Vector3 outward = w * edgeAtA + u * edgeAtB + v * edgeAtC;
                // A point on an internal triangulation edge remains in the field. A point past the physical rim does not.
                return outward.LengthSquared() > .98f && Vector3.Dot(point - contact, outward) > .003f;
            }

            internal Vector3 SmoothUp(Vector3 contact)
            {
                (float u, float v) = Coordinates(contact);
                return Vector3.Normalize((1 - u - v) * upAtA + u * upAtB + v * upAtC);
            }

            private (float u, float v) Coordinates(Vector3 point)
            {
                Vector3 offset = point - Origin;
                return (Vector3.Dot(uPerMetre, offset), Vector3.Dot(vPerMetre, offset));
            }

            private static Vector3 ClosestOnSegment(Vector3 point, Vector3 from, Vector3 to)
            {
                Vector3 direction = to - from;
                float t = Math.Clamp(Vector3.Dot(point - from, direction) / direction.LengthSquared(), 0, 1);
                return from + t * direction;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    // Stable floor identities bind the plan to its physical assembly. The flat poses never
    // depend on the realized bends or on the portal pairs chosen after spatial placement.
    internal sealed class RaidPlanarLayout
    {
        internal const int Stock = -1;
        internal const string StockFloor = "stock-start";
        internal const float StockDepth = 27.105f;
        internal const float StockHalfWidth = 8.95f;
        private const float CorridorPadding = .82f;

        internal readonly struct FlatPose
        {
            internal readonly Vector2 Position;
            internal readonly float Angle;
            internal FlatPose(Vector2 position, float angle) { Position = position; Angle = angle; }
        }

        internal sealed class BranchOutline
        {
            internal (string Floor, Vector2 Sill) Entrance { get; }
            internal IReadOnlyList<(string Name, Rect Bounds)> Floors { get; }
            internal IReadOnlyList<(string From, Vector2 Sill, string To, Vector2 Landing)> Passages { get; }

            internal BranchOutline((string Floor, Vector2 Sill) entrance, IReadOnlyList<(string, Rect)> floors,
                IReadOnlyList<(string, Vector2, string, Vector2)> passages)
            {
                Entrance = entrance;
                Floors = floors;
                Passages = passages;
            }
        }

        private sealed class FloorShape
        {
            internal readonly (int Room, string Floor) Key;
            internal readonly Vector2[] Outline;
            internal FlatPose Pose;
            internal Rect Bounds;

            internal FloorShape(int room, string floor, Vector2[] outline)
            {
                Key = (room, floor);
                Outline = outline;
            }

            internal void Place(Vector2 position, float angle)
            {
                Pose = new FlatPose(position, angle);
                Bounds = Enclose(Outline.Select(Point));
            }

            internal Vector2 Point(Vector2 local) => Pose.Position + Turn(local, Pose.Angle);
        }

        private readonly struct Mouth
        {
            internal readonly FloorShape Floor;
            internal readonly Vector2 Sill;
            internal readonly Vector2 Outward;
            internal Mouth(FloorShape floor, Vector2 point, Vector2 direction)
            {
                Floor = floor;
                Sill = Boundary(floor.Outline, point, out Vector2 normal);
                Outward = Vector2.Dot(direction, normal) > .55f ? direction : normal;
            }
        }

        private readonly Dictionary<(int Room, string Floor), FlatPose> poses;

        private RaidPlanarLayout(IEnumerable<FloorShape> floors) =>
            poses = floors.ToDictionary(floor => floor.Key, floor => floor.Pose);

        internal bool TryGetPose(int room, string floor, out FlatPose pose) => poses.TryGetValue((room, floor), out pose);

        internal static bool TryCreate(RaidPlan draft, out RaidPlanarLayout layout)
        {
            var stock = new FloorShape(Stock, StockFloor, Rectangle(0f, -StockHalfWidth, StockDepth, StockHalfWidth));
            var floors = new List<FloorShape> { stock };
            var byFloor = new Dictionary<(int, string), FloorShape>();
            var links = new List<(Mouth A, Mouth B)>();
            foreach (RaidPlan.Room room in draft.Rooms)
            {
                foreach (var floor in room.Module.Floors)
                {
                    var shape = new FloorShape(room.Index, floor.Id, floor.MapContour.Select(Point).ToArray());
                    floors.Add(shape);
                    byFloor.Add(shape.Key, shape);
                }
                AddFloorLinks(room, byFloor, links);
            }

            Mouth branchEntrance = default;
            if (draft.Branch is { } branch)
            {
                var ring = draft.RingRoom;
                if (branch.Descriptor.Outline is { } outline)
                {
                    foreach (var (name, bounds) in outline.Floors) AddBranchFloor(name, bounds);
                    branchEntrance = new Mouth(byFloor[(ring.Index, outline.Entrance.Floor)], outline.Entrance.Sill, Vector2.zero);
                    foreach (var (from, sill, to, landing) in outline.Passages)
                        links.Add((new Mouth(byFloor[(ring.Index, from)], sill, Vector2.zero),
                            new Mouth(byFloor[(ring.Index, to)], landing, Vector2.zero)));
                }
                else
                {
                    AddBranchFloor("ground", new Rect(0f, -6f, 12f, 12f));
                    branchEntrance = new Mouth(byFloor[(ring.Index, "ground")], Vector2.zero, Vector2.left);
                }

                void AddBranchFloor(string name, Rect bounds)
                {
                    var shape = new FloorShape(ring.Index, name, Rectangle(bounds.xMin, bounds.yMin, bounds.xMax, bounds.yMax));
                    floors.Add(shape);
                    byFloor.Add(shape.Key, shape);
                }
            }

            Mouth DockMouth(RaidPlan.End end)
            {
                if (end.Room == draft.RingRoom)
                    return branchEntrance;
                var shape = byFloor[(end.Room.Index, end.Dock.FloorId)];
                var floor = end.Room.Module.Floors.Single(item => item.Id == end.Dock.FloorId);
                var forward = end.Dock.Threshold.Forward;
                return new Mouth(shape, Point(end.Dock.MapPoint), new Vector2(
                    System.Numerics.Vector3.Dot(forward, floor.Pose.Forward),
                    System.Numerics.Vector3.Dot(forward, floor.Pose.Right)).normalized);
            }

            links.Add((new Mouth(stock, Vector2.zero, Vector2.left), DockMouth(draft.Entrance)));
            foreach (var link in draft.Freight) links.Add((DockMouth(link.From), DockMouth(link.To)));
            foreach (var (from, to) in draft.HandPassages) links.Add((DockMouth(from), DockMouth(to)));
            if (Arrange(4f, false) || Arrange(6f, true) || Arrange(9f, false) || Arrange(12f, true))
            {
                layout = new RaidPlanarLayout(floors);
                return true;
            }
            layout = null;
            return false;

            bool Arrange(float gap, bool reverse)
            {
                stock.Place(Vector2.zero, 180f);
                var placed = new HashSet<FloorShape> { stock };
                var reserved = new List<(Mouth A, Mouth B)>();
                while (placed.Count < floors.Count)
                {
                    var next = links.Where(link => placed.Contains(link.A.Floor) != placed.Contains(link.B.Floor));
                    if (reverse) next = next.Reverse();
                    var frontier = next.ToArray();
                    if (frontier.Length == 0)
                    {
                        var detached = floors.First(floor => !placed.Contains(floor));
                        detached.Place(new Vector2(placed.Max(floor => floor.Bounds.xMax) + gap, 0f) -
                            Enclose(detached.Outline).min, 0f);
                        placed.Add(detached);
                        continue;
                    }
                    bool found = false;
                    foreach (var link in frontier)
                    {
                        bool forward = placed.Contains(link.A.Floor);
                        if (!Place(forward ? link.A : link.B, forward ? link.B : link.A)) continue;
                        reserved.Add(link);
                        found = true;
                        break;
                    }
                    if (!found) return false;
                }
                foreach (var link in links)
                {
                    if (reserved.Contains(link)) continue;
                    if (!Clear(link.A, link.B, placed, reserved)) return false;
                    reserved.Add(link);
                }
                return true;

                bool Place(Mouth from, Mouth to)
                {
                    Vector2 direction = Turn(from.Outward, from.Floor.Pose.Angle);
                    Vector2 lateral = new Vector2(-direction.y, direction.x);
                    float angle = Vector2.SignedAngle(to.Outward, -direction);
                    Vector2 start = from.Floor.Point(from.Sill);
                    Vector2 anchor = start - Turn(to.Sill, angle);
                    for (int lane = 0; lane < 20; lane++)
                    {
                        for (int slot = 0; slot <= lane * 2; slot++)
                        {
                            int side = (slot % 2 == 0) == reverse ? 1 : -1;
                            float shift = (slot + 1) / 2 * side * gap;
                            to.Floor.Place(anchor + direction * ((lane + 1) * gap) + lateral * shift, angle);
                            Rect bounds = Pad(to.Floor.Bounds, .8f);
                            if (placed.Any(floor => bounds.Overlaps(floor.Bounds))) continue;
                            if (reserved.Any(link => Meets(Pad(bounds, CorridorPadding),
                                link.A.Floor.Point(link.A.Sill), link.B.Floor.Point(link.B.Sill)))) continue;
                            if (!Clear(from, to, placed, reserved)) continue;
                            placed.Add(to.Floor);
                            return true;
                        }
                    }
                    return false;
                }
            }
        }

        private static void AddFloorLinks(RaidPlan.Room room, Dictionary<(int, string), FloorShape> shapes,
            List<(Mouth A, Mouth B)> links)
        {
            var floors = room.Module.Floors;
            if (floors.Count < 2) return;
            var joined = new HashSet<(string, string)>();
            foreach (HaulRoute route in room.Module.Routes)
            {
                if (route.Flight.HasValue) continue;
                Floor previous = null;
                Vector2 previousPoint = default;
                foreach (ModulePose knot in route.Knots)
                {
                    Floor chosen = null;
                    Vector2 chosenPoint = default;
                    float best = .32f;
                    foreach (Floor floor in floors)
                    {
                        var delta = knot.Position - floor.Pose.Position;
                        float height = System.Numerics.Vector3.Dot(delta, floor.Pose.Up);
                        if (height < -.28f || height > best) continue;
                        Vector2 point = new Vector2(System.Numerics.Vector3.Dot(delta, floor.Pose.Forward),
                            System.Numerics.Vector3.Dot(delta, floor.Pose.Right));
                        var outline = shapes[(room.Index, floor.Id)].Outline;
                        float distance = Mathf.Abs(height);
                        if (!Contains(outline, point))
                            distance += Vector2.Distance(point, Boundary(outline, point, out _)) * 2f;
                        if (distance >= best) continue;
                        chosen = floor;
                        chosenPoint = point;
                        best = distance;
                    }
                    if (chosen == null) continue;
                    if (previous != null && chosen != previous && joined.Add((previous.Id, chosen.Id)))
                    {
                        joined.Add((chosen.Id, previous.Id));
                        links.Add((new Mouth(shapes[(room.Index, previous.Id)], previousPoint, Vector2.zero),
                            new Mouth(shapes[(room.Index, chosen.Id)], chosenPoint, Vector2.zero)));
                    }
                    previous = chosen;
                    previousPoint = chosenPoint;
                }
            }
        }

        private static bool Clear(Mouth a, Mouth b, IEnumerable<FloorShape> floors,
            IEnumerable<(Mouth A, Mouth B)> reserved)
        {
            Vector2 from = a.Floor.Point(a.Sill);
            Vector2 to = b.Floor.Point(b.Sill);
            if (floors.Any(floor => floor != a.Floor && floor != b.Floor &&
                Meets(Pad(floor.Bounds, CorridorPadding), from, to))) return false;
            foreach (var other in reserved)
            {
                Vector2 first = other.A.Floor.Point(other.A.Sill);
                Vector2 last = other.B.Floor.Point(other.B.Sill);
                if (DistanceBetween(from, to, first, last) < 2f * CorridorPadding) return false;
            }
            return true;
        }

        private static float DistanceBetween(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float abC = Cross(b - a, c - a), abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c), cdB = Cross(d - c, b - c);
            if (abC * abD < 0f && cdA * cdB < 0f) return 0f;
            return Mathf.Min(Distance(a, c, d), Distance(b, c, d), Distance(c, a, b), Distance(d, a, b));
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static float Distance(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 axis = end - start;
            return axis.sqrMagnitude < .0001f ? Vector2.Distance(point, start) :
                Vector2.Distance(point, start + axis * Mathf.Clamp01(Vector2.Dot(point - start, axis) / axis.sqrMagnitude));
        }

        private static bool Meets(Rect rect, Vector2 from, Vector2 to)
        {
            var delta = to - from;
            float enter = 0f, exit = 1f;
            return Clip(from.x, delta.x, rect.xMin, rect.xMax) && Clip(from.y, delta.y, rect.yMin, rect.yMax);

            bool Clip(float start, float movement, float min, float max)
            {
                if (Mathf.Abs(movement) < .0001f) return start >= min && start <= max;
                float a = (min - start) / movement, b = (max - start) / movement;
                enter = Mathf.Max(enter, Mathf.Min(a, b));
                exit = Mathf.Min(exit, Mathf.Max(a, b));
                return enter <= exit;
            }
        }

        internal static Vector2 Boundary(Vector2[] polygon, Vector2 point, out Vector2 normal)
        {
            float nearest = float.PositiveInfinity;
            Vector2 result = default;
            normal = default;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                var axis = b - a;
                if (axis.sqrMagnitude < .000001f) continue;
                Vector2 p = a + axis * Mathf.Clamp01(Vector2.Dot(point - a, axis) / axis.sqrMagnitude);
                float distance = (point - p).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                result = p;
                normal = new Vector2(axis.y, -axis.x).normalized;
            }
            if (Contains(polygon, result + normal * .03f)) normal = -normal;
            return result;
        }

        private static bool Contains(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        private static Vector2 Turn(Vector2 point, float angle)
        {
            float cosine = Mathf.Cos(angle * Mathf.Deg2Rad), sine = Mathf.Sin(angle * Mathf.Deg2Rad);
            return new Vector2(cosine * point.x - sine * point.y, sine * point.x + cosine * point.y);
        }

        private static Rect Pad(Rect rect, float margin) =>
            Rect.MinMaxRect(rect.xMin - margin, rect.yMin - margin, rect.xMax + margin, rect.yMax + margin);

        private static Rect Enclose(IEnumerable<Vector2> outline)
        {
            Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var point in outline)
            {
                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
            }
            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        private static Vector2[] Rectangle(float minX, float minY, float maxX, float maxY) =>
            new[] { new Vector2(minX, minY), new Vector2(maxX, minY), new Vector2(maxX, maxY), new Vector2(minX, maxY) };

        private static Vector2 Point(System.Numerics.Vector2 point) => new Vector2(point.X, point.Y);
    }
}

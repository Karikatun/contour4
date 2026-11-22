using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using UnityEngine;
using CatalogFloor = Contour4.Procedural.Floor;
using ModulePose = Contour4.Procedural.ModulePose;

namespace SpaceJourney
{
    internal sealed class RaidMapPlan
    {
        private const int Width = 640;
        private const int Height = 320;
        private const float Gap = 4f;
        private const float Margin = 12f;
        private static readonly Color32[] PairColors =
        {
            new Color32(93, 211, 255, 255), new Color32(255, 168, 62, 255), new Color32(226, 134, 241, 255),
            new Color32(157, 234, 87, 255), new Color32(255, 111, 117, 255),
            new Color32(98, 237, 190, 255), new Color32(255, 226, 91, 255), new Color32(119, 142, 247, 255),
            new Color32(245, 134, 206, 255), new Color32(255, 96, 52, 255),
        };

        internal sealed class FloorView
        {
            internal readonly CatalogFloor Floor;
            internal readonly Pose Room;
            internal readonly Vector2[] Local;
            internal Vector2[] Outline;
            internal Vector2 Position;
            internal float Angle;
            internal Rect Bounds;
            internal bool Revealed;
            internal readonly Rect[] Slabs;
            internal int Story => Mathf.Max(0, Mathf.RoundToInt(Floor.Pose.Position.Y / 4f));
            private readonly ModulePose[] course;
            private readonly float[] distances;
            private readonly float length;

            internal FloorView(CatalogFloor floor, Pose room, IReadOnlyList<HaulRoute> routes, IReadOnlyList<Block> blocks = null)
            {
                Floor = floor;
                Room = room;
                Local = floor.MapContour.Select(Point).ToArray();
                length = Local.Max(p => p.x);
                course = Course(routes, floor, length);
                Slabs = course == null && blocks != null ? blocks.Where(block => block.Role == BlockRole.Floor)
                    .Select(block => block.Bounds)
                    .Where(bounds => Mathf.Abs(bounds.Max.Y - floor.Pose.Position.Y) < .005f)
                    .Select(bounds => new Rect(bounds.Min.X - floor.Pose.Position.X, bounds.Min.Z - floor.Pose.Position.Z,
                        bounds.Max.X - bounds.Min.X, bounds.Max.Z - bounds.Min.Z))
                    .Where(slab => Contains(Local, slab.center)).ToArray()
                    : Array.Empty<Rect>();
                if (course == null) return;
                distances = new float[course.Length];
                for (int i = 1; i < course.Length; i++)
                    distances[i] = distances[i - 1] + Vector3.Distance(V(course[i - 1].Position), V(course[i].Position));
            }

            internal Vector2 Project(Vector3 world, out float height)
            {
                return ProjectLocal(Quaternion.Inverse(Room.rotation) * (world - Room.position), out height);
            }

            internal Vector2 ProjectLocal(Vector3 local, out float height)
            {
                if (course == null)
                {
                    Vector3 delta = local - V(Floor.Pose.Position);
                    height = Vector3.Dot(delta, V(Floor.Pose.Up));
                    return new Vector2(Vector3.Dot(delta, V(Floor.Pose.Forward)), Vector3.Dot(delta, V(Floor.Pose.Right)));
                }

                float best = float.PositiveInfinity;
                Vector2 point = default;
                height = 0f;
                for (int i = 1; i < course.Length; i++)
                {
                    Vector3 start = V(course[i - 1].Position);
                    Vector3 path = V(course[i].Position) - start;
                    float t = Mathf.Clamp01(Vector3.Dot(local - start, path) / path.sqrMagnitude);
                    Vector3 delta = local - (start + t * path);
                    if (delta.sqrMagnitude >= best) continue;
                    best = delta.sqrMagnitude;
                    Vector3 up = Vector3.Lerp(V(course[i - 1].Up), V(course[i].Up), t).normalized;
                    Vector3 right = Vector3.Lerp(V(course[i - 1].Right), V(course[i].Right), t).normalized;
                    height = Vector3.Dot(delta, up);
                    point = new Vector2(Mathf.Lerp(distances[i - 1], distances[i], t) / distances[distances.Length - 1] * length,
                        Vector3.Dot(delta, right));
                    // Clamped centres must not claim players beyond either end cap.
                    if ((i == 1 && t == 0f) || (i == course.Length - 1 && t == 1f))
                        point.x += Vector3.Dot(delta, path.normalized);
                }
                return point;
            }

            private static ModulePose[] Course(IReadOnlyList<HaulRoute> routes, CatalogFloor floor, float length)
            {
                if (length < 3f) return null;
                foreach (var route in routes)
                {
                    var knots = route.Knots;
                    int start = Array.FindIndex(knots, p =>
                        (p.Position - floor.Pose.Position).LengthSquared() < .01f &&
                        System.Numerics.Vector3.Dot(p.Forward, floor.Pose.Forward) > .999f);
                    if (start < 0) continue;
                    var chosen = new List<ModulePose> { knots[start] };
                    float distance = 0f;
                    bool bends = false;
                    for (int i = start + 1; i < knots.Length; i++)
                    {
                        float step = (knots[i].Position - chosen[chosen.Count - 1].Position).Length();
                        if (step < .001f) continue;
                        distance += step;
                        if (distance > length + .25f) break;
                        bends |= System.Numerics.Vector3.Dot(knots[i].Up, floor.Pose.Up) < .995f;
                        chosen.Add(knots[i]);
                    }
                    if (bends && distance >= length - .25f && chosen.Count > 2) return chosen.ToArray();
                }
                return null;
            }

            internal void Place(Vector2 position, float angle)
            {
                Position = position;
                Angle = angle;
                Outline = Local.Select(Flat).ToArray();
                Bounds = Enclose(Outline);
            }

            internal Vector2 Flat(Vector2 point) => Position + Turn(point, Angle);

            internal float FloorDistance(Vector3 position, out Vector2 point)
            {
                point = Project(position, out float height);
                return ContactDistance(point, height);
            }

            internal float ContactDistance(Vector2 point, float height)
            {
                if (height < -.28f || height > 2.8f) return float.PositiveInfinity;
                float horizontal = Contains(Local, point) ? 0f : Vector2.Distance(point, Boundary(Local, point, out _));
                if (horizontal > .42f) return float.PositiveInfinity;
                return Mathf.Abs(height) + horizontal * 2f;
            }
        }

        internal readonly struct End
        {
            internal readonly FloorView Floor;
            internal readonly Vector2 Point;
            internal readonly Vector2 Edge;
            internal readonly Vector2 Outward;
            internal readonly float Width;

            internal End(FloorView floor, Vector2 point, Vector2? expectedOutward = null, float width = 3f)
            {
                Floor = floor;
                Point = point;
                Width = width;
                Edge = Boundary(floor.Local, point, out var outward);
                Outward = expectedOutward.HasValue && Vector2.Dot(expectedOutward.Value, outward) > .55f
                    ? expectedOutward.Value : outward;
            }

            internal Vector2 Outside => Floor.Flat(Edge + Outward * 1.3f);
        }

        internal sealed class Link
        {
            internal readonly End A;
            internal readonly End B;
            internal readonly Vector3[] Transition;
            internal readonly bool Stairs;
            internal Vector2Int[] Path;
            internal bool Visited;

            internal Link(End a, End b, Vector3[] transition = null, bool stairs = false)
            {
                A = a;
                B = b;
                Transition = transition;
                Stairs = stairs;
            }

            internal bool TryProjectTransition(Vector3 position, out float progress)
            {
                progress = 0f;
                float distance = 0f, nearest = float.PositiveInfinity;
                for (int i = 1; i < Transition.Length; i++)
                {
                    Vector3 start = Transition[i - 1];
                    Vector3 segment = Transition[i] - start;
                    float length = segment.magnitude;
                    if (length < .001f) continue;
                    float t = Mathf.Clamp01(Vector3.Dot(position - start, segment) / segment.sqrMagnitude);
                    float error = (position - (start + segment * t)).sqrMagnitude;
                    if (error < nearest)
                    {
                        nearest = error;
                        progress = distance + length * t;
                    }
                    distance += length;
                }
                if (distance < .001f || nearest > 1.4f * 1.4f) return false;
                progress /= distance;
                return progress > .12f && progress < .88f;
            }
        }

        internal sealed class Door
        {
            internal readonly End End;
            internal readonly Color32 Color;
            internal readonly bool Portal;

            internal Door(End end, Color32 color, bool portal) { End = end; Color = color; Portal = portal; }
        }

        private readonly List<FloorView> floors;
        private readonly List<Link> links;
        private readonly List<Door> doors;
        private float scale;
        private Vector2 origin;
        internal FloorView Stock { get; }
        internal IReadOnlyList<FloorView> Floors => floors;
        internal IReadOnlyList<Link> Links => links;
        internal IReadOnlyList<Door> Doors => doors;

        internal static RaidMapPlan Create(RaidPlan plan, Bounds? startBounds = null,
            RingRuntime ring = null, RaidPlan.End? ringEntrance = null, Matrix4x4? ringWorldToPlan = null)
        {
            var floors = new List<FloorView>();
            var byRoom = new Dictionary<RaidPlan.Room, Dictionary<string, FloorView>>();
            var links = new List<Link>();
            var doors = new List<Door>();
            foreach (var room in plan.Rooms)
            {
                var views = room.Module.Floors.ToDictionary(f => f.Id,
                    f => new FloorView(f, room.Frame, room.Module.Routes, room.Module.Geometry));
                byRoom.Add(room, views);
                floors.AddRange(views.Values);
                ConnectFloors(room.Module.Routes, views.Values.ToArray(), links);

            }

            End DockEnd(RaidPlan.End end)
            {
                var floor = byRoom[end.Room][end.Dock.FloorId];
                Vector3 direction = V(end.Dock.Threshold.Forward);
                return new End(floor, Point(end.Dock.MapPoint),
                    new Vector2(Vector3.Dot(direction, V(floor.Floor.Pose.Forward)),
                        Vector3.Dot(direction, V(floor.Floor.Pose.Right))).normalized, end.Dock.Opening.X);
            }

            void Physical(End a, End b, Vector3[] course = null)
            {
                links.Add(new Link(a, b, course));
                doors.Add(new Door(a, Color.white, false));
                doors.Add(new Door(b, Color.white, false));
            }

            Vector3[] FoldCourse(RaidPlan.FreightLink link) => link.Fold?.Module.Routes[0].Knots
                .Select(knot => link.Fold.Point(knot.Position)).ToArray();
            foreach (var link in plan.Freight.Where(link => link.To.Room != plan.RingRoom))
                Physical(DockEnd(link.From), DockEnd(link.To), FoldCourse(link));

            // Draw the native truck using its stock map floor extent where it is present.
            var entrance = plan.Entrance;
            var outward = entrance.Outward;
            var stockPose = new Pose(entrance.Sill, Quaternion.LookRotation(Vector3.Cross(outward, Vector3.up), Vector3.up));
            float depth = 9f, halfWidth = 3.5f;
            if (startBounds.HasValue)
            {
                Bounds bounds = startBounds.Value;
                Vector3 offset = bounds.center - entrance.Sill;
                depth = Mathf.Max(4f, Vector3.Dot(offset, outward) + Vector3.Dot(bounds.extents, Abs(outward)));
                Vector3 lateral = stockPose.rotation * Vector3.forward;
                halfWidth = Mathf.Max(entrance.Dock.Opening.X * .55f,
                    Mathf.Abs(Vector3.Dot(offset, lateral)) + Vector3.Dot(bounds.extents, Abs(lateral)));
            }
            var stock = Rectangle("stock-start", stockPose, new Rect(0, -halfWidth, depth, halfWidth * 2f));
            stock.Revealed = true;
            floors.Insert(0, stock);
            Physical(new End(stock, Vector2.zero, Vector2.left), DockEnd(entrance));

            int pair = 0;
            void PortalPair(End a, End b)
            {
                var color = pair < PairColors.Length ? PairColors[pair] :
                    (Color32)Color.HSVToRGB(Mathf.Repeat(pair * .618034f, 1f), .68f, .96f);
                doors.Add(new Door(a, color, true));
                doors.Add(new Door(b, color, true));
                pair++;
            }
            foreach (var (a, b) in plan.Portals) PortalPair(DockEnd(a), DockEnd(b));

            if (ring && ringEntrance.HasValue)
            {
                Matrix4x4 frame = ringWorldToPlan ?? Matrix4x4.identity;
                Quaternion rotation = frame.rotation;
                var branch = ring.Rooms.Select(room => Rectangle(room.name,
                    new Pose(frame.MultiplyPoint3x4(room.position), rotation * room.rotation), new Rect(-6, -6, 12, 12))).ToArray();
                floors.AddRange(branch);
                var entry = RingRuntime.Marker(ring.Rooms[0], "Quota__South_Truck");
                Vector3 point = frame.MultiplyPoint3x4(entry.position);
                Vector3[] courseToRing = FoldCourse(plan.Freight.Last())?.Append(point).ToArray();
                Physical(DockEnd(ringEntrance.Value), new End(branch[0], branch[0].Project(point, out _)), courseToRing);

                string[] bridges = { "BridgeQuotaLoot1", "BridgeLoot1Loot2" };
                for (int i = 0; i < bridges.Length; i++)
                {
                    var bridge = ring.Ring.Find(bridges[i]);
                    Vector3[] course = BridgeCourse(bridge).Select(frame.MultiplyPoint3x4).ToArray();
                    links.Add(new Link(new End(branch[i], branch[i].Project(course[0], out _)),
                        new End(branch[i + 1], branch[i + 1].Project(course[course.Length - 1], out _)), course));
                }

                End PortalEnd(PortalEnd portal)
                {
                    var floor = branch[Array.IndexOf(ring.Rooms, portal.Room)];
                    return new End(floor, floor.Project(frame.MultiplyPoint3x4(portal.Marker.position), out _));
                }
                foreach (var portal in ring.Portals) PortalPair(PortalEnd(portal.A), PortalEnd(portal.B));
            }

            return new RaidMapPlan(floors, links, doors, stock);
        }

        private static FloorView Rectangle(string name, Pose pose, Rect bounds) =>
            new FloorView(new CatalogFloor(name, GrayBuilder.Level, new[]
            {
                new System.Numerics.Vector2(bounds.xMin, bounds.yMin), new System.Numerics.Vector2(bounds.xMax, bounds.yMin),
                new System.Numerics.Vector2(bounds.xMax, bounds.yMax), new System.Numerics.Vector2(bounds.xMin, bounds.yMax),
            }), pose, Array.Empty<HaulRoute>());

        private static Vector3[] BridgeCourse(Transform bridge)
        {
            Vector3 start = RingRuntime.Marker(bridge, bridge.name + "__Start").localPosition;
            Vector3 end = RingRuntime.Marker(bridge, bridge.name + "__End").localPosition;
            Vector3 delta = end - start;
            const int segments = 12;
            return Enumerable.Range(0, segments + 1).Select(i =>
            {
                float angle = Mathf.PI / 2f * i / segments;
                return bridge.TransformPoint(start + new Vector3(0, delta.y * (1 - Mathf.Cos(angle)), delta.z * Mathf.Sin(angle)));
            }).ToArray();
        }

        private RaidMapPlan(List<FloorView> floors, List<Link> links, List<Door> doors,
            FloorView stock)
        {
            this.floors = floors;
            this.links = links;
            this.doors = doors;
            Stock = stock;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Arrange(Gap + attempt * 2f);
                Rect extent = Enclose(floors.SelectMany(f => f.Outline));
                if (extent.height > extent.width)
                {
                    foreach (var floor in floors) floor.Place(Turn(floor.Position, -90f), floor.Angle - 90f);
                    extent = Enclose(floors.SelectMany(f => f.Outline));
                }
                scale = Mathf.Min((Width - 2 * Margin) / extent.width, (Height - 2 * Margin) / extent.height);
                origin = new Vector2(Width * .5f, Height * .5f) - extent.center * scale;
                if (RouteLinks(reverse: false, clearance: 2) || RouteLinks(reverse: true, clearance: 2) ||
                    RouteLinks(reverse: false, clearance: 1)) return;
            }
            throw new InvalidOperationException("No non-crossing physical route on the flat raid map.");
        }

        private bool RouteLinks(bool reverse, int clearance)
        {
            bool[] blocked = RasterizeFootprints();
            var routes = links.Select(link => (Link: link, From: Pixel(link.A.Outside), To: Pixel(link.B.Outside)))
                .OrderBy(route => (reverse ? -1 : 1) * (route.From - route.To).sqrMagnitude).ToArray();
            for (int i = 0; i < routes.Length; i++)
            {
                var (link, from, to) = routes[i];
                var clear = (bool[])blocked.Clone();
                for (int next = i + 1; next < routes.Length; next++)
                {
                    Reserve(clear, routes[next].From, clearance);
                    Reserve(clear, routes[next].To, clearance);
                }
                link.Path = Trace(from, to, clear);
                if (link.Path == null) return false;
                foreach (var point in link.Path) Reserve(blocked, point, clearance);
            }
            return true;
        }

        private static void Reserve(bool[] blocked, Vector2Int point, int clearance)
        {
            for (int y = Mathf.Max(0, point.y - clearance); y <= Mathf.Min(Height - 1, point.y + clearance); y++)
            for (int x = Mathf.Max(0, point.x - clearance); x <= Mathf.Min(Width - 1, point.x + clearance); x++)
                blocked[y * Width + x] = true;
        }

        private static void ConnectFloors(IReadOnlyList<HaulRoute> routes, FloorView[] floors, List<Link> links)
        {
            if (floors.Length < 2) return;
            var added = new HashSet<(FloorView, FloorView)>();
            foreach (var route in routes)
            {
                if (route.Flight.HasValue) continue;
                FloorView previous = null;
                Vector2 previousPoint = default;
                int previousKnot = 0;
                for (int knotIndex = 0; knotIndex < route.Knots.Length; knotIndex++)
                {
                    ModulePose knot = route.Knots[knotIndex];
                    FloorView surface = null;
                    Vector2 point = default;
                    float best = .32f;
                    foreach (var candidate in floors)
                    {
                        Vector2 location = candidate.ProjectLocal(V(knot.Position), out float height);
                        float distance = candidate.ContactDistance(location, height);
                        if (distance >= best) continue;
                        best = distance;
                        surface = candidate;
                        point = location;
                    }
                    if (surface == null) continue;
                    if (previous != null && surface != previous && added.Add((previous, surface)))
                    {
                        added.Add((surface, previous));
                        // A shared threshold belongs to both floors. Use the first contact, even
                        // when the old floor won the distance tie at that exact route knot.
                        int landingKnot = knotIndex;
                        Vector2 landing = point;
                        for (int i = previousKnot; i < knotIndex; i++)
                        {
                            Vector2 contact = surface.ProjectLocal(V(route.Knots[i].Position), out float height);
                            if (surface.ContactDistance(contact, height) >= .32f) continue;
                            landingKnot = i;
                            landing = contact;
                            break;
                        }
                        Vector3[] transition = route.Knots.Skip(previousKnot).Take(landingKnot - previousKnot + 1)
                            .Select(frame => surface.Room.position + surface.Room.rotation * V(frame.Position)).ToArray();
                        bool stairs = landingKnot > previousKnot &&
                            Vector3.Distance(transition[0], transition[transition.Length - 1]) > .5f;
                        links.Add(new Link(new End(previous, previousPoint), new End(surface, landing), transition, stairs));
                    }
                    previous = surface;
                    previousPoint = point;
                    previousKnot = knotIndex;
                }
            }
        }

        private void Arrange(float gap)
        {
            var placed = new HashSet<FloorView> { Stock };
            Stock.Place(Vector2.zero, 180f);
            while (placed.Count != floors.Count)
            {
                var edge = links.FirstOrDefault(link => placed.Contains(link.A.Floor) != placed.Contains(link.B.Floor));
                if (edge == null)
                {
                    // Portal-only rooms have no physical planar anchor.
                    var floor = floors.First(candidate => !placed.Contains(candidate));
                    floor.Place(new Vector2(placed.Max(f => f.Bounds.xMax) + gap, 0f) - Enclose(floor.Local).min, 0f);
                    placed.Add(floor);
                    continue;
                }
                bool fromA = placed.Contains(edge.A.Floor);
                End from = fromA ? edge.A : edge.B;
                End to = fromA ? edge.B : edge.A;
                Vector2 direction = Turn(from.Outward, from.Floor.Angle);
                Vector2 lateral = new Vector2(-direction.y, direction.x);
                float angle = Vector2.SignedAngle(to.Outward, -direction);
                Vector2 start = from.Floor.Flat(from.Edge) - Turn(to.Edge, angle);
                for (int lane = 0; ; lane++)
                {
                    float along = gap + lane * gap;
                    bool found = false;
                    for (int slot = 0; slot <= lane * 2; slot++)
                    {
                        int shift = (slot + 1) / 2 * (slot % 2 == 0 ? -1 : 1);
                        to.Floor.Place(start + direction * along + lateral * (shift * gap), angle);
                        Rect bounds = to.Floor.Bounds;
                        bounds.min -= Vector2.one * (gap * .7f);
                        bounds.max += Vector2.one * (gap * .7f);
                        if (placed.Any(f => bounds.Overlaps(f.Bounds))) continue;
                        found = true;
                        break;
                    }
                    if (found) break;
                }
                placed.Add(to.Floor);
            }
        }

        internal bool Project(Vector3 position, Vector3 forward, bool visit, out FloorView floor,
            out Vector2 point, out Vector2 heading)
        {
            floor = null;
            point = heading = default;
            float best = float.PositiveInfinity;
            foreach (var candidate in floors)
            {
                float distance = candidate.FloorDistance(position, out Vector2 local);
                if (distance >= best) continue;
                best = distance;
                floor = candidate;
                point = candidate.Flat(local);
            }
            if (best > .5f)
            {
                foreach (var link in links)
                {
                    if (link.Transition == null || !link.TryProjectTransition(position, out float progress)) continue;
                    floor = progress < .5f ? link.A.Floor : link.B.Floor;
                    int index = Mathf.RoundToInt(progress * (link.Path.Length - 1));
                    point = Unpixel(link.Path[index]);
                    int before = Mathf.Max(0, index - 1), after = Mathf.Min(link.Path.Length - 1, index + 1);
                    heading = (Vector2)(link.Path[after] - link.Path[before]);
                    if (Vector3.Dot(forward, link.Transition[link.Transition.Length - 1] - link.Transition[0]) < 0f)
                        heading = -heading;
                    if (visit) link.Visited = true;
                    return true;
                }
            }
            if (floor == null) return false;
            if (visit) floor.Revealed = true;
            heading = floor.Flat(floor.Project(position + forward * .4f, out _)) - point;
            return true;
        }

        // Anchor the projection to the actual stock truck. The stock camera and its native
        // metres-to-map scale see an ordinary XZ level even when the underlying raid is folded.
        internal Vector3 NativeVector(Vector2 flat)
        {
            var local = Turn(flat, -Stock.Angle);
            return Stock.Room.rotation * (V(Stock.Floor.Pose.Forward) * local.x + V(Stock.Floor.Pose.Right) * local.y);
        }

        internal Vector3 NativePoint(Vector2 flat) => Stock.Room.position + NativeVector(flat - Stock.Position);

        internal IEnumerable<Vector2> Corridor(Link link)
        {
            yield return link.A.Floor.Flat(link.A.Edge);
            yield return link.A.Outside;
            Vector2Int previous = default;
            for (int i = 1; i < link.Path.Length; i++)
            {
                Vector2Int step = link.Path[i] - link.Path[i - 1];
                if (i > 1 && step != previous) yield return Unpixel(link.Path[i - 1]);
                previous = step;
            }
            yield return link.B.Outside;
            yield return link.B.Floor.Flat(link.B.Edge);
        }

        private Vector2 Unpixel(Vector2Int pixel) => ((Vector2)pixel - origin) / scale;

        private bool[] RasterizeFootprints()
        {
            var blocked = new bool[Width * Height];
            foreach (var floor in floors) Visit(floor, (x, y) => blocked[y * Width + x] = true);
            return blocked;
        }

        private void Visit(FloorView floor, Action<int, int> visit)
        {
            Vector2Int min = Pixel(floor.Bounds.min);
            Vector2Int max = Pixel(floor.Bounds.max);
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
                if (Contains(floor.Outline, (new Vector2(x, y) - origin) / scale)) visit(x, y);
        }

        private static Vector2Int[] Trace(Vector2Int from, Vector2Int to, bool[] blocked)
        {
            if (from.x < 3 || from.y < 3 || from.x >= Width - 3 || from.y >= Height - 3 ||
                to.x < 3 || to.y < 3 || to.x >= Width - 3 || to.y >= Height - 3) return null;
            var predecessor = new int[Width * Height];
            Array.Fill(predecessor, -1);
            int start = from.y * Width + from.x, end = to.y * Width + to.x;
            if (blocked[start] || blocked[end]) return null;
            var queue = new Queue<int>();
            queue.Enqueue(start);
            predecessor[start] = start;
            while (queue.Count != 0 && predecessor[end] == -1)
            {
                int p = queue.Dequeue();
                if (p % Width > 3) Visit(p - 1, p);
                if (p % Width < Width - 4) Visit(p + 1, p);
                if (p / Width > 3) Visit(p - Width, p);
                if (p / Width < Height - 4) Visit(p + Width, p);
            }

            void Visit(int p, int parent)
            {
                if (predecessor[p] != -1 || (p != end && blocked[p])) return;
                predecessor[p] = parent;
                queue.Enqueue(p);
            }

            if (predecessor[end] == -1) return null;
            var path = new List<Vector2Int> { to };
            for (int p = end; p != start;)
            {
                p = predecessor[p];
                path.Add(new Vector2Int(p % Width, p / Width));
            }
            path.Reverse();
            return path.ToArray();
        }

        private Vector2Int Pixel(Vector2 point) => Vector2Int.RoundToInt(origin + point * scale);

        private static Vector2 Boundary(Vector2[] polygon, Vector2 point, out Vector2 normal)
        {
            float best = float.PositiveInfinity;
            Vector2 result = point;
            normal = Vector2.right;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i];
                Vector2 edge = polygon[(i + 1) % polygon.Length] - a;
                if (edge.sqrMagnitude < .0001f) continue;
                Vector2 nearest = a + edge * Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude);
                float distance = (nearest - point).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                result = nearest;
                normal = new Vector2(edge.y, -edge.x).normalized;
                if (Contains(polygon, nearest + normal * .1f)) normal = -normal;
            }
            return result;
        }

        private static bool Contains(Vector2[] polygon, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        private static Rect Enclose(IEnumerable<Vector2> points)
        {
            Vector2 low = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 high = -low;
            foreach (var point in points) { low = Vector2.Min(low, point); high = Vector2.Max(high, point); }
            return Rect.MinMaxRect(low.x, low.y, high.x, high.y);
        }

        private static Vector2 Turn(Vector2 point, float angle)
        {
            float sin = Mathf.Sin(angle * Mathf.Deg2Rad), cos = Mathf.Cos(angle * Mathf.Deg2Rad);
            return new Vector2(cos * point.x - sin * point.y, sin * point.x + cos * point.y);
        }

        private static Vector2 Point(System.Numerics.Vector2 point) => new Vector2(point.X, point.Y);
        private static Vector3 V(System.Numerics.Vector3 point) => RaidPlan.Vector(point);
        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    }
}

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
        internal const float CorridorWidth = 1.45f;
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
            internal readonly Vector2[][] SurfaceRoutes;
            internal int Story => Mathf.Max(0, Mathf.RoundToInt(Floor.Pose.Position.Y / 4f));
            private readonly ModulePose[] course;
            private readonly float[] distances;
            private readonly float length;
            private readonly int sphereMask;
            private readonly bool mobius;

            internal FloorView(CatalogFloor floor, Pose room, IReadOnlyList<HaulRoute> routes,
                IReadOnlyList<Block> blocks = null, string moduleId = null)
            {
                Floor = floor;
                Room = room;
                sphereMask = floor.Id == "ground" ? SphericalChamber.GetMask(moduleId) : 0;
                mobius = moduleId == MobiusHall.Id;
                Local = sphereMask != 0 ? SphereOutline((sphereMask & SphericalChamber.EastMask) != 0) :
                    floor.MapContour.Select(Point).ToArray();
                length = Local.Max(p => p.x);
                course = Course(routes, floor, length);
                SurfaceRoutes = (sphereMask & (SphericalChamber.UpperMask | SphericalChamber.LowerMask)) switch
                {
                    SphericalChamber.UpperMask => new[] { SphereRoute(1) },
                    SphericalChamber.LowerMask => new[] { SphereRoute(-1) },
                    SphericalChamber.UpperMask | SphericalChamber.LowerMask => new[] { SphereRoute(1), SphereRoute(-1) },
                    _ => Array.Empty<Vector2[]>(),
                };
                Slabs = course == null && blocks != null ? blocks.Where(block => block.Role == BlockRole.Floor)
                    .Where(block => Mathf.Abs(block.Vertices.Max(vertex => Vector3.Dot(
                        V(vertex - floor.Pose.Position), V(floor.Pose.Up)))) < .005f)
                    .Select(block => Enclose(block.Vertices.Select(vertex => ProjectLocal(V(vertex), out _))))
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
                if (sphereMask != 0 && point.sqrMagnitude + height * height <= Mathf.Pow(SphericalChamber.Radius + .32f, 2))
                {
                    float leadStart = SphericalChamber.BranchFrame(1, SphericalChamber.BranchPhaseCount - 1, 0, 0).Position.Y;
                    int branch = height > 0f ? SphericalChamber.UpperMask : SphericalChamber.LowerMask;
                    if ((sphereMask & branch) != 0 && Mathf.Abs(height) >= leadStart && Mathf.Abs(point.x) <= SphericalChamber.HalfWidth &&
                        point.y >= -2.8f && point.y <= .28f)
                        return float.PositiveInfinity;
                    return 0f;
                }
                if (mobius && Contains(Local, point))
                {
                    float roomY = (Quaternion.Inverse(Room.rotation) * (position - Room.position)).y;
                    if (roomY >= -.25f && roomY <= MobiusHall.InteriorHeight + .25f)
                    {
                        bool ceilingHalf = roomY >= MobiusHall.InteriorHeight / 2f;
                        return (Floor.Id == "ceiling") == ceilingHalf ? 0f : float.PositiveInfinity;
                    }
                }
                return ContactDistance(point, height);
            }

            private static Vector2[] SphereOutline(bool east)
            {
                const int steps = 24;
                float span = SphericalChamber.HalfReservation;
                float width = SphericalChamber.HalfWidth;
                float radius = SphericalChamber.Radius;
                float edge = Mathf.Asin(width / radius);
                var outline = new List<Vector2>();
                if (east)
                {
                    outline.Add(new Vector2(span, -width));
                    outline.Add(new Vector2(span, width));
                }
                Arc(east ? edge : 0f, Mathf.PI - edge);
                outline.Add(new Vector2(-span, width));
                outline.Add(new Vector2(-span, -width));
                Arc(Mathf.PI + edge, east ? 2 * Mathf.PI - edge : 2 * Mathf.PI);
                if (!east) outline.RemoveAt(outline.Count - 1);
                return outline.ToArray();

                void Arc(float from, float to)
                {
                    for (int i = 0; i <= steps; i++)
                    {
                        float angle = Mathf.Lerp(from, to, i / (float)steps);
                        outline.Add(radius * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
                    }
                }
            }

            private static Vector2[] SphereRoute(int side)
            {
                var start = SphericalChamber.BranchFrame(side, 0, 0, 0).Position;
                var turn = SphericalChamber.BranchFrame(side, 0, 1, 0).Position;
                return new[] { new Vector2(start.X, start.Z), new Vector2(turn.X, turn.Z) };
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
            internal readonly Vector2 Across;
            internal readonly float Width;
            internal readonly bool Interior;

            internal End(FloorView floor, Vector2 point, Vector2? expectedOutward = null, float width = 3f,
                bool interior = false, Vector2? across = null)
            {
                Floor = floor;
                Point = point;
                Width = width;
                Interior = interior;
                if (Interior)
                {
                    Edge = point;
                    Outward = expectedOutward is Vector2 direction && direction.sqrMagnitude > .0001f
                        ? direction : Vector2.right;
                    Across = across is Vector2 span && span.sqrMagnitude > .0001f
                        ? span.normalized : new Vector2(-Outward.y, Outward.x);
                }
                else
                {
                    Edge = Boundary(floor.Local, point, out var outward);
                    Outward = expectedOutward.HasValue && Vector2.Dot(expectedOutward.Value, outward) > .55f
                        ? expectedOutward.Value : outward;
                    Across = new Vector2(-outward.y, outward.x);
                }
            }

            internal Vector2 Outside => Floor.Flat(Edge + Outward * 1.3f);
        }

        internal sealed class Link
        {
            internal readonly End A;
            internal readonly End B;
            internal readonly Vector3[] Transition;
            internal readonly bool Stairs;
            internal Vector2[] Path;
            internal bool Straight;
            internal bool Bridge => !Stairs && Transition != null && Transition.Length > 1;
            internal Vector2 Start => A.Floor.Flat(A.Edge);
            internal Vector2 Finish => B.Floor.Flat(B.Edge);
            internal bool Visited;

            internal Link(End a, End b, Vector3[] transition = null, bool stairs = false)
            {
                A = a;
                B = b;
                Transition = transition;
                Stairs = stairs;
            }

            internal bool TryProjectTransition(Vector3 position, out float progress, out Vector3 tangent)
            {
                progress = 0f;
                tangent = default;
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
                        tangent = segment / length;
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

            internal (Vector2 From, Vector2 To) PortalStroke()
            {
                Vector2 center = End.Interior ? End.Point : End.Edge;
                Vector2 half = End.Across * (End.Width / 2f);
                return (End.Floor.Flat(center - half), End.Floor.Flat(center + half));
            }
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
            RingRuntime ring = null, RaidPlan.End? ringEntrance = null, Matrix4x4? ringWorldToPlan = null,
            IEnumerable<RaidPlan.Room> preparedRooms = null)
        {
            var floors = new List<FloorView>();
            var byRoom = new Dictionary<RaidPlan.Room, Dictionary<string, FloorView>>();
            var links = new List<Link>();
            var doors = new List<Door>();
            var flat = new Dictionary<FloorView, RaidPlanarLayout.FlatPose>();
            void Bind(FloorView view, int room, string floor)
            {
                if (plan.Planar != null && plan.Planar.TryGetPose(room, floor, out var pose))
                    flat.Add(view, pose);
            }
            foreach (var room in plan.Rooms)
            {
                var views = room.Module.Floors.ToDictionary(f => f.Id,
                    f => new FloorView(f, room.Frame, room.Module.Routes, room.Module.Geometry, room.Module.Id));
                byRoom.Add(room, views);
                floors.AddRange(views.Values);
                foreach (var (id, view) in views) Bind(view, room.Index, id);
                ConnectFloors(room.Module.Routes, views.Values.ToArray(), links);

            }

            End DockEnd(RaidPlan.End end, float? opening = null)
            {
                var floor = byRoom[end.Room][end.Dock.FloorId];
                bool hatch = (end.Dock.Use & (DockUse.UpperPortal | DockUse.LowerPortal)) != 0;
                var threshold = end.Dock.Threshold;
                Vector3 direction = V(hatch ? threshold.Up : threshold.Forward);
                Vector2 across = floor.ProjectLocal(V(threshold.Position + threshold.Right), out _) -
                    floor.ProjectLocal(V(threshold.Position - threshold.Right), out _);
                return new End(floor, Point(end.Dock.MapPoint),
                    new Vector2(Vector3.Dot(direction, V(floor.Floor.Pose.Forward)),
                        Vector3.Dot(direction, V(floor.Floor.Pose.Right))).normalized,
                    opening ?? end.Dock.Opening.X, hatch, across);
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
            foreach (var (from, to) in plan.HandPassages)
            {
                float width = Mathf.Min(from.Dock.Opening.X, to.Dock.Opening.X);
                Physical(DockEnd(from, width), DockEnd(to, width));
            }

            // Draw the native truck using its stock map floor extent where it is present.
            var entrance = plan.Entrance;
            var outward = entrance.Outward;
            var stockPose = new Pose(entrance.Sill, Quaternion.LookRotation(Vector3.Cross(outward, Vector3.up), Vector3.up));
            float depth = plan.Planar == null ? 9f : RaidPlanarLayout.StockDepth;
            float halfWidth = plan.Planar == null ? 3.5f : RaidPlanarLayout.StockHalfWidth;
            if (startBounds.HasValue)
            {
                Bounds bounds = startBounds.Value;
                Vector3 offset = bounds.center - entrance.Sill;
                depth = Mathf.Max(depth, Vector3.Dot(offset, outward) + Vector3.Dot(bounds.extents, Abs(outward)));
                Vector3 lateral = stockPose.rotation * Vector3.forward;
                halfWidth = Mathf.Max(halfWidth,
                    Mathf.Abs(Vector3.Dot(offset, lateral)) + Vector3.Dot(bounds.extents, Abs(lateral)));
            }
            var stock = Rectangle("stock-start", stockPose, new Rect(0, -halfWidth, depth, halfWidth * 2f));
            stock.Revealed = true;
            floors.Insert(0, stock);
            Bind(stock, RaidPlanarLayout.Stock, RaidPlanarLayout.StockFloor);
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
            foreach (var room in preparedRooms ?? Array.Empty<RaidPlan.Room>())
            foreach (var (a, b) in room.Module.InternalPortals)
                PortalPair(DockEnd(new RaidPlan.End(room, room.Module.Docks.Single(dock => dock.Id == a))),
                    DockEnd(new RaidPlan.End(room, room.Module.Docks.Single(dock => dock.Id == b))));

            if (ring && ringEntrance.HasValue)
            {
                Matrix4x4 frame = ringWorldToPlan ?? Matrix4x4.identity;
                Quaternion rotation = frame.rotation;
                var branch = ring.Rooms.Select(room => Rectangle(room.name,
                    new Pose(frame.MultiplyPoint3x4(room.position), rotation * room.rotation), new Rect(-6, -6, 12, 12))).ToArray();
                floors.AddRange(branch);
                foreach (var view in branch) Bind(view, plan.RingRoom.Index, view.Floor.Id);
                var entry = RingRuntime.Marker(ring.Rooms[0], "Quota__South_Truck");
                Vector3 point = frame.MultiplyPoint3x4(entry.position);
                Vector3[] courseToRing = FoldCourse(plan.RingLink)?.Append(point).ToArray();
                Physical(DockEnd(ringEntrance.Value), new End(branch[0], branch[0].Project(point, out _)), courseToRing);

                string[] bridges = { "BridgeQuotaLoot1", "BridgeLoot1Loot2" };
                for (int i = 0; i < bridges.Length; i++)
                {
                    var bridge = ring.Ring.Find(bridges[i]);
                    Vector3[] course = BridgeCourse(bridge).Select(frame.MultiplyPoint3x4).ToArray();
                    Physical(new End(branch[i], branch[i].Project(course[0], out _)),
                        new End(branch[i + 1], branch[i + 1].Project(course[course.Length - 1], out _)), course);
                }

                End PortalEnd(PortalEnd portal)
                {
                    var floor = branch[Array.IndexOf(ring.Rooms, portal.Room)];
                    Vector3 point = frame.MultiplyPoint3x4(portal.Marker.position);
                    Vector3 right = frame.MultiplyVector(portal.Orientation * Vector3.right);
                    Vector2 across = floor.Project(point + right, out _) - floor.Project(point - right, out _);
                    return new End(floor, floor.Project(point, out _), width: portal.Width, interior: portal.Upper, across: across);
                }
                foreach (var portal in ring.Portals) PortalPair(PortalEnd(portal.A), PortalEnd(portal.B));
            }

            return new RaidMapPlan(floors, links, doors, stock, flat);
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
            FloorView stock, IReadOnlyDictionary<FloorView, RaidPlanarLayout.FlatPose> flat)
        {
            this.floors = floors;
            this.links = links;
            this.doors = doors;
            Stock = stock;
            if (flat.Count == floors.Count)
            {
                foreach (var (floor, pose) in flat) floor.Place(pose.Position, pose.Angle);
                FitCanvas();
                foreach (var link in links)
                {
                    link.Path = new[] { link.Start, link.Finish };
                    link.Straight = true;
                }
                return;
            }
            for (int pass = 0; pass < 3; pass++)
            for (int attempt = 0; attempt < 4; attempt++)
            {
                float gap = Gap + attempt * 2f;
                if (pass == 2)
                {
                    // Some folded layouts do not admit straight corridors under the bounded
                    // placement search. Keep their physical connections on the original map.
                    foreach (var link in links) link.Straight = false;
                    ArrangeWithRoutedPassages(gap);
                    MarkClearLinks(new HashSet<FloorView>(floors), new List<Link>(), requireStraightBridges: false);
                }
                else if (!Arrange(gap, attempt % 2 != 0, requireStraightBridges: pass == 0)) continue;

                FitCanvas();
                if (TryRouteLinks())
                {
                    MergeCollinearBridges();
                    return;
                }
                if (pass == 2)
                {
                    // Shared approaches can make a reserved straight strip block another
                    // real passage. Keep the connected map in that case.
                    foreach (var link in links) link.Straight = false;
                    if (TryRouteLinks(smooth: false))
                    {
                        MergeCollinearBridges();
                        return;
                    }
                }
            }
            throw new InvalidOperationException("No non-crossing physical route on the flat raid map.");

            void FitCanvas()
            {
                Rect extent = Enclose(floors.SelectMany(f => f.Outline));
                if (extent.height > extent.width)
                {
                    foreach (var floor in floors) floor.Place(Turn(floor.Position, -90f), floor.Angle - 90f);
                    extent = Enclose(floors.SelectMany(f => f.Outline));
                }
                scale = Mathf.Min((Width - 2 * Margin) / extent.width, (Height - 2 * Margin) / extent.height);
                origin = new Vector2(Width * .5f, Height * .5f) - extent.center * scale;
            }
        }

        private void MergeCollinearBridges()
        {
            foreach (var bridge in links.Where(link => link.Bridge && !link.Straight))
            {
                Vector2 span = bridge.Finish - bridge.Start;
                float length = span.magnitude;
                if (length < .001f) continue;
                Vector2 direction = span / length;
                Vector2 normal = new(-direction.y, direction.x);
                float previous = 0f;
                bool collinear = true;
                foreach (var point in bridge.Path)
                {
                    Vector2 offset = point - bridge.Start;
                    float distance = Vector2.Dot(offset, direction);
                    if (Mathf.Abs(Vector2.Dot(offset, normal)) > .001f ||
                        distance < previous - .001f || distance > length + .001f)
                    {
                        collinear = false;
                        break;
                    }
                    previous = distance;
                }
                if (!collinear) continue;
                bridge.Path = new[] { bridge.Start, bridge.Finish };
                bridge.Straight = true;
            }
        }

        private bool TryRouteLinks(bool smooth = true) => RouteLinks(reverse: false, clearance: 2, smooth) ||
            RouteLinks(reverse: true, clearance: 2, smooth) || RouteLinks(reverse: false, clearance: 1, smooth);

        private bool RouteLinks(bool reverse, int clearance, bool smooth)
        {
            bool[] blocked = RasterizeFootprints();
            foreach (var link in links.Where(link => link.Straight))
            {
                Vector2Int from = Pixel(link.Start), to = Pixel(link.Finish);
                link.Path = new[] { link.Start, link.Finish };
                int padding = Mathf.Max(clearance, Mathf.CeilToInt(CorridorWidth * .5f * scale));
                ReserveSegment(blocked, from, to, padding);
            }
            var routes = links.Where(link => !link.Straight)
                .Select(link => (Link: link, From: Pixel(link.A.Outside), To: Pixel(link.B.Outside)))
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
                Vector2Int[] path = Trace(from, to, clear);
                if (path == null) return false;
                var simplified = smooth ? Simplify(path, clear) : StraightRuns(path);
                link.Path = new[] { link.Start, link.A.Outside }
                    .Concat(simplified.Skip(1).Take(simplified.Count - 2).Select(Unpixel))
                    .Concat(new[] { link.B.Outside, link.Finish }).ToArray();
                if (smooth)
                {
                    Reserve(blocked, simplified[0], clearance);
                    for (int point = 1; point < simplified.Count; point++)
                        ReserveSegment(blocked, simplified[point - 1], simplified[point], clearance);
                }
                else
                {
                    foreach (var point in path) Reserve(blocked, point, clearance);
                }
            }
            return true;
        }

        private static List<Vector2Int> StraightRuns(Vector2Int[] path)
        {
            var turns = new List<Vector2Int> { path[0] };
            for (int i = 2; i < path.Length; i++)
                if (path[i] - path[i - 1] != path[i - 1] - path[i - 2]) turns.Add(path[i - 1]);
            turns.Add(path[path.Length - 1]);
            return turns;
        }

        private static List<Vector2Int> Simplify(Vector2Int[] path, bool[] blocked)
        {
            var simplified = new List<Vector2Int> { path[0] };
            for (int from = 0; from < path.Length - 1;)
            {
                int next = from + 1;
                // Preserve navigable bends but remove artificial runs of one-pixel corners.
                for (int end = Mathf.Min(path.Length - 1, from + 48); end > next; end--)
                {
                    if (!SegmentPixels(path[from], path[end]).All(point => !blocked[point.y * Width + point.x])) continue;
                    next = end;
                    break;
                }
                simplified.Add(path[next]);
                from = next;
            }
            return simplified;
        }

        private static void ReserveSegment(bool[] blocked, Vector2Int from, Vector2Int to, int clearance)
        {
            foreach (var point in SegmentPixels(from, to)) Reserve(blocked, point, clearance);
        }

        private static IEnumerable<Vector2Int> SegmentPixels(Vector2Int from, Vector2Int to)
        {
            Vector2Int previous = from;
            yield return from;
            int steps = Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y));
            for (int step = 1; step <= steps; step++)
            {
                Vector2Int point = Vector2Int.RoundToInt(Vector2.Lerp(from, to, step / (float)steps));
                if (point.x != previous.x && point.y != previous.y)
                {
                    yield return new Vector2Int(previous.x, point.y);
                    yield return new Vector2Int(point.x, previous.y);
                }
                yield return point;
                previous = point;
            }
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

        private bool Arrange(float gap, bool reverse, bool requireStraightBridges)
        {
            var placed = new HashSet<FloorView> { Stock };
            var corridors = new List<Link>();
            foreach (var link in links) link.Straight = false;
            Stock.Place(Vector2.zero, 180f);
            while (placed.Count != floors.Count)
            {
                var edges = links.Where(link => placed.Contains(link.A.Floor) != placed.Contains(link.B.Floor))
                    .OrderByDescending(link => link.Bridge).ToArray();
                if (edges.Length == 0)
                {
                    var detached = floors.First(floor => !placed.Contains(floor));
                    detached.Place(new Vector2(placed.Max(f => f.Bounds.xMax) + gap, 0f) - Enclose(detached.Local).min, 0f);
                    placed.Add(detached);
                    continue;
                }
                if (edges.Any(edge => Place(edge, edge.Bridge))) continue;
                if (!requireStraightBridges && edges.Any(edge => Place(edge, false))) continue;
                return false;
            }
            return MarkClearLinks(placed, corridors, requireStraightBridges);

            bool Place(Link edge, bool straight)
            {
                bool fromA = placed.Contains(edge.A.Floor);
                End from = fromA ? edge.A : edge.B;
                End to = fromA ? edge.B : edge.A;
                Vector2 direction = Turn(from.Outward, from.Floor.Angle);
                Vector2 lateral = new Vector2(-direction.y, direction.x);
                float angle = Vector2.SignedAngle(to.Outward, -direction);
                Vector2 start = from.Floor.Flat(from.Edge);
                Vector2 anchor = start - Turn(to.Edge, angle);
                for (int lane = 0; lane < 24; lane++)
                {
                    float along = gap + lane * gap;
                    for (int slot = 0; slot <= lane * 2; slot++)
                    {
                        int side = (slot % 2 == 0) == reverse ? 1 : -1;
                        float shift = (slot + 1) / 2 * side * gap;
                        to.Floor.Place(anchor + direction * along + lateral * shift, angle);
                        Rect bounds = Padded(to.Floor.Bounds, gap * .7f);
                        if (placed.Any(floor => bounds.Overlaps(floor.Bounds))) continue;
                        if (corridors.Any(link => Meets(bounds, link.Start, link.Finish))) continue;
                        Vector2 finish = to.Floor.Flat(to.Edge);
                        if (straight && !Clear(start, finish, from.Floor, to.Floor, placed, corridors)) continue;
                        placed.Add(to.Floor);
                        if (straight)
                        {
                            edge.Straight = true;
                            corridors.Add(edge);
                        }
                        return true;
                    }
                }
                return false;
            }
        }

        private bool MarkClearLinks(HashSet<FloorView> placed, List<Link> corridors, bool requireStraightBridges)
        {
            foreach (var link in links.Where(link => !link.Straight).OrderByDescending(link => link.Bridge))
            {
                Vector2 span = (link.Finish - link.Start).normalized;
                if (Vector2.Dot(span, Turn(link.A.Outward, link.A.Floor.Angle)) <= 0f ||
                    Vector2.Dot(-span, Turn(link.B.Outward, link.B.Floor.Angle)) <= 0f ||
                    !Clear(link.Start, link.Finish, link.A.Floor, link.B.Floor, placed, corridors))
                {
                    if (requireStraightBridges && link.Bridge) return false;
                    continue;
                }
                link.Straight = true;
                corridors.Add(link);
            }
            return true;
        }

        private static bool Clear(Vector2 from, Vector2 to, FloorView a, FloorView b,
            IEnumerable<FloorView> placed, IEnumerable<Link> corridors)
        {
            const float padding = CorridorWidth * .5f + .1f;
            if (placed.Any(floor => floor != a && floor != b && Meets(Padded(floor.Bounds, padding), from, to)))
                return false;
            return !corridors.Any(other =>
                SegmentDistance(from, to, other.Start, other.Finish) < CorridorWidth + .2f);
        }

        private static Rect Padded(Rect rect, float distance) =>
            Rect.MinMaxRect(rect.xMin - distance, rect.yMin - distance, rect.xMax + distance, rect.yMax + distance);

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

        private static float SegmentDistance(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float abC = Cross(b - a, c - a), abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c), cdB = Cross(d - c, b - c);
            if (abC * abD < 0f && cdA * cdB < 0f) return 0f;
            return Mathf.Min(Distance(a, c, d), Distance(b, c, d), Distance(c, a, b), Distance(d, a, b));

            static float Cross(Vector2 x, Vector2 y) => x.x * y.y - x.y * y.x;
            static float Distance(Vector2 point, Vector2 start, Vector2 end)
            {
                Vector2 axis = end - start;
                if (axis.sqrMagnitude < .0001f) return Vector2.Distance(point, start);
                return Vector2.Distance(point, start + axis * Mathf.Clamp01(Vector2.Dot(point - start, axis) / axis.sqrMagnitude));
            }
        }

        private void ArrangeWithRoutedPassages(float gap)
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
                    if (link.Transition == null || !link.TryProjectTransition(position, out float progress, out Vector3 tangent)) continue;
                    floor = progress < .5f ? link.A.Floor : link.B.Floor;
                    if (link.Straight)
                    {
                        point = Vector2.Lerp(link.Start, link.Finish, progress);
                        heading = link.Finish - link.Start;
                    }
                    else
                    {
                        ProjectAlong(link.Path, progress, out point, out heading);
                    }
                    if (Vector3.Dot(forward, tangent) < 0f) heading = -heading;
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

        internal IEnumerable<Vector2> Corridor(Link link) => link.Path;

        private static void ProjectAlong(Vector2[] path, float progress, out Vector2 point, out Vector2 heading)
        {
            float length = 0f;
            for (int i = 1; i < path.Length; i++) length += Vector2.Distance(path[i - 1], path[i]);
            float distance = progress * length;
            for (int i = 1; i < path.Length; i++)
            {
                Vector2 segment = path[i] - path[i - 1];
                float span = segment.magnitude;
                if (span < .001f) continue;
                if (distance <= span)
                {
                    point = path[i - 1] + segment * (distance / span);
                    heading = segment;
                    return;
                }
                distance -= span;
            }
            point = path[path.Length - 1];
            heading = point - path[path.Length - 2];
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

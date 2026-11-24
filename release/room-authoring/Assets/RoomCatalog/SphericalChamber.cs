using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class SphericalChamber
    {
        public const string Id = "spherical-chamber";
        public const string Shell = "spherical-shell";
        public const string Left = "left-rise";
        public const string Right = "right-descent";
        public const string UpperExit = "upper-exit";
        public const string LowerExit = "lower-exit";
        public const string DiameterRailings = "diameter-railings";
        public const int WestMask = 1;
        public const int EastMask = 2;
        public const int UpperMask = 4;
        public const int LowerMask = 8;
        public const int FullMask = WestMask | EastMask | UpperMask | LowerMask;
        public static IReadOnlyList<int> VariantMasks { get; } = Array.AsReadOnly(new[]
        {
            FullMask, WestMask, WestMask | EastMask, WestMask | UpperMask,
            WestMask | EastMask | UpperMask, WestMask | LowerMask,
            WestMask | EastMask | LowerMask, WestMask | UpperMask | LowerMask,
        });
        public const float Scale = 1.5f;
        public const float Radius = 11.25f * Scale;
        public const float Diameter = 2 * Radius;
        public const float HalfWidth = Wide / 2;
        public const float EastCut = HalfWidth;
        public const float HalfReservation = 15 * Scale;
        public const float ServiceBayNear = 5 * Scale;
        public const float ServiceBayFar = 8 * Scale;
        public const float ServiceBayCenter = (ServiceBayNear + ServiceBayFar) / 2;
        public const float ServiceBayEdge = 4.1f;
        public const int BranchPhaseCount = 4;
        private const float LaunchRadius = 3 * Scale;
        private const float JunctionRadius = 1.25f * Scale;
        private const float ThresholdRunout = 1.5f;
        private const float LandingStart = 9.5f * Scale;
        private const float RecoveryLength = LandingStart - LaunchRadius;
        private const float Reach = HalfWidth + LaunchRadius;
        private const float RecoveryRadius = (RecoveryLength * RecoveryLength + Reach * Reach) / (4 * Reach);
        private static readonly float RecoveryTurn = 2 * MathF.Atan2(Reach, RecoveryLength);
        private const int EdgeSteps = 16;
        private const float CollarBottom = -.30f;
        private const float RailHeight = 1.06f;
        private const float RailInset = .06f;
        private const float FootprintTolerance = .00002f;

        public static string VariantId(int mask) => mask switch
        {
            FullMask => Id,
            WestMask => Id + "-west",
            WestMask | EastMask => Id + "-west-east",
            WestMask | UpperMask => Id + "-west-upper",
            WestMask | EastMask | UpperMask => Id + "-west-east-upper",
            WestMask | LowerMask => Id + "-west-lower",
            WestMask | EastMask | LowerMask => Id + "-west-east-lower",
            WestMask | UpperMask | LowerMask => Id + "-west-upper-lower",
            _ => throw new ArgumentOutOfRangeException(nameof(mask)),
        };

        public static int GetMask(string id) => id switch
        {
            Id => FullMask,
            Id + "-west" => WestMask,
            Id + "-west-east" => WestMask | EastMask,
            Id + "-west-upper" => WestMask | UpperMask,
            Id + "-west-east-upper" => WestMask | EastMask | UpperMask,
            Id + "-west-lower" => WestMask | LowerMask,
            Id + "-west-east-lower" => WestMask | EastMask | LowerMask,
            Id + "-west-upper-lower" => WestMask | UpperMask | LowerMask,
            _ => 0,
        };

        public static bool IsVariant(string id) => GetMask(id) != 0;

        public static GrayModule Create() => Create(FullMask);

        public static GrayModule Create(int mask)
        {
            bool eastOpen = (mask & EastMask) != 0;
            bool upperOpen = (mask & UpperMask) != 0;
            bool lowerOpen = (mask & LowerMask) != 0;
            var chamber = new GrayBuilder(VariantId(mask), 2 * HalfReservation, 2 * HalfReservation, difficulty: 2);
            chamber.Module.LayoutReservation = Block.Box("spherical-chamber-reservation", BlockRole.Free, Level,
                -HalfReservation, HalfReservation, -HalfReservation, HalfReservation, -HalfReservation, HalfReservation);
            float deckEnd = eastOpen ? HalfReservation : EastCut;
            chamber.Deck("diameter-deck", -HalfReservation, deckEnd, 0, -HalfWidth, HalfWidth);
            chamber.Deck("left-service-bay", -ServiceBayFar, -ServiceBayNear, 0, HalfWidth, ServiceBayEdge);
            var ground = new List<Vector2> { new(-HalfReservation, -HalfWidth) };
            if (eastOpen)
            {
                chamber.Deck("right-service-bay", ServiceBayNear, ServiceBayFar, 0, -ServiceBayEdge, -HalfWidth);
                ground.AddRange(new Vector2[] { new(ServiceBayNear, -HalfWidth), new(ServiceBayNear, -ServiceBayEdge),
                    new(ServiceBayFar, -ServiceBayEdge), new(ServiceBayFar, -HalfWidth) });
            }
            ground.AddRange(new Vector2[] { new(deckEnd, -HalfWidth), new(deckEnd, HalfWidth),
                new(-ServiceBayNear, HalfWidth), new(-ServiceBayNear, ServiceBayEdge), new(-ServiceBayFar, ServiceBayEdge),
                new(-ServiceBayFar, HalfWidth), new(-HalfReservation, HalfWidth) });
            chamber.MapFloor("ground", Level, ground.ToArray());

            Dock west = chamber.Door("west", "ground", Pose(-HalfReservation, 0, 0, -Vector3.UnitX), Cargo.Hand);
            if (eastOpen)
            {
                Dock east = chamber.Door("east", "ground", Pose(HalfReservation, 0, 0), Cargo.Hand,
                    DockUse.Physical | DockUse.DoorPortal);
                chamber.Route("diameter-through", west.Id, east.Id, Cargo.Hand,
                    Pose(-HalfReservation - ThresholdRunout, 0, 0), Pose(HalfReservation + ThresholdRunout, 0, 0));
            }
            if (upperOpen) AddBranch(chamber, 1, Left, UpperExit);
            if (lowerOpen) AddBranch(chamber, -1, Right, LowerExit);
            if (mask == WestMask)
            {
                const string junction = "diameter-junction";
                chamber.Module.Landmarks.Add(junction, Vector3.Zero);
                chamber.Route("diameter-terminal", west.Id, junction, Cargo.Hand,
                    Pose(-HalfReservation - ThresholdRunout, 0, 0), Level);
            }

            chamber.Module.Geometry.Add(CreateShell(chamber.Module.Docks));
            foreach (Dock dock in chamber.Module.Docks)
            {
                chamber.Seal(dock);
                chamber.Module.Geometry.Add(CreateThresholdCollar(dock));
            }
            chamber.Module.Geometry.Add(CreateDiameterRailings(eastOpen, upperOpen, lowerOpen));
            return chamber.Module;
        }

        // Round trips through the room transform can move a lateral edge a few
        // micrometers. Keep the longitudinal sills at their exact bind planes.
        public static bool IsStraightDeckFootprint(Vector3 point, int mask = FullMask) =>
            point.X >= -HalfReservation && point.X <= ((mask & EastMask) != 0 ? HalfReservation : EastCut) &&
            (MathF.Abs(point.Z) <= HalfWidth + FootprintTolerance ||
            point.X >= -ServiceBayFar - FootprintTolerance && point.X <= -ServiceBayNear + FootprintTolerance &&
                point.Z >= HalfWidth && point.Z <= ServiceBayEdge + FootprintTolerance ||
            point.X >= ServiceBayNear - FootprintTolerance && point.X <= ServiceBayFar + FootprintTolerance &&
                point.Z >= -ServiceBayEdge - FootprintTolerance && point.Z <= -HalfWidth);

        public static bool TryPlayerUp(Vector3 feet, Vector3 priorUp, out Vector3 up) =>
            SphericalGravityField.TryUp(feet, priorUp, out up, out _);

        public static int BranchSteps(int phase) => phase switch
        {
            0 => 48,
            1 or 2 => 42,
            _ => 18,
        };

        // The quarter bend leads into two reverse-curvature arcs and reaches a
        // vertical tangent on the centerline before the shell penetration.
        public static ModulePose BranchFrame(int side, int phase, float t, float lateral)
        {
            float y, z, forwardY, forwardZ;
            switch (phase)
            {
                case 0:
                    float launchAngle = t * MathF.PI / 2;
                    forwardY = t == 1 ? 1 : MathF.Sin(launchAngle);
                    forwardZ = t == 1 ? 0 : MathF.Cos(launchAngle);
                    y = LaunchRadius * (1 - forwardZ);
                    z = HalfWidth + LaunchRadius * forwardY;
                    break;
                case 1:
                    float inwardAngle = t * RecoveryTurn;
                    forwardY = MathF.Cos(inwardAngle);
                    forwardZ = -MathF.Sin(inwardAngle);
                    y = LaunchRadius - RecoveryRadius * forwardZ;
                    z = Reach - RecoveryRadius * (1 - forwardY);
                    break;
                case 2:
                    float outwardAngle = (1 - t) * RecoveryTurn;
                    forwardY = MathF.Cos(outwardAngle);
                    forwardZ = -MathF.Sin(outwardAngle);
                    y = LandingStart + RecoveryRadius * forwardZ;
                    z = RecoveryRadius * (1 - forwardY);
                    break;
                default:
                    y = LandingStart + (HalfReservation - LandingStart) * t;
                    z = 0;
                    forwardY = 1;
                    forwardZ = 0;
                    break;
            }
            return new ModulePose(new Vector3(lateral, side * y, side * z),
                new Vector3(0, side * forwardY, side * forwardZ), new Vector3(0, forwardZ, -forwardY));
        }

        private static void AddBranch(GrayBuilder chamber, int side, string id, string exitId)
        {
            const int landingPhase = BranchPhaseCount - 1;
            ModulePose landing = BranchFrame(side, landingPhase, 0, 0);
            ModulePose threshold = BranchFrame(side, landingPhase, 1, 0);
            string floorId = id + "-landing";
            float lead = Vector3.Distance(landing.Position, threshold.Position);
            chamber.MapFloor(floorId, landing, new(0, -HalfWidth), new(lead, -HalfWidth),
                new(lead, HalfWidth), new(0, HalfWidth));
            chamber.Door(exitId, floorId, threshold, Cargo.Hand, mapPoint: new Vector2(lead, 0));
            chamber.Module.Geometry.Add(CreateBranchDeck(id, side));
            chamber.Module.Geometry.Add(Block.Span(id + "-sill-deck", BlockRole.Floor, landing, threshold,
                -Skirt, 0, -HalfWidth, HalfWidth));
            chamber.Module.Geometry.Add(CreateBranchRailings(id, side));
            chamber.Module.Landmarks.Add(id + "-entry", BranchFrame(side, 0, 0, 0).Position);
            chamber.Module.Landmarks.Add(id + "-vertical", BranchFrame(side, 0, 1, 0).Position);
            chamber.Module.Landmarks.Add(id + "-landing", landing.Position);

            var route = new List<ModulePose> { Pose(-HalfReservation - ThresholdRunout, 0, 0), Pose(-JunctionRadius, 0, 0) };
            const int junctionSteps = 12;
            for (int i = 1; i <= junctionSteps; i++)
            {
                float angle = MathF.PI * i / (2 * junctionSteps);
                float sin = i == junctionSteps ? 1 : MathF.Sin(angle);
                float cos = i == junctionSteps ? 0 : MathF.Cos(angle);
                route.Add(Pose(JunctionRadius * (sin - 1), 0, side * JunctionRadius * (1 - cos),
                    new Vector3(cos, 0, side * sin)));
            }
            route.AddRange(BranchRows(side, 0, BranchPhaseCount, density: 2));
            route.Add(new ModulePose(threshold.Point(ThresholdRunout, 0), threshold.Forward, threshold.Up));
            chamber.Route(id + "-through", "west", exitId, Cargo.Hand, route.ToArray());
        }

        private static IEnumerable<ModulePose> BranchRows(int side, int firstPhase, int endPhase, int density = 1)
        {
            for (int phase = firstPhase; phase < endPhase; phase++)
            {
                int steps = BranchSteps(phase) / density;
                for (int step = phase == firstPhase ? 0 : 1; step <= steps; step++)
                    yield return BranchFrame(side, phase, step / (float)steps, 0);
            }
        }

        private static Block CreateBranchDeck(string id, int side)
        {
            var mesh = new CurvedRoomMesh();
            ModulePose? previous = null;
            foreach (ModulePose row in BranchRows(side, 0, BranchPhaseCount - 1))
            {
                if (previous is { } start)
                    AddSpan(mesh, start, row, -Skirt, 0, -HalfWidth, HalfWidth, capEnds: false);
                previous = row;
            }
            return mesh.Build(id, BlockRole.Floor);
        }

        private static Block CreateDiameterRailings(bool eastOpen, bool upperOpen, bool lowerOpen)
        {
            var mesh = new CurvedRoomMesh();
            const float lip = HalfWidth - RailInset;
            const float bayEdge = ServiceBayEdge - RailInset;
            foreach (int side in new[] { 1, -1 })
            {
                float bayStart = side > 0 ? -ServiceBayFar : ServiceBayNear;
                float bayEnd = side > 0 ? -ServiceBayNear : ServiceBayFar;
                Vector2[] before = side > 0
                    ? new[] { new Vector2(-HalfReservation, lip), new(bayStart + RailInset, lip),
                        new(bayStart + RailInset, bayEdge), new(bayEnd - RailInset, bayEdge),
                        new(bayEnd - RailInset, lip), new(-HalfWidth, lip) }
                    : new[] { new Vector2(-HalfReservation, lip), new(-HalfWidth, lip) };
                Vector2[] after = side < 0
                    ? new[] { new Vector2(HalfWidth, lip), new(bayStart + RailInset, lip),
                        new(bayStart + RailInset, bayEdge), new(bayEnd - RailInset, bayEdge),
                        new(bayEnd - RailInset, lip), new(HalfReservation, lip) }
                    : new[] { new Vector2(HalfWidth, lip), new(HalfReservation, lip) };
                AddEdge(side, before);
                if (!(side > 0 ? upperOpen : lowerOpen))
                {
                    Vector2 end = new(eastOpen ? HalfWidth : lip, lip);
                    AddEdge(side, new[] { before[before.Length - 1], end });
                }
                if (eastOpen) AddEdge(side, after);
            }
            if (!eastOpen) AddEdge(1, new[] { new Vector2(lip, -lip), new Vector2(lip, lip) });
            return mesh.Build(DiameterRailings, BlockRole.Obstacle);

            void AddEdge(int side, IReadOnlyList<Vector2> edge)
            {
                for (int segment = 1; segment < edge.Count; segment++)
                {
                    Vector2 a = edge[segment - 1], b = edge[segment];
                    Vector3 from = new(a.X, 0, side * a.Y);
                    Vector3 to = new(b.X, 0, side * b.Y);
                    Vector3 direction = Vector3.Normalize(to - from);
                    AddRail(mesh, new[] { new ModulePose(from, direction, Vector3.UnitY),
                        new ModulePose(to, direction, Vector3.UnitY) });
                }
            }
        }

        private static Block CreateBranchRailings(string id, int side)
        {
            var mesh = new CurvedRoomMesh();
            foreach (int edge in new[] { -1, 1 })
            {
                var frames = new List<ModulePose>();
                foreach (var row in BranchRows(side, 0, BranchPhaseCount))
                    frames.Add(new ModulePose(row.Position + edge * (HalfWidth - RailInset) * Vector3.UnitX,
                        row.Forward, row.Up));
                AddRail(mesh, frames);
            }
            return mesh.Build(id + "-railings", BlockRole.Obstacle);
        }

        private static void AddRail(CurvedRoomMesh mesh, IReadOnlyList<ModulePose> frames)
        {
            const float beamHalfWidth = .035f;
            const float postHalf = .042f;
            float distanceToPost = 0;
            for (int i = 1; i < frames.Count; i++)
            {
                ModulePose a = frames[i - 1], b = frames[i];
                AddSpan(mesh, a, b, RailHeight - .07f, RailHeight, -beamHalfWidth, beamHalfWidth,
                    capEnds: false);
                AddSpan(mesh, a, b, .48f, .53f, -beamHalfWidth, beamHalfWidth, capEnds: false);
                float length = Vector3.Distance(a.Position, b.Position);
                while (distanceToPost <= length - postHalf)
                {
                    float location = MathF.Max(postHalf, distanceToPost);
                    float t = location / length;
                    Vector3 forward = Vector3.Normalize(Vector3.Lerp(a.Forward, b.Forward, t));
                    Vector3 up = Vector3.Normalize(Vector3.Lerp(a.Up, b.Up, t));
                    Post(new ModulePose(Vector3.Lerp(a.Position, b.Position, t), forward, up));
                    distanceToPost = location + 1.35f;
                }
                distanceToPost -= length;
            }
            ModulePose end = frames[frames.Count - 1];
            Post(new ModulePose(end.Point(-postHalf, 0), end.Forward, end.Up));

            void Post(ModulePose row)
            {
                var start = new ModulePose(row.Point(-postHalf, 0), row.Forward, row.Up);
                var finish = new ModulePose(row.Point(postHalf, 0), row.Forward, row.Up);
                AddSpan(mesh, start, finish, -.02f, RailHeight, -postHalf, postHalf, capEnds: true);
            }
        }

        private static void AddSpan(CurvedRoomMesh mesh, ModulePose a, ModulePose b, float low, float high,
            float left, float right, bool capEnds)
        {
            int al = mesh.Add(a.Point(0, low, left)), ar = mesh.Add(a.Point(0, low, right));
            int bl = mesh.Add(b.Point(0, low, left)), br = mesh.Add(b.Point(0, low, right));
            int at = mesh.Add(a.Point(0, high, left)), au = mesh.Add(a.Point(0, high, right));
            int bt = mesh.Add(b.Point(0, high, left)), bu = mesh.Add(b.Point(0, high, right));
            mesh.Quad(al, ar, br, bl, -a.Up - b.Up);
            mesh.Quad(at, au, bu, bt, a.Up + b.Up);
            mesh.Quad(al, bl, bt, at, -a.Right - b.Right);
            mesh.Quad(ar, br, bu, au, a.Right + b.Right);
            if (capEnds)
            {
                mesh.Quad(al, ar, au, at, -a.Forward);
                mesh.Quad(bl, br, bu, bt, b.Forward);
            }
        }

        private static Vector2 SquarePerimeter(int index)
        {
            float t = 2 * (index % EdgeSteps) / (float)EdgeSteps - 1;
            return (index / EdgeSteps) switch
            {
                0 => new Vector2(t, -1),
                1 => new Vector2(1, t),
                2 => new Vector2(-t, 1),
                _ => new Vector2(-1, -t),
            };
        }

        private static Vector2 Aperture(int index)
        {
            Vector2 p = SquarePerimeter(index);
            return new Vector2(p.X * HalfWidth, CollarBottom + (p.Y + 1) * (Height - CollarBottom) / 2);
        }

        private static Vector3 ShellPoint(ModulePose face, Vector2 p)
        {
            float axial = MathF.Sqrt(Radius * Radius - p.LengthSquared());
            return face.Forward * axial + p.Y * face.Up + p.X * face.Right;
        }

        private static Block CreateThresholdCollar(Dock dock)
        {
            var mesh = new CurvedRoomMesh();
            ModulePose frame = dock.Threshold;
            Vector3 center = frame.Up * ((Height + CollarBottom) / 2);
            for (int i = EdgeSteps; i < 4 * EdgeSteps; i++)
            {
                Vector2 a = Aperture(i), b = Aperture((i + 1) % (4 * EdgeSteps));
                Vector3 normal = center - frame.Up * ((a.Y + b.Y) / 2) - frame.Right * ((a.X + b.X) / 2);
                mesh.Quad(mesh.Add(ShellPoint(frame, a)), mesh.Add(ShellPoint(frame, b)),
                    mesh.Add(frame.Point(0, b.Y, b.X)), mesh.Add(frame.Point(0, a.Y, a.X)), normal);
            }
            return mesh.Build(dock.Id + "-collar", BlockRole.Shell);
        }

        private static Block CreateShell(IReadOnlyList<Dock> docks)
        {
            var mesh = new CurvedRoomMesh();
            foreach (Dock dock in docks)
                AddShellPanel(mesh, dock.Threshold, hasAperture: true);
            foreach (Vector3 direction in new[] { Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY })
            {
                if (docks.Any(dock => dock.Threshold.Forward == direction)) continue;
                Vector3 up = direction == Vector3.UnitX ? Vector3.UnitY : -Vector3.UnitZ;
                AddShellPanel(mesh, new ModulePose(Vector3.Zero, direction, up), hasAperture: false);
            }
            AddShellPanel(mesh, new ModulePose(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY), hasAperture: false);
            AddShellPanel(mesh, new ModulePose(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY), hasAperture: false);
            return mesh.Build(Shell, BlockRole.Shell);
        }

        private static void AddShellPanel(CurvedRoomMesh mesh, ModulePose face, bool hasAperture)
        {
            // Project six cube faces onto the sphere. Their common edges share
            // the same sample spacing; the inner rings meet the exact collars.
            const int radialSteps = 20, ringSize = 4 * EdgeSteps;
            int[] previous = null;
            for (int row = 0; row <= radialSteps; row++)
            {
                var current = new int[ringSize];
                var points = new Vector3[ringSize];
                for (int i = 0; i < ringSize; i++)
                {
                    Vector2 p = SquarePerimeter(i);
                    Vector2 border = p * (Radius / MathF.Sqrt(1 + p.LengthSquared()));
                    Vector2 inner = hasAperture ? Aperture(i) : Vector2.Zero;
                    points[i] = ShellPoint(face, Vector2.Lerp(border, inner, row / (float)radialSteps));
                    current[i] = mesh.Add(points[i]);
                }
                if (previous != null)
                {
                    for (int i = 0; i < ringSize; i++)
                    {
                        int j = (i + 1) % ringSize;
                        mesh.Quad(previous[i], previous[j], current[j], current[i], -(points[i] + points[j]));
                    }
                }
                previous = current;
            }
        }
    }
}

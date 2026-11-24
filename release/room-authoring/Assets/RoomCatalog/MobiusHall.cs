using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class MobiusHall
    {
        public const string Id = "mobius-hall";
        public const string Ribbon = "mobius-ribbon";
        public const string Landing = "ribbon-landing";
        public const string Ramp = "access-ramp";
        public const string CeilingRamp = "ceiling-access-ramp";
        public const string CeilingLanding = "ceiling-ribbon-landing";
        public const string CeilingRoute = "ground-to-ceiling";
        public const float Scale = 1.5f;
        public const string RailInner = "mobius-rail-inner";
        public const string RailOuter = "mobius-rail-outer";
        public const float RailHeight = 1.06f;
        public const float RailInset = .06f;
        public const float RailFlare = 0;
        public const float WallEquipmentSetback = 1.3f;
        public const float WorktableSetback = .45f;
        public const float MonitoringOffsetZ = 7.75f;
        public static readonly Vector3[] NativeBypassPads = {
            new Vector3(-10.9f*Scale, 0, -4.1f*Scale), new Vector3(10.9f*Scale, 0, -4.1f*Scale)
        };
        public const float Clearance = 3;
        public const float RibbonHeight = 6;
        public const float GravityReach = 1;
        public const float InteriorHeight = RibbonHeight + 2 * Clearance;
        public const float HalfWidth = Wide * Scale / 2;
        public const float RampFoot = -7.4f * Scale;
        public const float LandingHalfLength = 1.55f * Scale;
        public const int RibbonSections = 320;
        public const int WidthSections = 12;
        private const int RailSections = RibbonSections / 2;
        // Static workstation hulls; catalog geometry is the authoritative collision definition.
        public static readonly (string id, Vector3 center, Vector3 size)[] WorkstationHulls = {
            ("mobius-west-worktable", new Vector3(-4.5f*Scale,.48f,(-7.65f-WorktableSetback)*Scale), new Vector3(3.2f,.96f,.85f)),
            ("mobius-east-worktable", new Vector3(4.5f*Scale,.48f,(-7.65f-WorktableSetback)*Scale), new Vector3(3.2f,.96f,.85f)),
            ("mobius-monitoring", new Vector3(0,1.04f,-7.75f+MonitoringOffsetZ), new Vector3(2.45f,2.08f,.72f)),
            ("mobius-east-storage", new Vector3(11.05f*Scale,1.1f,(-5.05f-WallEquipmentSetback)*Scale), new Vector3(.65f,2.2f,1.5f)),
            ("mobius-west-diagnostics", new Vector3(-11.35f*Scale,1.15f,(-5.1f-WallEquipmentSetback)*Scale), new Vector3(.52f,2.3f,1.65f))
        };
        // Conservative registered collision envelopes for the freestanding lamp stands.
        public static readonly (string id, Vector3 center, Vector3 size)[] TripodHulls = {
            ("mobius-tripod-west", new Vector3(-9.4f*Scale,.75f,-3f*Scale), new Vector3(1f,1.5f,1f)),
            ("mobius-tripod-east", new Vector3(9.4f*Scale,.75f,-3f*Scale), new Vector3(1f,1.5f,1f)),
            ("mobius-tripod-north", new Vector3(9.2f*Scale,.75f,7.7f*Scale), new Vector3(1f,1.5f,1f))
        };
        private const float BlankThickness = .18f * Scale;
        private const float Tau = 2 * MathF.PI;
        private const float BendRadius = 4.6f * Scale;
        private const float HalfStraight = 3f * Scale;
        private const float RearRise = 3.8f * Scale;
        private const float BendLength = MathF.PI * BendRadius;
        private const float Perimeter = 4 * HalfStraight + 2 * BendLength;
        private static readonly Vector3[] BlankVertices = CreateBlankVertices();
        private static readonly float BlankBottom = BlankVertices.Min(p => p.Y);
        private static readonly float VerticalScale = RibbonHeight / (BlankVertices.Max(p => p.Y) - BlankBottom);
        private static readonly float BlankWest = BlankVertices.Min(p => p.X);
        private static readonly float BlankEast = BlankVertices.Max(p => p.X);
        private static readonly float BlankCenterX = (BlankEast + BlankWest) / 2;
        private static readonly float BlankNear = BlankVertices.Min(p => p.Z);
        private static readonly float BlankFar = BlankVertices.Max(p => p.Z);
        private static readonly float BlankCenterZ = (BlankFar + BlankNear) / 2;
        private static readonly float HorizontalScaleX = 29.49012375f / (BlankEast - BlankWest);
        private static readonly float HorizontalScaleZ = 19.91574478f / (BlankFar - BlankNear);
        private static readonly Vector3[] Vertices = BlankVertices.Select(Fit).ToArray();
        private static readonly int[] Triangles = CreateRibbonTriangles();
        public static float HalfInteriorX { get; } = 29.49012375f / 2 + Clearance;
        public static float HalfInteriorZ { get; } = 19.91574478f / 2 + Clearance;
        public static float EntranceHeight { get; } = Surface(0, 0).Position.Y;
        public static float EntranceX { get; } = Surface(0, 0).Position.X;
        public static float LandingInnerEdge { get; } = Surface(0, HalfWidth).Position.Z;
        public static float RampInnerEdge => LandingInnerEdge + .10f;
        public static float CeilingEntranceHeight => Surface(.5f,0).Position.Y;
        public static float CeilingEntranceX => Surface(.5f,0).Position.X;
        public static float CeilingLandingInnerEdge => Surface(.5f,-HalfWidth).Position.Z;
        public static float CeilingRampInnerEdge => CeilingLandingInnerEdge - .10f;
        public static float CeilingRampOuterEdge => -HalfInteriorZ + .15f;
        public static float CeilingRampCenter => (CeilingRampInnerEdge + CeilingRampOuterEdge)/2;
        public static float CeilingRampFoot => -HalfInteriorX + 1.2f;
        public static float CeilingRampStart => CeilingEntranceX - LandingHalfLength;
        public static Vector3 RoomUpAt(Vector3 point) => point.Y >= InteriorHeight / 2 ? -Vector3.UnitY : Vector3.UnitY;
        public static float RampOuterEdge => HalfInteriorZ - .15f;
        public static float RampCenter => (RampInnerEdge + RampOuterEdge) / 2;

        public static GrayModule Create()
        {
            float outsideX = HalfInteriorX + Skirt;
            float outsideZ = HalfInteriorZ + Skirt;
            float approachDepth = Clearance + Skirt;
            var hall = new GrayBuilder(Id, outsideZ * 2, outsideX * 2, difficulty: 2);
            var west = hall.Door("west", "ground", Pose(-outsideX, 0, 0, -Vector3.UnitX), Cargo.Hand,
                depth: approachDepth);
            var east = hall.Door("east", "ground", Pose(outsideX, 0, 0), Cargo.Hand,
                DockUse.Physical | DockUse.DoorPortal, depth: approachDepth);
            hall.Floor("ground", -outsideX, outsideX, 0, -outsideZ, outsideZ);
            hall.Roof(-outsideX, outsideX, InteriorHeight, -outsideZ, outsideZ);
            hall.MapFloor("ceiling", new ModulePose(new Vector3(0,InteriorHeight,0),Vector3.UnitX,-Vector3.UnitY),
                new Vector2(-outsideX,-outsideZ),new Vector2(outsideX,-outsideZ),new Vector2(outsideX,outsideZ),new Vector2(-outsideX,outsideZ));
            var ceiling = hall.Door("ceiling-east", "ceiling", new ModulePose(new Vector3(outsideX,InteriorHeight,0),Vector3.UnitX,-Vector3.UnitY), Cargo.Hand, DockUse.DoorPortal, depth:approachDepth);
            hall.Wall("west-wall", Pose(-outsideX,0,0,-Vector3.UnitX),outsideZ,InteriorHeight,west);
            hall.Wall("east-wall", Pose(outsideX,0,0),outsideZ,InteriorHeight/2,east);
            hall.Wall("east-ceiling-wall", new ModulePose(new Vector3(outsideX,InteriorHeight,0),Vector3.UnitX,-Vector3.UnitY),outsideZ,InteriorHeight/2,ceiling);
            hall.Wall("near-wall",Pose(0,0,-outsideZ,-Vector3.UnitZ),outsideX-Skirt,InteriorHeight);
            hall.Wall("far-wall",Pose(0,0,outsideZ,Vector3.UnitZ),outsideX-Skirt,InteriorHeight);
            foreach (var (id, center, size) in WorkstationHulls)
                hall.Box(id, BlockRole.Fixture, center.X-size.X/2, center.X+size.X/2,
                    center.Y-size.Y/2, center.Y+size.Y/2, center.Z-size.Z/2, center.Z+size.Z/2);
            foreach (var (id, center, size) in TripodHulls)
                hall.Box(id, BlockRole.Fixture, center.X-size.X/2, center.X+size.X/2,
                    center.Y-size.Y/2, center.Y+size.Y/2, center.Z-size.Z/2, center.Z+size.Z/2);
            hall.Module.Geometry.Add(CreateRibbon());
            hall.Module.Geometry.Add(CreateRailings(1));
            hall.Module.Geometry.Add(CreateRailings(-1));
            float landingStart = EntranceX - LandingHalfLength;
            hall.Box(Landing,BlockRole.Floor,landingStart,EntranceX+LandingHalfLength,EntranceHeight-BlankThickness,EntranceHeight,LandingInnerEdge,RampOuterEdge);
            hall.Box(CeilingLanding,BlockRole.Floor,CeilingRampStart,CeilingEntranceX+LandingHalfLength,CeilingEntranceHeight,CeilingEntranceHeight+BlankThickness,CeilingRampOuterEdge,CeilingLandingInnerEdge);
            foreach (var block in AccessBlocks()) hall.Module.Geometry.Add(block);
            hall.Route(CeilingRoute, west.Id, ceiling.Id, Cargo.Hand, FloorToCeilingRoute());
            hall.Module.Landmarks.Add("ribbon-entry", Surface(0, 0).Position);
            hall.Module.Landmarks.Add("ribbon-approach", new Vector3(RampFoot, 0, RampCenter));

            // Both-face guards descend below the low ribbon. Keep the floor route
            // outside those low side edges, then cross beneath the raised rear arc.
            float bypassX = HalfInteriorX - 1.2f;
            hall.Route("ground-through", west.Id, east.Id, Cargo.Hand,
                Pose(-outsideX - 1.5f, 0, 0), Pose(-bypassX, 0, 0),
                Pose(-bypassX, 0, -1.85f*Scale, -Vector3.UnitZ), Pose(-8*Scale, 0, -1.85f*Scale),
                Pose(8*Scale, 0, -1.85f*Scale), Pose(bypassX, 0, -1.85f*Scale),
                Pose(bypassX, 0, 0, Vector3.UnitZ), Pose(outsideX + 1.5f, 0, 0));
            return hall.Module;
        }

        public static ModulePose AccessPose(bool ceiling, float t)
        {
            float x0=ceiling ? CeilingRampStart : RampFoot, x1=ceiling ? CeilingRampFoot : EntranceX-LandingHalfLength;
            float y0=ceiling ? CeilingEntranceHeight : 0, y1=ceiling ? InteriorHeight : EntranceHeight;
            float phase=t*t*(3-2*t), slope=(y1-y0)*6*t*(1-t)/(x1-x0);
            var forward=Vector3.Normalize(new Vector3(1,slope,0))*(x1>x0?1:-1);
            var up=Vector3.Normalize(new Vector3(-slope,1,0))*(ceiling?-1:1);
            return new ModulePose(new Vector3(x0+(x1-x0)*t,y0+(y1-y0)*phase,ceiling?CeilingRampCenter:RampCenter),forward,up);
        }
        public static IEnumerable<Block> AccessBlocks()
        {
            foreach(bool upper in new[]{false,true})
            for(int i=0;i<48;i++)
                yield return Block.Span($"{(upper?CeilingRamp:Ramp)}-{i+1:D2}",BlockRole.Floor,AccessPose(upper,i/48f),AccessPose(upper,(i+1)/48f),-BlankThickness,0,-(upper?CeilingRampInnerEdge-CeilingRampOuterEdge:RampOuterEdge-RampInnerEdge)/2,(upper?CeilingRampInnerEdge-CeilingRampOuterEdge:RampOuterEdge-RampInnerEdge)/2);
        }
        public static ModulePose[] FloorToCeilingRoute()
        {
            var path=new List<ModulePose>();
            path.Add(Pose(-HalfInteriorX-Skirt-1.5f,0,0));
            path.Add(Pose(-HalfInteriorX+1.2f,0,0));
            path.Add(Pose(-HalfInteriorX+1.2f,0,RampCenter));
            path.Add(Pose(RampFoot-1.5f,0,RampCenter));
            for(int i=0;i<=48;i++) path.Add(AccessPose(false,i/48f));
            path.Add(Pose(EntranceX,EntranceHeight,RampCenter));
            path.Add(new ModulePose(Surface(0,0).Position,-Vector3.UnitZ,Vector3.UnitY));
            for(int i=1;i<=RibbonSections/2;i++) path.Add(Surface(i/(float)RibbonSections,0));
            path.Add(new ModulePose(new Vector3(CeilingEntranceX,CeilingEntranceHeight,CeilingRampCenter),-Vector3.UnitZ,-Vector3.UnitY));
            for(int i=0;i<=48;i++) path.Add(AccessPose(true,i/48f));
            path.Add(new ModulePose(new Vector3(CeilingRampFoot,InteriorHeight,0),Vector3.UnitZ,-Vector3.UnitY));
            path.Add(new ModulePose(new Vector3(HalfInteriorX+Skirt+1.5f,InteriorHeight,0),Vector3.UnitX,-Vector3.UnitY));
            return path.ToArray();
        }
        // Two laps describe the oriented walking surface; the center surface closes after one
        // lap with its width reversed. The lower arc stays flat through the deck joint.
        public static ModulePose Surface(float turns, float across)
        {
            var blank = BlankSurface(turns, across, BlankThickness / 2);
            var forward = new Vector3(blank.Forward.X * HorizontalScaleX, blank.Forward.Y * VerticalScale, blank.Forward.Z * HorizontalScaleZ);
            return new ModulePose(Fit(blank.Position), Vector3.Normalize(forward), FitNormal(blank.Up));
        }

        private static Vector3 Fit(Vector3 point) =>
            new Vector3((point.X - BlankCenterX)*HorizontalScaleX, Clearance + (point.Y - BlankBottom) * VerticalScale, (point.Z - BlankCenterZ)*HorizontalScaleZ);

        private static Vector3 FitNormal(Vector3 normal) =>
            Vector3.Normalize(new Vector3(normal.X/HorizontalScaleX, normal.Y / VerticalScale, normal.Z/HorizontalScaleZ));

        private static ModulePose BlankSurface(float turns, float across, float offset)
        {
            float angle = turns * Tau;
            Vector3 position = Point(angle, across);
            Vector3 tangent = Vector3.Normalize(Point(angle + .001f, across) - Point(angle - .001f, across));
            Vector3 up = Vector3.Normalize(Vector3.Cross(Across(angle), tangent));
            return new ModulePose(position + offset * up, tangent, up);
        }

        public static Vector3 NormalAt(Vector3 point, Vector3 faceNormal) =>
            GravityField.Instance.TryProject(point, out var up, faceNormal) ? up : faceNormal;

        // Use the same outer triangles as the physical ribbon. The narrow rim is not a walking face.
        public static bool TryGravity(Vector3 roomLocalPoint, out Vector3 roomLocalUp) =>
            GravityField.Instance.TryProject(roomLocalPoint, out roomLocalUp);

        private static Vector3 Point(float angle, float across) => Center(angle).position + across * Across(angle);

        private static (Vector3 position, Vector3 outward) Center(float angle)
        {
            float turn = angle / Tau;
            float distance = (turn - MathF.Floor(turn)) * Perimeter;
            if (distance <= HalfStraight) return (new Vector3(distance, 0, BendRadius), Vector3.UnitZ);
            distance -= HalfStraight;
            if (distance <= BendLength)
            {
                float bend = distance / BendRadius;
                float sin = MathF.Sin(bend), cos = MathF.Cos(bend);
                return (new Vector3(HalfStraight + BendRadius * sin, RearRise * (1 - cos) / 2,
                    BendRadius * cos), new Vector3(sin, 0, cos));
            }
            distance -= BendLength;
            if (distance <= 2 * HalfStraight)
                return (new Vector3(HalfStraight - distance, RearRise, -BendRadius), -Vector3.UnitZ);
            distance -= 2 * HalfStraight;
            if (distance <= BendLength)
            {
                float bend = distance / BendRadius;
                float sin = MathF.Sin(bend), cos = MathF.Cos(bend);
                return (new Vector3(-HalfStraight - BendRadius * sin, RearRise * (1 + cos) / 2,
                    -BendRadius * cos), new Vector3(-sin, 0, -cos));
            }
            return (new Vector3(distance - BendLength - HalfStraight, 0, BendRadius), Vector3.UnitZ);
        }

        private static Vector3 Across(float angle)
        {
            float turn = angle / Tau;
            float lap = MathF.Floor(turn);
            // Spread the half-twist across the raised end. The lower seam stays level across its full width.
            float phase = Math.Clamp((turn - lap - .08f) / .34f, 0, 1);
            float twist = MathF.PI * (lap + phase * phase * (3 - 2 * phase));
            return MathF.Cos(twist) * Center(angle).outward + MathF.Sin(twist) * Vector3.UnitY;
        }

        private static Vector3[] CreateBlankVertices()
        {
            const int row = WidthSections + 1;
            var vertices = new Vector3[2 * RibbonSections * row];
            for (int i = 0; i < RibbonSections; i++)
            for (int j = 0; j <= WidthSections; j++)
            {
                float t = i / (float)RibbonSections;
                float width = HalfWidth * (2 * j / (float)WidthSections - 1);
                vertices[At(i, j, 0)] = BlankSurface(t, width, BlankThickness / 2).Position;
                vertices[At(i, j, 1)] = BlankSurface(t, width, -BlankThickness / 2).Position;
            }
            return vertices;
        }

        private static Block CreateRibbon() => Block.FromMesh(Ribbon, BlockRole.Obstacle,
            (Vector3[])Vertices.Clone(), (int[])Triangles.Clone());

        // An oriented boundary returns after TWO laps: after one lap it lies on the
        // other edge and opposite physical face. Do not duplicate a one-lap railing.
        private static Block CreateRailings(int edge)
        {
            const float postHalf = .042f;
            var mesh = new CurvedRoomMesh();
            var frames = new List<ModulePose>();

            // The floor apron at turn zero and ceiling apron at turn one-half are geographically opposite.
            // Each oriented edge opens only at its own physically connected landing.
            float gap = (LandingHalfLength + .06f) / (Perimeter*HorizontalScaleX);
            float start = (edge > 0 ? 0 : .5f) + gap;
            float finish = (edge > 0 ? 2 : 2.5f) - gap;
            frames.Add(Frame(start));
            for (int i = 1; i < 3 * RailSections; i++)
            {
                float turn = i / (float)RailSections;
                if (turn > start && turn < finish) frames.Add(Frame(turn));
            }
            bool closed = false;
            if (!closed) frames.Add(Frame(finish));
            Beam(.99f, RailHeight, .035f);
            Beam(.48f, .53f, .035f);

            float sincePost = 0;
            Post(frames[0]);
            for (int i = 1; i < frames.Count; i++)
            {
                sincePost += Vector3.Distance(frames[i - 1].Position, frames[i].Position);
                if (sincePost < 1.35f) continue;
                Post(frames[i]);
                sincePost = 0;
            }
            if (!closed) Post(frames[frames.Count - 1]);
            return mesh.Build(edge > 0 ? RailOuter : RailInner, BlockRole.Obstacle);

            ModulePose Frame(float turn)
            {
                ModulePose row = Surface(turn, edge * (HalfWidth - RailInset));
                return new ModulePose(row.Position, Vector3.Normalize(row.Forward), row.Up);
            }

            void Beam(float low, float high, float half)
            {
                var rows = frames.Select(row => new[] {
                    mesh.Add(RailPoint(row, 0, low, -half)), mesh.Add(RailPoint(row, 0, low, half)),
                    mesh.Add(RailPoint(row, 0, high, -half)), mesh.Add(RailPoint(row, 0, high, half)) }).ToArray();
                for (int i = 0; i < (closed ? rows.Length : rows.Length - 1); i++)
                {
                    int j = (i + 1) % rows.Length;
                    int[] a = rows[i], b = rows[j];
                    ModulePose from = frames[i], to = frames[j];
                    mesh.Quad(a[0], a[1], b[1], b[0], -from.Up - to.Up);
                    mesh.Quad(a[2], b[2], b[3], a[3], from.Up + to.Up);
                    mesh.Quad(a[0], b[0], b[2], a[2], -from.Right - to.Right);
                    mesh.Quad(a[1], a[3], b[3], b[1], from.Right + to.Right);
                }
                if (closed) return;
                Cap(rows[0], -frames[0].Forward);
                Cap(rows[rows.Length - 1], frames[frames.Count - 1].Forward);
            }

            void Post(ModulePose row)
            {
                // Post axes follow the exact local smooth surface normal throughout the twist.
                var rows = new[] { -.02f, .48f, RailHeight }.Select(height => new[] {
                    mesh.Add(RailPoint(row, -postHalf, height, -postHalf)), mesh.Add(RailPoint(row, -postHalf, height, postHalf)),
                    mesh.Add(RailPoint(row, postHalf, height, -postHalf)), mesh.Add(RailPoint(row, postHalf, height, postHalf)) }).ToArray();
                mesh.Quad(rows[0][0], rows[0][1], rows[0][3], rows[0][2], -row.Up);
                mesh.Quad(rows[2][0], rows[2][2], rows[2][3], rows[2][1], row.Up);
                for (int i = 0; i < 2; i++)
                {
                    int[] a = rows[i], b = rows[i + 1];
                    mesh.Quad(a[0], a[2], b[2], b[0], -row.Right);
                    mesh.Quad(a[1], b[1], b[3], a[3], row.Right);
                    mesh.Quad(a[0], b[0], b[1], a[1], -row.Forward);
                    mesh.Quad(a[2], a[3], b[3], b[2], row.Forward);
                }
            }

            Vector3 RailPoint(ModulePose row, float forward, float height, float lateral) =>
                row.Point(forward, height, lateral);

            void Cap(int[] row, Vector3 normal) => mesh.Quad(row[0], row[2], row[3], row[1], normal);
        }

        private static int[] CreateRibbonTriangles()
        {
            var triangles = new System.Collections.Generic.List<int>(12 * RibbonSections * (WidthSections + 1));
            for (int i = 0; i < RibbonSections; i++)
            for (int j = 0; j < WidthSections; j++)
            {
                Quad(At(i, j, 0), At(i, j + 1, 0), At(i + 1, j + 1, 0), At(i + 1, j, 0));
                Quad(At(i, j, 1), At(i + 1, j, 1), At(i + 1, j + 1, 1), At(i, j + 1, 1));
            }
            for (int i = 0; i < RibbonSections; i++)
            {
                Quad(At(i, 0, 0), At(i + 1, 0, 0), At(i + 1, 0, 1), At(i, 0, 1));
                Quad(At(i, WidthSections, 1), At(i + 1, WidthSections, 1),
                    At(i + 1, WidthSections, 0), At(i, WidthSections, 0));
            }
            return triangles.ToArray();

            void Quad(int a, int b, int c, int d)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(d);
            }
        }

        private static int At(int i, int j, int side) => i < RibbonSections ?
            2 * (i * (WidthSections + 1) + j) + side : 2 * (WidthSections - j) + 1 - side;

        private static class GravityField
        {
            internal static readonly MobiusGravityField Instance = new(Vertices, CreateSurfaceNormals(), CreateEdgeNormals(), Triangles,
                12 * RibbonSections * WidthSections, GravityReach);

            private static Vector3[] CreateSurfaceNormals()
            {
                var normals = new Vector3[Vertices.Length];
                for (int i = 0; i < RibbonSections; i++)
                for (int j = 0; j <= WidthSections; j++)
                {
                    float across = HalfWidth * (2 * j / (float)WidthSections - 1);
                    Vector3 up = Surface(i / (float)RibbonSections, across).Up;
                    normals[At(i, j, 0)] = up;
                    normals[At(i, j, 1)] = -up;
                }
                return normals;
            }

            private static Vector3[] CreateEdgeNormals()
            {
                var result = new Vector3[Vertices.Length];
                for (int section = 0; section < RibbonSections; section++)
                foreach (int edge in new[] { 0, WidthSections })
                for (int side = 0; side < 2; side++)
                {
                    int at = At(section, edge, side);
                    int inset = edge == 0 ? 1 : WidthSections - 1;
                    int prior = section > 0 ? At(section - 1, edge, side) : At(RibbonSections - 1, WidthSections - edge, 1 - side);
                    Vector3 tangent = Vector3.Normalize(Vertices[At(section + 1, edge, side)] - Vertices[prior]);
                    Vector3 outward = Vertices[at] - Vertices[At(section, inset, side)];
                    result[at] = Vector3.Normalize(outward - Vector3.Dot(outward, tangent) * tangent);
                }
                return result;
            }
        }
    }
}



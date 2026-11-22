using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Contour4.Procedural.Catalog
{
    [Flags]
    public enum DockUse { Physical = 1, DoorPortal = 2, UpperPortal = 4, LowerPortal = 8 }

    public enum Cargo { Hand, Cart }
    public enum BlockRole { Floor, Shell, Obstacle, Plug, Free, Fixture }

    public sealed class Block
    {
        public string Id { get; }
        public BlockRole Role { get; }
        public Vector3[] Vertices { get; }
        public int[] Faces { get; }
        public DressingSurface? Surface { get; }
        // Counterclockwise as seen from outside in the source coordinate system.
        private static readonly int[] SpanTriangles = {
            0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7,
        };

        private Block(string id, BlockRole role, Vector3[] vertices, int[] faces, DressingSurface? surface = null)
        {
            Id = id;
            Role = role;
            Vertices = vertices;
            Faces = faces;
            Surface = surface;
        }

        // For curved decks, an end cross section can be at a different tangent and roll.
        public static Block Span(string id, BlockRole role, ModulePose start, ModulePose end,
            float low, float high, float left, float right) =>
            new Block(id, role, Ring(start, low, high, left, right).Concat(Ring(end, low, high, left, right)).ToArray(),
                SpanTriangles);

        public static Block FromPrism(string id, BlockRole role, Solid solid)
        {
            int count = solid.Section.Length;
            var triangles = new List<int>();
            for (int i = 1; i < count - 1; i++)
            {
                triangles.AddRange(new[] { 0, 2 * (i + 1), 2 * i });
                triangles.AddRange(new[] { 1, 2 * i + 1, 2 * (i + 1) + 1 });
            }
            for (int i = 0; i < count; i++)
            {
                int a = 2 * i;
                int b = 2 * ((i + 1) % count);
                triangles.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
            }
            return new Block(id, role, solid.Vertices.ToArray(), triangles.ToArray());
        }

        public Block Reframe(string id, ModulePose placement) => new Block(id, Role,
            Vertices.Select(v => placement.Point(v.X, v.Y, v.Z)).ToArray(), Faces, Surface);

        public static Block FromFixture(RoomDressing dressing, RoomDressing.Part part)
        {
            Vector3 a = part.Center - part.Size / 2;
            Vector3 b = part.Center + part.Size / 2;
            Block block = Box(dressing.Name(part.Id), BlockRole.Fixture, dressing.Frame,
                a.X, b.X, a.Y, b.Y, a.Z, b.Z);
            return new Block(block.Id, block.Role, block.Vertices, block.Faces, part.Surface);
        }

        private static Vector3[] Ring(ModulePose pose, float low, float high, float left, float right) =>
            new[] { pose.Point(0, low, left), pose.Point(0, high, left),
                pose.Point(0, high, right), pose.Point(0, low, right) };

        public static Block Box(string id, BlockRole role, ModulePose frame, float x0, float x1,
            float y0, float y1, float z0, float z1) => Span(id, role,
                new ModulePose(frame.Point(x0, 0), frame.Forward, frame.Up),
                new ModulePose(frame.Point(x1, 0), frame.Forward, frame.Up), y0, y1, z0, z1);

        public (Vector3 Min, Vector3 Max) Bounds =>
            (Vertices.Aggregate(Vector3.Min), Vertices.Aggregate(Vector3.Max));
    }

    public sealed class Dock
    {
        public string Id { get; }
        public string FloorId { get; }
        public ModulePose Threshold { get; }
        public DockUse Use { get; }
        public Vector2 Opening { get; }
        public Vector2 MapPoint { get; }
        public Cargo MaxCargo { get; }
        public string PlugId { get; }

        public Dock(string id, string floorId, ModulePose threshold, DockUse use,
            Vector2 opening, Vector2 mapPoint, Cargo cargo, string plugId = null)
        {
            Id = id;
            FloorId = floorId;
            Threshold = threshold;
            Use = use;
            Opening = opening;
            MapPoint = mapPoint;
            MaxCargo = cargo;
            PlugId = plugId ?? $"{id}-plug";
        }

        public Vector3 ContactStart => Threshold.Point(0, 0, -Opening.X / 2);
        public Vector3 ContactEnd => Threshold.Point(0, 0, Opening.X / 2);

        public bool FitsPhysical(Dock other) =>
            (Use & other.Use & DockUse.Physical) != 0 && Opening == other.Opening &&
            Vector3.Distance(Threshold.Position, other.Threshold.Position) < .001f &&
            Vector3.Dot(Threshold.Forward, other.Threshold.Forward) < -.999f &&
            Vector3.Dot(Threshold.Up, other.Threshold.Up) > .999f;
    }

    public sealed class HaulRoute
    {
        public string Id { get; }
        public string From { get; }
        public string To { get; }
        public Cargo Cargo { get; }
        public ModulePose[] Knots { get; }
        public float StepClearance { get; }
        public string Condition { get; }
        public (Vector3 Start, Vector3 End)? Flight { get; }
        public Vector3 Envelope => Cargo == Cargo.Cart ? CornerSlice.CartEnvelope : new Vector3(.8f, 1.88f, .90f);

        public HaulRoute(string id, string from, string to, Cargo cargo, ModulePose[] knots,
            float stepClearance = .03f, string condition = null, (Vector3 Start, Vector3 End)? flight = null)
        {
            Id = id;
            From = from;
            To = to;
            Cargo = cargo;
            Knots = knots;
            StepClearance = stepClearance;
            Condition = condition;
            Flight = flight;
        }

        public bool NeedsSupport(ModulePose pose)
        {
            if (!(Flight is { } flight)) return true;
            Vector3 direction = flight.End - flight.Start;
            return Vector3.Dot(pose.Position - flight.Start, direction) <= .0001f ||
                Vector3.Dot(pose.Position - flight.End, direction) >= -.0001f;
        }

        public IEnumerable<ModulePose> Samples(float step)
        {
            yield return Knots[0];
            for (int i = 1; i < Knots.Length; i++)
            {
                ModulePose a = Knots[i - 1];
                ModulePose b = Knots[i];
                int count = Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(a.Position, b.Position) / step));
                float turn = MathF.Min(Vector3.Dot(a.Up, b.Up), Vector3.Dot(a.Forward, b.Forward));
                count = Math.Max(count, (int)MathF.Ceiling(MathF.Acos(Math.Clamp(turn, -1, 1)) / .045f));
                for (int j = 1; j <= count; j++)
                {
                    float t = (float)j / count;
                    Vector3 forward = Vector3.Normalize(Vector3.Lerp(a.Forward, b.Forward, t));
                    Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.Lerp(a.Up, b.Up, t)));
                    yield return new ModulePose(Vector3.Lerp(a.Position, b.Position, t), forward,
                        Vector3.Cross(right, forward));
                }
            }
        }
    }

    public sealed class GrayModule
    {
        public string Id { get; }
        public Vector2 Plan { get; }
        public int Difficulty { get; }
        public List<Floor> Floors { get; } = new List<Floor>();
        public List<Dock> Docks { get; } = new List<Dock>();
        public List<Block> Geometry { get; } = new List<Block>();
        public List<Block> Approaches { get; } = new List<Block>();
        public List<Block> FallVolumes { get; } = new List<Block>();
        public List<RoomDressing> Dressing { get; } = new List<RoomDressing>();
        public List<HaulRoute> Routes { get; } = new List<HaulRoute>();
        public Dictionary<string, Vector3> Landmarks { get; } = new Dictionary<string, Vector3>();
        public List<(string A, string B)> InternalPortals { get; } = new List<(string A, string B)>();
        public IEnumerable<Dock> ExternalDocks => Docks.Where(d => InternalPortals.All(p => p.A != d.Id && p.B != d.Id));

        public GrayModule(string id, float width, float length, int difficulty = 0)
        {
            Id = id;
            Plan = new Vector2(width, length);
            Difficulty = difficulty;
        }

        public Vector3 Size
        {
            get
            {
                var bounds = Geometry.Select(block => block.Bounds).ToArray();
                return bounds.Select(bound => bound.Max).Aggregate(Vector3.Max) -
                    bounds.Select(bound => bound.Min).Aggregate(Vector3.Min);
            }
        }
    }
}

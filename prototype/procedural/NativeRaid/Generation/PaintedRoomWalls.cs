using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class PaintedRoomWalls
    {
        private static readonly int UpA = Shader.PropertyToID("_WallUpA");
        private static readonly int UpB = Shader.PropertyToID("_WallUpB");
        private static readonly int FromA = Shader.PropertyToID("_WallFromA");
        private static readonly int FromB = Shader.PropertyToID("_WallFromB");
        private static readonly int Station = Shader.PropertyToID("_WallStation");
        private static readonly int Across = Shader.PropertyToID("_WallAcross");
        private static readonly int Center = Shader.PropertyToID("_WallCenter");

        private readonly Dictionary<string, Projection> walls = new(StringComparer.Ordinal);

        internal PaintedRoomWalls(IEnumerable<Block> geometry, GrayModule module)
        {
            if (geometry == null || (module != null && SphericalChamber.IsVariant(module.Id)))
                return;

            var blocks = geometry.ToLookup(block => block.Id, StringComparer.Ordinal);
            var distances = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (Block deck in geometry)
            {
                if (deck.Role != BlockRole.Floor || !deck.Id.EndsWith("-deck", StringComparison.Ordinal))
                    continue;
                string segment = deck.Id.Substring(0, deck.Id.Length - "-deck".Length);
                // Unroofed freight folds have short guardrails. A swept hall has actual side walls.
                if (!blocks.Contains(segment + "-roof") ||
                    !blocks.Contains(segment + "-left") || !blocks.Contains(segment + "-right"))
                    continue;
                string path = segment.Substring(0, segment.LastIndexOf('-'));
                distances.TryGetValue(path, out float start);
                var a = Section(deck, 0);
                var b = Section(deck, 4);
                float end = start + Vector3.Distance(a.Origin, b.Origin);
                walls.Add(segment + "-left", Projection.Swept(a, b, start, end));
                walls.Add(segment + "-right", Projection.Swept(a, b, start, end));
                distances[path] = end;
            }

            if (module == null) return;
            if (module.Id == "right-angle-chambers")
                for (int index = 0; index < CornerSlice.Segments; index++)
                {
                    float start = CornerSlice.ArcLength * index / CornerSlice.Segments;
                    float end = CornerSlice.ArcLength * (index + 1) / CornerSlice.Segments;
                    var projection = Projection.Swept(Frame.Of(CornerSlice.Frame(start)),
                        Frame.Of(CornerSlice.Frame(end)), start, end);
                    walls.Add($"wall-left-{index:D2}", projection);
                    walls.Add($"wall-right-{index:D2}", projection);
                }

            foreach (Block block in module.Geometry)
            {
                if (walls.ContainsKey(block.Id)) continue;
                if (block.Role == BlockRole.Shell && IsRoomWall(block.Id))
                {
                    Frame floor = WallFloor(module, block);
                    var (min, max) = block.Bounds;
                    walls.Add(block.Id, Projection.Flat(floor, RaidPlan.Vector(max - min)));
                }
                else if (block.Role == BlockRole.Plug)
                {
                    var door = module.Docks.FirstOrDefault(dock => dock.PlugId == block.Id &&
                        (dock.Use & (DockUse.Physical | DockUse.DoorPortal)) != 0);
                    if (door == null) continue;
                    var floor = module.Floors.FirstOrDefault(candidate => candidate.Id == door.FloorId);
                    Frame sill = Frame.Of(door.Threshold);
                    // A door above the floor still shares the surrounding room's paint.
                    // At a rotated bridge exit the threshold is the physical floor frame.
                    if (floor != null &&
                        Vector3.Dot(sill.Up, RaidPlan.Vector(floor.Pose.Up)) > .999f)
                        sill = new Frame(RaidPlan.Vector(floor.Pose.Position), sill.Up, sill.Forward);
                    walls.Add(block.Id, Projection.Flat(sill.Origin, sill.Up, Vector3.Cross(sill.Forward, sill.Up)));
                }
            }
        }

        internal bool TryGet(string name, out Projection projection) => walls.TryGetValue(name, out projection);

        private static bool IsRoomWall(string id) =>
            (id != "pit-catch-wall" && id.Contains("-wall", StringComparison.Ordinal)) ||
            id.EndsWith("-niche-return", StringComparison.Ordinal) ||
            id.StartsWith("branch-end-", StringComparison.Ordinal) && id.EndsWith("-jamb", StringComparison.Ordinal) ||
            id.Contains("-bridge-jamb-", StringComparison.Ordinal) ||
            id is "pit-east" or "pit-west" or "pit-south" or "pit-freight-fascia" or
                "shaft-east" or "shaft-west" or "shaft-near" or "shaft-far" or
                "shutter-north-guide" or "shutter-south-guide";

        private static Frame WallFloor(GrayModule module, Block wall)
        {
            string id = wall.Id;
            // These vertical walls begin at a recessed shaft floor rather than the deck above it.
            string recess = id switch
            {
                "pit-east" or "pit-west" or "pit-south" or "pit-freight-fascia" => "pit-catch-wall",
                "shaft-east" or "shaft-west" or "shaft-near" or "shaft-far" => "shaft-bottom",
                _ => null
            };
            if (recess != null)
            {
                float height = module.Geometry.Single(block => block.Id == recess).Bounds.Max.Y;
                return new Frame(Vector3.up * height, Vector3.up, Vector3.right);
            }

            // Compound catalog rooms with a different local gravity use named floor frames.
            string floorId = module.Id switch
            {
                "gallery-chamber" when id.StartsWith("small-", StringComparison.Ordinal) => "small-floor",
                "niche-chamber" when id.StartsWith("cell-", StringComparison.Ordinal) => "cell-floor",
                "top-to-top-chambers" or "floor-to-floor-chambers" => id.Substring(0, 1),
                "right-angle-chambers" when id.StartsWith("distribution-hall-", StringComparison.Ordinal) => "distribution-hall",
                "u-shaft" when id.StartsWith("east-", StringComparison.Ordinal) => "east",
                "u-shaft" when id.StartsWith("west-", StringComparison.Ordinal) => "west",
                MobiusHall.Id when id.StartsWith("east-ceiling-wall", StringComparison.Ordinal) => "ceiling",
                _ => null
            };
            var floor = floorId == null ? module.Floors.FirstOrDefault() : module.Floors.Single(f => f.Id == floorId);
            return floor == null ? new Frame(Vector3.zero, Vector3.up, Vector3.right) : Frame.Of(floor.Pose);
        }

        private static Frame Section(Block deck, int end)
        {
            // A catalog span stores low/left, high/left, high/right, low/right at each end.
            var low = RaidPlan.Vector(deck.Vertices[end]);
            var left = RaidPlan.Vector(deck.Vertices[end + 1]);
            var right = RaidPlan.Vector(deck.Vertices[end + 2]);
            Vector3 up = (left - low).normalized;
            return new Frame((left + right) * .5f, up, Vector3.Cross(up, (right - left).normalized));
        }

        internal readonly struct Frame
        {
            internal readonly Vector3 Origin, Up, Forward;

            internal Frame(Vector3 origin, Vector3 up, Vector3 forward)
            {
                Origin = origin;
                Up = up;
                Forward = forward;
            }

            internal static Frame Of(ModulePose pose) => new(RaidPlan.Vector(pose.Position),
                RaidPlan.Vector(pose.Up), RaidPlan.Vector(pose.Forward));
        }

        internal readonly struct Projection
        {
            private readonly Frame a, b;
            private readonly Vector3 across, center;
            private readonly Vector4 station;

            private Projection(Frame a, Frame b, Vector3 across, Vector4 station, Vector3 center = default)
            {
                this.a = a;
                this.b = b;
                this.across = across;
                this.station = station;
                this.center = center;
            }

            internal static Projection Flat(Vector3 origin, Vector3 up, Vector3 along) =>
                new(new Frame(origin, up, Vector3.zero), new Frame(origin, up, Vector3.zero), along, Vector4.zero);

            internal static Projection Flat(Frame floor, Vector3 size)
            {
                Vector3 right = Vector3.Cross(floor.Forward, floor.Up);
                Vector3 along = Vector3.Dot(Absolute(floor.Forward), size) >= Vector3.Dot(Absolute(right), size)
                    ? floor.Forward : right;
                // Give neighbouring sections of the same wall a common projection direction.
                if (Mathf.Abs(along.x) > .5f ? along.x < 0 : Mathf.Abs(along.z) > .5f ? along.z < 0 : along.y < 0)
                    along = -along;
                return Flat(floor.Origin, floor.Up, along);
            }

            internal static Projection Swept(Frame a, Frame b, float start, float end) =>
                new(a, b, Vector3.zero, new Vector4(start, end, 1, 0));

            internal static Projection Circular(Frame a, Frame b)
            {
                Vector3 change = a.Up - b.Up;
                float radius = Vector3.Dot(b.Origin - a.Origin, change) / change.sqrMagnitude;
                Vector3 center = a.Origin + a.Up * radius;
                Vector3 axis = Vector3.Cross(a.Up, b.Up).normalized;
                return new(new Frame(a.Origin, a.Up, -a.Up), b, axis, new Vector4(0, 0, 2, radius), center);
            }

            internal void Apply(MaterialPropertyBlock properties)
            {
                properties.SetVector(UpA, Plane(a.Up, a.Origin));
                properties.SetVector(UpB, Plane(b.Up, b.Origin));
                properties.SetVector(FromA, Plane(a.Forward, a.Origin));
                properties.SetVector(FromB, Plane(-b.Forward, b.Origin));
                properties.SetVector(Station, station);
                properties.SetVector(Across, across);
                properties.SetVector(Center, center);
            }

            private static Vector4 Plane(Vector3 direction, Vector3 origin) =>
                new(direction.x, direction.y, direction.z, -Vector3.Dot(direction, origin));

            private static Vector3 Absolute(Vector3 vector) =>
                new(Mathf.Abs(vector.x), Mathf.Abs(vector.y), Mathf.Abs(vector.z));
        }
    }
}

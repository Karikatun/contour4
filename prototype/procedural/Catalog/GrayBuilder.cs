using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Contour4.Procedural.Catalog
{
    public sealed class GrayBuilder
    {
        public static readonly ModulePose Level = new ModulePose(Vector3.Zero, Vector3.UnitX, Vector3.UnitY);
        public const float Skirt = .25f;
        public const float Height = 4;
        public const float Wide = 4.5f;
        public const float Carry = 1.5f;
        public GrayModule Module { get; }

        public GrayBuilder(string id, float width, float length, int difficulty = 0)
        {
            Module = new GrayModule(id, width, length, difficulty);
        }

        public static ModulePose Pose(float x, float y, float z, Vector3? heading = null) =>
            new ModulePose(new Vector3(x, y, z), heading ?? Vector3.UnitX, Vector3.UnitY);

        public void Box(string id, BlockRole role, float x0, float x1, float y0, float y1, float z0, float z1) =>
            Module.Geometry.Add(Block.Box(id, role, Level, x0, x1, y0, y1, z0, z1));

        public void Deck(string id, float x0, float x1, float y, float z0, float z1) =>
            Box(id, BlockRole.Floor, x0, x1, y - Skirt, y, z0, z1);

        public void FallVolume(string id, float x0, float x1, float y0, float y1, float z0, float z1) =>
            Module.FallVolumes.Add(Block.Box(id, BlockRole.Free, Level, x0, x1, y0, y1, z0, z1));

        public void MapFloor(string id, ModulePose frame, params Vector2[] outline) =>
            Module.Floors.Add(new Floor(id, frame, outline));

        public void MapRectangle(string id, float x0, float x1, float y, float z0, float z1) =>
            MapFloor(id, Pose(0, y, 0), new Vector2(x0, z0), new Vector2(x1, z0),
                new Vector2(x1, z1), new Vector2(x0, z1));

        public void Floor(string id, float x0, float x1, float y, float z0, float z1)
        {
            Deck(id, x0, x1, y, z0, z1);
            MapRectangle(id, x0, x1, y, z0, z1);
        }

        public IEnumerable<ModulePose> Stairs(string id, float x0, float x1, float y0, float y1,
            float z0, float z1, int count)
        {
            float stepDepth = (x1 - x0) / count;
            float rise = (y1 - y0) / count;
            float center = (z0 + z1) / 2;
            for (int i = 0; i < count; i++)
                Box($"{id}-{i + 1:D2}", BlockRole.Floor, x0 + stepDepth * i, x0 + stepDepth * (i + 1),
                    y0, y0 + rise * (i + 1), z0, z1);
            yield return Pose(x0, y0, center);
            yield return Pose(x1, y1, center);
        }

        public void Roof(float x0, float x1, float y, float z0, float z1) =>
            Box("roof", BlockRole.Shell, x0, x1, y, y + Skirt, z0, z1);

        public void RoofHatch(string id, string floor, float x0, float x1, float y, float z0, float z1,
            float hatchX, float hatchZ, float size, float depth)
        {
            float endX = hatchX + size;
            float endZ = hatchZ + size;
            var pose = new ModulePose(new Vector3(hatchX, y + Skirt, (hatchZ + endZ) / 2),
                Vector3.UnitY, Vector3.UnitX);
            Door(id, floor, pose, Cargo.Hand, DockUse.UpperPortal, size, size, depth,
                new Vector2((hatchX + endX) / 2, (hatchZ + endZ) / 2));
            Box("roof-west", BlockRole.Shell, x0, hatchX, y, y + Skirt, z0, z1);
            Box("roof-east", BlockRole.Shell, endX, x1, y, y + Skirt, z0, z1);
            Box("roof-near", BlockRole.Shell, hatchX, endX, y, y + Skirt, z0, hatchZ);
            Box("roof-far", BlockRole.Shell, hatchX, endX, y, y + Skirt, endZ, z1);
            Box($"{id}-plug", BlockRole.Plug, hatchX, endX, y, y + Skirt, hatchZ, endZ);
        }

        // Door sits in the outside face. The wall and plug only extend into their own module.
        public void Wall(string id, ModulePose edge, float halfWidth, float height, Dock door = null)
        {
            if (door == null)
            {
                WallPart(id, edge, BlockRole.Shell, 0, height, -halfWidth, halfWidth);
                return;
            }
            float center = Vector3.Dot(door.Threshold.Position - edge.Position, edge.Right);
            float sill = Vector3.Dot(door.Threshold.Position - edge.Position, edge.Up);
            float left = center - door.Opening.X / 2;
            float right = center + door.Opening.X / 2;
            float lintel = sill + door.Opening.Y;
            WallPart($"{id}-left", edge, BlockRole.Shell, 0, height, -halfWidth, left);
            WallPart($"{id}-right", edge, BlockRole.Shell, 0, height, right, halfWidth);
            WallPart($"{id}-sill", edge, BlockRole.Shell, 0, sill, left, right);
            WallPart($"{id}-lintel", edge, BlockRole.Shell, lintel, height, left, right);
            WallPart(door.PlugId, edge, BlockRole.Plug, sill, lintel, left, right);
        }

        private void WallPart(string id, ModulePose edge, BlockRole role,
            float y0, float y1, float z0, float z1)
        {
            if (y1 > y0 && z1 > z0)
                Module.Geometry.Add(Block.Box(id, role, edge, -Skirt, 0, y0, y1, z0, z1));
        }

        public Dock Door(string id, string floor, ModulePose pose, Cargo cargo,
            DockUse use = DockUse.Physical, float width = Wide, float height = Height, float depth = 2.8f,
            Vector2? mapPoint = null, string plugId = null)
        {
            var door = new Dock(id, floor, pose, use, new Vector2(width, height),
                mapPoint ?? new Vector2(pose.Position.X, pose.Position.Z), cargo, plugId);
            Module.Docks.Add(door);
            Module.Approaches.Add(Block.Box($"{id}-approach", BlockRole.Free, pose,
                -depth, 0, 0, height, -width / 2, width / 2));
            return door;
        }

        public void Seal(Dock dock) => Module.Geometry.Add(Block.Box(dock.PlugId, BlockRole.Plug,
            dock.Threshold, -Skirt, 0, 0, dock.Opening.Y, -dock.Opening.X / 2, dock.Opening.X / 2));

        public void RectangleWalls(float x0, float x1, float baseY, float height, float z0, float z1,
            Dock west = null, Dock east = null, Dock near = null, Dock far = null)
        {
            float centerX = (x0 + x1) / 2;
            float centerZ = (z0 + z1) / 2;
            Wall("west-wall", Pose(x0, baseY, centerZ, -Vector3.UnitX), (z1 - z0) / 2, height, west);
            Wall("east-wall", Pose(x1, baseY, centerZ), (z1 - z0) / 2, height, east);
            Wall("near-wall", Pose(centerX, baseY, z0, -Vector3.UnitZ), (x1 - x0) / 2 - Skirt, height, near);
            Wall("far-wall", Pose(centerX, baseY, z1, Vector3.UnitZ), (x1 - x0) / 2 - Skirt, height, far);
        }

        public void Route(string id, string from, string to, Cargo cargo, params ModulePose[] knots) =>
            Module.Routes.Add(new HaulRoute(id, from, to, cargo, knots));

        public void AddDressing(RoomDressing dressing)
        {
            Module.Geometry.AddRange(dressing.Parts.Select(part => Block.FromFixture(dressing, part)));
            Module.Dressing.Add(dressing);
        }

        public void AddPart(string prefix, GrayModule part, ModulePose placement)
        {
            string Name(string id) => $"{prefix}-{id}";
            Vector3 Point(Vector3 p) => placement.Point(p.X, p.Y, p.Z);
            Vector3 Direction(Vector3 d) => placement.Forward * d.X + placement.Up * d.Y + placement.Right * d.Z;
            ModulePose PoseOf(ModulePose p) => new ModulePose(Point(p.Position), Direction(p.Forward), Direction(p.Up));
            foreach (Floor f in part.Floors)
                Module.Floors.Add(new Floor(Name(f.Id), PoseOf(f.Pose), f.MapContour));
            foreach (Dock d in part.Docks)
                Module.Docks.Add(new Dock(Name(d.Id), Name(d.FloorId), PoseOf(d.Threshold), d.Use,
                    d.Opening, d.MapPoint, d.MaxCargo, Name(d.PlugId)));
            foreach (Block b in part.Geometry)
                Module.Geometry.Add(b.Reframe(Name(b.Id), placement));
            foreach (Block b in part.Approaches)
                Module.Approaches.Add(b.Reframe(Name(b.Id), placement));
            foreach (Block b in part.FallVolumes)
                Module.FallVolumes.Add(b.Reframe(Name(b.Id), placement));
            foreach (RoomDressing dressing in part.Dressing)
                Module.Dressing.Add(dressing.Reframe(prefix, placement));
            foreach (KeyValuePair<string, Vector3> landmark in part.Landmarks)
                Module.Landmarks.Add(Name(landmark.Key), Point(landmark.Value));
            foreach (HaulRoute r in part.Routes)
                Module.Routes.Add(new HaulRoute(Name(r.Id), Name(r.From), Name(r.To), r.Cargo,
                    r.Knots.Select(PoseOf).ToArray(), r.StepClearance, r.Condition,
                    r.Flight is { } flight ? (Point(flight.Start), Point(flight.End)) : default((Vector3, Vector3)?)));
            foreach (var (a, b) in part.InternalPortals)
                Module.InternalPortals.Add((Name(a), Name(b)));
        }

        public static IEnumerable<ModulePose> Bend(ModulePose start, float radius, Vector3 toward,
            int segments = 20)
        {
            float rise = Vector3.Dot(toward, start.Up);
            for (int i = 0; i <= segments; i++)
            {
                float sin = i == segments ? 1 : MathF.Sin(i * MathF.PI / (2 * segments));
                float cos = i == segments ? 0 : MathF.Cos(i * MathF.PI / (2 * segments));
                yield return new ModulePose(start.Position + radius * (sin * start.Forward + (1 - cos) * toward),
                    cos * start.Forward + sin * toward,
                    start.Up + rise * ((cos - 1) * toward - sin * start.Forward));
            }
        }

        public static IEnumerable<ModulePose> LaneChange(float start, float end, float from, float to, float y = 0)
        {
            const int segments = 20;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float offset = from + (to - from) * t * t * (3 - 2 * t);
                float slope = (to - from) * 6 * t * (1 - t) / (end - start);
                yield return Pose(start + (end - start) * t, y, offset,
                    Vector3.Normalize(new Vector3(1, 0, slope)));
            }
        }

        public void SweptHall(string prefix, IReadOnlyList<ModulePose> frames, float width, float height)
        {
            float half = width / 2;
            for (int i = 1; i < frames.Count; i++)
            {
                var a = frames[i - 1];
                var b = frames[i];
                var name = $"{prefix}-{i:D2}";
                Module.Geometry.Add(Block.Span($"{name}-deck", BlockRole.Floor, a, b, -Skirt, 0, -half, half));
                Module.Geometry.Add(Block.Span($"{name}-roof", BlockRole.Shell, a, b, height, height + Skirt, -half, half));
                Module.Geometry.Add(Block.Span($"{name}-left", BlockRole.Shell, a, b, -Skirt, height + Skirt, -half - Skirt, -half));
                Module.Geometry.Add(Block.Span($"{name}-right", BlockRole.Shell, a, b, -Skirt, height + Skirt, half, half + Skirt));
                Module.Approaches.Add(Block.Span($"{name}-free", BlockRole.Free, a, b, 0, height, -half, half));
            }
        }
    }
}

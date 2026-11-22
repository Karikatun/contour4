using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Contour4.Procedural
{
    public sealed class CornerSlice
    {
        public const float RoomDepth = 15;
        public const float Width = 4.5f;
        public const float Headroom = 4;
        public const float Thickness = .25f;
        public const float Radius = 11.25f;
        public const int Segments = 36;
        public const float ArcLength = Radius * MathF.PI / 2;
        public const float RouteApproach = 2;
        public static readonly Vector3 CartEnvelope = new Vector3(2.6f, 2.05f, 1.8f);
        public static readonly Vector2 ArcCenter = new Vector2(-Radius, -Radius);
        public static readonly Vector3 ContactStart = new Vector3(0, 0, -3.75f);
        public static readonly Vector3 ContactEnd = new Vector3(0, 0, 3.75f);

        public Room Airlock { get; }
        public Room Hall { get; }
        public IReadOnlyList<Solid> Turn { get; }
        public IReadOnlyList<Solid> TurnClearance { get; }
        public Solid ContactRail { get; }
        public IReadOnlyList<RoomDressing> Dressing { get; }
        public IEnumerable<Solid> Solids => Airlock.Solids.Concat(Hall.Solids).Concat(Turn).Append(ContactRail);

        public CornerSlice()
        {
            Airlock = CreateRoom("airlock", 7.5f, new ModulePose(Vector3.Zero, Vector3.UnitX, Vector3.UnitY),
                new Vector2(-RoomDepth + Radius, 0), new Vector2(0, 0));
            Hall = CreateRoom("distribution-hall", 15, new ModulePose(Vector3.Zero, Vector3.UnitY, Vector3.UnitX),
                new Vector2(ArcLength + RoomDepth - Radius, 0), new Vector2(ArcLength, 0));
            var dressing = new List<RoomDressing> { RoomDressing.CornerRoom(Airlock), RoomDressing.CornerRoom(Hall) };
            for (int i = 0; i <= 4; i++)
            {
                var guide = new RoomDressing($"quarter-turn-field-{i}", Frame(ArcLength * i / 4));
                guide.GuideFromWall("guard-recess", 2.1f, 2.17f, 1);
                dressing.Add(guide);
            }
            Dressing = dressing;

            ContactRail = new Solid("common-edge", new[] { Vector2.Zero, new Vector2(.35f, .1f),
                new Vector2(.1f, .35f) }, ContactStart.Z, ContactEnd.Z);
            var turn = new List<Solid>();
            var clearance = new List<Solid>();
            for (int i = 0; i < Segments; i++)
            {
                Vector2 start = Radial(i);
                Vector2 end = Radial(i + 1);
                float halfWidth = Width / 2;
                turn.Add(ArcSolid($"deck-{i:D2}", start, end, -Thickness, 0, -halfWidth, halfWidth));
                turn.Add(ArcSolid($"roof-{i:D2}", start, end, Headroom, Headroom + Thickness,
                    -halfWidth, halfWidth, ceiling: true));
                turn.Add(ArcSolid($"wall-left-{i:D2}", start, end, -Thickness, Headroom + Thickness,
                    -halfWidth - Thickness, -halfWidth));
                turn.Add(ArcSolid($"wall-right-{i:D2}", start, end, -Thickness, Headroom + Thickness,
                    halfWidth, halfWidth + Thickness));
                clearance.Add(ArcSolid($"free-{i:D2}", start, end, 0, Headroom, -halfWidth, halfWidth));
            }
            Turn = turn;
            TurnClearance = clearance;
        }

        // Negative distance is the airlock approach. After ArcLength the route continues on the hall floor.
        public static ModulePose Frame(float distance)
        {
            if (distance <= 0)
                return new ModulePose(new Vector3(distance - Radius, 0, 0), Vector3.UnitX, Vector3.UnitY);
            if (distance >= ArcLength)
                return new ModulePose(new Vector3(0, -Radius - (distance - ArcLength), 0),
                    -Vector3.UnitY, Vector3.UnitX);

            float angle = distance / Radius;
            Vector3 up = new Vector3(MathF.Sin(angle), MathF.Cos(angle), 0);
            Vector3 forward = new Vector3(up.Y, -up.X, 0);
            return new ModulePose(new Vector3(ArcCenter, 0) + up * Radius, forward, up);
        }

        public static float RouteDistance(Vector3 position)
        {
            if (position.X <= -Radius)
                return position.X + Radius;
            if (position.Y <= -Radius)
                return ArcLength - Radius - position.Y;
            return Radius * MathF.Atan2(position.X + Radius, position.Y + Radius);
        }

        private static Vector2 Radial(int step)
        {
            if (step == 0) return Vector2.UnitY;
            if (step == Segments) return Vector2.UnitX;
            float angle = MathF.PI * step / (2 * Segments);
            return new Vector2(MathF.Sin(angle), MathF.Cos(angle));
        }

        private static Solid ArcSolid(string id, Vector2 start, Vector2 end, float inner, float outer,
            float z0, float z1, bool ceiling = false) => new Solid(id, new[]
            {
                ArcCenter + start * (Radius + inner), ArcCenter + end * (Radius + inner),
                ArcCenter + end * (Radius + outer), ArcCenter + start * (Radius + outer),
            }, z0, z1, ceiling: ceiling);

        private static Room CreateRoom(string id, float width, ModulePose pose, Vector2 doorMap, Vector2 dockMap)
        {
            const string level = "ground";
            const string plug = "door-plug";
            float half = width / 2;
            float clear = Width / 2;
            float reserve = clear + Thickness;
            var solids = new List<Solid>();

            void Box(string name, float x0, float x1, float y0, float y1, float z0, float z1,
                bool isPlug = false, bool ceiling = false) => solids.Add(
                Solid.Rectangle(name, pose, x0, x1, y0, y1, z0, z1, isPlug, ceiling));

            Box("floor", -RoomDepth, -Radius, -Thickness, 0, -half, half);
            Box("ceiling", -RoomDepth, -Radius, Headroom, Headroom + Thickness, -half, half, ceiling: true);
            foreach (int side in new[] { -1, 1 })
            {
                string name = side < 0 ? "left" : "right";
                float z0 = side < 0 ? -half : reserve;
                float z1 = side < 0 ? -reserve : half;
                // Bevel each flank back from the edge so the two perpendicular slabs only touch along a line.
                solids.Add(Solid.Extrude($"floor-wing-{name}", pose, new[] {
                    new Vector2(-Radius, -Thickness), new Vector2(-2 * Thickness, -Thickness),
                    Vector2.Zero, new Vector2(-Radius, 0),
                }, z0, z1));
                Box($"ceiling-wing-{name}", -Radius, 0, Headroom, Headroom + Thickness, z0, z1, ceiling: true);
                Box($"corner-wall-{name}", -Thickness, 0, Thickness, Headroom, z0, z1);
                Box($"outer-wall-{name}", -RoomDepth + Thickness, -Thickness, 0, Headroom,
                    side < 0 ? -half : half - Thickness, side < 0 ? -half + Thickness : half);
                Box($"rear-wall-{name}", -RoomDepth, -RoomDepth + Thickness, 0, Headroom,
                    side < 0 ? -half : clear, side < 0 ? -clear : half);
            }
            Box(plug, -RoomDepth, -RoomDepth + Thickness, 0, Headroom, -clear, clear, isPlug: true);
            solids.Add(Solid.Extrude("contact-lip", pose, new[] { new Vector2(-Thickness, 0),
                Vector2.Zero, new Vector2(-Thickness, Thickness) }, -half, half));

            Vector2[] outline = {
                new Vector2(doorMap.X, -half), new Vector2(dockMap.X, -half),
                new Vector2(dockMap.X, half), new Vector2(doorMap.X, half),
            };
            var floor = new Floor(level, pose, outline);
            Vector2 opening = new Vector2(Width, Headroom);
            var door = new Socket("external-door", level, new ModulePose(pose.Point(-RoomDepth, 0),
                -pose.Forward, pose.Up), SocketKind.PhysicalDoor, opening, doorMap, plug);
            var dock = new Socket("turn-dock", level, new ModulePose(pose.Point(-Radius, 0),
                pose.Forward, pose.Up), SocketKind.QuarterTurn, opening, dockMap);
            Solid approach = Solid.Rectangle("door-to-turn", pose, -RoomDepth + Thickness, -Radius,
                0, Headroom, -clear, clear);
            return new Room(id, new Vector2(width, RoomDepth), new[] { floor }, new[] { door, dock },
                solids, new[] { approach });
        }
    }
}

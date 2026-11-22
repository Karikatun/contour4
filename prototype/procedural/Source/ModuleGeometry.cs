using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Contour4.Procedural
{
    public readonly struct ModulePose
    {
        public readonly Vector3 Position;
        public readonly Vector3 Forward;
        public readonly Vector3 Up;
        public Vector3 Right => Vector3.Cross(Forward, Up);

        public ModulePose(Vector3 position, Vector3 forward, Vector3 up)
        {
            Position = position;
            Forward = forward;
            Up = up;
        }

        public Vector3 Point(float forward, float up, float lateral = 0) =>
            Position + Forward * forward + Up * up + Right * lateral;
    }

    public sealed class Solid
    {
        public string Id { get; }
        public Vector2[] Section { get; }
        public float MinZ { get; }
        public float MaxZ { get; }
        public bool Plug { get; }
        public bool Ceiling { get; }

        // The cross section is counterclockwise in XY and extruded along world Z.
        public Solid(string id, Vector2[] section, float minZ, float maxZ,
            bool plug = false, bool ceiling = false)
        {
            Id = id;
            Section = SignedArea(section) > 0 ? section : section.Reverse().ToArray();
            MinZ = minZ;
            MaxZ = maxZ;
            Plug = plug;
            Ceiling = ceiling;
        }

        public IEnumerable<Vector3> Vertices => Section.SelectMany(
            point => new[] { new Vector3(point, MinZ), new Vector3(point, MaxZ) });

        public static Solid Rectangle(string id, ModulePose pose, float x0, float x1,
            float y0, float y1, float z0, float z1, bool plug = false, bool ceiling = false) =>
            Extrude(id, pose, new[] { new Vector2(x0, y0), new Vector2(x1, y0),
                new Vector2(x1, y1), new Vector2(x0, y1) }, z0, z1, plug, ceiling);

        public static Solid Extrude(string id, ModulePose pose, Vector2[] section,
            float z0, float z1, bool plug = false, bool ceiling = false)
        {
            if (pose.Forward.Z != 0 || pose.Up.Z != 0)
                throw new ArgumentException("This XY cross section requires a Z-aligned extrusion.", nameof(pose));
            var worldSection = Array.ConvertAll(section, point =>
            {
                Vector3 world = pose.Point(point.X, point.Y);
                return new Vector2(world.X, world.Y);
            });
            float a = pose.Point(0, 0, z0).Z;
            float b = pose.Point(0, 0, z1).Z;
            return new Solid(id, worldSection, MathF.Min(a, b), MathF.Max(a, b), plug, ceiling);
        }

        public static float SignedArea(Vector2[] section)
        {
            float doubledArea = 0;
            for (int i = 0; i < section.Length; i++)
            {
                Vector2 a = section[i];
                Vector2 b = section[(i + 1) % section.Length];
                doubledArea += a.X * b.Y - a.Y * b.X;
            }
            return doubledArea / 2;
        }
    }

    public sealed class Floor
    {
        public string Id { get; }
        public ModulePose Pose { get; }
        public Vector2[] MapContour { get; }

        public Floor(string id, ModulePose pose, Vector2[] mapContour)
        {
            Id = id;
            Pose = pose;
            MapContour = mapContour;
        }
    }

    public enum SocketKind { PhysicalDoor, QuarterTurn }

    public sealed class Socket
    {
        public string Id { get; }
        public string FloorId { get; }
        public ModulePose Threshold { get; }
        public SocketKind Kind { get; }
        public Vector2 Opening { get; }
        public Vector2 MapPoint { get; }
        public string PlugId { get; }

        public Socket(string id, string floorId, ModulePose threshold, SocketKind kind,
            Vector2 opening, Vector2 mapPoint, string plugId = null)
        {
            Id = id;
            FloorId = floorId;
            Threshold = threshold;
            Kind = kind;
            Opening = opening;
            MapPoint = mapPoint;
            PlugId = plugId;
        }
    }

    public sealed class Room
    {
        public string Id { get; }
        public Vector2 NominalPlan { get; }
        public IReadOnlyList<Floor> Floors { get; }
        public IReadOnlyList<Socket> Sockets { get; }
        public IReadOnlyList<Solid> Solids { get; }
        public IReadOnlyList<Solid> ReservedVolumes { get; }

        public Room(string id, Vector2 nominalPlan, IReadOnlyList<Floor> floors, IReadOnlyList<Socket> sockets,
            IReadOnlyList<Solid> solids, IReadOnlyList<Solid> reservedVolumes)
        {
            Id = id;
            NominalPlan = nominalPlan;
            Floors = floors;
            Sockets = sockets;
            Solids = solids;
            ReservedVolumes = reservedVolumes;
        }
    }
}

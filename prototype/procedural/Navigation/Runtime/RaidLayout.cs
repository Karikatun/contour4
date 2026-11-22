using System;
using System.Collections.Generic;
using UnityEngine;

namespace Contour4.Procedural.Navigation
{
    // All positions are world-space unless their name explicitly says local.
    public sealed class RaidFloor
    {
        public string RoomId { get; }
        public string FloorId { get; }
        public string IslandId { get; }
        private readonly Pose placed;
        private readonly Transform live;
        public Pose Frame => live ? new Pose(live.position, live.rotation) : placed;
        public Bounds LocalBakeBounds { get; }
        internal Collider[] Geometry { get; }
        internal HashSet<Collider> Support { get; }
        public Vector3 Up => Frame.rotation * Vector3.up;

        public RaidFloor(string roomId, string floorId, string islandId, Pose frame,
            Bounds localBakeBounds, IEnumerable<Collider> geometry, IEnumerable<Collider> support, Transform liveFrame = null)
        {
            if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(floorId) ||
                string.IsNullOrWhiteSpace(islandId) || !RaidGeometry.Valid(frame) ||
                !RaidGeometry.Finite(localBakeBounds.center) || !RaidGeometry.Finite(localBakeBounds.size) ||
                Mathf.Min(localBakeBounds.size.x, localBakeBounds.size.y, localBakeBounds.size.z) <= 0)
                throw new ArgumentException("A raid floor needs an identity, rigid frame and positive bake bounds.");

            RoomId = roomId;
            FloorId = floorId;
            IslandId = islandId;
            placed = frame;
            live = liveFrame;
            LocalBakeBounds = localBakeBounds;
            Geometry = new List<Collider>(geometry).ToArray();
            Support = new HashSet<Collider>(support);
            if (Support.Count == 0 || Support.Contains(null) || !Support.IsSubsetOf(Geometry))
                throw new ArgumentException("Support colliders must be a nonempty subset of floor geometry.");
        }

        internal Vector3 Local(Vector3 position) => Quaternion.Inverse(Frame.rotation) * (position - Frame.position);
        public Vector3 World(Vector3 position) => Frame.position + Frame.rotation * position;
    }

    public enum RaidPortalKind { Door, Upper, Lower }

    [Flags]
    public enum RaidPortalUse { None = 0, Vision = 1, Walking = 2 }

    public sealed class RaidAperture
    {
        public RaidFloor Floor { get; }
        public RaidPortalKind Kind { get; }
        private readonly Pose placed;
        private readonly Transform live;
        private readonly Vector3 localApproach;
        public Pose Frame => live ? new Pose(live.position, live.rotation) : placed;
        public Vector2 Size { get; }
        public Vector3 Approach => live ? live.TransformPoint(localApproach) : localApproach;
        internal Vector3 Normal => Frame.rotation * Vector3.forward;

        // Frame.forward points out of the room. The approach is a feet position inside the room.
        // Vision-only apertures have no need for a walkable approach.
        public RaidAperture(RaidFloor floor, RaidPortalKind kind, Pose frame, Vector2 size, Vector3 approach,
            Transform liveFrame = null)
        {
            if (floor == null || !Enum.IsDefined(typeof(RaidPortalKind), kind) || !RaidGeometry.Valid(frame) ||
                !RaidGeometry.Finite(new Vector3(size.x, size.y, 0)) || size.x <= 0 || size.y <= 0 ||
                !RaidGeometry.Finite(approach))
                throw new ArgumentException("A raid aperture needs a floor, rigid frame and positive dimensions.");
            if (kind == RaidPortalKind.Door &&
                Vector3.Dot(frame.rotation * Vector3.up, floor.Up) < .999f)
                throw new ArgumentException("The screen-up of a floor-level door must match its local floor.");

            Floor = floor;
            Kind = kind;
            placed = frame;
            live = liveFrame;
            Size = size;
            localApproach = live ? live.InverseTransformPoint(approach) : approach;
        }

        internal Vector3 Local(Vector3 position) => Quaternion.Inverse(Frame.rotation) * (position - Frame.position);
        internal bool Contains(Vector3 point, float marginX = 0, float marginY = 0)
        {
            var local = Local(point);
            return Mathf.Abs(local.x) < Size.x * .5f - marginX &&
                Mathf.Abs(local.y) < Size.y * .5f - marginY;
        }
    }

    public sealed class RaidPortal
    {
        public RaidAperture A { get; }
        public RaidAperture B { get; }
        public RaidPortalUse AtoB { get; }
        public RaidPortalUse BtoA { get; }

        public RaidPortal(RaidAperture a, RaidAperture b, RaidPortalUse aToB, RaidPortalUse bToA)
        {
            const RaidPortalUse supported = RaidPortalUse.Vision | RaidPortalUse.Walking;
            if (a == null || b == null || ReferenceEquals(a, b) ||
                ((aToB | bToA) & ~supported) != RaidPortalUse.None ||
                (((aToB | bToA) & RaidPortalUse.Walking) != 0 &&
                    (a.Kind != RaidPortalKind.Door || b.Kind != RaidPortalKind.Door)))
                throw new ArgumentException("Walking is supported for paired floor-level door apertures.");
            A = a;
            B = b;
            AtoB = aToB;
            BtoA = bToA;
        }
    }

    public sealed class RaidWalkPassage
    {
        public RaidFloor From { get; }
        public RaidFloor To { get; }
        private readonly Vector3 localApproach;
        private readonly Vector3 localLanding;
        public Vector3 Approach => From.World(localApproach);
        public Vector3 Landing => To.World(localLanding);

        // A directed physical threshold. The reserved segment connects nearby pads on the two floors.
        public RaidWalkPassage(RaidFloor from, RaidFloor to, Vector3 approach, Vector3 landing)
        {
            float length = Vector3.Distance(approach, landing);
            if (from == null || to == null || ReferenceEquals(from, to) ||
                Vector3.Dot(from.Up, to.Up) < .999f ||
                !RaidGeometry.Finite(approach) || !RaidGeometry.Finite(landing) || length < .05f || length > 2)
                throw new ArgumentException("Physical walking passages need adjoining floors in the same gravity frame.");
            From = from;
            To = to;
            localApproach = From.Local(approach);
            localLanding = To.Local(landing);
        }
    }

    public sealed class RaidLayout
    {
        internal RaidFloor[] Floors { get; }
        internal RaidPortal[] Portals { get; }
        internal RaidWalkPassage[] Passages { get; }
        public LayerMask SolidMask { get; }
        public Vector3 QueryOrigin { get; }
        public float BakeRadius { get; }
        public float BakeHeight { get; }

        // QueryOrigin reserves isolated NavMesh coordinates, separate from the placed rooms.
        public RaidLayout(IEnumerable<RaidFloor> floors, IEnumerable<RaidPortal> portals,
            IEnumerable<RaidWalkPassage> passages, LayerMask solidMask, Vector3 queryOrigin,
            float bakeRadius, float bakeHeight)
        {
            if (solidMask.value == 0 || !RaidGeometry.Finite(queryOrigin) ||
                !RaidGeometry.Finite(new Vector3(bakeRadius, bakeHeight, 0)) ||
                bakeRadius < .1f || bakeHeight <= 2 * bakeRadius)
                throw new ArgumentException("A raid layout needs solid layers and a valid navigation capsule.");
            Floors = new List<RaidFloor>(floors).ToArray();
            Portals = new List<RaidPortal>(portals).ToArray();
            Passages = new List<RaidWalkPassage>(passages).ToArray();
            if (Floors.Length == 0)
                throw new ArgumentException("A raid needs at least one placed navigation floor.");
            SolidMask = solidMask;
            QueryOrigin = queryOrigin;
            BakeRadius = bakeRadius;
            BakeHeight = bakeHeight;
        }
    }

    public sealed class RaidEnemyProfile
    {
        public float Radius { get; }
        public float Height { get; }
        public float EyeHeight { get; }
        public float Speed { get; }
        public float SightRange { get; }
        public float SightAngle { get; }
        public float MemorySeconds { get; }
        public bool WalksPortals { get; }
        public bool Reorients { get; }

        public RaidEnemyProfile(float radius, float height, float eyeHeight, float speed, float sightRange,
            float sightAngle, float memorySeconds, bool walksPortals, bool reorients)
        {
            if (!RaidGeometry.Finite(new Vector3(radius, height, eyeHeight)) ||
                !RaidGeometry.Finite(new Vector3(speed, sightRange, sightAngle)) ||
                !RaidGeometry.Finite(new Vector3(memorySeconds, 0, 0)) ||
                radius <= 0 || height <= 2 * radius || eyeHeight <= 0 || eyeHeight > height ||
                speed <= 0 || sightRange <= 0 || sightAngle <= 0 || sightAngle > 360 || memorySeconds <= 0)
                throw new ArgumentException("Invalid raid enemy body, movement or sight.");
            Radius = radius;
            Height = height;
            EyeHeight = eyeHeight;
            Speed = speed;
            SightRange = sightRange;
            SightAngle = sightAngle;
            MemorySeconds = memorySeconds;
            WalksPortals = walksPortals;
            Reorients = reorients;
        }
    }

    internal static class RaidGeometry
    {
        internal const float Skin = .025f;
        internal static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        internal static bool Valid(Pose pose) => Finite(pose.position) &&
            Mathf.Abs(Quaternion.Dot(pose.rotation, pose.rotation) - 1) < .001f;
    }
}

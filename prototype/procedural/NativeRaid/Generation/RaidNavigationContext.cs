using System;
using System.Collections.Generic;
using Contour4.Procedural.Navigation;
using UnityEngine;
using NavigationRuntime = Contour4.Procedural.Navigation.RaidNavigation;

namespace Contour4.NativeRaid.Runtime
{

    public static class RaidNavigation
    {
        public static RaidNavigationContext Current { get; private set; }

        internal static void Publish(RaidNavigationContext context)
        {
            if (Current != null && Current.Navigation && Current.Navigation.Ready)
                throw new InvalidOperationException("A native raid is already active.");
            Current = context;
        }

        internal static void Clear(RaidNavigationContext context)
        {
            if (ReferenceEquals(Current, context)) Current = null;
        }
    }

    public sealed class RaidNavigationContext
    {
        private readonly BakedRoom[] rooms;
        private readonly int solidMask;
        public int Seed { get; }
        public NavigationRuntime Navigation { get; }
        public IReadOnlyList<NativeRaidPortal> Portals { get; }

        internal RaidNavigationContext(int seed, NavigationRuntime navigation, BakedRoom[] rooms, NativeRaidPortal[] portals,
            int solidMask)
        {
            Seed = seed;
            Navigation = navigation;
            this.rooms = rooms;
            this.solidMask = solidMask;
            Portals = Array.AsReadOnly(portals);
        }

        public bool Contains(Vector3 position) => TryGetFloor(position, out _);

        public bool TryGetFloor(Vector3 feet, out RaidFloor floor)
        {
            float nearest = float.PositiveInfinity;
            floor = null;
            foreach (var room in rooms)
            {
                if (!room.Prepared || !room.PreparedSurfaces.TryNearFloor(feet, 1.65f, solidMask, out var candidate,
                    out float distance) || distance >= nearest) continue;
                floor = candidate;
                nearest = distance;
            }
            if (floor != null) return true;
            foreach (var room in rooms)
            {
                Vector3 local = room.Root.InverseTransformPoint(feet);
                if (room.FloorBounds.Exists(bounds => bounds.Contains(local)))
                {
                    floor = room.Floor;
                    return true;
                }
            }
            floor = null;
            return false;
        }
    }

    public sealed class NativeRaidPortal
    {
        public string Id { get; }
        public Transform Entrance { get; }
        public Transform Exit { get; }
        public float HalfWidth { get; }
        public float HalfHeight { get; }
        internal bool AllowsCart { get; }
        public RaidPortalKind EntryKind { get; }
        public RaidPortalKind ExitKind { get; }
        public bool Door => EntryKind == RaidPortalKind.Door && ExitKind == RaidPortalKind.Door;
        internal float DoorInset { get; }
        public RaidFloor EntryFloor { get; }
        public RaidFloor ExitFloor { get; }
        private readonly Transform entryPad, exitPad;
        public Quaternion Rotation => Exit.rotation * Quaternion.AngleAxis(180f, Vector3.up) * Quaternion.Inverse(Entrance.rotation);
        public Quaternion TravelRotation
        {
            get
            {
                if (Door) return Rotation;

                // Opposed floor hatches land on the approach side of the opening.
                // Reverse the forward axis so continued movement carries the player away.
                Quaternion arrival = exitPad.rotation;
                if (EntryKind == RaidPortalKind.Lower && ExitKind == RaidPortalKind.Lower &&
                    Vector3.Dot(EntryFloor.Up, ExitFloor.Up) < -.999f)
                    arrival *= Quaternion.AngleAxis(180f, Vector3.up);
                return arrival * Quaternion.Inverse(entryPad.rotation);
            }
        }
        public Vector3 EntryPad => entryPad.position;
        public Vector3 ExitPad => exitPad.position;

        internal NativeRaidPortal(string id, Transform entrance, Transform exit, Vector2 size, RaidFloor entryFloor,
            RaidFloor exitFloor, bool allowsCart, RaidPortalKind entryKind, RaidPortalKind exitKind,
            Transform entryPad, Transform exitPad, float doorInset)
        {
            Id = id;
            Entrance = entrance;
            Exit = exit;
            HalfWidth = size.x / 2;
            HalfHeight = size.y / 2;
            AllowsCart = allowsCart;
            EntryFloor = entryFloor;
            ExitFloor = exitFloor;
            EntryKind = entryKind;
            ExitKind = exitKind;
            this.entryPad = entryPad;
            this.exitPad = exitPad;
            DoorInset = doorInset;
        }

        public Vector3 MapPoint(Vector3 point) => Exit.position + Rotation * (point - Entrance.position);

        internal bool Crossed(Vector3 lastFeet, Vector3 feet, float radius, float height)
        {
            if (EntryKind != RaidPortalKind.Door)
            {
                // The upper hatch accepts a rising head at the mouth. At a lower hatch the
                // feet enter first. Cutting the sensor inward gives PhysX a small skin margin.
                Vector3 lead = EntryKind == RaidPortalKind.Upper ?
                    EntryFloor.Up * (RaidAssembly.Character.Height - .10f) : Vector3.zero;
                float plane = EntryKind == RaidPortalKind.Upper ? -.14f : -.025f;
                Vector3 before = Entrance.InverseTransformPoint(lastFeet + lead);
                Vector3 after = Entrance.InverseTransformPoint(feet + lead);
                if (before.z >= plane || after.z < plane) return false;
                Vector3 center = Vector3.Lerp(before, after, (plane - before.z) / (after.z - before.z));
                return Mathf.Abs(center.x) + radius < HalfWidth && Mathf.Abs(center.y) + radius < HalfHeight;
            }
            Vector3 last = Entrance.InverseTransformPoint(lastFeet);
            Vector3 current = Entrance.InverseTransformPoint(feet);
            if (last.z >= -DoorInset || current.z < -DoorInset) return false;
            Vector3 atDoor = Vector3.Lerp(last, current, (-DoorInset - last.z) / (current.z - last.z));
            return Mathf.Abs(atDoor.x) + radius < HalfWidth && atDoor.y >= -HalfHeight - .12f &&
                atDoor.y + height < HalfHeight;
        }
    }
}

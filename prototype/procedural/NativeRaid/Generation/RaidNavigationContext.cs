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
        public int Seed { get; }
        public NavigationRuntime Navigation { get; }
        public IReadOnlyList<NativeRaidPortal> Portals { get; }

        internal RaidNavigationContext(int seed, NavigationRuntime navigation, BakedRoom[] rooms, NativeRaidPortal[] portals)
        {
            Seed = seed;
            Navigation = navigation;
            this.rooms = rooms;
            Portals = Array.AsReadOnly(portals);
        }

        public bool Contains(Vector3 position) => TryGetFloor(position, out _);

        public bool TryGetFloor(Vector3 feet, out RaidFloor floor)
        {
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
        public RaidFloor EntryFloor { get; }
        public RaidFloor ExitFloor { get; }
        public Quaternion Rotation => Exit.rotation * Quaternion.AngleAxis(180f, Vector3.up) * Quaternion.Inverse(Entrance.rotation);
        public Vector3 EntryPad => Entrance.position - Entrance.up * HalfHeight - Entrance.forward * RaidAssembly.PortalSetback;
        public Vector3 ExitPad => Exit.position - Exit.up * HalfHeight - Exit.forward * RaidAssembly.PortalSetback;

        internal NativeRaidPortal(string id, Transform entrance, Transform exit, Vector2 size, RaidFloor entryFloor,
            RaidFloor exitFloor, bool allowsCart)
        {
            Id = id;
            Entrance = entrance;
            Exit = exit;
            HalfWidth = size.x / 2;
            HalfHeight = size.y / 2;
            AllowsCart = allowsCart;
            EntryFloor = entryFloor;
            ExitFloor = exitFloor;
        }

        public Vector3 MapPoint(Vector3 point) => Exit.position + Rotation * (point - Entrance.position);

        internal bool Crossed(Vector3 lastFeet, Vector3 feet, float radius, float height)
        {
            Vector3 last = Entrance.InverseTransformPoint(lastFeet);
            Vector3 current = Entrance.InverseTransformPoint(feet);
            if (last.z >= 0 || current.z < 0) return false;
            Vector3 atDoor = Vector3.Lerp(last, current, -last.z / (current.z - last.z));
            return Mathf.Abs(atDoor.x) + radius < HalfWidth && atDoor.y >= -HalfHeight - .12f &&
                atDoor.y + height < HalfHeight;
        }
    }
}

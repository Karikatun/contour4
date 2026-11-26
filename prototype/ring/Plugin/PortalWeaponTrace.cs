using System;
using Contour4.NativeRaid.Runtime;
using UnityEngine;
using GrabDoor = SpaceJourney.PortalGrab.GrabCrossing;

namespace SpaceJourney;

internal static class PortalWeaponTrace
{
    internal static bool Active
    {
        get
        {
            var generator = LevelGenerator.Instance;
            return GameManager.instance && generator && generator.LevelParent &&
                (Contour4.IsProceduralRaid(generator) || Contour4.IsSoloRing(generator));
        }
    }

    internal readonly struct Route(GrabDoor door, Vector3 origin, Vector3 direction, float distance, float range)
    {
        internal GrabDoor Door { get; } = door;
        internal Vector3 Origin { get; } = origin;
        internal Vector3 Direction { get; } = direction;
        internal float Distance { get; } = distance;
        internal float Remaining { get; } = range - distance;
        internal Vector3 Entrance => Origin + Direction * Distance;
        internal Vector3 Exit => Door.MapPoint(Entrance);
        internal Vector3 ExitDirection => Door.Rotation * Direction;
        internal Vector3 ExitNormal => Door.Ring is Crossing ring ? ring.Exit.Normal : Door.Exit.forward;
        internal Vector3 End => Exit + ExitDirection * Remaining;
    }

    internal static bool TryRoute(Vector3 origin, Vector3 direction, float range, out Route route)
    {
        route = default;
        if (!Active || range <= 0f || direction.sqrMagnitude < .00001f)
            return false;

        direction.Normalize();
        GrabDoor? closest = null;
        float distance = range;
        var generator = LevelGenerator.Instance;
        var ring = RingRuntime.Active;
        if (ring && ring.transform.IsChildOf(generator.LevelParent.transform))
            foreach (Portal pair in ring.Portals)
            {
                Consider(new GrabDoor(new Crossing(pair.A, pair.B)));
                Consider(new GrabDoor(new Crossing(pair.B, pair.A)));
            }

        var raid = ProceduralPortalPresentation.Grab;
        if (Contour4.IsProceduralRaid(generator) && raid?.Module && raid.Module.isActiveAndEnabled &&
            raid.Module.transform.IsChildOf(generator.LevelParent.transform))
            foreach (NativeRaidPortal portal in raid.Portals)
                Consider(new GrabDoor(portal));

        if (closest is not GrabDoor door)
            return false;

        route = new Route(door, origin, direction, distance, range);
        return true;

        void Consider(GrabDoor candidate)
        {
            if (!candidate.Entry || !candidate.Exit || !candidate.TryRay(origin, direction, distance, out float hit))
                return;
            closest = candidate;
            distance = hit;
        }
    }

    internal static bool InsideExit(GrabDoor crossing, Vector3 point)
    {
        if (crossing.Ring is Crossing ring)
            return ring.Exit.InAperture(point);

        Vector3 local = crossing.Exit.InverseTransformPoint(point);
        foreach (NativeRaidPortal reverse in ProceduralPortalPresentation.Grab.Portals)
            if (reverse.Entrance == crossing.Exit && reverse.Exit == crossing.Entry)
                return Mathf.Abs(local.x) < reverse.HalfWidth && Mathf.Abs(local.y) < reverse.HalfHeight;

        return false;
    }

    internal static bool Enclosed(Vector3 point, int mask, QueryTriggerInteraction triggers, Predicate<Collider> accepts = null)
    {
        // Unity launch rays omit shapes that already contain their artificial exit origin.
        foreach (Collider collider in Physics.OverlapSphere(point, .001f, mask, triggers))
            if (collider is BoxCollider or SphereCollider or CapsuleCollider or MeshCollider { convex: true } &&
                (collider.ClosestPoint(point) - point).sqrMagnitude < 1e-10f &&
                (accepts == null || accepts(collider)))
                return true;
        return false;
    }
}

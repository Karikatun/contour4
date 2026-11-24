using System;
using System.Linq;
using Contour4.NativeRaid;
using UnityEngine;

namespace SpaceJourney;

internal readonly struct Crossing
{
    internal PortalEnd Entry { get; }
    internal PortalEnd Exit { get; }
    internal Quaternion Rotation =>
        Exit.Orientation * Quaternion.AngleAxis(180f, Vector3.up) * Quaternion.Inverse(Entry.Orientation);

    internal Crossing(PortalEnd entry, PortalEnd exit)
    {
        Entry = entry;
        Exit = exit;
    }

    internal Vector3 TransformPoint(Vector3 point) => Exit.Center + Rotation * (point - Entry.Center);
    internal Crossing Reverse() => new(Exit, Entry);
}

internal sealed class PortalEnd
{
    internal Transform Room { get; }
    internal Transform Marker { get; }
    internal bool Upper { get; }
    internal float Width => (Upper ? 3.5f : 3.6f) * Marker.lossyScale.x;
    internal float Height => (Upper ? 2.75f : 3.2f) * Marker.lossyScale.y;
    internal Vector3 Normal => Upper ? Room.up : Marker.forward;
    internal Vector3 ScreenUp => Upper ? Marker.forward : Room.up;
    internal Quaternion Orientation => Quaternion.LookRotation(Normal, ScreenUp);
    internal Vector3 Center => Marker.position + (Upper ? Vector3.zero : ScreenUp * Height * .5f);

    internal PortalEnd(Transform room, string target, bool upper)
    {
        Room = room;
        Upper = upper;
        Marker = room.Find("Markers").Cast<Transform>().Single(marker =>
            marker.name.EndsWith("_" + target, StringComparison.Ordinal) &&
            marker.name.Contains("__Upper_") == upper);
    }

    internal CargoMouth CargoMouth => new(Center, Orientation, Width * .5f, Height * .5f);

    internal bool Crossed(Vector3 start, Vector3 finish) => CargoMouth.CrossingFraction(start, finish, out _);

    internal bool HasCargoSupport(Vector3 sill)
    {
        return Physics.Raycast(sill + ScreenUp * .14f, -ScreenUp, out var hit, .30f,
            LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore) &&
            (hit.collider.transform.IsChildOf(Room) || hit.collider.transform.IsChildOf(Marker)) &&
            Vector3.Dot(hit.normal, ScreenUp) >= .8f;
    }

    internal bool InAperture(Vector3 point)
    {
        Vector3 aperture = Quaternion.Inverse(Orientation) * (point - Center);
        return Mathf.Abs(aperture.x) < Width * .5f && Mathf.Abs(aperture.y) < Height * .5f;
    }

    internal bool TryRay(Vector3 origin, Vector3 direction, float range, out float distance)
    {
        float facing = Vector3.Dot(direction, Normal);
        float depth = DistanceFromPlane(origin);
        distance = facing > .00001f ? -depth / facing : -1;
        return distance >= 0 && distance < range && InAperture(origin + direction * distance);
    }

    internal bool Straddles(Bounds bounds)
    {
        Quaternion frame = Orientation;
        Vector3 local = Quaternion.Inverse(frame) * (bounds.center - Center);
        return Mathf.Abs(local.z) <= ExtentAlong(bounds, Normal) &&
            Mathf.Abs(local.x) <= Width * .5f + ExtentAlong(bounds, frame * Vector3.right) &&
            Mathf.Abs(local.y) <= Height * .5f + ExtentAlong(bounds, ScreenUp);
    }

    private static float ExtentAlong(Bounds bounds, Vector3 axis) => Vector3.Dot(bounds.extents,
        new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)));

    internal float DistanceFromPlane(Vector3 point) => Vector3.Dot(point - Center, Normal);

    internal bool NearAperture(Vector3 point)
    {
        Vector3 aperture = Quaternion.Inverse(Orientation) * (point - Center);
        return Mathf.Abs(aperture.x) <= Width * .5f + .75f && Mathf.Abs(aperture.y) <= Height * .5f + .75f &&
            Mathf.Abs(aperture.z) <= 2.5f;
    }

    internal Vector3 FindLandingCenter(Vector3 center, float height)
    {
        var up = Room.up;
        var model = Room.Find("Model");
        var ray = new Ray(center + up * 1.4f, -up);
        RaycastHit[] hits = Physics.RaycastAll(ray, 4f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits.OrderBy(hit => hit.distance))
        {
            if (hit.collider.transform.IsChildOf(model) && Vector3.Dot(hit.normal, up) > .55f)
                return hit.point + up * (height + .035f);
        }
        return center;
    }
}

internal sealed class Portal
{
    internal PortalEnd A { get; }
    internal PortalEnd B { get; }

    private Portal(PortalEnd a, PortalEnd b)
    {
        A = a;
        B = b;
    }

    internal static Portal Pair(Transform a, string aTarget, Transform b, string bTarget, bool upper) =>
        new(new PortalEnd(a, aTarget, upper), new PortalEnd(b, bTarget, upper));

    internal bool TryCross(Vector3 start, Vector3 finish, PortalEnd blockedEnd, out Crossing crossing)
    {
        if (A != blockedEnd && A.Crossed(start, finish))
        {
            crossing = new Crossing(A, B);
            return true;
        }
        if (B != blockedEnd && B.Crossed(start, finish))
        {
            crossing = new Crossing(B, A);
            return true;
        }
        crossing = default;
        return false;
    }
}

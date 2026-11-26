using System.Collections.Generic;
using Contour4.NativeRaid;
using Contour4.NativeRaid.Runtime;
using HarmonyLib;
using UnityEngine;

namespace SpaceJourney;

internal sealed class PortalHeldCargo(IReadOnlyList<NativeRaidPortal> doors, PortalGrab.MappedHold hold)
{
    // Each side has its own threshold beyond the aperture. The mapped arrival stays
    // that far inside the exit, leaving a 16 cm gap before a deliberate return.
    private const float CrossingDepth = .08f;
    private static readonly AccessTools.FieldRef<PhysGrabber, PhysGrabObject> HeldObject =
        AccessTools.FieldRefAccess<PhysGrabber, PhysGrabObject>("grabbedPhysGrabObject");
    private static readonly AccessTools.FieldRef<PhysGrabber, int> GrabRevision =
        AccessTools.FieldRefAccess<PhysGrabber, int>("grabRevision");
    private static readonly AccessTools.FieldRef<PhysGrabber, Vector3> LocalGrabPoint =
        AccessTools.FieldRefAccess<PhysGrabber, Vector3>("localGrabPosition");
    private static readonly AccessTools.FieldRef<PhysGrabber, Vector3> GrabForce =
        AccessTools.FieldRefAccess<PhysGrabber, Vector3>("currentGrabForce");
    private static readonly AccessTools.FieldRef<PhysGrabber, Vector3> GrabTorque =
        AccessTools.FieldRefAccess<PhysGrabber, Vector3>("currentTorqueForce");

    private readonly HashSet<Rigidbody> moving = new();
    private Rigidbody tracked;
    private int revision;
    private Vector3 previous;
    private NativeRaidPortal blocked;
    private NativeRaidPortal pending;
    private Vector3 pendingPoint;
    private bool eligible;

    internal void Step(RaidAssembly raid, Rigidbody player)
    {
        PhysGrabber grabber = PhysGrabber.instance;
        if (!player || !grabber || !grabber.isLocal || !grabber.grabbed || !grabber.physGrabPointPuller ||
            HeldObject(grabber) is not { } item || !item || item.dead || !item.rb)
        {
            tracked = null;
            blocked = null;
            pending = null;
            return;
        }

        Rigidbody body = item.rb;
        Vector3 center = body.worldCenterOfMass;
        int stockRevision = GrabRevision(grabber);
        if (tracked != body || revision != stockRevision)
        {
            tracked = body;
            revision = stockRevision;
            previous = raid.transform.InverseTransformPoint(center);
            blocked = null;
            pending = null;
            eligible = PortalGrab.IsHandCargo(body);
            return;
        }
        Vector3 last = previous;
        previous = raid.transform.InverseTransformPoint(center);
        if (!eligible || body.isKinematic || !body.detectCollisions ||
            !PortalCargoDoor.Continuous(raid.transform, last, center, out Vector3 start))
        {
            pending = null;
            return;
        }

        bool mapped = hold.IsHeldBy(grabber) && hold.Door?.Raid != null;
        if (mapped && hold.Expired)
        {
            pending = null;
            return;
        }
        NativeRaidPortal route = mapped ? hold.Door.Value.Raid : null;
        Vector3 pull = grabber.physGrabPointPuller.position;
        Vector3 grabPoint = body.transform.TransformPoint(LocalGrabPoint(grabber));
        Vector3 target = (mapped ? route.MapPoint(pull) : pull) + center - grabPoint;
        if (blocked != null && blocked.Entrance.InverseTransformPoint(target).z < -CrossingDepth)
            blocked = null;
        if (pending != null && !CanUse(pending, route, player.worldCenterOfMass))
            pending = null;
        if (pending == null)
        {
            pending = FindCrossing(player.worldCenterOfMass, start, center, route, out float fraction);
            if (pending == null)
                return;
            pendingPoint = pending.Entrance.InverseTransformPoint(Vector3.LerpUnclamped(start, center, fraction));
        }

        var entry = pending;
        var disposition = ResolvePending(entry.Entrance.InverseTransformPoint(center).z,
            entry.Entrance.InverseTransformPoint(target).z);
        if (disposition == PendingDisposition.Wait)
            return;
        pending = null;
        if (disposition == PendingDisposition.Retreat || FindReverse(entry) is not { } exit)
            return;

        moving.Add(body);
        var result = PortalCargoDoor.Traverse(Mouth(entry, 0f), Mouth(exit, 0f), entry.Rotation,
            body, null, moving, entry.Entrance.TransformPoint(pendingPoint), false,
            holder: player, avatar: grabber.playerAvatar ? grabber.playerAvatar.transform : null);
        moving.Clear();
        previous = raid.transform.InverseTransformPoint(body.worldCenterOfMass);
        if (grabber.physGrabPoint)
            grabber.physGrabPointPosition = grabber.physGrabPoint.position =
                body.transform.TransformPoint(LocalGrabPoint(grabber));
        if (result != CargoTraversalResult.Moved)
        {
            blocked = entry;
            return;
        }

        blocked = null;
        GrabForce(grabber) = entry.Rotation * GrabForce(grabber);
        GrabTorque(grabber) = entry.Rotation * GrabTorque(grabber);
        NativeRaidGravity.RefreshActorFromRing(raid, body, false);
        if (mapped)
            hold.Clear();
        else
        {
            hold.Begin(grabber, item, new PortalGrab.GrabCrossing(entry));
            hold.Remap(grabber, retainExpiredRoute: true);
        }
    }

    internal enum PendingDisposition { Retreat, Wait, Traverse }

    internal static PendingDisposition ResolvePending(float bodyDepth, float targetDepth) =>
        bodyDepth < CrossingDepth ? PendingDisposition.Retreat :
        targetDepth < CrossingDepth ? PendingDisposition.Wait : PendingDisposition.Traverse;

    private NativeRaidPortal FindCrossing(Vector3 player, Vector3 start, Vector3 end, NativeRaidPortal route, out float fraction)
    {
        NativeRaidPortal entry = null;
        fraction = float.PositiveInfinity;
        foreach (var portal in doors)
        {
            if (!CanUse(portal, route, player) ||
                !Mouth(portal, CrossingDepth).CrossingFraction(start, end, out float t) || t >= fraction)
                continue;
            entry = portal;
            fraction = t;
        }
        return entry;
    }

    private bool CanUse(NativeRaidPortal portal, NativeRaidPortal route, Vector3 player) =>
        portal.Entrance && portal.Exit && portal != blocked && (route == null ?
            portal.Entrance.InverseTransformPoint(player).z < 0f :
            portal.Entrance == route.Exit && portal.Exit == route.Entrance);

    private NativeRaidPortal FindReverse(NativeRaidPortal entry)
    {
        foreach (var portal in doors)
            if (portal.Entrance == entry.Exit && portal.Exit == entry.Entrance)
                return portal;
        return null;
    }

    private static CargoMouth Mouth(NativeRaidPortal portal, float depth) =>
        new(portal.Entrance.position, portal.Entrance.rotation, portal.HalfWidth, portal.HalfHeight, depth);
}

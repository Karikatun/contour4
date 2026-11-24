using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Contour4.NativeRaid.Runtime;
using HarmonyLib;
using UnityEngine;
using GrabContext = SpaceJourney.ProceduralPortalPresentation.GrabContext;

namespace SpaceJourney;

internal static class PortalGrab
{
    // This state belongs to the synchronous local RayCheck call, including its native grab start.
    private static PhysGrabber rayOwner;
    private static GrabCrossing? rayCrossing;
    private static Rigidbody rayBody;
    private static Vector3 rayOrigin;
    private static GrabContext rayRaid;

    [HarmonyPatch(typeof(PhysGrabber), "RayCheck")]
    private static class NativeRay
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var ray = AccessTools.Method(typeof(Physics), nameof(Physics.Raycast), new[]
            {
                typeof(Vector3), typeof(Vector3), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int),
                typeof(QueryTriggerInteraction)
            });
            bool replaced = false;
            foreach (var instruction in instructions)
            {
                // The first ray selects a PhysGrabObject. The later ray implements climbing.
                if (!replaced && instruction.Calls(ray))
                {
                    var owner = new CodeInstruction(OpCodes.Ldarg_0);
                    instruction.MoveLabelsTo(owner);
                    yield return owner;
                    instruction.operand = AccessTools.Method(typeof(PortalGrab), nameof(Cast));
                    replaced = true;
                }
                yield return instruction;
            }
            if (!replaced)
                throw new InvalidOperationException("Contour-4 could not locate the native physics grab ray.");
        }

        private static void Finalizer()
        {
            rayOwner = null;
            rayCrossing = null;
            rayBody = null;
            rayRaid = null;
        }
    }

    [HarmonyPatch(typeof(PhysGrabber), "StartGrabbingPhysObject")]
    private static class NativeGrabStart
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var distance = AccessTools.Method(typeof(Vector3), nameof(Vector3.Distance), new[] { typeof(Vector3), typeof(Vector3) });
            bool replaced = false;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(distance))
                {
                    instruction.operand = AccessTools.Method(typeof(PortalGrab), nameof(PullDistance));
                    replaced = true;
                }
                yield return instruction;
            }
            if (!replaced)
                throw new InvalidOperationException("Contour-4 could not locate the native initial pull distance.");
        }

        private static void Postfix(PhysGrabber __instance, PhysGrabObject ___grabbedPhysGrabObject)
        {
            if (rayOwner != __instance || rayCrossing is not GrabCrossing crossing || !__instance.grabbed ||
                !___grabbedPhysGrabObject || ___grabbedPhysGrabObject.rb != rayBody)
                return;

            if (crossing.Raid != null && rayRaid != null && rayRaid == ProceduralPortalPresentation.Grab && IsCurrentRaid(rayRaid))
            {
                rayRaid.Hold.Begin(__instance, ___grabbedPhysGrabObject, crossing);
                rayRaid.Hold.Remap(__instance, retainExpiredRoute: true);
            }
            else if (crossing.Ring is Crossing ringCrossing && RingRuntime.Active)
                RingRuntime.Active.BeginMappedHold(__instance, ___grabbedPhysGrabObject, ringCrossing);
        }
    }

    private static bool Cast(Vector3 origin, Vector3 direction, out RaycastHit hit, float range, int mask,
        QueryTriggerInteraction triggers, PhysGrabber grabber)
    {
        if (!grabber.isLocal)
            return Physics.Raycast(origin, direction, out hit, range, mask, triggers);

        var ring = RingRuntime.Active;
        var raid = CurrentRaid();
        if (!ring && (raid == null || raid.Doors.Count == 0))
            return Physics.Raycast(origin, direction, out hit, range, mask, triggers);

        if (!Trace(origin, direction, out hit, range, mask, triggers, ring, raid, out var crossing, out var pullOrigin))
            return false;

        if (crossing is GrabCrossing mapped && mapped.Raid != null && !IsHandCargo(hit.rigidbody))
        {
            hit = default;
            return false;
        }

        if (crossing.HasValue && hit.rigidbody)
        {
            rayOwner = grabber;
            rayCrossing = crossing;
            rayBody = hit.rigidbody;
            rayOrigin = pullOrigin;
            rayRaid = raid;
        }
        return true;
    }

    private static bool IsHandCargo(Rigidbody body) =>
        body && !body.isKinematic && body.detectCollisions && body.TryGetComponent(out PhysGrabObject item) &&
        !item.dead && !item.GetComponentInChildren<PhysGrabCart>() && !item.GetComponent<Joint>();

    private static GrabContext CurrentRaid()
    {
        var raid = ProceduralPortalPresentation.Grab;
        return IsCurrentRaid(raid) ? raid : null;
    }

    private static bool IsCurrentRaid(GrabContext raid)
    {
        if (raid == null || !raid.Module || !raid.Module.isActiveAndEnabled)
            return false;
        var generator = LevelGenerator.Instance;
        return Contour4.IsProceduralRaid(generator) && generator.LevelParent &&
            raid.Module.transform.IsChildOf(generator.LevelParent.transform);
    }

    internal static bool RemapRaidPull(PhysGrabber grabber)
    {
        var raid = ProceduralPortalPresentation.Grab;
        if (raid == null || !raid.Hold.Door.HasValue)
            return false;
        if (IsCurrentRaid(raid))
        {
            raid.Hold.Remap(grabber, retainExpiredRoute: true);
            return raid.Hold.IsHeldBy(grabber);
        }

        raid.Hold.Clear();
        return false;
    }

    private static float PullDistance(Vector3 camera, Vector3 target) =>
        Vector3.Distance(rayCrossing is GrabCrossing crossing ? crossing.MapPoint(rayOrigin) : camera, target);

    private static bool Trace(Vector3 origin, Vector3 direction, out RaycastHit hit, float range, int mask,
        QueryTriggerInteraction triggers, RingRuntime ring, GrabContext raid, out GrabCrossing? mapped,
        out Vector3 pullOrigin)
    {
        GrabCrossing? eyeDoor = null;
        if (GameDirector.instance && SemiFunc.MainCamera() is Camera camera && camera)
        {
            // VisionTransform can lag behind the rendered eye at a jamb.
            float nearCorner = RingRuntime.NearCorner(camera);
            Vector3 rendered = ring ? ring.UnshiftedGrabEye(camera) : raid.Views.UnshiftedEye(camera);
            eyeDoor = CameraSide(ring, raid, rendered, nearCorner);
            if (eyeDoor?.Entry != CameraSide(ring, raid, origin, nearCorner)?.Entry)
                origin = rendered;
        }

        pullOrigin = origin;
        if (eyeDoor is GrabCrossing eye)
        {
            origin = eye.MapPoint(origin);
            direction = eye.Rotation * direction;
        }
        mapped = eyeDoor;

        GrabCrossing? nearest = null;
        float reach = range;
        void Consider(GrabCrossing door)
        {
            if (door.TryRay(origin, direction, reach, out float distance))
            {
                nearest = door;
                reach = distance;
            }
        }
        if (ring)
            for (int index = 0; index < ring.GrabDoors.Count; index++)
                Consider(new GrabCrossing(ring.GrabDoors[index]));
        if (raid != null)
            for (int index = 0; index < raid.Doors.Count; index++)
                Consider(new GrabCrossing(raid.Doors[index]));

        bool local = Physics.Raycast(origin, direction, out hit, reach, mask, triggers);
        if (ring && eyeDoor?.Raid == null && ring.TryHitExposedForGrab(origin, direction, local ? hit.distance : reach, mask, triggers,
            out var exposed, out var exposedCrossing))
        {
            hit = exposed;
            mapped = ResolveGrabSide(eyeDoor, new GrabCrossing(exposedCrossing));
            return true;
        }
        if (local || nearest is not GrabCrossing crossing)
            return local;

        // A near-plane handoff already used one link. Only its reverse can cancel that mapping.
        if (eyeDoor is GrabCrossing previous && previous.Exit != crossing.Entry &&
            (previous.Raid != null || crossing.Raid != null))
            return false;

        Vector3 remoteOrigin = crossing.MapPoint(origin + direction * reach);
        Vector3 remoteDirection = crossing.Rotation * direction;
        // A stock ray ignores shapes enclosing its origin. At the artificial segment start
        // a convex obstruction spanning the exit still blocks the beam.
        foreach (var collider in Physics.OverlapSphere(remoteOrigin, .001f, mask, triggers))
            if (collider is BoxCollider or SphereCollider or CapsuleCollider or MeshCollider { convex: true } &&
                (collider.ClosestPoint(remoteOrigin) - remoteOrigin).sqrMagnitude < 1e-10f)
                return false;
        if (!Physics.Raycast(remoteOrigin, remoteDirection, out hit, range - reach, mask, triggers))
            return false;

        hit.distance += reach;
        mapped = ResolveGrabSide(eyeDoor, crossing);
        return true;
    }

    private static GrabCrossing? CameraSide(RingRuntime ring, GrabContext raid, Vector3 eye, float nearCorner)
    {
        if (ring && ring.CameraSide(eye, nearCorner) is Crossing ringDoor)
            return new GrabCrossing(ringDoor);
        if (raid?.Module.CameraSide(eye, nearCorner) is NativeRaidPortal raidDoor && raid.Doors.Contains(raidDoor))
            return new GrabCrossing(raidDoor);
        return null;
    }

    private static GrabCrossing? ResolveGrabSide(GrabCrossing? eyeDoor, GrabCrossing hitDoor) =>
        eyeDoor is GrabCrossing eye && eye.Exit == hitDoor.Entry ? null : hitDoor;

    internal readonly struct GrabCrossing
    {
        internal Crossing? Ring { get; }
        internal NativeRaidPortal Raid { get; }
        internal Transform Entry => Ring is Crossing ring ? ring.Entry.Marker : Raid.Entrance;
        internal Transform Exit => Ring is Crossing ring ? ring.Exit.Marker : Raid.Exit;
        internal Vector3 Center => Ring is Crossing ring ? ring.Entry.Center : Raid.Entrance.position;
        internal Quaternion Rotation => Ring is Crossing ring ? ring.Rotation : Raid.Rotation;

        internal GrabCrossing(Crossing ring) { Ring = ring; Raid = null; }
        internal GrabCrossing(NativeRaidPortal raid) { Ring = null; Raid = raid; }

        internal Vector3 MapPoint(Vector3 point) => Ring is Crossing ring ? ring.TransformPoint(point) : Raid.MapPoint(point);

        internal bool TryRay(Vector3 origin, Vector3 direction, float range, out float distance)
        {
            if (Ring is Crossing ring)
                return ring.Entry.TryRay(origin, direction, range, out distance);

            Transform entrance = Raid.Entrance;
            float depth = Vector3.Dot(origin - entrance.position, entrance.forward);
            float facing = Vector3.Dot(direction, entrance.forward);
            distance = facing > .00001f ? -depth / facing : -1f;
            if (distance < 0f || distance >= range)
                return false;

            Vector3 aperture = entrance.InverseTransformPoint(origin + direction * distance);
            return Mathf.Abs(aperture.x) < Raid.HalfWidth && Mathf.Abs(aperture.y) < Raid.HalfHeight;
        }
    }

    internal sealed class MappedHold
    {
        private static readonly AccessTools.FieldRef<PhysGrabber, PhysGrabObject> HeldObject =
            AccessTools.FieldRefAccess<PhysGrabber, PhysGrabObject>("grabbedPhysGrabObject");

        private PhysGrabber owner;
        private bool mapped;
        internal PhysGrabObject Held { get; private set; }
        internal GrabCrossing? Door { get; private set; }
        internal bool Expired { get; private set; }

        internal void Begin(PhysGrabber grabber, PhysGrabObject held, GrabCrossing crossing)
        {
            if (owner != grabber)
                Clear();
            owner = grabber;
            Held = held;
            Door = crossing;
            mapped = false;
            Expired = false;
            grabber.OverrideAlwaysGrabbable(.10f);
        }

        internal bool IsHeldBy(PhysGrabber grabber) =>
            owner && owner == grabber && owner.grabbed && Held && Held.rb && HeldObject(owner) == Held;

        internal bool Remap(PhysGrabber grabber, bool retainExpiredRoute = false)
        {
            if (Door is not GrabCrossing crossing)
                return false;

            var player = PlayerController.instance;
            if (!IsHeldBy(grabber) || !player || !player.rb || !grabber.physGrabPointPuller ||
                !crossing.Entry || !crossing.Exit)
            {
                Clear();
                return false;
            }

            if (Expired)
                return false;

            Vector3 playerCenter = player.rb.worldCenterOfMass;
            Vector3 virtualPlayer = crossing.MapPoint(playerCenter);
            if (Vector3.Distance(playerCenter, crossing.Center) > grabber.grabReleaseDistance ||
                Vector3.Distance(Held.rb.worldCenterOfMass, virtualPlayer) > grabber.grabReleaseDistance)
            {
                if (retainExpiredRoute)
                {
                    // Stock may take another Update to release. Until then this is still far-side cargo.
                    RestoreNativePull();
                    Expired = true;
                }
                else
                    Clear();
                return false;
            }

            // Keep the native transform in the controller frame.
            grabber.physGrabPointPullerPosition = crossing.MapPoint(grabber.physGrabPointPuller.position);
            grabber.OverrideAlwaysGrabbable(.10f);
            mapped = true;
            return true;
        }

        internal void Clear(bool preservePullPoint = false)
        {
            if (!preservePullPoint)
                RestoreNativePull();
            owner = null;
            Held = null;
            Door = null;
            mapped = false;
            Expired = false;
        }

        private void RestoreNativePull()
        {
            if (mapped && owner && owner.physGrabPointPuller)
                owner.physGrabPointPullerPosition = owner.physGrabPointPuller.position;
            mapped = false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SpaceJourney;

internal static class PortalGrab
{
    // This state belongs to the synchronous local RayCheck call, including its native grab start.
    private static PhysGrabber rayOwner;
    private static Crossing? rayCrossing;
    private static Rigidbody rayBody;
    private static Vector3 rayOrigin;

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
            if (rayOwner == __instance && rayCrossing is Crossing crossing && __instance.grabbed &&
                ___grabbedPhysGrabObject && ___grabbedPhysGrabObject.rb == rayBody && RingRuntime.Active)
                RingRuntime.Active.BeginMappedHold(__instance, ___grabbedPhysGrabObject, crossing);
        }
    }

    private static bool Cast(Vector3 origin, Vector3 direction, out RaycastHit hit, float range, int mask,
        QueryTriggerInteraction triggers, PhysGrabber grabber)
    {
        var ring = RingRuntime.Active;
        if (!ring || !grabber.isLocal)
            return Physics.Raycast(origin, direction, out hit, range, mask, triggers);
        if (!ring.TraceGrab(origin, direction, out hit, range, mask, triggers, out Crossing? crossing, out Vector3 pullOrigin))
            return false;
        if (crossing.HasValue && hit.rigidbody)
        {
            rayOwner = grabber;
            rayCrossing = crossing;
            rayBody = hit.rigidbody;
            rayOrigin = pullOrigin;
        }
        return true;
    }

    private static float PullDistance(Vector3 camera, Vector3 target) =>
        Vector3.Distance(rayCrossing is Crossing crossing ? crossing.TransformPoint(rayOrigin) : camera, target);
}

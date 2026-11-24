using HarmonyLib;
using UnityEngine;

namespace Contour4.NativeRaid;

internal sealed class PortalDoorDebris
{
    private const float Approach = 1.05f;
    private const float ContactDepth = .15f;
    private const float InteractionRange = 2.6f;
    private static readonly AccessTools.FieldRef<PhysGrabHinge, bool> Broken =
        AccessTools.FieldRefAccess<PhysGrabHinge, bool>("broken");

    private readonly Collider[] overlaps = new Collider[32];
    private float nextCheck;

    internal bool NeedsCheck()
    {
        if (Time.fixedTime < nextCheck || GameManager.Multiplayer())
            return false;

        nextCheck = Time.fixedTime + .10f;
        return true;
    }

    internal void ClearNear(Vector3 observer, Vector3 center, Quaternion orientation, float halfWidth, float halfHeight,
        Vector3 linkedCenter, Quaternion linkedOrientation)
    {
        Vector3 local = Quaternion.Inverse(orientation) * (observer - center);
        if (Mathf.Abs(local.x) > halfWidth + .65f || Mathf.Abs(local.y) > halfHeight + 1f ||
            local.z < -InteractionRange || local.z > ContactDepth)
            return;

        ClearOpening(center, orientation, halfWidth, halfHeight);
        ClearOpening(linkedCenter, linkedOrientation, halfWidth, halfHeight);
    }

    private void ClearOpening(Vector3 center, Quaternion orientation, float halfWidth, float halfHeight)
    {
        // Protect the doorway and its checked landing strip, including the room-facing side
        // of a floor hatch. The rest of the room keeps normal debris collisions.
        Vector3 outward = orientation * Vector3.forward;
        Vector3 boxCenter = center + outward * ((ContactDepth - Approach) * .5f);
        var halfSize = new Vector3(halfWidth - .04f, halfHeight - .02f, (Approach + ContactDepth) * .5f);
        int mask = LayerMask.GetMask("Default", "PhysGrabObject", "PhysGrabObjectHinge");
        int count = Physics.OverlapBoxNonAlloc(boxCenter, halfSize, overlaps, orientation, mask, QueryTriggerInteraction.Ignore);
        for (int index = 0; index < count; index++)
        {
            var part = overlaps[index];
            overlaps[index] = null;
            var body = part ? part.attachedRigidbody : null;
            if (!body || body.isKinematic || !body.detectCollisions ||
                !body.TryGetComponent(out PhysGrabObject actor) ||
                actor.grabbed || actor.grabbedLocal || actor.playerGrabbing.Count > 0 ||
                !IsDetachedDoor(actor))
                continue;

            // This is broken scenery. Retiring it via the native path avoids dropping it
            // into another cargo lane or outside the map. Physics clears it this same step.
            body.detectCollisions = false;
            actor.DestroyPhysGrabObject();
        }
    }

    private static bool IsDetachedDoor(PhysGrabObject actor)
    {
        if (actor.TryGetComponent(out PhysGrabHinge hinge))
            return Broken(hinge) && !actor.GetComponent<HingeJoint>();
        return actor.TryGetComponent(out NativeRaidCabinetMount mount) &&
            mount.Joint is HingeJoint && !mount.IsAnchored;
    }
}

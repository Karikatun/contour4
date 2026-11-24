using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Contour4.NativeRaid;

internal readonly struct CargoMouth(Vector3 center, Quaternion rotation, float halfWidth, float halfHeight, float depth = 0f)
{
    internal Vector3 Center => center;
    internal Quaternion Rotation => rotation;
    internal Vector3 Forward => rotation * Vector3.forward;
    internal Vector3 Up => rotation * Vector3.up;
    internal float HalfWidth => halfWidth;
    internal float HalfHeight => halfHeight;
    internal float Depth => depth;

    internal Vector3 Point(Vector3 local) => center + rotation * local;
    internal Vector3 Local(Vector3 point) => Quaternion.Inverse(rotation) * (point - center);
    internal Bounds Local(Bounds bounds) => new(Local(bounds.center), 2f * new Vector3(
        PortalCargoDoor.Extent(bounds, rotation * Vector3.right), PortalCargoDoor.Extent(bounds, Up),
        PortalCargoDoor.Extent(bounds, Forward)));

    internal Bounds Local(Collider collider)
    {
        if (collider is BoxCollider box) return Local(box.transform, new Bounds(box.center, box.size));
        if (collider is MeshCollider mesh && mesh.sharedMesh) return Local(mesh.transform, mesh.sharedMesh.bounds);
        if (collider is SphereCollider)
            return new Bounds(Local(collider.bounds.center), collider.bounds.size);
        return Local(collider.bounds);
    }

    private Bounds Local(Transform transform, Bounds shape)
    {
        Quaternion inverse = Quaternion.Inverse(rotation);
        Vector3 x = inverse * transform.TransformVector(Vector3.right * shape.extents.x);
        Vector3 y = inverse * transform.TransformVector(Vector3.up * shape.extents.y);
        Vector3 z = inverse * transform.TransformVector(Vector3.forward * shape.extents.z);
        var size = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
            Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
            Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2f;
        return new Bounds(Local(transform.TransformPoint(shape.center)), size);
    }

    internal bool CrossingFraction(Vector3 start, Vector3 finish, out float fraction)
    {
        Vector3 before = Local(start);
        Vector3 after = Local(finish);
        fraction = float.PositiveInfinity;
        if (before.z >= depth || after.z < depth) return false;
        float t = (depth - before.z) / (after.z - before.z);
        Vector3 at = Vector3.LerpUnclamped(before, after, t);
        if (Mathf.Abs(at.x) >= halfWidth || Mathf.Abs(at.y) >= halfHeight) return false;
        fraction = t;
        return true;
    }
}

internal enum CargoTraversalResult
{
    Moved,
    Retreated,
    Blocked
}

internal static class PortalCargoDoor
{
    private const float Clearance = .025f;
    internal const float MaxStep = 5f;

    private static readonly AccessTools.FieldRef<PhysGrabber, PhysGrabObject> HeldObject =
        AccessTools.FieldRefAccess<PhysGrabber, PhysGrabObject>("grabbedPhysGrabObject");
    private static readonly AccessTools.FieldRef<PhysGrabCart, Transform> Basket =
        AccessTools.FieldRefAccess<PhysGrabCart, Transform>("inCart");
    private static readonly AccessTools.FieldRef<PhysGrabCart, Rigidbody> CartBody =
        AccessTools.FieldRefAccess<PhysGrabCart, Rigidbody>("rb");

    internal static int ActorMask => LayerMask.GetMask("PhysGrabObject", "PhysGrabObjectCart");
    internal static float Extent(Bounds bounds, Vector3 axis) =>
        Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)));

    internal static bool Continuous(Transform frame, Vector3 previousLocal, Vector3 worldFinish, out Vector3 worldStart)
    {
        worldStart = frame.TransformPoint(previousLocal);
        return (worldFinish - worldStart).sqrMagnitude <= MaxStep * MaxStep;
    }

    internal static bool TryActor(Rigidbody body, out PhysGrabObject actor)
    {
        actor = null;
        return body && body.gameObject.activeInHierarchy && !body.isKinematic && body.detectCollisions &&
            body.TryGetComponent(out actor) && actor.isActiveAndEnabled && actor.rb == body && !actor.dead;
    }

    internal static bool IsHeld(PhysGrabObject actor)
    {
        var grabber = PhysGrabber.instance;
        return actor.grabbed || actor.grabbedLocal || actor.playerGrabbing.Count > 0 ||
            (grabber && grabber.grabbed && HeldObject(grabber) == actor);
    }

    internal static bool IsLocalHold(PhysGrabObject actor)
    {
        var grabber = PhysGrabber.instance;
        return grabber && grabber.isLocal && grabber.grabbed && HeldObject(grabber) == actor;
    }

    internal static PhysGrabCart CartFor(PhysGrabObject actor) => actor.GetComponentInChildren<PhysGrabCart>();

    internal static bool IsValidCart(PhysGrabCart cart, Rigidbody body) =>
        cart && cart.isActiveAndEnabled && CartBody(cart) == body && Basket(cart);

    internal static void CaptureBasket(PhysGrabCart cart, Rigidbody cartBody, HashSet<Rigidbody> cargo)
    {
        // Stock updates its item list on a timer. Use the present basket volume and support direction.
        Transform basket = Basket(cart);
        if (!basket) return;
        Vector3 half = basket.localScale * .5f;
        Quaternion inverse = Quaternion.Inverse(basket.rotation);
        Vector3 down = inverse * (cartBody.rotation * Vector3.down);
        var volume = new Bounds(Vector3.zero, half * 2f);
        foreach (Collider collider in Physics.OverlapBox(basket.position, half, basket.rotation,
            LayerMask.GetMask("PhysGrabObject"), QueryTriggerInteraction.Ignore))
        {
            Rigidbody body = collider.attachedRigidbody;
            if (body == cartBody || !TryActor(body, out var item) || CartFor(item) || IsHeld(item)) continue;
            Vector3 center = inverse * (body.worldCenterOfMass - basket.position);
            if (volume.IntersectRay(new Ray(center, down))) cargo.Add(body);
        }
    }

    internal static bool HasClosedJoints(HashSet<Rigidbody> bodies)
    {
        foreach (Rigidbody body in bodies)
        foreach (Joint joint in body.GetComponents<Joint>())
            if (!joint.connectedBody || !bodies.Contains(joint.connectedBody)) return false;
        return true;
    }

    internal static bool Fits(Bounds bounds, CargoMouth mouth) =>
        Mathf.Abs(bounds.center.x) + bounds.extents.x <= mouth.HalfWidth - Clearance &&
        bounds.min.y >= -mouth.HalfHeight - .12f && bounds.max.y <= mouth.HalfHeight - Clearance;

    internal static bool RunwayClear(CargoMouth mouth, Bounds hull, Collider source, HashSet<Rigidbody> transferring,
        Rigidbody player = null, Transform avatar = null)
    {
        Vector3 low = hull.min;
        Vector3 high = hull.max;
        low.y = Mathf.Max(low.y, -mouth.HalfHeight + Clearance);
        high.y = Mathf.Max(high.y, low.y + .001f);
        low.z = Mathf.Min(low.z, mouth.Depth);
        high.z = Mathf.Max(high.z, mouth.Depth);
        foreach (Collider hit in Physics.OverlapBox(mouth.Point((low + high) * .5f), (high - low) * .5f,
            mouth.Rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            Rigidbody other = hit.attachedRigidbody;
            if ((player && other == player) || transferring.Contains(other) ||
                (avatar && hit.transform.IsChildOf(avatar)) ||
                Physics.GetIgnoreLayerCollision(source.gameObject.layer, hit.gameObject.layer) ||
                Physics.GetIgnoreCollision(source, hit)) continue;
            return false;
        }
        return true;
    }

    internal static bool HasSupport(CargoMouth mouth, Bounds hull, Func<Vector3, bool> supported)
    {
        hull.Expand(-.08f);
        foreach (float x in new[] { hull.min.x, hull.max.x })
        foreach (float z in new[] { hull.min.z, hull.max.z })
            if (!supported(mouth.Point(new Vector3(x, -mouth.HalfHeight, z)))) return false;
        return true;
    }

    internal static CargoTraversalResult Traverse(CargoMouth entry, CargoMouth exit, Quaternion turn, Rigidbody lead,
        PhysGrabCart cart, HashSet<Rigidbody> bodies, Vector3 crossingPoint, bool permitsCart,
        Func<Vector3, bool> entrySupport = null, Func<Vector3, bool> exitSupport = null)
    {
        if (!HasClosedJoints(bodies))
        {
            StopOutwardMotion(entry, bodies);
            return CargoTraversalResult.Blocked;
        }
        var hulls = new List<(Collider Collider, Bounds Bounds)>();
        Bounds cartHull = default;
        bool cartHullFound = false;
        float rear = float.PositiveInfinity;
        float front = float.NegativeInfinity;
        foreach (Rigidbody body in bodies)
        {
            int first = hulls.Count;
            foreach (Collider collider in body.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody != body) continue;
                Bounds hull = entry.Local(collider);
                rear = Mathf.Min(rear, hull.min.z);
                front = Mathf.Max(front, hull.max.z);
                hulls.Add((collider, hull));
                if (body != lead) continue;
                if (cartHullFound) cartHull.Encapsulate(hull);
                else { cartHull = hull; cartHullFound = true; }
            }
            if (hulls.Count == first)
            {
                StopOutwardMotion(entry, bodies);
                return CargoTraversalResult.Blocked;
            }
        }

        Vector3 toCrossing = entry.Local(crossingPoint) - entry.Local(lead.worldCenterOfMass);
        float downToExitSill = cart ? entry.HalfHeight - exit.HalfHeight : 0f;
        float advance = Mathf.Max(0f, -exit.Depth + Clearance - rear);
        bool fits = !cart || permitsCart;
        foreach (var (collider, hull) in hulls)
        {
            if (!fits) break;
            Bounds atCrossing = new(hull.center + toCrossing, hull.size);
            Bounds arrival = Across(hull, advance, downToExitSill);
            Bounds exitCrossing = Across(atCrossing, -entry.Depth - exit.Depth, downToExitSill);
            fits = Fits(atCrossing, entry) && Fits(hull, entry) && Fits(exitCrossing, exit) && Fits(arrival, exit) &&
                RunwayClear(entry, Sweep(atCrossing, hull), collider, bodies) &&
                RunwayClear(exit, Sweep(exitCrossing, arrival), collider, bodies);
        }

        if (fits && cart)
        {
            Bounds approach = cartHull;
            approach.center -= Vector3.forward * Mathf.Max(0f, approach.max.z - entry.Depth + Clearance * 2f);
            fits = HasSupport(entry, approach, entrySupport) && HasSupport(exit, Across(cartHull, advance, downToExitSill), exitSupport);
        }
        if (!fits)
        {
            float setback = Mathf.Max(0f, front - entry.Depth) + Clearance;
            var offset = Vector3.back * setback;
            bool clear = true;
            foreach (var (collider, hull) in hulls)
            {
                var retreat = new Bounds(hull.center + offset, hull.size);
                if (RunwayClear(entry, Sweep(hull, retreat), collider, bodies)) continue;
                clear = false;
                break;
            }
            if (clear && cart)
            {
                cartHull.center += offset;
                clear = HasSupport(entry, cartHull, entrySupport);
            }
            if (clear)
                NativeRaidPortalCargo.ApplyIndependent(cart, bodies, Vector3.zero, -entry.Forward * setback, Quaternion.identity);
            StopOutwardMotion(entry, bodies);
            return clear ? CargoTraversalResult.Retreated : CargoTraversalResult.Blocked;
        }

        Vector3 arrivalCenter = exit.Center + exit.Up * downToExitSill - exit.Forward * advance;
        NativeRaidPortalCargo.ApplyIndependent(cart, bodies, entry.Center, arrivalCenter, turn);
        return CargoTraversalResult.Moved;
    }

    private static void StopOutwardMotion(CargoMouth entry, HashSet<Rigidbody> bodies)
    {
        Vector3 outward = entry.Forward;
        foreach (Rigidbody body in bodies)
            body.linearVelocity -= outward * Mathf.Max(0f, Vector3.Dot(body.linearVelocity, outward));
    }

    internal static Bounds Across(Bounds entry, float advance, float lift = 0f) =>
        new(new Vector3(-entry.center.x, entry.center.y + lift, -entry.center.z - advance), entry.size);

    private static Bounds Sweep(Bounds from, Bounds to)
    {
        from.Encapsulate(to);
        return from;
    }
}

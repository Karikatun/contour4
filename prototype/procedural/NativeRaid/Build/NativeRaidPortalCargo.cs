using System.Collections.Generic;
using Contour4.NativeRaid.Runtime;
using HarmonyLib;
using UnityEngine;

namespace Contour4.NativeRaid;

internal sealed class NativeRaidPortalCargo
{
    private const float JambClearance = .025f;
    private const float FloorSkin = .025f;
    private const float ExitClearance = .20f;

    private static readonly AccessTools.FieldRef<PhysGrabber, PhysGrabObject> HeldObject =
        AccessTools.FieldRefAccess<PhysGrabber, PhysGrabObject>("grabbedPhysGrabObject");
    private static readonly AccessTools.FieldRef<PhysGrabCart, Transform> Basket =
        AccessTools.FieldRefAccess<PhysGrabCart, Transform>("inCart");
    private static readonly AccessTools.FieldRef<PhysGrabCart, Rigidbody> CartBody =
        AccessTools.FieldRefAccess<PhysGrabCart, Rigidbody>("rb");
    private static readonly AccessTools.FieldRef<PhysGrabber, Vector3> GrabForce =
        AccessTools.FieldRefAccess<PhysGrabber, Vector3>("currentGrabForce");
    private static readonly AccessTools.FieldRef<PhysGrabber, Vector3> GrabTorque =
        AccessTools.FieldRefAccess<PhysGrabber, Vector3>("currentTorqueForce");
    private static readonly AccessTools.FieldRef<PhysGrabBeam, Vector3> SmoothPuller =
        AccessTools.FieldRefAccess<PhysGrabBeam, Vector3>("physGrabPointPullerSmoothPosition");

    private readonly PhysGrabCart cart;
    private readonly GrabPose grab;
    private readonly List<BodyPose> poses;
    private readonly Motion motion;
    internal HashSet<Rigidbody> Bodies { get; }

    private NativeRaidPortalCargo(PhysGrabCart cart, PhysGrabber grabber, HashSet<Rigidbody> bodies, Motion motion)
    {
        this.cart = cart;
        this.motion = motion;
        Bodies = bodies;
        poses = new List<BodyPose>(bodies.Count);
        foreach (Rigidbody body in bodies)
            poses.Add(new BodyPose(body, motion));
        grab = new GrabPose(grabber, motion);
    }

    internal static bool TryPrepare(RaidAssembly raid, NativeRaidPortal portal, Rigidbody player, Vector3 previousFeet,
        PhysGrabber grabber, bool rebase, out NativeRaidPortalCargo transfer, out Vector3 landing)
    {
        transfer = null;
        landing = default;
        if (!portal.AllowsCart || !grabber.isLocal) return false;
        var held = HeldObject(grabber);
        if (!held || held.dead || !held.rb || !held.rb.detectCollisions || held.rb.isKinematic ||
            !held.TryGetComponent(out PhysGrabCart cart) || CartBody(cart) != held.rb ||
            !cart.cartGrabPoint || !Basket(cart) || !grabber.physGrabPoint || !grabber.physGrabPointPuller ||
            !grabber.physGrabPointPlane)
            return false;

        var bodies = new HashSet<Rigidbody> { held.rb };
        CaptureBasket(cart, held.rb, bodies);
        var hulls = new List<ColliderHull>();
        Bounds cartHull = default;
        foreach (Rigidbody body in bodies)
        {
            foreach (Joint joint in body.GetComponents<Joint>())
                if (!joint.connectedBody || !bodies.Contains(joint.connectedBody))
                    return false;
            int first = hulls.Count;
            foreach (Collider collider in body.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody != body) continue;
                var bounds = InFrame(collider.bounds, portal.Entrance);
                if (!Fits(bounds, portal)) return false;
                hulls.Add(new ColliderHull(collider, bounds));
                if (body != held.rb) continue;
                if (hulls.Count == first + 1) cartHull = bounds;
                else cartHull.Encapsulate(bounds);
            }
            if (hulls.Count == first) return false;
        }

        // Keep a grip on a cart that is still on the source side. A cart completely beyond
        // the sill is no longer rolling on the source floor.
        if (cartHull.min.z >= 0f) return false;

        Vector3 source = player.position;
        float minDepth = float.PositiveInfinity;
        foreach (ColliderHull hull in hulls) minDepth = Mathf.Min(minDepth, hull.Bounds.min.z);
        float playerDepth = portal.Entrance.InverseTransformPoint(source).z;
        float minimumAdvance = Mathf.Max(.002f - playerDepth, 0f);
        float advance = Mathf.Max(0f, ExitClearance - minDepth - minimumAdvance,
            RaidAssembly.Character.Radius + ExitClearance - playerDepth - minimumAdvance);
        if (!raid.TryPassPlayer(portal, source, out landing, previousFeet, advance)) return false;
        float offset = advance + minimumAdvance;

        Transform localAvatar = grabber.playerAvatar ? grabber.playerAvatar.transform : null;
        foreach (ColliderHull hull in hulls)
        {
            Bounds entry = hull.Bounds;
            Bounds exit = Across(entry, offset);
            if (!RunwayClear(portal.Entrance, portal.HalfHeight, entry, hull.Collider, bodies, player, localAvatar) ||
                !RunwayClear(portal.Exit, portal.HalfHeight, exit, hull.Collider, bodies, player, localAvatar))
                return false;
        }
        if (!HasCartSupport(raid, portal, Across(cartHull, offset))) return false;

        // When the stage rotates around the player, the portal turn and world turn
        // cancel for the load. Capture its original world pose before moving its parent.
        var motion = rebase ? new Motion(source, source, Quaternion.identity) :
            new Motion(source, landing, portal.Rotation);
        transfer = new NativeRaidPortalCargo(cart, grabber, bodies, motion);
        return true;
    }

    internal static bool TryPrepareHand(RaidAssembly raid, NativeRaidPortal portal, Rigidbody player, Vector3 previousFeet,
        PhysGrabber grabber, bool rebase, out NativeRaidPortalCargo transfer, out Vector3 landing)
    {
        transfer = null;
        landing = default;
        var held = HeldObject(grabber);
        if (!grabber.isLocal || !held || held.dead || !held.rb || held.rb.isKinematic || !held.rb.detectCollisions ||
            held.GetComponentInChildren<PhysGrabCart>() || held.GetComponent<Joint>() || !grabber.physGrabPoint ||
            !grabber.physGrabPointPuller || !grabber.physGrabPointPlane ||
            !raid.TryPassPlayer(portal, player.position, out landing, previousFeet)) return false;
        var body = held.rb;
        Vector3 source = player.position;
        Quaternion turn = portal.TravelRotation;
        var pose = new Motion(source, landing, turn);
        var hull = new Bounds();
        var hand = Quaternion.LookRotation(portal.Door ? portal.Entrance.forward : portal.Entrance.up, portal.EntryFloor.Up);
        bool first = true;
        foreach (Collider collider in body.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody != body) continue;
            Bounds shape = collider.bounds;
            Bounds mouth = InFrame(shape, portal.Entrance);
            if (portal.Door ? !Fits(mouth, portal) :
                Mathf.Abs(mouth.center.x) + mouth.extents.x > portal.HalfWidth - JambClearance ||
                Mathf.Abs(mouth.center.y) + mouth.extents.y > portal.HalfHeight - JambClearance) return false;
            Vector3 size = new Vector3(ExtentAlong(shape, hand * Vector3.right),
                ExtentAlong(shape, hand * Vector3.up), ExtentAlong(shape, hand * Vector3.forward)) * 2f;
            var near = new Bounds(Quaternion.Inverse(hand) * (shape.center - source), size);
            if (first) { hull = near; first = false; }
            else hull.Encapsulate(near);
            var avatar = grabber.playerAvatar ? grabber.playerAvatar.transform : null;
            foreach (Collider hit in Physics.OverlapBox(pose.Map(shape.center), shape.extents, turn,
                Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.attachedRigidbody == player || hit.attachedRigidbody == body ||
                    (avatar && hit.transform.IsChildOf(avatar)) ||
                    Physics.GetIgnoreLayerCollision(collider.gameObject.layer, hit.gameObject.layer) ||
                    Physics.GetIgnoreCollision(collider, hit)) continue;
                return false;
            }
        }
        if (first || hull.size.x > .80f || hull.size.y > 1.88f || hull.size.z > .90f) return false;
        var motion = rebase ? new Motion(source, source, Quaternion.identity) : pose;
        transfer = new NativeRaidPortalCargo(null, grabber, new HashSet<Rigidbody> { body }, motion);
        return true;
    }

    private static void CaptureBasket(PhysGrabCart cart, Rigidbody cartBody, HashSet<Rigidbody> bodies)
    {
        // Physical basket membership uses the same overlap and support test as the ring.
        // Sample current overlaps because the stock itemsInCart list updates on a timer.
        Transform basket = Basket(cart);
        Vector3 half = basket.localScale * .5f;
        Quaternion inverse = Quaternion.Inverse(basket.rotation);
        Vector3 down = inverse * (cartBody.rotation * Vector3.down);
        var basketVolume = new Bounds(Vector3.zero, half * 2f);
        foreach (Collider collider in Physics.OverlapBox(basket.position, half, basket.rotation,
            LayerMask.GetMask("PhysGrabObject"), QueryTriggerInteraction.Ignore))
        {
            Rigidbody body = collider.attachedRigidbody;
            if (!body || body == cartBody || body.isKinematic || !body.detectCollisions ||
                !body.TryGetComponent(out PhysGrabObject item) || item.dead ||
                item.GetComponentInChildren<PhysGrabCart>() || item.grabbed || item.grabbedLocal ||
                item.playerGrabbing.Count > 0)
                continue;
            Vector3 center = inverse * (body.worldCenterOfMass - basket.position);
            if (basketVolume.IntersectRay(new Ray(center, down)))
                bodies.Add(body);
        }
    }

    private static Bounds InFrame(Bounds bounds, Transform frame) => new(frame.InverseTransformPoint(bounds.center),
        2f * new Vector3(ExtentAlong(bounds, frame.right), ExtentAlong(bounds, frame.up), ExtentAlong(bounds, frame.forward)));

    private static float ExtentAlong(Bounds bounds, Vector3 axis) =>
        Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)));

    private static bool Fits(Bounds bounds, NativeRaidPortal portal) =>
        Mathf.Abs(bounds.center.x) + bounds.extents.x <= portal.HalfWidth - JambClearance &&
        bounds.min.y >= -portal.HalfHeight - .12f && bounds.max.y <= portal.HalfHeight - JambClearance;

    private static Bounds Across(Bounds entry, float offset) =>
        new(new Vector3(-entry.center.x, entry.center.y, -entry.center.z - offset), entry.size);

    private static bool RunwayClear(Transform frame, float halfHeight, Bounds hull, Collider source,
        HashSet<Rigidbody> transferring, Rigidbody player, Transform localAvatar)
    {
        Vector3 low = hull.min;
        Vector3 high = hull.max;
        // Cover the planned straight sweep to or from the threshold, including the end pose.
        // Floor contact is tested separately against the landing room's actual support.
        low.y = Mathf.Max(low.y, -halfHeight + FloorSkin);
        high.y = Mathf.Max(high.y, low.y + .001f);
        low.z = Mathf.Min(low.z, 0f);
        high.z = Mathf.Max(high.z, 0f);
        foreach (Collider hit in Physics.OverlapBox(frame.TransformPoint((low + high) * .5f),
            (high - low) * .5f, frame.rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            Rigidbody hitBody = hit.attachedRigidbody;
            if (hitBody == player || transferring.Contains(hitBody) ||
                (localAvatar && hit.transform.IsChildOf(localAvatar)) ||
                Physics.GetIgnoreLayerCollision(source.gameObject.layer, hit.gameObject.layer) ||
                Physics.GetIgnoreCollision(source, hit))
                continue;
            return false;
        }
        return true;
    }

    private static bool HasCartSupport(RaidAssembly raid, NativeRaidPortal portal, Bounds hull)
    {
        var bounds = hull;
        bounds.Expand(-.08f);
        foreach (float x in new[] { bounds.min.x, bounds.max.x })
        foreach (float z in new[] { bounds.min.z, bounds.max.z })
            if (!raid.HasPortalSupport(portal.ExitFloor, portal.Exit.TransformPoint(new Vector3(x, -portal.HalfHeight, z))))
                return false;
        return true;
    }

    internal void Apply(NativeRaidPortal portal)
    {
        foreach (BodyPose pose in poses) pose.Apply();
        if (cart && (motion.Rotation != Quaternion.identity || motion.Source != motion.Destination))
            NativeRaidGravity.TransformCartHistory(cart, motion.Map, motion.Rotation);
        grab.Apply();
        Physics.SyncTransforms();
        if (cart)
            Debug.Log($"[Contour4 Procedural] {portal.Id}: cart {CartBody(cart).GetInstanceID()} and " +
                $"{Bodies.Count - 1} loose cargo bodies crossed with the native hold.");
    }

    private readonly struct ColliderHull
    {
        internal readonly Collider Collider;
        internal readonly Bounds Bounds;

        internal ColliderHull(Collider collider, Bounds bounds)
        {
            Collider = collider;
            Bounds = bounds;
        }
    }

    private readonly struct Motion
    {
        internal readonly Vector3 Source, Destination;
        internal readonly Quaternion Rotation;

        internal Motion(Vector3 source, Vector3 destination, Quaternion rotation)
        {
            Source = source;
            Destination = destination;
            Rotation = rotation;
        }

        internal Vector3 Map(Vector3 point) => Destination + Rotation * (point - Source);
    }

    private readonly struct BodyPose
    {
        private readonly Rigidbody body;
        private readonly Vector3 position, velocity, spin, forceDelta, torqueDelta;
        private readonly Quaternion rotation;
        private readonly bool asleep;

        internal BodyPose(Rigidbody body, Motion motion)
        {
            this.body = body;
            Quaternion turn = motion.Rotation;
            position = motion.Map(body.position);
            rotation = turn * body.rotation;
            velocity = turn * body.linearVelocity;
            spin = turn * body.angularVelocity;
            Vector3 force = body.GetAccumulatedForce();
            Vector3 torque = body.GetAccumulatedTorque();
            forceDelta = turn * force - force;
            torqueDelta = turn * torque - torque;
            asleep = body.IsSleeping();
        }

        internal void Apply()
        {
            body.position = position;
            body.rotation = rotation;
            body.transform.SetPositionAndRotation(position, rotation);
            body.linearVelocity = velocity;
            body.angularVelocity = spin;
            if (forceDelta.sqrMagnitude > 0f) body.AddForce(forceDelta);
            if (torqueDelta.sqrMagnitude > 0f) body.AddTorque(torqueDelta);
            if (asleep) body.Sleep();
        }
    }

    private readonly struct GrabPose
    {
        private readonly PhysGrabber grabber;
        private readonly TransformPose point, puller, plane;
        private readonly Vector3 pointCache, pullerCache, beamCache, force, torque;
        private readonly PhysGrabBeam beam;

        internal GrabPose(PhysGrabber grabber, Motion motion)
        {
            this.grabber = grabber;
            point = new TransformPose(grabber.physGrabPoint, motion);
            puller = new TransformPose(grabber.physGrabPointPuller, motion);
            plane = new TransformPose(grabber.physGrabPointPlane, motion);
            pointCache = motion.Map(grabber.physGrabPointPosition);
            pullerCache = motion.Map(grabber.physGrabPointPullerPosition);
            beam = grabber.physGrabBeamComponent;
            beamCache = beam ? motion.Map(SmoothPuller(beam)) : default;
            force = motion.Rotation * GrabForce(grabber);
            torque = motion.Rotation * GrabTorque(grabber);
        }

        internal void Apply()
        {
            // Stock uses a spring force with a body-local grab point and camera-relative
            // orientation, so both relative bases remain valid in the common frame.
            plane.Apply();
            puller.Apply();
            point.Apply();
            grabber.physGrabPointPosition = pointCache;
            grabber.physGrabPointPullerPosition = pullerCache;
            if (beam) SmoothPuller(beam) = beamCache;
            GrabForce(grabber) = force;
            GrabTorque(grabber) = torque;
        }
    }

    private readonly struct TransformPose
    {
        private readonly Transform transform;
        private readonly Vector3 position;
        private readonly Quaternion rotation;

        internal TransformPose(Transform transform, Motion motion)
        {
            this.transform = transform;
            position = motion.Map(transform.position);
            rotation = motion.Rotation * transform.rotation;
        }

        internal void Apply() => transform.SetPositionAndRotation(position, rotation);
    }
}

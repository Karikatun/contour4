using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Contour4.NativeRaid;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceJourney;

internal sealed class CartSeams
{
    private const float ApproachDistance = 2.6f;
    private static readonly System.Reflection.FieldInfo Basket = AccessTools.Field(typeof(PhysGrabCart), "inCart");
    private static readonly Dictionary<Rigidbody, Vector3> supportAxes = new();

    private readonly List<Crossing> doors = new();
    private readonly Dictionary<Rigidbody, Cart> carts = new();
    private readonly Dictionary<Rigidbody, PortalEnd> claimed = new();

    internal CartSeams(IEnumerable<Portal> portals)
    {
        foreach (var portal in portals)
        {
            if (portal.A.Upper)
                continue;
            doors.Add(new Crossing(portal.A, portal.B));
            doors.Add(new Crossing(portal.B, portal.A));
            PrepareThreshold(portal.A);
            PrepareThreshold(portal.B);
        }
    }

    private static void PrepareThreshold(PortalEnd end)
    {
        const float length = 3.4f;
        var floor = new GameObject("Doorway support");
        floor.transform.SetParent(end.Marker, false);
        var scale = end.Marker.lossyScale;
        var collider = floor.AddComponent<BoxCollider>();
        collider.center = new Vector3(0, -.10f / scale.y, length * .5f / scale.z);
        collider.size = new Vector3((end.Width - .10f) / scale.x, .20f / scale.y, length / scale.z);

        // A lamp inside the jamb lights the same floor before and after the viewer crosses.
        var fixture = new GameObject("Doorway light").transform;
        fixture.SetParent(end.Marker, false);
        fixture.localPosition = new Vector3(0, 2.64f, -.44f);
        fixture.localRotation = Quaternion.LookRotation(new Vector3(0, -.54f, -1f), Vector3.up);
        var lamp = fixture.gameObject.AddComponent<Light>();
        lamp.type = LightType.Spot;
        lamp.spotAngle = 110f;
        lamp.range = 14f;
        lamp.intensity = 2.4f;
        lamp.color = new Color(.92f, .95f, 1f);
        lamp.renderMode = LightRenderMode.ForcePixel;
    }

    internal void Register(PhysGrabCart cart)
    {
        var obj = cart.GetComponentInParent<PhysGrabObject>();
        if (!obj)
            obj = cart.GetComponentInChildren<PhysGrabObject>();
        if (!obj || !obj.rb || carts.ContainsKey(obj.rb))
            return;
        carts.Add(obj.rb, new Cart(cart, obj.rb));
        supportAxes[obj.rb] = Vector3.up;
    }

    internal bool TryGetSupport(Rigidbody body, out PortalEnd end) => claimed.TryGetValue(body, out end);
    internal bool IsCart(Rigidbody body) => carts.ContainsKey(body);

    internal void SetSupportAxis(Rigidbody body, Vector3 up)
    {
        if (IsCart(body))
            supportAxes[body] = up;
    }

    internal void PhysicsStep(Func<Rigidbody, PortalEnd, bool> crossedWhileLoose, Action<Rigidbody, Crossing> onMoved)
    {
        claimed.Clear();
        foreach (var cart in carts.Values)
        {
            if (!cart.Body || cart.Body.isKinematic)
            {
                cart.Cancel();
                continue;
            }

            var body = cart.Body;
            if (cart.Crossing is not Crossing crossing)
            {
                foreach (var door in doors)
                {
                    Vector3 center = body.worldCenterOfMass;
                    float distance = door.Entry.DistanceFromPlane(center);
                    if (distance <= 0 && distance > -ApproachDistance && door.Entry.NearAperture(center))
                    {
                        cart.Crossing = door;
                        break;
                    }
                }
                if (cart.Crossing is not Crossing active)
                    continue;
                crossing = active;
            }

            Vector3 position = body.worldCenterOfMass;
            if (!crossing.Entry.NearAperture(position) ||
                crossing.Entry.DistanceFromPlane(position) < -ApproachDistance)
            {
                cart.Cancel();
                continue;
            }

            cart.CaptureCargo();
            cart.Cargo.RemoveWhere(item => crossedWhileLoose(item, crossing.Entry));
            claimed[body] = crossing.Entry;
            foreach (var cargo in cart.Cargo)
                claimed[cargo] = crossing.Entry;

            Bounds hull = cart.HullBounds();
            float rear = BackOf(hull, crossing.Entry);
            foreach (var item in cart.Cargo)
                rear = Mathf.Min(rear, BackOf(cart.CargoBounds(item), crossing.Entry));
            if (rear <= .035f)
                continue;

            Move(cart, crossing);
            claimed[body] = crossing.Exit;
            onMoved(body, crossing);
            foreach (var cargo in cart.Cargo)
            {
                claimed[cargo] = crossing.Exit;
                onMoved(cargo, crossing);
            }
            cart.Cancel();
        }
    }

    private static float BackOf(Bounds bounds, PortalEnd end)
    {
        Vector3 normal = end.Normal;
        float radius = Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z)));
        return end.DistanceFromPlane(bounds.center) - radius;
    }

    private static float FrontOf(Bounds bounds, PortalEnd end) =>
        2f * end.DistanceFromPlane(bounds.center) - BackOf(bounds, end);

    private static void Move(Cart cart, Crossing crossing)
    {
        // One rigid frame transform preserves contact geometry and relative velocity of the entire load.
        var movement = new List<BodyTransfer>(cart.Cargo.Count + 1) { new(cart.Body, crossing) };
        foreach (var cargo in cart.Cargo)
            movement.Add(new BodyTransfer(cargo, crossing));
        foreach (var body in movement)
            body.Apply();
        cart.MoveNativeHistory(crossing.TransformPoint, crossing.Rotation);
        Physics.SyncTransforms();
        Debug.Log($"[Contour4Ring] Cart and {cart.Cargo.Count} cargo bodies rolled through {crossing.Entry.Room.name} -> {crossing.Exit.Room.name}.");
    }

    private readonly struct BodyTransfer
    {
        private readonly Rigidbody body;
        private readonly Vector3 position;
        private readonly Quaternion rotation;
        private readonly Vector3 velocity;
        private readonly Vector3 spin;

        internal BodyTransfer(Rigidbody body, Crossing crossing)
        {
            this.body = body;
            var turn = crossing.Rotation;
            position = crossing.TransformPoint(body.position);
            rotation = turn * body.rotation;
            velocity = turn * body.linearVelocity;
            spin = turn * body.angularVelocity;
        }

        internal void Apply()
        {
            body.transform.SetPositionAndRotation(position, rotation);
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = velocity;
            body.angularVelocity = spin;
        }
    }

    internal void ReorientNativeHistory(Vector3 pivot, Vector3 destination, Quaternion delta)
    {
        foreach (var cart in carts.Values)
            if (cart.Component)
                cart.MoveNativeHistory(point => destination + delta * (point - pivot), delta);
    }

    internal void RefreshVisuals()
    {
        foreach (var cart in carts.Values)
            cart.RefreshVisuals();
    }

    internal void Dispose()
    {
        foreach (var cart in carts.Values)
        {
            supportAxes.Remove(cart.Body);
            cart.Cancel();
        }
    }

    [HarmonyPatch(typeof(PhysGrabCart), "FixedUpdate")]
    private static class CartUpright
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var rotation = AccessTools.Method(typeof(Rigidbody), nameof(Rigidbody.MoveRotation), new[] { typeof(Quaternion) });
            var spin = AccessTools.PropertySetter(typeof(Rigidbody), nameof(Rigidbody.angularVelocity));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(rotation))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(CartUpright), nameof(ApplyRotation));
                }
                else if (instruction.Calls(spin))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(CartUpright), nameof(ApplySpin));
                }
                yield return instruction;
            }
        }

        private static void ApplyRotation(Rigidbody body, Quaternion original)
        {
            if (!TrySupportAxis(body, out var up) || Vector3.Dot(up, Vector3.up) > .99999f)
            {
                body.MoveRotation(original);
                return;
            }

            Quaternion current = body.rotation;
            Vector3 forward = Vector3.ProjectOnPlane(current * Vector3.forward, up);
            if (forward.sqrMagnitude < .01f)
                forward = Vector3.Cross(current * Vector3.right, up);
            body.MoveRotation(Quaternion.LookRotation(forward, up));
        }

        private static void ApplySpin(Rigidbody body, Vector3 original)
        {
            body.angularVelocity = TrySupportAxis(body, out var up) && Vector3.Dot(up, Vector3.up) <= .99999f
                ? Vector3.Project(body.angularVelocity, up)
                : original;
        }

        private static bool TrySupportAxis(Rigidbody body, out Vector3 up) =>
            NativeRaidGravity.TryGetCartSupport(body, out up) || supportAxes.TryGetValue(body, out up);
    }

    private sealed class Cart
    {
        private readonly Transform basket;
        private readonly Collider[] hull;
        private readonly Dictionary<Rigidbody, SeamImage[]> copies = new();
        private readonly Dictionary<Rigidbody, Collider[]> cargoColliders = new();
        private readonly List<Rigidbody> obsolete = new();
        private readonly Collider[] overlaps = new Collider[96];
        internal PhysGrabCart Component { get; }
        internal Rigidbody Body { get; }
        internal Crossing? Crossing { get; set; }
        internal readonly HashSet<Rigidbody> Cargo = new();

        internal Cart(PhysGrabCart component, Rigidbody body)
        {
            Component = component;
            Body = body;
            basket = Basket.GetValue(component) as Transform;
            hull = body.GetComponentsInChildren<Collider>(true)
                .Where(collider => !collider.isTrigger && collider.attachedRigidbody == body).ToArray();
        }

        internal Bounds HullBounds()
        {
            Bounds bounds = new Bounds(Body.worldCenterOfMass, Vector3.zero);
            foreach (var collider in hull)
                if (collider && collider.enabled && collider.gameObject.activeInHierarchy)
                    bounds.Encapsulate(collider.bounds);
            return bounds;
        }

        internal Bounds CargoBounds(Rigidbody body)
        {
            if (!cargoColliders.TryGetValue(body, out var colliders))
            {
                colliders = body.GetComponentsInChildren<Collider>(true)
                    .Where(collider => !collider.isTrigger && collider.attachedRigidbody == body).ToArray();
                cargoColliders.Add(body, colliders);
            }

            var bounds = new Bounds(body.worldCenterOfMass, Vector3.zero);
            foreach (var collider in colliders)
                if (collider && collider.enabled && collider.gameObject.activeInHierarchy)
                    bounds.Encapsulate(collider.bounds);
            return bounds;
        }

        internal void CaptureCargo()
        {
            Cargo.Clear();
            if (!basket)
                return;
            // Match the game's physical basket query; the UI's cached items list can be stale at the threshold.
            Vector3 half = basket.localScale * .5f;
            int count = Physics.OverlapBoxNonAlloc(basket.position, half, overlaps, basket.rotation,
                LayerMask.GetMask("PhysGrabObject"), QueryTriggerInteraction.Ignore);
            Quaternion toBasket = Quaternion.Inverse(basket.rotation);
            Vector3 down = toBasket * (Body.rotation * Vector3.down);
            var basketVolume = new Bounds(Vector3.zero, half * 2f);
            for (int i = 0; i < count; i++)
            {
                var item = overlaps[i].attachedRigidbody;
                if (!item || item == Body || item.isKinematic || !item.TryGetComponent<PhysGrabObject>(out var prop) ||
                    item.GetComponentInChildren<PhysGrabCart>())
                    continue;
                if (prop.grabbed || prop.grabbedLocal || prop.playerGrabbing.Count > 0)
                    continue;
                // Require the basket beneath the cargo center, including bulky loads that protrude above the rim.
                Vector3 center = toBasket * (item.worldCenterOfMass - basket.position);
                if (basketVolume.IntersectRay(new Ray(center, down)))
                    Cargo.Add(item);
            }
        }

        internal void MoveNativeHistory(Func<Vector3, Vector3> map, Quaternion turn)
        {
            NativeRaidGravity.TransformCartHistory(Component, map, turn);
        }

        internal void RefreshVisuals()
        {
            if (Crossing is not Crossing crossing || !Body)
                return;
            bool visibleAcrossPlane = FrontOf(HullBounds(), crossing.Entry) > 0f ||
                Cargo.Any(item => item && FrontOf(CargoBounds(item), crossing.Entry) > 0f);
            if (!visibleAcrossPlane)
            {
                ClearCopies();
                return;
            }
            obsolete.Clear();
            foreach (var previous in copies.Keys)
                if (!previous || (previous != Body && !Cargo.Contains(previous)))
                    obsolete.Add(previous);
            foreach (var previous in obsolete)
            {
                foreach (var copy in copies[previous])
                    copy.Dispose();
                copies.Remove(previous);
            }
            Show(Body, crossing);
            foreach (var cargo in Cargo)
                if (cargo)
                    Show(cargo, crossing);
        }

        private void Show(Rigidbody body, Crossing crossing)
        {
            if (!copies.TryGetValue(body, out var parts))
            {
                parts = body.GetComponentsInChildren<MeshRenderer>()
                    .Where(renderer => renderer.GetComponent<MeshFilter>()?.sharedMesh)
                    .Select(renderer => new SeamImage(renderer, crossing.Exit.Marker, "Portal load image")).ToArray();
                copies.Add(body, parts);
            }
            foreach (var part in parts)
                part.Update(crossing);
        }

        internal void Cancel()
        {
            Crossing = null;
            Cargo.Clear();
            cargoColliders.Clear();
            ClearCopies();
        }

        private void ClearCopies()
        {
            foreach (var parts in copies.Values)
            foreach (var copy in parts)
                copy.Dispose();
            copies.Clear();
        }
    }

    internal sealed class SeamImage
    {
        internal MeshRenderer Original { get; }
        private readonly MeshRenderer renderer;
        private readonly MaterialPropertyBlock properties = new();

        internal SeamImage(MeshRenderer original, Transform exit, string name)
        {
            Original = original;
            var obj = new GameObject(name) { layer = original.gameObject.layer };
            obj.transform.SetParent(exit, false);
            obj.AddComponent<MeshFilter>().sharedMesh = original.GetComponent<MeshFilter>().sharedMesh;
            renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = original.sharedMaterials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        internal void Update(Crossing crossing)
        {
            renderer.enabled = Original && Original.enabled && Original.gameObject.activeInHierarchy;
            if (!renderer.enabled)
                return;
            var pose = Original.transform;
            var target = renderer.transform;
            target.SetPositionAndRotation(crossing.TransformPoint(pose.position), crossing.Rotation * pose.rotation);
            var scale = target.parent.lossyScale;
            target.localScale = new Vector3(pose.lossyScale.x / scale.x, pose.lossyScale.y / scale.y, pose.lossyScale.z / scale.z);
            Original.GetPropertyBlock(properties);
            renderer.SetPropertyBlock(properties);
        }

        internal void Dispose()
        {
            renderer.enabled = false;
            Object.Destroy(renderer.gameObject);
        }
    }
}

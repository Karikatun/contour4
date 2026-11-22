using System;
using System.Collections.Generic;
using System.Reflection;
using Contour4.NativeRaid.Runtime;
using HarmonyLib;
using UnityEngine;

namespace Contour4.NativeRaid;

// The controller and camera keep the stock world-up frame. The freight deck rolls around
// the player, preserving positions and velocities relative to the rest of the scene.
[DefaultExecutionOrder(-150)]
internal sealed class NativeRaidGravity : MonoBehaviour
{
    private static readonly FieldInfo CartBody = AccessTools.Field(typeof(PhysGrabCart), "rb");
    private static readonly FieldInfo ActualLast = AccessTools.Field(typeof(PhysGrabCart), "actualVelocityLastPosition");
    private static readonly FieldInfo LastPosition = AccessTools.Field(typeof(PhysGrabCart), "lastPosition");
    private static readonly AccessTools.FieldRef<PhysGrabCart, Vector3> ActualVelocity =
        AccessTools.FieldRefAccess<PhysGrabCart, Vector3>("actualVelocity");
    private static NativeRaidGravity active;
    private RaidAssembly raid;
    private Transform levelRoot;
    private readonly List<Rigidbody> actors = new();
    private readonly Dictionary<Rigidbody, PhysGrabCart> carts = new();
    private readonly RaidWorldRebase stage = new();
    private float nextActorScan;

    internal static void Attach(RaidAssembly raid)
    {
        var gravity = raid.gameObject.AddComponent<NativeRaidGravity>();
        gravity.raid = raid;
        gravity.levelRoot = raid.transform.root;
        active = gravity;
    }

    private void OnEnable()
    {
        if (raid) active = this;
    }

    private void OnDisable()
    {
        if (active == this) active = null;
        carts.Clear();
        actors.Clear();
        nextActorScan = 0f;
    }

    private void FixedUpdate()
    {
        var controller = PlayerController.instance;
        if (raid.Context == null || !controller || !controller.rb || !SemiFunc.LevelGenDone()) return;
        var player = controller.rb;
        if (raid.TryUp(player.position, out Vector3 up) && Vector3.Dot(up, Vector3.up) < .999999f)
            Rebase(player, player.position, Quaternion.FromToRotation(up, Vector3.up));
        RefreshActors(player);
        foreach (Rigidbody actor in actors)
        {
            if (!actor || actor.isKinematic || actor.IsSleeping() || !actor.useGravity ||
                !TryUp(actor.worldCenterOfMass, out Vector3 gravityUp)) continue;
            Vector3 correction = -gravityUp * Physics.gravity.magnitude - Physics.gravity;
            if (correction.sqrMagnitude > .00001f) actor.AddForce(correction, ForceMode.Acceleration);
        }
    }

    private bool TryUp(Vector3 point, out Vector3 up)
    {
        // The ring handles its own rigidbodies and fades its gravity independently.
        up = default;
        if (raid.ContainsRing(point)) return false;
        if (raid.TryUp(point, out up)) return true;
        // The truck uses the original entrance floor basis. External bodies outside the
        // generated sections use it only while they are parented to this level.
        up = raid.Room(raid.Plan.Entrance.Room).Root.up;
        return true;
    }

    internal static bool TryGetCartSupport(Rigidbody body, out Vector3 up)
    {
        up = default;
        var gravity = active;
        if (!gravity || !body || gravity.raid.Context == null || !SemiFunc.LevelGenDone() ||
            body.gameObject.scene != gravity.gameObject.scene) return false;

        Vector3 center = body.worldCenterOfMass;
        if (gravity.raid.ContainsRing(center)) return false;
        if (gravity.raid.TryUp(center, out up)) return true;
        if (!body.transform.IsChildOf(gravity.levelRoot)) return false;
        up = gravity.raid.Room(gravity.raid.Plan.Entrance.Room).Root.up;
        return true;
    }

    internal static void TransformCartHistory(PhysGrabCart cart, Func<Vector3, Vector3> map, Quaternion turn)
    {
        ActualLast.SetValue(cart, map((Vector3)ActualLast.GetValue(cart)));
        LastPosition.SetValue(cart, map((Vector3)LastPosition.GetValue(cart)));
        // Cargo can read this world velocity before the cart's next FixedUpdate.
        ref Vector3 cachedVelocity = ref ActualVelocity(cart);
        cachedVelocity = turn * cachedVelocity;
    }

    internal static bool TryRebasePlayerAcross(RaidAssembly raid, NativeRaidPortal portal, Rigidbody body,
        Vector3 destinationFeet, ISet<Rigidbody> portalLoad = null)
    {
        if (!body || !portal.Entrance || !portal.Exit ||
            !raid.TryGetComponent(out NativeRaidGravity gravity)) return false;
        gravity.Rebase(body, destinationFeet, Quaternion.Inverse(portal.Rotation), portalLoad);
        return true;
    }

    private void RefreshActors(Rigidbody player)
    {
        if (Time.fixedTime < nextActorScan) return;
        nextActorScan = Time.fixedTime + .25f;
        actors.Clear();
        carts.Clear();
        var avatar = PlayerAvatar.instance;
        foreach (var body in FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!body || body == player || body.gameObject.scene != gameObject.scene ||
                (avatar && body.transform.IsChildOf(avatar.transform)) ||
                body.transform.IsChildOf(player.transform)) continue;
            if (!body.transform.IsChildOf(levelRoot) && !raid.TryUp(body.worldCenterOfMass, out _) &&
                !raid.ContainsRing(body.worldCenterOfMass)) continue;
            actors.Add(body);
        }
        foreach (var cart in FindObjectsByType<PhysGrabCart>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!cart || cart.gameObject.scene != gameObject.scene ||
                !(CartBody.GetValue(cart) is Rigidbody body) || !body) continue;
            carts.TryAdd(body, cart);
        }
    }

    private void Rebase(Rigidbody player, Vector3 pivot, Quaternion turn, ISet<Rigidbody> portalLoad = null)
    {
        RefreshActors(player);
        Vector3 destination = player.position;
        stage.Apply(levelRoot, player, actors, pivot, turn, portalLoad);
        foreach (var body in actors)
        {
            if (body && (portalLoad == null || !portalLoad.Contains(body)) &&
                carts.TryGetValue(body, out var cart) && cart && (Rigidbody)CartBody.GetValue(cart) == body)
                TransformCartHistory(cart, point => destination + turn * (point - pivot), turn);
        }
    }
}

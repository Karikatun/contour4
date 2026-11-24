using System;
using System.Collections.Generic;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using Contour4.Procedural.Navigation;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using NativeNavigation = Contour4.NativeRaid.Runtime.RaidNavigation;

namespace Contour4.NativeRaid.Enemies;

// A local solo adapter for the audited Headman. Native chase still chooses and
// remembers its target; the real body traverses the supported oriented deck.
internal static class NativeMobiusPursuit
{
    internal static readonly AccessTools.FieldRef<Enemy, EnemyNavMeshAgent> Nav = AccessTools.FieldRefAccess<Enemy, EnemyNavMeshAgent>("NavMeshAgent");
    internal static readonly AccessTools.FieldRef<EnemyNavMeshAgent, NavMeshAgent> Agent = AccessTools.FieldRefAccess<EnemyNavMeshAgent, NavMeshAgent>("Agent");
    internal static readonly AccessTools.FieldRef<Enemy, EnemyRigidbody> Body = AccessTools.FieldRefAccess<Enemy, EnemyRigidbody>("Rigidbody");
    internal static readonly AccessTools.FieldRef<EnemyRigidbody, Rigidbody> BodyRb = AccessTools.FieldRefAccess<EnemyRigidbody, Rigidbody>("rb");
    internal static readonly AccessTools.FieldRef<EnemyRigidbody, bool> Grabbed = AccessTools.FieldRefAccess<EnemyRigidbody, bool>("grabbed");
    internal static readonly AccessTools.FieldRef<Enemy, PlayerAvatar> Target = AccessTools.FieldRefAccess<Enemy, PlayerAvatar>("TargetPlayerAvatar");
    private static readonly AccessTools.FieldRef<Enemy, EnemyParent> Parent = AccessTools.FieldRefAccess<Enemy, EnemyParent>("EnemyParent");
    private static readonly AccessTools.FieldRef<EnemyParent, bool> Spawned = AccessTools.FieldRefAccess<EnemyParent, bool>("Spawned");
    private static readonly AccessTools.FieldRef<Enemy, bool> Master = AccessTools.FieldRefAccess<Enemy, bool>("MasterClient");
    private static readonly AccessTools.FieldRef<Enemy, int> TargetView = AccessTools.FieldRefAccess<Enemy, int>("TargetPlayerViewID");
    private static readonly AccessTools.FieldRef<PlayerAvatar, bool> Disabled = AccessTools.FieldRefAccess<PlayerAvatar, bool>("isDisabled");
    private static readonly HashSet<NativeMobiusMotor> motors = new();

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.DeclaredMethod(typeof(EnemyRigidbody), "FixedUpdate"),
            prefix: new HarmonyMethod(typeof(NativeMobiusPursuit), nameof(BodyStep)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(EnemyNavMeshAgent), "Update"),
            prefix: new HarmonyMethod(typeof(NativeMobiusPursuit), nameof(NavStep)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(EnemyHeadController), "Update"),
            postfix: new HarmonyMethod(typeof(NativeMobiusPursuit), nameof(HeadStep)));
    }

    internal static bool Drive(Enemy enemy, EnemyStateChase state)
    {
        var raid = NativeNavigation.Current;
        if (raid == null || !raid.Navigation || !raid.Navigation.Ready || !Eligible(enemy)) return false;
        PlayerAvatar player = Target(enemy);
        if (!player || !player.TryGetComponent<RaidTarget>(out var target) || !ValidTarget(enemy, player, target, raid) ||
            !target.Floor.RoomId.StartsWith(MobiusHall.Id + "-", StringComparison.Ordinal)) return false;
        enemy.TryGetComponent<NativeMobiusMotor>(out var motor);
        if (!motor || !motor.Leased)
        {
            if (!raid.TryGetFloor(enemy.transform.position, out var from) || from == target.Floor ||
                from.RoomId != target.Floor.RoomId || !enemy.TryGetComponent<EnemyHeadController>(out _)) return false;
            var wrapper = Nav(enemy);
            var agent = wrapper ? Agent(wrapper) : null;
            var body = Body(enemy);
            var rb = body ? BodyRb(body) : null;
            if (!agent || agent.agentTypeID != StockPortalPursuit.AgentType || agent.areaMask != 1 ||
                !body || !rb || rb.isKinematic || !body.followTarget) return false;
            motor ??= enemy.gameObject.AddComponent<NativeMobiusMotor>();
            if (!motor.Bind(raid, enemy, body, rb, wrapper, agent, from, state.Speed)) return false;
            motors.Add(motor);
        }
        if (motor.Context != raid || motor.RoomId != target.Floor.RoomId) return false;
        motor.enabled = true;
        motor.Chase(state, player, target);
        return true;
    }

    internal static bool Eligible(Enemy enemy) => enemy && enemy.isActiveAndEnabled && Master(enemy) &&
        Parent(enemy) && Spawned(Parent(enemy)) && Parent(enemy).enemyName == "Headman" &&
        !SemiFunc.IsMultiplayer() && enemy.gameObject.scene == NativeNavigation.Current?.Navigation.gameObject.scene;

    // Revalidate the same live native identity in Update and every physics step.
    // Occlusion does not invalidate a remembered target; disabled/destroyed or
    // reassigned players, views and sight proxies do.
    internal static bool ValidTarget(Enemy enemy, PlayerAvatar player, RaidTarget target, RaidNavigationContext raid) =>
        raid != null && raid == NativeNavigation.Current && raid.Navigation && raid.Navigation.Ready &&
        enemy && player && player.isActiveAndEnabled && !Disabled(player) && Target(enemy) == player &&
        player.photonView && player.photonView.ViewID == TargetView(enemy) && target && target.isActiveAndEnabled &&
        target.gameObject == player.gameObject && target.Owner == raid.Navigation && target.HasSightPoint && target.Floor != null;

    private static bool BodyStep(EnemyRigidbody __instance)
    {
        foreach (var motor in motors)
            if (motor && motor.Leased && motor.NativeBody == __instance && motor.SuppressBodyStep) return false;
        return true;
    }

    private static bool NavStep(EnemyNavMeshAgent __instance)
    {
        foreach (var motor in motors)
            if (motor && motor.Leased && motor.NativeNav == __instance) return false;
        return true;
    }

    private static void HeadStep(EnemyHeadController __instance)
    {
        foreach (var motor in motors)
            if (motor && motor.Leased && motor.SuppressBodyStep && motor.Head == __instance &&
                __instance.LookAtTransform is { } look && look)
            {
                // Stock LookAt supplies the aim direction but assumes world up.
                // Preserve that direction and supply the support frame's roll.
                Vector3 forward = look.forward;
                if (Vector3.Cross(forward, motor.transform.up).sqrMagnitude > .00001f)
                    look.rotation = Quaternion.LookRotation(forward, motor.transform.up);
            }
    }

    internal static void Clear()
    {
        foreach (var motor in motors) if (motor) motor.Release(force: true);
        motors.Clear();
    }
}

[DefaultExecutionOrder(150)]
internal sealed class NativeMobiusMotor : MonoBehaviour
{
    private readonly Collider[] occupied = new Collider[24];
    private Enemy enemy;
    private Rigidbody body;
    private NavMeshAgent agent;
    private bool agentEnabled, wrapperEnabled, gravity, holdingNativeNav;
    private Vector3 bodyOffset;
    private Quaternion bodyRotation;
    private MobiusPursuit pursuit;
    private RaidEnemyProfile profile;
    private EnemyStateChase chase;
    private PlayerAvatar player;
    private RaidTarget target;
    private bool reachable;
    internal RaidNavigationContext Context { get; private set; }
    internal string RoomId => pursuit?.Floor.RoomId;
    internal EnemyRigidbody NativeBody { get; private set; }
    internal EnemyNavMeshAgent NativeNav { get; private set; }
    internal EnemyHeadController Head { get; private set; }
    internal bool Leased { get; private set; }
    internal bool SuppressBodyStep => isActiveAndEnabled && Leased && enemy && NativeBody && body &&
        !body.isKinematic && !NativeMobiusPursuit.Grabbed(NativeBody) && !enemy.IsStunned() && enemy.FreezeTimer <= 0;

    internal bool Bind(RaidNavigationContext context, Enemy owner, EnemyRigidbody nativeBody, Rigidbody rigidbody,
        EnemyNavMeshAgent wrapper, NavMeshAgent nativeAgent, RaidFloor floor, float speed)
    {
        profile = new RaidEnemyProfile(StockPortalPursuit.Radius, StockPortalPursuit.Height, 1.87f,
            Mathf.Clamp(speed, .1f, 10f), 30f, 360f, 5f, false, true);
        if (!context.Navigation.TryStand(floor, transform.position, profile, out _) ||
            !context.Navigation.BodyClear(transform.position, floor.Up, profile)) return false;
        Context = context; enemy = owner; NativeBody = nativeBody; body = rigidbody; NativeNav = wrapper; agent = nativeAgent;
        Head = owner.GetComponent<EnemyHeadController>();
        bodyOffset = Quaternion.Inverse(transform.rotation) * (body.position - transform.position);
        bodyRotation = Quaternion.Inverse(transform.rotation) * body.rotation;
        gravity = body.useGravity;
        if (!holdingNativeNav) { agentEnabled = agent.enabled; wrapperEnabled = wrapper.enabled; }
        holdingNativeNav = false;
        wrapper.enabled = wrapperEnabled;
        if (agent.enabled && agent.isOnNavMesh) agent.ResetPath();
        agent.enabled = false;
        pursuit = new MobiusPursuit(context.Navigation, floor, profile, transform, Write);
        reachable = true;
        Leased = true;
        return true;
    }

    internal void Chase(EnemyStateChase state, PlayerAvatar avatar, RaidTarget goal)
    {
        chase = state; player = avatar; target = goal;
        state.ChasePosition = goal.Feet;
        state.ChaseCanReach = reachable && state.VisionTimer > 0 && SuppressBodyStep;
    }

    private bool HasGoal => chase && enemy && enemy.CurrentState == EnemyState.Chase &&
        NativeMobiusPursuit.ValidTarget(enemy, player, target, Context) &&
        target.Floor.RoomId == RoomId && chase.VisionTimer > 0;

    private void FixedUpdate()
    {
        if (!Leased)
        {
            // Permanent authority/context loss revokes this motor immediately.
            // A small component-owned guard (not the global pursuit registry)
            // can restore the stock agent once its body reaches genuine stock
            // support. Until then only ordinary dynamic physics acts on it.
            if (holdingNativeNav)
            {
                if (CanReturnToStock())
                {
                    holdingNativeNav = false;
                    if (NativeNav) NativeNav.enabled = wrapperEnabled;
                    if (agent) agent.enabled = agentEnabled;
                }
            }
            return;
        }
        if (Context != NativeNavigation.Current || !NativeMobiusPursuit.Eligible(enemy)) { Release(force: true); return; }
        // The raid can be rebased around the local player before this step. The
        // independent native body is part of that rebase even when its AI root
        // is not parented to the stage. Reconstruct its live feet before moving.
        Vector3 expected = transform.position + transform.rotation * bodyOffset;
        bool displaced = Vector3.Distance(body.position, expected) > .25f;
        FollowPhysicalBody();
        if (!SuppressBodyStep || !HasGoal)
        {
            reachable = false;
            if (chase) chase.ChaseCanReach = false;
            StopMoving();
            if (CanReturnToStock()) Release();
            return;
        }
        // A grab/stun may have displaced the independent physical body. Reacquire
        // from its supported live feet; never snap it back to a remembered path.
        if (displaced && Context.TryGetFloor(transform.position, out var recovered) && recovered.RoomId == RoomId &&
            Vector3.Dot(transform.up, recovered.Up) > .999f)
        {
            StopMoving();
            if (!Context.Navigation.TryStand(recovered, transform.position, profile, out _)) return;
            pursuit = new MobiusPursuit(Context.Navigation, recovered, profile, transform, Write);
        }
        body.useGravity = false;
        bool advanced = pursuit.Advance(target.Floor, target.Feet, Time.fixedDeltaTime);
        reachable = advanced;
        chase.ChaseCanReach = advanced;
        if (!advanced) StopMoving();
        if (!pursuit.OnDeck && pursuit.Floor == target.Floor && CanReturnToStock()) Release();
    }

    private void FollowPhysicalBody()
    {
        if (!body || !enemy) return;
        Vector3 position = body.position;
        Quaternion rotation = body.rotation;
        Quaternion feetRotation = rotation * Quaternion.Inverse(bodyRotation);
        transform.SetPositionAndRotation(position - feetRotation * bodyOffset, feetRotation);
        body.position = position;
        body.rotation = rotation;
    }

    private bool Write(Pose pose)
    {
        if (!SuppressBodyStep || !HasGoal || Vector3.Distance(transform.position, pose.position) > .121f) return false;
        int mask = (1 << 10) | (1 << 11) | (1 << 18);
        int count = Physics.OverlapCapsuleNonAlloc(pose.position + pose.up * (.86f + RaidGeometry.Skin),
            pose.position + pose.up * (2.86f - .86f + RaidGeometry.Skin), .86f, occupied, mask, QueryTriggerInteraction.Ignore);
        bool blocked = count == occupied.Length;
        for (int i = 0; i < count && !blocked; i++)
            blocked = occupied[i] && occupied[i].attachedRigidbody != body && !occupied[i].transform.IsChildOf(transform);
        Array.Clear(occupied, 0, count);
        if (blocked) return false;
        Quaternion turn = pose.rotation * Quaternion.Inverse(transform.rotation);
        var head = enemy.GetComponent<EnemyHeadController>();
        if (head && head.LookAtTransform && !head.LookAtTransform.IsChildOf(transform))
            head.LookAtTransform.rotation = turn * head.LookAtTransform.rotation;
        transform.SetPositionAndRotation(pose.position, pose.rotation);
        body.position = pose.position + pose.rotation * bodyOffset;
        body.rotation = pose.rotation * bodyRotation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        return true;
    }

    private bool CanReturnToStock()
    {
        if (!body || !agent || !Context?.Navigation || pursuit == null ||
            !Context.TryGetFloor(transform.position, out var floor) || !StockPortalPursuit.WorldUp(floor) ||
            Vector3.Dot(transform.up, Vector3.up) < .9999f ||
            !Context.Navigation.TryStand(floor, transform.position, profile, out _) ||
            !StockPortalPursuit.TryNativePoint(transform.position, agent.agentTypeID, agent.areaMask, out var sample)) return false;
        return Vector3.Distance(sample, transform.position) <= StockPortalPursuit.PositionTolerance;
    }

    private void StopMoving()
    {
        if (body) body.useGravity = gravity;
    }

    internal void Release(bool force = false)
    {
        bool native = CanReturnToStock();
        StopMoving();
        if (agent) agent.enabled = agentEnabled && native;
        // At an inverted support the stock world-up agent stays off. The body is
        // dynamic again; this component keeps checking a genuine stock handoff.
        if (!force && !native && Context == NativeNavigation.Current && enemy && enemy.isActiveAndEnabled) return;
        Leased = false;
        holdingNativeNav = !native && agentEnabled;
        if (NativeNav) NativeNav.enabled = holdingNativeNav ? false : wrapperEnabled;
        chase = null; player = null; target = null;
    }

    private void OnDisable() { StopMoving(); if (agent) agent.enabled = agentEnabled && CanReturnToStock(); }
    private void OnDestroy() { StopMoving(); if (agent) agent.enabled = agentEnabled && CanReturnToStock(); Leased = false; }
}

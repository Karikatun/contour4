using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Navigation;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using NativeNavigation = Contour4.NativeRaid.Runtime.RaidNavigation;

namespace Contour4.NativeRaid.Enemies
{
    internal static class EnemyHooks
    {
        internal static readonly Guid SupportedGameModule = new Guid("bb352449-c1e7-4a41-9f75-0ed4e11521ea");
        private static readonly AccessTools.FieldRef<EnemyVision, Enemy> VisionEnemy =
            AccessTools.FieldRefAccess<EnemyVision, Enemy>("Enemy");
        private static readonly AccessTools.FieldRef<EnemyStateChase, Enemy> ChaseEnemy =
            AccessTools.FieldRefAccess<EnemyStateChase, Enemy>("Enemy");
        private static readonly AccessTools.FieldRef<EnemyHeadController, Enemy> HeadEnemy =
            AccessTools.FieldRefAccess<EnemyHeadController, Enemy>("Enemy");
        private static readonly AccessTools.FieldRef<Enemy, EnemyParent> Parent =
            AccessTools.FieldRefAccess<Enemy, EnemyParent>("EnemyParent");
        private static readonly AccessTools.FieldRef<EnemyParent, bool> Spawned =
            AccessTools.FieldRefAccess<EnemyParent, bool>("Spawned");
        private static readonly AccessTools.FieldRef<Enemy, bool> Master =
            AccessTools.FieldRefAccess<Enemy, bool>("MasterClient");
        private static readonly AccessTools.FieldRef<Enemy, EnemyVision> Vision =
            AccessTools.FieldRefAccess<Enemy, EnemyVision>("Vision");
        private static readonly AccessTools.FieldRef<Enemy, EnemyNavMeshAgent> Nav =
            AccessTools.FieldRefAccess<Enemy, EnemyNavMeshAgent>("NavMeshAgent");
        private static readonly AccessTools.FieldRef<EnemyNavMeshAgent, NavMeshAgent> Agent =
            AccessTools.FieldRefAccess<EnemyNavMeshAgent, NavMeshAgent>("Agent");
        private static readonly AccessTools.FieldRef<Enemy, EnemyRigidbody> Body =
            AccessTools.FieldRefAccess<Enemy, EnemyRigidbody>("Rigidbody");
        private static readonly AccessTools.FieldRef<EnemyRigidbody, Rigidbody> Rigidbody =
            AccessTools.FieldRefAccess<EnemyRigidbody, Rigidbody>("rb");
        private static readonly AccessTools.FieldRef<EnemyRigidbody, bool> Grabbed =
            AccessTools.FieldRefAccess<EnemyRigidbody, bool>("grabbed");
        private static readonly AccessTools.FieldRef<Enemy, PlayerAvatar> Target =
            AccessTools.FieldRefAccess<Enemy, PlayerAvatar>("TargetPlayerAvatar");
        private static readonly AccessTools.FieldRef<Enemy, int> TargetView =
            AccessTools.FieldRefAccess<Enemy, int>("TargetPlayerViewID");
        private static readonly AccessTools.FieldRef<PlayerAvatar, bool> PlayerDisabled =
            AccessTools.FieldRefAccess<PlayerAvatar, bool>("isDisabled");
        // HarmonyX is compiled against Mono's mscorlib. The netstandard facade cannot bind its Label field directly.
        private static readonly FieldInfo InstructionLabels = AccessTools.Field(typeof(CodeInstruction), "labels");
        private static readonly Dictionary<EnemyVision, ApproachPath> Approaches = new();

        internal static void Install(Harmony harmony)
        {
            if (typeof(Enemy).Module.ModuleVersionId != SupportedGameModule)
                throw new NotSupportedException("The stock portal AI adapter requires the audited R.E.P.O. 0.5.2 assembly.");
            var vision = AccessTools.EnumeratorMoveNext(AccessTools.DeclaredMethod(typeof(EnemyVision), "Vision"));
            var chase = AccessTools.DeclaredMethod(typeof(EnemyStateChase), "Update");
            var head = AccessTools.DeclaredMethod(typeof(EnemyHeadController), "Update");
            harmony.Patch(vision, transpiler: Patch(nameof(VisionCode)));
            harmony.Patch(chase, postfix: Patch(nameof(AfterChase)), transpiler: Patch(nameof(ChaseCode)));
            harmony.Patch(head, transpiler: Patch(nameof(HeadCode)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(EnemyVision), "OnDisable"),
                postfix: Patch(nameof(AfterVisionDisable)));
            NativeMobiusPursuit.Install(harmony);
        }

        private static HarmonyMethod Patch(string method) => new HarmonyMethod(typeof(EnemyHooks), method);

        internal static void Clear()
        {
            NativeMobiusPursuit.Clear();
            foreach (var approach in Approaches.Values)
                approach.Release();
            Approaches.Clear();
        }

        private static void AfterVisionDisable(EnemyVision __instance) => ReleaseApproach(__instance);

        private static void AfterChase(EnemyStateChase __instance)
        {
            Enemy enemy = ChaseEnemy(__instance);
            EnemyVision vision = enemy ? Vision(enemy) : null;
            if (!vision || !Approaches.TryGetValue(vision, out var approach))
                return;
            PlayerAvatar player = Target(enemy);
            if (enemy.CurrentState != EnemyState.Chase || player != approach.Player ||
                !TryScope(enemy, player, out var raid, out _, out _) || approach.Raid != raid)
                ReleaseApproach(vision);
        }

        private static void ReleaseApproach(EnemyVision vision)
        {
            if (Approaches.Remove(vision, out var approach))
                approach.Release();
        }

        private static bool TryScope(Enemy enemy, PlayerAvatar player, out RaidNavigationContext raid,
            out RaidFloor from, out RaidTarget target)
        {
            raid = NativeNavigation.Current;
            from = null;
            target = null;
            if (raid == null || !raid.Navigation || !raid.Navigation.Ready || !enemy || !enemy.isActiveAndEnabled ||
                !player || !player.isActiveAndEnabled || PlayerDisabled(player) || !Master(enemy) ||
                !GameManager.instance || SemiFunc.IsMultiplayer() || !StockPortalPursuit.WorldGravity(Physics.gravity) ||
                enemy.gameObject.scene != raid.Navigation.gameObject.scene ||
                !Parent(enemy) || !Spawned(Parent(enemy)) || Parent(enemy).enemyName != "Headman" ||
                !enemy.TryGetComponent<EnemyHeadController>(out _) ||
                !raid.TryGetFloor(enemy.transform.position, out from) || !StockPortalPursuit.WorldUp(from) ||
                !player.TryGetComponent(out target) || target.Owner != raid.Navigation || !target.HasSightPoint ||
                !StockPortalPursuit.WorldUp(target.Floor) || from == target.Floor)
                return false;
            return true;
        }

        private static NativeRaidPortal FindDoor(RaidNavigationContext raid, RaidFloor from, RaidFloor to)
        {
            NativeRaidPortal match = null;
            foreach (NativeRaidPortal door in raid.Portals)
            {
                if (!door.Door || door.EntryFloor != from || door.ExitFloor != to || !door.Entrance || !door.Exit)
                    continue;
                if (match != null)
                    return null;
                match = door;
            }
            return match;
        }

        private static bool TryPortalDirection(EnemyVision vision, PlayerAvatar player, out Vector3 direction)
        {
            direction = default;
            if (!vision || !TryScope(VisionEnemy(vision), player, out var raid, out var from, out var target))
                return false;
            return TryPortalDirection(vision, player, raid, from, target, out direction);
        }

        private static bool TryPortalDirection(EnemyVision vision, PlayerAvatar player, RaidNavigationContext raid,
            RaidFloor from, RaidTarget target, out Vector3 direction)
        {
            direction = default;
            NativeRaidPortal reverse = FindDoor(raid, target.Floor, from);
            if (reverse == null || !vision || !vision.VisionTransform ||
                !NativeRaidModule.RefreshTargetEye(target, player.PlayerVisionTarget))
                return false;
            Vector3 eye = vision.VisionTransform.position;
            Vector3 apparent = reverse.MapPoint(target.SightPoint);
            var body = Body(VisionEnemy(vision));
            if (!StockPortalPursuit.SeesPortal(raid.Navigation, from, eye, target, apparent, body ? Rigidbody(body) : null))
                return false;
            direction = apparent - eye;
            return true;
        }

        private static Vector3 LookThroughDoor(Vector3 actual, EnemyHeadController head)
        {
            Enemy enemy = HeadEnemy(head);
            if (!enemy)
                return actual;
            EnemyVision vision = Vision(enemy);
            return TryPortalDirection(vision, Target(enemy), out Vector3 direction)
                ? vision.VisionTransform.position + direction
                : actual;
        }

        private static bool DriveThroughDoor(EnemyStateChase state)
        {
            Enemy enemy = ChaseEnemy(state);
            if (!enemy)
                return false;
            if (NativeMobiusPursuit.Drive(enemy, state)) return true;
            PlayerAvatar player = Target(enemy);
            if (!TryScope(enemy, player, out var raid, out var from, out var target) ||
                !player.photonView || player.photonView.ViewID != TargetView(enemy) ||
                FindDoor(raid, from, target.Floor) == null)
                return false;

            EnemyNavMeshAgent wrapper = Nav(enemy);
            EnemyVision vision = Vision(enemy);
            NavMeshAgent agent = wrapper ? Agent(wrapper) : null;
            if (!vision || !agent || agent.agentTypeID != StockPortalPursuit.AgentType || agent.areaMask != 1 ||
                !agent.isActiveAndEnabled || !agent.isOnNavMesh)
                return true;

            var navigation = raid.Navigation;
            Vector3 start = enemy.transform.position;
            EnemyRigidbody body = Body(enemy);
            Rigidbody traveller = body ? Rigidbody(body) : null;
            if (!StockPortalPursuit.TryRoute(navigation, from, start, target.Floor, target.Feet, out var plan, traveller))
                return StopRoute(state, vision, agent, start);
            if (plan.Edge == null || plan.Edge.Kind != RaidRouteKind.Portal)
            {
                ReleaseApproach(vision);
                return false;
            }

            if (!traveller || !body.followTarget || Grabbed(body) ||
                enemy.IsStunned() || enemy.FreezeTimer > 0 || agent.isStopped ||
                state.VisionTimer <= 0 ||
                !TryPortalDirection(vision, player, raid, from, target, out _) ||
                !navigation.TryTransition(plan.Edge, StockPortalPursuit.Headman, out var approach, out var landing, traveller) ||
                !StockPortalPursuit.TryNativePoint(approach, agent.agentTypeID, agent.areaMask, out var nativeApproach) ||
                !StockPortalPursuit.TryNativePath(agent.nextPosition, nativeApproach, agent.agentTypeID, agent.areaMask,
                    out var approachPath))
                return StopRoute(state, vision, agent, start);

            state.ChasePosition = nativeApproach;
            state.ChaseCanReach = true;
            if (Vector3.Distance(agent.nextPosition, nativeApproach) > StockPortalPursuit.PositionTolerance)
            {
                if (!agent.hasPath || Vector3.Distance(agent.destination, nativeApproach) > .05f)
                {
                    if (!agent.SetPath(approachPath))
                        return StopRoute(state, vision, agent, start);
                    Approaches[vision] = new ApproachPath(raid, player, agent, nativeApproach);
                }
                return true;
            }

            if (Vector3.ProjectOnPlane(traveller.position - approach, Vector3.up).magnitude > .65f)
                return true;
            if (!StockPortalPursuit.TryNativePoint(landing, agent.agentTypeID, agent.areaMask, out var nativeLanding) ||
                !navigation.TryStand(plan.Edge.To, nativeLanding, out _) ||
                !navigation.BodyClear(nativeLanding, Vector3.up, StockPortalPursuit.Headman) ||
                Vector3.ProjectOnPlane(target.Feet - nativeLanding, Vector3.up).magnitude <
                    StockPortalPursuit.Radius + .35f ||
                !navigation.TryProjectPursuitGoal(plan.Edge.To, target.Feet, StockPortalPursuit.Headman, out var goal) ||
                !StockPortalPursuit.TryNativePoint(goal, agent.agentTypeID, agent.areaMask, out var nativeGoal) ||
                !StockPortalPursuit.TryNativePath(nativeLanding, nativeGoal, agent.agentTypeID, agent.areaMask, out _) ||
                StockPortalPursuit.LandingOccupied(navigation.gameObject.scene.GetPhysicsScene(),
                    nativeLanding, traveller) ||
                !StockPortalPursuit.Warp(agent, nativeLanding))
                return StopRoute(state, vision, agent, start);

            Approaches.Remove(vision);
            var head = enemy.GetComponent<EnemyHeadController>();
            enemy.transform.rotation = plan.Edge.Rotation * enemy.transform.rotation;
            if (head.LookAtTransform && head.LookAtTransform != enemy.transform &&
                !head.LookAtTransform.IsChildOf(enemy.transform))
                head.LookAtTransform.rotation = plan.Edge.Rotation * head.LookAtTransform.rotation;
            enemy.EnemyTeleported(nativeLanding);
            state.ChasePosition = nativeGoal;
            state.VisionTimer = state.VisionTime;
            wrapper.SetDestination(nativeGoal);
            return true;
        }

        private static bool StopRoute(EnemyStateChase state, EnemyVision vision, NavMeshAgent agent, Vector3 position)
        {
            Approaches.Remove(vision);
            if (agent.hasPath || agent.pathPending)
                agent.ResetPath();
            state.ChasePosition = position;
            state.ChaseCanReach = false;
            return true;
        }

        private readonly struct ApproachPath
        {
            internal RaidNavigationContext Raid { get; }
            internal PlayerAvatar Player { get; }
            private readonly NavMeshAgent agent;
            private readonly Vector3 destination;

            internal ApproachPath(RaidNavigationContext raid, PlayerAvatar player, NavMeshAgent agent, Vector3 destination)
            {
                Raid = raid;
                Player = player;
                this.agent = agent;
                this.destination = destination;
            }

            internal void Release()
            {
                if (agent && agent.isActiveAndEnabled && agent.isOnNavMesh && agent.hasPath &&
                    Vector3.Distance(agent.destination, destination) < .05f)
                    agent.ResetPath();
            }
        }

        // The audited iterator receives the same PlayerAvatar and runs the original range, posture, dot,
        // exposure counters and VisionTrigger. The two-segment aperture ray replaces just its player ray.
        private static IEnumerable<CodeInstruction> VisionCode(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var code = instructions.ToList();
            if (code.Count != 761 || code[334].opcode != OpCodes.Ldloc_1 || code[342].opcode != OpCodes.Stloc_S ||
                code[357].opcode != OpCodes.Ldloc_S || code[391].opcode != OpCodes.Stloc_S ||
                code[392].opcode != OpCodes.Ldloc_1 || code[531].opcode != OpCodes.Ldloc_1 ||
                !code[689].Calls(AccessTools.DeclaredMethod(typeof(EnemyVision), nameof(EnemyVision.VisionTrigger))))
                throw new InvalidOperationException("Unexpected stock EnemyVision iterator shape.");

            LocalBuilder direction = il.DeclareLocal(typeof(Vector3));
            LocalBuilder throughPortal = il.DeclareLocal(typeof(bool));
            var regularDistance = il.DefineLabel();
            var regularRay = il.DefineLabel();
            var posture = il.DefineLabel();
            var nativeDot = il.DefineLabel();
            Labels(code[357]).Add(posture);
            Labels(code[531]).Add(nativeDot);
            var beforeDistance = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldloc_1),
                new CodeInstruction(OpCodes.Ldloc, code[357].operand),
                new CodeInstruction(OpCodes.Ldloca, direction),
                new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(EnemyHooks),
                    nameof(TryPortalDirection), new[] { typeof(EnemyVision), typeof(PlayerAvatar), typeof(Vector3).MakeByRefType() })),
                new CodeInstruction(OpCodes.Stloc, throughPortal),
                new CodeInstruction(OpCodes.Ldloc, throughPortal),
                new CodeInstruction(OpCodes.Brfalse, regularDistance),
                new CodeInstruction(OpCodes.Ldloca, direction),
                new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Vector3), nameof(Vector3.magnitude))),
                new CodeInstruction(OpCodes.Stloc, code[342].operand),
                new CodeInstruction(OpCodes.Br, posture)
            };
            var beforeRay = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldloc, throughPortal),
                new CodeInstruction(OpCodes.Brfalse, regularRay),
                new CodeInstruction(OpCodes.Ldloc, direction),
                new CodeInstruction(OpCodes.Stloc, code[391].operand),
                new CodeInstruction(OpCodes.Br, nativeDot)
            };
            MoveLabels(code[334], beforeDistance[0], regularDistance);
            MoveLabels(code[392], beforeRay[0], regularRay);
            // Portal visibility comes from the aperture, rather than stock culling of the remote world position.
            code.InsertRange(688, new[]
            {
                new CodeInstruction(OpCodes.Ldloc, throughPortal),
                new CodeInstruction(OpCodes.Ldc_I4_0),
                new CodeInstruction(OpCodes.Ceq),
                new CodeInstruction(OpCodes.And)
            });
            code.InsertRange(392, beforeRay);
            code.InsertRange(334, beforeDistance);
            return LongBranches(code);
        }

        // Leave stock target identity, chase initialization and timeout intact. Skip the direct-position,
        // unreachable and world-space SetDestination branches only while the native player is across a door.
        private static IEnumerable<CodeInstruction> ChaseCode(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var code = instructions.ToList();
            if (code.Count != 345 || code[92].opcode != OpCodes.Ldarg_0 ||
                !code[93].LoadsField(AccessTools.Field(typeof(EnemyStateChase), nameof(EnemyStateChase.VisionTimer))) ||
                code[255].opcode != OpCodes.Ldarg_0 ||
                !code[256].LoadsField(AccessTools.Field(typeof(EnemyStateChase), nameof(EnemyStateChase.ChaseCanReach))))
                throw new InvalidOperationException("Unexpected stock EnemyStateChase update shape.");
            Label continueTimers = il.DefineLabel();
            Labels(code[255]).Add(continueTimers);
            var loadThis = new CodeInstruction(OpCodes.Ldarg_0);
            MoveLabels(code[92], loadThis);
            code.InsertRange(92, new[]
            {
                loadThis,
                new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(EnemyHooks), nameof(DriveThroughDoor))),
                new CodeInstruction(OpCodes.Brtrue, continueTimers)
            });
            return LongBranches(code);
        }

        private static IEnumerable<CodeInstruction> HeadCode(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var look = AccessTools.DeclaredMethod(typeof(Transform), nameof(Transform.LookAt), new[] { typeof(Vector3) });
            var project = AccessTools.DeclaredMethod(typeof(EnemyHooks), nameof(LookThroughDoor));
            if (code.Count != 298 || !code[160].Calls(look) || !code[204].Calls(look) || !code[223].Calls(look))
                throw new InvalidOperationException("Unexpected stock Headman look update shape.");
            foreach (int index in new[] { 223, 204, 160 })
            {
                var loadThis = new CodeInstruction(OpCodes.Ldarg_0);
                MoveLabels(code[index], loadThis);
                code.InsertRange(index, new[] { loadThis, new CodeInstruction(OpCodes.Call, project) });
            }
            return LongBranches(code);
        }

        private static List<Label> Labels(CodeInstruction instruction) =>
            (List<Label>) InstructionLabels.GetValue(instruction);

        private static void MoveLabels(CodeInstruction original, CodeInstruction first, Label? originalTarget = null)
        {
            var originalLabels = Labels(original);
            Labels(first).AddRange(originalLabels);
            originalLabels.Clear();
            if (originalTarget.HasValue)
                originalLabels.Add(originalTarget.Value);
        }

        private static IEnumerable<CodeInstruction> LongBranches(IEnumerable<CodeInstruction> code)
        {
            foreach (var instruction in code)
            {
                if (instruction.opcode.OperandType == OperandType.ShortInlineBrTarget)
                    instruction.opcode = LongerBranch[instruction.opcode];
                yield return instruction;
            }
        }

        private static readonly Dictionary<OpCode, OpCode> LongerBranch = new Dictionary<OpCode, OpCode>
        {
            [OpCodes.Br_S] = OpCodes.Br, [OpCodes.Brfalse_S] = OpCodes.Brfalse, [OpCodes.Brtrue_S] = OpCodes.Brtrue,
            [OpCodes.Beq_S] = OpCodes.Beq, [OpCodes.Bne_Un_S] = OpCodes.Bne_Un,
            [OpCodes.Bge_S] = OpCodes.Bge, [OpCodes.Bge_Un_S] = OpCodes.Bge_Un,
            [OpCodes.Bgt_S] = OpCodes.Bgt, [OpCodes.Bgt_Un_S] = OpCodes.Bgt_Un,
            [OpCodes.Ble_S] = OpCodes.Ble, [OpCodes.Ble_Un_S] = OpCodes.Ble_Un,
            [OpCodes.Blt_S] = OpCodes.Blt, [OpCodes.Blt_Un_S] = OpCodes.Blt_Un, [OpCodes.Leave_S] = OpCodes.Leave
        };
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using Contour4.NativeRaid.Runtime;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Events;

namespace Contour4.NativeRaid;

internal static class NativeRaidCabinetGrab
{
    private const float StockFallHeight = -50f;

    internal static void Install(CabinetFront front) => Install(front.Body, front.Parts);

    internal static void Install(Rigidbody body, IReadOnlyList<Collider> parts)
    {
        var actor = body.gameObject;
        if (actor.TryGetComponent<NativeRaidCabinetMount>(out _)) return;
        var joint = actor.GetComponent<Joint>();
        var mount = joint ? joint.connectedBody : null;
        if (!mount || !mount.isKinematic) throw new InvalidOperationException("The cabinet front requires a room-local anchor.");

        int hingeLayer = LayerMask.NameToLayer("PhysGrabObjectHinge");
        if (hingeLayer < 0) throw new InvalidOperationException("The stock furniture collision layer is unavailable.");

        actor.SetActive(false);
        actor.layer = hingeLayer;
        actor.tag = "Phys Grab Object";
        int obstructionLayers = LayerMask.GetMask("PhysGrabObjectCart", "Enemy");
        foreach (Collider part in parts)
        {
            part.gameObject.layer = hingeLayer;
            part.gameObject.tag = "Phys Grab Object";
            // Stock hinges ignore cart chassis and enemies. These fronts also guard the raid's cargo lanes.
            part.includeLayers = obstructionLayers;
            part.layerOverridePriority = 1;
            part.gameObject.AddComponent<PhysGrabObjectCollider>();
        }

        // PhysGrabObject's stock lifecycle needs both components in solo. The view keeps its default ID 0.
        actor.AddComponent<PhotonView>();
        actor.AddComponent<PhotonTransformView>();
        // The stock grab grid and held torque both read this sibling. Fixed furniture uses it without loose-object impact updates.
        var detector = actor.AddComponent<PhysGrabObjectImpactDetector>();
        detector.destroyDisable = true;
        detector.particleDisable = true;
        // Scripted break impulses still reach native BreakRPC even when detector updates are disabled.
        detector.onAllBreaks ??= new UnityEvent();
        detector.onBreakLight ??= new UnityEvent();
        detector.onBreakMedium ??= new UnityEvent();
        detector.onBreakHeavy ??= new UnityEvent();
        detector.enabled = false;
        var grab = actor.AddComponent<PhysGrabObject>();
        grab.overrideTagsAndLayers = true;
        grab.ignoreGrabPointCentering = true;
        var marker = actor.AddComponent<NativeRaidCabinetMount>();
        marker.Joint = joint;
        marker.Anchor = mount;
        actor.SetActive(true);
    }

    internal static float FallHeight(PhysGrabObject actor)
    {
        if (actor.TryGetComponent<NativeRaidCabinetMount>(out var marker) && marker.IsAnchored) return float.NegativeInfinity;
        return NativeRaidGravity.FellInRotaryVoid(actor.rb) ? float.PositiveInfinity : StockFallHeight;
    }

    [HarmonyPatch(typeof(PhysGrabObject), "Update")]
    internal static class FallHeightPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var authority = AccessTools.Method(typeof(SemiFunc), nameof(SemiFunc.IsMasterClientOrSingleplayer));
            var transform = AccessTools.PropertyGetter(typeof(Component), nameof(Component.transform));
            var position = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position));
            var y = AccessTools.Field(typeof(Vector3), nameof(Vector3.y));
            var detector = AccessTools.Field(typeof(PhysGrabObject), "impactDetector");
            var destroyDisable = AccessTools.Field(typeof(PhysGrabObjectImpactDetector), "destroyDisable");
            // HarmonyX links its label list against Mono's mscorlib.
            var labels = AccessTools.Field(typeof(CodeInstruction), "labels");
            int match = -1;
            for (int i = 6; i + 4 < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldc_R4 || code[i].operand is not float height || height != StockFallHeight ||
                    !code[i - 6].Calls(authority) || !code[i - 3].Calls(transform) || !code[i - 2].Calls(position) ||
                    !code[i - 1].LoadsField(y) || !code[i + 3].LoadsField(detector) || !code[i + 4].LoadsField(destroyDisable)) continue;

                var authorityExit = code[i - 5];
                var fallExit = code[i + 1];
                if ((authorityExit.opcode != OpCodes.Brfalse && authorityExit.opcode != OpCodes.Brfalse_S) ||
                    (fallExit.opcode != OpCodes.Bge_Un && fallExit.opcode != OpCodes.Bge_Un_S) ||
                    code[i - 4].opcode != OpCodes.Ldarg_0 || code[i + 2].opcode != OpCodes.Ldarg_0) continue;
                bool sameReturn = code.FindIndex(i + 5, instruction =>
                {
                    if (instruction.opcode != OpCodes.Ret) return false;
                    var destinations = (IList)labels.GetValue(instruction);
                    return destinations.Contains(authorityExit.operand) && destinations.Contains(fallExit.operand);
                }) >= 0;
                if (!sameReturn) continue;
                if (match != -1) throw new InvalidOperationException("The stock cabinet fall comparison is ambiguous.");
                match = i;
            }
            if (match == -1) throw new InvalidOperationException("The stock cabinet fall comparison is unavailable.");

            // Replace only the threshold. Stock still decides whether to destroy or recover the object.
            code[match].opcode = OpCodes.Ldarg_0;
            code[match].operand = null;
            code.Insert(match + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NativeRaidCabinetGrab), nameof(FallHeight))));
            return code;
        }
    }
}

internal sealed class NativeRaidCabinetMount : MonoBehaviour
{
    internal Joint Joint;
    internal Rigidbody Anchor;

    internal bool IsAnchored => isActiveAndEnabled && Joint && Anchor && Anchor.isKinematic && Joint.connectedBody == Anchor;
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Contour4.NativeRaid;

[HarmonyPatch(typeof(PlayerController), "FixedUpdate")]
internal static class NativeRaidSphericalFallPatch
{
    // Run after the ring's patch, which also recognizes the stock fall impulse assembly.
    // Leave either impulse receiver and its original arguments in place.
    [HarmonyPriority(Priority.Last)]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var customGravity = AccessTools.Field(typeof(PlayerController), "CustomGravity");
        var vector = AccessTools.Constructor(typeof(Vector3), new[] { typeof(float), typeof(float), typeof(float) });
        var timeStep = AccessTools.PropertyGetter(typeof(Time), nameof(Time.fixedDeltaTime));
        var capture = AccessTools.Method(typeof(NativeRaidGravity), nameof(NativeRaidGravity.CaptureFallImpulse));
        var sites = new List<(int Insert, bool Reduced)>();

        for (int start = 0; start + 9 < code.Count; start++)
        {
            if (!IsFloat(code[start], 0) || code[start + 1].opcode != OpCodes.Ldarg_0 ||
                !code[start + 2].LoadsField(customGravity)) continue;
            int negate = start + 3;
            bool reduced = IsFloat(code[negate], .1f) && code[negate + 1].opcode == OpCodes.Mul;
            if (reduced) negate += 2;
            int mode = negate + 5;
            if (mode + 1 < code.Count && code[negate].opcode == OpCodes.Neg && code[negate + 1].Calls(timeStep) &&
                code[negate + 2].opcode == OpCodes.Mul && IsFloat(code[negate + 3], 0) &&
                code[negate + 4].opcode == OpCodes.Newobj && Equals(code[negate + 4].operand, vector) &&
                code[mode].opcode == OpCodes.Ldc_I4_1 &&
                (code[mode + 1].opcode == OpCodes.Call || code[mode + 1].opcode == OpCodes.Callvirt))
                sites.Add((mode, reduced));
        }

        if (sites.Count != 2 || sites[0].Reduced == sites[1].Reduced)
        {
            Debug.LogError("[Contour-4] Stock player fall impulses could not be bound for the spherical chamber.");
            return code;
        }

        for (int site = sites.Count - 1; site >= 0; site--)
        {
            int insert = sites[site].Insert;
            var loadController = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code[insert]);
            code.Insert(insert, loadController);
            code.Insert(insert + 1, new CodeInstruction(OpCodes.Call, capture));
        }
        return code;
    }

    private static bool IsFloat(CodeInstruction instruction, float expected) =>
        instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value && value == expected;
}

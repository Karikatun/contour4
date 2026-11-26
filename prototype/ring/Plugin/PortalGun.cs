using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Route = SpaceJourney.PortalWeaponTrace.Route;

namespace SpaceJourney;

internal static class PortalGun
{
    private readonly struct Shot(Route route, bool enclosed)
    {
        internal Route Route { get; } = route;
        internal bool Enclosed { get; } = enclosed;
    }

    [ThreadStatic] private static Shot? shot;
    [ThreadStatic] private static RaycastHit[] exitHits;

    [HarmonyPatch(typeof(ItemGun), "AimPositionGet")]
    private static class NativeAim
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var nonAlloc = AccessTools.Method(typeof(Physics), nameof(Physics.RaycastNonAlloc),
                new[] { typeof(Vector3), typeof(Vector3), typeof(RaycastHit[]), typeof(float), typeof(int),
                    typeof(QueryTriggerInteraction) });
            var all = AccessTools.Method(typeof(Physics), nameof(Physics.RaycastAll),
                new[] { typeof(Vector3), typeof(Vector3), typeof(float), typeof(int), typeof(QueryTriggerInteraction) });
            return ReplaceCalls(code, nonAlloc, nameof(AimNonAlloc), all, nameof(AimAll));
        }
    }

    [HarmonyPatch(typeof(ItemGun), "ShootRPC")]
    private static class NativeFire
    {
        private static void Prefix(out Shot? __state)
        {
            __state = shot;
            shot = null;
        }

        private static void Finalizer(Shot? __state) => shot = __state;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var ray = AccessTools.Method(typeof(Physics), nameof(Physics.Raycast),
                new[] { typeof(Vector3), typeof(Vector3), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int) });
            return ReplaceCalls(code, ray, nameof(FireRay));
        }
    }

    [HarmonyPatch(typeof(ItemGun), "ShootBullet")]
    private static class NativeBulletEndpoint
    {
        private static void Prefix(ref Vector3 __0, ref bool __1, bool __2)
        {
            if (__2 || shot is not Shot path)
                return;
            // The stock no-contact branch constructs an endpoint from the muzzle again.
            __0 = path.Enclosed ? path.Route.Exit : path.Route.End;
            if (path.Enclosed)
                __1 = false;
        }
    }

    [HarmonyPatch(typeof(ItemGunBullet), "ActivateAll")]
    private static class NativeBulletEffects
    {
        private static void Prefix(ItemGunBullet __instance, out Route? __state)
        {
            __state = null;
            if (shot is not Shot path)
                return;
            Route route = path.Route;
            __state = route;
            __instance.transform.SetPositionAndRotation(route.Exit, Quaternion.LookRotation(route.ExitDirection,
                route.Door.Rotation * __instance.transform.up));
        }

        private static void Postfix(ItemGunBullet __instance, Route? __state, LineRenderer ___shootLine,
            Vector3 ___hitPosition)
        {
            if (__state is not Route route || !___shootLine)
                return;
            SetLine(___shootLine, route.Exit, ___hitPosition);
            if (route.Distance <= .001f)
                return;

            var source = new GameObject("Portal shot entrance");
            source.transform.SetParent(__instance.transform, false);
            var line = source.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterials = ___shootLine.sharedMaterials;
            line.widthCurve = ___shootLine.widthCurve;
            line.widthMultiplier = ___shootLine.widthMultiplier;
            line.colorGradient = ___shootLine.colorGradient;
            line.alignment = ___shootLine.alignment;
            line.textureMode = ___shootLine.textureMode;
            line.numCapVertices = ___shootLine.numCapVertices;
            line.numCornerVertices = ___shootLine.numCornerVertices;
            line.shadowCastingMode = ___shootLine.shadowCastingMode;
            line.receiveShadows = ___shootLine.receiveShadows;
            line.sortingLayerID = ___shootLine.sortingLayerID;
            line.sortingOrder = ___shootLine.sortingOrder;
            SetLine(line, route.Origin, route.Entrance);
            source.AddComponent<PortalShotLine>().Bind(line, ___shootLine);
        }

        private static void SetLine(LineRenderer line, Vector3 start, Vector3 end)
        {
            float step = Mathf.Min(.5f, Vector3.Distance(start, end) / 3f);
            line.positionCount = 4;
            line.SetPosition(0, start);
            line.SetPosition(1, Vector3.MoveTowards(start, end, step));
            line.SetPosition(2, Vector3.MoveTowards(end, start, step));
            line.SetPosition(3, end);
        }
    }

    private static IEnumerable<CodeInstruction> ReplaceCalls(IEnumerable<CodeInstruction> code,
        MethodInfo first, string firstReplacement, MethodInfo second = null, string secondReplacement = null)
    {
        int foundFirst = 0, foundSecond = 0;
        foreach (var instruction in code)
        {
            if (instruction.Calls(first))
            {
                foundFirst++;
                instruction.operand = AccessTools.Method(typeof(PortalGun), firstReplacement);
            }
            else if (second != null && instruction.Calls(second))
            {
                foundSecond++;
                instruction.operand = AccessTools.Method(typeof(PortalGun), secondReplacement);
            }
            yield return instruction;
        }
        if (foundFirst != 1 || (second != null && foundSecond != 1))
            throw new InvalidOperationException("Contour-4 found a different native firearm query layout.");
    }

    private static int AimNonAlloc(Vector3 origin, Vector3 direction, RaycastHit[] results, float range,
        int mask, QueryTriggerInteraction triggers)
    {
        if (!PortalWeaponTrace.TryRoute(origin, direction, range, out var route))
            return Physics.RaycastNonAlloc(origin, direction, results, range, mask, triggers);

        int count = Physics.RaycastNonAlloc(origin, direction, results, route.Distance, mask, triggers);
        if (count >= results.Length || ExitBlocked(route, mask, triggers))
            return count;

        exitHits ??= new RaycastHit[32];
        int remote = Physics.RaycastNonAlloc(route.Exit, route.ExitDirection, exitHits, route.Remaining, mask, triggers);
        if (remote >= exitHits.Length || count + remote >= results.Length)
            return results.Length; // The original method retries through AimAll in this case.

        for (int i = 0; i < remote; i++)
        {
            var hit = exitHits[i];
            hit.distance += route.Distance;
            results[count++] = hit;
        }
        return count;
    }

    private static RaycastHit[] AimAll(Vector3 origin, Vector3 direction, float range, int mask,
        QueryTriggerInteraction triggers)
    {
        if (!PortalWeaponTrace.TryRoute(origin, direction, range, out var route))
            return Physics.RaycastAll(origin, direction, range, mask, triggers);

        var near = Physics.RaycastAll(origin, direction, route.Distance, mask, triggers);
        if (ExitBlocked(route, mask, triggers))
            return near;

        var far = Physics.RaycastAll(route.Exit, route.ExitDirection, route.Remaining, mask, triggers);
        if (far.Length == 0)
            return near;

        var all = new RaycastHit[near.Length + far.Length];
        Array.Copy(near, all, near.Length);
        for (int i = 0; i < far.Length; i++)
        {
            var hit = far[i];
            hit.distance += route.Distance;
            all[near.Length + i] = hit;
        }
        return all;
    }

    private static bool ExitBlocked(Route route, int mask, QueryTriggerInteraction triggers) =>
        !PortalWeaponTrace.InsideExit(route.Door, route.Exit) ||
        PortalWeaponTrace.Enclosed(route.Exit, mask, triggers);

    private static bool FireRay(Vector3 origin, Vector3 direction, out RaycastHit hit, float range, int mask)
    {
        shot = null;
        if (!PortalWeaponTrace.TryRoute(origin, direction, range, out var route))
            return Physics.Raycast(origin, direction, out hit, range, mask);
        if (Physics.Raycast(origin, direction, out hit, route.Distance, mask))
            return true;

        bool enclosed = ExitBlocked(route, mask, QueryTriggerInteraction.UseGlobal);
        shot = new Shot(route, enclosed);
        if (enclosed)
            return false;

        return Physics.Raycast(route.Exit, route.ExitDirection, out hit, route.Remaining, mask);
    }
}

internal sealed class PortalShotLine : MonoBehaviour
{
    private LineRenderer source;
    private LineRenderer destination;

    internal void Bind(LineRenderer start, LineRenderer finish) => (source, destination) = (start, finish);

    private void LateUpdate()
    {
        source.enabled = destination && destination.enabled && destination.gameObject.activeInHierarchy;
        if (source.enabled)
            source.widthMultiplier = destination.widthMultiplier;
    }
}

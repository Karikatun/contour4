using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Route = SpaceJourney.PortalWeaponTrace.Route;

namespace SpaceJourney;

internal static class PortalMelee
{
    private static readonly ConditionalWeakTable<HurtCollider, ItemMelee> Weapons = new();
    private static readonly AccessTools.FieldRef<ItemMelee, Transform> Hilt =
        AccessTools.FieldRefAccess<ItemMelee, Transform>("forceGrabPoint");
    private static readonly AccessTools.FieldRef<HurtCollider, LayerMask> WallMask =
        AccessTools.FieldRefAccess<HurtCollider, LayerMask>("RayMask");

    [ThreadStatic] private static ContactPass pass;

    private sealed class ContactPass(HurtCollider owner, Route route, HashSet<Collider> exitContacts)
    {
        internal HurtCollider Owner { get; } = owner;
        internal Route Route { get; } = route;
        internal HashSet<Collider> ExitContacts { get; } = exitContacts;
        internal bool Remote { get; set; }
    }

    [HarmonyPatch(typeof(ItemMelee), "Start")]
    private static class NativeMeleeOwner
    {
        private static void Postfix(ItemMelee __instance, HurtCollider ___hurtCollider)
        {
            if (___hurtCollider)
                Weapons.GetValue(___hurtCollider, _ => __instance);
        }
    }

    [HarmonyPatch]
    private static class NativeContacts
    {
        private static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(HurtCollider), "ColliderCheck"));

        private static void Prefix(out ContactPass __state)
        {
            __state = pass;
            pass = null;
        }

        private static void Finalizer(ContactPass __state) => pass = __state;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var box = AccessTools.Method(typeof(Physics), nameof(Physics.OverlapBox),
                new[] { typeof(Vector3), typeof(Vector3), typeof(Quaternion), typeof(int), typeof(QueryTriggerInteraction) });
            var sphere = AccessTools.Method(typeof(Physics), nameof(Physics.OverlapSphere),
                new[] { typeof(Vector3), typeof(float), typeof(int), typeof(QueryTriggerInteraction) });
            var iteratorOwner = AccessTools.Field(TargetMethod().DeclaringType, "<>4__this");
            var mark = AccessTools.Method(typeof(PortalMelee), nameof(MarkContact));
            int boxes = 0, spheres = 0, contacts = 0;
            foreach (var instruction in code)
            {
                if (instruction.Calls(box) || instruction.Calls(sphere))
                {
                    bool isBox = instruction.Calls(box);
                    if (isBox) boxes++;
                    else spheres++;
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    instruction.MoveLabelsTo(load);
                    yield return load;
                    yield return new CodeInstruction(OpCodes.Ldfld, iteratorOwner);
                    instruction.operand = AccessTools.Method(typeof(PortalMelee), isBox ? nameof(Box) : nameof(Sphere));
                }
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldelem_Ref)
                {
                    contacts++;
                    yield return new CodeInstruction(OpCodes.Call, mark);
                }
            }
            if (boxes != 1 || spheres != 1 || contacts != 1)
                throw new InvalidOperationException("Contour-4 found a different native melee contact loop.");
        }
    }

    [HarmonyPatch(typeof(HurtCollider), "CanHit")]
    private static class NativeWallCheck
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var stock = AccessTools.Method(typeof(Physics), nameof(Physics.RaycastAll),
                new[] { typeof(Vector3), typeof(Vector3), typeof(float), typeof(int), typeof(QueryTriggerInteraction) });
            int calls = 0;
            foreach (var instruction in code)
            {
                if (instruction.Calls(stock))
                {
                    calls++;
                    var owner = new CodeInstruction(OpCodes.Ldarg_0);
                    instruction.MoveLabelsTo(owner);
                    yield return owner;
                    instruction.operand = AccessTools.Method(typeof(PortalMelee), nameof(WallRay));
                }
                yield return instruction;
            }
            if (calls != 1)
                throw new InvalidOperationException("Contour-4 found a different native melee wall check.");
        }
    }

    [HarmonyPatch]
    private static class NativeImpactVectors
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(HurtCollider), "PhysObjectHurt");
            yield return AccessTools.Method(typeof(HurtCollider), "PlayerHurt");
            yield return AccessTools.Method(typeof(HurtCollider), "EnemyHurt");
            yield return AccessTools.Method(typeof(HurtCollider), "HitForceGet");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var bounds = AccessTools.PropertyGetter(typeof(Collider), nameof(Collider.bounds));
            var forward = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.forward));
            var position = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position));
            foreach (var instruction in code)
            {
                string replacement = instruction.Calls(bounds) ? nameof(ImpactBounds) :
                    instruction.Calls(forward) ? nameof(ImpactForward) :
                    instruction.Calls(position) ? nameof(ImpactPosition) : null;
                if (replacement != null)
                {
                    var owner = new CodeInstruction(OpCodes.Ldarg_0);
                    instruction.MoveLabelsTo(owner);
                    yield return owner;
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(PortalMelee), replacement);
                }
                yield return instruction;
            }
        }
    }

    private static Collider[] Box(Vector3 center, Vector3 half, Quaternion rotation, int mask,
        QueryTriggerInteraction triggers, HurtCollider owner)
    {
        var local = Physics.OverlapBox(center, half, rotation, mask, triggers);
        if (!TryRoute(owner, center, out Vector3 anchor, out Vector3 direction, out float distance))
            return local;

        Vector3 axis = Quaternion.Inverse(rotation) * direction;
        float extent = Mathf.Abs(axis.x * half.x) + Mathf.Abs(axis.y * half.y) + Mathf.Abs(axis.z * half.z);
        if (!PortalWeaponTrace.TryRoute(anchor, direction, distance + extent, out Route route))
            return local;

        Vector3 exitCenter = route.Door.MapPoint(center);
        var remote = Physics.OverlapBox(exitCenter, half, route.Door.Rotation * rotation, mask, triggers);
        return Merge(local, remote, center, exitCenter, route, owner);
    }

    private static Collider[] Sphere(Vector3 center, float radius, int mask, QueryTriggerInteraction triggers,
        HurtCollider owner)
    {
        var local = Physics.OverlapSphere(center, radius, mask, triggers);
        if (!TryRoute(owner, center, out Vector3 anchor, out Vector3 direction, out float distance) ||
            !PortalWeaponTrace.TryRoute(anchor, direction, distance + radius, out Route route))
            return local;

        Vector3 exitCenter = route.Door.MapPoint(center);
        var remote = Physics.OverlapSphere(exitCenter, radius, mask, triggers);
        return Merge(local, remote, center, exitCenter, route, owner);
    }

    private static bool TryRoute(HurtCollider owner, Vector3 center, out Vector3 anchor,
        out Vector3 direction, out float distance)
    {
        anchor = direction = default;
        distance = 0;
        if (!Weapons.TryGetValue(owner, out var weapon) || !weapon || !PortalWeaponTrace.Active ||
            Hilt(weapon) is not Transform hilt || !hilt)
            return false;

        anchor = hilt.position;
        Vector3 offset = center - anchor;
        distance = offset.magnitude;
        if (distance <= .001f)
            return false;
        direction = offset / distance;
        return true;
    }

    private static Collider[] Merge(Collider[] local, Collider[] remote, Vector3 center, Vector3 exitCenter,
        Route route, HurtCollider owner)
    {
        var result = new List<Collider>(local.Length + remote.Length);
        foreach (var collider in local)
        {
            Vector3 contact = Closest(collider, center);
            if (!PassesAperture(route, contact, out _, out _, out _))
                result.Add(collider);
        }

        HashSet<Collider> exitContacts = null;
        Quaternion inverse = Quaternion.Inverse(route.Door.Rotation);
        int wallMask = WallMask(owner);
        foreach (var collider in remote)
        {
            if (result.Contains(collider))
                continue;
            Vector3 contact = Closest(collider, exitCenter);
            Vector3 virtualContact = route.Entrance + inverse * (contact - route.Exit);
            if (!PassesAperture(route, virtualContact, out Vector3 direction, out float entranceDistance,
                out float totalDistance))
                continue;

            Vector3 aperture = route.Origin + direction * entranceDistance;
            Vector3 exitEdge = route.Door.MapPoint(aperture);
            Vector3 exitDirection = route.Door.Rotation * direction;
            var target = collider.GetComponentInParent<PhysGrabObject>();
            if (!PortalWeaponTrace.InsideExit(route.Door, exitEdge) ||
                WallBlocks(route.Origin, direction, entranceDistance, wallMask, target) ||
                PortalWeaponTrace.Enclosed(exitEdge, wallMask, QueryTriggerInteraction.Collide, wall => IsOtherWall(wall, target)) ||
                WallBlocks(exitEdge, exitDirection, totalDistance - entranceDistance, wallMask, target))
                continue;

            result.Add(collider);
            (exitContacts ??= new HashSet<Collider>()).Add(collider);
        }
        if (exitContacts != null)
            pass = new ContactPass(owner, route, exitContacts);
        return result.ToArray();
    }

    private static bool WallBlocks(Vector3 origin, Vector3 direction, float distance, int mask, PhysGrabObject target)
    {
        foreach (var hit in Physics.RaycastAll(origin, direction, distance, mask, QueryTriggerInteraction.Collide))
            if (IsOtherWall(hit.collider, target))
                return true;
        return false;
    }

    private static bool IsOtherWall(Collider wall, PhysGrabObject target) =>
        wall.CompareTag("Wall") && (!target || wall.GetComponentInParent<PhysGrabObject>() != target);

    private static bool PassesAperture(Route route, Vector3 contact, out Vector3 direction, out float entranceDistance,
        out float totalDistance)
    {
        Vector3 offset = contact - route.Origin;
        totalDistance = offset.magnitude;
        direction = totalDistance > .001f ? offset / totalDistance : Vector3.zero;
        return route.Door.TryRay(route.Origin, direction, totalDistance, out entranceDistance);
    }

    private static Vector3 Closest(Collider collider, Vector3 center) =>
        collider is BoxCollider or SphereCollider or CapsuleCollider or MeshCollider { convex: true }
            ? collider.ClosestPoint(center) : collider.bounds.ClosestPoint(center);

    private static Collider MarkContact(Collider collider)
    {
        if (pass != null)
            pass.Remote = pass.ExitContacts.Contains(collider);
        return collider;
    }

    private static bool TryImpact(HurtCollider owner, out Route route)
    {
        route = default;
        if (pass == null || pass.Owner != owner || !pass.Remote)
            return false;
        route = pass.Route;
        return true;
    }

    private static RaycastHit[] WallRay(Vector3 origin, Vector3 direction, float range, int mask,
        QueryTriggerInteraction triggers, HurtCollider owner)
    {
        if (TryImpact(owner, out Route route))
        {
            Vector3 end = origin + direction * range;
            origin = route.Door.MapPoint(origin);
            Vector3 normal = route.ExitNormal;
            Vector3 delta = end - origin;
            float outside = Vector3.Dot(origin - route.Exit, normal);
            float incoming = -Vector3.Dot(delta, normal);
            if (outside > 0f && incoming > outside)
                origin += delta * (outside / incoming);
            delta = end - origin;
            range = delta.magnitude;
            direction = range > .001f ? delta / range : Vector3.zero;
        }
        return Physics.RaycastAll(origin, direction, range, mask, triggers);
    }

    private static Bounds ImpactBounds(Collider collider, HurtCollider owner)
    {
        Bounds bounds = collider.bounds;
        if (TryImpact(owner, out Route route))
            bounds.center = route.Door.MapPoint(bounds.center);
        return bounds;
    }

    private static Vector3 ImpactForward(Transform transform, HurtCollider owner) =>
        transform == owner.transform && TryImpact(owner, out Route route)
            ? route.Door.Rotation * transform.forward : transform.forward;

    private static Vector3 ImpactPosition(Transform transform, HurtCollider owner) =>
        transform == owner.transform && TryImpact(owner, out Route route)
            ? route.Door.MapPoint(transform.position) : transform.position;
}

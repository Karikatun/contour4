using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Contour4.NativeRaid.Enemies;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Navigation;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace Contour4.NativeRaid;

internal static class NativeRaidEnemyStarts
{
    private const float WaypointHeight = .15f;
    private const float SampleRadius = .30f;
    private const float DuplicateTolerance = .20f;
    private const int SamplesPerAxis = 5;
    private const int StartsPerFloor = 3;
    private static readonly Vector3[] Footprint =
    {
        Vector3.forward, Vector3.right, Vector3.back, Vector3.left
    };
    private static readonly AccessTools.FieldRef<EnemyParent, bool> FirstSpawnPointUsed =
        AccessTools.FieldRefAccess<EnemyParent, bool>("firstSpawnPointUsed");
    private static readonly Func<LevelGenerator, bool> NavMeshReady =
        AccessTools.MethodDelegate<Func<LevelGenerator, bool>>(
            AccessTools.DeclaredPropertyGetter(typeof(LevelGenerator), "NavMeshReady"));

    internal static void Install(Harmony harmony)
    {
        var selector = AccessTools.DeclaredMethod(typeof(EnemyDirector), nameof(EnemyDirector.FirstSpawnPointAdd),
            new[] { typeof(EnemyParent) });
        var filter = AccessTools.DeclaredMethod(typeof(NativeRaidEnemyStarts), nameof(FilterFirstSpawnPoints));
        harmony.Patch(selector, transpiler: new HarmonyMethod(filter));

        var bootstrap = AccessTools.DeclaredMethod(typeof(LevelGenerator), "EnemySpawnBudgeted",
            new[] { typeof(EnemySetup), typeof(Vector3) });
        var position = AccessTools.DeclaredMethod(typeof(NativeRaidEnemyStarts), nameof(BootstrapPosition));
        harmony.Patch(bootstrap, prefix: new HarmonyMethod(position));
    }

    private static void BootstrapPosition(LevelGenerator __instance, ref Vector3 __1)
    {
        if (__instance.Generated || !ActiveModule(__instance, out var module)) return;
        var eligible = EligiblePoints(__instance, __instance.LevelPathPoints, module);
        if (eligible == null) return;

        // Awake enables the stock NavMeshAgent before the enemy gets its separate first spawn point.
        var truck = __instance.LevelParent.GetComponentsInChildren<RoomVolume>().FirstOrDefault(room => room.Truck);
        Vector3 origin = truck ? truck.transform.position : __instance.LevelPathTruck.transform.position;
        var furthest = eligible.OrderByDescending(point => (point.transform.position - origin).sqrMagnitude).First();
        __1 = furthest.transform.position;
    }

    private static IEnumerable<CodeInstruction> FilterFirstSpawnPoints(IEnumerable<CodeInstruction> instructions)
    {
        var stockPoints = AccessTools.DeclaredMethod(typeof(SemiFunc), nameof(SemiFunc.LevelPointsGetAll),
            Type.EmptyTypes);
        var getPoints = AccessTools.DeclaredMethod(typeof(NativeRaidEnemyStarts), nameof(FirstSpawnPoints));
        var code = instructions.ToList();
        int original = code.FindIndex(instruction => instruction.Calls(stockPoints));
        if (original < 0 || code.FindLastIndex(instruction => instruction.Calls(stockPoints)) != original)
            throw new InvalidOperationException("The stock initial enemy spawn selector has changed.");

        // Keep the stock distance ranking and occupied-point rotation. Substitute its inputs here.
        var argument = code[original];
        argument.opcode = OpCodes.Ldarg_1;
        argument.operand = null;
        code.Insert(original + 1, new CodeInstruction(OpCodes.Call, getPoints));
        return code;
    }

    private static List<LevelPoint> FirstSpawnPoints(EnemyParent enemy)
    {
        var generator = LevelGenerator.Instance;
        var points = SemiFunc.LevelPointsGetAll();
        if (!enemy || FirstSpawnPointUsed(enemy) || !ActiveModule(generator, out var module))
            return points;
        return EligiblePoints(generator, points, module) ?? points;
    }

    private static bool ActiveModule(LevelGenerator generator, out NativeRaidModule module)
    {
        module = null;
        if (!generator || !generator.Level || generator.Level.name != NativeRaidFeature.LevelName ||
            !GameManager.instance || GameManager.Multiplayer() || !generator.LevelParent ||
            !generator.LevelPathTruck || !NavMeshReady(generator))
            return false;
        module = generator.LevelParent.GetComponentInChildren<NativeRaidModule>();
        return module;
    }

    private static List<LevelPoint> EligiblePoints(LevelGenerator generator, List<LevelPoint> points, NativeRaidModule module)
    {
        var filter = new NavMeshQueryFilter { agentTypeID = StockPortalPursuit.AgentType, areaMask = 1 };
        int solids = LayerMask.GetMask("Default", "PhysGrabObjectHinge");
        var result = new List<LevelPoint>();
        var represented = new HashSet<RaidFloor>();
        foreach (var point in points)
        {
            if (!point || point.Truck || !point.Room || !module.EnemyStartWaypoints.TryGetValue(point, out var floor) ||
                !Supported(point.transform.position - floor.Up * WaypointHeight, floor, filter, solids))
                continue;
            represented.Add(floor);
            AddDistinct(result, point);
        }

        module.SupplementalEnemyStarts ??= SampleMissingFloors(module.EnemyStartFloors, represented,
            generator.LevelPathTruck.transform.position, filter, solids);
        foreach (var (point, floor) in module.SupplementalEnemyStarts)
        {
            if (point && Supported(point.transform.position - floor.Up * WaypointHeight, floor, filter, solids))
                AddDistinct(result, point);
        }

        if (result.Count != 0) return result;
        module.WarnAboutMissingEnemyStarts();
        return null;
    }

    private static void AddDistinct(List<LevelPoint> points, LevelPoint point)
    {
        Vector3 position = point.transform.position;
        if (!points.Any(other => (other.transform.position - position).sqrMagnitude < DuplicateTolerance * DuplicateTolerance))
            points.Add(point);
    }

    private static Dictionary<LevelPoint, RaidFloor> SampleMissingFloors(
        IReadOnlyDictionary<RaidFloor, RoomVolume> floors, HashSet<RaidFloor> represented, Vector3 truck,
        NavMeshQueryFilter filter, int solids)
    {
        var supplemental = new Dictionary<LevelPoint, RaidFloor>();
        foreach (var (floor, volume) in floors)
        {
            if (!volume || represented.Contains(floor) || !StockPortalPursuit.WorldUp(floor)) continue;
            var nearby = new List<Vector3>();
            foreach (Vector3 feet in SampleFloor(floor, volume, filter, solids)
                .OrderByDescending(feet => (feet - truck).sqrMagnitude))
            {
                const float spacing = StockPortalPursuit.Radius * 2f;
                if (nearby.Any(other => (other - feet).sqrMagnitude < spacing * spacing)) continue;
                var marker = RaidAssembly.Child(volume.transform, "Initial enemy spawn");
                marker.SetPositionAndRotation(feet + floor.Up * WaypointHeight, floor.Frame.rotation);
                var point = marker.gameObject.AddComponent<LevelPoint>();
                // LevelPoint.Start registers the stock roaming point. Only the initial selector sees these.
                point.enabled = false;
                point.Room = volume;
                point.ConnectedPoints = new List<LevelPoint>();
                supplemental.Add(point, floor);
                nearby.Add(feet);
                if (nearby.Count == StartsPerFloor) break;
            }
        }
        return supplemental;
    }

    private static IEnumerable<Vector3> SampleFloor(RaidFloor floor, RoomVolume volume, NavMeshQueryFilter filter, int solids)
    {
        const float margin = StockPortalPursuit.Radius;
        Bounds bounds = floor.LocalBakeBounds;
        if (bounds.size.x < margin * 2f || bounds.size.z < margin * 2f) yield break;
        var boxes = volume.GetComponents<BoxCollider>();
        for (int row = 0; row < SamplesPerAxis; row++)
        for (int column = 0; column < SamplesPerAxis; column++)
        {
            float x = Mathf.Lerp(bounds.min.x + margin, bounds.max.x - margin, row / (SamplesPerAxis - 1f));
            float z = Mathf.Lerp(bounds.min.z + margin, bounds.max.z - margin, column / (SamplesPerAxis - 1f));
            Vector3 sample = floor.World(new Vector3(x, 0f, z));
            if (!OnNativeMesh(sample, floor.Up, filter, out Vector3 feet) ||
                !boxes.Any(box => new Bounds(box.center, box.size).Contains(
                    box.transform.InverseTransformPoint(feet + floor.Up * WaypointHeight))) ||
                !Supported(feet, floor, filter, solids))
                continue;
            yield return feet;
        }
    }

    private static bool OnNativeMesh(Vector3 feet, Vector3 up, NavMeshQueryFilter filter, out Vector3 floor)
    {
        if (NavMesh.SamplePosition(feet, out NavMeshHit hit, SampleRadius, filter) &&
            Vector3.ProjectOnPlane(hit.position - feet, up).sqrMagnitude <= .01f)
        {
            floor = hit.position;
            return true;
        }
        floor = default;
        return false;
    }

    private static bool Supported(Vector3 feet, RaidFloor floor, NavMeshQueryFilter filter, int solids)
    {
        if (!StockPortalPursuit.WorldUp(floor) || !OnNativeMesh(feet, floor.Up, filter, out _) ||
            !PreparedRaidSurfaces.StockLink(floor, feet, feet, solids))
            return false;

        // Keep the spawn footprint on the room's deck, including near open pits and narrow bridges.
        foreach (Vector3 direction in Footprint)
            if (!PreparedRaidSurfaces.Probe(feet + direction * StockPortalPursuit.Radius, floor.Up, .18f,
                solids, floor.Support, out _))
                return false;
        return true;
    }
}

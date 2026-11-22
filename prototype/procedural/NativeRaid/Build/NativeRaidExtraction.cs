using System;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using HarmonyLib;
using UnityEngine;

namespace Contour4.NativeRaid;

internal static class NativeRaidExtraction
{
    private const string StockRoom = "Module - Manor - E - 1 - Hallway";
    private static readonly AccessTools.FieldRef<ExtractionPoint, Transform> Ramp =
        AccessTools.FieldRefAccess<ExtractionPoint, Transform>("ramp");
    private static readonly AccessTools.FieldRef<ExtractionPoint, Transform> Platform =
        AccessTools.FieldRefAccess<ExtractionPoint, Transform>("platform");
    private static readonly AccessTools.FieldRef<ExtractionPoint, Transform> Tube =
        AccessTools.FieldRefAccess<ExtractionPoint, Transform>("extractionTube");

    internal static void Place(RaidAssembly raid, Level stockManor)
    {
        if (raid.Plan.QuotaCount == 0) return;
        var reference = stockManor.ModulesExtraction1.SingleOrDefault(candidate => candidate.PrefabName == StockRoom);
        var prefab = reference?.Prefab;
        var machines = prefab ? prefab.GetComponentsInChildren<ExtractionPoint>(true) : Array.Empty<ExtractionPoint>();
        if (machines.Length != 1) throw new InvalidOperationException($"Cannot load the stock extraction point from {StockRoom}.");
        var source = machines[0];
        if (!Ramp(source) || !Platform(source) || !Tube(source) ||
            !Ramp(source).IsChildOf(source.transform) || !Platform(source).IsChildOf(source.transform) ||
            !Tube(source).IsChildOf(source.transform))
            throw new InvalidOperationException("The stock extraction point is missing its native ramp, platform or tube.");

        foreach (var room in raid.Rooms)
        {
            if (!room.Plan.Module.Landmarks.TryGetValue(RaidExtraction.Machine, out var position)) continue;
            // Its serialized ramp starts on local -Z. Face it into the hall's open freight approach.
            var machine = UnityEngine.Object.Instantiate(source, room.Root);
            machine.transform.SetLocalPositionAndRotation(RaidPlan.Vector(position),
                Quaternion.LookRotation(Vector3.right, Vector3.up));
            machine.name = "Contour-4 native extraction point";
        }
    }
}

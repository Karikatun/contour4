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
        var ramp = Ramp(source);
        var platform = Platform(source);
        var tube = Tube(source);
        if (!ramp || !platform || !tube || !ramp.IsChildOf(source.transform) ||
            !platform.IsChildOf(source.transform) || !tube.IsChildOf(source.transform))
            throw new InvalidOperationException("The stock extraction point is missing its native ramp, platform or tube.");

        // The ramp's immediate parent is rotated relative to the stock machine root.
        var loadingDirection = Vector3.ProjectOnPlane(source.transform.InverseTransformDirection(ramp.forward), Vector3.up);
        var rampOffset = source.transform.InverseTransformVector(ramp.position - platform.position);
        if (Vector3.Dot(loadingDirection, rampOffset) <= .01f)
            throw new InvalidOperationException("The stock extraction ramp axis does not point towards its loading approach.");

        foreach (var room in raid.Rooms)
        {
            if (!room.Plan.Module.Landmarks.TryGetValue(RaidExtraction.Machine, out var position)) continue;
            var approach = RaidExtraction.LoadingRoute(room.Plan.Module).Knots.Last();
            var machine = UnityEngine.Object.Instantiate(source, room.Root);
            machine.transform.SetLocalPositionAndRotation(RaidPlan.Vector(position),
                RaidExtraction.FaceLoading(approach, loadingDirection));
            machine.name = "Contour-4 native extraction point";
            if (room.Plan.Module.Id == RaidQuotaRotaryRoom.Id)
                NativeRaidQuotaRotary.Install(raid, room);
        }
    }
}

using System;
using System.Linq;
using Contour4.NativeRaid;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural;
using UnityEngine;

namespace SpaceJourney;

internal static class ValuableSites
{
    internal static RaidLootPlacement CatalogPlacement(Transform marker, string key)
    {
        string support = key switch
        {
            "experiment-cassette" => "Wooden operator worktop",
            "control-sample" => "Sample shelf base",
            "sensor-rod" => "Open sensor rack base",
            "calibration-weight" => "Calibration pallet",
            "portable-gravimeter" => "Continuous wooden worktop",
            "gimbal-gyroscope" => "Continuous wooden worktop",
            "gravity-lens" => "Lens low plinth",
            "mobius-strip" => "Demonstration sample shelf",
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
        var room = marker.parent.parent;
        var area = room.Find($"Markers/{room.name}__RoomVolume").GetComponent<BoxCollider>();
        var supports = room.GetComponentsInChildren<Collider>().Where(item => item.name == support);
        string floor = key == "sensor-rod" ? "Upper gallery deck" : room.name + "__Floor";
        var bounds = new Bounds(area.center, area.size);
        foreach (var collider in supports)
        {
            var placement = new RaidLootPlacement(area.transform, bounds, collider, floor);
            if (placement.Supports(marker.position, marker.up)) return placement;
        }
        throw new InvalidOperationException("Missing Ring catalog support " + key);
    }

    internal static void InstallOrdinary(Transform ring)
    {
        Physics.SyncTransforms();
        for (int index = 0; index < RingRuntime.RoomNames.Length; index++)
        {
            string name = RingRuntime.RoomNames[index];
            Transform room = ring.Find(name);
            Transform markers = room.Find("Markers");
            BoxCollider area = markers.Find(name + "__RoomVolume").GetComponent<BoxCollider>();
            Collider deck = room.Find("Model").GetComponentsInChildren<BoxCollider>()
                .Single(collider => collider.name == name + "__Floor");
            var placement = new RaidLootPlacement(area.transform, new Bounds(area.center, area.size), deck, deck.name);
            foreach (Transform marker in markers)
            {
                if (!marker.name.StartsWith(name + "__LootSpawn_", StringComparison.Ordinal) ||
                    marker.GetComponent<RaidAuthoredLoot>())
                    continue;

                var category = Enum.Parse<RaidLootSize>(marker.name.Substring(marker.name.LastIndexOf('_') + 1));
                Vector3 size = RaidLootSites.Envelope(category);
                // These four historical center markers overlap fixed furnishings at stock scale.
                // Corrections are in imported room coordinates and preserve their original deck.
                Vector3 correction = marker.name switch
                {
                    "Loot2__LootSpawn_04_Wide" => new Vector3(.60f, 0, -1.60f),
                    "Loot3__LootSpawn_02_Medium" => new Vector3(0, 0, -.10f),
                    "Loot3__LootSpawn_03_Big" => new Vector3(0, 0, -.20f),
                    "Loot3__LootSpawn_04_Wide" => new Vector3(-.40f, 0, 0),
                    _ => Vector3.zero
                };
                Vector3 source = marker.position + room.TransformVector(correction);
                if (!deck.Raycast(new Ray(source + room.up * .025f, -room.up), out var hit, 1.2f))
                    continue;

                // Authored marker centers are imported at 1.25 scale. Native items use
                // a world-metre bottom/front pivot regardless of the marker's parent scale.
                var pose = new Pose(hit.point + room.up * .012f - marker.forward * (size.z / 2), marker.rotation);
                if (!placement.AllowsEnvelope(pose, size)) continue;

                marker.SetPositionAndRotation(pose.position, pose.rotation);
                NativeRaidLoot.AddVolume(marker, category, placement, LootSurfaceKind.Floor, index, ringFloor: true);
            }
        }
    }
}

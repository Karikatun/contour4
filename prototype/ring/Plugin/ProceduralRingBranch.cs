using System;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using Unity.AI.Navigation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceJourney;

internal static class ProceduralRingBranch
{
    private const string Entry = "Quota/Markers/Quota__South_Truck";

    internal static RaidPrefabBranch Describe(GameObject prefab)
    {
        var root = prefab.transform;
        var entry = root.Find(Entry);
        if (!entry) throw new InvalidOperationException("The ring has no authored freight entrance.");
        var reservation = RaidPrefabBranch.Measure(prefab, "TruckSocket");
        var entrance = new Pose(root.InverseTransformPoint(entry.position),
            Quaternion.Inverse(root.rotation) * entry.rotation);
        // Quota__South_Truck is authored as a cart dock of 3.6 × 3.2 before the 1.25 grid scale.
        return new RaidPrefabBranch("test-ring", reservation, entrance, new Vector2(4.5f, 4f),
            RingRuntime.PortalPairCount, host => Mount(prefab, host, reservation), DescribeOutline(root, entry),
            RingRoomNavigation.Collect);
    }

    private static RaidPlanarLayout.BranchOutline DescribeOutline(Transform root, Transform entrance)
    {
        string[] names = RingRuntime.RoomNames;
        var floors = names.Select(name => (name, new Rect(-6f, -6f, 12f, 12f))).ToArray();
        string[] bridges = { "BridgeQuotaLoot1", "BridgeLoot1Loot2" };
        var passages = bridges.Select((name, index) =>
        {
            Transform bridge = root.Find(name);
            Transform from = RingRuntime.Marker(bridge, name + "__Start");
            Transform to = RingRuntime.Marker(bridge, name + "__End");
            return (names[index], LocalPoint(root.Find(names[index]), from),
                names[index + 1], LocalPoint(root.Find(names[index + 1]), to));
        }).ToArray();
        return new RaidPlanarLayout.BranchOutline((names[0], LocalPoint(root.Find(names[0]), entrance)), floors, passages);

        static Vector2 LocalPoint(Transform room, Transform marker)
        {
            Vector3 point = room.InverseTransformPoint(marker.position);
            return new Vector2(point.x, point.z);
        }
    }

    private static void Mount(GameObject prefab, Transform host, Bounds reservation)
    {
        var ring = Object.Instantiate(prefab, host);
        ring.name = prefab.name;
        var socket = ring.transform.Find("TruckSocket");
        if (socket) socket.gameObject.SetActive(false);

        // Stock world-up actors stay on the generated spine; the ring retains its own rotated movement.
        foreach (var collider in ring.GetComponentsInChildren<Collider>())
        {
            if (collider.isTrigger) continue;
            var modifier = collider.GetComponent<NavMeshModifier>() ??
                collider.gameObject.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = 1;
        }
        foreach (string name in RingRuntime.RoomNames)
        {
            var markers = ring.transform.Find(name + "/Markers");
            var roomVolume = markers.Find(name + "__RoomVolume");
            var trigger = roomVolume.gameObject.AddComponent<BoxCollider>();
            trigger.size = new Vector3(12f, 3.7f, 12f);
            trigger.isTrigger = true;
            roomVolume.gameObject.AddComponent<RoomVolume>();
        }
        ValuableSites.InstallOrdinary(ring.transform);
        host.gameObject.AddComponent<RingRuntime>().ConnectProcedural(ring.transform, reservation);
    }
}

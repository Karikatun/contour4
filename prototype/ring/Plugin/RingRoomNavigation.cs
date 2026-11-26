using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Navigation;
using UnityEngine;

namespace SpaceJourney;

internal static class RingRoomNavigation
{
    internal static (RaidFloor Entrance, RaidFloor[] Floors, RaidDeckPassage[] Decks, RaidPortal[] Portals) Collect(Transform host, Collider collar)
    {
        var ring = host.GetComponent<RingRuntime>();
        var entrance = ring.Rooms.Single(room => room.name == "Quota");
        var floors = ring.Rooms.ToDictionary(room => room, room => CreateFloor(room, room == entrance ? collar : null));
        var decks = new List<RaidDeckPassage>();
        AddBridge(ring.Ring.Find("BridgeQuotaLoot1"), floors[ring.Rooms[0]], floors[ring.Rooms[1]], decks);
        AddBridge(ring.Ring.Find("BridgeLoot1Loot2"), floors[ring.Rooms[1]], floors[ring.Rooms[2]], decks);

        const RaidPortalUse use = RaidPortalUse.Vision | RaidPortalUse.Walking;
        var doors = ring.Portals.Where(pair => !pair.A.Upper).Select(pair =>
            new RaidPortal(Aperture(pair.A, floors[pair.A.Room]), Aperture(pair.B, floors[pair.B.Room]), use, use));
        return (floors[entrance], floors.Values.ToArray(), decks.ToArray(), doors.ToArray());
    }

    private static RaidAperture Aperture(PortalEnd end, RaidFloor floor)
    {
        var frame = RaidAssembly.Child(end.Marker, "Navigation portal plane");
        frame.SetPositionAndRotation(end.Center, end.Orientation);
        var approach = end.Marker.position - end.Normal * ApproachDepth;
        return new RaidAperture(floor, RaidPortalKind.Door, new Pose(frame.position, frame.rotation),
            new Vector2(end.Width, end.Height), approach, frame);
    }

    private const float ApproachDepth = 1.40f;

    private static void AddBridge(Transform bridge, RaidFloor from, RaidFloor to, List<RaidDeckPassage> decks)
    {
        var markers = bridge.Find("Markers");
        var start = RingRuntime.Marker(bridge, bridge.name + "__Start");
        var finish = RingRuntime.Marker(bridge, bridge.name + "__End");
        var checkpoints = markers.Cast<Transform>()
            .Where(marker => marker.name.StartsWith(bridge.name + "__Sample_", StringComparison.Ordinal))
            .OrderBy(marker => marker.name, StringComparer.Ordinal).Prepend(start).Append(finish)
            .Select(marker => LocalPose(marker.position, marker.rotation));
        var poses = checkpoints.Prepend(LocalPose(start.position - start.forward * ApproachDepth, start.rotation))
            .Append(LocalPose(finish.position + finish.forward * ApproachDepth, finish.rotation)).ToArray();
        var samples = RaidDeckPassage.DenseSamples(poses, .075f / bridge.lossyScale.x).ToArray();
        var walkable = bridge.Find("Model").GetComponentsInChildren<Collider>()
            .Single(collider => collider.name == bridge.name + "__Walkable_Deck");
        var support = new HashSet<Collider>(from.Support);
        support.UnionWith(to.Support);
        support.Add(walkable);
        decks.Add(new RaidDeckPassage(from, to, bridge, samples, support));
        var reverse = samples.Reverse().Select(pose =>
            new Pose(pose.position, pose.rotation * Quaternion.AngleAxis(180f, Vector3.up))).ToArray();
        decks.Add(new RaidDeckPassage(to, from, bridge, reverse, support));

        Pose LocalPose(Vector3 position, Quaternion rotation) =>
            new(bridge.InverseTransformPoint(position), Quaternion.Inverse(bridge.rotation) * rotation);
    }

    private static RaidFloor CreateFloor(Transform room, Collider entranceCollar)
    {
        var solids = room.Find("Model").GetComponentsInChildren<Collider>()
            .Where(collider => collider.enabled && !collider.isTrigger).ToArray();
        var floor = solids.Single(collider => collider.name == room.name + "__Floor");
        var ramp = solids.SingleOrDefault(collider => collider.name == room.name + "__Upper_Ramp");
        var volume = RingRuntime.Marker(room, room.name + "__RoomVolume").GetComponent<BoxCollider>();
        Bounds bounds = VolumeBounds(volume, room);
        bounds.Expand(new Vector3(.28f, 1.2f, .28f));

        var support = new List<Collider> { floor };
        if (ramp) support.Add(ramp);
        if (entranceCollar)
        {
            solids = solids.Append(entranceCollar).ToArray();
            support.Add(entranceCollar);
        }
        return new RaidFloor($"test-ring/{room.name}", "ground", "floor", new Pose(room.position, room.rotation),
            bounds, solids, support, room, prepared: true);
    }

    private static Bounds VolumeBounds(BoxCollider volume, Transform room)
    {
        Quaternion local = Quaternion.Inverse(room.rotation);
        var bounds = new Bounds(local * (volume.transform.TransformPoint(volume.center) - room.position), Vector3.zero);
        var half = volume.size * .5f;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            var corner = volume.transform.TransformPoint(volume.center + new Vector3(x * half.x, y * half.y, z * half.z));
            bounds.Encapsulate(local * (corner - room.position));
        }
        return bounds;
    }
}

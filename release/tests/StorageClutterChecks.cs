using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceJourney
{
    internal static class StorageClutterChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Rack = "samples-packing-trays-shelf-";
        private const string Contents = "Storage packing tray contents";

        internal static void Run()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string output = Environment.GetEnvironmentVariable("CONTOUR4_FURNITURE_OUTPUT");
            bool candidate = Environment.GetEnvironmentVariable("CONTOUR4_WORKROOM_EXPECT_CLUTTER") == "1";
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            using var bundles = RoomPrefabBundle.Load(Path.Combine(output, "contour4-rooms.bundle"),
                Path.Combine(output, "contour4-prepared-modules.bundle"), requireWear: true);
            var rows = new List<string>();
            var failures = new List<string>();
            foreach (string moduleId in new[] { "sample-storage", "sample-storage-narrow" })
            {
                (string Support, RaidLootSize Size, Pose Pose)[] reservations = null;
                foreach (bool authored in new[] { true, false })
                {
                    var module = ModuleCatalog.Rooms().Single(room => room.Id == moduleId);
                    var label = moduleId + (authored ? "-prepared" : "-fallback");
                    var host = new GameObject("Storage fixture");
                    try
                    {
                        var raid = host.AddComponent<RaidAssembly>();
                        var pose = new Pose(Vector3.zero, Quaternion.identity);
                        var plan = new RaidPlan.Room(module, pose, pose, 0);
                        var shader = Shader.Find("Standard");
                        var fitout = (RaidInteriorFitout)Invoke(raid, "CreateFitout", shader);
                        var obstacle = (Material)Invoke(raid, "Material", shader, new Color(.39f, .33f, .25f));
                        BakedRoom room;
                        if (authored)
                        {
                            if (!bundles.Prefabs.TryGetPrepared(plan, out var prefab))
                                throw new InvalidOperationException($"Missing prepared room for {moduleId}");
                            room = raid.PlacePrepared(plan, 0, prefab, obstacle, fitout);
                            var preparedSlots = bundles.Prefabs.PreparedLoot(room)
                                .Where(slot => slot.Support.name.StartsWith(Rack, StringComparison.Ordinal)).ToArray();
                            reservations = preparedSlots.Select(slot => (slot.Support.name, slot.Size, slot.Pose)).ToArray();
                            foreach (var slot in preparedSlots)
                                Check("physics-loot", $"{slot.Support.name}; {slot.Size}; pivot={slot.WorldPose.position:F4}",
                                    slot.Placement(RaidLootPlacement.StructuralBounds(room))?.AllowsEnvelope(slot.WorldPose, RaidLootSites.Envelope(slot.Size)) == true);
                        }
                        else
                        {
                            RaidCatalogSites.Dress(module);
                            RaidLootSites.Dress(module);
                            var (floor, wall) = ((Material, Material))Invoke(raid, "Finishes", shader);
                            var shell = (Material)Invoke(raid, "Material", shader, new Color(.52f, .56f, .57f));
                            var sight = new RaidSightStrips(shader, Field<List<Material>>(raid, "materials"), Field<List<Mesh>>(raid, "meshes"));
                            room = (BakedRoom)Invoke(raid, "PlaceRoom", plan, 0, floor, wall, shell, obstacle, sight, null, fitout);
                        }
                        Field<RoomBloodTraces>(raid, "bloodTraces").Apply(room);
                        Physics.SyncTransforms();
                        Check("room-path", $"prepared={room.Prepared}, authored={room.Authored}", room.Prepared == authored);

                        var wallRows = new List<string>();
                        ReferenceWallChecks.Render(room.Root, new(12.20f, 1.62f, 4.28f), new(13.56f, .91f, 6.98f), output,
                            label + "-rack", 52, wallRows);
                        ReferenceWallChecks.Render(room.Root, new(10.72f, 1.62f, 3.52f), new(13.56f, 1.05f, 6.99f), output,
                            label + "-room", 58, wallRows);
                        if (authored)
                        {
                            ReferenceWallChecks.Render(room.Root, new(4.25f, 1.62f, 0), new(8.72f, 1.31f, .10f), output,
                                label + "-central-aisle", 61, wallRows);
                            ReferenceWallChecks.Render(room.Root, new(4.68f, 1.62f, 5.08f), new(8.62f, 1.43f, 2.65f), output,
                                label + "-outside-racks", 58, wallRows);
                        }

                        var groups = room.Root.GetComponentsInChildren<Transform>().Where(part => part.name == Contents).ToArray();
                        Check("contents-group", $"{groups.Length}", groups.Length == (candidate ? 1 : 0));
                        var proxies = new List<MeshCollider>();
                        foreach (var group in groups)
                            foreach (var filter in group.GetComponentsInChildren<MeshFilter>())
                            {
                                var renderer = filter.GetComponent<MeshRenderer>();
                                Check("visible-mesh", $"{filter.name}; size={renderer.bounds.size:F3}; " +
                                    $"min={renderer.bounds.min:F3}; max={renderer.bounds.max:F3}", renderer.enabled && !renderer.forceRenderingOff &&
                                    filter.sharedMesh.vertexCount > 0 && filter.GetComponents<Collider>().Length == 0);
                                var proxy = filter.gameObject.AddComponent<MeshCollider>();
                                proxy.sharedMesh = filter.sharedMesh;
                                proxies.Add(proxy);
                            }
                        Physics.SyncTransforms();
                        Check("prepared-site-count", $"three reservations across prepared tiers: {reservations.Length}", reservations.Length == 3);
                        foreach (var slot in reservations)
                        {
                            var support = room.Colliders.Single(part => part.name == slot.Support);
                            Pose world = new(room.Root.TransformPoint(slot.Pose.position), room.Root.rotation * slot.Pose.rotation);
                            Vector3 size = RaidLootSites.Envelope(slot.Size);
                            Vector3 center = world.position + world.rotation * new Vector3(0, size.y / 2, size.z / 2);
                            Check("board", $"{support.name}; top={support.bounds.max.y:F4} m; " +
                                $"span={support.bounds.size.x:F3} m; depth={support.bounds.size.z:F3} m", Mathf.Abs(world.position.y - support.bounds.max.y - .008f) < .002f);
                            var visible = WorkroomClutterChecks.Sweep(center, size / 2 - Vector3.one * .002f,
                                -world.forward, .65f, world.rotation, proxies);
                            Check("reserved-fetch-volume", $"{support.name}; center={center:F4}; {WorkroomClutterChecks.ContactText(visible)}", visible.Count == 0);
                        }

                        foreach (var route in module.Routes)
                        {
                            var hits = new HashSet<string>();
                            foreach (var sample in route.Samples(.05f))
                            {
                                Vector3 center = RaidPlan.Vector(sample.Position + sample.Up * (.04f + route.Envelope.Y / 2));
                                var orientation = Quaternion.LookRotation(RaidPlan.Vector(sample.Right), RaidPlan.Vector(sample.Up));
                                hits.UnionWith(WorkroomClutterChecks.Overlap(center, RaidPlan.Vector(route.Envelope) * .499f, orientation, proxies));
                            }
                            Check("route-mesh", $"{route.Id}; {route.Cargo}; {WorkroomClutterChecks.ContactText(hits)}", hits.Count == 0);
                        }
                        foreach (var support in room.Colliders.Where(part => part.name.StartsWith("samples-packing-trays", StringComparison.Ordinal)))
                        {
                            rows.Add($"{label}\tPHYSICAL\t{support.name}\t{support.bounds.center:F4}\t{support.bounds.size:F4}");
                            var hits = WorkroomClutterChecks.Overlap(support.bounds.center, support.bounds.extents - Vector3.one * .004f,
                                Quaternion.identity, proxies).ToArray();
                            Check("fixture-mesh", $"{support.name}; {WorkroomClutterChecks.ContactText(hits)}", hits.Length == 0);
                        }
                        foreach (string surface in new[] { "samples-packing-trays-shelf-1", "samples-packing-trays-shelf-2" })
                        {
                            var support = room.Colliders.Single(part => part.name == surface);
                            Vector3 half = new(support.bounds.extents.x, .007f, support.bounds.extents.z);
                            Vector3 center = new(support.bounds.center.x, support.bounds.max.y + .003f, support.bounds.center.z);
                            var hits = WorkroomClutterChecks.Overlap(center, half, Quaternion.identity, proxies).ToArray();
                            Check("fixture-support", $"{surface}; {WorkroomClutterChecks.ContactText(hits)}", candidate ? hits.Length > 0 : hits.Length == 0);
                        }
                    }
                    finally { Object.DestroyImmediate(host); }

                    void Check(string name, string note, bool pass)
                    {
                        rows.Add($"{label}\t{name}\t{note}");
                        if (!pass) failures.Add($"{label}/{name}: {note}");
                    }
                }
            }
            File.WriteAllLines(Path.Combine(output, "storage-contract.tsv"), rows);
            if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
        }

        private static T Field<T>(RaidAssembly raid, string name) => (T)typeof(RaidAssembly).GetField(name, Private).GetValue(raid);
        private static object Invoke(RaidAssembly raid, string name, params object[] args) =>
            typeof(RaidAssembly).GetMethod(name, Private).Invoke(raid, args);
    }
}

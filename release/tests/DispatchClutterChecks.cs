using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceJourney
{
    internal static class DispatchClutterChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags LootPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        private const string Rack = "freight-dispatch-tag-and-seal-rack-";

        internal static void Run()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string output = Environment.GetEnvironmentVariable("CONTOUR4_FURNITURE_OUTPUT");
            bool candidate = Environment.GetEnvironmentVariable("CONTOUR4_WORKROOM_EXPECT_CLUTTER") == "1";
            var rows = new List<string>();
            var failures = new List<string>();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            using var bundles = RoomPrefabBundle.Load(Path.Combine(output, "contour4-rooms.bundle"),
                Path.Combine(output, "contour4-prepared-modules.bundle"), requireWear: true);
            foreach (bool research in new[] { true, false })
            {
                CheckVariant(research, candidate, output, rows, failures);
            }
            File.WriteAllLines(Path.Combine(output, "dispatch-contract.tsv"), rows);
            if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
        }

        private static void CheckVariant(bool research, bool candidate, string output, List<string> rows, List<string> failures)
        {
            string label = research ? "dispatch-research" : "dispatch-plain";
            var host = new GameObject("Dispatch fixture");
            try
            {
                var module = FreightServiceRoom.Create(FreightServiceRoom.Dispatch, research);
                var raid = host.AddComponent<RaidAssembly>();
                var pose = new Pose(Vector3.zero, Quaternion.identity);
                var plan = new RaidPlan.Room(module, pose, pose, 0);
                foreach (var dock in module.Docks)
                    if (dock.Id == "west" || dock.Id == FreightServiceRoom.ResearchDoor) plan.OpenPlugs.Add(dock.PlugId);
                RaidCatalogSites.Dress(module);
                RaidLootSites.Dress(module);
                var shader = Shader.Find("Standard");
                var fitout = (RaidInteriorFitout)Invoke(raid, "CreateFitout", shader);
                var (floor, wall) = ((Material, Material))Invoke(raid, "Finishes", shader);
                var shell = (Material)Invoke(raid, "Material", shader, new Color(.52f, .56f, .57f));
                var obstacle = (Material)Invoke(raid, "Material", shader, new Color(.39f, .33f, .25f));
                var sight = new RaidSightStrips(shader, Field<List<Material>>(raid, "materials"), Field<List<Mesh>>(raid, "meshes"));
                var room = (BakedRoom)Invoke(raid, "PlaceRoom", plan, 0, floor, wall, shell, obstacle, sight, null, fitout);
                Field<RoomBloodTraces>(raid, "bloodTraces").Apply(room);
                Physics.SyncTransforms();
                Check("room-path", $"fallback; {module.Docks.Count} docks", !room.Authored && !room.Prepared);
                var rackSlots = module.Dressing.SelectMany(dressing => dressing.LootSurfaces).Count(surface => surface.Support.Contains("tag-and-seal-rack"));
                Check("rack-loot", $"{rackSlots} registered stock-loot surfaces on this separate rack", rackSlots == 0);
                foreach (string surface in new[] { "ground", "near-wall", Rack + "shelf-0", Rack + "shelf-1", Rack + "shelf-2" })
                {
                    var renderer = room.Root.GetComponentsInChildren<MeshRenderer>().Single(part => part.name == surface);
                    var material = renderer.sharedMaterial;
                    Check("surface-finish", $"{surface}; {material.name}; {material.shader.name}",
                        material.shader.name == "Contour4/Room surface wear" && material.shader.isSupported);
                }

                ReferenceWallChecks.Render(room.Root, new(6.67f, 1.62f, .40f), new(8.71f, 1.18f, 3.80f), output,
                    label + "-room", 63, new List<string>());
                ReferenceWallChecks.Render(room.Root, new(8.10f, 1.62f, 1.72f), new(8.90f, .98f, 3.84f), output,
                    label + "-rack", 51, new List<string>());
                ReferenceWallChecks.Render(room.Root, new(.25f, 1.62f, .0f), new(8.0f, 1.24f, 1.20f), output,
                    label + "-entry", 59, new List<string>());

                foreach (var physical in room.Root.GetComponentsInChildren<Collider>())
                    rows.Add($"{label}\tPHYSICAL\t{physical.name}\t{physical.bounds.center:F4}\t{physical.bounds.size:F4}");
                var supplyBody = room.Root.GetComponentsInChildren<Transform>().Single(part => part.name == "Sealed shipment supply");
                var supplyRender = supplyBody.GetComponent<MeshRenderer>();
                var supply = supplyBody.GetComponent<BoxCollider>();
                Check("supply-collider", $"active={supply && supply.enabled}; renderer center={supplyRender.bounds.center:F3}; " +
                    $"size={supplyRender.bounds.size:F3}; included in baked list={room.Colliders.Contains(supply)}",
                    supply && supply.enabled && supplyRender.enabled);
                if (supply)
                {
                    var overlaps = Physics.OverlapBox(supply.bounds.center, supply.bounds.extents - Vector3.one * .006f,
                        Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Where(hit => hit != supply).Select(hit => hit.name).ToArray();
                    Check("supply-placement", WorkroomClutterChecks.ContactText(overlaps), overlaps.Length == 0);
                }
                int unobstructed = 0;
                foreach (float y in new[] { .56f, 1.18f, 1.81f })
                {
                    Vector3 eye = new(8.10f, 1.62f, 1.72f);
                    Vector3 mark = new(8.92f, y, 3.69f);
                    var ray = new Ray(eye, (mark - eye).normalized);
                    if (!supplyRender.bounds.IntersectRay(ray, out float hit) || hit >= Vector3.Distance(eye, mark))
                        unobstructed++;
                }
                Check("rack-sightline", $"{unobstructed} of 3 shelf tiers unobstructed by supply from aisle",
                    candidate ? unobstructed == 3 : unobstructed < 3);
                var groups = room.Root.GetComponentsInChildren<Transform>().Where(part => part.name == "Dispatch sorting rack contents").ToArray();
                Check("contents-group", $"{groups.Length} active root", groups.Length == (candidate ? 1 : 0));
                var proxies = new List<MeshCollider>();
                foreach (var filter in groups.SelectMany(group => group.GetComponentsInChildren<MeshFilter>()))
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    Check("visible-mesh", $"{filter.name}; min={renderer.bounds.min:F3}; max={renderer.bounds.max:F3}",
                        renderer.enabled && !renderer.forceRenderingOff && filter.sharedMesh.vertexCount > 0 &&
                        filter.GetComponents<Collider>().Length == 0);
                    var proxy = filter.gameObject.AddComponent<MeshCollider>();
                    proxy.sharedMesh = filter.sharedMesh;
                    proxies.Add(proxy);
                }
                Physics.SyncTransforms();
                var lootHits = new HashSet<string>();
                int availableSites = 0;
                var cargoSweeps = (IEnumerable<Bounds>)typeof(RaidLootSites).GetMethod("RouteSweeps", LootPrivate)
                    .Invoke(null, new[] { (object)room });
                var routeBounds = cargoSweeps.ToArray();
                var manifestBin = room.Colliders.OfType<BoxCollider>().Single(part => part.name == "freight-dispatch-outbound-manifest-bin");
                for (int seed = 0; seed < 12; seed++)
                {
                    var slots = new List<RaidLootSlot>();
                    typeof(RaidLootSites).GetMethod("AddSurface", LootPrivate)
                        .Invoke(null, new object[] { room, manifestBin, LootSurfaceKind.Raised, -1,
                            new System.Random(seed), routeBounds, slots });
                    foreach (var slot in slots)
                    {
                        Pose world = slot.WorldPose;
                        Vector3 size = RaidLootSites.Envelope(slot.Size);
                        if (slot.Placement(RaidLootPlacement.StructuralBounds(room))?.AllowsEnvelope(world, size) != true) continue;
                        availableSites++;
                        Vector3 center = world.position + world.rotation * new Vector3(0, size.y / 2, size.z / 2);
                        if (supply && supply.bounds.Intersects(slot.LocalBounds)) lootHits.Add(supply.name);
                        lootHits.UnionWith(WorkroomClutterChecks.Sweep(center, size / 2 - Vector3.one * .002f,
                            -world.forward, .90f, world.rotation, proxies));
                    }
                }
                Check("manifest-bin-loot", $"{availableSites} placements over 12 fixed seeds; {WorkroomClutterChecks.ContactText(lootHits)}",
                    availableSites > 0 && lootHits.Count == 0);
                foreach (var fixture in room.Colliders.Where(part => part.name.StartsWith(Rack, StringComparison.Ordinal)))
                {
                    var hits = WorkroomClutterChecks.Overlap(fixture.bounds.center, fixture.bounds.extents - Vector3.one * .004f,
                        Quaternion.identity, proxies).ToArray();
                    Check("fixture-mesh", $"{fixture.name}; {WorkroomClutterChecks.ContactText(hits)}", hits.Length == 0);
                    if (!fixture.name.Contains("shelf")) continue;
                    Vector3 center = new(fixture.bounds.center.x, fixture.bounds.max.y + .003f, fixture.bounds.center.z);
                    Vector3 half = new(fixture.bounds.extents.x, .007f, fixture.bounds.extents.z);
                    var supports = WorkroomClutterChecks.Overlap(center, half, Quaternion.identity, proxies).ToArray();
                    Check("fixture-support", $"{fixture.name}; {WorkroomClutterChecks.ContactText(supports)}",
                        candidate ? supports.Length > 0 : supports.Length == 0);
                }
                foreach (var route in module.Routes)
                {
                    var hits = new HashSet<string>();
                    int samples = 0;
                    foreach (var sample in route.Samples(.05f))
                    {
                        Vector3 center = RaidPlan.Vector(sample.Position + sample.Up * (.04f + route.Envelope.Y / 2));
                        var orientation = Quaternion.LookRotation(RaidPlan.Vector(sample.Right), RaidPlan.Vector(sample.Up));
                        var half = RaidPlan.Vector(route.Envelope) * .499f;
                        hits.UnionWith(WorkroomClutterChecks.Overlap(center, half, orientation, proxies));
                        if (supply && Physics.OverlapBox(center, half, orientation, ~0, QueryTriggerInteraction.Ignore).Contains(supply))
                            hits.Add(supply.name);
                        samples++;
                    }
                    Check("route-mesh", $"{route.Id}; {route.Cargo}; {samples} poses; {WorkroomClutterChecks.ContactText(hits)}",
                        samples > 0 && hits.Count == 0);
                }
                foreach (var dock in module.Docks)
                {
                    Vector3 up = RaidPlan.Vector(dock.Threshold.Up);
                    var orientation = Quaternion.LookRotation(RaidPlan.Vector(dock.Threshold.Forward), up);
                    Vector3 center = RaidPlan.Vector(dock.Threshold.Position) + up * (dock.Opening.Y / 2);
                    var half = new Vector3(dock.Opening.X / 2 - .005f, dock.Opening.Y / 2 - .02f, .24f);
                    var hits = WorkroomClutterChecks.Overlap(center, half, orientation, proxies).ToList();
                    if (supply && Physics.OverlapBox(center, half, orientation, ~0, QueryTriggerInteraction.Ignore).Contains(supply))
                        hits.Add(supply.name);
                    Check("doorway", $"{dock.Id}; {dock.Opening}; {WorkroomClutterChecks.ContactText(hits)}", hits.Count == 0);
                }
            }
            finally { Object.DestroyImmediate(host); }

            void Check(string name, string note, bool pass)
            {
                rows.Add($"{label}\t{name}\t{note}");
                if (!pass) failures.Add($"{label}/{name}: {note}");
            }
        }

        private static T Field<T>(RaidAssembly raid, string name) => (T)typeof(RaidAssembly).GetField(name, Private).GetValue(raid);
        private static object Invoke(RaidAssembly raid, string name, params object[] args) =>
            typeof(RaidAssembly).GetMethod(name, Private).Invoke(raid, args);
    }
}

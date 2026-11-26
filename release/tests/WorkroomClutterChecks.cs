using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceJourney
{
    internal static class WorkroomClutterChecks
    {
        private static readonly string[] ReceivingGroups = {
            "Receiving open packing carton", "Receiving packing station", "Receiving sorting station"
        };
        private static readonly string[] ServiceGroups = { "Service bench diagnostic tester", "Service repair station" };

        public static void Run()
        {
            string selection = Environment.GetEnvironmentVariable("CONTOUR4_FURNITURE_ROOMS");
            if (selection == "storage")
                StorageClutterChecks.Run();
            else if (selection == "dispatch")
                DispatchClutterChecks.Run();
            else
            {
                ReferenceWallChecks.Run();
                FurnitureAccessChecks.Run();
            }
            Debug.Log("CONTOUR4 WORKROOM CLUTTER PASSED");
        }

        internal static void Probe(BakedRoom room, string label, Action<string, string, bool> check)
        {
            bool receiving = room.Plan.Module.Id == "sample-receiving";
            bool expectClutter = Environment.GetEnvironmentVariable("CONTOUR4_WORKROOM_EXPECT_CLUTTER") == "1";
            var expected = receiving ? ReceivingGroups : ServiceGroups;
            var groups = room.Root.GetComponentsInChildren<Transform>().Where(part => expected.Contains(part.name)).ToArray();
            check("workroom-group-presence", string.Join(" | ", groups.Select(group => group.name)),
                expectClutter ? groups.Select(group => group.name).OrderBy(name => name).SequenceEqual(expected) : groups.Length == 0);
            var proxies = new List<MeshCollider>();
            var structural = RaidLootPlacement.StructuralBounds(room);
            structural.Expand(.001f);
            try
            {
                foreach (var group in groups)
                {
                    var filters = group.GetComponentsInChildren<MeshFilter>();
                    int triangles = 0;
                    int vertices = 0;
                    Bounds? local = null;
                    bool contained = true;
                    foreach (var filter in filters)
                    {
                        var mesh = filter.sharedMesh;
                        var renderer = filter.GetComponent<MeshRenderer>();
                        if (!mesh || !renderer || !renderer.enabled || renderer.forceRenderingOff)
                            throw new InvalidOperationException($"Invalid added mesh {group.name}/{filter.name} in {label}");
                        var toLocal = room.Root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                        foreach (Vector3 vertex in mesh.vertices)
                        {
                            Vector3 point = toLocal.MultiplyPoint3x4(vertex);
                            var bounds = local.GetValueOrDefault(new Bounds(point, Vector3.zero));
                            bounds.Encapsulate(point);
                            local = bounds;
                            contained &= structural.Contains(point);
                        }
                        vertices += mesh.vertexCount;
                        triangles += Enumerable.Range(0, mesh.subMeshCount).Sum(index => (int)mesh.GetIndexCount(index) / 3);
                        var proxy = filter.gameObject.AddComponent<MeshCollider>();
                        proxy.sharedMesh = mesh;
                        proxies.Add(proxy);
                    }
                    check("workroom-mesh", $"{group.name}; meshes={filters.Length}; vertices={vertices}; triangles={triangles}; " +
                        $"room local min={local?.min:F3}; max={local?.max:F3}",
                        vertices > 0 && contained && group.GetComponentsInChildren<Collider>().Length == filters.Length);
                    VerifySupport(room, group, check);
                }
                Physics.SyncTransforms();
                VerifyCargo(room, proxies, check);
                VerifyLoot(room, proxies, check);
                if (receiving) VerifyPackingBay(room, check);
            }
            finally
            {
                foreach (var proxy in proxies) Object.DestroyImmediate(proxy);
                Physics.SyncTransforms();
            }
        }

        private static void VerifyPackingBay(BakedRoom room, Action<string, string, bool> check)
        {
            const float radius = .20f, height = .425f, foot = .02f;
            var contacts = new HashSet<string>();
            int samples = 0;
            foreach (float x in new[] { 7.50f, 7.64f, 7.78f })
                for (int i = 0; i <= 25; i++)
                {
                    float z = Mathf.Lerp(-2.52f, -3.51f, i / 25f);
                    foreach (var hit in Physics.OverlapCapsule(new Vector3(x, foot + radius, z),
                        new Vector3(x, foot + height - radius, z), radius, ~0, QueryTriggerInteraction.Ignore))
                        if (hit.transform.IsChildOf(room.Root)) contacts.Add(hit.transform.parent.name + "/" + hit.name);
                    samples++;
                }
            check("workroom-packing-bay", $"{samples} stock-crawl overlap poses through the left bay, {ContactText(contacts)}",
                contacts.Count == 0);

            var carton = room.Root.GetComponentsInChildren<Transform>().SingleOrDefault(part => part.name == "Receiving open packing carton");
            if (carton == null) return;
            var leg = room.Colliders.Single(part => part.name == "receiving-packing-table-leg-1");
            var brace = room.Colliders.Single(part => part.name == "receiving-packing-table-back-brace");
            Bounds box = carton.GetComponentsInChildren<MeshRenderer>().Select(renderer => renderer.bounds)
                .Aggregate((a, b) => { a.Encapsulate(b); return a; });
            float clearance = leg.bounds.min.x - box.max.x;
            bool legHit = Intersects(leg), braceHit = Intersects(brace);
            check("workroom-carton-placement", $"bounds gap to right leg {clearance:F3} m; " +
                $"mesh contacts leg={legHit}, rear brace={braceHit}", !legHit && !braceHit);

            bool Intersects(Collider fixture) => Physics.OverlapBox(fixture.bounds.center,
                fixture.bounds.extents - Vector3.one * .001f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
                .Any(hit => hit.transform.IsChildOf(carton));
        }

        private static void VerifySupport(BakedRoom room, Transform group, Action<string, string, bool> check)
        {
            if (group.name == "Service repair station")
            {
                var basePlate = group.Find("Steel").GetComponent<MeshRenderer>().bounds;
                var chassis = group.parent.Find("Service opened instrument chassis/Circuit").GetComponent<MeshRenderer>().bounds;
                float gap = basePlate.min.y - chassis.max.y;
                bool grounded = gap >= -.005f && gap <= .008f &&
                    Mathf.Min(basePlate.max.x, chassis.max.x) - Mathf.Max(basePlate.min.x, chassis.min.x) > .16f &&
                    Mathf.Min(basePlate.max.z, chassis.max.z) - Mathf.Max(basePlate.min.z, chassis.min.z) > .16f;
                check("workroom-support", $"{group.name}; chassis board; {gap:F4} m gap", grounded);
                return;
            }
            bool wall = group.name is "Receiving packing station" or "Receiving sorting station";
            string material = group.name switch
            {
                "Receiving sorting station" => "Teal",
                "Receiving open packing carton" => "Wood",
                _ => "Edge"
            };
            string supportName = group.name switch
            {
                "Receiving packing station" => "near-wall",
                "Receiving sorting station" => "spur-wall-left",
                "Receiving open packing carton" => "ground",
                _ => "instrument-service-inspection-bench-top"
            };
            var support = room.Root.GetComponentsInChildren<Collider>().Single(part => part.name == supportName ||
                (supportName == "spur-wall-left" && part.name == "far-wall-left"));
            var backing = group.Find(material).GetComponent<MeshRenderer>().bounds;
            Vector3 towardSupport = wall
                ? (group.name == "Receiving packing station" ? Vector3.back : Vector3.forward)
                : Vector3.down;
            var face = new Vector3(backing.center.x, wall ? backing.center.y : backing.min.y,
                wall ? (towardSupport.z > 0 ? backing.max.z : backing.min.z) : backing.center.z);
            var gaps = new List<float>();
            foreach (float offset in new[] { -.30f, 0f, .30f })
            {
                Vector3 point = face + Vector3.right * (backing.size.x * offset);
                if (support.Raycast(new Ray(point - towardSupport * .16f, towardSupport), out var hit, .25f))
                    gaps.Add(hit.distance - .16f);
            }
            check("workroom-support", $"{group.name}; {support.name}; signed gap m [{string.Join(", ", gaps.Select(value => value.ToString("F4")))}]",
                gaps.Count == 3 && gaps.All(gap => gap >= -.008f && gap <= .008f));
        }

        private static void VerifyCargo(BakedRoom room, List<MeshCollider> proxies, Action<string, string, bool> check)
        {
            foreach (var route in room.Plan.Module.Routes)
            {
                var contacts = new HashSet<string>();
                int samples = 0;
                foreach (var pose in route.Samples(.05f))
                {
                    Vector3 center = RaidPlan.Vector(pose.Position + pose.Up * (.04f + route.Envelope.Y / 2));
                    var orientation = Quaternion.LookRotation(RaidPlan.Vector(pose.Right), RaidPlan.Vector(pose.Up));
                    contacts.UnionWith(Overlap(center, RaidPlan.Vector(route.Envelope) * .499f, orientation, proxies));
                    samples++;
                }
                check("workroom-render-route", $"{route.Id}; {route.Cargo}; {route.Envelope}; {samples} samples; {ContactText(contacts)}",
                    samples > 0 && contacts.Count == 0);
            }
            foreach (var dock in room.Plan.Module.Docks)
            {
                Vector3 up = RaidPlan.Vector(dock.Threshold.Up);
                Vector3 forward = RaidPlan.Vector(dock.Threshold.Forward);
                var orientation = Quaternion.LookRotation(forward, up);
                var threshold = RaidPlan.Vector(dock.Threshold.Position);
                var half = new Vector3(dock.Opening.X / 2 - .005f, dock.Opening.Y / 2 - .02f, .24f);
                var contacts = Overlap(threshold + up * (dock.Opening.Y / 2), half, orientation, proxies).ToArray();
                check("workroom-render-doorway", $"{dock.Id}; {dock.Opening}; {ContactText(contacts)}", contacts.Length == 0);
            }
        }

        private static void VerifyLoot(BakedRoom room, List<MeshCollider> proxies, Action<string, string, bool> check)
        {
            if (room.Plan.Module.Id != "sample-receiving")
            {
                var pocket = BrokenEquipmentLoot.Find(room);
                // Existing furniture check independently probes a real-size fitting witness with
                // oversized/unsupported/closed negative controls and fixed-step settling.
                string details = "missing";
                bool available = pocket != null && pocket.EquipmentRetrieval.HasValue &&
                    ProbePocket(room, pocket, proxies, out details);
                check("workroom-render-pocket", details, available);
                return;
            }

            // Build the same 25 site volumes as the fixed receiving placement code.
            // List the exact site even if render geometry overlaps it; never silently drop a slot.
            var supports = room.Colliders.OfType<BoxCollider>().ToLookup(collider => collider.name);
            foreach (var site in SampleReceivingLoot.Sites)
            {
                var support = supports[site.Support].Single();
                Vector3 size = RaidLootSites.Envelope(site.Size);
                Vector3 center = new(site.X, support.bounds.max.y + .008f + size.y / 2, site.Z);
                Vector3 half = size / 2 - Vector3.one * .002f;
                Vector3 pull = Vector3.back * site.Side;
                var contacts = Sweep(center, half, pull, .90f, Quaternion.identity, proxies);
                check("workroom-render-loot", $"{site.Support}; {site.Size}; center={center:F3}; .90 m fetch; {ContactText(contacts)}",
                    contacts.Count == 0);
            }
        }

        private static bool ProbePocket(BakedRoom room, RaidLootSlot pocket, List<MeshCollider> proxies, out string details)
        {
            var route = pocket.EquipmentRetrieval.Value;
            Vector3 localMin = new(-.06236f, .01240f, .01448f), localMax = new(.10236f, .21237f, .23915f);
            var pose = pocket.WorldPose;
            Vector3 center = pose.position + pose.rotation * ((localMin + localMax) / 2);
            Vector3 half = (localMax - localMin) / 2 - Vector3.one * .002f;
            Vector3 towardEye = room.Root.TransformPoint(route.Eye) - center;
            float pull = (route.Opening - room.Root.InverseTransformPoint(center).z + half.z + .082f)
                / (route.Eye.z - room.Root.InverseTransformPoint(center).z) * towardEye.magnitude;
            var contacts = Sweep(center, half, towardEye.normalized, pull, pose.rotation, proxies);
            contacts.UnionWith(Physics.RaycastAll(center + towardEye, -towardEye.normalized, towardEye.magnitude,
                ~0, QueryTriggerInteraction.Ignore).Where(hit => hit.collider is MeshCollider mesh && proxies.Contains(mesh))
                .Select(hit => hit.collider.transform.parent.name + "/" + hit.collider.name));
            details = $"{pocket.Support.name}; center={center:F3}; {pull:F3} m extraction; {ContactText(contacts)}";
            return contacts.Count == 0;
        }

        internal static HashSet<string> Sweep(Vector3 center, Vector3 half, Vector3 direction, float distance,
            Quaternion orientation, List<MeshCollider> proxies)
        {
            var hits = new HashSet<string>();
            // Both directions plus sampled overlaps cover contacts inside a start pose.
            Vector3 end = center + direction * distance;
            foreach (var hit in Physics.BoxCastAll(center, half, direction, orientation, distance)
                .Concat(Physics.BoxCastAll(end, half, -direction, orientation, distance)))
                if (hit.collider is MeshCollider mesh && proxies.Contains(mesh))
                    hits.Add(mesh.transform.parent.name + "/" + mesh.name);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / .015f));
            for (int i = 0; i <= steps; i++)
                hits.UnionWith(Overlap(Vector3.Lerp(center, end, i / (float)steps), half, orientation, proxies));
            return hits;
        }

        internal static IEnumerable<string> Overlap(Vector3 center, Vector3 half, Quaternion orientation, List<MeshCollider> proxies) =>
            Physics.OverlapBox(center, half, orientation, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => hit is MeshCollider mesh && proxies.Contains(mesh))
                .Select(hit => hit.transform.parent.name + "/" + hit.name);

        internal static string ContactText(IEnumerable<string> contacts) =>
            contacts.Any() ? string.Join(", ", contacts.OrderBy(name => name)) : "clear";
    }
}

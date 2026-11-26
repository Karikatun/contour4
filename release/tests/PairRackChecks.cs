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
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SpaceJourney
{
    public static class PairRackChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const float FootGap = .02f, LaneHalf = .26f, Steering = .016f, SampleStep = .035f;
        private static readonly Body Stock = new(.20f, .425f);
        private static readonly Body Comfort = new(.24f, .50f);
        private static readonly Body Wide = new(.30f, .80f);
        private static readonly Body Standing = new(.20f, 1.70f);

        private readonly struct Body
        {
            internal readonly float Radius, Height;
            internal Body(float radius, float height) { Radius = radius; Height = height; }
        }

        private sealed class Rack
        {
            internal readonly string Module, Family, Floor, Roof, OldRoof;
            internal readonly Vector3 Front;
            internal Rack(string module, string family, string floor, Vector3 front, string roof = null, string oldRoof = null)
            {
                Module = module; Family = family; Floor = floor; Front = front;
                Roof = roof ?? family + "-shelf-0"; OldRoof = oldRoof ?? Roof;
            }
        }

        private sealed class Bay
        {
            internal readonly Rack Rack;
            internal readonly string Label;
            internal readonly Collider Roof;
            internal readonly Vector3 Origin, Across, Up, Forward;
            internal readonly Bounds Deck;
            internal readonly HashSet<string> Floors;
            internal readonly HashSet<Collider> SceneColliders;
            internal readonly Collider[] RackColliders;
            internal Bay(Rack rack, BakedRoom room, string label, Collider roof, Collider[] physical)
            {
                Rack = rack; Label = label; Roof = roof;
                var floor = room.Plan.Module.Floors.Single(item => item.Id == rack.Floor);
                Up = room.Root.TransformDirection(RaidPlan.Vector(floor.Pose.Up));
                Forward = room.Root.TransformDirection(rack.Front);
                Across = Vector3.Cross(Up, Forward).normalized;
                Vector3 floorOrigin = room.Root.TransformPoint(RaidPlan.Vector(floor.Pose.Position));
                Origin = roof.bounds.center - Up * Vector3.Dot(roof.bounds.center - floorOrigin, Up);
                Deck = LocalBounds(roof, this);
                Floors = room.Plan.Module.Geometry.Where(block => block.Role == BlockRole.Floor).Select(block => block.Id)
                    .ToHashSet(StringComparer.Ordinal);
                SceneColliders = physical.ToHashSet();
                RackColliders = physical.Where(item => item.name.StartsWith(rack.Family, StringComparison.Ordinal)).ToArray();
                if (Mathf.Abs(Vector3.Dot(Up, Forward)) > .001f) throw new InvalidOperationException("Invalid local floor axes.");
            }

            internal Vector3 Point(float x, float y, float z) => Origin + x * Across + y * Up + z * Forward;
            internal Vector3 Local(Vector3 world) => new(Vector3.Dot(world - Origin, Across),
                Vector3.Dot(world - Origin, Up), Vector3.Dot(world - Origin, Forward));
        }

        private static readonly Rack[] Inventory =
        {
            new("sample-receiving", "receiving-return-rack", "ground", Vector3.forward, "receiving-return-rack-low"),
            new("sample-receiving", "receiving-sorting-rack", "ground", Vector3.back,
                "receiving-sorting-rack-shelter-roof", "receiving-sorting-rack-middle"),
            new("freight-dispatch", "freight-dispatch-tag-and-seal-rack", "ground", Vector3.back),
            new("calibration-sector", "calibration-removed-gyro-parts", "ground", Vector3.back),
            new("observation-lab", "observation-lower-film-transport-rack", "lower", Vector3.forward),
            new("observation-lab-broken-floor", "observation-lower-film-transport-rack", "lower", Vector3.forward),
            new("gallery-chamber", "lab-observation-lower-film-transport-rack", "lab-lower", Vector3.forward),
            new("gallery-chamber", "small-lens-inspection-lens-trays", "small-floor", Vector3.down),
            new("floor-to-floor-chambers", "a-test-bay-open-parts-trays", "a", Vector3.forward),
            new("top-to-top-chambers", "a-test-bay-open-parts-trays", "a", Vector3.forward),
            new("shaft-plant", "plant-pump-spares-rack", "ground", Vector3.back),
            new("shaft-plant", "plant-balcony-rack", "middle-landing", Vector3.forward,
                "plant-balcony-rack-shelter-roof", "plant-balcony-rack-shelf"),
            new("sample-storage", "samples-packing-trays", "ground", Vector3.back),
            new("sample-storage", "near-rack", "ground", Vector3.back, "near-rack-shelter-roof", "near-rack"),
            new("sample-storage", "far-rack", "ground", Vector3.forward, "far-rack-shelter-roof", "far-rack"),
            new("sample-storage-narrow", "samples-packing-trays", "ground", Vector3.back),
            new("sample-storage-narrow", "near-rack", "ground", Vector3.back, "near-rack-shelter-roof", "near-rack"),
            new("sample-storage-narrow", "far-rack", "ground", Vector3.forward, "far-rack-shelter-roof", "far-rack"),
            new("mobius-hall", "mobius-east-storage", "ground", Vector3.left,
                "mobius-east-storage-shelf-0", "mobius-east-storage")
        };

        private static readonly List<string> Measures = new() {
            "module\tfamily\tvariant\tfloor\troof\tspan_m\tdepth_m\troof_clear_m\tclear_lane_span_m\tclear_lane_depth_m\tseat_z_m\tstart_z_m\tstock\tcomfort\twide_single\tentry_front\tphysical_lanes\tvisible_lanes\tcommon_side_cover\tcover_detail\tvisible_proxy_count"
        };
        private static readonly List<string> Checks = new() { "module\tfamily\tvariant\tcheck\tok\tdetails" };
        private static readonly List<string> Controls = new() { "module\tvariant\tpath\tsample_or_site\tpass\tobstructions" };
        private static readonly List<string> Frames = new() { "frame\tlabel\ta_x\ta_y\tb_x\tb_y\tbody_visible" };
        private static readonly List<string> Failures = new();
        private static string output;
        private static bool baseline;
        private static bool renders;

        public static void Run()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            output = Environment.GetEnvironmentVariable("CONTOUR4_PAIR_RACK_OUTPUT");
            baseline = Environment.GetEnvironmentVariable("CONTOUR4_PAIR_RACK_BASELINE") == "1";
            renders = Environment.GetEnvironmentVariable("CONTOUR4_PAIR_RACK_RENDERS") == "1";
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            using var bundles = RoomPrefabBundle.Load(Path.Combine(output, "contour4-rooms.bundle"),
                Path.Combine(output, "contour4-prepared-modules.bundle"), requireWear: true);
            try
            {
                foreach (var group in Inventory.GroupBy(rack => rack.Module))
                {
                    string id = group.Key;
                    if (id == "sample-receiving")
                    {
                        foreach (bool research in new[] { false, true })
                            foreach (bool loaded in new[] { true, false })
                                Fixture(FreightServiceRoom.Create(id, research), group, bundles.Prefabs,
                                    $"{(research ? "research" : "plain")}-{(loaded ? "bundle" : "fallback")}", false, loaded, false, research);
                    }
                    else if (id == "freight-dispatch")
                    {
                        foreach (var (research, annex, label) in new[] {
                            (false, false, "plain"), (true, false, "research"), (false, true, "west-annex") })
                            Fixture(FreightServiceRoom.Create(id, research, annex), group, bundles.Prefabs, label, false, false, false, research);
                    }
                    else
                    {
                        foreach (bool prepared in new[] { true, false })
                            Fixture(ModuleCatalog.Rooms().Single(module => module.Id == id), group, bundles.Prefabs,
                                prepared ? "prepared" : "fallback", prepared, false, false, false);
                        if (id is "gallery-chamber" or "shaft-plant" or "floor-to-floor-chambers" or "mobius-hall")
                            Fixture(ModuleCatalog.Rooms().Single(module => module.Id == id), group, bundles.Prefabs,
                                "prepared-rotated", true, false, true, false);
                    }
                }
                if (Measures.Count - 1 != 49) Failures.Add($"Expected 49 rack placements, got {Measures.Count - 1}.");
            }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "rack-measurements.tsv"), Measures);
                File.WriteAllLines(Path.Combine(output, "rack-checks.tsv"), Checks);
                File.WriteAllLines(Path.Combine(output, "room-regressions.tsv"), Controls);
                File.WriteAllLines(Path.Combine(output, "rack-frames.tsv"), Frames);
                File.WriteAllLines(Path.Combine(output, "rack-failures.txt"), Failures);
            }
            Debug.Log($"CONTOUR4 PAIR RACK CHECK COMPLETED: {Measures.Count - 1} rack placements, " +
                $"{Checks.Count - 1} rack observations, {Controls.Count - 1} route and loot records, {Failures.Count} failed checks.");
            if (!baseline && Failures.Count != 0) throw new InvalidOperationException("Pair rack observations failed; see rack-failures.txt.");
        }

        private static void Fixture(GrayModule module, IEnumerable<Rack> racks, RaidRoomPrefabs prefabs,
            string label, bool prepared, bool loaded, bool rotated, bool research)
        {
            var host = new GameObject("Rack evidence fixture");
            try
            {
                var raid = host.AddComponent<RaidAssembly>();
                var pose = rotated ? new Pose(new(11, 21, -17), Quaternion.Euler(39, 63, 117)) : new Pose(Vector3.zero, Quaternion.identity);
                var plan = new RaidPlan.Room(module, pose, pose, 0);
                foreach (var dock in module.Docks.Where(dock => dock.Id is "west" or "east" || research && dock.Id == "research-door"))
                    plan.OpenPlugs.Add(dock.PlugId);
                var shader = Shader.Find("Standard");
                var fitout = (RaidInteriorFitout)Invoke(raid, "CreateFitout", shader);
                var obstacles = (Material)Invoke(raid, "Material", shader, new Color(.39f, .33f, .25f));
                BakedRoom room;
                if (prepared)
                {
                    if (!prefabs.TryGetPrepared(plan, out var prefab)) throw new InvalidOperationException("Prepared room unavailable.");
                    room = raid.PlacePrepared(plan, 0, prefab, null, fitout);
                }
                else
                {
                    if (module.Id is "sample-receiving" or "freight-dispatch") RaidCatalogSites.Dress(module);
                    RaidLootSites.Dress(module);
                    var (floor, wall) = ((Material, Material))Invoke(raid, "Finishes", shader);
                    var shell = (Material)Invoke(raid, "Material", shader, new Color(.52f, .56f, .57f));
                    var sight = new RaidSightStrips(shader, Field<List<Material>>(raid, "materials"), Field<List<Mesh>>(raid, "meshes"));
                    room = (BakedRoom)Invoke(raid, "PlaceRoom", plan, 0, floor, wall, shell, obstacles, sight,
                        loaded ? prefabs.For(plan) : null, fitout);
                }
                Field<RoomBloodTraces>(raid, "bloodTraces").Apply(room);
                Physics.SyncTransforms();
                RecordRoomControls(room, prefabs, label);
                foreach (Rack rack in racks)
                {
                    try { Inspect(room, rack, label); }
                    catch (Exception error)
                    {
                        string failure = $"{rack.Module}/{rack.Family}/{label}: {error}";
                        Failures.Add(failure);
                        Debug.LogWarning(failure);
                    }
                }
            }
            finally { Object.DestroyImmediate(host); Physics.SyncTransforms(); }
        }

        private static void Inspect(BakedRoom room, Rack rack, string label)
        {
            var physical = room.Root.GetComponentsInChildren<Collider>().Where(hit => hit.enabled && !hit.isTrigger).ToArray();
            var roof = physical.SingleOrDefault(hit => hit.name == rack.Roof);
            if (baseline && !roof) roof = physical.SingleOrDefault(hit => hit.name == rack.OldRoof);
            if (!roof) throw new InvalidOperationException("Physical roof missing: " + rack.Roof);
            var bay = new Bay(rack, room, label, roof, physical);
            Bounds deck = bay.Deck;
            float seat = deck.center.z;
            float minSeat = deck.min.z + Comfort.Radius + Steering + .005f;
            float maxSeat = deck.max.z - Comfort.Radius - Steering - .005f;
            float start = deck.max.z + .42f;
            var seatCandidates = new List<float> { seat };
            if (minSeat <= maxSeat)
                seatCandidates.AddRange(new[] { Mathf.Lerp(minSeat, maxSeat, .25f), Mathf.Lerp(minSeat, maxSeat, .75f) });
            (float Seat, float Start) choice = (seat, start);
            bool physicsChoice = false;
            foreach (float offset in new[] { .42f, .52f, .65f, .82f })
            {
                foreach (float candidate in seatCandidates)
                {
                    float approach = deck.max.z + offset;
                    if (!Interior(bay, candidate, Comfort) || !StartClear(bay, approach)) continue;
                    if (ProbeLanes(bay, candidate, approach, Comfort).Count != 0) continue;
                    choice = (candidate, approach);
                    physicsChoice = true;
                    break;
                }
                if (physicsChoice) break;
            }
            seat = choice.Seat;
            start = choice.Start;

            bool stockCovered = Interior(bay, seat, Stock);
            bool comfortCovered = Interior(bay, seat, Comfort);
            bool entrance = StartClear(bay, start);
            var stockPhysics = ProbeLanes(bay, seat, start, Stock);
            var comfortPhysics = ProbeLanes(bay, seat, start, Comfort);
            var (horizontalSpan, coveredDepth) = ClearDimensions(bay, seat);
            Record("stock-footprint-roof-and-standing", stockCovered, $"roof underside {deck.min.y:F3}, full capsule disk at either seat");
            Record("comfort-footprint-roof-and-standing", comfortCovered, $"roof underside {deck.min.y:F3}, full capsule and ±{Steering:F3} m");
            Record("approach-stand-and-floor", entrance, $"z={start:F3} relative to roof center");
            Record("stock-physics", stockPhysics.Count == 0, ContactText(stockPhysics));
            Record("comfort-physics", comfortPhysics.Count == 0, ContactText(comfortPhysics));

            var proxies = VisibleProxies(room, bay, start);
            (bool side, string sideDetail) = (false, "");
            bool stock = false, comfort = false, wide = false;
            string visibleResult;
            try
            {
                var stockArt = ProbeLanes(bay, seat, start, Stock);
                var comfortArt = ProbeLanes(bay, seat, start, Comfort);
                (side, sideDetail) = CommonSideCover(bay, seat);
                stock = stockCovered && entrance && stockArt.Count == 0 && side;
                comfort = comfortCovered && entrance && comfortArt.Count == 0 && side;
                wide = WideSingle(bay, seat, start);
                Record("stock-visible-and-physical", stockArt.Count == 0, ContactText(stockArt));
                Record("comfort-visible-and-physical", comfortArt.Count == 0, ContactText(comfortArt));
                Record("common-side-occlusion", side, sideDetail);
                Record("single-wide-informative", rack.Family != "receiving-sorting-rack" || wide, $"0.60 x 0.80 fits: {wide}");
                visibleResult = ContactText(comfortArt);
            }
            finally
            {
                foreach (var proxy in proxies) Object.DestroyImmediate(proxy);
                Physics.SyncTransforms();
            }
            Measures.Add($"{rack.Module}\t{rack.Family}\t{label}\t{rack.Floor}\t{roof.name}\t{deck.size.x:F3}\t{deck.size.z:F3}\t" +
                $"{deck.min.y:F3}\t{horizontalSpan:F3}\t{coveredDepth:F3}\t{seat:F3}\t{start:F3}\t{stock}\t{comfort}\t{wide}\t{entrance}\t" +
                $"{ContactText(comfortPhysics)}\t{visibleResult}\t{side}\t{sideDetail}\t{proxies.Length}");
            if (renders && label is "prepared" or "research" or "research-bundle") Render(room, bay, seat, comfort);

            void Record(string check, bool passed, string details)
            {
                Checks.Add($"{rack.Module}\t{rack.Family}\t{label}\t{check}\t{passed}\t{details}");
                if (!passed) Failures.Add($"{rack.Module}/{rack.Family}/{label}/{check}: {details}");
            }
        }

        private static Bounds LocalBounds(Collider collider, Bay bay)
        {
            IEnumerable<Vector3> Points()
            {
                if (collider is MeshCollider mesh)
                {
                    foreach (var vertex in mesh.sharedMesh.vertices) yield return mesh.transform.TransformPoint(vertex);
                    yield break;
                }
                if (collider is not BoxCollider box) throw new InvalidOperationException("Rack cover is not box or mesh.");
                foreach (float x in new[] { -.5f, .5f })
                    foreach (float y in new[] { -.5f, .5f })
                        foreach (float z in new[] { -.5f, .5f })
                            yield return box.transform.TransformPoint(box.center + Vector3.Scale(box.size, new Vector3(x, y, z)));
            }
            using var it = Points().GetEnumerator();
            it.MoveNext();
            Bounds result = new(bay.Local(it.Current), Vector3.zero);
            while (it.MoveNext()) result.Encapsulate(bay.Local(it.Current));
            return result;
        }

        private static bool Interior(Bay bay, float depth, Body body)
        {
            if (bay.Deck.min.y < body.Height + FootGap + .002f) return false;
            foreach (int lane in new[] { -1, 1 })
                foreach (float steering in new[] { -Steering, Steering })
                {
                    float x = lane * LaneHalf + steering;
                    for (int i = 0; i < 12; ++i)
                    {
                        float angle = i * Mathf.PI * 2f / 12;
                        float span = x + Mathf.Cos(angle) * body.Radius;
                        float z = depth + Mathf.Sin(angle) * body.Radius;
                        if (!bay.Roof.Raycast(new Ray(bay.Point(span, .055f, z), bay.Up), out var hit, 1.69f) ||
                            Vector3.Dot(hit.normal, bay.Up) > -.95f) return false;
                    }
                    var feet = bay.Point(x, 0, depth);
                    if (Free(feet, bay.Up, Standing)) return false;
                }
            return true;
        }

        private static bool StartClear(Bay bay, float start) => new[] { -1, 1 }.All(lane =>
            new[] { -Steering, Steering }.All(offset => {
                var position = bay.Point(lane * LaneHalf + offset, 0, start);
                return Free(position, bay.Up, Standing) && Supported(bay, position, Standing.Radius);
            }));

        private static HashSet<string> ProbeLanes(Bay bay, float seat, float start, Body body)
        {
            var contacts = new HashSet<string>(StringComparer.Ordinal);
            foreach (int stationary in new[] { -1, 1 })
            {
                foreach (float steering in new[] { -Steering, 0f, Steering })
                {
                    float x = -stationary * LaneHalf + steering;
                    Vector3 hold = bay.Point(stationary * (LaneHalf - Steering), 0, seat);
                    var buddy = new GameObject("WITNESS stationary buddy");
                    try
                    {
                        buddy.transform.SetPositionAndRotation(hold, Quaternion.LookRotation(bay.Forward, bay.Up));
                        var collider = buddy.AddComponent<CapsuleCollider>();
                        collider.radius = body.Radius;
                        collider.height = body.Height;
                        collider.center = Vector3.up * (body.Height / 2 + FootGap);
                        Physics.SyncTransforms();
                        var from = bay.Point(x, 0, start);
                        var mouth = bay.Point(x, 0, bay.Deck.max.z + .04f);
                        var to = bay.Point(x, 0, seat);
                        var dogleg = bay.Point(-stationary * LaneHalf, 0, bay.Deck.max.z + .13f);
                        Sweep(bay, new[] { from, dogleg, mouth, to, mouth, dogleg, from }, body, contacts, collider);
                        if (!Supported(bay, hold, body.Radius)) contacts.Add("stationary witness floor support");
                    }
                    finally { Object.DestroyImmediate(buddy); Physics.SyncTransforms(); }
                }
            }
            return contacts;
        }

        private static void Sweep(Bay bay, Vector3[] points, Body body, HashSet<string> contacts, Collider stationary = null)
        {
            for (int index = 1; index < points.Length; ++index)
            {
                Vector3 from = points[index - 1], delta = points[index] - from;
                Capsule(from, bay.Up, body, out var low, out var high);
                foreach (var hit in Physics.CapsuleCastAll(low, high, body.Radius, delta.normalized, delta.magnitude,
                             ~0, QueryTriggerInteraction.Ignore))
                    contacts.Add(Contact(hit.collider));
                int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / SampleStep));
                for (int i = 0; i <= steps; ++i)
                {
                    var p = from + delta * (i / (float)steps);
                    Capsule(p, bay.Up, body, out low, out high);
                    foreach (var hit in Physics.OverlapCapsule(low, high, body.Radius, ~0, QueryTriggerInteraction.Ignore))
                        contacts.Add(Contact(hit));
                    if (!Supported(bay, p, body.Radius)) contacts.Add("moving witness floor support");
                }
            }
            string Contact(Collider hit) => hit == stationary ? "stationary buddy" : hit.name;
        }

        private static bool Supported(Bay bay, Vector3 feet, float radius)
        {
            foreach (var offset in new[] { Vector3.zero, bay.Across * radius, -bay.Across * radius,
                         bay.Forward * radius, -bay.Forward * radius })
            {
                // Render-only query surfaces, such as paint markings, must not replace the physical supporting deck.
                var hit = Physics.RaycastAll(feet + offset + bay.Up * .045f, -bay.Up, .075f, ~0, QueryTriggerInteraction.Ignore)
                    .Where(candidate => bay.SceneColliders.Contains(candidate.collider)).OrderBy(candidate => candidate.distance).FirstOrDefault();
                if (!hit.collider || !bay.Floors.Contains(hit.collider.name) || Vector3.Dot(hit.normal, bay.Up) < .85f) return false;
            }
            return true;
        }

        private static bool Free(Vector3 feet, Vector3 up, Body body)
        {
            Capsule(feet, up, body, out var low, out var high);
            return !Physics.CheckCapsule(low, high, body.Radius, ~0, QueryTriggerInteraction.Ignore);
        }

        private static void Capsule(Vector3 feet, Vector3 up, Body body, out Vector3 low, out Vector3 high)
        {
            low = feet + up * (FootGap + body.Radius);
            high = feet + up * (FootGap + body.Height - body.Radius);
        }

        private static MeshCollider[] VisibleProxies(BakedRoom room, Bay bay, float start)
        {
            Bounds region = new(bay.Origin, Vector3.zero);
            foreach (float x in new[] { bay.Deck.min.x - 1.0f, bay.Deck.max.x + 1.0f })
                foreach (float z in new[] { bay.Deck.min.z - .85f, start + .4f })
                    foreach (float y in new[] { 0, 1.75f })
                        region.Encapsulate(bay.Point(x, y, z));
            var proxies = room.Root.GetComponentsInChildren<MeshRenderer>()
                .Where(render => render.enabled && !render.forceRenderingOff && render.shadowCastingMode != ShadowCastingMode.ShadowsOnly &&
                    render.bounds.Intersects(region)).Select(render => render.GetComponent<MeshFilter>())
                .Where(filter => filter && filter.sharedMesh).Where(filter =>
                    !filter.GetComponents<Collider>().OfType<MeshCollider>().Any(mesh => mesh.enabled && mesh.sharedMesh == filter.sharedMesh))
                .Select(filter => {
                    var proxy = filter.gameObject.AddComponent<MeshCollider>();
                    proxy.sharedMesh = filter.sharedMesh;
                    return proxy;
                }).ToArray();
            Physics.SyncTransforms();
            return proxies;
        }

        private static (float span, float depth) ClearDimensions(Bay bay, float seat)
        {
            var point = bay.Point(0, .28f, seat);
            float Distance(Vector3 direction, float max) => Physics.Raycast(point, direction, out var hit, max, ~0,
                QueryTriggerInteraction.Ignore) ? hit.distance : max;
            float span = Distance(bay.Across, bay.Deck.max.x) + Distance(-bay.Across, -bay.Deck.min.x);
            float depth = Distance(-bay.Forward, seat - bay.Deck.min.z) + Distance(bay.Forward, bay.Deck.max.z - seat);
            return (span, depth);
        }

        private static (bool, string) CommonSideCover(Bay bay, float seat)
        {
            bool witness = false;
            var lines = new List<string>();
            foreach (int side in new[] { -1, 1 })
            {
                foreach (float frontShift in new[] { 0f, .10f, -.10f })
                    foreach (float distance in new[] { .24f, .46f, .68f, 1.36f, 1.56f })
                    {
                        float edge = side < 0 ? bay.Deck.min.x : bay.Deck.max.x;
                        Vector3 from = bay.Point(edge + distance * side, .43f, seat + frontShift);
                        if (Physics.CheckSphere(from, .035f, ~0, QueryTriggerInteraction.Ignore) ||
                            !Supported(bay, from - bay.Up * .43f, .035f)) continue;
                        var hits = new List<string>();
                        bool all = true;
                        foreach (int lane in new[] { -1, 1 })
                            foreach (float height in new[] { .22f, .40f })
                            {
                                var to = bay.Point(lane * LaneHalf, height, seat);
                                bool hit = Physics.Linecast(from, to, out var cover, ~0, QueryTriggerInteraction.Ignore);
                                var ray = new Ray(from, (to - from).normalized);
                                var panel = bay.RackColliders.FirstOrDefault(collider => collider != bay.Roof &&
                                    collider.Raycast(ray, out _, Vector3.Distance(from, to)));
                                var name = hit ? cover.collider.name : "exposed";
                                hits.Add(panel && panel != cover.collider ? $"{name} → {panel.name}" : name);
                                all &= hit && panel;
                            }
                        witness |= all;
                        if (all || lines.Count < 4) lines.Add($"{side:+0;-0}/{frontShift:F2}/{distance:F2} {string.Join(",", hits)}");
                        if (all) break;
                    }
            }
            return (witness, lines.Count == 0 ? "no supported side observer" : string.Join(" | ", lines));
        }

        private static bool WideSingle(Bay bay, float depth, float start)
        {
            if (bay.Deck.min.y < Wide.Height + FootGap) return false;
            foreach (float x in new[] { -Wide.Radius, Wide.Radius })
                if (!bay.Roof.Raycast(new Ray(bay.Point(x, .05f, depth), bay.Up), out _, 1.7f)) return false;
            foreach (float z in new[] { depth - Wide.Radius, depth + Wide.Radius })
                if (!bay.Roof.Raycast(new Ray(bay.Point(0, .05f, z), bay.Up), out _, 1.7f)) return false;
            var contacts = new HashSet<string>();
            Sweep(bay, new[] { bay.Point(0, 0, start), bay.Point(0, 0, depth), bay.Point(0, 0, start) }, Wide, contacts);
            return contacts.Count == 0;
        }

        private static void RecordRoomControls(BakedRoom room, RaidRoomPrefabs prefabs, string label)
        {
            var module = room.Plan.Module;
            var registered = new List<RaidLootSlot>();
            if (room.Prepared) registered.AddRange(prefabs.PreparedLoot(room));
            if (module.Id == "sample-receiving")
            {
                const BindingFlags LootPrivate = BindingFlags.Static | BindingFlags.NonPublic;
                var supports = room.Colliders.OfType<BoxCollider>().ToLookup(collider => collider.name);
                var routes = (IEnumerable<Bounds>)typeof(RaidLootSites).GetMethod("RouteSweeps", LootPrivate).Invoke(null, new object[] { room });
                typeof(RaidLootSites).GetMethod("AddReceivingSites", LootPrivate).Invoke(null, new object[] { room, supports, routes.ToArray(), registered });
            }
            var structure = RaidLootPlacement.StructuralBounds(room);
            foreach (RaidLootSlot slot in registered)
            {
                var shape = RaidLootSites.Envelope(slot.Size);
                bool clear = slot.Placement(structure)?.AllowsEnvelope(slot.WorldPose, shape) == true;
                Controls.Add($"{module.Id}\t{label}\tloot\t{slot.Support.name}/{slot.Size}/{room.Root.InverseTransformPoint(slot.WorldPose.position):F3}\t{clear}\t");
            }
            foreach (var route in module.Routes.Where(route => route.Cargo == Cargo.Cart))
            {
                int index = 0;
                foreach (var pose in route.Samples(.12f))
                {
                    Vector3 center = room.Root.TransformPoint(RaidPlan.Vector(pose.Position + pose.Up * (.04f + route.Envelope.Y / 2)));
                    var rotation = room.Root.rotation * Quaternion.LookRotation(RaidPlan.Vector(pose.Right), RaidPlan.Vector(pose.Up));
                    var contacts = Physics.OverlapBox(center, RaidPlan.Vector(route.Envelope) * .495f, rotation, ~0,
                        QueryTriggerInteraction.Ignore).Select(hit => hit.name).Distinct().OrderBy(name => name).ToArray();
                    Controls.Add($"{module.Id}\t{label}\t{route.Id}\t{index++}\t{contacts.Length == 0}\t{ContactText(contacts)}");
                }
            }
        }

        private static void Render(BakedRoom room, Bay bay, float seat, bool pair)
        {
            var camera = new GameObject("Inspection camera").AddComponent<Camera>();
            var light = new GameObject("Inspection light").AddComponent<Light>();
            var frame = RenderTexture.GetTemporary(1000, 700, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(1000, 700, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var lamps = room.Root.GetComponentsInChildren<Light>().Where(item => item.enabled).ToArray();
            var witnesses = new List<GameObject>();
            var materials = new List<Material>();
            var meshes = new List<Mesh>();
            try
            {
                foreach (var lamp in lamps) lamp.enabled = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.44f, .44f, .44f);
                RenderSettings.reflectionIntensity = 0;
                Vector3 target = bay.Point(0, .64f, bay.Deck.center.z);
                Vector3 eye = bay.Point(.04f, .48f, bay.Deck.max.z + 1.60f);
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, bay.Up));
                camera.fieldOfView = 62;
                camera.nearClipPlane = .035f;
                camera.targetTexture = frame;
                light.type = LightType.Directional;
                light.intensity = .9f;
                light.transform.rotation = Quaternion.LookRotation(-bay.Forward + bay.Across * .38f - bay.Up * .46f, bay.Up);
                if (pair)
                {
                    foreach (int lane in new[] { -1, 1 })
                    {
                        var visual = new GameObject(lane < 0 ? "Witness A" : "Witness B");
                        witnesses.Add(visual);
                        visual.transform.SetPositionAndRotation(bay.Point(lane * LaneHalf, FootGap, seat),
                            Quaternion.LookRotation(bay.Forward, bay.Up));
                        var geometry = CapsuleMesh(Comfort);
                        meshes.Add(geometry);
                        var material = new Material(Shader.Find("Unlit/Color")) {
                            color = lane < 0 ? new Color(.19f, .78f, 1f) : new Color(1f, .67f, .19f)
                        };
                        materials.Add(material);
                        visual.AddComponent<MeshFilter>().sharedMesh = geometry;
                        visual.AddComponent<MeshRenderer>().sharedMaterial = material;
                    }
                }
                camera.Render();
                RenderTexture.active = frame;
                image.ReadPixels(new Rect(0, 0, 1000, 700), 0, 0);
                image.Apply();
                string file = $"{bay.Rack.Module}--{bay.Rack.Family}--{bay.Label}.png";
                File.WriteAllBytes(Path.Combine(output, file), image.EncodeToPNG());
                Vector3 a = camera.WorldToScreenPoint(bay.Point(-LaneHalf, .29f, seat));
                Vector3 b = camera.WorldToScreenPoint(bay.Point(LaneHalf, .29f, seat));
                Frames.Add($"{file}\t{bay.Rack.Module}/{bay.Rack.Family}\t{a.x:F0}\t{(700-a.y):F0}\t{b.x:F0}\t{(700-b.y):F0}\t{pair}");
            }
            finally
            {
                foreach (var lamp in lamps) lamp.enabled = true;
                foreach (var witness in witnesses) Object.DestroyImmediate(witness);
                foreach (var material in materials) Object.DestroyImmediate(material);
                foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
                RenderTexture.active = previous;
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(light.gameObject);
                Object.DestroyImmediate(image);
                RenderTexture.ReleaseTemporary(frame);
            }
        }

        private static Mesh CapsuleMesh(Body body)
        {
            const int sides = 36, ringCount = 16;
            var points = new List<Vector3>();
            var triangles = new List<int>();
            for (int ring = 0; ring <= ringCount; ++ring)
            {
                float angle = Mathf.PI * ring / ringCount;
                float radius = Mathf.Sin(angle) * body.Radius;
                float height = body.Height / 2 - Mathf.Cos(angle) * body.Radius +
                    Mathf.Sign(ring - ringCount / 2) * (body.Height / 2 - body.Radius);
                for (int side = 0; side < sides; ++side)
                {
                    float turn = 2 * Mathf.PI * side / sides;
                    points.Add(new Vector3(radius * Mathf.Cos(turn), height, radius * Mathf.Sin(turn)));
                    if (ring == ringCount) continue;
                    int a = ring * sides + side, b = ring * sides + (side + 1) % sides;
                    triangles.Add(a); triangles.Add(a + sides); triangles.Add(b);
                    triangles.Add(b); triangles.Add(a + sides); triangles.Add(b + sides);
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(points);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        private static string ContactText(IEnumerable<string> contact) => contact.Any() ?
            string.Join(", ", contact.OrderBy(item => item)) : "clear";
        private static T Field<T>(RaidAssembly raid, string name) => (T)typeof(RaidAssembly).GetField(name, Private).GetValue(raid);
        private static object Invoke(RaidAssembly raid, string name, params object[] args) =>
            typeof(RaidAssembly).GetMethod(name, Private).Invoke(raid, args);
    }
}

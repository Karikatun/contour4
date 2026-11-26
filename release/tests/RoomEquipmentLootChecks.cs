using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceJourney
{
    public static class RoomEquipmentLootChecks
    {
        private static readonly (int Progress, int Seed)[] Layouts = { (0, 2133), (0, -32), (3, -31) };

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var output = Environment.GetEnvironmentVariable("CONTOUR4_EQUIPMENT_LOOT_OUTPUT");
            using var bundles = RoomPrefabBundle.Load(Environment.GetEnvironmentVariable("CONTOUR4_ROOMS_BUNDLE"),
                Environment.GetEnvironmentVariable("CONTOUR4_PREPARED_BUNDLE"), requireWear: true);
            var control = new List<string>();
            var picked = new HashSet<string>();
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            foreach (var (progress, seed) in Layouts)
            {
                var root = new GameObject("Equipment loot inspection");
                try
                {
                    var raid = root.AddComponent<RaidAssembly>();
                    raid.Place(seed, new Pose(new Vector3(11, 2, -18), Quaternion.Euler(0, 90, 0)), 0, 0,
                        catalogSites: true, completedLevels: progress, roomPrefabs: bundles.Prefabs);
                    Physics.SyncTransforms();
                    foreach (RaidLootSlot site in RaidLootSites.Find(raid))
                        control.Add($"{seed}|ordinary|{site.Room.Plan.Index}|{site.Kind}|{site.Size}|{site.FloorId}|{site.Support.name}|{site.Pose.position:F4}");
                    foreach (var room in raid.Rooms)
                    {
                        control.Add($"{seed}|room|{room.Plan.Index}|{room.Plan.Module.Id}|{room.Root.position:F4}");
                        foreach (var light in room.Root.GetComponentsInChildren<Light>())
                            control.Add($"{seed}|light|{room.Plan.Index}|{light.name}|{light.transform.position:F4}|{light.color}|{light.intensity}|{light.range}");
#if CONTOUR4_EQUIPMENT_TEST
                        var pocket = BrokenEquipmentLoot.Find(room);
                        if (pocket != null && picked.Add(room.Plan.Module.Id)) VerifyPocket(room, pocket);
#endif
                        if (room.Plan.Module.Id is "instrument-service" or "shaft-plant")
                            VerifyRoutes(room, seed, control);
                    }
                }
                finally { Object.DestroyImmediate(root); }
            }
#if CONTOUR4_EQUIPMENT_TEST
            Require(picked.SetEquals(new[] { "instrument-service", "shaft-plant" }), "missing one fitted device");
            VerifyIntactControl();
#endif
            File.WriteAllLines(Path.Combine(output, "parity.txt"), control);
            Debug.Log($"CONTOUR4 EQUIPMENT LOOT PASSED: {picked.Count} fitted devices, {Layouts.Length} layouts, {control.Count} parity records.");
        }

        private static void VerifyRoutes(BakedRoom room, int seed, List<string> output)
        {
            foreach (var route in room.Plan.Module.Routes)
            {
                var envelope = RaidPlan.Vector(route.Envelope);
                int count = 0;
                int blocked = 0;
                var trace = new List<string>();
                foreach (var sample in route.Samples(.14f))
                {
                    Vector3 up = RaidPlan.Vector(sample.Up);
                    var center = RaidPlan.Vector(sample.Position) + up * (envelope.y * .5f + route.StepClearance + .04f);
                    var orientation = Quaternion.LookRotation(RaidPlan.Vector(sample.Forward), up);
                    var touched = Physics.OverlapBox(room.Root.TransformPoint(center),
                        new Vector3(envelope.z, envelope.y, envelope.x) / 2 - Vector3.one * .005f,
                        room.Root.rotation * orientation, ~0, QueryTriggerInteraction.Ignore)
                        .Select(part => part.name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                    if (touched.Length > 0)
                    {
                        blocked++;
                        trace.Add($"{count}:{string.Join(",", touched)}");
                    }
                    count++;
                }
                output.Add($"{seed}|route|{room.Plan.Index}|{route.Id}|{route.Cargo}|{count}|{blocked}|{string.Join(";", trace)}");
            }
        }

#if CONTOUR4_EQUIPMENT_TEST
        private static void VerifyPocket(BakedRoom room, RaidLootSlot pocket)
        {
            string id = room.Plan.Module.Id;
            var route = pocket.EquipmentRetrieval.Value;
            var placement = pocket.Placement(RaidLootPlacement.StructuralBounds(room));
            bool hasFloor = room.TryFloor("ground", out var floor) && floor.FloorId == pocket.FloorId;
            Require(hasFloor && pocket.Size == RaidLootSize.Tiny && placement.EquipmentPocket,
                $"{id}: no Tiny slot on its exact ground floor");
            Require(pocket.LocalBounds.max.z < route.Opening && room.Colliders.Contains(pocket.Support),
                $"{id}: slot extends outside the equipment or has no authored support");

            // Geometry metadata read from the pinned R.E.P.O. 0.5.2 stock resources,
            // without importing or distributing any stock asset. The live game tests
            // are responsible for proof on the prefabs themselves.
            bool instrument = id == "instrument-service";
            Bounds visual = instrument
                ? LocalBounds(new(-.06236f, .02300f, .01619f), new(.10236f, .21237f, .23915f))
                : LocalBounds(new(-.07270f, .00476f, .04530f), new(.07270f, .24264f, .19070f));
            Bounds[] solids = instrument
                ? new[] {
                    LocalBounds(new(-.05152f, .01240f, .01545f), new(.08848f, .05858f, .23299f)),
                    LocalBounds(new(-.05200f, .02368f, .01448f), new(.08800f, .20632f, .06752f))
                }
                : new[] { LocalBounds(new(-.07398f, .00487f, .04418f), new(.07390f, .24189f, .19122f)) };
            Bounds physical = solids[0];
            foreach (var shell in solids.Skip(1)) physical.Encapsulate(shell);
            Bounds combined = visual;
            combined.Encapsulate(physical);
            var valid = CreateProbe(visual, solids);
            var oversized = new Bounds(visual.center, new Vector3(.455f, visual.size.y, visual.size.z));
            var tooWide = CreateProbe(oversized, oversized);
            var previousMode = Physics.simulationMode;
            try
            {
                Pose pose = pocket.WorldPose;
                Physics.SyncTransforms();
                DescribeClearance(room, pocket, placement, physical, combined);
                Require(Contour4.NativeRaid.RaidLootPrefab.Fits(valid, placement, pose), $"{id}: fitted probe cannot clear the shell");
                Require(!Contour4.NativeRaid.RaidLootPrefab.Fits(tooWide, placement, pose), $"{id}: oversized Tiny-class probe accepted");
                Require(!Contour4.NativeRaid.RaidLootPrefab.Fits(valid, placement, new Pose(pose.position + pose.up * .045f, pose.rotation)),
                    $"{id}: unsupported elevated probe accepted");

                Physics.simulationMode = SimulationMode.Script;
                valid.transform.SetPositionAndRotation(pose.position, pose.rotation);
                valid.SetActive(true);
                var body = valid.AddComponent<Rigidbody>();
                body.mass = .4f;
                body.centerOfMass = physical.center;
                body.useGravity = false;
                body.position = pose.position;
                body.rotation = pose.rotation;
                Physics.SyncTransforms();
                for (int tick = 0; tick < 60; tick++)
                {
                    if (!body.IsSleeping()) body.AddForce(-pose.up * Physics.gravity.magnitude, ForceMode.Acceleration);
                    Physics.Simulate(.02f);
                }
                Vector3 bottom = valid.transform.TransformPoint(new Vector3(physical.center.x, physical.min.y, physical.center.z));
                bool settled = placement.Supports(bottom, pose.up);
                float drift = Vector3.Distance(body.position, pose.position);
                Require(body.linearVelocity.magnitude < .035f && settled && drift < .015f,
                    $"{id}: released probe did not settle on its tray; supported={settled}, drift={drift:F4}, speed={body.linearVelocity.magnitude:F4}");
                valid.SetActive(false);
                Physics.SyncTransforms();

                var occluder = new GameObject("Fixture-only closed aperture");
                try
                {
                    occluder.transform.SetParent(room.Root, false);
                    occluder.transform.localPosition = new Vector3(pocket.Pose.position.x,
                        pocket.Pose.position.y + .08f, route.Opening);
                    occluder.AddComponent<BoxCollider>().size = new Vector3(.19f, .24f, .012f);
                    Physics.SyncTransforms();
                    Require(!Contour4.NativeRaid.RaidLootPrefab.Fits(valid, placement, pose), $"{id}: occluded probe accepted");
                }
                finally { Object.DestroyImmediate(occluder); }
                Debug.Log($"CONTOUR4 EQUIPMENT LOOT PROBE | {id}/ground | support={pocket.Support.name} | " +
                    $"pivot={pocket.Pose.position:F3} | clearance={pocket.LocalBounds.size:F3} | settled=yes | reject=oversized/floating/closed");
            }
            finally
            {
                Physics.simulationMode = previousMode;
                Object.DestroyImmediate(valid);
                Object.DestroyImmediate(tooWide);
                Physics.SyncTransforms();
            }
        }

        private static Bounds LocalBounds(Vector3 min, Vector3 max) => new((min + max) / 2, max - min);

        private static GameObject CreateProbe(Bounds visual, params Bounds[] solids)
        {
            var root = new GameObject("Offline physical fitting probe");
            root.SetActive(false);
            var visible = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visible.transform.SetParent(root.transform, false);
            visible.transform.localScale = visual.size;
            visible.transform.localPosition = visual.center;
            Object.DestroyImmediate(visible.GetComponent<Collider>());
            foreach (var solid in solids)
            {
                var collider = root.AddComponent<BoxCollider>();
                collider.center = solid.center;
                collider.size = solid.size;
            }
            return root;
        }

        private static void DescribeClearance(BakedRoom room, RaidLootSlot pocket, RaidLootPlacement placement,
            Bounds physical, Bounds combined)
        {
            Vector3 half = combined.extents;
            Pose pose = pocket.WorldPose;
            var route = pocket.EquipmentRetrieval.Value;
            Vector3 bottom = pose.position + pose.rotation * new Vector3(physical.center.x, physical.min.y, physical.center.z);
            Vector3 center = pose.position + pose.rotation * combined.center;
            Vector3 eye = room.Root.TransformPoint(route.Eye);
            Vector3 towardEye = (eye - center).normalized;
            float localZ = room.Root.InverseTransformPoint(center).z;
            float pull = (route.Opening - localZ + half.z + .08f) /
                (route.Eye.z - localZ) * Vector3.Distance(eye, center);
            Vector3 extracted = center + towardEye * pull;
            Vector3 extent = half - Vector3.one * .002f;
            Debug.Log($"CONTOUR4 EQUIPMENT LOOT CLEARANCE | {room.Plan.Module.Id} | " +
                $"supports={placement.Supports(bottom, pose.up)}, contains={placement.Contains(center, pose.rotation, half)} | " +
                $"spawn={Names(Physics.OverlapBox(center, extent, pose.rotation, ~0, QueryTriggerInteraction.Ignore))} | " +
                $"exit={Names(Physics.OverlapBox(extracted, extent, pose.rotation, ~0, QueryTriggerInteraction.Ignore))} | " +
                $"ray={Names(Physics.RaycastAll(eye, -towardEye, Vector3.Distance(eye, center), ~0, QueryTriggerInteraction.Ignore).Select(hit => hit.collider))} | " +
                $"pullOut={Names(Physics.BoxCastAll(center, extent, towardEye, pose.rotation, pull, ~0, QueryTriggerInteraction.Ignore).Select(hit => hit.collider))} | " +
                $"pullIn={Names(Physics.BoxCastAll(extracted, extent, -towardEye, pose.rotation, pull, ~0, QueryTriggerInteraction.Ignore).Select(hit => hit.collider))}");
            static string Names(IEnumerable<Collider> colliders) => string.Join(",", colliders.Select(collider => collider.name));
        }

        private static void VerifyIntactControl()
        {
            var root = new GameObject("Intact fallback control");
            var damage = new BrokenEquipment();
            try
            {
                damage.DressCabinet(root.transform, new List<Collider>());
                Require(!damage.TryDressInstrument(root.transform, new List<Collider>()) && root.transform.childCount == 0,
                    "a missing or substituted housing created an orphaned tray");
            }
            finally { damage.Release(); Object.DestroyImmediate(root); }
        }
#endif

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}

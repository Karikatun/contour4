using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using UnityEngine;
using UnityEngine.Rendering;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class RoomLighting
    {
        private const string CeilingFixtures = "Ceiling luminaires";
        private const string WallFixtures = "Wall luminaires";
        private static readonly Color Work = new(.90f, .94f, 1f);
        private static readonly Color Reserve = new(1f, .86f, .70f);
        private static readonly Color Guide = new(.76f, .94f, .85f);
        private readonly List<Material> materials;
        private readonly List<Mesh> meshes;
        private Mesh lens;
        private Mesh housing;
        private Material workLens;
        private Material reserveLens;
        private Material guideLens;
        private Material housingMaterial;

        internal RoomLighting(List<Material> materials, List<Mesh> meshes)
        {
            this.materials = materials;
            this.meshes = meshes;
        }

        internal static HashSet<string> OfflineFixtureParts(GrayModule module) =>
            new(module.Dressing.SelectMany(dressing => dressing.OfflineLampParts.Select(dressing.Name)),
                StringComparer.Ordinal);

        internal static void RemoveFixture(GameObject fixture)
        {
            fixture.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(fixture);
            else UnityEngine.Object.DestroyImmediate(fixture);
        }

        private static void RemoveRetiredLights(GrayModule module, Transform room)
        {
            if (module.Id == "sample-receiving")
            {
                var fill = room.Find("Lighting/Freight overhead fill");
                if (fill) RemoveFixture(fill.gameObject);
            }
            else if (SphericalChamber.IsVariant(module.Id))
            {
                var original = room.Find("Sphere lighting");
                if (original && !original.gameObject.activeSelf) RemoveFixture(original.gameObject);
            }
        }

        // Catalog fixtures supply task lighting. Ceiling fixtures are spaced to overlap across working routes.
        internal void Apply(GrayModule module, Transform room, bool prefab)
        {
            RemoveRetiredLights(module, room);
            if (module.Id == "shaft-plant") AddShaftTaskLights(room);
            if (room.Find(CeilingFixtures) || room.Find(WallFixtures)) return;

            const float ceiling = GrayBuilder.Height;
            switch (module.Id)
            {
                case "freight-dispatch":
                    Row(room, ceiling, .10f, 1.20f, 2.8f, 8.2f);
                    break;
                case "instrument-service":
                    Row(room, ceiling, .15f, 1.14f, 2.35f, 6.9f);
                    break;
                case "sample-receiving":
                    Row(room, ceiling, .15f, 1.38f, 2.35f, 6.9f);
                    break;
                case "calibration-sector":
                    Row(room, ceiling, -3.55f, 1.70f, 3.8f, 11.25f, 18.7f);
                    Row(room, ceiling, 3.55f, 1.48f, 3.8f, 11.25f, 18.7f);
                    break;
                case "gallery-chamber":
                    Observation(room, false);
                    AddCeiling(room, FloorFrame(module, "small-floor"), 3.4f, ceiling, 1.7f, .82f, 7f);
                    break;
                case "observation-lab":
                case "observation-lab-broken-floor":
                    Observation(room, module.Id == "observation-lab-broken-floor");
                    break;
                case "sample-storage":
                case "sample-storage-narrow":
                    Row(room, ceiling, 3.6f, .86f, 3.7f, 11.3f);
                    Row(room, ceiling, -3.45f, .56f, 3.7f, 11.3f);
                    break;
                case "shaft-plant":
                    // The plant's open shaft has an 11.2 m ceiling, independent of its stair landings.
                    AddCeiling(room, 5.4f, 11.2f, 2.65f, 1.2f, 15f);
                    AddCeiling(room, 11.25f, 11.2f, 2.65f, 1.2f, 15f);
                    AddCeiling(room, 17.1f, 11.2f, 2.65f, 1.2f, 15f);
                    AddCeiling(room, 20.25f, 11.2f, -4.7f, .65f, 7f);
                    break;
                case "maintenance-trench":
                    // Light the freight bypass and let the existing standby lamps mark both plank approaches.
                    Row(room, 5f, 1.25f, .77f, 3.4f, 9f, 14.6f);
                    break;
                case "service-cupboard":
                    AddCeiling(room, 3.8f, ceiling, .5f, .66f, 6.0f, reserve: true);
                    break;
                case "niche-chamber":
                    Row(room, ceiling, -.25f, .8f, 3.75f, 11.25f, 18.75f);
                    ModulePose cell = module.Floors.Find(floor => floor.Id == "cell-floor").Pose;
                    AddCeiling(room, Vector(cell.Point(3.75f, ceiling)),
                        Quaternion.LookRotation(Vector(cell.Right), Vector(cell.Up)), .63f, 5.8f);
                    break;
                case "straight-corridor-short":
                    Row(room, ceiling, 0f, .65f, 2.5f, 5f);
                    break;
                case "straight-corridor-long":
                    Row(room, ceiling, 0f, .68f, 2.5f, 7.5f, 12.5f);
                    break;
                case "elbow-corridor":
                    AddCeiling(room, 2.5f, ceiling, 0f, .69f, 6.4f);
                    AddCeiling(room, 6f, ceiling, 2.7f, .69f, 6.4f, alongZ: true);
                    AddCeiling(room, 6f, ceiling, 7.15f, .69f, 6.4f, alongZ: true);
                    break;
                case "junction-corridor":
                    Row(room, ceiling, 0f, .72f, 2.8f, 7.5f, 12.2f);
                    AddCeiling(room, 7.5f, ceiling, 7.3f, .64f, 6.8f, alongZ: true);
                    break;
                case RaidExtraction.OrdinaryId:
                    AddCeiling(room, 3.4f, 8f, 0f, 1.7f, 12f);
                    // The loading-bay roof ends before the native quota shaft.
                    AddWall(room, GrayBuilder.Level, 8.9f, 4.6f, RaidExtraction.Width / 2 - GrayBuilder.Skirt,
                        .94f, 8.6f);
                    AddWall(room, GrayBuilder.Level, 12.9f, 4.6f, -RaidExtraction.Width / 2 + GrayBuilder.Skirt,
                        .94f, 8.6f);
                    break;
                case RaidExtraction.StraightBridgeId:
                case RaidExtraction.BentBridgeId:
                    AddCeiling(room, 2.65f, 8f, 0f, 1.45f, 11f);
                    AddWall(room, GrayBuilder.Level, 7.15f, 3.85f, RaidExtractionBridge.NearWall + GrayBuilder.Skirt,
                        1.05f, 13.5f);
                    bool bent = module.Id == RaidExtraction.BentBridgeId;
                    AddWall(room, GrayBuilder.Level, bent ? RaidExtractionBridge.BentAxis : 14.8f, 4.55f,
                        (bent ? RaidExtractionBridge.BentFarWall : RaidExtractionBridge.StraightFarWall) - GrayBuilder.Skirt,
                        1.0f, 14f);
                    break;
                case "upper-exit-corridor":
                    Row(room, ceiling, 0f, .77f, 3.3f, 9.5f, 15.5f, 21f);
                    AddCeiling(room, 8.8f, ceiling, -6.5f, .76f, 7f);
                    CeilingAtDressing(room, module, "bridge-field-12", "bridge-field-22", "bridge-field-32");
                    AddCeiling(room, FloorFrame(module, "upper-landing"), 1.35f, ceiling, 0f, .75f, 6.6f);
                    break;
                case "quarter-turn-bridge":
                case "spiral-corridor":
                    foreach (RoomDressing station in module.Dressing)
                        AddCeiling(room, station.Frame, 0f, ceiling, 0f, .71f, 6.6f);
                    break;
                case "right-angle-chambers":
                    CornerRooms(room, module);
                    break;
                case "top-to-top-chambers":
                case "floor-to-floor-chambers":
                    foreach (string id in new[] { "a", "b" })
                    {
                        ModulePose floor = FloorFrame(module, id);
                        const float chamberCeiling = 4.25f;
                        AddCeiling(room, floor, 2.45f, chamberCeiling, 0f, .79f, 6.9f);
                        AddCeiling(room, floor, 5.9f, chamberCeiling, 2.4f, .60f, 6.1f);
                    }
                    break;
                case "u-shaft":
                    // The lower bay and both end rooms open into the shaft. Their practicals are on side walls.
                    AddWall(room, GrayBuilder.Level, -3f, 3.35f, 3.5f, .98f, 7.6f);
                    AddWall(room, GrayBuilder.Level, 3.65f, 3.35f, 3.5f, .98f, 7.6f);
                    foreach (string id in new[] { "west", "east" })
                    {
                        CeilingAtDressing(room, module, id + "-bend-03", id + "-bend-16", id + "-bend-29");
                        AddWall(room, FloorFrame(module, id), 4.85f, 3.35f, -3.5f, 1.05f, 8f);
                    }
                    break;
                case MobiusHall.Id when !prefab:
                    AddCeiling(room, -5f, MobiusHall.InteriorHeight, 4.5f, 1.65f, MobiusHall.InteriorHeight + 4f);
                    AddCeiling(room, 5f, MobiusHall.InteriorHeight, 4.5f, 1.65f, MobiusHall.InteriorHeight + 4f);
                    break;
                case MobiusHall.Id:
                    ConfigureRibbonPracticals(room);
                    AttachRampPractical(room, module);
                    break;
                case string id when !prefab && SphericalChamber.IsVariant(id):
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector3 outward = new Vector3(side * .65f, .47f, side * .58f).normalized;
                        AddWall(room, outward * SphericalChamber.Radius, outward, Vector3.up,
                            -outward, 1.6f, SphericalChamber.Radius + 3f);
                    }
                    break;
            }

            foreach (RoomDressing dressing in module.Dressing)
            {
                var lighting = room.Find(dressing.Id + "-lighting");
                if (!lighting && module.Id == "sample-receiving") lighting = room.Find("Lighting");
                foreach (RoomDressing.Lamp lamp in dressing.Lamps)
                {
                    if (lamp.Duty == LampDuty.Offline) continue;
                    string name = dressing.Name(lamp.Id);
                    var node = lighting ? lighting.Find(name) : null;
                    if (!node && !prefab)
                    {
                        if (!lighting) lighting = Child(room, dressing.Id + "-lighting");
                        node = Child(lighting, name);
                        node.localPosition = Vector(dressing.Point(lamp.Emitter));
                        node.localRotation = Quaternion.LookRotation(Vector(dressing.Direction(lamp.Direction)),
                            Vector(dressing.Frame.Forward));
                        node.gameObject.AddComponent<Light>();
                    }
                    if (!node || !node.TryGetComponent(out Light light)) continue;
                    SetAccent(light, lamp);
                    if (dressing.Parts.Count == 0 && lamp.Duty == LampDuty.Guidance)
                        AddGuideLens(lighting, dressing, lamp);
                }
            }
        }

        private void AddShaftTaskLights(Transform room)
        {
            const string bank = "Shaft task lights";
            if (room.Find(bank)) return;
            Transform lights = Child(room, bank);
            var bench = new Vector3(ShaftPlant.TableX + .14f, 9.80f, ShaftPlant.TableZ - .21f);
            var rack = new Vector3(ShaftPlant.RackX - .17f, ShaftPlant.MiddleY + 1.415f, ShaftPlant.RackZ + .15f);
            AddTaskLight("Gallery workbench light", bench, new Vector3(.39f, 1f, .58f),
                new Vector3(0, -1, .16f), .95f, 2.55f, 53f, 35f);
            AddTaskLight("Balcony tool rack light", rack, new Vector3(.35f, 1f, .42f),
                new Vector3(0, -1, -.18f), 1.85f, .78f, 100f, 84f);

            const float roof = 11.2f;
            var ceilingMount = Child(lights, "Workbench ceiling mount");
            ceilingMount.localPosition = new Vector3(bench.x, roof - .045f, bench.z);
            ceilingMount.localScale = new Vector3(.35f, 1f, .62f);
            AddHousing(ceilingMount);
            foreach (float offset in new[] { -.22f, .22f })
            {
                var hanger = Child(lights, offset < 0 ? "West workbench hanger" : "East workbench hanger");
                hanger.SetLocalPositionAndRotation(new Vector3(bench.x + offset, (roof + bench.y) / 2, bench.z),
                    Quaternion.Euler(0, 0, 90f));
                hanger.localScale = new Vector3((roof - bench.y + .02f) / 1.88f, .42f, .10f);
                AddHousing(hanger);
            }

            void AddTaskLight(string name, Vector3 at, Vector3 shadeScale, Vector3 aim,
                float intensity, float range, float angle, float inner)
            {
                var fixture = Child(lights, name);
                fixture.localPosition = at;
                var shade = Child(fixture, "Shade");
                shade.localScale = shadeScale;
                AddHousing(shade);
                AddLens(shade, reserve: true);

                var emitter = Child(fixture, "Task spotlight");
                emitter.SetLocalPositionAndRotation(new Vector3(0, -.024f, 0),
                    Quaternion.LookRotation(aim, Vector3.forward));
                SetPractical(emitter, intensity, range, reserve: true, LightShadows.None, angle, inner);
                emitter.GetComponent<Light>().color = new Color(1f, .93f, .82f);
            }
        }

        internal void Fold(FreightFold fold)
        {
            GrayModule module = fold.Link.Fold.Module;
            ModulePose[] route = module.Routes[0].Knots;
            if (module.Id != RaidFreightFold.LongSpan)
            {
                AddFreightRail(fold.Root, route[route.Length / 7], -1f);
                AddFreightRail(fold.Root, route[2 * route.Length / 5], 1f);
                AddFreightRail(fold.Root, route[3 * route.Length / 5], -1f);
                AddFreightRail(fold.Root, route[6 * route.Length / 7], 1f);
                return;
            }

            // Sample the actual roof strip, including any extended straight approach.
            var distance = new float[route.Length];
            for (int i = 1; i < route.Length; i++)
                distance[i] = distance[i - 1] + Vector3.Distance(Vector(route[i - 1].Position), Vector(route[i].Position));
            float length = distance[distance.Length - 1];
            int fixtures = Mathf.Max(2, Mathf.CeilToInt(length / 5.4f));
            int end = 1;
            for (int i = 0; i < fixtures; i++)
            {
                float station = length * (i + .5f) / fixtures;
                while (distance[end] < station) end++;
                float t = Mathf.InverseLerp(distance[end - 1], distance[end], station);
                ModulePose a = route[end - 1], b = route[end];
                Vector3 mount = Vector3.Lerp(Vector(a.Point(0f, GrayBuilder.Height)), Vector(b.Point(0f, GrayBuilder.Height)), t);
                Quaternion rotation = Quaternion.Slerp(Crosswise(a), Crosswise(b), t);
                AddCeiling(fold.Root, mount, rotation, .85f, 7.1f, reserve: true);
            }
        }

        private void AddFreightRail(Transform room, ModulePose frame, float side)
        {
            Vector3 up = Vector(frame.Up);
            Vector3 outward = side * Vector(frame.Right);
            Vector3 mount = Vector(frame.Point(0f, .58f, side * GrayBuilder.Wide / 2));
            // Aim the wall wash across to the center of the deck.
            AddWall(room, mount, outward, up, -outward - up * .26f, 1.02f, 6.2f, reserve: true);
        }

        private void CornerRooms(Transform room, GrayModule module)
        {
            ModulePose airlock = FloorFrame(module, "airlock"), hall = FloorFrame(module, "distribution-hall");
            const float ceiling = CornerSlice.Headroom;
            AddCeiling(room, airlock, -13.1f, ceiling, 0f, .78f, 7f, across: false);
            AddCeiling(room, airlock, -6.1f, ceiling, -3.05f, .62f, 5.9f, across: false);
            AddCeiling(room, airlock, -6.1f, ceiling, 3.05f, .62f, 5.9f, across: false);
            AddCeiling(room, hall, -13.1f, ceiling, 0f, .84f, 7.4f, across: false);
            AddCeiling(room, hall, -6.1f, ceiling, -4.15f, .83f, 7.6f, across: false);
            AddCeiling(room, hall, -6.1f, ceiling, 4.15f, .80f, 7.2f, across: false);
            foreach (int section in new[] { 8, 18, 28 })
                AddCeiling(room, CornerSlice.Frame(CornerSlice.ArcLength * section / CornerSlice.Segments),
                    0f, ceiling, 0f, .75f, 6.8f);
        }

        private void CeilingAtDressing(Transform room, GrayModule module, params string[] ids)
        {
            foreach (string id in ids)
                AddCeiling(room, module.Dressing.First(dressing => dressing.Id == id).Frame,
                    0f, GrayBuilder.Height, 0f, .73f, 7f);
        }

        private void AttachRampPractical(Transform room, GrayModule module)
        {
            var fixture = room.Find("Mobius dressing/Ceiling ramp wall practical");
            if (!fixture) fixture = room.Find("Ceiling ramp wall practical");
            if (!fixture || fixture.Find("Wall bracket")) return;
            var emitter = fixture.Find("Ceiling ramp practical emitter");
            var housing = fixture.Find("Finish 0");
            if (!emitter || !housing || !housing.TryGetComponent(out MeshRenderer renderer)) return;

            var wall = module.Geometry.First(block => block.Id == "near-wall");
            Vector3 center = emitter.localPosition;
            // Overlap the authored housing and the interior face of the wall.
            var bracket = Block.Box("Wall bracket", BlockRole.Fixture, GrayBuilder.Level,
                center.x - .21f, center.x + .21f, center.y - .11f, center.y + .11f,
                wall.Bounds.Max.Z - .02f, center.z - .20f);
            var mesh = new Mesh { name = bracket.Id, vertices = bracket.Vertices.Select(Vector).ToArray(), triangles = bracket.Faces };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            var mount = Child(fixture, bracket.Id);
            mount.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            mount.gameObject.AddComponent<MeshRenderer>().sharedMaterial = renderer.sharedMaterial;
        }

        private static void ConfigureRibbonPracticals(Transform room)
        {
            var fixtures = room.Find("Ribbon work lights");
            if (!fixtures) return;
            for (int index = 1; index <= 4; index++)
            {
                var fixture = fixtures.Find($"Fixture {index}");
                if (!fixture || !fixture.TryGetComponent(out Light light)) continue;
                bool bench = index <= 2;
                Vector3 aim = bench ? Vector3.down + Vector3.forward * .36f : Vector3.down;
                fixture.localRotation = Quaternion.LookRotation(aim, Vector3.forward);
                light.type = LightType.Spot;
                light.intensity = bench ? .91f : 2.15f;
                light.range = bench ? 5.8f : 15f;
                light.spotAngle = bench ? 104f : 114f;
                light.innerSpotAngle = 42f;
            }
        }

        private void Observation(Transform room, bool broken)
        {
            float freight = broken ? .94f : 2.00f;
            AddCeiling(room, 3.4f, 7.45f, 3.65f, freight, 10.8f);
            AddCeiling(room, 10.95f, 7.45f, 3.65f, freight, 10.8f);
            AddCeiling(room, 3.85f, 7.45f, -4.9f, broken ? .81f : 1.76f, 11.4f);
            // The archive and operator have separate fixtures. Align the upper lens beside the hatch.
            AddCeiling(room, 12.7f, 3.35f, -5.2f, .49f, 5.5f, reserve: true);
            AddCeiling(room, 13.8f, 7.45f, -5.2f, .94f, 6.3f, alongZ: true);
        }

        private static void SetAccent(Light light, RoomDressing.Lamp lamp)
        {
            light.type = LightType.Spot;
            light.renderMode = LightRenderMode.Auto;
            light.shadows = LightShadows.None;
            if (lamp.Optics == LampOptics.Aisle)
            {
                light.intensity = .24f;
                light.range = 3.2f;
                light.spotAngle = 86f;
                light.color = Guide;
            }
            else
            {
                bool work = lamp.Duty == LampDuty.Work;
                bool guide = lamp.Duty == LampDuty.Guidance;
                bool task = lamp.Optics == LampOptics.Task;
                light.intensity = work ? (task ? .54f : .44f) : guide ? .30f : .28f;
                light.range = work ? (task ? 6.0f : 5.5f) : guide ? 4.6f : 4.2f;
                light.spotAngle = task ? 92f : 108f;
                light.color = work ? Work : guide ? Guide : Reserve;
            }
            light.innerSpotAngle = light.spotAngle * .34f;
        }

        private void Row(Transform room, float ceiling, float z, float intensity, params float[] stations)
        {
            foreach (float x in stations)
                AddCeiling(room, x, ceiling, z, intensity, 7.4f);
        }

        private static ModulePose FloorFrame(GrayModule module, string id) => module.Floors.First(floor => floor.Id == id).Pose;

        private static Quaternion Crosswise(ModulePose frame) => Quaternion.LookRotation(Vector(frame.Forward), Vector(frame.Up));

        private void AddGuideLens(Transform lights, RoomDressing dressing, RoomDressing.Lamp lamp)
        {
            Vector3 outward = Mathf.Sign(lamp.Emitter.Z) * Vector(dressing.Frame.Right);
            Transform fixture = Child(lights, dressing.Name(lamp.Id) + " lens");
            fixture.SetLocalPositionAndRotation(Vector(dressing.Point(lamp.Emitter)) + outward * .05f,
                Quaternion.LookRotation(Vector(dressing.Frame.Up), outward));
            fixture.localScale = new Vector3(.26f, 1f, .54f);
            AddLens(fixture, reserve: false, guide: true);
        }

        private void AddWall(Transform room, ModulePose frame, float x, float y, float z, float intensity, float range,
            bool reserve = false)
        {
            Vector3 up = Vector(frame.Up);
            Vector3 outward = Mathf.Sign(z) * Vector(frame.Right);
            AddWall(room, Vector(frame.Point(x, y, z)), outward, up, -outward - up * .58f, intensity, range, reserve);
        }

        private void AddWall(Transform room, Vector3 mount, Vector3 outward, Vector3 up, Vector3 direction,
            float intensity, float range, bool reserve = false)
        {
            Quaternion rotation = Quaternion.LookRotation(up, outward);
            Transform fixture = AddFixture(room, WallFixtures, mount, rotation, reserve);
            Transform emitter = Child(fixture, "Wall wash");
            emitter.localPosition = Vector3.down * .025f;
            emitter.localRotation = Quaternion.Inverse(rotation) * Quaternion.LookRotation(direction, up);
            SetPractical(emitter, intensity, range, reserve, LightShadows.None, 106f, 42f);
        }

        private void AddCeiling(Transform room, ModulePose frame, float x, float y, float z, float intensity, float range,
            bool across = true) => AddCeiling(room, Vector(frame.Point(x, y, z)),
                across ? Crosswise(frame) : Quaternion.LookRotation(Vector(frame.Right), Vector(frame.Up)), intensity, range);

        private void AddCeiling(Transform room, float x, float y, float z, float intensity, float range,
            bool alongZ = false, bool reserve = false) =>
            AddCeiling(room, new Vector3(x, y, z), alongZ ? Quaternion.Euler(0, 90f, 0) : Quaternion.identity,
                intensity, range, reserve);

        private void AddCeiling(Transform room, Vector3 ceiling, Quaternion rotation, float intensity, float range,
            bool reserve = false)
        {
            Transform fixture = AddFixture(room, CeilingFixtures, ceiling, rotation, reserve);
            Transform emitter = Child(fixture, "Diffuse downlight");
            emitter.localPosition = Vector3.down * .025f;
            emitter.localRotation = Quaternion.Euler(90f, 0, 0);
            SetPractical(emitter, intensity, range, reserve, LightShadows.Soft, 124f, 44f);
        }

        private Transform AddFixture(Transform room, string bank, Vector3 mount, Quaternion rotation, bool reserve)
        {
            Transform group = room.Find(bank);
            if (!group) group = Child(room, bank);
            Transform fixture = Child(group, $"Fixture {group.childCount + 1}");
            fixture.SetLocalPositionAndRotation(mount + rotation * (Vector3.down * .045f), rotation);
            AddLens(fixture, reserve);
            AddHousing(fixture);
            return fixture;
        }

        private static void SetPractical(Transform emitter, float intensity, float range, bool reserve,
            LightShadows shadows, float spotAngle, float innerSpotAngle)
        {
            var light = emitter.gameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.shadows = shadows;
            light.shadowStrength = .75f;
            light.shadowResolution = LightShadowResolution.Low;
            light.renderMode = LightRenderMode.ForcePixel;
            light.color = reserve ? Reserve : Work;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = spotAngle;
            light.innerSpotAngle = innerSpotAngle;
        }

        private void AddHousing(Transform fixture)
        {
            if (!housing)
            {
                Shader shader = Shader.Find("Standard");
                if (!shader) return;
                // The backplate extends past the lens standoff into the mounting surface.
                var block = Block.Box("Luminaire housing", BlockRole.Fixture, GrayBuilder.Level,
                    -.94f, .94f, .004f, .069f, -.144f, .144f);
                housing = new Mesh { name = block.Id, vertices = block.Vertices.Select(Vector).ToArray(), triangles = block.Faces };
                housing.RecalculateNormals();
                housing.RecalculateBounds();
                meshes.Add(housing);
                housingMaterial = new Material(shader) { color = new Color(.145f, .16f, .17f) };
                housingMaterial.SetFloat("_Glossiness", .24f);
                materials.Add(housingMaterial);
            }

            var plate = Child(fixture, "Housing");
            plate.gameObject.AddComponent<MeshFilter>().sharedMesh = housing;
            var renderer = plate.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = housingMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private void AddLens(Transform fixture, bool reserve, bool guide = false)
        {
            if (!lens)
            {
                Shader shader = Shader.Find("Unlit/Color");
                if (!shader) return;
                lens = new Mesh { name = "Ceiling luminaire lens" };
                lens.vertices = new[]
                {
                    new Vector3(-.88f, 0, -.10f), new Vector3(.88f, 0, -.10f),
                    new Vector3(.88f, 0, .10f), new Vector3(-.88f, 0, .10f),
                };
                lens.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                lens.RecalculateBounds();
                meshes.Add(lens);
                workLens = new Material(shader) { color = new Color(.78f, .85f, .87f) };
                reserveLens = new Material(shader) { color = new Color(.72f, .56f, .38f) };
                guideLens = new Material(shader) { color = new Color(.53f, .76f, .66f) };
                materials.Add(workLens);
                materials.Add(reserveLens);
                materials.Add(guideLens);
            }
            fixture.gameObject.AddComponent<MeshFilter>().sharedMesh = lens;
            var renderer = fixture.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = guide ? guideLens : reserve ? reserveLens : workLens;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static Vector3 Vector(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);
    }
}

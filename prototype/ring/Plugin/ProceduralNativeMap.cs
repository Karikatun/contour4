using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Contour4.NativeRaid.Runtime;
using HarmonyLib;
using UnityEngine;

namespace SpaceJourney
{
    internal sealed class ProceduralRaidMap : MonoBehaviour
    {
        private static ProceduralRaidMap active;
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorProperty = Shader.PropertyToID("_EmissionColor");
        private static readonly FieldInfo MapFloorObject = AccessTools.Field(typeof(DirtFinderMapFloor), "MapObject");
        private readonly Dictionary<RaidMapPlan.FloorView, GameObject> roomGraphics = new();
        private readonly Dictionary<RaidMapPlan.Link, GameObject> corridorGraphics = new();
        private readonly List<(RaidMapPlan.FloorView Floor, Renderer Renderer, bool Enabled)> stockRoomGraphics = new();
        private RaidAssembly raid;
        private RaidMapPlan plan;
        private Transform raidRoot;
        private Matrix4x4 originalRootToWorld;
        private Bounds? stockBounds;
        private Map native;
        private Transform playerSource;
        private Transform markerSource;
        private bool ready;
        private bool setupFailed;

        // The map stays in its original coordinates while physical gravity can rebase the raid.
        private Matrix4x4 WorldToPlan => originalRootToWorld * raidRoot.worldToLocalMatrix;

        internal static void Attach(RaidAssembly raid)
        {
            var map = raid.gameObject.AddComponent<ProceduralRaidMap>();
            map.raid = raid;
            map.raidRoot = raid.transform.root;
            map.originalRootToWorld = map.raidRoot.localToWorldMatrix;
            Transform start = LevelGenerator.Instance.LevelParent.GetComponentInChildren<StartRoom>()?.transform;
            map.stockBounds = StartBounds(start);
            map.plan = RaidMapPlan.Create(raid.Plan, map.stockBounds);
            map.native = Map.Instance;
            map.playerSource = RaidAssembly.Child(map.transform, "Stock map player source");
            map.markerSource = RaidAssembly.Child(map.transform, "Stock map marker source");
            active = map;
        }

        internal static void AttachRing(RaidAssembly raid, RingRuntime ring, RaidPlan.End parentDock)
        {
            var map = raid.GetComponent<ProceduralRaidMap>();
            map.plan = RaidMapPlan.Create(raid.Plan, map.stockBounds, ring, parentDock, map.WorldToPlan);
        }

        private static Bounds? StartBounds(Transform start)
        {
            if (!start) return null;
            var module = start.GetComponentInParent<global::Module>();
            var floors = (module ? module.transform : start).GetComponentsInChildren<DirtFinderMapFloor>(true);
            if (floors.Length == 0) return null;
            var bounds = new Bounds(floors[0].transform.position, Vector3.zero);
            foreach (var floor in floors) bounds.Encapsulate(floor.transform.position);
            bounds.Expand(new Vector3(3f, 0f, 3f));
            return bounds;
        }

        private static ProceduralRaidMap For(Map map)
        {
            if (!map || !active || !active.isActiveAndEnabled || map != Map.Instance) return null;
            if (!active.native) active.native = map;
            return active.native == map ? active : null;
        }

        private void LateUpdate()
        {
            if (setupFailed) return;
            if (!native) native = Map.Instance;
            if (!native || !native.OverLayerParent || !native.LayerPrefab) return;
            if (!ready)
            {
                try
                {
                    BuildGraphics();
                    ready = true;
                }
                catch (Exception error)
                {
                    setupFailed = true;
                    ReleaseGraphics();
                    Debug.LogError("[Contour4 Map] Could not add generated geometry to the native map: " + error);
                    return;
                }
            }

            var player = PlayerController.instance;
            if (!player || !player.rb) return;
            UpdatePlayer(player.transform);
        }

        private bool UpdatePlayer(Transform player)
        {
            if (!Project(player, true, playerSource, out _)) return false;
            foreach (var room in roomGraphics)
                Reveal(room.Value, room.Key.Revealed);
            foreach (var corridor in corridorGraphics)
                Reveal(corridor.Value, corridor.Key.Visited || corridor.Key.A.Floor.Revealed && corridor.Key.B.Floor.Revealed);
            foreach (var graphic in stockRoomGraphics)
            {
                bool revealed = graphic.Enabled && graphic.Floor.Revealed;
                if (graphic.Renderer && graphic.Renderer.enabled != revealed)
                    graphic.Renderer.enabled = revealed;
            }
            return true;
        }

        private static void Reveal(GameObject group, bool revealed)
        {
            if (group.activeSelf != revealed) group.SetActive(revealed);
        }

        private bool Project(Transform source, bool visit, Transform proxy, out RaidMapPlan.FloorView floor)
        {
            var frame = WorldToPlan;
            Vector3 world = frame.MultiplyPoint3x4(source.position);
            Vector3 forward = frame.MultiplyVector(source.forward);
            if (!plan.Project(world, forward, visit, out floor, out var flat, out var heading)) return false;

            var direction = plan.NativeVector(heading);
            if (direction.sqrMagnitude < .0001f)
                direction = Vector3.ProjectOnPlane(proxy.forward, Vector3.up);
            if (direction.sqrMagnitude < .0001f) direction = Vector3.forward;
            Vector3 position = plan.NativePoint(flat);
            // A stock truck marker keeps its original floor position. The other stories are
            // separated by the native layer height so the normal under/over-layer shading applies.
            position.y = floor == plan.Stock ? world.y : ReferenceHeight(floor);
            proxy.SetPositionAndRotation(position, Quaternion.LookRotation(direction, Vector3.up));
            return true;
        }

        private float ReferenceHeight(RaidMapPlan.FloorView floor) =>
            plan.Stock.Room.position.y - native.GetLayerPosition(floor.Story).y / native.Scale;

        private Transform Group(string name, RaidMapPlan.FloorView floor)
        {
            float height = ReferenceHeight(floor);
            MapLayer layer = native.GetLayerParent(height);
            var group = RaidAssembly.Child(layer.transform, name);
            group.localPosition = new Vector3(0, height * native.Scale + native.GetLayerPosition(layer.layer).y, 0);
            group.gameObject.SetActive(false);
            return group;
        }

        private void BuildGraphics()
        {
            foreach (var floor in plan.Floors)
            {
                if (floor == plan.Stock) continue;
                Transform group = Group("Contour-4 " + floor.Floor.Id, floor);
                roomGraphics.Add(floor, group.gameObject);
                foreach (Rect tile in floor.Slabs.Length == 0 ? new[] { BoundsOf(floor.Local) } : floor.Slabs)
                    Tile(group, floor.Flat(tile.min), floor.Flat(tile.min + Vector2.up) - floor.Flat(tile.min),
                        tile.width, tile.height);
                var openings = plan.Doors.Where(door => door.End.Floor == floor).ToArray();
                for (int i = 0; i < floor.Local.Length; i++)
                    Wall(group, floor, floor.Local[i], floor.Local[(i + 1) % floor.Local.Length], openings);
                foreach (var door in openings.Where(door => door.Portal))
                    Door(group, floor, door);
            }

            foreach (var link in plan.Links)
            {
                Transform group = Group("Contour-4 " + (link.Stairs ? "stairs" : "passage"), link.A.Floor);
                corridorGraphics.Add(link, group.gameObject);
                Vector2[] path = plan.Corridor(link).ToArray();
                const float width = 1.45f;
                for (int i = 1; i < path.Length; i++)
                {
                    var from = path[i - 1];
                    var direction = path[i] - from;
                    if (direction.sqrMagnitude < .0001f) continue;
                    var side = new Vector2(direction.y, -direction.x).normalized;
                    Tile(group, from - side * (width / 2), direction, width, direction.magnitude);
                    // Cover the joint between the two map strips without crossing the room boundaries.
                    if (i < path.Length - 1)
                        Tile(group, path[i] - Vector2.one * (width / 2), Vector2.up, width, width);
                }
                if (link.Stairs && native.StairsShortObject && path.Length > 2)
                    Glyph(native.StairsShortObject, group, (path[0] + path[1]) / 2, path[1] - path[0],
                        new Vector3(.6f, 1f, .6f));
            }
        }

        private static Rect BoundsOf(IReadOnlyList<Vector2> polygon)
        {
            var min = polygon[0];
            var max = polygon[0];
            foreach (var point in polygon)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void Tile(Transform group, Vector2 corner, Vector2 along, float width, float length)
        {
            // The native 1×1 tile has its corner at the root and is .50 units wide.
            float toTile = native.Scale / .50f;
            Glyph(native.FloorObject1x1, group, corner, along, new Vector3(width * toTile, 1, length * toTile));
        }

        private void Wall(Transform group, RaidMapPlan.FloorView floor, Vector2 a, Vector2 b, RaidMapPlan.Door[] openings)
        {
            Vector2 direction = (b - a).normalized;
            float length = Vector2.Distance(a, b);
            // The landing for an internal stair can meet the edge without a door model.
            var cutouts = openings.Select(door => door.End)
                .Concat(plan.Links.Where(link => link.Stairs).Select(link => link.A.Floor == floor ? link.A : link.B)
                    .Where(end => end.Floor == floor))
                .Where(end => Mathf.Abs(Vector2.Dot(new Vector2(-direction.y, direction.x), end.Edge - a)) < .09f)
                .Select(end => (Center: Vector2.Dot(end.Edge - a, direction), Half: end.Width / 2))
                .Where(cut => cut.Center > -.08f && cut.Center <= length + .08f)
                .OrderBy(cut => cut.Center);
            float drawn = 0f;
            foreach (var cut in cutouts)
            {
                Segment(drawn, Mathf.Clamp(cut.Center - cut.Half, 0, length));
                drawn = Mathf.Max(drawn, Mathf.Clamp(cut.Center + cut.Half, 0, length));
            }
            Segment(drawn, length);

            void Segment(float low, float high)
            {
                if (high - low < .04f) return;
                // Native wall graphics span .50 units along local Z, with a .025 offset.
                float scale = (high - low) * native.Scale / .50f;
                Vector2 center = a + direction * ((low + high) / 2f - .05f * (high - low));
                Glyph(native.Wall1x1Object, group, floor.Flat(center), floor.Flat(center + direction) - floor.Flat(center),
                    new Vector3(1, 1, scale));
            }
        }

        private void Door(Transform group, RaidMapPlan.FloorView floor, RaidMapPlan.Door door)
        {
            Vector2 center = floor.Flat(door.End.Edge);
            Vector2 normal = floor.Flat(door.End.Edge + door.End.Outward) - center;
            var tangent = new Vector2(normal.y, -normal.x);
            float width = door.End.Width * native.Scale;
            var model = Glyph(native.Door1x1Object, group, center - tangent * width * .35f / native.Scale,
                normal, new Vector3(width / .50f, 1, 1));
            var color = new MaterialPropertyBlock();
            foreach (var mesh in model.GetComponentsInChildren<Renderer>(true))
            {
                mesh.GetPropertyBlock(color);
                color.SetColor(ColorProperty, door.Color);
                color.SetColor(EmissionColorProperty, door.Color);
                mesh.SetPropertyBlock(color);
                color.Clear();
            }
        }

        private GameObject Glyph(GameObject prefab, Transform group, Vector2 point, Vector2 along, Vector3 scale)
        {
            if (!prefab) throw new InvalidOperationException("A required native map glyph is missing.");
            var glyph = Instantiate(prefab, group);
            Vector3 position = plan.NativePoint(point) * native.Scale;
            glyph.transform.localPosition = new Vector3(position.x, 0, position.z);
            Vector3 forward = plan.NativeVector(along.normalized);
            glyph.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);
            glyph.transform.localScale = scale;
            return glyph;
        }

        private void ReleaseGraphics()
        {
            foreach (var room in roomGraphics.Values)
                Release(room);
            foreach (var corridor in corridorGraphics.Values)
                Release(corridor);
            foreach (var graphic in stockRoomGraphics)
                if (graphic.Renderer) graphic.Renderer.enabled = graphic.Enabled;
            roomGraphics.Clear();
            corridorGraphics.Clear();
            stockRoomGraphics.Clear();

            static void Release(GameObject group)
            {
                if (!group) return;
                group.SetActive(false);
                if (Application.isPlaying) Destroy(group); else DestroyImmediate(group);
            }
        }

        private void OnDisable()
        {
            foreach (var room in roomGraphics.Values)
                if (room) room.SetActive(false);
            foreach (var corridor in corridorGraphics.Values)
                if (corridor) corridor.SetActive(false);
            foreach (var graphic in stockRoomGraphics)
                if (graphic.Renderer) graphic.Renderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (active == this) active = null;
            ReleaseGraphics();
        }

        private static Transform MapPlayerSource(Transform original)
        {
            var map = For(Map.Instance);
            return map && original && map.ready && map.UpdatePlayer(original) ? map.playerSource : original;
        }

        private static Transform NativeRoomSource(Transform original)
        {
            var map = For(Map.Instance);
            if (!map || !original || !original.IsChildOf(map.raid.transform)) return original;
            return map.Project(original, false, map.markerSource, out _) ? map.markerSource : original;
        }

        private static Vector3 NativeRoomPosition(Transform original) => NativeRoomSource(original).position;
        private static Quaternion NativeRoomRotation(Transform original) => NativeRoomSource(original).rotation;

        private static Transform NativeMarkerSource(Transform original)
        {
            var map = For(Map.Instance);
            return map && original && map.Project(original, false, map.markerSource, out var floor) &&
                (floor != map.plan.Stock || map.WorldToPlan != Matrix4x4.identity) ? map.markerSource : original;
        }

        private static Vector3 NativeMarkerPosition(Transform original) => NativeMarkerSource(original).position;

        [HarmonyPatch]
        private static class NativePlayerPatch
        {
            private static MethodBase TargetMethod() => AccessTools.EnumeratorMoveNext(
                AccessTools.Method(typeof(DirtFinderMapPlayer), "Logic"));

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
            {
                var source = AccessTools.Field(typeof(DirtFinderMapPlayer), "PlayerTransform");
                var project = AccessTools.Method(typeof(ProceduralRaidMap), nameof(MapPlayerSource));
                foreach (var instruction in code)
                {
                    yield return instruction;
                    if (instruction.LoadsField(source)) yield return new CodeInstruction(OpCodes.Call, project);
                }
            }
        }

        [HarmonyPatch(typeof(Map), nameof(Map.CustomPositionSet))]
        private static class NativeMarkerPatch
        {
            private static void Prefix(Map __instance, ref Transform __1)
            {
                if (For(__instance)) __1 = NativeMarkerSource(__1);
            }
        }

        [HarmonyPatch]
        private static class NativeValuableLayerPatch
        {
            private static MethodBase TargetMethod() => AccessTools.EnumeratorMoveNext(
                AccessTools.Method(typeof(MapValuable), "Logic"));

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var position = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position));
                var project = AccessTools.Method(typeof(ProceduralRaidMap), nameof(NativeMarkerPosition));
                foreach (var instruction in instructions)
                {
                    if (instruction.Calls(position))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = project;
                    }
                    yield return instruction;
                }
            }
        }

        [HarmonyPatch(typeof(Map), nameof(Map.AddRoomVolume))]
        private static class NativeRoomMaskPatch
        {
            private static bool Prefix(Map __instance, GameObject __0)
            {
                var map = For(__instance);
                return !map || !__0 || !__0.transform.IsChildOf(map.raid.transform);
            }
        }

        [HarmonyPatch(typeof(Map), nameof(Map.AddFloor))]
        private static class NativeFloorPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) => MapRoomGeometry(code);

            private static void Postfix(Map __instance, DirtFinderMapFloor __0)
            {
                var map = For(__instance);
                if (!map || !__0 || !__0.transform.IsChildOf(map.raid.transform) ||
                    !map.Project(__0.transform, false, map.markerSource, out var floor) ||
                    MapFloorObject.GetValue(__0) is not MapObject glyph || !glyph) return;
                foreach (var renderer in glyph.GetComponentsInChildren<Renderer>(true))
                {
                    map.stockRoomGraphics.Add((floor, renderer, renderer.enabled));
                    renderer.enabled = renderer.enabled && floor.Revealed;
                }
            }
        }

        [HarmonyPatch(typeof(Map), nameof(Map.AddWall))]
        private static class NativeWallPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) => MapRoomGeometry(code);
        }

        private static IEnumerable<CodeInstruction> MapRoomGeometry(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var transform = AccessTools.PropertyGetter(typeof(Component), nameof(Component.transform));
            var position = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position));
            var rotation = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.rotation));
            var projectPosition = AccessTools.Method(typeof(ProceduralRaidMap), nameof(NativeRoomPosition));
            var projectRotation = AccessTools.Method(typeof(ProceduralRaidMap), nameof(NativeRoomRotation));
            for (int i = 2; i < code.Count; i++)
            {
                if (code[i - 2].opcode != OpCodes.Ldarg_1 || !code[i - 1].Calls(transform)) continue;
                if (code[i].Calls(position)) { code[i].opcode = OpCodes.Call; code[i].operand = projectPosition; }
                if (code[i].Calls(rotation)) { code[i].opcode = OpCodes.Call; code[i].operand = projectRotation; }
            }
            return code;
        }
    }
}


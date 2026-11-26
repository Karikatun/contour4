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
        private readonly Dictionary<RaidMapPlan.FloorView, GameObject> fogGraphics = new();
        private readonly List<Mesh> ownedMapMeshes = new();
        private readonly Dictionary<Renderer, bool> coveredValuables = new();
        private readonly Dictionary<RaidMapPlan.Link, GameObject> corridorGraphics = new();
        private readonly Dictionary<RaidMapPlan.Link, MutedPassage> linkFogGraphics = new();
        private readonly Dictionary<RaidMapPlan.Link, (Approach A, Approach B)> approachGraphics = new();
        private readonly Dictionary<RaidMapPlan.Door, GameObject> portalGraphics = new();
        private readonly Dictionary<RaidMapPlan.FloorView, BoxCollider[]> roomVolumes = new();
        private readonly List<(RaidMapPlan.FloorView Floor, Renderer Renderer, bool Enabled)> stockRoomGraphics = new();
        private RaidAssembly raid;
        private RaidMapPlan plan;
        private Transform raidRoot;
        private LevelGenerator generator;
        private GameObject levelParent;
        private Level level;
        private Matrix4x4 originalRootToWorld;
        private Bounds? stockBounds;
        private Map native;
        private Transform playerSource;
        private Transform markerSource;
        private bool ready;
        private bool setupFailed;
        private bool havePlayerPose;

        // The map stays in its original coordinates while physical gravity can rebase the raid.
        private Matrix4x4 WorldToPlan => originalRootToWorld * raidRoot.worldToLocalMatrix;

        internal static void Attach(RaidAssembly raid)
        {
            var map = raid.gameObject.AddComponent<ProceduralRaidMap>();
            map.raid = raid;
            map.raidRoot = raid.transform.root;
            map.originalRootToWorld = map.raidRoot.localToWorldMatrix;
            map.generator = LevelGenerator.Instance;
            map.levelParent = map.generator.LevelParent;
            map.level = map.generator.Level;
            Transform start = map.levelParent.GetComponentInChildren<StartRoom>()?.transform;
            map.stockBounds = StartBounds(start);
            map.plan = RaidMapPlan.Create(raid.Plan, map.stockBounds,
                preparedRooms: raid.Rooms.Where(room => room.Prepared).Select(room => room.Plan));
            map.BindRoomVolumes();
            map.BindHatchApproaches();
            map.native = Map.Instance;
            map.playerSource = RaidAssembly.Child(map.transform, "Stock map player source");
            map.markerSource = RaidAssembly.Child(map.transform, "Stock map marker source");
            if (active && active != map) active.ReleaseGraphics();
            active = map;
        }

        internal static void AttachRing(RaidAssembly raid, RingRuntime ring, RaidPlan.End parentDock)
        {
            var map = raid.GetComponent<ProceduralRaidMap>();
            map.plan = RaidMapPlan.Create(raid.Plan, map.stockBounds, ring, parentDock, map.WorldToPlan,
                raid.Rooms.Where(room => room.Prepared).Select(room => room.Plan));
            map.BindRoomVolumes(ring);
            map.BindHatchApproaches();
        }

        private void BindRoomVolumes(RingRuntime ring = null)
        {
            roomVolumes.Clear();
            foreach (var floor in plan.Floors)
            {
                if (floor == plan.Stock) continue;
                if (floor.Owner != null)
                {
                    var room = raid.Room(floor.Owner);
                    Transform volumes = room.Root.Find("Room volume");
                    if (!volumes) continue;
                    if (!room.Prepared)
                        roomVolumes.Add(floor, volumes.GetComponents<BoxCollider>());
                    else
                        roomVolumes.Add(floor, room.Floors.Where(surface => surface.FloorId == floor.Floor.Id)
                            .Select(surface => volumes.Find(surface.FloorId + " " + surface.IslandId))
                            .Where(node => node && node.GetComponent<RoomVolume>())
                            .SelectMany(node => node.GetComponents<BoxCollider>()).ToArray());
                }
                else if (ring)
                {
                    var room = ring.Rooms.Single(node => node.name == floor.Floor.Id);
                    var volume = RingRuntime.Marker(room, room.name + "__RoomVolume");
                    roomVolumes.Add(floor, volume.GetComponents<BoxCollider>());
                }
            }
        }

        private void BindHatchApproaches()
        {
            Matrix4x4 frame = WorldToPlan;
            foreach (var door in plan.Doors)
            {
                var end = door.End;
                if (!door.Portal || !end.Interior || end.Floor.Owner == null) continue;
                // The placed pad is already validated against the hatch's owning deck.
                Transform pad = raid.Room(end.Floor.Owner).Root.Find(end.DockId + " portal landing pad");
                if (pad)
                    door.HatchApproach = new Pose(frame.MultiplyPoint3x4(pad.position), frame.rotation * pad.rotation);
            }
        }

        private bool RoomOwns(RaidMapPlan.FloorView floor, Vector3 position)
        {
            if (!roomVolumes.TryGetValue(floor, out var volumes)) return false;
            foreach (var volume in volumes)
            {
                if (!volume || !volume.enabled || !volume.isTrigger || !volume.gameObject.activeInHierarchy) continue;
                Vector3 local = volume.transform.InverseTransformPoint(position) - volume.center;
                Vector3 half = volume.size / 2;
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z)
                    return true;
            }
            return false;
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

        private bool CurrentRaid => raid && generator && generator == LevelGenerator.Instance &&
            levelParent && generator.LevelParent == levelParent && generator.Level == level &&
            raid.transform.IsChildOf(levelParent.transform);

        private static ProceduralRaidMap For(Map map)
        {
            if (!map || !active || !active.isActiveAndEnabled || !active.CurrentRaid || map != Map.Instance) return null;
            if (!active.native) active.native = map;
            return active.native == map ? active : null;
        }

        private void LateUpdate()
        {
            if (!CurrentRaid || active != this || native && native != Map.Instance)
            {
                ReleaseGraphics();
                return;
            }
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
            if (player) UpdatePlayer(player.transform);
        }

        private bool UpdatePlayer(Transform player)
        {
            var controller = NativeMapPlayer.PlayingController(generator);
            if (!controller || player != controller.transform) return false;
            if (Project(player, true, playerSource, out _)) havePlayerPose = true;
            RefreshVisibility();
            return havePlayerPose;
        }

        private void RefreshVisibility()
        {
            bool open = native.Active;
            foreach (var room in roomGraphics)
                Reveal(room.Value, open && room.Key.Revealed);
            foreach (var fog in fogGraphics)
                Reveal(fog.Value, open && !fog.Key.Revealed);
            foreach (var portal in portalGraphics)
                Reveal(portal.Value, open && portal.Key.Revealed);
            foreach (var corridor in corridorGraphics)
            {
                var link = corridor.Key;
                bool full = link.Visited || link.A.Floor.Revealed && link.B.Floor.Revealed;
                Reveal(corridor.Value, open && full);
                var ends = approachGraphics[link];
                bool showA = open && !full && link.A.Floor.Revealed;
                bool showB = open && !full && link.B.Floor.Revealed;
                ends.A.Show(showA, link.ExploredFromA);
                ends.B.Show(showB, link.ExploredFromB);
                // The muted overlay sits above the native floor layers. Remove the part
                // covered by bright geometry only when that layer is visible to the player.
                linkFogGraphics[link].Show(open && !(full && InDisplayedLayer(corridor.Value)),
                    showA && InDisplayedLayer(ends.A.Root) ? ends.A.VisibleLength(link.ExploredFromA) : 0f,
                    showB && InDisplayedLayer(ends.B.Root) ? ends.B.VisibleLength(link.ExploredFromB) : 0f);
            }
            foreach (var graphic in stockRoomGraphics)
            {
                bool revealed = open && graphic.Enabled && graphic.Floor.Revealed;
                if (graphic.Renderer && graphic.Renderer.enabled != revealed)
                    graphic.Renderer.enabled = revealed;
            }
        }

        private bool InDisplayedLayer(GameObject group) =>
            Mathf.Abs(group.transform.parent.GetComponent<MapLayer>().layer - native.PlayerLayer) <= 1;

        private static void Reveal(GameObject group, bool revealed)
        {
            if (group && group.activeSelf != revealed) group.SetActive(revealed);
        }

        private bool Project(Transform source, bool visit, Transform proxy, out RaidMapPlan.FloorView floor)
        {
            var frame = WorldToPlan;
            Vector3 world = frame.MultiplyPoint3x4(source.position);
            Vector3 forward = frame.MultiplyVector(source.forward);
            Vector2 flat, heading;
            bool projected = visit
                ? plan.ProjectPlayer(world, forward, candidate => RoomOwns(candidate, source.position), out floor, out flat, out heading)
                : plan.Project(world, forward, false, out floor, out flat, out heading);
            if (!projected) return false;

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
                BuildFog(floor);
                Transform group = Group("Contour-4 " + floor.Floor.Id, floor);
                roomGraphics.Add(floor, group.gameObject);
                foreach (Rect tile in floor.Slabs.Length == 0 ? new[] { BoundsOf(floor.Local) } : floor.Slabs)
                    Tile(group, floor.Flat(tile.min), floor.Flat(tile.min + Vector2.up) - floor.Flat(tile.min),
                        tile.width, tile.height);
                foreach (var route in floor.SurfaceRoutes)
                for (int i = 1; i < route.Length; i++)
                {
                    Vector2 start = floor.Flat(route[i - 1]);
                    Vector2 along = floor.Flat(route[i]) - start;
                    const float width = 1f;
                    Vector2 side = new Vector2(along.y, -along.x).normalized;
                    Tile(group, start - side * (width / 2), along, width, along.magnitude);
                }
                var openings = plan.Doors.Where(door => door.End.Floor == floor).ToArray();
                for (int i = 0; i < floor.Local.Length; i++)
                    Wall(group, floor, floor.Local[i], floor.Local[(i + 1) % floor.Local.Length], openings);
                foreach (var door in openings.Where(door => door.Portal))
                    portalGraphics.Add(door, DrawPortalMouth(group, door));
            }

            foreach (var link in plan.Links)
            {
                Transform group = Group("Contour-4 " + (link.Stairs ? "stairs" : "passage"), link.A.Floor);
                corridorGraphics.Add(link, group.gameObject);
                Vector2[] path = plan.Corridor(link).ToArray();
                linkFogGraphics.Add(link, BuildMutedPassage(link, path));
                approachGraphics.Add(link, (BuildApproach(link.A.Floor, path),
                    BuildApproach(link.B.Floor, path.Reverse().ToArray())));
                const float width = RaidMapPlan.CorridorWidth;
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
                if (link.Stairs && native.StairsShortObject && path.Length >= 2)
                    Glyph(native.StairsShortObject, group, (path[0] + path[1]) / 2, path[1] - path[0],
                        new Vector3(.6f, 1f, .6f));
            }

        }

        private void BuildFog(RaidMapPlan.FloorView floor)
        {
            int[] triangles = floor.CoverTriangles(out Vector2 center);
            Transform group = RaidAssembly.Child(native.OverLayerParent, "Contour-4 unexplored " + floor.Floor.Id);
            group.gameObject.SetActive(false);
            fogGraphics.Add(floor, group.gameObject);
            Vector3 mapCenter = plan.NativePoint(center) * native.Scale;
            group.localPosition = new Vector3(mapCenter.x, 0, mapCenter.z);

            // Native cover and outline have top faces at these depths on OverLayerParent.
            // A native outline starts .025 map units outside each rectangular side.
            const float width = .025f;
            var points = floor.Outline.Select(p => plan.NativeVector(p - center) * native.Scale).ToArray();
            DrawFogMesh(native.RoomVolume, group, "Unexplored fill", points, triangles, .11f);
            var outline = new Vector3[points.Length * 2];
            var strip = new int[points.Length * 6];
            bool upward = Vector3.Cross(points[triangles[1]] - points[triangles[0]],
                points[triangles[2]] - points[triangles[0]]).y > 0;
            for (int i = 0; i < points.Length; i++)
            {
                int previous = (i + points.Length - 1) % points.Length;
                int next = (i + 1) % points.Length;
                Vector3 before = Vector3.Cross(Vector3.up, (points[i] - points[previous]).normalized);
                Vector3 after = Vector3.Cross(Vector3.up, (points[next] - points[i]).normalized);
                Vector3 corner = Vector3.ClampMagnitude((before + after) /
                    Mathf.Max(.25f, 1f + Vector3.Dot(before, after)), 2.5f);
                if (upward) corner = -corner;
                outline[i * 2] = points[i];
                outline[i * 2 + 1] = points[i] + corner * width;
                int index = i * 6;
                strip[index] = i * 2;
                strip[index + 1] = next * 2;
                strip[index + 2] = i * 2 + 1;
                strip[index + 3] = i * 2 + 1;
                strip[index + 4] = next * 2;
                strip[index + 5] = next * 2 + 1;
            }
            DrawFogMesh(native.RoomVolumeOutline, group, "Unexplored outline", outline, strip, .105f);
            var question = Instantiate(native.ModulePrefab, group);
            question.name = "Unexplored question";
            question.transform.localPosition = Vector3.zero;
        }

        private void DrawFogMesh(GameObject prefab, Transform group, string name, Vector3[] vertices,
            int[] triangles, float height)
        {
            CreateMapMesh(prefab, group, name, height, out var mesh);
            // The stock map camera views these planes from above; discard dependent plan winding.
            var indices = (int[])triangles.Clone();
            for (int i = 0; i < indices.Length; i += 3)
                if (Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]],
                    vertices[indices[i + 2]] - vertices[indices[i]]).y < 0)
                    (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
            mesh.vertices = vertices;
            mesh.triangles = indices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private MeshRenderer CreateMapMesh(GameObject prefab, Transform group, string name, float height, out Mesh mesh)
        {
            var template = prefab.GetComponentInChildren<MeshRenderer>(true);
            Transform node = RaidAssembly.Child(group, name);
            node.gameObject.layer = template.gameObject.layer;
            node.localPosition = Vector3.up * height;
            mesh = new Mesh { name = name };
            ownedMapMeshes.Add(mesh);
            node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = template.sharedMaterial;
            renderer.shadowCastingMode = template.shadowCastingMode;
            renderer.receiveShadows = template.receiveShadows;
            renderer.sortingLayerID = template.sortingLayerID;
            renderer.sortingOrder = template.sortingOrder;
            return renderer;
        }

        private MutedPassage BuildMutedPassage(RaidMapPlan.Link link, Vector2[] path)
        {
            var group = RaidAssembly.Child(native.OverLayerParent,
                link.Stairs ? "Contour-4 unexplored stairs" : "Contour-4 unexplored passage");
            group.gameObject.SetActive(false);
            var renderer = CreateMapMesh(native.FloorObject1x1, group, "Unexplored connector", .10f, out var mesh);
            var dim = new MaterialPropertyBlock();
            dim.SetColor(ColorProperty, Muted(renderer.sharedMaterial.GetColor(ColorProperty)));
            dim.SetColor(EmissionColorProperty, Muted(renderer.sharedMaterial.GetColor(EmissionColorProperty)));
            renderer.SetPropertyBlock(dim);
            mesh.MarkDynamic();
            return new MutedPassage(group.gameObject, mesh, plan, path, native.Scale);

            static Color Muted(Color color) => new Color(color.r * .62f, color.g * .62f, color.b * .62f, color.a);
        }

        private sealed class MutedPassage
        {
            internal readonly GameObject Root;
            private readonly Mesh mesh;
            private readonly (Vector3 Left, Vector3 Right, float Distance)[] stations;
            private readonly List<Vector3> vertices = new();
            private readonly List<int> triangles = new();
            private float begin = -1f, finish = -1f;

            internal MutedPassage(GameObject root, Mesh mesh, RaidMapPlan plan, Vector2[] path, float scale)
            {
                Root = root;
                this.mesh = mesh;
                var route = new List<Vector2>();
                foreach (Vector2 point in path)
                    if (route.Count == 0 || Vector2.Distance(route[route.Count - 1], point) >= .01f)
                        route.Add(point);
                stations = new (Vector3, Vector3, float)[route.Count];
                if (route.Count < 2) return;
                var points = route.Select(point =>
                {
                    Vector3 projected = plan.NativePoint(point) * scale;
                    return new Vector3(projected.x, 0, projected.z);
                }).ToArray();
                float distance = 0f;
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 before = points[i] - points[Mathf.Max(0, i - 1)];
                    Vector3 after = points[Mathf.Min(points.Length - 1, i + 1)] - points[i];
                    Vector3 a = Vector3.Cross(Vector3.up, (i == 0 ? after : before).normalized);
                    Vector3 b = Vector3.Cross(Vector3.up, (i == points.Length - 1 ? before : after).normalized);
                    // The 1 m stroke stays within the route's existing 1.45 m clearance at bends.
                    Vector3 side = Vector3.ClampMagnitude((a + b) / Mathf.Max(.25f, 1f + Vector3.Dot(a, b)), 1.4f) *
                        (.5f * scale);
                    if (i > 0) distance += Vector2.Distance(route[i - 1], route[i]);
                    stations[i] = (points[i] - side, points[i] + side, distance);
                }
            }

            internal void Show(bool visible, float trimA, float trimB)
            {
                float end = stations.Length < 2 ? 0 : stations[stations.Length - 1].Distance - trimB;
                bool show = visible && end - trimA > .001f;
                Reveal(Root, show);
                if (!show || Mathf.Abs(begin - trimA) < .001f && Mathf.Abs(finish - end) < .001f) return;
                begin = trimA;
                finish = end;
                vertices.Clear();
                triangles.Clear();
                for (int i = 1; i < stations.Length; i++)
                {
                    var from = stations[i - 1];
                    var to = stations[i];
                    if (to.Distance <= begin || from.Distance >= finish) continue;
                    float span = to.Distance - from.Distance;
                    if (vertices.Count == 0)
                        AddPair(Mathf.Clamp01((begin - from.Distance) / span));
                    int first = vertices.Count - 2;
                    AddPair(Mathf.Clamp01((finish - from.Distance) / span));
                    triangles.Add(first);
                    triangles.Add(first + 2);
                    triangles.Add(first + 1);
                    triangles.Add(first + 1);
                    triangles.Add(first + 2);
                    triangles.Add(first + 3);

                    void AddPair(float t)
                    {
                        vertices.Add(Vector3.Lerp(from.Left, to.Left, t));
                        vertices.Add(Vector3.Lerp(from.Right, to.Right, t));
                    }
                }
                mesh.Clear();
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
        }

        private sealed class Approach
        {
            internal readonly GameObject Root;
            internal readonly List<(GameObject Tile, GameObject Joint, float Start, float Length, Vector3 Scale)> Strips = new();
            internal float Length;

            internal Approach(GameObject root) => Root = root;

            internal float VisibleLength(float explored) =>
                Mathf.Min(Length * .9f, Mathf.Max(1.3f, explored * Length));

            internal void Show(bool visible, float explored)
            {
                Reveal(Root, visible);
                if (!visible) return;
                float distance = VisibleLength(explored);
                foreach (var strip in Strips)
                {
                    float portion = Mathf.Clamp01((distance - strip.Start) / strip.Length);
                    Reveal(strip.Tile, portion > 0f);
                    Reveal(strip.Joint, portion == 1f);
                    if (portion > 0f)
                        strip.Tile.transform.localScale = new Vector3(strip.Scale.x, strip.Scale.y, strip.Scale.z * portion);
                }
            }
        }

        private Approach BuildApproach(RaidMapPlan.FloorView floor, Vector2[] path)
        {
            Transform group = Group("Contour-4 approach", floor);
            var approach = new Approach(group.gameObject);
            const float width = RaidMapPlan.CorridorWidth;
            for (int i = 1; i < path.Length; i++)
            {
                Vector2 from = path[i - 1];
                Vector2 direction = path[i] - from;
                float length = direction.magnitude;
                if (length < .01f) continue;
                Vector2 side = new Vector2(direction.y, -direction.x) / length;
                var tile = Tile(group, from - side * (width / 2), direction, width, length);
                GameObject joint = i < path.Length - 1
                    ? Tile(group, path[i] - Vector2.one * (width / 2), Vector2.up, width, width) : null;
                approach.Strips.Add((tile, joint, approach.Length, length, tile.transform.localScale));
                approach.Length += length;
            }
            return approach;
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

        private GameObject Tile(Transform group, Vector2 corner, Vector2 along, float width, float length)
        {
            // The native 1×1 tile has its corner at the root and is .50 units wide.
            float toTile = native.Scale / .50f;
            return Glyph(native.FloorObject1x1, group, corner, along, new Vector3(width * toTile, 1, length * toTile));
        }

        private void Wall(Transform group, RaidMapPlan.FloorView floor, Vector2 a, Vector2 b, RaidMapPlan.Door[] openings)
        {
            Vector2 direction = (b - a).normalized;
            float length = Vector2.Distance(a, b);
            // The landing for an internal stair can meet the edge without a door model.
            var cutouts = openings.Where(door => !door.End.Interior).Select(door => door.End)
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
                WallStrip(group, floor.Flat(a + direction * low), floor.Flat(a + direction * high));
            }
        }

        private GameObject DrawPortalMouth(Transform group, RaidMapPlan.Door door)
        {
            var (from, to) = door.PortalStroke();
            // The native door model has two marks. Use a single native wall strip across the aperture.
            var glyph = WallStrip(group, from, to);
            glyph.name = "Portal opening";
            var color = new MaterialPropertyBlock();
            color.SetColor(ColorProperty, door.Color);
            color.SetColor(EmissionColorProperty, door.Color);
            foreach (var mesh in glyph.GetComponentsInChildren<Renderer>(true))
                mesh.SetPropertyBlock(color);
            glyph.SetActive(false);
            return glyph;
        }

        private GameObject WallStrip(Transform group, Vector2 from, Vector2 to)
        {
            Vector2 along = to - from;
            Vector2 normal = new Vector2(-along.y, along.x).normalized;
            // The native wall mesh runs from the prefab origin to .50 along local X.
            return Glyph(native.Wall1x1Object, group, from, normal,
                new Vector3(along.magnitude * native.Scale / .50f, 1f, 1f));
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
            foreach (var fog in fogGraphics.Values)
                Release(fog);
            foreach (var mesh in ownedMapMeshes)
                if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
            foreach (var graphic in coveredValuables)
                if (graphic.Key) graphic.Key.forceRenderingOff = graphic.Value;
            fogGraphics.Clear();
            ownedMapMeshes.Clear();
            coveredValuables.Clear();
            foreach (var corridor in corridorGraphics.Values)
                Release(corridor);
            foreach (var corridor in linkFogGraphics.Values)
                Release(corridor.Root);
            linkFogGraphics.Clear();
            foreach (var ends in approachGraphics.Values)
            {
                Release(ends.A.Root);
                Release(ends.B.Root);
            }
            foreach (var graphic in stockRoomGraphics)
                if (graphic.Renderer) graphic.Renderer.enabled = graphic.Enabled;
            roomGraphics.Clear();
            corridorGraphics.Clear();
            approachGraphics.Clear();
            portalGraphics.Clear();
            stockRoomGraphics.Clear();
            havePlayerPose = ready = false;

            static void Release(GameObject group)
            {
                if (!group) return;
                group.SetActive(false);
                if (Application.isPlaying) Destroy(group); else DestroyImmediate(group);
            }
        }

        private void OnDisable() => ReleaseGraphics();

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

        private Transform ProjectMarker(Transform graphic, Transform original)
        {
            RaidMapPlan.FloorView floor = null;
            bool projected = original && Project(original, false, markerSource, out floor);
            if (graphic && graphic.TryGetComponent<MapValuable>(out var valuable))
                HideCoveredValuable(valuable.spriteRenderer, projected && floor != plan.Stock && !floor.Revealed);
            return projected && (floor != plan.Stock || WorldToPlan != Matrix4x4.identity) ? markerSource : original;
        }

        private void HideCoveredValuable(Renderer renderer, bool hidden)
        {
            if (!renderer) return;
            if (hidden)
            {
                if (!coveredValuables.ContainsKey(renderer))
                    coveredValuables.Add(renderer, renderer.forceRenderingOff);
                renderer.forceRenderingOff = true;
            }
            else if (coveredValuables.TryGetValue(renderer, out bool wasHidden))
            {
                coveredValuables.Remove(renderer);
                renderer.forceRenderingOff = wasHidden;
            }
        }

        private static Vector3 NativeMarkerPosition(Transform original)
        {
            var map = For(Map.Instance);
            return (map ? map.ProjectMarker(null, original) : original).position;
        }

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

        [HarmonyPatch(typeof(Map), nameof(Map.ActiveSet))]
        private static class NativeActivePatch
        {
            private static void Postfix(Map __instance)
            {
                var map = For(__instance);
                if (map && map.ready) map.RefreshVisibility();
            }
        }

        [HarmonyPatch(typeof(Map), nameof(Map.CustomPositionSet))]
        private static class NativeMarkerPatch
        {
            private static void Prefix(Map __instance, Transform __0, ref Transform __1)
            {
                var map = For(__instance);
                if (map) __1 = map.ProjectMarker(__0, __1);
            }
        }

        [HarmonyPatch(typeof(MapValuable), "Start")]
        private static class NativeValuableStartPatch
        {
            private static void Postfix(MapValuable __instance)
            {
                var map = For(Map.Instance);
                if (map && __instance.target)
                    map.ProjectMarker(__instance.transform, __instance.target.transform);
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
                // Native fog masks use horizontal world boxes. Generated rooms own their
                // unfolded visibility, while stock volumes still run stock discovery.
                return !map || map.setupFailed || !__0 || !__0.transform.IsChildOf(map.raid.transform);
            }
        }

        [HarmonyPatch(typeof(Map), nameof(Map.AddFloor))]
        private static class NativeFloorPatch
        {
            private static bool Prefix(Map __instance, DirtFinderMapFloor __0)
            {
                var map = For(__instance);
                if (!map || map.setupFailed || !__0 || !__0.transform.IsChildOf(map.raid.transform)) return true;
                // ExtractionPoint.Show/Hide uses these native glyphs for the three quota states.
                return __0.Type is DirtFinderMapFloor.FloorType.Truck_Floor or DirtFinderMapFloor.FloorType.Truck_Wall or
                    DirtFinderMapFloor.FloorType.Used_Floor or DirtFinderMapFloor.FloorType.Used_Wall or
                    DirtFinderMapFloor.FloorType.Inactive_Floor or DirtFinderMapFloor.FloorType.Inactive_Wall;
            }

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
                    renderer.enabled = renderer.enabled && __instance.Active && floor.Revealed;
                }
            }
        }

        [HarmonyPatch(typeof(Map), nameof(Map.AddWall))]
        private static class NativeWallPatch
        {
            private static bool Prefix(Map __instance, DirtFinderMapWall __0)
            {
                var map = For(__instance);
                return !map || map.setupFailed || !__0 || !__0.transform.IsChildOf(map.raid.transform);
            }
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

    internal static class NativeMapPlayer
    {
        private static readonly AccessTools.FieldRef<PlayerAvatar, bool> Spawned =
            AccessTools.FieldRefAccess<PlayerAvatar, bool>("spawned");

        internal static PlayerController PlayingController(LevelGenerator generator)
        {
            // Generation can create a controller before stock moves it to the truck.
            // Discovery begins only once the local avatar has entered normal gameplay.
            if (!generator || !generator.Generated || !GameDirector.instance ||
                GameDirector.instance.currentState != GameDirector.gameState.Main)
                return null;

            var controller = PlayerController.instance;
            if (!controller || !controller.isActiveAndEnabled || !controller.rb) return null;
            var avatar = controller.playerAvatarScript;
            return avatar && avatar == SemiFunc.PlayerAvatarLocal() && Spawned(avatar) ? controller : null;
        }
    }
}

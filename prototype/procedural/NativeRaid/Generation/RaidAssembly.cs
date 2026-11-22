using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural.Catalog;
using Contour4.Procedural.Navigation;
using UnityEngine;
using Object = UnityEngine.Object;
using NavigationRuntime = Contour4.Procedural.Navigation.RaidNavigation;

namespace Contour4.NativeRaid.Runtime
{

    internal sealed class BakedRoom
    {
        internal RaidPlan.Room Plan { get; }
        internal Transform Root { get; }
        internal RaidFloor Floor { get; }
        internal IReadOnlyList<Collider> Colliders { get; }
        internal List<Bounds> FloorBounds { get; }

        internal BakedRoom(RaidPlan.Room plan, Transform root, RaidFloor floor,
            List<Collider> colliders, List<Bounds> floorBounds)
        {
            Plan = plan;
            Root = root;
            Floor = floor;
            Colliders = colliders.AsReadOnly();
            FloorBounds = floorBounds;
        }
    }

    // This provider is called by Module.ConnectionSetup and by the offline Unity checks.
    internal sealed class RaidAssembly : MonoBehaviour
    {
        internal const float PortalSetback = 1.22f;
        internal static readonly RaidEnemyProfile Character = new(.35f, 1.9f, 1.4f, 5f, 14f, 100f, 5f, true, false);
        private const float PassageSetback = .97f;
        private readonly Dictionary<RaidPlan.Room, BakedRoom> rooms = new();
        private readonly List<Material> materials = new();
        private readonly List<Mesh> meshes = new();
        private readonly List<FreightFold> folds = new();
        private RaidPlan plan;
        private RaidLayout layout;
        private NativeRaidPortal[] portals;
        private Action beforeClose;
        private int solidMask;
        internal RaidNavigationContext Context { get; private set; }
        internal IEnumerable<BakedRoom> Rooms => rooms.Values;
        internal IReadOnlyList<FreightFold> Folds => folds;
        internal RaidPlan Plan => plan;
        internal Transform BranchRoot { get; private set; }
        internal RaidPlan.End? BranchEntrance => plan?.Branch?.Entrance;
        internal bool ContainsRing(Vector3 point) => BranchRoot && plan.Branch is { } branch &&
            branch.Descriptor.Reservation.Contains(BranchRoot.InverseTransformPoint(point));
        internal BakedRoom Room(RaidPlan.Room room) => rooms[room];

        internal void Place(int seed, Pose entrance, int worldLayer, int obstacles, bool catalogSites = false,
            int completedLevels = 0, RaidPrefabBranch branch = null)
        {
            if (plan != null) throw new InvalidOperationException("The native raid module has already been placed.");
            solidMask = (1 << worldLayer) | obstacles;
            plan = RaidPlan.Generate(seed, entrance, completedLevels, branch, MaintenanceTrench.Create);
            var shader = Shader.Find("Standard");
            if (!shader) throw new InvalidOperationException("The gray raid requires Unity's Standard shader.");
            var floor = Material(shader, new Color(.32f, .36f, .40f));
            var wall = Material(shader, new Color(.52f, .56f, .57f));
            var obstaclesMaterial = Material(shader, new Color(.39f, .33f, .25f));
            var sightStrips = new RaidSightStrips(shader, materials, meshes);
            foreach (var room in plan.Rooms)
            {
                if (catalogSites) RaidCatalogSites.Dress(room.Module);
                RaidLootSites.Dress(room.Module);
                rooms.Add(room, PlaceRoom(room, worldLayer, floor, wall, obstaclesMaterial, sightStrips));
            }
            foreach (var link in plan.Freight)
            {
                if (link.Fold == null) continue;
                var root = Child(transform, $"Freight fold {link.To.Room.Index:D2}");
                root.SetPositionAndRotation(link.Fold.Frame.position, link.Fold.Frame.rotation);
                var geometry = link.Fold.Module.Geometry.Concat(RaidFreightFold.SeamDecks(link.Fold.Module));
                var pieces = geometry.Select(block => PlacePiece(root, block, worldLayer,
                    block.Role == BlockRole.Floor ? floor : wall)).ToArray();
                folds.Add(new FreightFold(link, root, pieces));
            }
            if (plan.Branch is { } selected)
            {
                BranchRoot = Child(transform, selected.Descriptor.Module.Id);
                BranchRoot.SetPositionAndRotation(selected.Placement.position, selected.Placement.rotation);
                selected.Descriptor.Mount(BranchRoot);
                var ringLink = plan.Freight.Last();
                var collar = ringLink.Fold == null ? selected.Entrance :
                    new RaidPlan.End(ringLink.Fold, ringLink.Fold.Module.Docks[1]);
                PlaceCollar(collar, selected.Descriptor.ThresholdSpacing, worldLayer, floor, wall);
            }
            if (catalogSites) RaidCatalogSites.Mark(this);

            var passages = new List<RaidWalkPassage>();
            foreach (var (from, to) in plan.Passages)
            {
                passages.Add(new RaidWalkPassage(Room(from.Room).Floor, Room(to.Room).Floor,
                    from.Pad(PassageSetback), to.Pad(PassageSetback)));
                passages.Add(new RaidWalkPassage(Room(to.Room).Floor, Room(from.Room).Floor,
                    to.Pad(PassageSetback), from.Pad(PassageSetback)));
            }

            var directions = new List<NativeRaidPortal>();
            var links = new List<RaidPortal>();
            foreach (var (a, b) in plan.Portals)
            {
                Transform aFrame = PlaceAperture(a), bFrame = PlaceAperture(b);
                Vector2 opening = new(a.Dock.Opening.X, a.Dock.Opening.Y);
                var aFloor = Room(a.Room).Floor;
                var bFloor = Room(b.Room).Floor;
                string link = $"research-link-{links.Count + 1}";
                bool allowsCart = a.Dock.MaxCargo == Cargo.Cart && b.Dock.MaxCargo == Cargo.Cart;
                directions.Add(new NativeRaidPortal(link + "-a", aFrame, bFrame, opening, aFloor, bFloor, allowsCart));
                directions.Add(new NativeRaidPortal(link + "-b", bFrame, aFrame, opening, bFloor, aFloor, allowsCart));
                var apertureA = new RaidAperture(aFloor, RaidPortalKind.Door, PoseOf(aFrame), opening, a.Pad(PortalSetback), aFrame);
                var apertureB = new RaidAperture(bFloor, RaidPortalKind.Door, PoseOf(bFrame), opening, b.Pad(PortalSetback), bFrame);
                const RaidPortalUse use = RaidPortalUse.Vision | RaidPortalUse.Walking;
                links.Add(new RaidPortal(apertureA, apertureB, use, use));
            }
            portals = directions.ToArray();
            layout = new RaidLayout(rooms.Values.Select(room => room.Floor), links, passages,
                solidMask, new Vector3(16000, 0, 16000), .86f, 2.86f);
        }

        private void PlaceCollar(RaidPlan.End dock, float depth, int worldLayer, Material floor, Material wall)
        {
            var collar = Child(BranchRoot, "Authored ring entrance collar");
            collar.SetPositionAndRotation(dock.Sill, Quaternion.LookRotation(dock.Outward, dock.Up));
            float width = dock.Dock.Opening.X;
            float height = dock.Dock.Opening.Y;
            var floorBox = Piece("Collar floor", floor, new Vector3(0, -.125f, depth * .5f),
                new Vector3(width, .25f, depth + .02f));
            floorBox.transform.SetParent(collar, false);
            foreach (float side in new[] { -1f, 1f })
            {
                var wallBox = Piece("Collar jamb", wall, new Vector3(side * (width / 2 + .125f), height / 2, depth * .5f),
                    new Vector3(.25f, height, depth));
                wallBox.transform.SetParent(collar, false);
            }

            GameObject Piece(string name, Material material, Vector3 position, Vector3 size)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = name;
                box.layer = worldLayer;
                box.transform.localPosition = position;
                box.transform.localScale = size;
                box.GetComponent<Renderer>().sharedMaterial = material;
                return box;
            }
        }

        internal void InstallNavigation(Action beforeClose = null)
        {
            if (Context != null) return;
            if (layout == null) throw new InvalidOperationException("Place the raid before installing navigation.");
            var navigation = gameObject.GetComponent<NavigationRuntime>() ?? gameObject.AddComponent<NavigationRuntime>();
            navigation.Install(layout);
            var context = new RaidNavigationContext(plan.Seed, navigation, rooms.Values.ToArray(), portals);
            try
            {
                RaidNavigation.Publish(context);
                Context = context;
                this.beforeClose = beforeClose;
            }
            catch
            {
                navigation.Close();
                throw;
            }
        }

        internal bool TryPassPlayer(NativeRaidPortal portal, Vector3 feet, out Vector3 landing, Vector3? start = null,
            float loadSetback = 0f)
        {
            landing = default;
            if (Context == null || !portal.Entrance || !portal.Exit) return false;
            var entrance = portal.Entrance;
            var exit = portal.Exit;
            Vector3 up = portal.ExitFloor.Up;
            float depth = Vector3.Dot(feet - entrance.position, entrance.forward);
            if (depth < -.001f) return false;
            Vector3 atDoor = feet - entrance.forward * depth;
            if (start is Vector3 earlier)
            {
                float previousDepth = Vector3.Dot(earlier - entrance.position, entrance.forward);
                if (previousDepth < 0 && depth > 0)
                    atDoor = Vector3.LerpUnclamped(earlier, feet, -previousDepth / (depth - previousDepth));
            }

            // Preserve lateral motion, height and the part of this physics step beyond the plane.
            Vector3 mapped = portal.MapPoint(feet) - exit.forward * (Mathf.Max(0, .002f - depth) + loadSetback);
            Vector3 threshold = portal.MapPoint(atDoor);
            // Keep the exit runway clear without using its end as the player spawn point.
            Vector3 clearance = mapped - exit.forward * Mathf.Max(0, 1.05f - depth - loadSetback);
            Vector3 sill = exit.position - exit.up * portal.HalfHeight;
            Vector3 floor = clearance - up * Vector3.Dot(clearance - sill, up);
            if (!HasPortalSupport(portal.ExitFloor, floor)) return false;

            var navigation = Context.Navigation;
            if (!navigation.BodyTravel(threshold, mapped, up, Character) ||
                !navigation.BodyTravel(mapped, clearance, up, Character))
                return false;
            landing = mapped;
            return true;
        }

        internal bool HasPortalSupport(RaidFloor floor, Vector3 sillPoint)
        {
            Vector3 up = floor.Up;
            return Physics.Raycast(sillPoint + up * .14f, -up, out var support, .3f,
                solidMask, QueryTriggerInteraction.Ignore) && floor.Support.Contains(support.collider) &&
                Vector3.Dot(support.normal, up) >= .8f;
        }

        private BakedRoom PlaceRoom(RaidPlan.Room room, int worldLayer, Material floor, Material wall, Material obstacles,
            RaidSightStrips sightStrips)
        {
            var root = Child(transform, room.Module.Id);
            root.SetPositionAndRotation(room.Frame.position, room.Frame.rotation);
            var colliders = new List<Collider>();
            var support = new List<Collider>();
            var floorBounds = new List<Bounds>();
            var localGeometry = new List<Bounds>();
            foreach (var block in room.Module.Geometry)
            {
                if (block.Role == BlockRole.Free || room.OpenPlugs.Contains(block.Id)) continue;
                var (minimum, maximum) = block.Bounds;
                Vector3 low = RaidPlan.Vector(minimum), high = RaidPlan.Vector(maximum);
                var bounds = new Bounds((low + high) / 2, high - low);
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = block.Id;
                box.layer = worldLayer;
                box.transform.SetParent(root, false);
                box.transform.localPosition = bounds.center;
                box.transform.localScale = bounds.size;
                box.GetComponent<Renderer>().sharedMaterial = block.Role switch
                {
                    BlockRole.Floor => floor,
                    BlockRole.Obstacle or BlockRole.Fixture => obstacles,
                    _ => wall,
                };
                sightStrips.Apply(room.Module, block, bounds, box);
                var collider = box.GetComponent<BoxCollider>();
                colliders.Add(collider);
                localGeometry.Add(bounds);
                if (block.Role != BlockRole.Floor) continue;
                support.Add(collider);
                floorBounds.Add(new Bounds(new Vector3(bounds.center.x, bounds.max.y + .93f, bounds.center.z),
                    new Vector3(bounds.size.x + .02f, 2.10f, bounds.size.z + .02f)));
            }
            Bounds volume = localGeometry[0];
            foreach (var block in localGeometry) volume.Encapsulate(block);
            Bounds bake = volume;
            bake.Expand(.20f);
            var raidFloor = new RaidFloor($"{room.Module.Id}-{room.Index}", "ground", room.Module.Id,
                room.Frame, bake, colliders, support, root);
            var lamp = Child(root, "Graybox work light");
            lamp.localPosition = new Vector3(volume.center.x, 3.45f, volume.center.z);
            var light = lamp.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = Mathf.Min(14f, Mathf.Max(volume.size.x, volume.size.z));
            light.intensity = 2f;
            light.color = new Color(.88f, .94f, 1f);
            return new BakedRoom(room, root, raidFloor, colliders, floorBounds);
        }

        private Transform PlaceAperture(RaidPlan.End end)
        {
            Transform frame = Child(Room(end.Room).Root, end.Dock.Id + " portal plane");
            frame.SetPositionAndRotation(end.Sill + end.Up * (end.Dock.Opening.Y / 2),
                Quaternion.LookRotation(end.Outward, end.Up));
            return frame;
        }

        private Material Material(Shader shader, Color color)
        {
            var material = new Material(shader) { color = color };
            materials.Add(material);
            return material;
        }

        private Collider PlacePiece(Transform parent, Block block, int layer, Material material)
        {
            var node = Child(parent, block.Id);
            node.gameObject.layer = layer;
            var mesh = new Mesh { name = block.Id };
            mesh.vertices = block.Vertices.Select(RaidPlan.Vector).ToArray();
            mesh.triangles = block.Faces;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            node.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            var collider = node.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            return collider;
        }

        internal bool TryUp(Vector3 point, out Vector3 up)
        {
            foreach (var fold in folds)
                if (fold.TryUp(point, out up)) return true;
            foreach (BakedRoom room in rooms.Values)
            {
                Vector3 local = room.Root.InverseTransformPoint(point);
                foreach (Bounds bounds in room.FloorBounds)
                {
                    if (local.x < bounds.min.x || local.x > bounds.max.x ||
                        local.z < bounds.min.z || local.z > bounds.max.z ||
                        local.y < bounds.min.y - .65f || local.y > bounds.max.y + 6f) continue;
                    up = room.Root.up;
                    return true;
                }
            }
            if (TryGetComponent(out RaidRoomInteractions mechanisms) && mechanisms.TryShaftUp(point, out up)) return true;
            up = default;
            return false;
        }

        private static Pose PoseOf(Transform frame) => new(frame.position, frame.rotation);

        internal static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        internal static void SyncVisionEye(Transform proxy, Transform liveEye) => proxy.position = liveEye.position;

        internal void ReleaseNavigation()
        {
            var context = Context;
            if (context == null) return;
            Context = null;
            var closing = beforeClose;
            beforeClose = null;
            try
            {
                if (ReferenceEquals(RaidNavigation.Current, context)) closing?.Invoke();
            }
            finally
            {
                RaidNavigation.Clear(context);
                if (context.Navigation) context.Navigation.Close();
            }
        }

        private void OnDisable() => ReleaseNavigation();

        private void OnDestroy()
        {
            ReleaseNavigation();
            foreach (var material in materials)
                if (Application.isPlaying) Object.Destroy(material); else Object.DestroyImmediate(material);
            foreach (var mesh in meshes)
                if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh);
        }
    }

    internal sealed class FreightFold
    {
        internal RaidPlan.FreightLink Link { get; }
        internal Transform Root { get; }
        internal IReadOnlyList<Collider> Colliders { get; }
        private readonly Vector3[] points;
        private readonly Vector3[] support;
        private readonly float halfWidth, height;

        internal FreightFold(RaidPlan.FreightLink link, Transform root, Collider[] colliders)
        {
            Link = link;
            Root = root;
            Colliders = colliders;
            var frames = link.Fold.Module.Routes[0].Knots;
            points = frames.Select(frame => RaidPlan.Vector(frame.Position)).ToArray();
            support = frames.Select(frame => RaidPlan.Vector(frame.Up)).ToArray();
            halfWidth = link.From.Dock.Opening.X / 2;
            height = link.From.Dock.Opening.Y;
        }

        internal bool TryUp(Vector3 position, out Vector3 up)
        {
            position = Root.InverseTransformPoint(position);
            float nearest = float.PositiveInfinity;
            Vector3 direction = Vector3.zero;
            for (int index = 1; index < points.Length; index++)
            {
                var delta = points[index] - points[index - 1];
                float fraction = Vector3.Dot(position - points[index - 1], delta) / delta.sqrMagnitude;
                float margin = .15f / delta.magnitude;
                if (fraction < -margin || fraction > 1f + margin) continue;
                float t = Mathf.Clamp01(fraction);
                Vector3 offset = position - Vector3.Lerp(points[index - 1], points[index], t);
                Vector3 normal = Vector3.Slerp(support[index - 1], support[index], t);
                float localHeight = Vector3.Dot(offset, normal);
                if (offset.sqrMagnitude >= nearest || Mathf.Abs(offset.z) > halfWidth + .10f ||
                    localHeight < -.55f || localHeight > height + .25f) continue;
                nearest = offset.sqrMagnitude;
                direction = normal;
            }
            up = Root.TransformDirection(direction);
            return direction.sqrMagnitude > .99f;
        }
    }
}

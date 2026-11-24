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
        internal bool Authored { get; }
        internal bool Prepared => PreparedSurfaces != null;
        internal PreparedRaidSurfaces PreparedSurfaces { get; }
        internal IReadOnlyDictionary<string, Collider> CollidersById { get; }
        internal IEnumerable<RaidFloor> Floors => PreparedSurfaces?.Floors ?? new[] { Floor };
        internal RaidFloor DockFloor(Dock dock) => PreparedSurfaces?.DockFloor(dock) ?? Floor;

        internal bool TryFloor(string id, out RaidFloor floor)
        {
            if (Prepared) return PreparedSurfaces.TryFloor(id, out floor);
            floor = Floor;
            return true;
        }

        internal BakedRoom(RaidPlan.Room plan, Transform root, RaidFloor floor,
            List<Collider> colliders, List<Bounds> floorBounds, bool authored,
            Dictionary<string, Collider> collidersById = null, PreparedRaidSurfaces prepared = null)
        {
            Plan = plan;
            Root = root;
            Floor = floor;
            Colliders = colliders.AsReadOnly();
            FloorBounds = floorBounds;
            Authored = authored;
            CollidersById = collidersById;
            PreparedSurfaces = prepared;
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
        private Material terminalSteel, terminalAmber;
        internal RaidNavigationContext Context { get; private set; }
        internal IEnumerable<BakedRoom> Rooms => rooms.Values;
        internal IReadOnlyList<FreightFold> Folds => folds;
        internal RaidPlan Plan => plan;
        internal RaidRoomPrefabs RoomPrefabs { get; private set; }
        internal Transform BranchRoot { get; private set; }
        internal RaidPlan.End? BranchEntrance => plan?.Branch?.Entrance;
        internal bool ContainsRing(Vector3 point) => BranchRoot && plan.Branch is { } branch &&
            branch.Descriptor.Reservation.Contains(BranchRoot.InverseTransformPoint(point));
        internal BakedRoom Room(RaidPlan.Room room) => rooms[room];

        internal void Place(int seed, Pose entrance, int worldLayer, int obstacles, bool catalogSites = false,
            int completedLevels = 0, RaidPrefabBranch branch = null, RaidRoomPrefabs roomPrefabs = null)
        {
            if (plan != null) throw new InvalidOperationException("The native raid module has already been placed.");
            bool CanPrepare(RaidPlan.Room room) => roomPrefabs != null && roomPrefabs.TryGetPrepared(room, out var prefab) && prefab;

            var candidate = RaidPlan.Generate(seed, entrance, completedLevels, branch, MaintenanceTrench.Create,
                canPrepare: CanPrepare);
            var portalPairs = CollectPortalPairs(candidate, CanPrepare);
            plan = candidate;
            solidMask = (1 << worldLayer) | obstacles;
            RoomPrefabs = roomPrefabs;
            var shader = Shader.Find("Standard");
            if (!shader) throw new InvalidOperationException("The gray raid requires Unity's Standard shader.");
            var floor = Material(shader, new Color(.32f, .36f, .40f));
            var wall = Material(shader, new Color(.52f, .56f, .57f));
            var obstaclesMaterial = Material(shader, new Color(.39f, .33f, .25f));
            var sightStrips = new RaidSightStrips(shader, materials, meshes);
            foreach (var room in plan.Rooms)
            {
                if (roomPrefabs != null && roomPrefabs.TryGetPrepared(room, out var prepared))
                {
                    rooms.Add(room, PlacePrepared(room, worldLayer, prepared, catalogSites ? obstaclesMaterial : null));
                    continue;
                }
                if (catalogSites) RaidCatalogSites.Dress(room.Module);
                RaidLootSites.Dress(room.Module);
                rooms.Add(room, PlaceRoom(room, worldLayer, floor, wall, obstaclesMaterial, sightStrips, roomPrefabs?.For(room)));
            }
            foreach (var link in plan.Freight)
            {
                if (link.Fold == null) continue;
                var root = Child(transform, $"Freight fold {link.To.Room.Index:D2}");
                root.SetPositionAndRotation(link.Fold.Frame.position, link.Fold.Frame.rotation);
                var geometry = link.Fold.Module.Geometry.Concat(RaidFreightFold.SeamDecks(link.Fold.Module)).ToArray();
                foreach (var block in geometry)
                    PlaceVisualPiece(root, block, worldLayer, block.Role == BlockRole.Floor ? floor : wall);

                // A rebase moves every fold at once. Keep the exact collision triangles but
                // submit one static shape per role, including a separate invisible barrier.
                var deck = PlaceFoldCollider(root, "Freight deck", worldLayer, geometry.Where(block => block.Role == BlockRole.Floor));
                var shell = PlaceFoldCollider(root, "Freight walls", worldLayer, geometry.Where(block => block.Role != BlockRole.Floor));
                PlaceFoldCollider(root, "Invisible freight safety", worldLayer, RaidFreightFold.SafetyBarriers(link.Fold.Module));
                var fold = new FreightFold(link, root, new[] { deck, shell });
                folds.Add(fold);
                RaidLighting.Fold(fold);
            }
            if (plan.Branch is { } selected)
            {
                BranchRoot = Child(transform, selected.Descriptor.Module.Id);
                BranchRoot.SetPositionAndRotation(selected.Placement.position, selected.Placement.rotation);
                selected.Descriptor.Mount(BranchRoot);
                var ringLink = plan.RingLink;
                var collar = ringLink.Fold == null ? selected.Entrance :
                    new RaidPlan.End(ringLink.Fold, ringLink.Fold.Module.Docks[1]);
                PlaceCollar(collar, selected.Descriptor.ThresholdSpacing, worldLayer, floor, wall);
            }
            if (catalogSites) RaidCatalogSites.Mark(this);
            Physics.SyncTransforms();

            var passages = new List<RaidWalkPassage>();
            foreach (var (from, to) in plan.Passages)
            {
                // Narrow hand docks need extra depth before the eroded local navmesh starts.
                bool hand = from.Dock.MaxCargo == Cargo.Hand || to.Dock.MaxCargo == Cargo.Hand;
                float setback = hand ? 1.40f : PassageSetback;
                float maximumLength = hand ? 3f : 2f;
                passages.Add(new RaidWalkPassage(Room(from.Room).DockFloor(from.Dock), Room(to.Room).DockFloor(to.Dock),
                    from.Pad(setback), to.Pad(setback), maximumLength));
                passages.Add(new RaidWalkPassage(Room(to.Room).DockFloor(to.Dock), Room(from.Room).DockFloor(from.Dock),
                    to.Pad(setback), from.Pad(setback), maximumLength));
            }

            var directions = new List<NativeRaidPortal>(portalPairs.Count * 2);
            var links = new List<RaidPortal>(portalPairs.Count);
            foreach (var (a, b) in portalPairs)
            {
                string link = a.Room == b.Room ? $"{a.Room.Module.Id}-{a.Room.Index}-{a.Dock.Id}-{b.Dock.Id}" :
                    $"research-link-{links.Count + 1}";
                var (forward, reverse, navigation) = PlacePortalPair(link, a, Room(a.Room), b, Room(b.Room), solidMask);
                directions.Add(forward);
                directions.Add(reverse);
                links.Add(navigation);
            }
            portals = directions.ToArray();
            layout = new RaidLayout(rooms.Values.SelectMany(room => room.Floors), links, passages,
                solidMask, new Vector3(16000, 0, 16000), .86f, 2.86f,
                rooms.Values.Where(room => room.Prepared).SelectMany(room => room.PreparedSurfaces.DeckPassages(solidMask)));
        }

        private static List<(RaidPlan.End A, RaidPlan.End B)> CollectPortalPairs(RaidPlan plan,
            Func<RaidPlan.Room, bool> canPrepare)
        {
            var pairs = plan.Portals.ToList();
            foreach (var room in plan.Rooms)
            {
                if (room.Module.InternalPortals.Count == 0) continue;
                if (!canPrepare(room))
                    throw new InvalidOperationException($"{room.Module.Id} requires its prepared prefab for the internal portals.");

                foreach (var (a, b) in room.Module.InternalPortals)
                    pairs.Add((new RaidPlan.End(room, room.Module.Docks.Single(dock => dock.Id == a)),
                        new RaidPlan.End(room, room.Module.Docks.Single(dock => dock.Id == b))));
            }

            int count = pairs.Count + (plan.Branch?.Descriptor.PortalPairCount ?? 0);
            if (count > RaidPlan.MaximumPortalPairs)
                throw new InvalidOperationException($"Raid seed {plan.Seed} has {count} portal pairs (maximum {RaidPlan.MaximumPortalPairs}).");
            return pairs;
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
            var context = new RaidNavigationContext(plan.Seed, navigation, rooms.Values.ToArray(), portals, solidMask);
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
            => TryPassPlayer(Context?.Navigation, solidMask, portal, feet, out landing, start, loadSetback);

        internal static bool TryPassPlayer(NavigationRuntime navigation, int mask, NativeRaidPortal portal, Vector3 feet,
            out Vector3 landing, Vector3? start = null, float loadSetback = 0f)
        {
            landing = default;
            if (!navigation || !navigation.Ready || !portal.Entrance || !portal.Exit) return false;
            if (!portal.Door)
            {
                if (!start.HasValue || !portal.Crossed(start.Value, feet, Character.Radius, Character.Height) ||
                    !HasPortalSupport(portal.ExitFloor, portal.ExitPad, mask) ||
                    !navigation.BodyClear(portal.ExitPad, portal.ExitFloor.Up, Character)) return false;
                landing = portal.ExitPad;
                return true;
            }
            var entrance = portal.Entrance;
            var exit = portal.Exit;
            Vector3 up = portal.ExitFloor.Up;
            float depth = Vector3.Dot(feet - entrance.position, entrance.forward);
            if (portal.DoorInset > 0)
            {
                if (depth < -portal.DoorInset - .001f) return false;
                Vector3 thresholdPoint = portal.MapPoint(feet - entrance.forward * depth) - exit.forward * portal.DoorInset;
                Vector3 arrival = thresholdPoint - exit.forward * (Mathf.Max(0, depth + portal.DoorInset) + .002f + loadSetback);
                Vector3 runway = arrival - exit.forward * Mathf.Max(0, 1.05f - Vector3.Dot(exit.position - arrival, exit.forward));
                Vector3 sillPoint = exit.position - exit.up * portal.HalfHeight;
                Vector3 support = runway - up * Vector3.Dot(runway - sillPoint, up);
                if (!HasPortalSupport(portal.ExitFloor, support, mask) ||
                    !navigation.BodyTravel(thresholdPoint, arrival, up, Character) ||
                    !navigation.BodyTravel(arrival, runway, up, Character)) return false;
                landing = arrival;
                return true;
            }
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
            if (!HasPortalSupport(portal.ExitFloor, floor, mask)) return false;

            if (!navigation.BodyTravel(threshold, mapped, up, Character) ||
                !navigation.BodyTravel(mapped, clearance, up, Character))
                return false;
            landing = mapped;
            return true;
        }

        internal bool HasPortalSupport(RaidFloor floor, Vector3 sillPoint)
            => HasPortalSupport(floor, sillPoint, solidMask);

        internal static bool HasPortalSupport(RaidFloor floor, Vector3 sillPoint, int mask)
        {
            Vector3 up = floor.Up;
            return Physics.Raycast(sillPoint + up * .14f, -up, out var support, .3f,
                mask, QueryTriggerInteraction.Ignore) && floor.Support.Contains(support.collider) &&
                Vector3.Dot(support.normal, up) >= .8f;
        }

        private BakedRoom PlaceRoom(RaidPlan.Room room, int worldLayer, Material floor, Material wall, Material obstacles,
            RaidSightStrips sightStrips, GameObject prefab)
        {
            var root = prefab ? Object.Instantiate(prefab, transform, false).transform : Child(transform, room.Module.Id);
            root.name = room.Module.Id;
            root.SetPositionAndRotation(room.Frame.position, room.Frame.rotation);
            var colliders = new List<Collider>();
            var support = new List<Collider>();
            var floorBounds = new List<Bounds>();
            var localGeometry = new List<Bounds>();
            var authored = prefab ? AuthoredBlocks(root, room.Module) : null;
            foreach (var block in room.Module.Geometry)
            {
                if (block.Role == BlockRole.Free) continue;
                if (room.OpenPlugs.Contains(block.Id))
                {
                    if (authored != null) authored[block.Id].gameObject.SetActive(false);
                    continue;
                }
                var (minimum, maximum) = block.Bounds;
                Vector3 low = RaidPlan.Vector(minimum), high = RaidPlan.Vector(maximum);
                var bounds = new Bounds((low + high) / 2, high - low);
                BoxCollider collider;
                if (authored != null) collider = authored[block.Id];
                else
                {
                    var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    box.name = block.Id;
                    box.transform.SetParent(root, false);
                    box.transform.localPosition = bounds.center;
                    box.transform.localScale = bounds.size;
                    box.GetComponent<Renderer>().sharedMaterial = block.Role switch
                    {
                        BlockRole.Floor => floor,
                        BlockRole.Obstacle or BlockRole.Fixture => obstacles,
                        _ => wall,
                    };
                    collider = box.GetComponent<BoxCollider>();
                }
                collider.gameObject.layer = worldLayer;
                sightStrips.Apply(room.Module, block, bounds, collider.gameObject);
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
            if (prefab) RaidLighting.Brighten(root);
            else RaidLighting.GrayRoom(root, volume);
            DressCappedTransit(room, root, worldLayer);
            return new BakedRoom(room, root, raidFloor, colliders, floorBounds, prefab);
        }

        internal BakedRoom PlacePrepared(RaidPlan.Room room, int layer, GameObject prefab, Material catalogMaterial = null)
        {
            var root = Object.Instantiate(prefab, transform, false).transform;
            try
            {
                root.name = room.Module.Id;
                root.SetPositionAndRotation(room.Frame.position, room.Frame.rotation);
                if (!root.gameObject.activeSelf || (root.localScale - Vector3.one).sqrMagnitude > .000001f)
                    throw new InvalidOperationException($"{room.Module.Id}: invalid prepared prefab root.");
                var parts = root.GetComponentsInChildren<Collider>(true).ToDictionary(collider => collider.name, StringComparer.Ordinal);
                var known = room.Module.Geometry.Where(block => block.Role != BlockRole.Free).ToArray();
                if (parts.Count != known.Length)
                    throw new InvalidOperationException($"{room.Module.Id}: prefab has {parts.Count} colliders for {known.Length} catalog blocks.");
                var colliders = new List<Collider>(parts.Count);
                foreach (var (a, b) in room.Module.InternalPortals)
                {
                    room.OpenPlugs.Add(room.Module.Docks.Single(dock => dock.Id == a).PlugId);
                    room.OpenPlugs.Add(room.Module.Docks.Single(dock => dock.Id == b).PlugId);
                }
                foreach (var block in known)
                {
                    if (!parts.TryGetValue(block.Id, out Collider part) || part is not MeshCollider mesh || !mesh.sharedMesh ||
                        !mesh.sharedMesh.isReadable || !part.enabled || part.isTrigger || !part.gameObject.activeInHierarchy)
                        throw new InvalidOperationException($"{room.Module.Id}/{block.Id}: missing prepared collision mesh.");
                    if (room.OpenPlugs.Contains(block.Id))
                    {
                        part.gameObject.SetActive(false);
                        continue;
                    }
                    part.gameObject.layer = layer;
                    colliders.Add(part);
                }
                if (catalogMaterial)
                {
                    int preparedBlocks = room.Module.Geometry.Count;
                    RaidCatalogSites.Dress(room.Module);
                    foreach (var block in room.Module.Geometry.Skip(preparedBlocks))
                    {
                        var collider = PlacePiece(root, block, layer, catalogMaterial);
                        parts.Add(block.Id, collider);
                        colliders.Add(collider);
                    }
                }
                var prepared = new PreparedRaidSurfaces(room, root, parts, colliders.ToArray());
                var baked = new BakedRoom(room, root, prepared.Floors.First(), colliders, new List<Bounds>(), true, parts, prepared);
                RaidLighting.Brighten(root);
                RaidLighting.DimPassage(baked);
                DressCappedTransit(room, root, layer);
                return baked;
            }
            catch
            {
                if (Application.isPlaying) Object.Destroy(root.gameObject); else Object.DestroyImmediate(root.gameObject);
                throw;
            }
        }

        private void DressCappedTransit(RaidPlan.Room room, Transform root, int layer)
        {
            if (room.Module.Id != "straight-corridor-short") return;
            Dock end = room.Dock("east");
            if (room.OpenPlugs.Contains(end.PlugId)) return;

            var frame = end.Threshold;
            Block bulkhead = room.Module.Geometry.Single(block => block.Id == end.PlugId);
            float face = bulkhead.Vertices.Min(vertex =>
                System.Numerics.Vector3.Dot(vertex - frame.Position, frame.Forward));

            // Put the sealed power-distribution terminal on the existing bulkhead.
            // The visual projection is 38 mm. The original wall remains its collision surface.
            Shader shader = Shader.Find("Standard");
            if (!terminalSteel) terminalSteel = Material(shader, new Color(.24f, .30f, .32f));
            if (!terminalAmber) terminalAmber = Material(shader, new Color(.67f, .46f, .20f));
            Plate("Sealed transfer switchboard", 1.32f, 2.13f, -.78f, .78f, .035f, terminalSteel);
            Plate("Transfer switchboard rail", 2.03f, 2.09f, -.62f, .62f, .038f, terminalAmber);
            for (int i = -1; i <= 1; i++)
            {
                float center = i * .42f;
                Plate("Transfer circuit label", 1.68f, 1.75f, center - .13f, center + .13f, .038f, terminalAmber);
            }

            void Plate(string name, float bottom, float top, float left, float right, float thickness, Material material) =>
                PlaceVisualPiece(root, Block.Box(name, BlockRole.Fixture, frame,
                    face - thickness, face, bottom, top, left, right), layer, material);
        }

        private static Dictionary<string, BoxCollider> AuthoredBlocks(Transform root, GrayModule module)
        {
            const float tolerance = .001f;
            if (!root.gameObject.activeSelf || !Near(root.localScale, Vector3.one))
                throw new InvalidOperationException($"{module.Id}: authored room root must be active at unit scale.");

            var solids = module.Geometry.Where(block => block.Role != BlockRole.Free).ToArray();
            var colliders = root.GetComponentsInChildren<Collider>(true);
            if (colliders.Length != solids.Length)
                throw new InvalidOperationException($"{module.Id}: prefab has {colliders.Length} colliders for {solids.Length} catalog blocks.");

            var byId = new Dictionary<string, BoxCollider>();
            foreach (var block in solids)
            {
                var part = root.Find(block.Id);
                if (!part || !part.gameObject.activeSelf || !part.TryGetComponent(out BoxCollider collider) ||
                    !collider.enabled || collider.isTrigger || !part.TryGetComponent(out MeshRenderer renderer) ||
                    !renderer.sharedMaterial || !part.TryGetComponent(out MeshFilter mesh) || !mesh.sharedMesh)
                    throw new InvalidOperationException($"{module.Id}/{block.Id}: missing active authored collider or render mesh.");

                var (low, high) = block.Bounds;
                if (!Near(part.localPosition, RaidPlan.Vector((low + high) / 2)) ||
                    !Near(part.localScale, RaidPlan.Vector(high - low)) ||
                    Quaternion.Angle(part.localRotation, Quaternion.identity) > .01f ||
                    !Near(collider.center, Vector3.zero) || !Near(collider.size, Vector3.one))
                    throw new InvalidOperationException($"{module.Id}/{block.Id}: authored collider differs from catalog bounds.");
                byId.Add(block.Id, collider);
            }
            return byId;

            static bool Near(Vector3 actual, Vector3 expected) => (actual - expected).sqrMagnitude <= tolerance * tolerance;
        }

        internal static (NativeRaidPortal Forward, NativeRaidPortal Reverse, RaidPortal Navigation) PlacePortalPair(
            string id, RaidPlan.End a, BakedRoom aRoom, RaidPlan.End b, BakedRoom bRoom, int mask)
        {
            Transform aFrame = PlaceAperture(a, aRoom), bFrame = PlaceAperture(b, bRoom);
            Vector2 opening = new(a.Dock.Opening.X, a.Dock.Opening.Y);
            Vector2 otherOpening = new(b.Dock.Opening.X, b.Dock.Opening.Y);
            var aFloor = aRoom.DockFloor(a.Dock);
            var bFloor = bRoom.DockFloor(b.Dock);
            RaidPortalKind aKind = KindOf(a.Dock), bKind = KindOf(b.Dock);
            bool doors = aKind == RaidPortalKind.Door && bKind == RaidPortalKind.Door;
            bool allowsCart = doors && a.Dock.MaxCargo == Cargo.Cart && b.Dock.MaxCargo == Cargo.Cart;
            var aPad = PlacePortalPad(a, aRoom, aFloor, aKind, mask);
            var bPad = PlacePortalPad(b, bRoom, bFloor, bKind, mask);
            // A compound door can open onto the other room's perpendicular deck before
            // the first aperture plane. Enter and leave one capsule radius inside the sill.
            float inset = doors && !allowsCart && aRoom == bRoom ? Character.Radius + .04f : 0f;
            var forward = new NativeRaidPortal(id + "-a", aFrame, bFrame, opening, aFloor, bFloor, allowsCart,
                aKind, bKind, aPad, bPad, inset);
            var reverse = new NativeRaidPortal(id + "-b", bFrame, aFrame, otherOpening, bFloor, aFloor, allowsCart,
                bKind, aKind, bPad, aPad, inset);
            var apertureA = new RaidAperture(aFloor, aKind, PoseOf(aFrame), opening, aPad.position, aFrame, inset);
            var apertureB = new RaidAperture(bFloor, bKind, PoseOf(bFrame), otherOpening, bPad.position, bFrame, inset);
            var use = RaidPortalUse.Vision | (doors ? RaidPortalUse.Walking : RaidPortalUse.None);
            return (forward, reverse, new RaidPortal(apertureA, apertureB, use, use));
        }

        private static Transform PlaceAperture(RaidPlan.End end, BakedRoom room)
        {
            Transform frame = Child(room.Prepared ? room.Root.Find("Docks/" + end.Dock.Id) : room.Root, end.Dock.Id + " portal plane");
            frame.SetPositionAndRotation(end.Sill + end.Up * (end.Dock.Opening.Y / 2),
                Quaternion.LookRotation(end.Outward, end.Up));
            return frame;
        }

        private static RaidPortalKind KindOf(Dock dock) => (dock.Use & DockUse.UpperPortal) != 0 ? RaidPortalKind.Upper :
            (dock.Use & DockUse.LowerPortal) != 0 ? RaidPortalKind.Lower : RaidPortalKind.Door;

        private static Transform PlacePortalPad(RaidPlan.End end, BakedRoom room, RaidFloor floor, RaidPortalKind kind, int mask)
        {
            Transform pad = Child(room.Root, end.Dock.Id + " portal landing pad");
            if (kind == RaidPortalKind.Door)
            {
                pad.SetPositionAndRotation(end.Pad(PortalSetback), Quaternion.LookRotation(-end.Outward, floor.Up));
                return pad;
            }

            Vector3 up = floor.Up;
            Vector3 center = end.Sill + end.Up * (end.Dock.Opening.Y * .5f);
            Vector3 fromCenter = center - up * Vector3.Dot(center - floor.Frame.position, up);
            Vector3 forward = Vector3.ProjectOnPlane(end.Up, up).normalized;
            Vector3 side = Vector3.Cross(up, forward);
            var routes = end.Room.Module.Routes.Where(route => route.To == end.Dock.Id)
                .Select(route => end.Room.Point(route.Knots.Last().Position));
            var candidates = routes.Concat(new[] { 0f, .5f, 1f, 1.45f, 1.85f, 2.2f, 2.65f }
                .SelectMany(step => new[] { -forward, forward, -side, side }.Select(axis => fromCenter + axis * step)));
            foreach (Vector3 candidate in candidates)
            {
                if (kind == RaidPortalKind.Lower && Vector3.ProjectOnPlane(candidate - center, up).magnitude <
                    end.Dock.Opening.X * .5f + Character.Radius + .035f) continue;
                if (!Physics.Raycast(candidate + up * .24f, -up, out var hit, .53f, mask,
                    QueryTriggerInteraction.Ignore) || !floor.Support.Contains(hit.collider) ||
                    Vector3.Dot(hit.normal, up) < .8f) continue;
                Vector3 bottom = hit.point + up * (Character.Radius + .025f);
                Vector3 top = hit.point + up * (Character.Height - Character.Radius - .025f);
                if (Physics.CheckCapsule(bottom, top, Character.Radius, mask, QueryTriggerInteraction.Ignore)) continue;
                pad.SetPositionAndRotation(hit.point, Quaternion.LookRotation(forward, up));
                return pad;
            }
            throw new InvalidOperationException($"{end.Room.Module.Id}/{end.Dock.Id}: no clear supported hatch landing.");
        }

        private Material Material(Shader shader, Color color)
        {
            var material = new Material(shader) { color = color };
            materials.Add(material);
            return material;
        }

        private Collider PlacePiece(Transform parent, Block block, int layer, Material material)
        {
            var visual = PlaceVisualPiece(parent, block, layer, material);
            var collider = visual.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = visual.sharedMesh;
            return collider;
        }

        private MeshFilter PlaceVisualPiece(Transform parent, Block block, int layer, Material material)
        {
            var node = Child(parent, block.Id);
            node.gameObject.layer = layer;
            var mesh = new Mesh { name = block.Id };
            mesh.vertices = block.Vertices.Select(RaidPlan.Vector).ToArray();
            mesh.triangles = block.Faces;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            var filter = node.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            node.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            return filter;
        }

        private Collider PlaceFoldCollider(Transform root, string name, int layer, IEnumerable<Block> blocks)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (Block block in blocks)
            {
                int offset = vertices.Count;
                vertices.AddRange(block.Vertices.Select(RaidPlan.Vector));
                foreach (int index in block.Faces) triangles.Add(offset + index);
            }
            if (vertices.Count == 0) return null;

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            meshes.Add(mesh);
            var node = Child(root, name);
            node.gameObject.layer = layer;
            var collider = node.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            return collider;
        }

        internal bool TryPlayerUp(Vector3 point, Vector3 previousUp, out Vector3 up) =>
            TrySphericalUp(point, previousUp, out up, out _) || TryUp(point, out up);

        internal bool TrySphericalUp(Vector3 point, Vector3 previousUp, out Vector3 up, out BakedRoom sphericalRoom)
        {
            foreach (BakedRoom room in rooms.Values)
            {
                int mask = SphericalChamber.GetMask(room.Plan.Module.Id);
                if (!room.Prepared || mask == 0 || !room.PreparedSurfaces.Contains(point)) continue;
                Vector3 local = room.Root.InverseTransformPoint(point);
                Vector3 localUp = room.Root.InverseTransformDirection(previousUp);
                if (!SphericalGravityField.TryUp(new System.Numerics.Vector3(local.x, local.y, local.z),
                    new System.Numerics.Vector3(localUp.x, localUp.y, localUp.z), out var direction, out _, mask)) continue;
                up = room.Root.TransformDirection(RaidPlan.Vector(direction));
                sphericalRoom = room;
                return true;
            }
            up = default;
            sphericalRoom = null;
            return false;
        }

        internal BakedRoom MobiusRoomAt(Vector3 point)
        {
            foreach (BakedRoom room in rooms.Values)
            {
                if (!room.Prepared || room.Plan.Module.Id != MobiusHall.Id) continue;
                Vector3 local = room.Root.InverseTransformPoint(point);
                if (Mathf.Abs(local.x) <= MobiusHall.HalfInteriorX + GrayBuilder.Skirt &&
                    Mathf.Abs(local.z) <= MobiusHall.HalfInteriorZ &&
                    local.y >= -GrayBuilder.Skirt && local.y <= MobiusHall.InteriorHeight)
                    return room;
            }
            return null;
        }

        internal bool TryUp(Vector3 point, out Vector3 up, bool allowsHandOnly = true)
        {
            // Navigation knots describe a traversable course, not a room-wide field.
            // Only the ribbon skin wins within one metre; elsewhere the room is
            // divided exactly at its middle plane, including unsupported falling bodies.
            if (MobiusRoomAt(point) is { } mobius)
            {
                Vector3 local = mobius.Root.InverseTransformPoint(point);
                var position = new System.Numerics.Vector3(local.x, local.y, local.z);
                System.Numerics.Vector3 direction;
                if (!allowsHandOnly || !MobiusHall.TryGravity(position, out direction))
                    direction = MobiusHall.RoomUpAt(position);
                up = mobius.Root.TransformDirection(RaidPlan.Vector(direction));
                return true;
            }
            foreach (var fold in folds)
                if (fold.TryUp(point, out up)) return true;
            float nearestSupport = float.PositiveInfinity;
            Vector3 localUp = default;
            foreach (var room in rooms.Values.Where(room => room.Prepared))
            {
                // Spherical route knots are for navigation. The deck and shell
                // fields alone determine the chamber's physical gravity.
                if (SphericalChamber.IsVariant(room.Plan.Module.Id) || !room.PreparedSurfaces.Contains(point)) continue;
                foreach (var turn in room.PreparedSurfaces.Turns)
                    if (turn.TryUp(point, solidMask, allowsHandOnly, out var rotated, out float depth) && depth <= nearestSupport)
                    {
                        nearby(depth, rotated);
                    }
                if (room.PreparedSurfaces.TryNearFloor(point, GrayBuilder.Height, solidMask, out var floor, out float distance) &&
                    distance + .02f < nearestSupport)
                    nearby(distance, floor.Up);
            }
            if (nearestSupport < float.PositiveInfinity)
            {
                up = localUp;
                return true;
            }
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

            void nearby(float distance, Vector3 direction)
            {
                nearestSupport = distance;
                localUp = direction;
            }
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

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
        internal RaidGravityVolume GravityVolume { get; }
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
            if (plan.Module.Id != MobiusHall.Id && !SphericalChamber.IsVariant(plan.Module.Id))
                GravityVolume = new RaidGravityVolume(this);
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
        private RoomLighting roomLighting;
        private ExtractionBridgeVista bridgeVista;
        private readonly RaidInteriorDetails details = new();
        private readonly RoomBloodTraces bloodTraces = new();
        private readonly BrokenEquipment brokenEquipment = new();
        private RoomLighting Lighting => roomLighting ??= new RoomLighting(materials, meshes);
        private readonly List<FreightFold> folds = new();
        private RaidPlan plan;
        private RaidLayout layout;
        private RaidFloor[] branchFloors = Array.Empty<RaidFloor>();
        private NativeRaidPortal[] portals;
        private Action beforeClose;
        private int solidMask;
        private (Material Floor, Material Wall) finishes;
        private Material terminalSteel, terminalAmber;
        internal RaidNavigationContext Context { get; private set; }
        internal IEnumerable<BakedRoom> Rooms => rooms.Values;
        internal IReadOnlyList<FreightFold> Folds => folds;
        internal RaidPlan Plan => plan;
        internal RaidRoomPrefabs RoomPrefabs { get; private set; }
        internal Transform BranchRoot { get; private set; }
        internal RaidPlan.End? BranchEntrance => plan?.Branch?.Entrance;
        private Bounds ringGravityBounds;
        internal Action<Rigidbody> TrackRingActor { get; private set; }

        internal void BindRingGravity(Bounds reservation, Action<Rigidbody> trackActor)
        {
            ringGravityBounds = reservation;
            ringGravityBounds.Expand(new Vector3(.4f, 8f, .4f));
            TrackRingActor = trackActor;
        }

        internal bool ContainsRing(Vector3 point)
        {
            if (!BranchRoot || !ringGravityBounds.Contains(BranchRoot.InverseTransformPoint(point))) return false;
            // Plan reservation and the ring's soft margin are broader than walkable geometry.
            // A native floor or fold owns the overlap at the physical entrance.
            if (TryAmbientGravity(point, out _, out _) || MobiusRoomAt(point) != null ||
                TrySphericalUp(point, Vector3.zero, out _, out _)) return false;
            foreach (var fold in folds)
                if (fold.TryUp(point, out _)) return false;
            return true;
        }
        internal BakedRoom Room(RaidPlan.Room room) => rooms[room];

        internal void Place(int seed, Pose entrance, int worldLayer, int obstacles, bool catalogSites = false,
            int completedLevels = 0, RaidPrefabBranch branch = null, RaidRoomPrefabs roomPrefabs = null,
            bool rotaryQuota = false)
        {
            if (plan != null) throw new InvalidOperationException("The native raid module has already been placed.");
            bool CanPrepare(RaidPlan.Room room) => roomPrefabs != null && roomPrefabs.TryGetPrepared(room, out var prefab) && prefab;

            var candidate = RaidPlan.Generate(seed, entrance, completedLevels, branch, MaintenanceTrench.Create,
                canPrepare: CanPrepare, rotaryQuota: rotaryQuota);
            var portalPairs = CollectPortalPairs(candidate, CanPrepare);
            plan = candidate;
            solidMask = (1 << worldLayer) | obstacles;
            RoomPrefabs = roomPrefabs;
            var shader = Shader.Find("Standard");
            if (!shader) throw new InvalidOperationException("The gray raid requires Unity's Standard shader.");
            var (floor, wall) = Finishes(shader);
            var shellDetails = Material(shader, new Color(.52f, .56f, .57f));
            var obstaclesMaterial = Material(shader, new Color(.39f, .33f, .25f));
            var sightStrips = new RaidSightStrips(shader, materials, meshes);
            var fitout = CreateFitout(shader);
            foreach (var room in plan.Rooms)
            {
                if (roomPrefabs != null && roomPrefabs.TryGetPrepared(room, out var prepared))
                {
                    rooms.Add(room, PlacePrepared(room, worldLayer, prepared, catalogSites ? obstaclesMaterial : null, fitout));
                    continue;
                }
                if (catalogSites) RaidCatalogSites.Dress(room.Module);
                RaidLootSites.Dress(room.Module);
                rooms.Add(room, PlaceRoom(room, worldLayer, floor, wall, shellDetails, obstaclesMaterial, sightStrips,
                    roomPrefabs?.For(room), fitout));
            }
            foreach (var link in plan.Freight)
            {
                if (link.Fold == null) continue;
                var root = Child(transform, $"Freight fold {link.To.Room.Index:D2}");
                root.SetPositionAndRotation(link.Fold.Frame.position, link.Fold.Frame.rotation);
                var geometry = link.Fold.Module.Geometry.Concat(RaidFreightFold.SeamDecks(link.Fold.Module)).ToArray();
                bool open = link.Fold.Module.Id is RaidFreightFold.ShortRise or RaidFreightFold.ShortDrop;
                foreach (var block in geometry)
                {
                    var finish = block.Role == BlockRole.Floor ? floor :
                        open || RetainsShellFinish(block.Id) ? shellDetails : wall;
                    PlaceVisualPiece(root, block, worldLayer, finish);
                }

                // A rebase moves every fold at once. Keep the exact collision triangles but
                // submit one static shape per role, including a separate invisible barrier.
                var deck = PlaceFoldCollider(root, "Freight deck", worldLayer, geometry.Where(block => block.Role == BlockRole.Floor));
                var shell = PlaceFoldCollider(root, "Freight walls", worldLayer, geometry.Where(block => block.Role != BlockRole.Floor));
                PlaceFoldCollider(root, "Invisible freight safety", worldLayer, RaidFreightFold.SafetyBarriers(link.Fold.Module));
                var fold = new FreightFold(link, root, deck, shell);
                folds.Add(fold);
                Lighting.Fold(fold);
                details.Transit(root, geometry);
            }
            Collider branchCollar = null;
            if (plan.Branch is { } selected)
            {
                BranchRoot = Child(transform, selected.Descriptor.Module.Id);
                BranchRoot.SetPositionAndRotation(selected.Placement.position, selected.Placement.rotation);

                selected.Descriptor.Mount(BranchRoot);
                var ringLink = plan.RingLink;
                var collar = ringLink.Fold == null ? selected.Entrance :
                    new RaidPlan.End(ringLink.Fold, ringLink.Fold.Module.Docks[1]);
                branchCollar = PlaceCollar(collar, selected.Descriptor.ThresholdSpacing, worldLayer, floor, wall);
            }
            if (catalogSites) RaidCatalogSites.Mark(this);
            Physics.SyncTransforms();
            foreach (var room in rooms.Values) bloodTraces.Apply(room);

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
            var decks = rooms.Values.Where(room => room.Prepared)
                .SelectMany(room => room.PreparedSurfaces.DeckPassages(solidMask)
                    .Concat(room.PreparedSurfaces.StairPassages(solidMask)))
                .Concat(folds.Where(fold => fold.Link != plan.RingLink).SelectMany(fold => fold.DeckPassages(
                    Room(fold.Link.From.Room).DockFloor(fold.Link.From.Dock),
                    Room(fold.Link.To.Room).DockFloor(fold.Link.To.Dock))));
            if (plan.Branch?.Descriptor.Navigation?.Invoke(BranchRoot, branchCollar) is { } branchNavigation)
            {
                branchFloors = branchNavigation.Floors;
                decks = decks.Concat(branchNavigation.Decks);
                links.AddRange(branchNavigation.Portals);
                var link = plan.RingLink;
                var source = Room(link.From.Room).DockFloor(link.From.Dock);
                float arrivalLead = plan.Branch.Value.Descriptor.ThresholdSpacing;
                if (link.Fold == null)
                {
                    const float setback = 1.40f;
                    Vector3 from = link.From.Pad(setback), to = link.To.Pad(setback + arrivalLead);
                    float length = Vector3.Distance(from, to);
                    passages.Add(new RaidWalkPassage(source, branchNavigation.Entrance, from, to, length));
                    passages.Add(new RaidWalkPassage(branchNavigation.Entrance, source, to, from, length));
                }
                else
                {
                    var fold = folds.Single(candidate => candidate.Link == link);
                    decks = decks.Concat(fold.DeckPassages(source, branchNavigation.Entrance, arrivalLead));
                }
            }
            layout = new RaidLayout(rooms.Values.SelectMany(room => room.Floors).Concat(branchFloors), links, passages,
                solidMask, new Vector3(16000, 0, 16000), .86f, 2.86f, decks);
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

        private Collider PlaceCollar(RaidPlan.End dock, float depth, int worldLayer, Material floor, Material wall)
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

            details.RingCollar(collar);
            return floorBox.GetComponent<Collider>();

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
            var context = new RaidNavigationContext(plan.Seed, navigation, rooms.Values.ToArray(), branchFloors, portals, solidMask);
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

        private static bool RetainsShellFinish(string id) =>
            id is "roof" or "freight-pit-rail" or "gallery-rail" or "lab-gallery-rail" or "common-edge" ||
            id.StartsWith("roof-", StringComparison.Ordinal) || id.EndsWith("-roof", StringComparison.Ordinal) ||
            id.Contains("-roof-", StringComparison.Ordinal) ||
            id.EndsWith("-ceiling", StringComparison.Ordinal) || id.Contains("-ceiling-wing-", StringComparison.Ordinal) ||
            id.EndsWith("stair-landing-rail-east", StringComparison.Ordinal) ||
            id.EndsWith("stair-landing-rail-west", StringComparison.Ordinal) ||
            id.StartsWith("middle-guard-", StringComparison.Ordinal) || id.StartsWith("upper-guard-", StringComparison.Ordinal);

        private BakedRoom PlaceRoom(RaidPlan.Room room, int worldLayer, Material floor, Material wall, Material shellDetails,
            Material obstacles, RaidSightStrips sightStrips, GameObject prefab, RaidInteriorFitout fitout)
        {
            var root = prefab ? Object.Instantiate(prefab, transform, false).transform : Child(transform, room.Module.Id);
            root.name = room.Module.Id;
            root.SetPositionAndRotation(room.Frame.position, room.Frame.rotation);
            var colliders = new List<Collider>();
            var support = new List<Collider>();
            var floorBounds = new List<Bounds>();
            var localGeometry = new List<Bounds>();
            var authored = prefab ? AuthoredBlocks(root, room.Module) : null;
            var offlineParts = RoomLighting.OfflineFixtureParts(room.Module);
            bool bridge = room.Module.Id is RaidExtraction.StraightBridgeId or RaidExtraction.BentBridgeId;
            if (bridge) bridgeVista ??= new ExtractionBridgeVista(floor.shader, materials, meshes);
            foreach (var block in room.Module.Geometry)
            {
                if (block.Role == BlockRole.Free) continue;
                if (offlineParts.Contains(block.Id))
                {
                    if (authored != null) RoomLighting.RemoveFixture(authored[block.Id].gameObject);
                    continue;
                }
                if (room.OpenPlugs.Contains(block.Id))
                {
                    if (authored != null) authored[block.Id].gameObject.SetActive(false);
                    continue;
                }
                var (minimum, maximum) = block.Bounds;
                Vector3 low = RaidPlan.Vector(minimum), high = RaidPlan.Vector(maximum);
                var bounds = new Bounds((low + high) / 2, high - low);
                Collider collider;
                if (authored != null) collider = authored[block.Id];
                else if (bridge && block.Id.StartsWith(RaidExtractionBridge.FastenerPrefix, StringComparison.Ordinal))
                {
                    PlaceVisualPiece(root, block, worldLayer, bridgeVista.Fastener);
                    continue;
                }
                else if (bridge && block.Id.StartsWith(RaidExtractionBridge.TimberPrefix, StringComparison.Ordinal))
                {
                    int row = block.Id[block.Id.Length - 1] - '0';
                    collider = PlacePiece(root, block, worldLayer, bridgeVista.Timber(row));
                }
                else if (bridge && block.Id.StartsWith(RaidExtractionBridge.FracturePrefix, StringComparison.Ordinal))
                    collider = PlacePiece(root, block, worldLayer, floor);
                else
                {
                    var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    box.name = block.Id;
                    box.transform.SetParent(root, false);
                    box.transform.localPosition = bounds.center;
                    box.transform.localScale = bounds.size;
                    if (room.Module.Id == RaidQuotaRotaryRoom.Id &&
                        (block.Id == "approach-ramp-down" || block.Id == "approach-ramp-up"))
                    {
                        // Generic room placement uses an AABB; keep this ramp's true slope.
                        Vector3 a = RaidPlan.Vector(block.Vertices[0]);
                        Vector3 along = RaidPlan.Vector(block.Vertices[4]) - a;
                        Vector3 up = RaidPlan.Vector(block.Vertices[1]) - a;
                        Vector3 across = RaidPlan.Vector(block.Vertices[3]) - a;
                        box.transform.localPosition = (a + RaidPlan.Vector(block.Vertices[6])) * .5f;
                        box.transform.localRotation = Quaternion.LookRotation(across, up);
                        box.transform.localScale = new Vector3(along.magnitude, up.magnitude, across.magnitude);
                    }
                    box.GetComponent<Renderer>().sharedMaterial = block.Role switch
                    {
                        BlockRole.Floor => floor,
                        BlockRole.Obstacle or BlockRole.Fixture => obstacles,
                        BlockRole.Shell when RetainsShellFinish(block.Id) => shellDetails,
                        BlockRole.Plug when block.Id.EndsWith("hatch-plug", StringComparison.Ordinal) => shellDetails,
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
            Collider cableSupport = fitout.Install(room.Module.Id, root, worldLayer, colliders);
            if (cableSupport) support.Add(cableSupport);
            Bounds volume = localGeometry[0];
            foreach (var block in localGeometry) volume.Encapsulate(block);
            Bounds bake = volume;
            bake.Expand(.20f);
            var raidFloor = new RaidFloor($"{room.Module.Id}-{room.Index}", "ground", room.Module.Id,
                room.Frame, bake, colliders, support, root) { NarrowDeck = bridge };
            DressCappedTransit(room, root, worldLayer);
            var baked = new BakedRoom(room, root, raidFloor, colliders, floorBounds, prefab);
            if (bridge)
            {
                bool bent = room.Module.Id == RaidExtraction.BentBridgeId;
                bridgeVista.Apply(root, bent);
                ExtractionBridgeLandingArt.Apply(root, bent, bridgeVista, materials, meshes);
            }
            else
            {
                Lighting.Apply(room.Module, root, prefab);
                details.Room(baked);
            }
            return baked;
        }

        internal BakedRoom PlacePrepared(RaidPlan.Room room, int layer, GameObject prefab, Material catalogMaterial = null, RaidInteriorFitout fitout = null)
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
                var offlineParts = RoomLighting.OfflineFixtureParts(room.Module);
                bool shaft = room.Module.Id == "shaft-plant";
                var shaftFinishes = shaft ? Finishes(Shader.Find("Standard")) : default;
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
                    if (offlineParts.Contains(block.Id))
                    {
                        parts.Remove(block.Id);
                        RoomLighting.RemoveFixture(part.gameObject);
                        continue;
                    }
                    if (room.OpenPlugs.Contains(block.Id))
                    {
                        part.gameObject.SetActive(false);
                        continue;
                    }
                    part.gameObject.layer = layer;
                    if (shaft && part.TryGetComponent(out MeshRenderer renderer))
                    {
                        if (block.Role == BlockRole.Floor) renderer.sharedMaterial = shaftFinishes.Floor;
                        if (block.Role == BlockRole.Shell && !RetainsShellFinish(block.Id))
                            renderer.sharedMaterial = shaftFinishes.Wall;
                    }
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
                Collider cableSupport = fitout?.Install(room.Module.Id, root, layer, colliders);
                var prepared = new PreparedRaidSurfaces(room, root, parts, colliders.ToArray(), cableSupport);
                var baked = new BakedRoom(room, root, prepared.Floors.First(), colliders, new List<Bounds>(), true, parts, prepared);
                Lighting.Apply(room.Module, root, true);
                DressCappedTransit(room, root, layer);
                details.Room(baked);
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
            if (!terminalSteel) terminalSteel = NamedMaterial(shader, new Color(.24f, .30f, .32f), "Contour-4 terminal enamel");
            if (!terminalAmber) terminalAmber = NamedMaterial(shader, new Color(.67f, .46f, .20f), "Contour-4 terminal legend");
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

        private (Material Floor, Material Wall) Finishes(Shader shader)
        {
            if (finishes.Floor) return finishes;
            if (!shader) throw new InvalidOperationException("The raid requires Unity's Standard shader.");
            finishes = (RoomFinish.CreateFloor(shader), RoomFinish.CreateWall(shader));
            materials.Add(finishes.Floor);
            materials.Add(finishes.Wall);
            return finishes;
        }

        private Material Material(Shader shader, Color color)
        {
            var material = new Material(shader) { color = color };
            materials.Add(material);
            return material;
        }

        private RaidInteriorFitout CreateFitout(Shader shader) =>
            new(NamedMaterial(shader, new Color(.24f, .34f, .34f), "Contour-4 fitout housing"),
                NamedMaterial(shader, new Color(.28f, .39f, .43f), "Contour-4 fitout control housing"),
                NamedMaterial(shader, new Color(.43f, .47f, .43f), "Contour-4 fitout storage housing"),
                NamedMaterial(shader, new Color(.63f, .55f, .32f), "Contour-4 fitout trim metal"),
                NamedMaterial(shader, new Color(.11f, .16f, .17f), "Contour-4 fitout faceplate"), materials, meshes, brokenEquipment);

        private Material NamedMaterial(Shader shader, Color color, string name)
        {
            var material = Material(shader, color);
            material.name = name;
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
            if (MobiusRoomAt(point) is { } mobius)
            {
                up = allowsHandOnly && MobiusWalk.TryFieldUp(mobius.Root, point, out var ribbonUp)
                    ? ribbonUp
                    : MobiusWalk.AmbientUp(mobius.Root, point);
                return true;
            }
            foreach (var fold in folds)
                if (fold.TryUp(point, out up)) return true;

            bool ambient = TryAmbientGravity(point, out up, out float nearestSupport);
            bool supportedTurn = false;
            foreach (var room in rooms.Values)
            {
                // Sphere route knots are for navigation. The deck and shell
                // fields alone determine its physical gravity.
                if (!room.Prepared || SphericalChamber.IsVariant(room.Plan.Module.Id) ||
                    !room.PreparedSurfaces.Contains(point)) continue;
                foreach (var turn in room.PreparedSurfaces.Turns)
                {
                    if (!turn.TryUp(point, solidMask, allowsHandOnly, out var rotated, out float depth) ||
                        depth > nearestSupport + .02f) continue;
                    nearestSupport = depth;
                    up = rotated;
                    supportedTurn = true;
                }
            }
            if (supportedTurn || ambient) return true;
            return TryGetComponent(out RaidRoomInteractions mechanisms) && mechanisms.TryShaftUp(point, out up);
        }

        private bool TryAmbientGravity(Vector3 point, out Vector3 up, out float distance)
        {
            distance = float.PositiveInfinity;
            up = default;
            int ownerIndex = int.MaxValue;
            foreach (BakedRoom room in rooms.Values)
            {
                if (room.GravityVolume == null || !room.GravityVolume.TryFloor(point, out var floor, out float depth) ||
                    depth > distance || depth == distance && room.Plan.Index >= ownerIndex) continue;
                distance = depth;
                ownerIndex = room.Plan.Index;
                up = floor.Up;
            }
            return ownerIndex != int.MaxValue;
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
            details.Release();
            bloodTraces.Release();
            brokenEquipment.Release();
            bridgeVista?.Release();
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
        private readonly Collider deck;
        private readonly Vector3[] points;
        private readonly Vector3[] support;
        private readonly float halfWidth, height;

        internal FreightFold(RaidPlan.FreightLink link, Transform root, Collider deck, Collider shell)
        {
            Link = link;
            Root = root;
            this.deck = deck;
            Colliders = new[] { deck, shell };
            var frames = link.Fold.Module.Routes[0].Knots;
            points = frames.Select(frame => RaidPlan.Vector(frame.Position)).ToArray();
            support = frames.Select(frame => RaidPlan.Vector(frame.Up)).ToArray();
            halfWidth = link.From.Dock.Opening.X / 2;
            height = link.From.Dock.Opening.Y;
        }

        internal IEnumerable<RaidDeckPassage> DeckPassages(RaidFloor from, RaidFloor to, float arrivalLead = 0f)
        {
            // Local meshes are eroded at the sill, even though the fold joins the room.
            const float setback = 1.30f;
            var course = new List<Pose> { Lead(Link.From, Link.From.Outward, setback) };
            var knots = Link.Fold.Module.Routes[0].Knots;
            foreach (var knot in knots)
                course.Add(new Pose(RaidPlan.Vector(knot.Position),
                    Quaternion.LookRotation(RaidPlan.Vector(knot.Forward), RaidPlan.Vector(knot.Up))));
            course.Add(Lead(Link.To, -Link.To.Outward, setback + arrivalLead));

            // Keep the preflight sampling finer than the motion driver and include both room leads.
            var samples = RaidDeckPassage.DenseSamples(course, .12f).ToArray();

            var supports = new HashSet<Collider>(from.Support);
            supports.UnionWith(to.Support);
            supports.Add(deck);
            yield return new RaidDeckPassage(from, to, Root, samples, supports);
            var reverse = samples.Reverse().Select(pose => new Pose(pose.position,
                pose.rotation * Quaternion.AngleAxis(180f, Vector3.up))).ToArray();
            yield return new RaidDeckPassage(to, from, Root, reverse, supports);

            Pose Lead(RaidPlan.End end, Vector3 forward, float distance) => new(Root.InverseTransformPoint(end.Pad(distance)),
                Quaternion.LookRotation(Root.InverseTransformDirection(forward), Root.InverseTransformDirection(end.Up)));
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Contour4.NativeRaid.Enemies;
using Contour4.Procedural.Catalog;
using Contour4.Procedural.Navigation;
using Contour4.NativeRaid.Runtime;
using HarmonyLib;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using StockModule = global::Module;

namespace Contour4.NativeRaid;

[DefaultExecutionOrder(1000)]
internal sealed class NativeRaidModule : MonoBehaviour
{
    private static readonly FieldInfo[] Connections = new[] { "Top", "Right", "Bottom", "Left" }
        .Select(side => AccessTools.Field(typeof(StockModule), "Connecting" + side)).ToArray();
    private static readonly FieldInfo AvatarTumbling = AccessTools.Field(typeof(PlayerAvatar), "isTumbling");
    private static readonly FieldInfo AvatarDisabled = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
    private RaidAssembly raid;
    private RaidTarget playerTarget;
    private Transform eye;
    private Rigidbody trackedBody;
    private PlayerAvatar trackedAvatar;
    private readonly PortalDoorDebris portalDoorDebris = new();
    private readonly NativeRaidCargoTraversal cargoTraversal = new();
    private readonly HashSet<Rigidbody> transferredWithPlayer = new();
    internal Func<PhysGrabber, NativeRaidPortal, (bool CanCross, Rigidbody Body)?> MappedHandFor { private get; set; }
    internal Action<RaidAssembly, Rigidbody> StepHeldHand { private get; set; }
    internal Action<bool> ClearMappedHand { private get; set; }
    internal Action UnbindMappedHand { private get; set; }
    private Transform arrivalDoor;
    private Vector3 previousFeetInRaid;
    private bool catalogSpawned;
    internal Dictionary<ValuableVolume, RaidLootSlot> Loot { get; private set; }
    internal int LootSeed => raid.Plan.Seed;
    internal bool TrenchReplayLoot { get; private set; }
    private readonly Dictionary<LevelPoint, RaidFloor> enemyStartWaypoints = new();
    private readonly Dictionary<RaidFloor, RoomVolume> enemyStartFloors = new();
    private bool warnedAboutMissingEnemyStarts;
    internal IReadOnlyDictionary<LevelPoint, RaidFloor> EnemyStartWaypoints => enemyStartWaypoints;
    internal IReadOnlyDictionary<RaidFloor, RoomVolume> EnemyStartFloors => enemyStartFloors;
    internal Dictionary<LevelPoint, RaidFloor> SupplementalEnemyStarts { get; set; }

    internal void WarnAboutMissingEnemyStarts()
    {
        if (warnedAboutMissingEnemyStarts) return;
        warnedAboutMissingEnemyStarts = true;
        Debug.LogWarning($"[Contour4 Procedural] No supported initial enemy spawn points for seed {raid.Plan.Seed}. Using the stock selection.");
    }

    internal void Place(StockModule module, Func<int> seed, Level stockManor, Action<RaidAssembly> prepareCatalog = null,
        RaidPrefabBranch branch = null, RaidRoomPrefabs roomPrefabs = null, bool rotaryQuota = false,
        TrenchShutterMode shutterMode = TrenchShutterMode.Campaign, bool trenchReplayLoot = false)
    {
        if (raid) return;
        if (GameManager.Multiplayer())
            throw new InvalidOperationException("Contour-4 Procedural is registered for solo levels.");

        var directions = Enumerable.Range(0, Connections.Length)
            .Where(index => (bool)Connections[index].GetValue(module)).ToArray();
        if (directions.Length != 1)
            throw new InvalidOperationException("Contour-4 expects one native connection to the stock starter.");

        var generator = LevelGenerator.Instance;
        var inward = generator.LevelParent.transform.rotation * Quaternion.AngleAxis(directions[0] * 90f - 180f,
            Vector3.up) * Vector3.forward;
        float centerToSeam = LevelGenerator.ModuleWidth * LevelGenerator.TileSize * .5f;
        var entrance = new Pose(transform.position - inward * centerToSeam,
            Quaternion.LookRotation(inward, Vector3.up) * Quaternion.Euler(0f, -90f, 0f));

        raid = gameObject.AddComponent<RaidAssembly>();
        // Cabinet fronts are moved to the stock hinge layer before the raid bakes its room colliders.
        raid.Place(seed(), entrance, LayerMask.NameToLayer("Default"), LayerMask.GetMask("PhysGrabObject", "PhysGrabObjectCart", "PhysGrabObjectHinge"),
            catalogSites: prepareCatalog != null, completedLevels: RunManager.instance.levelsCompleted, branch: branch,
            roomPrefabs: roomPrefabs, rotaryQuota: rotaryQuota);
        NativeRaidInteractions.Attach(raid, shutterMode);
        TrenchReplayLoot = trenchReplayLoot;
        if (raid.Rooms.Any(room => room.Plan.Module.Id == MaintenanceTrench.Id))
            Debug.Log($"[Contour4 Trench] seed={raid.Plan.Seed}, completed={raid.Plan.CompletedLevels}, " +
                $"shutter={RaidRoomInteractions.ResolveTrenchShutter(shutterMode, raid.Plan.CompletedLevels)}, replayLoot={trenchReplayLoot}.");
        else if (trenchReplayLoot)
            Debug.LogWarning($"[Contour4 Trench] Replay loot requested, but seed {raid.Plan.Seed} at {raid.Plan.CompletedLevels} completed levels has no trench.");
        InstallNativeRoomComponents();
        NativeRaidGravity.Attach(raid);
        NativeRaidExtraction.Place(raid, stockManor);
        Loot = NativeRaidLoot.Install(raid);
        prepareCatalog?.Invoke(raid);
    }

    internal void SpawnCatalog(Action<RaidAssembly> spawn)
    {
        if (catalogSpawned || spawn == null || !raid) return;
        spawn(raid);
        catalogSpawned = true;
    }

    internal void InstallNavigation(LevelGenerator generator, Action<RaidAssembly> present = null)
    {
        if (!raid) throw new InvalidOperationException("Generated catalog rooms were not placed before NavMeshSetup.");
        // Stock NavMeshSetupRPC caches this same serialized component after the prefix returns.
        var surface = generator.GetComponent<NavMeshSurface>();
        if (!surface || surface.agentTypeID != StockPortalPursuit.AgentType ||
            surface.useGeometry != NavMeshCollectGeometry.PhysicsColliders || (surface.layerMask.value & 1) == 0)
            throw new InvalidOperationException("The native enemy NavMeshSurface cannot bake the catalog colliders.");
        if (raid.Context != null) return;
        NativeRaidStartBay.Install(generator, raid.Plan.Entrance, raid.Plan.Seed);
        raid.InstallNavigation(EnemyHooks.Clear);
        present?.Invoke(raid);
        Debug.Log($"[Contour4 Procedural] seed={raid.Context.Seed}, level={raid.Plan.CompletedLevels + 1}, " +
            $"modules={raid.Plan.Rooms.Count}, extractions={raid.Plan.QuotaCount}, portalPairs={raid.Plan.Portals.Count}, " +
            $"branch={raid.Plan.Branch?.Descriptor.Module.Id ?? "none"}, rooms=" +
            string.Join(",", raid.Plan.Rooms.Select(room => room.Module.Id)));
    }

    private void InstallNativeRoomComponents()
    {
        var volumes = new Dictionary<RaidPlan.Room, RoomVolume>();
        var preparedVolumes = new Dictionary<RaidFloor, RoomVolume>();
        foreach (var room in raid.Rooms)
        {
            var volumeNode = RaidAssembly.Child(room.Root, "Room volume");
            if (!room.Prepared)
            {
                foreach (Bounds floor in room.FloorBounds)
                {
                    var bounds = volumeNode.gameObject.AddComponent<BoxCollider>();
                    bounds.center = new Vector3(floor.center.x, 1.9f, floor.center.z);
                    bounds.size = new Vector3(floor.size.x, 3.85f, floor.size.z);
                    bounds.isTrigger = true;
                }
                var volume = volumeNode.gameObject.AddComponent<RoomVolume>();
                volumes.Add(room.Plan, volume);
                RegisterEnemyStartFloor(room, room.Floor, volume);
            }

            foreach (var floor in room.Floors.Where(floor => floor.Prepared))
            {
                var node = RaidAssembly.Child(volumeNode, floor.FloorId + " " + floor.IslandId);
                node.SetPositionAndRotation(floor.Frame.position, floor.Frame.rotation);
                var bounds = node.gameObject.AddComponent<BoxCollider>();
                bounds.center = floor.LocalBakeBounds.center;
                bounds.size = floor.LocalBakeBounds.size;
                bounds.isTrigger = true;
                var volume = node.gameObject.AddComponent<RoomVolume>();
                preparedVolumes.Add(floor, volume);
                RegisterEnemyStartFloor(room, floor, volume);
            }

            foreach (Collider collider in room.Colliders)
            {
                var modifier = collider.gameObject.AddComponent<NavMeshModifier>();
                modifier.overrideArea = true;
                modifier.area = room.Floors.Any(floor => Vector3.Dot(floor.Up, Vector3.up) > .999f &&
                    floor.Support.Contains(collider)) ? 0 : 1;
            }
        }

        var points = new Dictionary<(RaidPlan.Room, string), LevelPoint>();
        LevelPoint AtDock(RaidPlan.End end, float setback, bool stockConnection = false)
        {
            var key = (end.Room, end.Dock.Id);
            if (!points.TryGetValue(key, out var point))
            {
                var room = raid.Room(end.Room);
                var floor = room.DockFloor(end.Dock);
                if (room.Prepared && (Vector3.Dot(floor.Up, Vector3.up) < .999f ||
                    !PreparedRaidSurfaces.StockLink(floor, end.Pad(setback), end.Pad(setback),
                        LayerMask.GetMask("Default", "PhysGrabObjectHinge")))) return null;
                var volume = preparedVolumes.TryGetValue(floor, out var localVolume) ? localVolume : volumes[end.Room];
                point = Point(room.Root, end.Dock.Id, end.Pad(setback), end.Outward,
                    volume, stockConnection, floor.Up);
                points.Add(key, point);
            }
            return point;
        }

        AtDock(raid.Plan.Entrance, 2.5f, true);
        foreach (var (from, to) in raid.Plan.Passages)
            Connect(AtDock(from, 1.1f), AtDock(to, 1.1f));
        foreach (var (a, b) in raid.Plan.Portals)
        {
            AtDock(a, RaidAssembly.PortalSetback);
            AtDock(b, RaidAssembly.PortalSetback);
        }
        if (raid.BranchEntrance is RaidPlan.End branch)
            AtDock(branch, 1.1f);

        foreach (var room in raid.Rooms)
        {
            if (room.Prepared)
            {
                var paths = room.Root.Find("Monster paths/Headman body geometry (r0.86 h2.86)");
                var endpoints = new Dictionary<RaidFloor, List<LevelPoint>>();
                int solids = LayerMask.GetMask("Default", "PhysGrabObjectHinge");
                const string approachPrefix = "Path approach ";
                foreach (var path in paths.GetComponentsInChildren<Transform>().Where(marker =>
                    marker.name.StartsWith("Path ", StringComparison.Ordinal)))
                {
                    LevelPoint previous = null;
                    RaidFloor previousFloor = null;
                    foreach (Transform knot in path)
                    {
                        if (Vector3.Dot(knot.up, Vector3.up) < .999f ||
                            !room.PreparedSurfaces.TryNearFloor(knot.position, .26f, solids,
                                out var floor, out _))
                        {
                            previous = null;
                            previousFloor = null;
                            continue;
                        }
                        var point = Point(room.Root, $"{path.name} {knot.name}", knot.position, knot.forward,
                            preparedVolumes[floor], up: floor.Up);
                        if (enemyStartFloors.ContainsKey(floor) && !path.name.StartsWith(approachPrefix, StringComparison.Ordinal))
                            enemyStartWaypoints.Add(point, floor);
                        if (previous && previousFloor == floor) ConnectIfClear(floor, previous, point, float.PositiveInfinity);
                        if (!previous || knot == path.GetChild(path.childCount - 1))
                        {
                            if (!endpoints.TryGetValue(floor, out var terminals))
                                endpoints.Add(floor, terminals = new List<LevelPoint>());
                            foreach (LevelPoint terminal in terminals)
                                ConnectIfClear(floor, point, terminal);
                            terminals.Add(point);
                        }
                        previous = point;
                        previousFloor = floor;
                    }
                    if (previous && path.name.StartsWith(approachPrefix, StringComparison.Ordinal))
                    {
                        string dockId = path.name.Substring(approachPrefix.Length);
                        if (points.TryGetValue((room.Plan, dockId), out var dockPoint) &&
                            previousFloor == room.DockFloor(room.Plan.Module.Docks.Single(dock => dock.Id == dockId)))
                            ConnectIfClear(previousFloor, previous, dockPoint);
                    }
                }

                void ConnectIfClear(RaidFloor floor, LevelPoint from, LevelPoint to, float maximumLength = 2.65f)
                {
                    Vector3 lift = floor.Up * .15f;
                    if (PreparedRaidSurfaces.StockLink(floor, from.transform.position - lift, to.transform.position - lift,
                        solids, maximumLength))
                        Connect(from, to);
                }
                continue;
            }
            // Use the catalog's cart routes to seed useful points around fixed obstacles.
            var used = new HashSet<(string From, string To)>();
            foreach (HaulRoute route in room.Plan.Module.Routes.Where(route => route.Cargo == Cargo.Cart))
            {
                if (!points.TryGetValue((room.Plan, route.From), out var previous) || !used.Add((route.From, route.To))) continue;
                for (int index = 0; index < route.Knots.Length; index++)
                {
                    Vector3 position = room.Plan.Point(route.Knots[index].Position);
                    Vector3 local = room.Root.InverseTransformPoint(position);
                    if (!room.FloorBounds.Any(bounds => bounds.Contains(local)) ||
                        Vector3.Distance(previous.transform.position, position) < 2f) continue;
                    var point = Point(room.Root, $"{route.Id} waypoint {index}", position,
                        room.Plan.Direction(route.Knots[index].Forward), volumes[room.Plan]);
                    if (enemyStartFloors.ContainsKey(room.Floor))
                        enemyStartWaypoints.Add(point, room.Floor);
                    Connect(previous, point);
                    previous = point;
                }
                if (points.TryGetValue((room.Plan, route.To), out var destination)) Connect(previous, destination);
            }

        }
    }

    private void RegisterEnemyStartFloor(BakedRoom room, RaidFloor floor, RoomVolume volume)
    {
        if (room.Plan == raid.Plan.Entrance.Room ||
            room.Plan.Module.Id is SphericalChamber.Id or MobiusHall.Id ||
            !StockPortalPursuit.WorldUp(floor) || floor.Prepared && floor.IslandId != "floor")
            return;
        enemyStartFloors.Add(floor, volume);
    }

    private static LevelPoint Point(Transform parent, string name, Vector3 feet, Vector3 forward, RoomVolume volume,
        bool connect = false, Vector3? up = null)
    {
        var marker = RaidAssembly.Child(parent, name);
        Vector3 localUp = up ?? parent.up;
        marker.SetPositionAndRotation(feet + localUp * .15f, Quaternion.LookRotation(forward, localUp));
        var point = marker.gameObject.AddComponent<LevelPoint>();
        point.ConnectedPoints = new List<LevelPoint>();
        point.Room = volume;
        point.ModuleConnect = connect;
        return point;
    }

    private static void Connect(LevelPoint a, LevelPoint b)
    {
        if (!a || !b) return;
        if (!a.ConnectedPoints.Contains(b)) a.ConnectedPoints.Add(b);
        if (!b.ConnectedPoints.Contains(a)) b.ConnectedPoints.Add(a);
    }

    private void FixedUpdate()
    {
        transferredWithPlayer.Clear();
        var context = raid ? raid.Context : null;
        var generator = LevelGenerator.Instance;
        if (context == null || !generator || !generator.Level || generator.Level.name != NativeRaidFeature.LevelName ||
            !GameManager.instance || GameManager.Multiplayer() || !SemiFunc.LevelGenDone())
        {
            cargoTraversal.Clear();
            StepHeldHand?.Invoke(raid, null);
            return;
        }
        var controller = PlayerController.instance;
        var avatar = PlayerAvatar.instance;
        if (!controller || !controller.rb || !avatar || (bool)AvatarDisabled.GetValue(avatar))
        {
            cargoTraversal.Clear();
            StepHeldHand?.Invoke(raid, null);
            ClearPlayerTracking();
            if (playerTarget) playerTarget.Unbind();
            return;
        }

        var body = controller.rb;
        bool canMove = CanMovePlayer(body, avatar);
        StepHeldHand?.Invoke(raid, canMove ? body : null);
        Vector3 feet = body.position;
        Vector3 previousFeet = raid.transform.TransformPoint(previousFeetInRaid);
        if (canMove && portalDoorDebris.NeedsCheck())
            foreach (var portal in context.Portals)
                portalDoorDebris.ClearNear(body.worldCenterOfMass, portal.Entrance.position, portal.Entrance.rotation,
                    portal.HalfWidth, portal.HalfHeight, portal.Exit.position, portal.Exit.rotation);
        RaidFloor destination = null;
        if (trackedBody == body && trackedAvatar == avatar && (feet - previousFeet).sqrMagnitude <= 25f)
        {
            if (arrivalDoor && Depth(arrivalDoor, feet) < -.15f)
                arrivalDoor = null;
            foreach (var portal in context.Portals)
            {
                if (!portal.Crossed(previousFeet, feet, 0, 0)) continue;
                if (AllowsCrossingDirection(portal.Entrance, previousFeet, body) && canMove &&
                    portal.Crossed(previousFeet, feet, RaidAssembly.Character.Radius, RaidAssembly.Character.Height) &&
                    TryMoveAcross(controller, avatar, portal, previousFeet))
                {
                    arrivalDoor = portal.Exit;
                    destination = portal.ExitFloor;
                    feet = body.position;
                    StepHeldHand?.Invoke(raid, null);
                }
                else
                    feet = RejectCrossing(body, portal, previousFeet);
                break;
            }
        }
        else
            arrivalDoor = null;
        trackedBody = body;
        trackedAvatar = avatar;
        previousFeetInRaid = raid.transform.InverseTransformPoint(feet);
        cargoTraversal.Step(raid, context, transferredWithPlayer);
        UpdateTarget(context, avatar, feet, destination);
    }

    private static bool CanMovePlayer(Rigidbody body, PlayerAvatar avatar) =>
        !body.isKinematic && !(bool)AvatarDisabled.GetValue(avatar) &&
        !(bool)AvatarTumbling.GetValue(avatar);

    private bool TryMoveAcross(PlayerController controller, PlayerAvatar avatar, NativeRaidPortal portal, Vector3 previousFeet)
    {
        var body = controller.rb;
        var grabber = PhysGrabber.instance;
        bool holding = grabber && grabber.grabbed;
        bool rebase = Vector3.Dot(portal.EntryFloor.Up, portal.ExitFloor.Up) < .9999f;
        if (holding && MappedHandFor?.Invoke(grabber, portal) is { } mappedHand)
        {
            // The held body already occupies the destination. Transfer only the player.
            if (!mappedHand.CanCross || !MovePlayerAcross(controller, avatar, portal, previousFeet, rebase, mappedHand.Body))
                return false;

            // A same-up crossing keeps the last mapped world pull until stock Update runs.
            // A stage rebase instead brings the destination into the native puller frame.
            ClearMappedHand?.Invoke(!rebase);
            return true;
        }
        if (holding)
        {
            if (!NativeRaidPortalCargo.TryPrepare(raid, portal, body, previousFeet, grabber, rebase,
                out var transfer, out Vector3 cargoLanding) &&
                !NativeRaidPortalCargo.TryPrepareHand(raid, portal, body, previousFeet, grabber, rebase,
                    out transfer, out cargoLanding))
                return false;
            if (rebase)
            {
                if (!NativeRaidGravity.TryRebasePlayerAcross(raid, portal, body, cargoLanding, transfer.Bodies))
                    return false;
            }
            else
            {
                NativeRaidGravity.ReleaseNativeJumpForPortal();
                NativeRaidPlayerPose.Reframe(controller, avatar, SemiFunc.MainCamera(), cargoLanding, portal.TravelRotation);
            }
            transfer.Apply(portal);
            transferredWithPlayer.UnionWith(transfer.Bodies);
            return true;
        }

        return MovePlayerAcross(controller, avatar, portal, previousFeet, rebase);
    }

    private bool MovePlayerAcross(PlayerController controller, PlayerAvatar avatar, NativeRaidPortal portal,
        Vector3 previousFeet, bool rebase, Rigidbody mappedHand = null)
    {
        var body = controller.rb;
        if (!raid.TryPassPlayer(portal, body.position, out Vector3 landing, previousFeet)) return false;
        if (rebase)
            return NativeRaidGravity.TryRebasePlayerAcross(raid, portal, body, landing, mappedHand: mappedHand);
        NativeRaidGravity.ReleaseNativeJumpForPortal();
        NativeRaidPlayerPose.Reframe(controller, avatar, SemiFunc.MainCamera(), landing, portal.TravelRotation);
        return true;
    }

    internal NativeRaidPortal CameraSide(Vector3 eyePosition, float nearCorner)
    {
        if (!raid || raid.Context == null || !isActiveAndEnabled ||
            !trackedBody || !trackedAvatar || trackedAvatar != PlayerAvatar.instance ||
            !CanMovePlayer(trackedBody, trackedAvatar) || SemiFunc.PhysGrabberLocalIsGrabbing())
            return null;

        Vector3 feet = trackedBody.position;
        Vector3 previousFeet = raid.transform.TransformPoint(previousFeetInRaid);
        foreach (var portal in raid.Context.Portals)
        {
            if (!portal.Door) continue;
            var entrance = portal.Entrance;
            Vector3 localEye = entrance.InverseTransformPoint(eyePosition);
            if (localEye.z <= 0 || localEye.z > nearCorner + .12f ||
                Mathf.Abs(localEye.x) >= portal.HalfWidth || Mathf.Abs(localEye.y) >= portal.HalfHeight)
                continue;
            float depth = Depth(entrance, feet);
            // The visual eye can protrude through the arrival door during the
            // world rebase. Use the same deliberate-return test as the body.
            if (depth > .25f || depth < -.75f || !AllowsCrossingDirection(entrance, previousFeet, trackedBody))
                continue;

            // The visible aperture selects the view. Test the capsule and destination at the physics threshold.
            Vector3 threshold = feet + entrance.forward * (portal.DoorCrossingDepth - depth);
            Vector3 step = entrance.forward * .002f;
            Vector3 before = threshold - step;
            Vector3 after = threshold + step;
            if (portal.Crossed(before, after, RaidAssembly.Character.Radius, RaidAssembly.Character.Height) &&
                raid.TryPassPlayer(portal, after, out _, before))
                return portal;
        }
        return null;
    }

    private static float Depth(Transform door, Vector3 point) => Vector3.Dot(point - door.position, door.forward);

    private bool AllowsCrossingDirection(Transform door, Vector3 previousFeet, Rigidbody body) =>
        door != arrivalDoor || Depth(door, previousFeet) < -.001f && Vector3.Dot(body.linearVelocity, door.forward) > .01f;

    private static Vector3 RejectCrossing(Rigidbody body, NativeRaidPortal portal, Vector3 lastFeet)
    {
        var door = portal.Entrance;
        Vector3 normal = door.forward;
        Vector3 retreat = lastFeet - normal * Mathf.Max(0, Depth(door, lastFeet) + .02f);
        body.position = retreat;
        if (!body.isKinematic)
            body.linearVelocity -= normal * Mathf.Max(0, Vector3.Dot(body.linearVelocity, normal));
        body.transform.position = retreat;
        return retreat;
    }

    private void UpdateTarget(RaidNavigationContext context, PlayerAvatar avatar, Vector3 feet, RaidFloor floor)
    {
        if (floor == null && !context.TryGetFloor(feet, out floor))
        {
            if (playerTarget) playerTarget.Unbind();
            return;
        }
        if (!playerTarget || playerTarget.gameObject != avatar.gameObject)
        {
            ClearTarget();
            playerTarget = avatar.gameObject.AddComponent<RaidTarget>();
            eye = RaidAssembly.Child(avatar.transform, "Contour-4 portal sight point");
        }
        if (!playerTarget.Owner) playerTarget.Bind(context.Navigation, floor, eye);
        else if (playerTarget.Floor != floor) playerTarget.SetFloor(floor);
        RefreshTargetEye(playerTarget, avatar.PlayerVisionTarget);
    }

    internal static bool RefreshTargetEye(RaidTarget target, PlayerVisionTarget stockVision)
    {
        if (!target || !target.Owner || !stockVision || !stockVision.VisionTransform) return false;
        var module = target.Owner.GetComponent<NativeRaidModule>();
        if (!module || module.playerTarget != target || !module.eye) return false;
        // The stock eye updates in rendered Update, between two of this module's physics ticks.
        RaidAssembly.SyncVisionEye(module.eye, stockVision.VisionTransform);
        return true;
    }

    private void ClearTarget()
    {
        if (playerTarget)
        {
            playerTarget.Unbind();
            Destroy(playerTarget);
        }
        if (eye) Destroy(eye.gameObject);
        playerTarget = null;
        eye = null;
    }

    private void OnDisable()
    {
        cargoTraversal.Clear();
        StepHeldHand?.Invoke(raid, null);
        ClearMappedHand?.Invoke(false);
        ClearTarget();
        ClearPlayerTracking();
    }

    private void OnDestroy() => UnbindMappedHand?.Invoke();

    private void ClearPlayerTracking()
    {
        trackedBody = null;
        trackedAvatar = null;
        arrivalDoor = null;
    }
}

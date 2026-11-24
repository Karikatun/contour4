using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Contour4.NativeRaid;
using Contour4.NativeRaid.Runtime;
using HarmonyLib;
using UnityEngine;

namespace SpaceJourney;

[DefaultExecutionOrder(-1000)]
internal sealed partial class RingRuntime : MonoBehaviour
{
    internal static readonly string[] RoomNames = { "Quota", "Loot1", "Loot2", "Loot3" };
    private static readonly (string A, string B, bool Upper)[] AuthoredPortalPairs =
    {
        ("Loot1", "Loot3", false),
        ("Loot2", "Loot3", false),
        ("Quota", "Loot3", true),
    };
    internal static int PortalPairCount => AuthoredPortalPairs.Length;
    private static readonly System.Reflection.FieldInfo GameState = AccessTools.Field(typeof(GameDirector), "currentState");

    private readonly Dictionary<Rigidbody, Vector3> bodies = new();
    private readonly List<Rigidbody> bodyBuffer = new();
    private readonly List<Bridge> bridges = new();
    private readonly List<Portal> portals = new();
    private readonly PortalDoorDebris portalDoorDebris = new();
    private readonly RingFurnitureMounts furniture = new();
    private readonly List<Crossing> doors = new();
    private readonly Dictionary<Rigidbody, Vector3> lastCenters = new();
    private readonly Dictionary<Rigidbody, PortalEnd> exitThresholds = new();
    private readonly Dictionary<Rigidbody, (PortalEnd Support, Crossing? Pending)> cartCargoSamples = new();
    private CartSeams cartSeams;
    private readonly LooseSeams looseSeams = new();
    private PortalViews portalViews;
    private Transform levelRoot;
    private Transform ring;
    private Transform[] rooms;
    private RoomGravity[] roomGravity;
    private Vector3 gravityTurnAxis;

    private bool connected;
    private bool live;
    private bool proceduralBranch;
    private Bounds branchBounds;
    private float scanTimer;
    private Vector3 playerUpInRing = Vector3.up;
    private Vector3 playerGravityUp = Vector3.up;
    private Rigidbody gravityControllerBody;
    private double gravitySampleTime;
    private float gravityTurnSpeed;
    private Bridge occupiedBridge;
    private Vector3 bridgeFooting;
    private Vector3 bridgeUp;
    private PlayerAvatar fallenPlayer;
    private static readonly System.Reflection.MethodInfo NativeDeath = AccessTools.Method(typeof(PlayerAvatar), "PlayerDeath");
    private static readonly System.Reflection.FieldInfo HeadOwner = AccessTools.Field(typeof(PlayerDeathHead), "playerAvatar");
    private static readonly System.Reflection.FieldInfo HeadPosition = AccessTools.Field(typeof(PlayerDeathHead), "triggeredPosition");
    private static readonly System.Reflection.FieldInfo HeadRotation = AccessTools.Field(typeof(PlayerDeathHead), "triggeredRotation");
    private static readonly System.Reflection.FieldInfo AvatarTumble = AccessTools.Field(typeof(PlayerAvatar), "tumble");
    private static readonly System.Reflection.FieldInfo TumbleBody = AccessTools.Field(typeof(PlayerTumble), "physGrabObject");
    private static readonly System.Reflection.FieldInfo IsTumbling = AccessTools.Field(typeof(PlayerAvatar), "isTumbling");

    internal Transform Ring => ring;
    internal Transform[] Rooms => rooms;
    internal IReadOnlyList<Portal> Portals => portals;
    private static RingRuntime active;
    internal static RingRuntime Active => active && active.live &&
        (active.proceduralBranch ? Contour4.IsProceduralRaid(LevelGenerator.Instance) : Contour4.IsSoloRing(LevelGenerator.Instance))
        ? active : null;
    private static bool MainRaid => GameDirector.instance && GameState.GetValue(GameDirector.instance).ToString() == "Main";

    internal void Connect(Module module)
    {
        if (connected)
            return;
        var connectionNames = new[] { "ConnectingTop", "ConnectingRight", "ConnectingBottom", "ConnectingLeft" };
        int side = Array.FindIndex(connectionNames, name => (bool)AccessTools.Field(typeof(Module), name).GetValue(module));
        if (side < 0)
            throw new InvalidOperationException("Contour-4 has no native route to the starter.");
        levelRoot = LevelGenerator.Instance.LevelParent.transform;
        ring = transform.Find("Contour4 Ring");
        // Connection flags name directions in the generator's grid, while a module
        // instance may have been given an independent quarter turn by the generator.
        ring.rotation = levelRoot.rotation * Quaternion.AngleAxis(side * 90f - 180f, Vector3.up);
        transform.Find("Quota grid entrance").position = ring.TransformPoint(new Vector3(0, .15f, -5f));
        PrepareRing();
        Debug.Log($"[Contour4Ring] Attached stock starter on {connectionNames[side]}.");
    }

    internal void ConnectProcedural(Transform authoredRing, Bounds reservation)
    {
        if (connected) throw new InvalidOperationException("Authored ring is already connected.");
        levelRoot = transform.root;
        ring = authoredRing;
        proceduralBranch = true;
        branchBounds = reservation;
        branchBounds.Expand(new Vector3(.4f, 8f, .4f));
        PrepareRing();
        Debug.Log($"[Contour4Ring] Attached authored branch at {transform.position:F2}.");
    }

    private void PrepareRing()
    {
        rooms = RoomNames.Select(room => ring.Find(room)).ToArray();
        foreach (Transform room in rooms)
            RaidLighting.Brighten(room.Find("Wall lights"));
        Vector3 shaftCenter = (rooms[0].position + rooms[2].position) * .5f;
        roomGravity = rooms.Select(room => new RoomGravity(room, ring, shaftCenter)).ToArray();
        gravityTurnAxis = Vector3.Cross(roomGravity[0].Up, roomGravity[1].Up).normalized;
        bridges.Add(new Bridge(ring.Find("BridgeQuotaLoot1")));
        bridges.Add(new Bridge(ring.Find("BridgeLoot1Loot2")));
        foreach (var (a, b, upper) in AuthoredPortalPairs)
            portals.Add(Portal.Pair(ring.Find(a), b, ring.Find(b), a, upper));
        foreach (var portal in portals.Where(portal => !portal.A.Upper))
        {
            doors.Add(new Crossing(portal.A, portal.B));
            doors.Add(new Crossing(portal.B, portal.A));
        }
        cartSeams = new CartSeams(portals);
        connected = true;
    }

    internal void TrackLoot(ValuableObject loot, Transform marker)
    {
        var body = loot.GetComponentInParent<Rigidbody>() ?? loot.GetComponentInChildren<Rigidbody>();
        if (body && connected)
            bodies[body] = ring.InverseTransformDirection(marker.up);
    }

    internal void BeginRaid(LevelGenerator generator)
    {
        if (live || !connected)
            return;
        if (GameManager.Multiplayer())
        {
            Debug.LogError("[Contour4Ring] This build's player gravity is available in single-player.");
            return;
        }

        // The authored quota doorway is already framed. Open its connection to the
        // native starter so the generated Manor door cannot swing into the cart lane.
        Transform truckDoor = Marker(rooms[0], "Quota__South_Truck");
        string connectorName = RunManager.instance.levelCurrent.ConnectObject.Prefab.name + "(Clone)";
        foreach (Transform child in levelRoot)
        {
            if (child.name == connectorName && Vector3.Distance(child.position, truckDoor.position) < 2f)
                child.gameObject.SetActive(false);
        }

        var extraction = generator.LevelParent.GetComponentsInChildren<ExtractionPoint>(true)
            .SingleOrDefault(candidate => (bool)AccessTools.Field(typeof(ExtractionPoint), "inStartRoom").GetValue(candidate));
        if (!extraction || !extraction.GetComponentInParent<StartRoom>())
            throw new InvalidOperationException("Contour-4 requires the first quota inside the native truck module.");

        var cart = FindFirstObjectByType<PhysGrabCart>();
        if (cart)
        {
            cartSeams.Register(cart);
            var cartBody = cart.GetComponentInParent<Rigidbody>() ?? cart.GetComponentInChildren<Rigidbody>();
            Transform cartMarker = Marker(rooms[0], "Quota__CartSpawn");
            if (cartBody)
            {
                cartBody.position = cartMarker.position + cartMarker.up * .08f;
                cartBody.rotation = cartMarker.rotation;
                if (!cartBody.isKinematic)
                {
                    cartBody.linearVelocity = Vector3.zero;
                    cartBody.angularVelocity = Vector3.zero;
                }
                bodies[cartBody] = Vector3.up;
            }
        }

        furniture.Bind(rooms);
        Physics.SyncTransforms();
        CatalogLoot.Spawn(this);
        live = true;
        active = this;
        var map = gameObject.AddComponent<RingMap>();
        map.Initialize(this);
        portalViews = gameObject.AddComponent<PortalViews>();
        portalViews.Initialize(this);
        Debug.Log($"[Contour4Ring] Raid ready. Truck quota={extraction.transform.position:F2}; cart={cart != null}; " +
                  $"wall lights={rooms.Sum(room => room.Find("Wall lights").GetComponentsInChildren<Light>().Length)}; initial loot={bodies.Count - (cart ? 1 : 0)}.");
    }

    internal void BeginProceduralRaid()
    {
        if (live || !connected || !proceduralBranch) return;
        furniture.Bind(rooms);
        Physics.SyncTransforms();
        live = true;
        active = this;
        portalViews = gameObject.AddComponent<PortalViews>();
        portalViews.Initialize(this);
        Debug.Log($"[Contour4Ring] Authored procedural branch ready: {rooms.Length} rooms, {bridges.Count} bridges, {portals.Count} portal pairs.");
    }

    private bool InBranch(Vector3 point) =>
        !proceduralBranch || branchBounds.Contains(ring.InverseTransformPoint(point));

    private void FixedUpdate()
    {
        gravityControllerBody = null;
        if (!connected || !levelRoot)
        {
            ReleaseJumpControl();
            return;
        }

        // Native item prefabs retain their own gravity switches. The correction changes the
        // direction only while the game allows gravity for that rigidbody.
        if (live && (scanTimer -= Time.fixedDeltaTime) <= 0)
        {
            scanTimer = .5f;
            foreach (var prop in FindObjectsByType<PhysGrabObject>(FindObjectsSortMode.None))
                if (prop.rb && !furniture.OwnsFront(prop.rb) && InBranch(prop.rb.worldCenterOfMass) && !bodies.ContainsKey(prop.rb))
                    bodies.Add(prop.rb, Vector3.up);
            foreach (var cart in FindObjectsByType<PhysGrabCart>(FindObjectsSortMode.None))
                if (InBranch(cart.transform.position)) cartSeams.Register(cart);
        }

        var controller = live && MainRaid ? PlayerController.instance : null;
        Rigidbody player = controller ? controller.rb : null;

        if (player && InBranch(player.position))
        {
            UpdateLandingCamera(player);
            bool landedTumbling = TumbleOnJumpLanding(controller);
            bool usedDoor = false;
            Vector3 canonical = ring.InverseTransformPoint(player.worldCenterOfMass);
            if (!landedTumbling && !player.isKinematic && portalDoorDebris.NeedsCheck())
                foreach (var portal in portals)
                {
                    PortalEnd a = portal.A, b = portal.B;
                    Vector3 observer = player.worldCenterOfMass;
                    portalDoorDebris.ClearNear(observer, a.Center, a.Orientation, a.Width * .5f, a.Height * .5f, b.Center, b.Orientation);
                    portalDoorDebris.ClearNear(observer, b.Center, b.Orientation, b.Width * .5f, b.Height * .5f, a.Center, a.Orientation);
                }
            if (!landedTumbling && !player.isKinematic && TryCross(player, canonical, out var crossing, true))
            {
                AdjustHeldTransfer(player, player, crossing);
                jumpFieldOffsetInRing = null;
                usedDoor = !crossing.Exit.Upper;
                if (usedDoor)
                {
                    playerUpInRing = ring.InverseTransformDirection(crossing.Exit.Room.up);
                    gravityTurnSpeed = 0;
                }
                occupiedBridge = null;
            }

            if (CheckBridgeFall(player))
            {
                ReleaseJumpControl();
                return;
            }
            Vector3 nextUp = FindUp(player.position, JumpGravitySample(player), playerUpInRing, out bool onBridge);
            if (!landedTumbling && !UpdateJumpGravity(controller, nextUp, onBridge, usedDoor))
                AlignPlayerGravity(player, nextUp, onBridge);
            playerUpInRing = nextUp;
            playerGravityUp = ring.TransformDirection(nextUp);
            float gravityLength = playerGravityUp.sqrMagnitude;
            if (gravityLength > .99f && gravityLength < 1.01f)
            {
                gravityControllerBody = player;
                gravitySampleTime = Time.fixedTimeAsDouble;
            }
        }
        else
        {
            if (proceduralBranch && player)
            {
                if (CheckBridgeFall(player)) return;
                lastCenters.Remove(player);
                playerUpInRing = Vector3.up;
                playerGravityUp = Vector3.up;
            }
            if (jumpTurn != JumpTurn.None || jumpBody || jumpCameraRoot || tumbleUpperExit != null)
                ReleaseJumpControl();
        }

        if (live)
            cartSeams.PhysicsStep(CrossedWhileLoose, (body, crossing) =>
            {
                cartCargoSamples.Remove(body);
                lastCenters[body] = ring.InverseTransformPoint(body.worldCenterOfMass);
                exitThresholds[body] = crossing.Exit;
                bodies[body] = ring.InverseTransformDirection(crossing.Exit.Room.up);
                if (player)
                    AdjustHeldTransfer(body, player, crossing);
            });

        bodyBuffer.Clear();
        bodyBuffer.AddRange(bodies.Keys);
        foreach (var body in bodyBuffer)
        {
            if (!body)
            {
                bodies.Remove(body);
                lastCenters.Remove(body);
                exitThresholds.Remove(body);
                cartCargoSamples.Remove(body);
                continue;
            }
            if (body.isKinematic || body == player)
            {
                cartCargoSamples.Remove(body);
                continue;
            }
            if (!InBranch(body.worldCenterOfMass))
            {
                bodies.Remove(body);
                lastCenters.Remove(body);
                exitThresholds.Remove(body);
                cartCargoSamples.Remove(body);
                cartSeams.SetSupportAxis(body, Vector3.up);
                continue;
            }

            Vector3 center = ring.InverseTransformPoint(body.worldCenterOfMass);
            PortalEnd support = null;
            bool inCartSeam = live && cartSeams.TryGetSupport(body, out support);
            if (inCartSeam)
            {
                if (!cartSeams.IsCart(body))
                    TrackCartCargoCrossing(body, center, support);
                lastCenters[body] = center;
            }
            if (live && !inCartSeam && TryCross(body, center, out var crossing))
            {
                if (player)
                    AdjustHeldTransfer(body, player, crossing);
                bodies[body] = ring.InverseTransformDirection(crossing.Exit.Room.up);
            }
            Vector3 up = inCartSeam ? ring.InverseTransformDirection(support.Room.up)
                : FindUp(body.position, body.worldCenterOfMass, bodies[body], out _);
            bodies[body] = up;
            cartSeams.SetSupportAxis(body, ring.TransformDirection(up));
            if (!body.useGravity)
                continue;
            ApplyGravityCorrection(body, ring.TransformDirection(up));
        }
    }

    private void OnDisable()
    {
        if (furniture.ReleaseHeld()) ClearHeldTransfer(PhysGrabber.instance);
        ReleaseJumpControl();
    }

    private void OnDestroy()
    {
        if (active == this)
            active = null;
        ReleaseJumpControl();
        if (jumpContacts)
            Destroy(jumpContacts);
        cartSeams?.Dispose();
        looseSeams.Dispose();
    }

    internal void RefreshPortalImages()
    {
        cartSeams.RefreshVisuals();
        looseSeams.Refresh(bodies.Keys, lastCenters, ring, doors, cartSeams);
    }

    internal static float NearCorner(Camera camera)
    {
        Matrix4x4 clipToCamera = camera.projectionMatrix.inverse;
        float farthest = 0;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
            farthest = Mathf.Max(farthest, clipToCamera.MultiplyPoint(new Vector3(x, y, -1)).sqrMagnitude);
        return Mathf.Sqrt(farthest);
    }

    internal Crossing? CameraSide(Vector3 eye, float nearCorner)
    {
        foreach (var door in doors)
        {
            float depth = door.Entry.DistanceFromPlane(eye);
            if (depth > 0 && depth <= nearCorner + .12f && door.Entry.InAperture(eye))
                return door;
        }
        return null;
    }

    internal bool TraceGrab(Vector3 origin, Vector3 direction, out RaycastHit hit, float range, int mask,
        QueryTriggerInteraction triggers, out Crossing? mapped, out Vector3 pullOrigin)
    {
        Camera camera = SemiFunc.MainCamera();
        float nearCorner = NearCorner(camera);
        Vector3 renderedEye = portalViews.UnshiftedEye(camera);
        // The native VisionTransform can lag behind the rendered eye at a jamb.
        var eyeDoor = CameraSide(renderedEye, nearCorner);
        if (eyeDoor?.Entry != CameraSide(origin, nearCorner)?.Entry)
            origin = renderedEye;
        pullOrigin = origin;
        if (eyeDoor is Crossing eyeCrossing)
        {
            origin = eyeCrossing.TransformPoint(origin);
            direction = eyeCrossing.Rotation * direction;
        }
        mapped = eyeDoor;

        Crossing? traversed = null;
        float reach = range;
        foreach (var door in doors)
        {
            if (door.Entry.TryRay(origin, direction, reach, out float distance))
            {
                traversed = door;
                reach = distance;
            }
        }

        bool local = Physics.Raycast(origin, direction, out hit, reach, mask, triggers);
        if (looseSeams.TryHitExposed(origin, direction, local ? hit.distance : reach, mask, triggers,
            out var exposed, out var exposedCrossing))
        {
            hit = exposed;
            mapped = ResolveGrabSide(eyeDoor, exposedCrossing);
            return true;
        }
        if (traversed is not Crossing crossing)
            return local;

        if (local)
            return true;

        // Only the residual segment goes through the opening. Exposed hits are tested above.
        Vector3 remoteOrigin = crossing.TransformPoint(origin + direction * reach);
        Vector3 remoteDirection = crossing.Rotation * direction;
        // A stock ray ignores a collider enclosing its origin. Test containment at the
        // artificial segment start so an obstruction spanning the exit cannot be skipped.
        hit = default;
        foreach (var collider in Physics.OverlapSphere(remoteOrigin, .001f, mask, triggers))
            if (collider is BoxCollider or SphereCollider or CapsuleCollider or MeshCollider { convex: true } &&
                (collider.ClosestPoint(remoteOrigin) - remoteOrigin).sqrMagnitude < 1e-10f)
                return false;
        if (!Physics.Raycast(remoteOrigin, remoteDirection, out hit, range - reach, mask, triggers))
            return false;
        hit.distance += reach;
        mapped = ResolveGrabSide(eyeDoor, crossing);
        return true;
    }

    private static Crossing? ResolveGrabSide(Crossing? eyeDoor, Crossing hitDoor) =>
        eyeDoor.HasValue && eyeDoor.Value.Exit == hitDoor.Entry ? null : hitDoor;

    private bool CheckBridgeFall(Rigidbody player)
    {
        var avatar = SemiFunc.PlayerAvatarLocal();
        if (!avatar)
            return false;
        if (fallenPlayer == avatar)
            return true;
        Rigidbody movingBody = player;
        if ((bool)IsTumbling.GetValue(avatar) && AvatarTumble.GetValue(avatar) is PlayerTumble tumble &&
            TumbleBody.GetValue(tumble) is PhysGrabObject ragdoll && ragdoll.rb)
            movingBody = ragdoll.rb;

        Vector3 worldPosition = movingBody.position;
        foreach (var bridge in bridges)
        {
            if (!bridge.TryGetUp(bridge.Root.InverseTransformPoint(worldPosition), out var localUp))
                continue;
            occupiedBridge = bridge;
            bridgeFooting = ring.InverseTransformPoint(bridge.SafeDeckPoint(localUp));
            bridgeUp = ring.InverseTransformDirection(bridge.Root.TransformDirection(localUp));
            return false;
        }
        if (occupiedBridge == null)
            return false;
        if (rooms.Any(room => IsInRoom(room.InverseTransformPoint(worldPosition))))
        {
            occupiedBridge = null;
            return false;
        }

        var offset = ring.InverseTransformPoint(worldPosition) - bridgeFooting;
        if (Vector3.Dot(offset, bridgeUp) > -3f)
            return false;
        fallenPlayer = avatar;
        Debug.Log($"[Contour4Ring] Fatal bridge fall on {occupiedBridge.Root.name}.");
        NativeDeath.Invoke(avatar, new object[] { -1 });
        return true;
    }

    internal void PlaceFallenHead(PlayerDeathHead head)
    {
        if (!fallenPlayer || HeadOwner.GetValue(head) as PlayerAvatar != fallenPlayer)
            return;
        Vector3 up = ring.TransformDirection(bridgeUp);
        Vector3 facing = Vector3.ProjectOnPlane(occupiedBridge.Root.right, up).normalized;
        Vector3 position = ring.TransformPoint(bridgeFooting) + up * .5f;
        HeadPosition.SetValue(head, position);
        HeadRotation.SetValue(head, Quaternion.LookRotation(facing, up));
        fallenPlayer = null;
        occupiedBridge = null;
        Debug.Log($"[Contour4Ring] Native death head returned to the bridge at {position:F2}.");
    }

    private static readonly System.Reflection.FieldInfo HeldObject =
        AccessTools.Field(typeof(PhysGrabber), "grabbedPhysGrabObject");
    private static readonly System.Reflection.FieldInfo PullingCart =
        AccessTools.Field(typeof(PhysGrabCart), "cartBeingPulled");
    private PhysGrabObject heldTransit;
    private Crossing? heldPull;
    private Vector3? mappedPullInRing;

    internal void BeginMappedHold(PhysGrabber grabber, PhysGrabObject held, Crossing crossing)
    {
        heldTransit = held;
        heldPull = crossing;
        mappedPullInRing = null;
        grabber.OverrideAlwaysGrabbable(.10f);
    }

    private void AdjustHeldTransfer(Rigidbody body, Rigidbody player, Crossing crossing)
    {
        var grabber = PhysGrabber.instance;
        var held = grabber && grabber.grabbed ? HeldObject.GetValue(grabber) as PhysGrabObject : null;
        if (!held || !held.rb || (body != player && body != held.rb))
            return;
        if (furniture.OwnsFront(held.rb))
        {
            grabber.ReleaseObject(-1);
            ClearHeldTransfer(grabber);
            return;
        }

        Crossing fromPlayer = body == player ? crossing.Reverse() : crossing;
        if (heldTransit == held && heldPull is Crossing previous &&
            previous.Entry == fromPlayer.Exit && previous.Exit == fromPlayer.Entry)
        {
            ClearHeldTransfer(grabber);
            return;
        }
        // The player can cross during physics, before the grabber updates its native puller.
        BeginMappedHold(grabber, held, fromPlayer);
    }

    internal void RemapHeldPull(PhysGrabber grabber)
    {
        if (heldPull is not Crossing crossing)
            return;
        var player = PlayerController.instance;
        if (!player || !player.rb || !grabber.physGrabPointPuller || !heldTransit || !heldTransit.rb || !grabber.grabbed ||
            HeldObject.GetValue(grabber) as PhysGrabObject != heldTransit)
        {
            ClearHeldTransfer(grabber);
            return;
        }

        Vector3 playerCenter = player.rb.worldCenterOfMass;
        Vector3 virtualPlayer = crossing.TransformPoint(playerCenter);
        Vector3 bodyCenter = heldTransit.rb.worldCenterOfMass;
        if (Vector3.Distance(playerCenter, crossing.Entry.Center) > grabber.grabReleaseDistance ||
            Vector3.Distance(bodyCenter, virtualPlayer) > grabber.grabReleaseDistance)
        {
            ClearHeldTransfer(grabber);
            return;
        }

        // Keep the game's puller transform untouched so each frame starts from native coordinates.
        Vector3 mapped = crossing.TransformPoint(grabber.physGrabPointPuller.position);
        grabber.physGrabPointPullerPosition = mapped;
        mappedPullInRing = ring.InverseTransformPoint(mapped);
        // Bypass the native straight-line range check without blocking release input.
        grabber.OverrideAlwaysGrabbable(.10f);
    }

    private void ClearHeldTransfer(PhysGrabber grabber)
    {
        if (mappedPullInRing.HasValue && grabber && grabber.physGrabPointPuller)
            grabber.physGrabPointPullerPosition = grabber.physGrabPointPuller.position;
        heldTransit = null;
        heldPull = null;
        mappedPullInRing = null;
    }

    internal bool SteerCartAcrossDoor(PhysGrabCart cart)
    {
        var grabber = PhysGrabber.instance;
        if (heldPull is not Crossing crossing || crossing.Exit.Upper || mappedPullInRing is not Vector3 mapped ||
            !grabber || !grabber.grabbed || !heldTransit || HeldObject.GetValue(grabber) as PhysGrabObject != heldTransit ||
            !heldTransit.rb || !cart.cartGrabPoint ||
            heldTransit.GetComponentInChildren<PhysGrabCart>() != cart)
            return false;

        var body = heldTransit.rb;
        Vector3 up = crossing.Exit.Room.up;
        Vector3 correction = Vector3.ProjectOnPlane(ring.TransformPoint(mapped) - cart.cartGrabPoint.position, up);
        Vector3 desired = Vector3.ClampMagnitude(correction * 2.4f, 4.5f);
        Vector3 planar = Vector3.ProjectOnPlane(body.linearVelocity, up);
        grabber.OverridePhysGrabForcesDisable(.10f);
        body.linearVelocity += Vector3.MoveTowards(planar, desired, Time.fixedDeltaTime * 8.0f) - planar;
        PullingCart.SetValue(cart, true);
        return true;
    }

    private Vector3 FindUp(Vector3 position, Vector3 center, Vector3 previous, out bool onBridge)
    {
        foreach (var bridge in bridges)
        {
            Vector3 localPoint = bridge.Root.InverseTransformPoint(position);
            if (!bridge.TryGetUp(localPoint, out Vector3 localUp))
                continue;
            onBridge = true;
            return ring.InverseTransformDirection(bridge.Root.TransformDirection(localUp));
        }
        onBridge = false;

        // Below the bottom room's truck door the native starter shares quota gravity.
        Vector3 quotaPosition = rooms[0].InverseTransformPoint(position);
        if (quotaPosition.z < -6 && Mathf.Abs(quotaPosition.x) < 10 && quotaPosition.y < 5)
            return Vector3.up;

        foreach (var gravity in roomGravity)
            if (gravity.ContainsRoom(position))
                return gravity.Up;

        // Retain the last field through a narrow band at a sloping face or the common tip.
        foreach (var gravity in roomGravity)
            if (Vector3.Dot(gravity.Up, previous) > .999f && gravity.ContainsPyramid(center, .12f))
                return gravity.Up;
        foreach (var gravity in roomGravity)
            if (gravity.ContainsPyramid(center, 0))
                return gravity.Up;
        return previous;
    }

    private void AlignPlayerGravity(Rigidbody player, Vector3 targetUp, bool onBridge)
    {
        const float maximumSpeed = 135f;
        const float acceleration = 540f;
        Vector3 target = ring.TransformDirection(targetUp);
        Vector3 axis = ring.TransformDirection(gravityTurnAxis);
        float angle = Vector3.SignedAngle(target, Vector3.up, axis);
        float remaining = Mathf.Abs(angle);
        if (remaining <= .08f)
        {
            gravityTurnSpeed = 0;
            return;
        }

        float elapsed = Time.fixedDeltaTime;
        // The adjacent deck samples already form a continuous gravity field.
        if (onBridge && remaining <= maximumSpeed * elapsed)
        {
            gravityTurnSpeed = 0;
            RotateLevel(Quaternion.AngleAxis(angle, axis), player);
            return;
        }

        // Opposite fields share the shaft axis, including the otherwise ambiguous half-turn.
        if (angle < -179.95f)
            angle = 180f;
        float speed = Mathf.Min(maximumSpeed, Mathf.Sqrt(2f * acceleration * remaining)) * Mathf.Sign(angle);
        gravityTurnSpeed = Mathf.MoveTowards(gravityTurnSpeed, speed, acceleration * elapsed);
        float step = gravityTurnSpeed * elapsed;
        if (Mathf.Sign(step) == Mathf.Sign(angle) && Mathf.Abs(step) >= remaining)
            step = angle;
        RotateLevel(Quaternion.AngleAxis(step, axis), player);
    }

    private static void ApplyGravityCorrection(Rigidbody body, Vector3 up)
    {
        Vector3 gravity = Physics.gravity;
        Vector3 correction = -up * gravity.magnitude - gravity;
        if (correction.sqrMagnitude > .002f)
            body.AddForce(correction, ForceMode.Acceleration);
    }

    private bool OwnsDynamicPlayerGravity(Rigidbody body)
    {
        if (!isActiveAndEnabled || !MainRaid || !body || body != gravityControllerBody || body.isKinematic ||
            Time.fixedTimeAsDouble != gravitySampleTime)
            return false;
        var avatar = SemiFunc.PlayerAvatarLocal();
        return avatar && !(bool)IsTumbling.GetValue(avatar);
    }

    [HarmonyPatch(typeof(PlayerController), "FixedUpdate")]
    private static class NativeGravity
    {
        private static bool bound;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var custom = AccessTools.Field(typeof(PlayerController), "CustomGravity");
            var addForce = AccessTools.Method(typeof(Rigidbody), nameof(Rigidbody.AddForce),
                new[] { typeof(Vector3), typeof(ForceMode) });
            var vector = AccessTools.Constructor(typeof(Vector3), new[] { typeof(float), typeof(float), typeof(float) });
            var timestep = AccessTools.PropertyGetter(typeof(Time), nameof(Time.fixedDeltaTime));
            var reorient = AccessTools.Method(typeof(NativeGravity), nameof(ApplyFallImpulse));
            var sites = new List<(int Index, bool Reduced)>();
            bool FloatAt(int index, float expected) => code[index].opcode == OpCodes.Ldc_R4 &&
                code[index].operand is float value && value == expected;

            // Match new Vector3(0, -CustomGravity * [0.1 *] fixedDeltaTime, 0) and Impulse.
            for (int start = 0; start + 9 < code.Count; start++)
            {
                if (!FloatAt(start, 0f) || code[start + 1].opcode != OpCodes.Ldarg_0 || !code[start + 2].LoadsField(custom))
                    continue;
                int negate = start + 3;
                bool reduced = FloatAt(negate, .1f) && code[negate + 1].opcode == OpCodes.Mul;
                if (reduced)
                    negate += 2;
                int call = negate + 6;
                if (call < code.Count && code[negate].opcode == OpCodes.Neg && code[negate + 1].Calls(timestep) &&
                    code[negate + 2].opcode == OpCodes.Mul && FloatAt(negate + 3, 0f) &&
                    code[negate + 4].opcode == OpCodes.Newobj && Equals(code[negate + 4].operand, vector) &&
                    code[negate + 5].opcode == OpCodes.Ldc_I4_1 && code[call].Calls(addForce))
                    sites.Add((call, reduced));
            }
            bound = sites.Count == 2 && sites[0].Reduced != sites[1].Reduced;
            if (!bound)
            {
                Debug.LogError("[Contour4Ring] Native fall impulse instructions differ from R.E.P.O. 0.5.2; stock player gravity retained.");
                return code;
            }
            foreach (var (index, _) in sites)
            {
                code[index].opcode = OpCodes.Call;
                code[index].operand = reorient;
            }
            NativeJumpMotion.Bind(code);
            return code;
        }

        // Native grounded, jump and low-gravity decisions still control when the impulse is applied.
        private static void ApplyFallImpulse(Rigidbody body, Vector3 force, ForceMode mode)
        {
            var active = Active;
            if (active && active.OwnsDynamicPlayerGravity(body) && body.useGravity)
                force = active.playerGravityUp * force.y;
            body.AddForce(force, mode);
        }

        private static void Prefix(PlayerController __instance, out bool __state)
        {
            var active = Active;
            // A kinematic controller can be made dynamic by the native early return.
            __state = bound && active && __instance == PlayerController.instance && active.OwnsDynamicPlayerGravity(__instance.rb);
        }

        private static void Postfix(PlayerController __instance, bool __state)
        {
            var active = Active;
            Rigidbody body = __instance.rb;
            if (__state && active && active.OwnsDynamicPlayerGravity(body) && body.useGravity)
                ApplyGravityCorrection(body, active.playerGravityUp);
        }
    }

    private static bool IsInRoom(Vector3 point) => Mathf.Abs(point.x) <= 6.06f &&
        Mathf.Abs(point.z) <= 6.06f && point.y >= -.75f && point.y < 5.7f;

    private void TrackCartCargoCrossing(Rigidbody body, Vector3 current, PortalEnd support)
    {
        Vector3 center = ring.TransformPoint(current);
        Crossing? pending = null;
        if (cartCargoSamples.TryGetValue(body, out var sample) && sample.Support == support &&
            support.DistanceFromPlane(center) >= 0 && support.InAperture(center) &&
            lastCenters.TryGetValue(body, out Vector3 previous) && (current - previous).sqrMagnitude <= 25f)
        {
            // A seated load stays in the cart frame after its center has crossed the doorway.
            pending = sample.Pending;
            if (!pending.HasValue && support.Crossed(ring.TransformPoint(previous), center))
                pending = doors.Find(door => door.Entry == support);
        }
        cartCargoSamples[body] = (support, pending);
    }

    private bool CrossedWhileLoose(Rigidbody body, PortalEnd support)
    {
        // Preserve a real loose crossing when an item overlaps the basket for the first time this step.
        if ((cartCargoSamples.TryGetValue(body, out var sample) && sample.Support == support) ||
            !lastCenters.TryGetValue(body, out Vector3 previous))
            return false;
        Vector3 finish = body.worldCenterOfMass;
        if ((ring.InverseTransformPoint(finish) - previous).sqrMagnitude > 25f)
            return false;
        Vector3 start = ring.TransformPoint(previous);
        return FindCrossing(body, start, finish, CrossingBlock(body, start, finish, false), false, out _);
    }

    private PortalEnd CrossingBlock(Rigidbody body, Vector3 start, Vector3 finish, bool isPlayer)
    {
        exitThresholds.TryGetValue(body, out var blockedEnd);
        if (blockedEnd != null && ((blockedEnd == tumbleUpperExit && (isPlayer || body == landingRagdoll)) ||
            (isPlayer && blockedEnd == airborneUpperExit && jumpBody == body)))
            return blockedEnd;
        // The rebased player sample starts inside the exit. A new outward sweep can return immediately.
        if (isPlayer && blockedEnd?.Upper == false && blockedEnd.Crossed(start, finish))
            return null;

        // Loose props can reverse direction immediately after a mapped pull.
        if (blockedEnd != null && (blockedEnd.Upper || isPlayer
            ? blockedEnd.DistanceFromPlane(finish) < -.45f
            : blockedEnd.DistanceFromPlane(start) < 0))
            return null;
        return blockedEnd;
    }

    private bool FindCrossing(Rigidbody body, Vector3 start, Vector3 finish, PortalEnd blockedEnd, bool isPlayer, out Crossing crossing)
    {
        foreach (var portal in portals)
        {
            if (!portal.A.Upper && !isPlayer && cartSeams.IsCart(body))
                continue;
            if (portal.TryCross(start, finish, blockedEnd, out crossing))
                return true;
        }
        crossing = default;
        return false;
    }

    private bool TryCross(Rigidbody body, Vector3 current, out Crossing crossing, bool isPlayer = false)
    {
        cartCargoSamples.Remove(body, out var cargoSample);
        bool previousKnown = lastCenters.TryGetValue(body, out Vector3 previous);
        lastCenters[body] = current;
        crossing = default;
        if (!previousKnown || (current - previous).sqrMagnitude > 25f)
            return false;

        Vector3 start = ring.TransformPoint(previous);
        Vector3 finish = ring.TransformPoint(current);
        PortalEnd blockedEnd = CrossingBlock(body, start, finish, isPlayer);
        if (blockedEnd == null)
            exitThresholds.Remove(body);

        if (!FindCrossing(body, start, finish, blockedEnd, isPlayer, out crossing))
        {
            // A released load can complete its recorded crossing through the same opening.
            if (cargoSample.Pending is not Crossing cargoCrossing || cargoCrossing.Entry == blockedEnd ||
                cargoCrossing.Entry.DistanceFromPlane(finish) < 0 || !cargoCrossing.Entry.InAperture(finish))
                return false;
            crossing = cargoCrossing;
        }

        if (isPlayer && jumpTurn == JumpTurn.Turning)
        {
            AbortJumpControl(PlayerController.instance, SemiFunc.PlayerAvatarLocal());
            finish = ring.TransformPoint(current);
        }

        var turn = crossing.Rotation;
        var center = crossing.TransformPoint(finish);
        bool playerDoor = isPlayer && !crossing.Exit.Upper;
        if (!playerDoor)
            center -= crossing.Exit.Normal * (crossing.Exit.Upper ? .055f : .001f);
        var centerOffset = body.worldCenterOfMass - body.position;
        Vector3 velocity = turn * body.linearVelocity;
        body.linearVelocity = isPlayer && crossing.Exit.Upper ? AirborneUpperVelocity(body, crossing, velocity) : velocity;
        body.angularVelocity = turn * body.angularVelocity;
        if (playerDoor)
        {
            // Rebase the exit around the current player so the stock camera follows without a teleport.
            RotateLevel(Quaternion.Inverse(turn), body, center, finish);
        }
        else if (isPlayer)
        {
            var exit = crossing.Exit;
            center = exit.FindLandingCenter(center, centerOffset.y);
            // Put the exit beneath the unchanged controller and let the new field turn gravity.
            RotateLevel(Quaternion.identity, body, center, finish);
        }
        else
        {
            Vector3 position = center - turn * centerOffset;
            Quaternion rotation = turn * body.rotation;
            body.transform.SetPositionAndRotation(position, rotation);
            body.position = position;
            body.rotation = rotation;
            Physics.SyncTransforms();
        }
        lastCenters[body] = ring.InverseTransformPoint(isPlayer ? finish : center);
        exitThresholds[body] = crossing.Exit;
        Debug.Log($"[Contour4Ring] {body.name} crossed {crossing.Entry.Room.name} -> {crossing.Exit.Room.name}.");
        return true;
    }

    private void RotateLevel(Quaternion delta, Rigidbody player, Vector3? pivotOverride = null, Vector3? destinationOverride = null)
    {
        Vector3 pivot = pivotOverride ?? player.position;
        Vector3 destination = destinationOverride ?? pivot;
        Vector3 newRoot = destination + delta * (levelRoot.position - pivot);
        Quaternion newRotation = delta * levelRoot.rotation;
        bodyBuffer.Clear();
        bodyBuffer.AddRange(bodies.Keys);
        foreach (var body in bodyBuffer)
        {
            if (!body || body == player)
                continue;
            bodyMoves.Add(new BodyMove(body, pivot, destination, delta));
        }
        foreach (var body in furniture.RebaseBodies)
            if (body) bodyMoves.Add(new BodyMove(body, pivot, destination, delta));

        levelRoot.SetPositionAndRotation(newRoot, newRotation);
        foreach (var move in bodyMoves)
            move.Apply();
        cartSeams.ReorientNativeHistory(pivot, destination, delta);
        bodyMoves.Clear();
        if (!player.isKinematic)
            player.linearVelocity = delta * player.linearVelocity;
        Physics.SyncTransforms();
    }

    private readonly List<BodyMove> bodyMoves = new();

    private readonly struct BodyMove
    {
        private readonly Rigidbody body;
        private readonly Vector3 position;
        private readonly Quaternion rotation;
        private readonly Vector3 velocity;
        private readonly Vector3 spin;

        internal BodyMove(Rigidbody body, Vector3 pivot, Vector3 destination, Quaternion delta)
        {
            this.body = body;
            position = destination + delta * (body.position - pivot);
            rotation = delta * body.rotation;
            velocity = delta * body.linearVelocity;
            spin = delta * body.angularVelocity;
        }

        internal void Apply()
        {
            // The stock cart measures its motion from the Transform during this same physics tick.
            body.transform.SetPositionAndRotation(position, rotation);
            body.position = position;
            body.rotation = rotation;
            if (body.isKinematic)
                return;
            body.linearVelocity = velocity;
            body.angularVelocity = spin;
        }
    }

    internal Vector2 MapPoint(Vector3 worldPosition, Func<int, Vector3, Vector2> project)
    {
        for (int index = 0; index < bridges.Count; index++)
        {
            var bridge = bridges[index];
            Vector3 local = bridge.Root.InverseTransformPoint(worldPosition);
            if (!bridge.TryGetUp(local, out var up))
                continue;
            float t = Vector3.Angle(up, Vector3.up) / 90f;
            return Vector2.Lerp(project(index, new Vector3(6, 0, 0)),
                project(index + 1, new Vector3(-6, 0, 0)), t);
        }

        Vector3 quota = rooms[0].InverseTransformPoint(worldPosition);
        if (quota.z < -6 && Mathf.Abs(quota.x) < 6 && quota.y < 5)
        {
            float depth = Mathf.Clamp01((-quota.z - 6f) / 19f);
            return project(0, new Vector3(Mathf.Lerp(-6f, -13.5f, depth), 0, Mathf.Clamp(quota.x, -2f, 2f)));
        }

        int best = 0;
        float score = float.PositiveInfinity;
        Vector3 projected = Vector3.zero;
        for (int i = 0; i < rooms.Length; i++)
        {
            Vector3 p = rooms[i].InverseTransformPoint(worldPosition);
            Vector3 floor = new(Mathf.Clamp(p.x, -6, 6), 0, Mathf.Clamp(p.z, -6, 6));
            float distance = (p - floor).sqrMagnitude;
            if (distance >= score)
                continue;
            score = distance;
            projected = floor;
            best = i;
        }
        return project(best, projected);
    }

    internal static Transform Marker(Transform room, string name) => room.Find("Markers/" + name);

    private sealed class RoomGravity
    {
        internal Vector3 Up { get; }
        private readonly Transform room;
        private readonly Vector3 baseCenter;
        private readonly Vector3 tip;
        private readonly Vector3 halfSize;
        private readonly Collider floor;
        private readonly Collider ramp;

        internal RoomGravity(Transform room, Transform ring, Vector3 shaftCenter)
        {
            this.room = room;
            Collider[] solids = room.Find("Model").GetComponentsInChildren<Collider>();
            floor = solids.Single(collider => collider.name == room.name + "__Floor");
            ramp = solids.FirstOrDefault(collider => collider.name == room.name + "__Upper_Ramp");
            Up = ring.InverseTransformDirection(room.up);
            var volume = Marker(room, room.name + "__RoomVolume").GetComponent<BoxCollider>();
            halfSize = volume.size * .5f;
            baseCenter = room.InverseTransformPoint(volume.transform.TransformPoint(volume.center)) + Vector3.up * halfSize.y;
            tip = room.InverseTransformPoint(shaftCenter);
        }

        internal bool IsLandingSurface(Collider candidate) => candidate && (candidate == floor || candidate == ramp);

        internal bool ContainsRoom(Vector3 point)
        {
            Vector3 local = room.InverseTransformPoint(point) - baseCenter;
            return local.y <= 0 && local.y >= -2f * halfSize.y - .75f &&
                Mathf.Abs(local.x) <= halfSize.x + .06f && Mathf.Abs(local.z) <= halfSize.z + .06f;
        }

        internal bool ContainsPyramid(Vector3 point, float margin)
        {
            Vector3 local = room.InverseTransformPoint(point);
            if (local.y < baseCenter.y - margin || local.y > tip.y + margin)
                return false;
            float t = Mathf.InverseLerp(baseCenter.y, tip.y, local.y);
            Vector3 section = Vector3.Lerp(baseCenter, tip, t);
            return Mathf.Abs(local.x - section.x) <= halfSize.x * (1f - t) + margin &&
                Mathf.Abs(local.z - section.z) <= halfSize.z * (1f - t) + margin;
        }
    }

    private sealed class Bridge
    {
        internal Transform Root { get; }
        private readonly Vector3 start;
        private readonly Vector3 end;
        private readonly float radius;
        private readonly float sign;

        internal Bridge(Transform root)
        {
            Root = root;
            start = Marker(root, root.name + "__Start").localPosition;
            end = Marker(root, root.name + "__End").localPosition;
            radius = Mathf.Abs(end.z - start.z);
            sign = Mathf.Sign(end.y - start.y);
            InstallSafetyBarriers();
        }

        private void InstallSafetyBarriers()
        {
            const int segments = 32;
            const float halfWidth = 2.05f;
            const float thickness = .08f;
            float height = 4f / Root.lossyScale.y;
            float railHeight = .85f / Root.lossyScale.y;
            var barriers = new GameObject("Invisible bridge barriers").transform;
            barriers.SetParent(Root, false);

            for (int i = 0; i < segments; i++)
            {
                float angleA = i * Mathf.PI / (2f * segments);
                float angleB = (i + 1) * Mathf.PI / (2f * segments);
                Vector3 a = Point(angleA);
                Vector3 b = Point(angleB);
                float angle = (angleA + angleB) / 2f;
                Vector3 up = new Vector3(0f, Mathf.Cos(angle), -sign * Mathf.Sin(angle));
                Quaternion rotation = Quaternion.LookRotation((b - a).normalized, up);
                float length = Vector3.Distance(a, b) + thickness;
                Vector3 center = (a + b) / 2f;
                Add("left", center + rotation * new Vector3(-halfWidth - thickness / 2f,
                    (height + railHeight) / 2f, 0f), rotation,
                    new Vector3(thickness, height - railHeight, length));
                Add("right", center + rotation * new Vector3(halfWidth + thickness / 2f,
                    (height + railHeight) / 2f, 0f), rotation,
                    new Vector3(thickness, height - railHeight, length));
                Add("ceiling", center + up * (height + thickness / 2f), rotation,
                    new Vector3(2f * (halfWidth + thickness), thickness, length));
            }

            Vector3 Point(float angle) => start + new Vector3(0f,
                sign * radius * (1f - Mathf.Cos(angle)), radius * Mathf.Sin(angle));

            void Add(string name, Vector3 center, Quaternion rotation, Vector3 size)
            {
                var node = new GameObject(name);
                node.layer = Root.gameObject.layer;
                node.transform.SetParent(barriers, false);
                node.transform.localPosition = center;
                node.transform.localRotation = rotation;
                node.AddComponent<BoxCollider>().size = size;
            }
        }

        internal Vector3 SafeDeckPoint(Vector3 up)
        {
            float angle = Mathf.Clamp(Vector3.Angle(up, Vector3.up), 8f, 82f) * Mathf.Deg2Rad;
            var point = start + new Vector3(0, sign * radius * (1f - Mathf.Cos(angle)), radius * Mathf.Sin(angle));
            return Root.TransformPoint(point);
        }

        internal bool TryGetUp(Vector3 point, out Vector3 up)
        {
            up = Vector3.up;
            Vector3 local = point - start;
            if (Mathf.Abs(local.x) > 2.05f || local.z < -.035f || local.y * sign > radius + .035f)
                return false;

            Vector2 radial = new(local.z, radius - local.y * sign);
            float angle = Vector2.SignedAngle(Vector2.up, radial) * -1f;
            if (angle < -.45f || angle > 90.45f || radial.magnitude > radius + .35f || radial.magnitude < radius - 2.55f)
                return false;
            float radians = Mathf.Clamp(angle, 0f, 90f) * Mathf.Deg2Rad;
            up = new Vector3(0, Mathf.Cos(radians), -sign * Mathf.Sin(radians));
            return true;
        }
    }
}

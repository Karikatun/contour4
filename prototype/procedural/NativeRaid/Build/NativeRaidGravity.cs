using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using HarmonyLib;
using UnityEngine;

namespace Contour4.NativeRaid;

// The controller and camera keep the stock world-up frame. The freight deck rolls around
// the player, preserving positions and velocities relative to the rest of the scene.
[DefaultExecutionOrder(-150)]
internal sealed partial class NativeRaidGravity : MonoBehaviour
{
    private static readonly FieldInfo CartBody = AccessTools.Field(typeof(PhysGrabCart), "rb");
    private static readonly FieldInfo ActualLast = AccessTools.Field(typeof(PhysGrabCart), "actualVelocityLastPosition");
    private static readonly FieldInfo LastPosition = AccessTools.Field(typeof(PhysGrabCart), "lastPosition");
    private static readonly AccessTools.FieldRef<PhysGrabCart, Vector3> ActualVelocity =
        AccessTools.FieldRefAccess<PhysGrabCart, Vector3>("actualVelocity");
    private static NativeRaidGravity active;
    private RaidAssembly raid;
    private Transform levelRoot;
    private readonly List<Rigidbody> actors = new();
    private readonly HashSet<Rigidbody> newRibbonLoot = new();
    private readonly Dictionary<Rigidbody, PhysGrabCart> carts = new();
    private readonly RaidWorldRebase stage = new();
    private readonly SphereGravityReorientation sphereOrientation = new();
    private BakedRoom ribbonRoom;
    private MobiusContactRelay ribbonContacts;
    private Rigidbody ribbonObserver;
    private BakedRoom sphericalRoom;
    private Rigidbody sphericalObserver;
    private float sphericalFallImpulse;
    private double sphericalFallStep = double.NegativeInfinity;
    private float nextActorScan;
    private bool hasMobiusHall;
    private bool hasSphericalChamber;
    private bool actorsReady;

    internal static void Attach(RaidAssembly raid)
    {
        var gravity = raid.gameObject.AddComponent<NativeRaidGravity>();
        gravity.raid = raid;
        gravity.levelRoot = raid.transform.root;
        gravity.hasMobiusHall = raid.Rooms.Any(room => room.Prepared && room.Plan.Module.Id == MobiusHall.Id);
        gravity.hasSphericalChamber = raid.Rooms.Any(room => room.Prepared && SphericalChamber.IsVariant(room.Plan.Module.Id));
        raid.gameObject.AddComponent<NativeRaidActorGravityStep>().Owner = gravity;
        raid.gameObject.AddComponent<NativeRaidSphericalGravityStep>().Owner = gravity;
        active = gravity;
    }

    private void OnEnable()
    {
        if (raid) active = this;
    }

    private void OnDisable()
    {
        if (active == this) active = null;
        ClearNativeJump();
        ClearRibbon();
        ClearSphericalOrientation();
        sphericalFallStep = double.NegativeInfinity;
        carts.Clear();
        actors.Clear();
        newRibbonLoot.Clear();
        nextActorScan = 0f;
        actorsReady = false;
        rotaryWithoutController = false;
        requestedCabin = null;
    }

    private void FixedUpdate()
    {
        var controller = PlayerController.instance;
        bool levelReady = raid.Context != null && SemiFunc.LevelGenDone();
        actorsReady = levelReady && controller && controller.rb;
        rotaryWithoutController = levelReady && !actorsReady && rotaryCabin;
        if (!actorsReady)
        {
            ClearNativeJump();
            if (rotaryWithoutController)
            {
                RefreshActors(null);
                StepRotaryCabin(null);
            }
            return;
        }
        var player = controller.rb;
        jumpSampled = false;
        bool onRing = raid.ContainsRing(player.position);
        if (jumpBody && (!ValidJump(controller) || onRing)) ClearNativeJump();
        if (StepRotaryCabin(player))
        {
            ClearNativeJump();
            ClearRibbon();
            ClearSphericalOrientation();
        }
        else if (onRing)
        {
            ClearRibbon();
            ClearSphericalOrientation();
        }
        else if (!UpdateRibbon(player) && !UpdateSphericalOrientation(player) && raid.TryUp(JumpSample(player), out Vector3 up) &&
            !HoldNativeJump(player, up, IsJumpBridge(JumpSample(player))) && Vector3.Dot(up, Vector3.up) < .999999f)
        {
            Quaternion turn = Quaternion.FromToRotation(up, Vector3.up);
            Vector3 velocity = player.linearVelocity;
            Vector3 spin = player.angularVelocity;
            Rebase(player, player.position, turn);
            // The stock controller keeps its upright pose while the deck changes frame.
            // Transport free motion with that frame, as the ring, sphere and ribbon do.
            if (!player.isKinematic)
            {
                player.linearVelocity = turn * velocity;
                player.angularVelocity = turn * spin;
            }
        }
        if (jumpBody && !jumpSampled) ClearNativeJump();
        RefreshActors(player);
        // Stock releases its kinematic spawn bodies during generation. Keep newly spawned
        // ribbon items in the fixed step even before the periodic scene scan finds them.
        foreach (Rigidbody item in newRibbonLoot)
            if (item && !actors.Contains(item)) actors.Add(item);
        newRibbonLoot.Clear();
    }

    internal void ApplyDeferredActorGravity()
    {
        if (!actorsReady && !rotaryWithoutController)
        {
            foreach (Rigidbody item in newRibbonLoot) ApplyActorGravity(item, ribbonOnly: true);
            return;
        }
        foreach (Rigidbody actor in actors) ApplyActorGravity(actor);
    }

    private bool UpdateSphericalOrientation(Rigidbody player)
    {
        if (!hasSphericalChamber || !raid.TrySphericalUp(JumpSample(player), Vector3.up, out var up, out var room))
        {
            ClearSphericalOrientation();
            return false;
        }
        if (sphericalRoom != room || sphericalObserver != player)
        {
            sphereOrientation.Reset();
            sphericalRoom = room;
            sphericalObserver = player;
        }
        if (HoldNativeJump(player, up, IsJumpBridge(JumpSample(player)))) return true;
        if (Vector3.Dot(up, Vector3.up) < .9999999f)
        {
            Quaternion turn = sphereOrientation.Step(up, room.Root.forward, Time.fixedDeltaTime);
            Vector3 velocity = player.linearVelocity;
            Vector3 spin = player.angularVelocity;
            Rebase(player, player.position, turn);
            if (!player.isKinematic)
            {
                player.linearVelocity = turn * velocity;
                player.angularVelocity = turn * spin;
            }
        }
        return true;
    }

    private void ClearSphericalOrientation()
    {
        sphereOrientation.Reset();
        sphericalRoom = null;
        sphericalObserver = null;
    }

    internal static Vector3 CaptureFallImpulse(Vector3 force, PlayerController controller)
    {
        var gravity = active;
        if (!gravity || (!gravity.hasSphericalChamber && !gravity.jumpWaiting) || !gravity.actorsReady || controller != PlayerController.instance)
            return force;

        double step = Time.fixedTimeAsDouble;
        if (gravity.sphericalFallStep != step)
        {
            gravity.sphericalFallStep = step;
            gravity.sphericalFallImpulse = 0;
        }
        gravity.sphericalFallImpulse += force.y;
        return force;
    }

    internal void ApplySphericalPlayerGravity()
    {
        if (!actorsReady) return;
        var controller = PlayerController.instance;
        if (!controller || !controller.rb || controller.rb.isKinematic || !controller.rb.useGravity) return;
        Rigidbody player = controller.rb;
        if (raid.ContainsRing(player.position)) return;
        Vector3 up;
        if (jumpWaiting && jumpBody == player && ValidJump(controller)) up = jumpUp;
        else if (!raid.TrySphericalUp(player.position, Vector3.up, out up, out _)) return;

        Vector3 correction = SphereGravityReorientation.ForceCorrection(up, Physics.gravity);
        if (correction.sqrMagnitude > .00001f) player.AddForce(correction, ForceMode.Acceleration);
        if (sphericalFallStep == Time.fixedTimeAsDouble)
        {
            Vector3 impulse = SphereGravityReorientation.FallImpulseCorrection(up, sphericalFallImpulse);
            if (impulse.sqrMagnitude > .00000001f) player.AddForce(impulse, ForceMode.Impulse);
        }
    }

    internal static void TrackRibbonSpawn(ValuableVolume volume, ValuableObject item)
    {
        if (!active || !active.hasMobiusHall || !volume || !item || !item.TryGetComponent(out Rigidbody body)) return;
        var room = active.raid.MobiusRoomAt(volume.transform.position);
        if (room != null) active.newRibbonLoot.Add(body);
    }

    private void ApplyActorGravity(Rigidbody actor, bool ribbonOnly = false)
    {
        if (!actor || actor.isKinematic || actor.IsSleeping() || !actor.useGravity) return;
        Vector3 center = actor.worldCenterOfMass;
        if (raid.ContainsRing(center))
        {
            if (!ribbonOnly) raid.TrackRingActor?.Invoke(actor);
            return;
        }

        Vector3 up;
        if (ribbonOnly)
        {
            var room = raid.MobiusRoomAt(center);
            if (room == null || !MobiusWalk.TryFieldUp(room.Root, center, out up)) return;
        }
        else if (!TryActorUp(actor, center, out up, !carts.ContainsKey(actor))) return;
        Vector3 correction = MobiusWalk.TakeoffGravityCorrection(up, Physics.gravity);
        if (correction.sqrMagnitude > .00001f) actor.AddForce(correction, ForceMode.Acceleration);
    }

    private bool UpdateRibbon(Rigidbody player)
    {
        BakedRoom room = raid.MobiusRoomAt(player.position);
        if (room != ribbonRoom || player != ribbonObserver)
        {
            ClearRibbon();
            if (room != null)
            {
                ribbonRoom = room;
                ribbonObserver = player;
                if (!player.TryGetComponent(out ribbonContacts))
                    ribbonContacts = player.gameObject.AddComponent<MobiusContactRelay>();
                ribbonContacts.Bind(player, new MobiusWalk(room.Root, room.CollidersById[MobiusHall.Ribbon]));
            }
        }
        if (!ribbonContacts || ribbonObserver.isKinematic || !ribbonObserver.useGravity) return false;

        Vector3 up = ribbonContacts.Step(out var change);
        if (HoldNativeJump(player, up, false)) return true;
        if (Vector3.Dot(up, Vector3.up) < .999999f)
        {
            Quaternion turn = Quaternion.FromToRotation(up, Vector3.up);
            Vector3 feet = player.position;
            Vector3 pivot = change == MobiusWalk.Change.Release
                ? MobiusWalk.ReleasePivot(feet, player.worldCenterOfMass, turn) : feet;
            Vector3 velocity = player.linearVelocity;
            Vector3 spin = player.angularVelocity;
            Rebase(player, pivot, turn);
            // A change of local gravity also changes the world frame. Transport
            // free-flight velocity so entry into and exit from the outlet stays continuous.
            // Attached ribbon motion retains its existing stock steering semantics.
            if (change != MobiusWalk.Change.None || !ribbonContacts.Walk.Attached)
                player.linearVelocity = turn * velocity;
            if (!ribbonContacts.Walk.Attached)
                player.angularVelocity = turn * spin;
        }
        if (ribbonContacts.Walk.TryPassiveGravity(player.position, out Vector3 localFieldUp))
            player.AddForce(MobiusWalk.TakeoffGravityCorrection(localFieldUp, Physics.gravity), ForceMode.Acceleration);
        return true;
    }

    internal static void NotifyRibbonJump(Rigidbody body)
    {
        if (!active || !body || body != active.ribbonObserver || !active.ribbonContacts ||
            !body.useGravity || body.isKinematic || !active.ribbonContacts.Walk.Jump()) return;

        // The native takeoff and movement are already queued in the current ribbon frame.
        // Use the room's acceleration in that frame for the first simulation step. The next
        // fixed update changes the frame and transports the resulting velocity with the room.
        Vector3 correction = MobiusWalk.TakeoffGravityCorrection(active.ribbonContacts.Walk.RoomUp(body.position), Physics.gravity);
        body.AddForce(correction, ForceMode.Acceleration);
    }

    private void ClearRibbon()
    {
        if (ribbonContacts) ribbonContacts.Bind(null, null);
        ribbonContacts = null;
        ribbonObserver = null;
        ribbonRoom = null;
    }

    private bool TryActorUp(Rigidbody actor, Vector3 point, out Vector3 up, bool allowsHandOnly)
    {
        if (TryRotaryUp(point, out up)) return true;
        if (hasSphericalChamber && allowsHandOnly && raid.TrySphericalUp(point, Vector3.zero, out up, out _)) return true;
        if (raid.TryUp(point, out up, allowsHandOnly)) return true;
        // The truck uses the original entrance floor basis.
        if (!actor.transform.IsChildOf(levelRoot)) return false;
        up = raid.Room(raid.Plan.Entrance.Room).Root.up;
        return true;
    }

    internal static bool TryGetCartSupport(Rigidbody body, out Vector3 up)
    {
        up = default;
        var gravity = active;
        if (!gravity || !body || gravity.raid.Context == null || !SemiFunc.LevelGenDone() ||
            body.gameObject.scene != gravity.gameObject.scene) return false;

        Vector3 center = body.worldCenterOfMass;
        if (gravity.raid.ContainsRing(center)) return false;
        if (gravity.TryRotaryUp(center, out up)) return true;
        if (gravity.raid.TryUp(center, out up, allowsHandOnly: false)) return true;
        if (!body.transform.IsChildOf(gravity.levelRoot)) return false;
        up = gravity.raid.Room(gravity.raid.Plan.Entrance.Room).Root.up;
        return true;
    }

    internal static void TransformCartHistory(PhysGrabCart cart, Func<Vector3, Vector3> map, Quaternion turn)
    {
        ActualLast.SetValue(cart, map((Vector3)ActualLast.GetValue(cart)));
        LastPosition.SetValue(cart, map((Vector3)LastPosition.GetValue(cart)));
        // Cargo can read this world velocity before the cart's next FixedUpdate.
        ref Vector3 cachedVelocity = ref ActualVelocity(cart);
        cachedVelocity = turn * cachedVelocity;
    }

    internal static bool TryRebasePlayerAcross(RaidAssembly raid, NativeRaidPortal portal, Rigidbody body,
        Vector3 destinationFeet, ISet<Rigidbody> portalLoad = null, Rigidbody mappedHand = null)
    {
        if (!body || !portal.Entrance || !portal.Exit ||
            !raid.TryGetComponent(out NativeRaidGravity gravity)) return false;
        gravity.ClearNativeJump();
        gravity.ClearRibbon();
        gravity.ClearSphericalOrientation();
        gravity.Rebase(body, destinationFeet, Quaternion.Inverse(portal.TravelRotation), portalLoad, mappedHand);
        return true;
    }

    internal static void RefreshActorFromRing(RaidAssembly raid, Rigidbody body, bool inRing)
    {
        if (!raid || !raid.TryGetComponent(out NativeRaidGravity gravity) || !gravity.isActiveAndEnabled) return;
        var controller = PlayerController.instance;
        if (!gravity.EligibleActor(body, controller ? controller.rb : null, PlayerAvatar.instance)) return;

        // A Ring scan can be the first to find this body. Keep it through a transfer
        // later in the same fixed step, before either periodic scan can see it again.
        if (inRing || gravity.InLevelGravity(body))
        {
            if (!gravity.actors.Contains(body)) gravity.actors.Add(body);
        }
        else gravity.actors.Remove(body);
    }

    private bool EligibleActor(Rigidbody body, Rigidbody player, PlayerAvatar avatar) =>
        body && body != player && body.gameObject.scene == gameObject.scene &&
        (!avatar || !body.transform.IsChildOf(avatar.transform)) &&
        (!player || !body.transform.IsChildOf(player.transform));

    private bool InLevelGravity(Rigidbody body)
    {
        Vector3 center = body.worldCenterOfMass;
        return body.transform.IsChildOf(levelRoot) || TryRotaryUp(center, out _) || raid.TryUp(center, out _) || raid.ContainsRing(center) ||
            hasSphericalChamber && raid.TrySphericalUp(center, Vector3.zero, out _, out _);
    }

    private void RefreshActors(Rigidbody player)
    {
        if (Time.fixedTime < nextActorScan) return;
        nextActorScan = Time.fixedTime + .25f;
        actors.Clear();
        carts.Clear();
        var avatar = PlayerAvatar.instance;
        foreach (var body in FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (EligibleActor(body, player, avatar) && InLevelGravity(body)) actors.Add(body);
        foreach (var cart in FindObjectsByType<PhysGrabCart>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!cart || cart.gameObject.scene != gameObject.scene ||
                !(CartBody.GetValue(cart) is Rigidbody body) || !body) continue;
            carts.TryAdd(body, cart);
        }
    }

    private void Rebase(Rigidbody player, Vector3 pivot, Quaternion turn, ISet<Rigidbody> portalLoad = null,
        Rigidbody mappedHand = null)
    {
        RefreshActors(player);
        // A newly grabbed far-side body can be outside LevelParent and absent from this cached scan.
        if (mappedHand && !actors.Contains(mappedHand))
            actors.Add(mappedHand);
        Vector3 destination = player.position;
        stage.Apply(levelRoot, player, actors, pivot, turn, portalLoad);
        foreach (var body in actors)
        {
            if (body && (portalLoad == null || !portalLoad.Contains(body)) &&
                carts.TryGetValue(body, out var cart) && cart && (Rigidbody)CartBody.GetValue(cart) == body)
                TransformCartHistory(cart, point => destination + turn * (point - pivot), turn);
        }
    }
}

// The portal controller can rotate the raid at order 1000. Resolve free and held
// items in the destination frame, after native grab forces and all portal transforms.
[DefaultExecutionOrder(1200)]
internal sealed class NativeRaidActorGravityStep : MonoBehaviour
{
    internal NativeRaidGravity Owner { get; set; }

    private void FixedUpdate()
    {
        if (Owner && Owner.isActiveAndEnabled) Owner.ApplyDeferredActorGravity();
    }
}

// Sample the destination position after native player movement and portal transfer.
[DefaultExecutionOrder(1200)]
internal sealed class NativeRaidSphericalGravityStep : MonoBehaviour
{
    internal NativeRaidGravity Owner { get; set; }

    private void FixedUpdate()
    {
        if (Owner && Owner.isActiveAndEnabled) Owner.ApplySphericalPlayerGravity();
    }
}

internal sealed class MobiusContactRelay : MonoBehaviour
{
    private Rigidbody observer;
    private Collider ground;
    internal MobiusWalk Walk { get; private set; }

    internal void Bind(Rigidbody body, MobiusWalk walk)
    {
        observer = body;
        Walk = walk;
        ground = null;
    }

    internal Vector3 Step(out MobiusWalk.Change change)
    {
        Vector3 up = Walk.Step(observer.position, observer.linearVelocity, ground, out change);
        ground = null;
        return up;
    }

    private void OnCollisionEnter(Collision collision) => OnCollisionStay(collision);

    private void OnCollisionStay(Collision collision)
    {
        if (Walk == null || !observer) return;
        Collider surface = collision.collider;
        Vector3 feet = observer.position;
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (Walk.IsWalkSurface(surface)) Walk.Observe(feet, contact.point, contact.normal);
            else if (contact.normal.y > .50f && Mathf.Abs(contact.point.y - feet.y) <= .39f)
                ground = surface;
        }
    }
}

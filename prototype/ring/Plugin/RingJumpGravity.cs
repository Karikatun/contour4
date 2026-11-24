using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Contour4.NativeRaid;
using HarmonyLib;
using UnityEngine;

namespace SpaceJourney;

internal sealed partial class RingRuntime
{
    private enum JumpTurn { None, Waiting, Turning }

    private static readonly AccessTools.FieldRef<PlayerAvatar, bool> AvatarDead =
        AccessTools.FieldRefAccess<PlayerAvatar, bool>("deadSet");
    private static readonly AccessTools.FieldRef<PlayerCollisionGrounded, bool> JumpGrounded =
        AccessTools.FieldRefAccess<PlayerCollisionGrounded, bool>("Grounded");
    private static readonly AccessTools.FieldRef<PlayerController, bool> JumpQueued =
        AccessTools.FieldRefAccess<PlayerController, bool>("JumpImpulse");
    private static readonly AccessTools.FieldRef<PlayerController, float> JumpBuffer =
        AccessTools.FieldRefAccess<PlayerController, float>("JumpInputBuffer");
    private static readonly AccessTools.FieldRef<PlayerController, float> AntiGravityTimer =
        AccessTools.FieldRefAccess<PlayerController, float>("antiGravityTimer");
    private static readonly AccessTools.FieldRef<PlayerController, float> FeatherTimer =
        AccessTools.FieldRefAccess<PlayerController, float>("featherTimer");
    private static readonly AccessTools.FieldRef<PlayerAvatar, Vector3> AvatarRawVelocity =
        AccessTools.FieldRefAccess<PlayerAvatar, Vector3>("rbVelocityRaw");
    private static readonly AccessTools.FieldRef<PhysGrabObject, bool> GrabObjectSpawned =
        AccessTools.FieldRefAccess<PhysGrabObject, bool>("spawned");
    private static readonly AccessTools.FieldRef<PlayerAvatar, Rigidbody> AvatarBody =
        AccessTools.FieldRefAccess<PlayerAvatar, Rigidbody>("rb");
    private static readonly AccessTools.FieldRef<PlayerAvatarVisuals, Vector3> VisualPosition =
        AccessTools.FieldRefAccess<PlayerAvatarVisuals, Vector3>("visualPosition");
    private static readonly AccessTools.FieldRef<PlayerAvatarVisuals, Vector3> VisualPrevious =
        AccessTools.FieldRefAccess<PlayerAvatarVisuals, Vector3>("positionLast");
    private static readonly AccessTools.FieldRef<PlayerLocalCamera, Vector3> PreviousCameraEye =
        AccessTools.FieldRefAccess<PlayerLocalCamera, Vector3>("previousPosition");
    private static readonly AccessTools.FieldRef<SpringQuaternion, Quaternion> BodySpringRotation =
        AccessTools.FieldRefAccess<SpringQuaternion, Quaternion>("lastRotation");
    private static readonly AccessTools.FieldRef<SpringQuaternion, Vector3> BodySpringVelocity =
        AccessTools.FieldRefAccess<SpringQuaternion, Vector3>("springVelocity");

    private JumpTurn jumpTurn;
    private Rigidbody jumpBody;
    private PlayerAvatar jumpOwner;
    private Rigidbody tiltedAvatarBody;
    private PlayerAvatarVisuals tiltedVisuals;
    private SpringQuaternion tiltedBodySpring;
    private TiltSnapshot controllerPose, avatarPose, visualPose, springTargetPose, springRotationPose;
    private RingJumpContacts jumpContacts;
    private RoomGravity jumpRoom;
    private bool unpreparedLanding;
    private PortalEnd tumbleUpperExit;
    private Rigidbody landingRagdoll;
    private Vector3 impactCameraAxis;
    private float impactCameraAngle;
    private float tookOffAt;
    private bool leftGround;
    private bool acceptedExtraJump;
    private bool redirectExtraImpulse;
    private PortalEnd airborneUpperExit;
    private Vector3? jumpFieldOffsetInRing;
    private Quaternion jumpTilt = Quaternion.identity;
    private float jumpTiltAngle;
    private float jumpCameraTiltAngle;
    private float jumpTiltTarget;
    private Vector3 jumpTiltAxis;
    private TurnArc bodyTurn;
    private TurnArc cameraTurn;
    private bool turnTouchedFloor;
    private Transform jumpCameraRoot;
    private Vector3 stockCameraRootPosition;
    private Quaternion stockCameraRootRotation;

    private PlayerAvatar CurrentJumpAvatar() =>
        live && isActiveAndEnabled && MainRaid ? SemiFunc.PlayerAvatarLocal() : null;

    private static bool ValidJumpController(PlayerController controller, PlayerAvatar avatar) =>
        controller && avatar && controller.playerAvatarScript == avatar && controller.rb &&
        !controller.rb.isKinematic && controller.rb.useGravity &&
        AntiGravityTimer(controller) <= 0f && FeatherTimer(controller) <= 0f && !AvatarDead(avatar) &&
        !(bool)IsTumbling.GetValue(avatar);

    private bool TryGetJumpOwner(PlayerController controller, out PlayerAvatar avatar)
    {
        avatar = CurrentJumpAvatar();
        return jumpBody && jumpOwner && avatar == jumpOwner && ValidJumpController(controller, avatar) &&
            controller.rb == jumpBody && (jumpTurn != JumpTurn.Turning || SameTiltRecipients(avatar));
    }

    private bool SameTiltRecipients(PlayerAvatar avatar) =>
        AvatarBody(avatar) == tiltedAvatarBody && avatar.playerAvatarVisuals == tiltedVisuals &&
        (!tiltedVisuals || ReferenceEquals(tiltedVisuals.bodySpring, tiltedBodySpring));

    private bool OwnsJumpMotion(Rigidbody body) =>
        body == jumpBody && OwnsDynamicPlayerGravity(body) && TryGetJumpOwner(PlayerController.instance, out _);

    private void RecordStockJump(PlayerAvatar avatar, bool extra)
    {
        var controller = PlayerController.instance;
        if (!NativeJumpInput.Bound || !NativeJumpMotion.Bound || avatar != CurrentJumpAvatar() ||
            !ValidJumpController(controller, avatar))
            return;

        if (extra)
        {
            if (jumpOwner == avatar && jumpBody == controller.rb && jumpTurn != JumpTurn.None)
            {
                acceptedExtraJump = true;
                jumpFieldOffsetInRing ??= ring.InverseTransformDirection(jumpBody.worldCenterOfMass - jumpBody.position);
            }
            return;
        }

        if ((jumpBody || jumpTurn != JumpTurn.None) && (jumpOwner != avatar || jumpBody != controller.rb))
            AbortJumpControl(controller, avatar);
        jumpBody = controller.rb;
        jumpOwner = avatar;
        airborneUpperExit = null;
        jumpFieldOffsetInRing = null;
        if (!jumpContacts || jumpContacts.gameObject != jumpBody.gameObject)
        {
            if (jumpContacts)
                Destroy(jumpContacts);
            jumpContacts = jumpBody.gameObject.AddComponent<RingJumpContacts>();
            jumpContacts.Runtime = this;
        }
        tookOffAt = Time.time;
        leftGround = false;
    }

    // A pose change alone should not move the sampled point across a shaft boundary.
    // Keep the takeoff offset in ring axes while the player continues along the actual flight path.
    private Vector3 JumpGravitySample(Rigidbody player) =>
        player == jumpBody && jumpFieldOffsetInRing is Vector3 offset && TryGetJumpOwner(PlayerController.instance, out _)
            ? player.position + ring.TransformDirection(offset)
            : player.worldCenterOfMass;

    private Vector3 AirborneUpperVelocity(Rigidbody player, Crossing crossing, Vector3 mappedVelocity)
    {
        var controller = PlayerController.instance;
        if (jumpBody != player || !TryGetJumpOwner(controller, out _) ||
            !controller.CollisionGrounded || JumpGrounded(controller.CollisionGrounded))
            return mappedVelocity;

        // Walking through the upper aperture lands on its partner ramp. A jump through it
        // keeps its rise, so the normal takeoff is not mapped into an immediate ramp strike.
        Vector3 up = crossing.Exit.Room.up;
        float incoming = Vector3.Dot(mappedVelocity, up);
        airborneUpperExit = crossing.Exit;
        return mappedVelocity - 2 * Mathf.Min(0, incoming) * up;
    }

    private bool UpdateJumpGravity(PlayerController controller, Vector3 fieldUp, bool onBridge, bool usedDoor)
    {
        if (!TryGetJumpOwner(controller, out var avatar) || usedDoor)
        {
            AbortJumpControl(controller, avatar);
            return false;
        }

        var player = controller.rb;
        bool grounded = controller.CollisionGrounded && JumpGrounded(controller.CollisionGrounded);
        leftGround |= !grounded;
        Vector3 targetUp = ring.TransformDirection(fieldUp);
        Vector3 axis = ring.TransformDirection(gravityTurnAxis);
        float angle = Vector3.SignedAngle(Vector3.up, targetUp, axis);
        if (angle < -179.95f)
            angle = 180f;

        if (onBridge)
        {
            if (jumpTurn == JumpTurn.Turning)
                FinishJumpTilt(controller, avatar);
            // The bridge supplies its own continuous support direction.
            ClearJumpGravity();
            return false;
        }

        if (jumpTurn != JumpTurn.Turning && Mathf.Abs(angle) < .08f)
        {
            jumpTurn = JumpTurn.None;
            jumpRoom = null;
            unpreparedLanding = false;
            if (grounded && !JumpQueued(controller) && !acceptedExtraJump && (leftGround || Time.time - tookOffAt > .25f))
                ClearJumpGravity();
            return false;
        }

        float now = Time.fixedTime;
        if (jumpTurn != JumpTurn.Turning)
        {
            if (!leftGround && !JumpQueued(controller) && grounded)
                return false;
            gravityTurnSpeed = 0;
            jumpTurn = acceptedExtraJump ? JumpTurn.Turning : JumpTurn.Waiting;
            if (jumpTurn == JumpTurn.Turning)
            {
                tiltedAvatarBody = AvatarBody(avatar);
                tiltedVisuals = avatar.playerAvatarVisuals;
                tiltedBodySpring = tiltedVisuals ? tiltedVisuals.bodySpring : null;
                StartJumpTurn(angle, axis, now - Time.fixedDeltaTime);
            }
        }
        else if (!turnTouchedFloor && Mathf.Abs(Mathf.DeltaAngle(jumpTiltTarget, angle)) > .08f)
        {
            StartJumpTurn(angle, axis, now);
        }

        jumpRoom = roomGravity.FirstOrDefault(room => Vector3.Dot(fieldUp, room.Up) > .9999f);
        redirectExtraImpulse = acceptedExtraJump;
        if (jumpTurn == JumpTurn.Waiting)
            return true;

        float bodyAngle = turnTouchedFloor ? jumpTiltTarget : bodyTurn.At(now);
        TurnJumpBody(player, bodyAngle);
        if (!turnTouchedFloor && !acceptedExtraJump && !JumpQueued(controller))
            FitViewToLanding(controller, targetUp, now);

        UpdateJumpView(now);
        if (turnTouchedFloor || (now >= bodyTurn.End && now >= cameraTurn.End))
            FinishJumpTilt(controller, avatar);
        return true;
    }

    private readonly struct TurnArc
    {
        private readonly float from;
        private readonly float to;
        private readonly float start;
        internal float End { get; }

        internal TurnArc(float from, float to, float start, float duration)
        {
            this.from = from;
            this.to = to;
            this.start = start;
            End = start + duration;
        }

        internal float At(float time) => Mathf.SmoothStep(from, to, Mathf.InverseLerp(start, End, time));
    }

    private void StartJumpTurn(float angle, Vector3 axis, float now)
    {
        jumpTiltAxis = axis;
        jumpTiltTarget = jumpTiltAngle + Mathf.DeltaAngle(jumpTiltAngle, angle);
        float bodyAngle = Mathf.Abs(jumpTiltTarget - jumpTiltAngle);
        float viewAngle = Mathf.Abs(jumpTiltTarget - jumpCameraTiltAngle);
        bodyTurn = new TurnArc(jumpTiltAngle, jumpTiltTarget, now, .055f + .00052f * bodyAngle);
        cameraTurn = new TurnArc(jumpCameraTiltAngle, jumpTiltTarget, now, .19f + .00125f * viewAngle);
    }

    private void TurnJumpBody(Rigidbody player, float angle)
    {
        if (angle == jumpTiltAngle)
            return;
        Quaternion tilt = Quaternion.AngleAxis(angle, jumpTiltAxis);
        Quaternion delta = tilt * Quaternion.Inverse(jumpTilt);
        Quaternion inverse = Quaternion.Inverse(tilt);
        SetJumpBodyRotation(player, delta * player.rotation);
        controllerPose = new TiltSnapshot(player.transform.rotation, inverse);
        if (tiltedAvatarBody && tiltedAvatarBody != player)
        {
            SetJumpBodyRotation(tiltedAvatarBody, delta * tiltedAvatarBody.rotation);
            avatarPose = new TiltSnapshot(tiltedAvatarBody.transform.rotation, inverse);
        }
        if (tiltedVisuals)
        {
            RotateJumpVisuals(tiltedVisuals, delta);
            visualPose = new TiltSnapshot(tiltedVisuals.transform.rotation, inverse);
            springTargetPose = new TiltSnapshot(tiltedVisuals.bodySpringTarget, inverse);
            if (tiltedBodySpring != null)
                springRotationPose = new TiltSnapshot(BodySpringRotation(tiltedBodySpring), inverse);
        }
        jumpTilt = tilt;
        jumpTiltAngle = angle;
    }

    private void FitViewToLanding(PlayerController controller, Vector3 up, float now)
    {
        Rigidbody player = controller.rb;
        float horizon = cameraTurn.End - now;
        float speed = Vector3.Dot(player.linearVelocity, -up);
        if (jumpRoom == null || horizon <= 2 * Time.fixedDeltaTime || speed <= .05f)
            return;

        // Predict the first possible deck contact from the actual body shape and its current fall.
        // Leave one fixed step of margin. A wall, rail or underside does not shorten the view turn.
        float acceleration = Physics.gravity.magnitude + Mathf.Abs(controller.CustomGravity) / player.mass;
        float reach = speed * horizon + .5f * acceleration * horizon * horizon;
        float distance = reach;
        foreach (RaycastHit hit in player.SweepTestAll(-up, reach, QueryTriggerInteraction.Ignore))
            if (jumpRoom.IsLandingSurface(hit.collider) && Vector3.Dot(hit.normal, up) > .65f)
                distance = Mathf.Min(distance, hit.distance);

        if (distance >= reach)
            return;
        float contactTime = 2 * distance / (speed + Mathf.Sqrt(speed * speed + 2 * acceleration * distance));
        float duration = Mathf.Max(Time.fixedDeltaTime, contactTime - Time.fixedDeltaTime);
        if (duration < horizon - Time.fixedDeltaTime)
            cameraTurn = new TurnArc(cameraTurn.At(now), jumpTiltTarget, now, duration);
    }

    private void Update()
    {
        if (jumpTurn == JumpTurn.Turning && TryGetJumpOwner(PlayerController.instance, out _))
            UpdateJumpView(Time.time);
    }

    private void UpdateJumpView(float now)
    {
        jumpCameraTiltAngle = turnTouchedFloor ? jumpTiltTarget : cameraTurn.At(now);
        ApplyJumpCamera(jumpBody.position, Quaternion.AngleAxis(jumpCameraTiltAngle, jumpTiltAxis));
    }

    private void ApplyJumpCamera(Vector3 pivot, Quaternion tilt)
    {
        if (!jumpCameraRoot && CameraPosition.instance)
        {
            jumpCameraRoot = CameraPosition.instance.transform.parent;
            if (jumpCameraRoot)
            {
                stockCameraRootPosition = jumpCameraRoot.position;
                stockCameraRootRotation = jumpCameraRoot.rotation;
            }
        }

        if (jumpCameraRoot)
            jumpCameraRoot.SetPositionAndRotation(pivot + tilt * (stockCameraRootPosition - pivot),
                tilt * stockCameraRootRotation);
    }

    private bool BlocksLandingJump(PlayerController controller) =>
        jumpTurn == JumpTurn.Waiting && unpreparedLanding && TryGetJumpOwner(controller, out _);

    internal void RecordJumpLanding(Collision collision)
    {
        var controller = PlayerController.instance;
        bool madeExtraJump = jumpTurn == JumpTurn.Turning;
        if ((!madeExtraJump && (jumpTurn != JumpTurn.Waiting || acceptedExtraJump || unpreparedLanding)) ||
            turnTouchedFloor || jumpRoom == null || !jumpRoom.IsLandingSurface(collision.collider) ||
            !TryGetJumpOwner(controller, out _))
            return;

        Vector3 up = ring.TransformDirection(jumpRoom.Up);
        if (!madeExtraJump && Vector3.Dot(jumpBody.rotation * Vector3.up, up) > .96f)
            return;
        for (int index = 0; index < collision.contactCount; index++)
        {
            var contact = collision.GetContact(index);
            if (contact.thisCollider && contact.thisCollider.attachedRigidbody == jumpBody &&
                contact.separation <= .015f && jumpRoom.IsLandingSurface(contact.otherCollider) && Vector3.Dot(contact.normal, up) > .65f)
            {
                if (madeExtraJump)
                {
                    turnTouchedFloor = true;
                    return;
                }
                unpreparedLanding = true;
                // The stock Update can poll jump between this contact and the next physics tick.
                JumpQueued(controller) = false;
                JumpBuffer(controller) = 0;
                return;
            }
        }
    }

    private bool TumbleOnJumpLanding(PlayerController controller)
    {
        if (!unpreparedLanding || jumpTurn != JumpTurn.Waiting)
            return false;
        if (!TryGetJumpOwner(controller, out var avatar))
        {
            AbortJumpControl(controller, avatar);
            return false;
        }
        if (!(AvatarTumble.GetValue(avatar) is PlayerTumble tumble && tumble) ||
            !(TumbleBody.GetValue(tumble) is PhysGrabObject ragdoll && ragdoll && ragdoll.rb && GrabObjectSpawned(ragdoll)))
            return false;

        // Rebase the impact and the current eye together. The stock fall then sees a level
        // floor and a body tipped sideways to it, and owns the ragdoll and the get-up.
        Vector3 targetUp = ring.TransformDirection(jumpRoom.Up);
        impactCameraAxis = ring.TransformDirection(gravityTurnAxis);
        impactCameraAngle = Vector3.SignedAngle(targetUp, Vector3.up, impactCameraAxis);
        Quaternion change = Quaternion.AngleAxis(impactCameraAngle, impactCameraAxis);
        tumbleUpperExit = airborneUpperExit;
        landingRagdoll = ragdoll.rb;
        ReframePlayer(controller, avatar, change, false);
        avatar.transform.SetPositionAndRotation(controller.rb.position, controller.rb.rotation);
        if (AvatarBody(avatar) is Rigidbody avatarBody && avatarBody)
        {
            avatarBody.position = controller.rb.position;
            avatarBody.rotation = controller.rb.rotation;
        }
        AvatarRawVelocity(avatar) = controller.rb.linearVelocity;
        controller.CollisionController.ResetFalling();
        var floorUp = jumpRoom.Up;
        ClearJumpGravity();
        tumble.TumbleRequest(true, false);
        if ((bool)IsTumbling.GetValue(avatar))
        {
            bodies[ragdoll.rb] = floorUp;
            lastCenters[ragdoll.rb] = ring.InverseTransformPoint(ragdoll.rb.worldCenterOfMass);
            if (tumbleUpperExit != null)
                exitThresholds[ragdoll.rb] = tumbleUpperExit;
        }
        return true;
    }

    private void UpdateLandingCamera(Rigidbody player)
    {
        if (landingRagdoll && SemiFunc.PlayerAvatarLocal() is PlayerAvatar avatar && avatar &&
            !(bool)IsTumbling.GetValue(avatar))
        {
            tumbleUpperExit = null;
            landingRagdoll = null;
        }

        if (impactCameraAngle == 0f)
            return;
        impactCameraAngle = Mathf.MoveTowards(impactCameraAngle, 0f, 360f * Time.fixedDeltaTime);
        if (impactCameraAngle == 0f)
            RestoreJumpCamera();
        else
            ApplyJumpCamera(player.position, Quaternion.AngleAxis(impactCameraAngle, impactCameraAxis));
    }

    private static void SetJumpBodyRotation(Rigidbody body, Quaternion rotation)
    {
        body.transform.rotation = rotation;
        body.rotation = rotation;
    }

    private void FinishJumpTilt(PlayerController controller, PlayerAvatar avatar)
    {
        Quaternion cameraTilt = Quaternion.AngleAxis(jumpCameraTiltAngle, jumpTiltAxis);
        if (Mathf.Abs(jumpTiltAngle - jumpCameraTiltAngle) > .001f)
        {
            // An interrupted jump can stop with different body and view angles. Use the
            // visible frame for the handoff and return only the controller to its stock basis.
            Quaternion toCamera = cameraTilt * Quaternion.Inverse(jumpTilt);
            SetJumpBodyRotation(controller.rb, toCamera * controller.rb.rotation);
            if (AvatarBody(avatar) is Rigidbody avatarBody && avatarBody)
                SetJumpBodyRotation(avatarBody, toCamera * avatarBody.rotation);
        }
        ReframePlayer(controller, avatar, Quaternion.Inverse(cameraTilt), true);
        jumpTurn = JumpTurn.None;
        jumpTilt = Quaternion.identity;
        jumpTiltAngle = 0f;
        jumpCameraTiltAngle = 0f;
        turnTouchedFloor = false;
        ClearTiltRecipients();
    }

    private static void RotateJumpVisuals(PlayerAvatarVisuals visuals, Quaternion change)
    {
        // The native body spring stores world rotations. Move its basis along with the rig
        // so its usual steering lag does not delay the destination vertical.
        visuals.transform.rotation = change * visuals.transform.rotation;
        visuals.bodySpringTarget = change * visuals.bodySpringTarget;
        if (visuals.bodySpring != null)
        {
            BodySpringRotation(visuals.bodySpring) = change * BodySpringRotation(visuals.bodySpring);
            BodySpringVelocity(visuals.bodySpring) = change * BodySpringVelocity(visuals.bodySpring);
        }
    }

    private void ReframePlayer(PlayerController controller, PlayerAvatar avatar, Quaternion change, bool restoreCamera)
    {
        Rigidbody player = controller.rb;
        Vector3 pivot = player.position;
        Vector3 NewPoint(Vector3 point) => pivot + change * (point - pivot);

        // This changes coordinates for both the already-turned player and the scene in one tick,
        // so the room-relative eye, aim, capsule and velocity survive the return to stock world-up.
        var visuals = avatar.playerAvatarVisuals;
        if (visuals)
        {
            visuals.transform.position = NewPoint(visuals.transform.position);
            VisualPosition(visuals) = NewPoint(VisualPosition(visuals));
            VisualPrevious(visuals) = NewPoint(VisualPrevious(visuals));
            RotateJumpVisuals(visuals, change);
        }

        var grabber = PhysGrabber.instance;
        Vector3 grabPoint = grabber && grabber.physGrabPoint ? NewPoint(grabber.physGrabPoint.position) : default;
        Vector3 grabPuller = grabber && grabber.physGrabPointPuller ? NewPoint(grabber.physGrabPointPuller.position) : default;
        Quaternion grabPointRotation = grabber && grabber.physGrabPoint ? change * grabber.physGrabPoint.rotation : default;
        Quaternion grabPullerRotation = grabber && grabber.physGrabPointPuller ? change * grabber.physGrabPointPuller.rotation : default;

        SetJumpBodyRotation(player, change * player.rotation);
        if (!player.isKinematic)
            player.angularVelocity = change * player.angularVelocity;
        controller.Velocity = change * controller.Velocity;
        if (AvatarBody(avatar) is Rigidbody avatarBody && avatarBody)
            new BodyMove(avatarBody, pivot, pivot, change).Apply();

        if (grabber && grabber.grabbed)
        {
            grabber.physGrabPointPosition = NewPoint(grabber.physGrabPointPosition);
            grabber.physGrabPointPullerPosition = NewPoint(grabber.physGrabPointPullerPosition);
            if (grabber.physGrabPoint)
                grabber.physGrabPoint.SetPositionAndRotation(grabPoint, grabPointRotation);
            if (grabber.physGrabPointPuller)
                grabber.physGrabPointPuller.SetPositionAndRotation(grabPuller, grabPullerRotation);
        }

        if (restoreCamera)
        {
            // Physics can move the pivot between the last rendered camera pose and an interruption.
            Vector3 eyeOffset = jumpCameraRoot ? NewPoint(jumpCameraRoot.position) - stockCameraRootPosition : Vector3.zero;
            RestoreJumpCamera();
            if (CameraPosition.instance)
                CameraPosition.instance.transform.position += eyeOffset;
        }
        else
        {
            ApplyJumpCamera(pivot, change);
        }
        if (avatar.localCamera && CameraNoise.Instance)
        {
            var eye = CameraNoise.Instance.transform;
            avatar.localCamera.transform.SetPositionAndRotation(eye.position, eye.rotation);
            PreviousCameraEye(avatar.localCamera) = eye.position;
        }

        RotateLevel(change, player);
        gravityTurnSpeed = 0;
    }

    private void RestoreJumpCamera()
    {
        if (jumpCameraRoot)
            jumpCameraRoot.SetPositionAndRotation(stockCameraRootPosition, stockCameraRootRotation);
        jumpCameraRoot = null;
    }

    private void AbortJumpControl(PlayerController controller, PlayerAvatar avatar)
    {
        if (jumpTurn == JumpTurn.Turning)
        {
            if (jumpBody && controller && controller.rb == jumpBody && avatar && avatar == jumpOwner &&
                controller.playerAvatarScript == avatar && SameTiltRecipients(avatar) &&
                !AvatarDead(avatar) && levelRoot && ring && MainRaid)
                FinishJumpTilt(controller, avatar);
            else
                RestoreAbandonedJumpPose();
        }
        ClearJumpGravity();
    }

    private readonly struct TiltSnapshot
    {
        internal Quaternion Inverse { get; }
        private readonly Vector3 turnedUp;
        private readonly Vector3 originalUp;

        internal TiltSnapshot(Quaternion applied, Quaternion inverse)
        {
            Inverse = inverse;
            turnedUp = applied * Vector3.up;
            originalUp = inverse * turnedUp;
        }

        internal bool HasTurn(Quaternion current)
        {
            Vector3 up = current * Vector3.up;
            return (up - turnedUp).sqrMagnitude < (up - originalUp).sqrMagnitude;
        }
    }

    private void RestoreAbandonedJumpPose()
    {
        // Read each world pose after its parents. The snapshot's original up includes
        // the stock lean, and a child already restored by its parent stays in place.
        static void RestoreBody(Rigidbody body, TiltSnapshot pose)
        {
            if (body && pose.HasTurn(body.transform.rotation))
                SetJumpBodyRotation(body, pose.Inverse * body.transform.rotation);
        }

        RestoreBody(jumpBody, controllerPose);
        RestoreBody(tiltedAvatarBody, avatarPose);
        if (tiltedVisuals)
        {
            if (visualPose.HasTurn(tiltedVisuals.transform.rotation))
                tiltedVisuals.transform.rotation = visualPose.Inverse * tiltedVisuals.transform.rotation;
            if (springTargetPose.HasTurn(tiltedVisuals.bodySpringTarget))
                tiltedVisuals.bodySpringTarget = springTargetPose.Inverse * tiltedVisuals.bodySpringTarget;
        }
        if (tiltedBodySpring != null && springRotationPose.HasTurn(BodySpringRotation(tiltedBodySpring)))
        {
            BodySpringRotation(tiltedBodySpring) = springRotationPose.Inverse * BodySpringRotation(tiltedBodySpring);
            BodySpringVelocity(tiltedBodySpring) = springRotationPose.Inverse * BodySpringVelocity(tiltedBodySpring);
        }
    }

    private void ClearTiltRecipients()
    {
        tiltedAvatarBody = null;
        tiltedVisuals = null;
        tiltedBodySpring = null;
        controllerPose = avatarPose = visualPose = springTargetPose = springRotationPose = default;
    }

    private void ReleaseJumpControl()
    {
        impactCameraAngle = 0f;
        if (jumpTurn == JumpTurn.Turning || jumpBody || jumpCameraRoot)
            AbortJumpControl(PlayerController.instance, MainRaid ? SemiFunc.PlayerAvatarLocal() : null);
        tumbleUpperExit = null;
        landingRagdoll = null;
    }

    private void ClearJumpGravity()
    {
        if (impactCameraAngle == 0f)
            RestoreJumpCamera();
        jumpTurn = JumpTurn.None;
        jumpBody = null;
        jumpOwner = null;
        ClearTiltRecipients();
        jumpRoom = null;
        unpreparedLanding = false;
        airborneUpperExit = null;
        jumpFieldOffsetInRing = null;
        jumpTilt = Quaternion.identity;
        jumpTiltAngle = 0;
        jumpCameraTiltAngle = 0;
        turnTouchedFloor = false;
        acceptedExtraJump = false;
        redirectExtraImpulse = false;
    }

    [HarmonyPatch(typeof(PlayerController), "Update")]
    private static class NativeJumpInput
    {
        internal static bool Bound { get; private set; }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var jump = AccessTools.Method(typeof(PlayerAvatar), nameof(PlayerAvatar.Jump), new[] { typeof(bool) });
            var charge = AccessTools.Field(typeof(PlayerController), "JumpExtraCurrent");
            var inputDown = AccessTools.Method(typeof(SemiFunc), nameof(SemiFunc.InputDown), new[] { typeof(InputKey) });
            var matches = Enumerable.Range(8, code.Count - 8).Where(index => code[index].Calls(jump)).ToArray();
            var polls = Enumerable.Range(1, code.Count - 3).Where(index => code[index].Calls(inputDown) &&
                code[index - 1].opcode == OpCodes.Ldc_I4_1 &&
                (code[index + 1].opcode == OpCodes.Brfalse || code[index + 1].opcode == OpCodes.Brfalse_S)).ToArray();
            Bound = matches.Length == 2 && polls.Length == 1 &&
                code[matches[0] - 1].opcode == OpCodes.Ldc_I4_0 &&
                code[matches[1] - 1].opcode == OpCodes.Ldc_I4_1 &&
                code[matches[1] - 4].StoresField(charge) && code[matches[1] - 5].opcode == OpCodes.Sub;
            if (!Bound)
            {
                Debug.LogError("[Contour4Ring] Native jump acceptance differs from R.E.P.O. 0.5.2; stock turning retained.");
                return code;
            }

            code[polls[0]].operand = AccessTools.Method(typeof(NativeJumpInput), nameof(ReadJump));
            var record = AccessTools.Method(typeof(NativeJumpInput), nameof(Record));
            foreach (int index in matches)
            {
                code[index].opcode = OpCodes.Call;
                code[index].operand = record;
            }
            return code;
        }

        private static bool ReadJump(InputKey key)
        {
            bool pressed = SemiFunc.InputDown(key);
            var runtime = Active;
            return pressed && (!runtime || !runtime.BlocksLandingJump(PlayerController.instance));
        }

        private static void Record(PlayerAvatar avatar, bool extra)
        {
            avatar.Jump(extra);
            if (Active)
                Active.RecordStockJump(avatar, extra);
            if (NativeJumpMotion.Bound) NativeRaidGravity.RecordNativeStockJump(avatar, extra);
        }
    }

    private static class NativeJumpMotion
    {
        internal static bool Bound { get; private set; }

        internal static void Bind(List<CodeInstruction> code)
        {
            var addForce = AccessTools.Method(typeof(Rigidbody), nameof(Rigidbody.AddForce),
                new[] { typeof(Vector3), typeof(ForceMode) });
            var relativeForce = AccessTools.Method(typeof(Rigidbody), nameof(Rigidbody.AddRelativeForce),
                new[] { typeof(Vector3), typeof(ForceMode) });
            var rotation = AccessTools.PropertySetter(typeof(Transform), nameof(Transform.rotation));
            var subtract = AccessTools.Method(typeof(Vector3), "op_Subtraction");
            var up = AccessTools.PropertyGetter(typeof(Vector3), nameof(Vector3.up));
            int[] turns = Enumerable.Range(1, code.Count - 1)
                .Where(index => code[index].Calls(rotation) && code[index - 1].opcode == OpCodes.Ldloc_0).ToArray();
            int[] jumpForce = Enumerable.Range(6, code.Count - 6).Where(index =>
                code[index].Calls(addForce) && code[index - 1].opcode == OpCodes.Ldc_I4_1 &&
                code[index - 2].Calls(subtract) && code[index - 6].Calls(up)).ToArray();
            int[] movement = Enumerable.Range(0, code.Count).Where(index => code[index].Calls(relativeForce)).ToArray();
            Bound = turns.Length == 1 && jumpForce.Length == 1 && movement.Length == 1;
            if (!Bound)
            {
                Debug.LogError("[Contour4Ring] Native jump motion differs from R.E.P.O. 0.5.2; stock turning retained.");
                return;
            }

            void Replace(int index, string method)
            {
                code[index].opcode = OpCodes.Call;
                code[index].operand = AccessTools.Method(typeof(NativeJumpMotion), method);
            }
            Replace(turns[0], nameof(TurnController));
            Replace(jumpForce[0], nameof(ApplyJump));
            Replace(movement[0], nameof(ApplyMovement));
        }

        private static void TurnController(Transform controllerTransform, Quaternion nativeRotation)
        {
            var runtime = Active;
            var controller = PlayerController.instance;
            if (runtime && runtime.jumpTurn == JumpTurn.Turning && controller && controller.transform == controllerTransform &&
                runtime.OwnsJumpMotion(controller.rb))
                SetJumpBodyRotation(controller.rb, runtime.jumpTilt * nativeRotation);
            else if (!NativeRaidGravity.TurnNativeJump(controllerTransform, nativeRotation))
                controllerTransform.rotation = nativeRotation;
        }

        private static void ApplyJump(Rigidbody body, Vector3 impulse, ForceMode mode)
        {
            var runtime = Active;
            if (runtime && runtime.jumpBody == body && runtime.acceptedExtraJump)
            {
                if (runtime.redirectExtraImpulse && runtime.OwnsJumpMotion(body))
                {
                    Vector3 up = runtime.playerGravityUp;
                    Vector3 velocity = body.linearVelocity;
                    // Replace only the stock vertical correction. The stock branch chose the force
                    // and paid for the jump, and the velocity tangent to the new floor is preserved.
                    float launch = impulse.y + velocity.y;
                    impulse = up * (launch - Vector3.Dot(velocity, up));
                }
                runtime.acceptedExtraJump = false;
                runtime.redirectExtraImpulse = false;
            }
            body.AddForce(NativeRaidGravity.NativeJumpImpulse(body, impulse), mode);
            NativeRaidGravity.NotifyRibbonJump(body);
        }

        private static void ApplyMovement(Rigidbody body, Vector3 impulse, ForceMode mode)
        {
            var runtime = Active;
            if (runtime && runtime.jumpTurn != JumpTurn.None && runtime.OwnsJumpMotion(body))
                body.AddForce(Vector3.ProjectOnPlane(body.rotation * impulse, runtime.playerGravityUp), mode);
            else if (!NativeRaidGravity.MoveNativeJump(body, impulse, mode))
                body.AddRelativeForce(impulse, mode);
        }
    }
}

internal sealed class RingJumpContacts : MonoBehaviour
{
    internal RingRuntime Runtime { get; set; }

    private void OnCollisionEnter(Collision collision)
    {
        if (Runtime)
            Runtime.RecordJumpLanding(collision);
    }

    private void OnCollisionStay(Collision collision) => OnCollisionEnter(collision);
}

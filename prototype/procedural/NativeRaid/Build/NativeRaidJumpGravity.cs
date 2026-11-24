using System;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using HarmonyLib;
using UnityEngine;

namespace Contour4.NativeRaid;

internal sealed partial class NativeRaidGravity
{
    private static readonly AccessTools.FieldRef<PlayerCollisionGrounded, bool> JumpGrounded =
        AccessTools.FieldRefAccess<PlayerCollisionGrounded, bool>("Grounded");
    private static readonly AccessTools.FieldRef<PlayerController, bool> JumpQueued =
        AccessTools.FieldRefAccess<PlayerController, bool>("JumpImpulse");
    private static readonly AccessTools.FieldRef<PlayerController, float> AntiGravityTimer =
        AccessTools.FieldRefAccess<PlayerController, float>("antiGravityTimer");
    private static readonly AccessTools.FieldRef<PlayerController, float> FeatherTimer =
        AccessTools.FieldRefAccess<PlayerController, float>("featherTimer");
    private static readonly AccessTools.FieldRef<PlayerAvatar, bool> AvatarDead =
        AccessTools.FieldRefAccess<PlayerAvatar, bool>("deadSet");
    private static readonly AccessTools.FieldRef<PlayerAvatar, bool> AvatarTumbling =
        AccessTools.FieldRefAccess<PlayerAvatar, bool>("isTumbling");
    private static readonly AccessTools.FieldRef<RaidAssembly, int> RaidSolidMask =
        AccessTools.FieldRefAccess<RaidAssembly, int>("solidMask");
    private Rigidbody jumpBody;
    private PlayerAvatar jumpAvatar;
    private bool jumpLeftGround, jumpExtra, jumpWaiting, jumpTurning, jumpSampled;
    private float jumpTookOff, jumpTurnStart, jumpBodyDuration, jumpViewDuration;
    private Vector3 jumpSampleOffset, jumpUp;
    private Quaternion jumpFrom = Quaternion.identity, jumpViewFrom = Quaternion.identity,
        jumpTarget = Quaternion.identity, jumpTilt = Quaternion.identity, jumpViewTilt = Quaternion.identity;
    private NativeRaidPlayerPose.JumpPose jumpPose;
    private Transform jumpCameraRoot;
    private Vector3 jumpCameraPosition;
    private Quaternion jumpCameraRotation;

    private bool ValidJump(PlayerController controller) =>
        controller && controller.rb && controller.rb == jumpBody && jumpAvatar &&
        controller.playerAvatarScript == jumpAvatar && !jumpBody.isKinematic && jumpBody.useGravity &&
        !AvatarDead(jumpAvatar) && !AvatarTumbling(jumpAvatar) && AntiGravityTimer(controller) <= 0 && FeatherTimer(controller) <= 0 &&
        (jumpPose == null || jumpPose.Matches(controller, jumpAvatar));

    // Called only from the existing, version-checked stock jump acceptance hook.
    // No input is synthesized and the stock upgrade/charge decision is left intact.
    internal static void RecordNativeStockJump(PlayerAvatar avatar, bool extra)
    {
        var owner = active;
        var controller = PlayerController.instance;
        if (!owner || !owner.actorsReady || !controller || !controller.rb || !avatar ||
            controller.playerAvatarScript != avatar || owner.raid.ContainsRing(controller.rb.position)) return;
        if (extra)
        {
            if (owner.jumpBody == controller.rb && owner.jumpAvatar == avatar && owner.ValidJump(controller))
                owner.jumpExtra = true;
            return;
        }
        owner.ClearNativeJump();
        owner.jumpBody = controller.rb;
        owner.jumpAvatar = avatar;
        if (!owner.ValidJump(controller)) { owner.ClearNativeJump(); return; }
        owner.jumpSampleOffset = owner.raid.transform.InverseTransformDirection(owner.jumpBody.worldCenterOfMass - owner.jumpBody.position);
        owner.jumpTookOff = Time.time;
    }

    // Bridge attachment is chosen from the feet, as in RingRuntime.FindUp. A held
    // takeoff COM can lie below a new deck while the old body up is still retained.
    private Vector3 JumpSample(Rigidbody player) => jumpBody == player && !IsJumpBridge(player.position)
        ? player.position + raid.transform.TransformDirection(jumpSampleOffset) : player.position;

    private bool IsJumpBridge(Vector3 point)
    {
        foreach (var fold in raid.Folds) if (fold.TryUp(point, out _)) return true;
        foreach (var room in raid.Rooms)
        {
            if (!room.Prepared || !room.PreparedSurfaces.Contains(point)) continue;
            int sphereMask = SphericalChamber.GetMask(room.Plan.Module.Id);
            if (sphereMask != 0)
            {
                Vector3 p = room.Root.InverseTransformPoint(point);
                Vector3 u = room.Root.InverseTransformDirection(Vector3.up);
                if (SphericalGravityField.TryUp(new(p.x,p.y,p.z), new(u.x,u.y,u.z), out _, out bool onDeck, sphereMask) && onDeck)
                    return true;
                continue;
            }
            foreach (var turn in room.PreparedSurfaces.Turns)
                if (turn.TryUp(point, RaidSolidMask(raid), true, out _, out _)) return true;
        }
        return false;
    }

    // True means this airborne jump owns pose only. The destination field still owns force.
    private bool HoldNativeJump(Rigidbody player, Vector3 up, bool onBridge)
    {
        jumpSampled = true;
        if (!jumpBody) return false;
        var controller = PlayerController.instance;
        if (!ValidJump(controller) || player != jumpBody || raid.ContainsRing(player.position) || onBridge)
        {
            ClearNativeJump();
            return false;
        }
        bool grounded = controller.CollisionGrounded && JumpGrounded(controller.CollisionGrounded);
        jumpLeftGround |= !grounded;
        if (grounded && jumpLeftGround && !JumpQueued(controller))
        {
            bool turning = jumpTurning;
            if (turning) CommitNativeJump(controller);
            ClearNativeJump();
            return turning;
        }
        jumpUp = up;
        if (!jumpTurning && Vector3.Dot(up, Vector3.up) > .999999f)
        {
            jumpWaiting = false;
            if (grounded && Time.time - jumpTookOff > .25f && !JumpQueued(controller)) ClearNativeJump();
            return false;
        }
        if (!jumpLeftGround && grounded && !JumpQueued(controller)) return false;
        jumpWaiting = true;
        if (!jumpExtra && !jumpTurning) return true;

        Quaternion target = Quaternion.FromToRotation(Vector3.up, up);
        float now = Time.fixedTime;
        if (!jumpTurning || Quaternion.Angle(jumpTarget, target) > .08f)
        {
            jumpFrom = jumpTilt;
            jumpViewFrom = jumpViewTilt;
            jumpTarget = target;
            jumpTurnStart = now - (jumpTurning ? 0 : Time.fixedDeltaTime);
            jumpBodyDuration = .055f + .00052f * Quaternion.Angle(jumpFrom, jumpTarget);
            jumpViewDuration = .19f + .00125f * Quaternion.Angle(jumpViewFrom, jumpTarget);
            jumpPose ??= new NativeRaidPlayerPose.JumpPose(controller, jumpAvatar);
            jumpTurning = true;
        }
        Quaternion tilt = Quaternion.Slerp(jumpFrom, jumpTarget,
            Mathf.SmoothStep(0, 1, (now-jumpTurnStart)/jumpBodyDuration));
        jumpPose.Rotate(tilt * Quaternion.Inverse(jumpTilt));
        jumpTilt = tilt;
        ApplyNativeJumpView(now);
        if (now >= jumpTurnStart + jumpViewDuration)
        {
            CommitNativeJump(controller);
            ClearNativeJump();
        }
        return true;
    }

    private void Update()
    {
        if (!jumpBody) return;
        if (!ValidJump(PlayerController.instance)) { ClearNativeJump(); return; }
        if (jumpTurning) ApplyNativeJumpView(Time.time);
    }

    private void ApplyNativeJumpView(float now)
    {
        if (!jumpCameraRoot && CameraPosition.instance)
        {
            jumpCameraRoot = CameraPosition.instance.transform.parent;
            if (jumpCameraRoot)
            {
                jumpCameraPosition = jumpCameraRoot.position;
                jumpCameraRotation = jumpCameraRoot.rotation;
            }
        }
        jumpViewTilt = Quaternion.Slerp(jumpViewFrom, jumpTarget,
            Mathf.SmoothStep(0, 1, (now-jumpTurnStart)/jumpViewDuration));
        if (!jumpCameraRoot || !jumpBody) return;
        Vector3 pivot = jumpBody.position;
        jumpCameraRoot.SetPositionAndRotation(pivot + jumpViewTilt*(jumpCameraPosition-pivot), jumpViewTilt*jumpCameraRotation);
    }

    private void CommitNativeJump(PlayerController controller)
    {
        Quaternion inverse = Quaternion.Inverse(jumpTilt);
        NativeRaidPlayerPose.FinishJump(controller, jumpAvatar, inverse);
        jumpTilt = Quaternion.identity;
        Rebase(controller.rb, controller.rb.position, inverse);
    }

    private void ClearNativeJump()
    {
        if (jumpPose != null && jumpTilt != Quaternion.identity) jumpPose.Rotate(Quaternion.Inverse(jumpTilt));
        if (jumpCameraRoot) jumpCameraRoot.SetPositionAndRotation(jumpCameraPosition, jumpCameraRotation);
        if (jumpCameraRoot) NativeRaidPlayerPose.SyncJumpEye(jumpAvatar);
        jumpCameraRoot = null;
        jumpBody = null;
        jumpAvatar = null;
        jumpPose = null;
        jumpLeftGround = jumpExtra = jumpWaiting = jumpTurning = false;
        jumpTilt = jumpFrom = jumpViewFrom = jumpViewTilt = jumpTarget = Quaternion.identity;
    }

    internal static void ReleaseNativeJumpForPortal()
    {
        if (active) active.ClearNativeJump();
    }

    internal static bool TurnNativeJump(Transform target, Quaternion native)
    {
        var owner = active;
        if (!owner || !owner.jumpTurning || !owner.ValidJump(PlayerController.instance) || target != owner.jumpBody.transform) return false;
        target.rotation = owner.jumpTilt * native;
        owner.jumpBody.rotation = target.rotation;
        return true;
    }

    internal static bool MoveNativeJump(Rigidbody body, Vector3 force, ForceMode mode)
    {
        var owner = active;
        if (!owner || !owner.jumpWaiting || body != owner.jumpBody || !owner.ValidJump(PlayerController.instance)) return false;
        body.AddForce(Vector3.ProjectOnPlane(body.rotation*force, owner.jumpUp), mode);
        return true;
    }

    internal static Vector3 NativeJumpImpulse(Rigidbody body, Vector3 impulse)
    {
        var owner = active;
        if (!owner || body != owner.jumpBody || !owner.jumpExtra || !owner.ValidJump(PlayerController.instance)) return impulse;
        owner.jumpExtra = false;
        if (!owner.jumpWaiting) return impulse;
        Vector3 velocity = body.linearVelocity;
        return owner.jumpUp * (impulse.y + velocity.y - Vector3.Dot(velocity, owner.jumpUp));
    }
}

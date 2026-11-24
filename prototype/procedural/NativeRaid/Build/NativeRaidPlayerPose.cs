using HarmonyLib;
using UnityEngine;

namespace Contour4.NativeRaid;

internal static class NativeRaidPlayerPose
{
    private static readonly AccessTools.FieldRef<PlayerAvatar, Rigidbody> AvatarBody =
        AccessTools.FieldRefAccess<PlayerAvatar, Rigidbody>("rb");
    private static readonly AccessTools.FieldRef<PlayerAvatar, Vector3> AvatarRawVelocity =
        AccessTools.FieldRefAccess<PlayerAvatar, Vector3>("rbVelocityRaw");
    private static readonly AccessTools.FieldRef<CameraAim, Quaternion> PlayerAim =
        AccessTools.FieldRefAccess<CameraAim, Quaternion>("playerAim");
    private static readonly AccessTools.FieldRef<PlayerLocalCamera, Vector3> PreviousEye =
        AccessTools.FieldRefAccess<PlayerLocalCamera, Vector3>("previousPosition");
    private static readonly AccessTools.FieldRef<PlayerAvatarVisuals, Vector3> VisualPosition =
        AccessTools.FieldRefAccess<PlayerAvatarVisuals, Vector3>("visualPosition");
    private static readonly AccessTools.FieldRef<PlayerAvatarVisuals, Vector3> VisualPrevious =
        AccessTools.FieldRefAccess<PlayerAvatarVisuals, Vector3>("positionLast");
    private static readonly AccessTools.FieldRef<SpringQuaternion, Quaternion> SpringRotation =
        AccessTools.FieldRefAccess<SpringQuaternion, Quaternion>("lastRotation");
    private static readonly AccessTools.FieldRef<SpringQuaternion, Vector3> SpringVelocity =
        AccessTools.FieldRefAccess<SpringQuaternion, Vector3>("springVelocity");

    internal static void Reframe(PlayerController controller, PlayerAvatar avatar, Camera mainCamera,
        Vector3 landing, Quaternion turn, bool keepStockView = false)
    {
        var body = controller.rb;
        Vector3 source = body.position;
        Vector3 Map(Vector3 point) => landing + turn * (point - source);
        var avatarBody = AvatarBody(avatar);
        Vector3 avatarPosition = avatarBody ? Map(avatarBody.position) : default;
        Quaternion avatarRotation = avatarBody ? turn * avatarBody.rotation : default;
        var visuals = avatar.playerAvatarVisuals;
        var aim = controller.cameraAim;
        Transform positionRig = CameraPosition.instance ? CameraPosition.instance.transform : null;
        Vector3 position = positionRig ? Map(positionRig.position) : default;
        Quaternion targetAim = aim ? turn * ParentRotation(aim.transform) * PlayerAim(aim) : default;

        // Take all samples before moving either hierarchy. The stock camera and the avatar spring
        // keep interpolating from the same lagged poses, expressed in the destination room.
        var avatarPose = new RenderPose(avatar.transform, source, landing, turn);
        var visualsPose = new RenderPose(visuals ? visuals.transform : null, source, landing, turn);
        var aimPose = new RenderPose(aim ? aim.transform : null, source, landing, turn);
        var noisePose = new RenderPose(CameraNoise.Instance ? CameraNoise.Instance.transform : null, source, landing, turn);
        var mainPose = new RenderPose(mainCamera ? mainCamera.transform : null, source, landing, turn);
        var localPose = new RenderPose(avatar.localCamera ? avatar.localCamera.transform : null, source, landing, turn);
        var vision = avatar.PlayerVisionTarget;
        var visionPose = new RenderPose(vision ? vision.VisionTransform : null, source, landing, turn);

        MoveBody(body, landing, turn * body.rotation, turn);
        if (avatarBody && avatarBody != body)
            MoveBody(avatarBody, avatarPosition, avatarRotation, turn);
        else if (avatar.transform != body.transform)
            avatarPose.Apply();
        controller.Velocity = turn * controller.Velocity;
        AvatarRawVelocity(avatar) = turn * AvatarRawVelocity(avatar);
        visualsPose.Apply();
        if (visuals)
        {
            VisualPosition(visuals) = Map(VisualPosition(visuals));
            VisualPrevious(visuals) = Map(VisualPrevious(visuals));
            visuals.bodySpringTarget = turn * visuals.bodySpringTarget;
            if (visuals.bodySpring != null)
            {
                SpringRotation(visuals.bodySpring) = turn * SpringRotation(visuals.bodySpring);
                SpringVelocity(visuals.bodySpring) = turn * SpringVelocity(visuals.bodySpring);
            }
        }

        if (positionRig && !keepStockView) positionRig.position = position;
        if (aim && !keepStockView)
        {
            aim.SetPlayerAim(Quaternion.Inverse(ParentRotation(aim.transform)) * targetAim, false);
            aimPose.Apply();
        }
        if (!keepStockView)
        {
            noisePose.Apply();
            mainPose.Apply();
            localPose.Apply();
        }
        if (avatar.localCamera)
            PreviousEye(avatar.localCamera) = Map(PreviousEye(avatar.localCamera));
        visionPose.Apply();
        Physics.SyncTransforms();
    }

    private static Quaternion ParentRotation(Transform transform) =>
        transform.parent ? transform.parent.rotation : Quaternion.identity;

    // Keep the native controller, remote body and the existing spring in one pose.
    // Captured recipients prevent an interrupted turn from tilting a replacement rig.
    internal sealed class JumpPose
    {
        private readonly Rigidbody controllerBody, avatarBody;
        private readonly PlayerAvatarVisuals visuals;
        private readonly SpringQuaternion spring;
        internal JumpPose(PlayerController controller, PlayerAvatar avatar)
        {
            controllerBody = controller.rb;
            avatarBody = AvatarBody(avatar);
            visuals = avatar.playerAvatarVisuals;
            spring = visuals ? visuals.bodySpring : null;
        }
        internal bool Matches(PlayerController controller, PlayerAvatar avatar) =>
            controller.rb == controllerBody && AvatarBody(avatar) == avatarBody && avatar.playerAvatarVisuals == visuals &&
            (!visuals || ReferenceEquals(visuals.bodySpring, spring));
        internal void Rotate(Quaternion turn)
        {
            void Body(Rigidbody body)
            {
                if (!body) return;
                body.rotation = turn * body.rotation;
                body.transform.rotation = body.rotation;
            }
            Body(controllerBody);
            if (avatarBody != controllerBody) Body(avatarBody);
            if (visuals)
            {
                visuals.transform.rotation = turn * visuals.transform.rotation;
                visuals.bodySpringTarget = turn * visuals.bodySpringTarget;
            }
            if (spring != null)
            {
                SpringRotation(spring) = turn * SpringRotation(spring);
                SpringVelocity(spring) = turn * SpringVelocity(spring);
            }
        }
    }

    internal static void FinishJump(PlayerController controller, PlayerAvatar avatar, Quaternion turn)
    {
        Vector3 pivot = controller.rb.position;
        Vector3 Map(Vector3 p) => pivot + turn * (p-pivot);
        var grabber = PhysGrabber.instance;
        bool grabbing = grabber && grabber.grabbed;
        Vector3 grip = grabbing ? Map(grabber.physGrabPointPosition) : default;
        Vector3 pull = grabbing ? Map(grabber.physGrabPointPullerPosition) : default;
        var gripPose = new RenderPose(grabbing ? grabber.physGrabPoint : null, pivot, pivot, turn);
        var pullPose = new RenderPose(grabbing ? grabber.physGrabPointPuller : null, pivot, pivot, turn);
        // The portal adapter already moves all native body/render interpolation caches atomically.
        // The jump camera parent is restored separately; mapping its children here would apply
        // the inverse a second time when that parent returns to the stock camera basis.
        Reframe(controller, avatar, null, pivot, turn, keepStockView: true);
        if (!grabbing) return;
        grabber.physGrabPointPosition = grip;
        grabber.physGrabPointPullerPosition = pull;
        gripPose.Apply();
        pullPose.Apply();
    }
    internal static void SyncJumpEye(PlayerAvatar avatar)
    {
        if (!avatar || !avatar.localCamera || !CameraNoise.Instance) return;
        Transform eye = CameraNoise.Instance.transform;
        avatar.localCamera.transform.SetPositionAndRotation(eye.position, eye.rotation);
        PreviousEye(avatar.localCamera) = eye.position;
    }

    private static void MoveBody(Rigidbody body, Vector3 position, Quaternion rotation, Quaternion turn)
    {
        Vector3 velocity = turn * body.linearVelocity;
        Vector3 spin = turn * body.angularVelocity;
        body.position = position;
        body.rotation = rotation;
        body.transform.SetPositionAndRotation(position, rotation);
        if (body.isKinematic) return;
        body.linearVelocity = velocity;
        body.angularVelocity = spin;
    }

    private readonly struct RenderPose
    {
        private readonly Transform transform;
        private readonly Vector3 position;
        private readonly Quaternion rotation;

        internal RenderPose(Transform transform, Vector3 source, Vector3 destination, Quaternion turn)
        {
            this.transform = transform;
            position = transform ? destination + turn * (transform.position - source) : default;
            rotation = transform ? turn * transform.rotation : default;
        }

        internal void Apply()
        {
            if (transform) transform.SetPositionAndRotation(position, rotation);
        }
    }
}

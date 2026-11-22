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
        Vector3 landing, Quaternion turn)
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

        if (positionRig) positionRig.position = position;
        if (aim)
        {
            aim.SetPlayerAim(Quaternion.Inverse(ParentRotation(aim.transform)) * targetAim, false);
            aimPose.Apply();
        }
        noisePose.Apply();
        mainPose.Apply();
        localPose.Apply();
        if (avatar.localCamera)
            PreviousEye(avatar.localCamera) = Map(PreviousEye(avatar.localCamera));
        visionPose.Apply();
        Physics.SyncTransforms();
    }

    private static Quaternion ParentRotation(Transform transform) =>
        transform.parent ? transform.parent.rotation : Quaternion.identity;

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

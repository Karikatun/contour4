using System.Reflection;
using Contour4.NativeRaid.Runtime;
using HarmonyLib;
using UnityEngine;

namespace Contour4.NativeRaid;

[DefaultExecutionOrder(1100)]
internal sealed class NativeRaidInteractions : MonoBehaviour
{
    private static readonly FieldInfo Disabled = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
    private static readonly FieldInfo Dead = AccessTools.Field(typeof(PlayerAvatar), "deadSet");
    private static readonly FieldInfo Tumbling = AccessTools.Field(typeof(PlayerAvatar), "isTumbling");
    private static readonly FieldInfo AvatarBody = AccessTools.Field(typeof(PlayerAvatar), "rb");
    private static readonly FieldInfo PlayerTumble = AccessTools.Field(typeof(PlayerAvatar), "tumble");
    private static readonly FieldInfo TumbleBody = AccessTools.Field(typeof(PlayerTumble), "rb");
    private RaidRoomInteractions interactions;
    private Rigidbody trackedBody;
    private Vector3 previousLocalFeet;
    private int occupiedLayers;

    internal static void Attach(RaidAssembly raid, TrenchShutterMode shutterMode)
    {
        if (raid.GetComponent<NativeRaidInteractions>()) return;
        var binding = raid.gameObject.AddComponent<NativeRaidInteractions>();
        binding.interactions = RaidRoomInteractions.Attach(raid, shutterMode);
        binding.occupiedLayers = LayerMask.GetMask("PhysGrabObject", "PhysGrabObjectCart", "PhysGrabObjectHinge", "Enemy", "Player");
        foreach (var front in binding.interactions.Cabinets) NativeRaidCabinetGrab.Install(front);
    }

    private static PlayerAvatar LiveAvatar()
    {
        if (!GameManager.instance || !LevelGenerator.Instance || GameManager.Multiplayer() || !SemiFunc.LevelGenDone() || !GameDirector.instance ||
            GameDirector.instance.currentState != GameDirector.gameState.Main) return null;
        var avatar = PlayerAvatar.instance;
        return avatar && avatar.playerHealth && !(bool)Disabled.GetValue(avatar) && !(bool)Dead.GetValue(avatar) ? avatar : null;
    }

    internal static Rigidbody FallBody(PlayerAvatar avatar, Rigidbody controllerBody, bool tumbling)
    {
        if (!tumbling) return controllerBody;
        if (PlayerTumble.GetValue(avatar) is PlayerTumble tumble && tumble &&
            TumbleBody.GetValue(tumble) is Rigidbody tumbleBody && tumbleBody) return tumbleBody;
        return AvatarBody.GetValue(avatar) is Rigidbody visualBody && visualBody ? visualBody : controllerBody;
    }

    private void OnDisable()
    {
        var grabber = PhysGrabber.instance;
        if (!interactions || !GameManager.instance || !PlayerController.instance || !grabber || !grabber.grabbed) return;
        foreach (var front in interactions.Cabinets)
        {
            if (!front.Body || grabber.grabbedObjectTransform != front.Body.transform) continue;
            grabber.ReleaseObject(-1);
            return;
        }
    }

    private void FixedUpdate()
    {
        var avatar = LiveAvatar();
        var controller = PlayerController.instance;
        if (!avatar || !controller || !controller.rb)
        {
            trackedBody = null;
            return;
        }
        bool tumbling = (bool)Tumbling.GetValue(avatar);
        var body = FallBody(avatar, controller.rb, tumbling);
        Vector3 feet = body.position;
        Vector3 previous = trackedBody == body ? transform.TransformPoint(previousLocalFeet) : feet;
        trackedBody = body;
        previousLocalFeet = transform.InverseTransformPoint(feet);

        if (interactions.CatchesFall(previous, feet) || NativeRaidGravity.FellInRotaryVoid(body))
        {
            avatar.PlayerDeath(-1);
            return;
        }
        if (interactions.Advance(feet, tumbling ? .60f : RaidAssembly.Character.Height, Time.fixedDeltaTime, occupiedLayers))
            avatar.playerHealth.Hurt(25, false, -1, false);
    }
}

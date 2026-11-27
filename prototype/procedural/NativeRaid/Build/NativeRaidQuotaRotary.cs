using System.Collections.Generic;
using Contour4.NativeRaid.Runtime;
using UnityEngine;

namespace Contour4.NativeRaid;

internal sealed class NativeRaidQuotaRotary : MonoBehaviour
{
    private RaidQuotaRotaryCabin cabin;
    private NativeRaidGravity gravity;
    private NativeRaidRotaryControls controls;

    internal static void Install(RaidAssembly raid, BakedRoom room)
    {
        var ground = room.Root.Find("ground").GetComponent<Renderer>();
        var bulkhead = room.Root.Find("near-rupture-left").GetComponent<Renderer>();
        float degrees = RaidQuotaRotaryRoom.TurnDegrees(raid.Plan.Seed);
        var cabin = RaidQuotaRotaryCabin.Create(room.Root, RaidQuotaRotaryRoom.Center, RaidQuotaRotaryRoom.NearRotation,
            RaidQuotaRotaryRoom.FarField, ground.gameObject.layer, ground.sharedMaterial, bulkhead.sharedMaterial, degrees);
        var interaction = cabin.gameObject.AddComponent<NativeRaidQuotaRotary>();
        interaction.cabin = cabin;
        interaction.gravity = raid.GetComponent<NativeRaidGravity>();
        interaction.gravity.BindRotaryCabin(cabin);
        interaction.controls = NativeRaidRotaryControls.Create(cabin, bulkhead.sharedMaterial, ground.gameObject.layer);
        AddLamp(room.Root, "Near docking work light", new Vector3(4.55f, 3.05f, 0), 6.5f, 2.4f,
            new Color(1f, .83f, .63f));
        AddLamp(cabin.Rotor, "Cabin work light", new Vector3(0, 1.4f, 1.5f), 4.5f, .9f,
            new Color(.78f, .87f, 1f));
        Transform far = cabin.DockFrame(true);
        AddLamp(far, "Far landing work light", new Vector3(1.8f, 3.8f, 5.2f),
            8f, 1.65f, new Color(.73f, .88f, 1f));
        // Light the whole stock loading face, including the display and the ramp.
        Light quota = AddLamp(far, "Quota loading work light", new Vector3(0, 2.20f, 6.45f),
            7.5f, 3.7f, new Color(1f, .86f, .68f));
        quota.renderMode = LightRenderMode.ForcePixel;
        Physics.SyncTransforms();
    }

    private static Light AddLamp(Transform parent, string name, Vector3 position, float range, float intensity, Color color)
    {
        var lamp = RaidAssembly.Child(parent, name);
        lamp.localPosition = position;
        var light = lamp.gameObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = range;
        light.intensity = intensity;
        light.color = color;
        return light;
    }

    private void Update()
    {
        controls.SetTurning(cabin.IsTurning);
        var controller = PlayerController.instance;
        if (cabin.IsTurning || !controller || !controller.rb ||
            !GameDirector.instance || GameDirector.instance.currentState != GameDirector.gameState.Main ||
            !CameraNoise.Instance || !SemiFunc.InputDown(InputKey.Interact)) return;

        Transform eye = CameraNoise.Instance.transform;
        Vector3 feet = controller.rb.position;
        if (cabin.CanBoard(feet) && AimedAt(eye, controls.Transfer)) gravity.RequestRotaryTrip(cabin);
        else if (cabin.CanCall(false, feet) && AimedAt(eye, controls.NearRecall, localSight: true)) gravity.RequestRotaryTrip(cabin, false);
        else if (cabin.CanCall(true, feet) && AimedAt(eye, controls.FarRecall, localSight: true)) gravity.RequestRotaryTrip(cabin, true);
    }

    private static bool AimedAt(Transform eye, Transform button, bool localSight = false)
    {
        Vector3 toButton = button.position - eye.position;
        if (toButton.sqrMagnitude > 6f || Vector3.Dot(toButton.normalized, eye.forward) < .78f) return false;
        return !localSight || !Physics.Raycast(eye.position, toButton.normalized, toButton.magnitude,
            1 << button.gameObject.layer, QueryTriggerInteraction.Ignore);
    }
}

internal sealed partial class NativeRaidGravity
{
    private RaidQuotaRotaryCabin rotaryCabin, requestedCabin;
    private bool rotaryWithoutController;
    private bool? requestedDock;

    internal void BindRotaryCabin(RaidQuotaRotaryCabin cabin) => rotaryCabin = cabin;

    internal void RequestRotaryTrip(RaidQuotaRotaryCabin cabin, bool? dock = null)
    {
        var avatar = PlayerAvatar.instance;
        if (actorsReady && cabin == rotaryCabin && avatar && !AvatarDead(avatar) && !AvatarTumbling(avatar))
        {
            requestedCabin = cabin;
            requestedDock = dock;
        }
    }

    private bool TryRotaryUp(Vector3 point, out Vector3 up)
    {
        if (rotaryCabin) return rotaryCabin.TryUp(point, out up);
        up = default;
        return false;
    }

    private bool StepRotaryCabin(Rigidbody player)
    {
        if (!rotaryCabin) return false;
        if (requestedCabin)
        {
            bool Eligible(Rigidbody body) => EligibleActor(body, player, PlayerAvatar.instance);
            if (requestedDock is bool dock) rotaryCabin.TryCall(player, dock, Eligible);
            else
            {
                var grabber = PhysGrabber.instance;
                bool grabbing = grabber && grabber.grabbed;
                Rigidbody held = grabbing && grabber.grabbedObjectTransform
                    ? grabber.grabbedObjectTransform.GetComponentInParent<Rigidbody>() : null;
                if ((!grabbing || held) && rotaryCabin.TryBegin(player, held, Eligible)) ClearNativeJump();
            }
            requestedCabin = null;
            requestedDock = null;
        }

        if (rotaryCabin.IsTurning)
        {
            RefreshActors(player);
            foreach (var body in rotaryCabin.Passengers)
                if (body && body != player && !actors.Contains(body)) actors.Add(body);
            if (rotaryCabin.Step(Time.fixedDeltaTime, player,
                (feet, rotation, cargo) => Rebase(player, feet, rotation, cargo), MoveDetachedCartHistory)) return true;
        }

        if (!player || !TryRotaryUp(player.position, out Vector3 up)) return false;
        if (Vector3.Dot(up, Vector3.up) < .999999f)
        {
            Quaternion rotation = rotaryCabin.LevelFrom(up);
            Vector3 velocity = player.linearVelocity;
            Vector3 spin = player.angularVelocity;
            Rebase(player, player.position, rotation);
            if (!player.isKinematic)
            {
                player.linearVelocity = rotation * velocity;
                player.angularVelocity = rotation * spin;
            }
        }
        return true;
    }

    private void MoveDetachedCartHistory(Vector3 pivot, Quaternion turn, ISet<Rigidbody> cargo)
    {
        foreach (var body in cargo)
            if (body && carts.TryGetValue(body, out var cart) && cart)
                TransformCartHistory(cart, point => pivot + turn * (point - pivot), turn);
    }
}

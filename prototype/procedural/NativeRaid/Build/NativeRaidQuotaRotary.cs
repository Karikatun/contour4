using System.Collections.Generic;
using Contour4.NativeRaid.Runtime;
using UnityEngine;

namespace Contour4.NativeRaid;

internal sealed class NativeRaidQuotaRotary : MonoBehaviour
{
    private RaidQuotaRotaryCabin cabin;
    private NativeRaidGravity gravity;
    private Transform nearRecall, farRecall;
    private Light indicator;

    internal static void Install(RaidAssembly raid, BakedRoom room)
    {
        var ground = room.Root.Find("ground").GetComponent<Renderer>();
        var bulkhead = room.Root.Find("near-rupture-left").GetComponent<Renderer>();
        var cabin = RaidQuotaRotaryCabin.Create(room.Root, RaidQuotaRotaryRoom.Center, RaidQuotaRotaryRoom.NearRotation,
            RaidQuotaRotaryRoom.FarField, ground.gameObject.layer, ground.sharedMaterial, bulkhead.sharedMaterial, bulkhead.sharedMaterial);
        var controls = cabin.gameObject.AddComponent<NativeRaidQuotaRotary>();
        controls.cabin = cabin;
        controls.gravity = raid.GetComponent<NativeRaidGravity>();
        controls.gravity.BindRotaryCabin(cabin);
        controls.InstallPanel();
        controls.nearRecall = controls.InstallRecallSwitch(false, bulkhead.sharedMaterial, ground.gameObject.layer);
        controls.farRecall = controls.InstallRecallSwitch(true, bulkhead.sharedMaterial, ground.gameObject.layer);
        AddLamp(room.Root, "Near docking work light", new Vector3(3.7f, 3.7f, .7f), 8.5f, 1.45f,
            new Color(1f, .83f, .63f));
        Pose far = RaidQuotaRotaryCabin.Doorway(RaidQuotaRotaryRoom.Center, RaidQuotaRotaryRoom.NearRotation, true);
        AddLamp(room.Root, "Far landing work light", far.position + far.rotation * new Vector3(1.8f, 3.8f, 5.2f),
            11f, 1.7f, new Color(.73f, .88f, 1f));
        Physics.SyncTransforms();
    }

    private void InstallPanel()
    {
        // The recessed panel sits at the middle of the cabin, clear of the freight path.
        Transform labelNode = RaidAssembly.Child(cabin.Rotor, "Transfer label");
        labelNode.localPosition = cabin.Button.localPosition + new Vector3(-.11f, .15f, 0);
        labelNode.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
        AddLabel(labelNode, "ПОВОРОТ");
        indicator = AddLamp(cabin.Rotor, "Transfer status", cabin.Button.localPosition + new Vector3(-.36f, -.10f, 0),
            1.7f, .65f, Color.green);
    }

    private Transform InstallRecallSwitch(bool far, Material material, int layer)
    {
        Transform dock = cabin.DockFrame(far);
        var button = GameObject.CreatePrimitive(PrimitiveType.Cube);
        button.name = far ? "Far recall switch" : "Near recall switch";
        button.layer = layer;
        button.GetComponent<BoxCollider>().enabled = false;
        button.GetComponent<Renderer>().sharedMaterial = material;
        Transform panel = button.transform;
        panel.SetParent(cabin.transform, false);
        Vector3 position = dock.TransformPoint(far ? new Vector3(2.55f, 1.23f, 3.25f) : new Vector3(1.72f, 1.23f, -1.55f));
        Quaternion rotation = Quaternion.LookRotation(dock.right, dock.up);
        panel.SetPositionAndRotation(position, rotation);
        panel.localScale = new Vector3(.58f, .50f, .16f);

        var bracket = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bracket.name = "Recall switch pedestal";
        bracket.GetComponent<BoxCollider>().enabled = false;
        bracket.GetComponent<Renderer>().sharedMaterial = material;
        bracket.transform.SetParent(cabin.transform, false);
        bracket.transform.SetPositionAndRotation(position - dock.up * .62f, rotation);
        bracket.transform.localScale = new Vector3(.14f, 1.24f, .14f);

        Transform label = RaidAssembly.Child(cabin.transform, "Recall label");
        label.SetPositionAndRotation(position - panel.forward * .091f, rotation);
        AddLabel(label, "ВЫЗОВ");
        return panel;
    }

    private static void AddLabel(Transform labelNode, string text)
    {
        var label = labelNode.gameObject.AddComponent<TextMesh>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.GetComponent<Renderer>().sharedMaterial = label.font.material;
        label.text = text;
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.characterSize = .055f;
        label.fontSize = 64;
        label.color = Color.white;

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
        indicator.color = cabin.IsTurning ? new Color(1f, .43f, .12f) : new Color(.40f, .95f, .65f);
        var controller = PlayerController.instance;
        if (cabin.IsTurning || !controller || !controller.rb ||
            !GameDirector.instance || GameDirector.instance.currentState != GameDirector.gameState.Main ||
            !CameraNoise.Instance || !SemiFunc.InputDown(InputKey.Interact)) return;

        Transform eye = CameraNoise.Instance.transform;
        Vector3 feet = controller.rb.position;
        if (cabin.CanBoard(feet) && AimedAt(eye, cabin.Button)) gravity.RequestRotaryTrip(cabin);
        else if (cabin.CanCall(false, feet) && AimedAt(eye, nearRecall, localSight: true)) gravity.RequestRotaryTrip(cabin, false);
        else if (cabin.CanCall(true, feet) && AimedAt(eye, farRecall, localSight: true)) gravity.RequestRotaryTrip(cabin, true);
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
            Quaternion rotation = Quaternion.FromToRotation(up, Vector3.up);
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

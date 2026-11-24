using System;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using UnityEngine;

namespace Contour4.NativeRaid;

internal sealed class NativeRaidStartBay : MonoBehaviour
{
    // The native starter's six floor cells form a 15 × 10 m apron. The truck,
    // first extraction, player spawns and map markers are outside the dressing.
    private const float BayWidth = 15f, BayDepth = 10f, WallThickness = .24f;
    private const float TruckOpening = 4.65f, TruckHeadroom = 4.55f;
    private Material floor, structure, equipment, accent;

    internal static void Install(LevelGenerator generator, RaidPlan.End entrance, int seed)
    {
        var starter = generator.LevelParent.GetComponentsInChildren<StartRoom>().Single();
        var root = starter.transform;
        if (root.GetComponentInChildren<NativeRaidStartBay>()) return;
        Vector3 sill = root.InverseTransformPoint(entrance.Sill);
        if (sill.sqrMagnitude > .01f || Vector3.Dot(root.forward, -entrance.Outward) < .999f)
            throw new InvalidOperationException("The launch bay does not align with the native truck floor.");

        var manorDressing = new[] { root.Find("Props"), root.Find("Walls"), root.Find("Ceiling") };
        Transform floors = root.Find("Floors");
        if (manorDressing.Any(part => !part) || !floors || floors.GetComponentsInChildren<BoxCollider>().Length != 6)
            throw new InvalidOperationException("The native launch floor or dressing has changed.");
        var connector = generator.Level.ConnectObject.Prefab;
        var manorDoor = connector ? generator.LevelParent.transform.Cast<Transform>().SingleOrDefault(child =>
            child.name == connector.name + "(Clone)" && (child.position - entrance.Sill).sqrMagnitude < .1f) : null;

        bool serviceBay = (seed & 1) != 0;
        string id = serviceBay ? "service-garage" : "pressure-dock";
        var bay = RaidAssembly.Child(root, "Contour-4 start " + id).gameObject.AddComponent<NativeRaidStartBay>();
        bay.Build(serviceBay, entrance.Dock.Opening.X, entrance.Dock.Opening.Y);
        foreach (var part in manorDressing) part.gameObject.SetActive(false);
        foreach (var renderer in floors.GetComponentsInChildren<Renderer>())
            renderer.enabled = false;
        if (manorDoor) manorDoor.gameObject.SetActive(false);
        Physics.SyncTransforms();
        Debug.Log($"[Contour4 Start] seed={seed}, bay={id}, apron=15x10m, door={entrance.Dock.Opening.X:F2}x{entrance.Dock.Opening.Y:F2}m.");
    }

    private void Build(bool serviceBay, float entranceWidth, float entranceHeight)
    {
        var shader = Shader.Find("Standard");
        if (!shader) throw new InvalidOperationException("The launch bay requires Unity's Standard shader.");
        floor = Paint(shader, new Color(.19f, .23f, .26f));
        structure = Paint(shader, new Color(.37f, .43f, .47f));
        equipment = Paint(shader, new Color(.13f, .17f, .19f));
        var tint = serviceBay ? new Color(.84f, .49f, .15f) : new Color(.26f, .70f, .77f);
        accent = Paint(shader, tint);
        accent.EnableKeyword("_EMISSION");
        accent.SetColor("_EmissionColor", tint * .6f);

        float height = serviceBay ? 5.35f : 4.8f;
        Box("Apron surface", floor, 0, -.038f, -BayDepth / 2, BayWidth, .1f, BayDepth, solid: false);
        Box("Bay ceiling", structure, 0, height + WallThickness / 2, -BayDepth / 2, BayWidth, WallThickness, BayDepth);
        foreach (float side in new[] { -1f, 1f })
        {
            Box("Outer wall", structure, side * (BayWidth - WallThickness) / 2, height / 2, -BayDepth / 2,
                WallThickness, height, BayDepth);
            Box("Lane edge", accent, side * 2.04f, .015f, -4f, .06f, .004f, 7f, solid: false);
        }
        EndWall("Station threshold", entranceWidth, entranceHeight, height, -WallThickness / 2);
        EndWall("Truck threshold", TruckOpening, TruckHeadroom, height, -BayDepth + WallThickness / 2);

        if (serviceBay)
        {
            Box("Service counter", equipment, 6.55f, .5f, -5.65f, 1.45f, 1f, 3.8f);
            Box("Equipment backboard", structure, 7.18f, 1.75f, -5.65f, .28f, 1.5f, 3.8f);
            Box("Tool drawers", structure, 6.55f, 1.3f, -6.65f, .67f, .6f, 1.3f);
            Box("Service gantry", equipment, 0, height - .15f, -5.1f, BayWidth - WallThickness, .3f, .42f);
        }
        else
        {
            Box("Pressure control", equipment, 6.72f, 1.12f, -5.75f, 1.15f, 2.24f, 1.85f);
            Box("Pressure control display", accent, 6.12f, 1.67f, -5.75f, .06f, .32f, 1.20f, solid: false);
            Box("Duct trunk", equipment, 5.2f, height - .20f, -5.0f, 1.5f, .4f, 9.1f);
            Box("Duct trunk", equipment, -5.2f, height - .20f, -5.0f, 1.5f, .4f, 9.1f);
        }

        LightAt("Apron lighting", new Vector3(-2.0f, height - .3f, -6.2f), 10f, 1.25f);
        LightAt("Entrance lighting", new Vector3(1.45f, height - .3f, -2.0f), 8f, 1.15f);
        RaidLighting.Brighten(transform);
    }

    private void EndWall(string name, float width, float openingHeight, float height, float z)
    {
        float jambWidth = (BayWidth - width) / 2;
        foreach (float side in new[] { -1f, 1f })
        {
            float center = side * (width + jambWidth) / 2;
            Box(name + " jamb", structure, center, height / 2, z, jambWidth, height, WallThickness);
            Box(name + " signal", accent, side * (width / 2 + .14f), 2.08f, z + .13f,
                .12f, 1.4f, .028f, solid: false);
        }
        float header = height - openingHeight;
        Box(name + " header", equipment, 0, openingHeight + header / 2, z, width, header, WallThickness);
    }

    private void Box(string name, Material material, float x, float y, float z,
        float width, float height, float depth, bool solid = true)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.layer = LayerMask.NameToLayer("Default");
        part.transform.SetParent(transform, false);
        part.transform.localPosition = new Vector3(x, y, z);
        part.transform.localScale = new Vector3(width, height, depth);
        part.GetComponent<MeshRenderer>().sharedMaterial = material;
        if (solid) return;
        var collider = part.GetComponent<BoxCollider>();
        collider.enabled = false;
        Destroy(collider);
    }

    private void LightAt(string name, Vector3 position, float range, float intensity)
    {
        var node = RaidAssembly.Child(transform, name);
        node.localPosition = position;
        var light = node.gameObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(.87f, .94f, 1f);
        light.range = range;
        light.intensity = intensity;
    }

    private static Material Paint(Shader shader, Color color) => new(shader) { color = color };

    private void OnDestroy()
    {
        Destroy(floor);
        Destroy(structure);
        Destroy(equipment);
        Destroy(accent);
    }
}

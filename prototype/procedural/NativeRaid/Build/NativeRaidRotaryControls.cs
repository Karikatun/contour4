using System.Collections.Generic;
using Contour4.NativeRaid.Runtime;
using UnityEngine;

namespace Contour4.NativeRaid;

// Visuals are deliberately non-solid. The cabin and landing own all collision.
internal sealed class NativeRaidRotaryControls : MonoBehaviour
{
    private readonly List<Transform> pinions = new();
    private readonly List<float> pinionPhases = new();
    private readonly List<Material> ownedMaterials = new();
    private readonly List<Mesh> ownedMeshes = new();
    private Material capMaterial;
    private bool turning, colorInitialized;
    internal Transform Transfer { get; private set; }
    internal Transform NearRecall { get; private set; }
    internal Transform FarRecall { get; private set; }

    internal static NativeRaidRotaryControls Create(RaidQuotaRotaryCabin cabin, Material metal, int layer)
    {
        var art = cabin.gameObject.AddComponent<NativeRaidRotaryControls>();
        var dark = art.MakeMaterial(metal, new Color(.16f, .18f, .18f));
        var steel = art.MakeMaterial(metal, new Color(.57f, .58f, .55f));
        var rim = art.MakeMaterial(metal, new Color(.32f, .34f, .32f));
        var pinionSteel = art.MakeMaterial(metal, new Color(.78f, .73f, .63f));
        pinionSteel.EnableKeyword("_EMISSION");
        if (pinionSteel.HasProperty("_EmissionColor")) pinionSteel.SetColor("_EmissionColor", new Color(.10f, .085f, .065f));
        art.capMaterial = art.MakeMaterial(metal, Color.green);
        art.capMaterial.EnableKeyword("_EMISSION");
        art.Transfer = art.Button(cabin.Rotor, new Vector3(2.06f, -.40f, 0f),
            Quaternion.LookRotation(Vector3.left, Vector3.up), steel, dark, layer, false);
        art.NearRecall = art.Button(cabin.DockFrame(false), new Vector3(1.72f, 1.23f, -1.55f),
            Quaternion.LookRotation(Vector3.back, Vector3.up), steel, dark, layer, true);
        art.FarRecall = art.Button(cabin.DockFrame(true), new Vector3(2.55f, 1.23f, 3.25f),
            Quaternion.LookRotation(Vector3.forward, Vector3.up), steel, dark, layer, true);
        art.Decorate(cabin, steel, rim, pinionSteel, layer);
        art.DressRuinedApproach(cabin, steel, dark, metal, layer);
        art.SetTurning(false);
        return art;
    }

    internal void SetTurning(bool value)
    {
        if (colorInitialized && turning == value) return;
        turning = value;
        colorInitialized = true;
        Color c = value ? new Color(1f, .32f, .035f) : new Color(.04f, .9f, .12f);
        capMaterial.color = c;
        if (capMaterial.HasProperty("_EmissionColor")) capMaterial.SetColor("_EmissionColor", c * .65f);
    }

    private Material MakeMaterial(Material template, Color color)
    {
        // A plain lit metal material avoids inheriting a wall's concrete albedo.
        Shader shader = Shader.Find("Standard");
        var result = shader ? new Material(shader) : new Material(template);
        result.color = color;
        if (result.HasProperty("_Metallic")) result.SetFloat("_Metallic", .48f);
        if (result.HasProperty("_Glossiness")) result.SetFloat("_Glossiness", .38f);
        ownedMaterials.Add(result);
        return result;
    }

    private Transform Button(Transform parent, Vector3 position, Quaternion rotation, Material steel, Material dark, int layer, bool pedestal)
    {
        Transform panel = Node(parent, "Rotary button", position, rotation, layer);
        var body = new MeshParts();
        body.Box(Vector3.zero, new Vector3(.48f, .48f, .16f));
        body.Box(new Vector3(0, 0, .10f), new Vector3(.39f, .39f, .06f));
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                body.Cylinder(new Vector3(x * .185f, y * .185f, .141f), .025f, .018f, 8);
        ownedMeshes.Add(body.Render(panel, "Button housing and bolts", steel, layer));
        var bezel = new MeshParts();
        bezel.Cylinder(new Vector3(0, 0, .15f), .152f, .05f, 16);
        ownedMeshes.Add(bezel.Render(panel, "Button bezel", dark, layer));
        var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        cap.name = "Rounded illuminated cap";
        cap.layer = layer;
        cap.GetComponent<Collider>().enabled = false;
        Destroy(cap.GetComponent<Collider>());
        cap.transform.SetParent(panel, false);
        cap.transform.localPosition = new Vector3(0, 0, .195f);
        cap.transform.localScale = new Vector3(.245f, .245f, .105f);
        cap.GetComponent<Renderer>().sharedMaterial = capMaterial;
        // Cabling remains entirely in the same moving or stationary frame as the switch.
        var cable = new MeshParts();
        if (pedestal)
        {
            cable.Box(new Vector3(0, -.72f, -.08f), new Vector3(.10f, 1.05f, .10f));
            cable.Box(new Vector3(0, -.29f, -.08f), new Vector3(.055f, .25f, .055f));
            cable.Box(new Vector3(-.33f, -1.21f, -.08f), new Vector3(.72f, .055f, .055f));
        }
        else
        {
            cable.Box(new Vector3(0, -.56f, -.08f), new Vector3(.055f, .70f, .055f));
            cable.Box(new Vector3(-1.48f, -.90f, -.08f), new Vector3(3.0f, .055f, .055f));
        }
        ownedMeshes.Add(cable.Render(panel, "Switch conduit", dark, layer));
        return cap.transform;
    }

    private void Decorate(RaidQuotaRotaryCabin cabin, Material steel, Material dark, Material pinionSteel, int layer)
    {
        // 120:15 internal gear pair. Pitch radii 3.00 and .375 m put the
        // pinion centres at 2.625 m. Raised lower axles clear the solid floor.
        const float z = -3.52f;
        var ring = new MeshParts();
        ring.Gear(Vector3.zero, 120, 3.00f, .04f, 3.16f, .12f, true, 0f);
        Transform fixedRing = Node(cabin.transform, "Fixed internal ring gear", new Vector3(0, 0, z), Quaternion.identity, layer);
        ownedMeshes.Add(ring.Render(fixedRing, "Fixed toothed ring", dark, layer));
        var rim = new MeshParts();
        rim.Annulus(Vector3.zero, 3.10f, 3.17f, .15f, 96);
        ownedMeshes.Add(rim.Render(fixedRing, "Ring rim", steel, layer));
        foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 })
        {
            Vector2 corner = new Vector2(x * Mathf.Sqrt(2.625f * 2.625f - 1.05f * 1.05f), y * 1.05f);
            float angle = Mathf.Atan2(corner.y, corner.x);
            var mount = Node(cabin.Rotor, "Pinion mount", new Vector3(corner.x, corner.y, z), Quaternion.identity, layer);
            var bracket = new MeshParts();
            // The bearing is connected back to the structural corner, not floating.
            bracket.Box(new Vector3(-x * .10f, y * .30f, .07f), new Vector3(.28f, .76f, .12f));
            bracket.Cylinder(new Vector3(0, 0, .22f), .115f, .56f, 12);
            ownedMeshes.Add(bracket.Render(mount, "Axle and corner bracket", steel, layer));
            Transform wheel = Node(mount, "Driven pinion", Vector3.zero, Quaternion.identity, layer);
            var gear = new MeshParts();
            gear.Gear(Vector3.zero, 15, .375f, .055f, .23f, .10f, false, 0f);
            gear.Cylinder(new Vector3(0, 0, -.07f), .15f, .06f, 12);
            gear.Cylinder(new Vector3(0, 0, -.105f), .085f, .025f, 8);
            ownedMeshes.Add(gear.Render(wheel, "Pinion teeth and hub", pinionSteel, layer));
            pinions.Add(wheel);
            pinionPhases.Add(-7f * angle * Mathf.Rad2Deg);
        }
    }

    private void DressRuinedApproach(RaidQuotaRotaryCabin cabin, Material steel, Material dark, Material wall, int layer)
    {
        // Flat, non-solid edge markings follow the actual shallow inclined deck.
        var warning = MakeMaterial(steel, new Color(.95f, .58f, .10f));
        warning.EnableKeyword("_EMISSION");
        if (warning.HasProperty("_EmissionColor")) warning.SetColor("_EmissionColor", new Color(.20f, .10f, .015f));
        void RampTrim(float z, float y, float length, float angle)
        {
            Transform ramp = Node(cabin.transform, "Ramp edge fittings", new Vector3(0, y, z), Quaternion.Euler(angle, 0, 0), layer);
            var rail = new MeshParts();
            var stripe = new MeshParts();
            foreach (int side in new[] { -1, 1 })
            {
                rail.Box(new Vector3(side * 2.16f, .012f, 0), new Vector3(.18f, .025f, length));
                stripe.Box(new Vector3(side * 2.16f, .028f, 0), new Vector3(.10f, .01f, length));
                for (float d = -length / 2 + .18f; d < length / 2; d += .50f)
                    rail.Cylinder(new Vector3(side * 2.16f, .035f, d), .037f, .018f, 8);
            }
            ownedMeshes.Add(rail.Render(ramp, "Ramp metal edge and rivets", steel, layer));
            ownedMeshes.Add(stripe.Render(ramp, "Amber safety edges", warning, layer));
        }
        RampTrim(-6.5f, -1.71f, 2f, 3.43f);
        RampTrim(-4.3f, -1.71f, 2.4f, -2.86f);

        void WorkLight(string name, Vector3 position, Color color, float range, float intensity)
        {
            Transform node = Node(cabin.transform, name, position, Quaternion.identity, layer);
            var light = node.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }
        // Fill the square facade corners up to the OUTSIDE of the toothed ring.
        // Thin visual spandrels do not seal the rotating chamber behind the ring.
        var shoulders = new MeshParts();
        shoulders.RingShoulders(3.25f, 3.20f, -1.65f, 3.20f, -3.86f);
        ownedMeshes.Add(shoulders.Render(cabin.transform, "Concrete ring spandrels", wall, layer));

        // Irregular visible fracture edges partially overlap the two genuine holes.
        var holeEdges = new MeshParts();
        holeEdges.WallTriangle(new Vector3(4.05f, -.25f, -3.88f), new Vector3(4.52f, -.25f, -3.88f), new Vector3(4.05f, .12f, -3.88f));
        holeEdges.WallTriangle(new Vector3(4.85f, .85f, -3.88f), new Vector3(4.40f, .85f, -3.88f), new Vector3(4.85f, .50f, -3.88f));
        holeEdges.WallTriangle(new Vector3(-4.05f, -.50f, -3.88f), new Vector3(-4.55f, -.50f, -3.88f), new Vector3(-4.05f, -.12f, -3.88f));
        holeEdges.WallTriangle(new Vector3(-4.85f, .60f, -3.88f), new Vector3(-4.36f, .60f, -3.88f), new Vector3(-4.85f, .23f, -3.88f));
        ownedMeshes.Add(holeEdges.Render(cabin.transform, "Broken hole edges", wall, layer));

        // These two meshes share the room's painted material but are created
        // after the room finish pass. Supply the same room-space projection as
        // the surrounding facade so the plaster does not sample a fixed texel.
        foreach (string name in new[] { "Concrete ring spandrels", "Broken hole edges" })
        {
            var surface = cabin.transform.Find(name).GetComponent<MeshRenderer>();
            var properties = new MaterialPropertyBlock();
            Matrix4x4 frame = cabin.transform.parent.worldToLocalMatrix * surface.transform.localToWorldMatrix;
            properties.SetMatrix("_WearFrame", frame);
            properties.SetMatrix("_WearInverseFrame", frame.inverse);
            properties.SetVector("_WearProfile", new Vector4(.71f, .23f, 0f, 1f));
            properties.SetVector("_WallUpA", new Vector4(0, 1, 0, 0));
            properties.SetVector("_WallUpB", new Vector4(0, 1, 0, 0));
            properties.SetVector("_WallAcross", new Vector4(0, 0, 1, 0));
            surface.SetPropertyBlock(properties);
        }

        // Old steel seams and brackets break up the otherwise flat facade.
        var seams = new MeshParts();
        foreach (int side in new[] { -1, 1 })
        {
            seams.Box(new Vector3(side * 3.53f, .30f, -3.89f), new Vector3(.055f, 1.25f, .035f));
            seams.Box(new Vector3(side * 3.53f, .87f, -3.90f), new Vector3(.32f, .08f, .055f));
            seams.Box(new Vector3(side * 3.53f, -.25f, -3.90f), new Vector3(.32f, .08f, .055f));
        }
        ownedMeshes.Add(seams.Render(cabin.transform, "Aged wall seams and staples", dark, layer));

        // Pale, irregular fracture caps sit on the low collidable masonry remnants.
        var fracture = MakeMaterial(steel, new Color(.68f, .64f, .56f));
        if (fracture.HasProperty("_Metallic")) fracture.SetFloat("_Metallic", 0f);
        fracture.EnableKeyword("_EMISSION");
        if (fracture.HasProperty("_EmissionColor")) fracture.SetColor("_EmissionColor", new Color(.09f, .08f, .065f));
        var cuts = new MeshParts();
        // x is the cabin-local lateral axis; z is along the entry approach.
        cuts.Fracture(4.25f, 4.90f, -6.8f, -6.35f, 1.12f - 1.65f, .19f, .04f);
        cuts.Fracture(3.55f, 4.25f, -6.8f, -6.35f, .72f - 1.65f, .04f, .17f);
        cuts.Fracture(2.75f, 3.55f, -6.8f, -6.35f, .53f - 1.65f, .11f, .03f);
        cuts.Fracture(-5f, -4.35f, -5.4f, -4.95f, .96f - 1.65f, .06f, .20f);
        cuts.Fracture(-4.35f, -3.55f, -5.4f, -4.95f, .63f - 1.65f, .14f, .03f);
        cuts.Fracture(-3.55f, -2.75f, -5.4f, -4.95f, .46f - 1.65f, .04f, .13f);
        ownedMeshes.Add(cuts.Render(cabin.transform, "Exposed chipped concrete", fracture, layer));
        var bars = new MeshParts();
        bars.Box(new Vector3(4.20f, 1.0f - 1.65f, -6.56f), new Vector3(.045f, .73f, .045f));
        bars.Box(new Vector3(-4.28f, .80f - 1.65f, -5.16f), new Vector3(.045f, .61f, .045f));
        ownedMeshes.Add(bars.Render(cabin.transform, "Exposed reinforcement", dark, layer));

        // Modest contrasting fills illuminate the exposed ring without a backing wall.
        // The repair cabinet is fixed to the solid left shelter. Its door hangs
        // open toward the wall, exposing a dark instrument panel and cable runs.
        var repair = new MeshParts();
        // Leave the actual enameled cabinet face visible, including its contact trace.
        repair.Box(new Vector3(4.00f, .40f, -6.88f), new Vector3(1.50f, .10f, .16f));
        repair.Box(new Vector3(3.31f, -.28f, -6.88f), new Vector3(.09f, 1.42f, .16f));
        repair.Box(new Vector3(4.69f, -.28f, -6.88f), new Vector3(.09f, 1.42f, .16f));
        repair.Box(new Vector3(4.0f, -.14f, -6.93f), new Vector3(.93f, .12f, .04f));
        repair.Box(new Vector3(3.68f, .65f, -6.72f), new Vector3(.08f, .50f, .08f));
        repair.Box(new Vector3(4.16f, .65f, -6.72f), new Vector3(.08f, .50f, .08f));
        repair.Box(new Vector3(3.92f, .88f, -6.72f), new Vector3(.56f, .08f, .08f));
        ownedMeshes.Add(repair.Render(cabin.transform, "Open lift service switchboard", dark, layer));
        Transform door = Node(cabin.transform, "Hanging service cabinet door", new Vector3(4.69f, -.27f, -6.91f),
            Quaternion.Euler(0, -32f, 0), layer);
        var doorParts = new MeshParts();
        doorParts.Box(new Vector3(.55f, 0, 0), new Vector3(1.10f, 1.30f, .075f));
        doorParts.Box(new Vector3(.95f, 0, -.06f), new Vector3(.055f, .36f, .06f));
        ownedMeshes.Add(doorParts.Render(door, "Bent open panel and handle", steel, layer));
        var lamps = MakeMaterial(steel, new Color(.20f, .45f, .32f));
        lamps.EnableKeyword("_EMISSION");
        if (lamps.HasProperty("_EmissionColor")) lamps.SetColor("_EmissionColor", new Color(.025f, .09f, .04f));
        var indicators = new MeshParts();
        indicators.Cylinder(new Vector3(3.72f, .12f, -7.00f), .055f, .04f, 10);
        indicators.Cylinder(new Vector3(3.92f, .12f, -7.00f), .055f, .04f, 10);
        ownedMeshes.Add(indicators.Render(cabin.transform, "Service panel dim indicators", lamps, layer));

        // Two old conduits run above the damaged left return and turn down to
        // the repair cabinet. All parts are visual and stay outside the lane.
        void Conduit(string name, Vector3 position, Quaternion rotation, float length, float radius)
        {
            Transform node = Node(cabin.transform, name, position, rotation, layer);
            var tube = new MeshParts();
            tube.Cylinder(Vector3.zero, radius, length, 12);
            foreach (float sign in new[] { -1f, 1f })
                tube.Cylinder(new Vector3(0, 0, sign * (length * .5f - .09f)), radius * 1.38f, .10f, 12);
            ownedMeshes.Add(tube.Render(node, name + " pipe and collars", steel, layer));
        }
        Conduit("Upper service conduit", new Vector3(5.05f, 1.30f, -7.65f), Quaternion.identity, 4.45f, .105f);
        Conduit("Lower service conduit", new Vector3(5.05f, .96f, -7.65f), Quaternion.identity, 4.45f, .075f);
        Conduit("Cabinet conduit cross feed", new Vector3(4.72f, 1.30f, -6.72f), Quaternion.Euler(0, 90, 0), .66f, .105f);
        Conduit("Cabinet conduit down feed", new Vector3(4.39f, 1.06f, -6.72f), Quaternion.Euler(90, 0, 0), .50f, .105f);
        var pipeMounts = new MeshParts();
        foreach (float z in new[] { -9.40f, -8.10f, -6.72f, -5.65f })
        {
            pipeMounts.Box(new Vector3(5.16f, 1.13f, z), new Vector3(.12f, .62f, .11f));
            pipeMounts.Box(new Vector3(5.05f, 1.30f, z), new Vector3(.30f, .26f, .09f));
            pipeMounts.Box(new Vector3(5.05f, .96f, z), new Vector3(.24f, .20f, .09f));
        }
        pipeMounts.Box(new Vector3(5.05f, 1.30f, -6.72f), new Vector3(.26f, .26f, .26f));
        pipeMounts.Box(new Vector3(4.39f, 1.30f, -6.72f), new Vector3(.26f, .26f, .26f));
        // Armoured, low-output service lamp at the conduit junction.
        pipeMounts.Box(new Vector3(5.06f, .61f, -6.72f), new Vector3(.18f, .30f, .42f));
        ownedMeshes.Add(pipeMounts.Render(cabin.transform, "Conduit clamps elbows and lamp guard", dark, layer));
        var serviceLens = new MeshParts();
        serviceLens.Box(new Vector3(4.96f, .61f, -6.72f), new Vector3(.025f, .18f, .28f));
        ownedMeshes.Add(serviceLens.Render(cabin.transform, "Protected service light lens", lamps, layer));
        WorkLight("Conduit junction service light", new Vector3(4.82f, .64f, -6.72f), new Color(.82f, .93f, .77f), 2.7f, .65f);

        // A retired drive sits on the other shelter, visually distinct from the
        // cabinet while retaining a clear path to its protected side.
        var drive = new MeshParts();
        drive.Box(new Vector3(-4.05f, -.03f, -5.48f), new Vector3(1.10f, .33f, .18f));
        drive.Cylinder(new Vector3(-4.05f, .10f, -5.59f), .32f, .12f, 16);
        drive.Cylinder(new Vector3(-4.05f, .10f, -5.67f), .13f, .06f, 12);
        ownedMeshes.Add(drive.Render(cabin.transform, "Retired drive casing and shaft", steel, layer));
        WorkLight("Left facade worklight", new Vector3(3.4f, 1.1f, -6.1f), new Color(1f, .86f, .69f), 9f, 1.05f);
        WorkLight("Right facade worklight", new Vector3(-3.4f, 1.1f, -6.1f), new Color(.75f, .84f, 1f), 9f, 1.05f);
        WorkLight("Service switchboard light", new Vector3(4.0f, .65f, -7.55f), new Color(1f, .83f, .60f), 3.4f, 1.1f);
        WorkLight("Retired drive light", new Vector3(-4.0f, .65f, -6.25f), new Color(.70f, .82f, 1f), 3.3f, .9f);
        WorkLight("Warm ring worklight", new Vector3(-3.4f, 1.6f, -4.8f), new Color(1f, .72f, .44f), 10f, 1.45f);
        WorkLight("Cool void fill", new Vector3(3.8f, 2f, 1.5f), new Color(.36f, .55f, .78f), 13f, 1.7f);
    }

    private void OnDestroy()
    {
        foreach (Mesh mesh in ownedMeshes) if (mesh) Destroy(mesh);
        foreach (Material material in ownedMaterials) if (material) Destroy(material);
    }
    private void LateUpdate()
    {
        if (!Transfer || !Transfer.parent || !Transfer.parent.parent) return;
        Transform rotor = Transfer.parent.parent;
        float carrier = rotor.localEulerAngles.z;
        for (int i = 0; i < pinions.Count; i++)
            if (pinions[i]) pinions[i].localRotation = Quaternion.Euler(0, 0, pinionPhases[i] - 8f * carrier);
    }

    private static Transform Node(Transform parent, string name, Vector3 position, Quaternion rotation, int layer)
    {
        var go = new GameObject(name);
        go.layer = layer;
        go.transform.SetParent(parent, false);
        go.transform.SetLocalPositionAndRotation(position, rotation);
        return go.transform;
    }

    // Flat-shaded extruded geometry, batched by material and moving part.
    private sealed class MeshParts
    {
        private readonly List<Vector3> vertices = new();
        private readonly List<int> triangles = new();
        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int n = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2);
            triangles.Add(n); triangles.Add(n + 2); triangles.Add(n + 3);
        }
        internal void Box(Vector3 c, Vector3 s)
        {
            Vector3 h = s * .5f;
            Vector3 a = c + new Vector3(-h.x,-h.y,-h.z), b = c + new Vector3(h.x,-h.y,-h.z);
            Vector3 d = c + new Vector3(-h.x,h.y,-h.z), e = c + new Vector3(h.x,h.y,-h.z);
            Vector3 f = c + new Vector3(-h.x,-h.y,h.z), g = c + new Vector3(h.x,-h.y,h.z);
            Vector3 i = c + new Vector3(-h.x,h.y,h.z), j = c + new Vector3(h.x,h.y,h.z);
            Quad(a,d,e,b); Quad(f,g,j,i); Quad(a,b,g,f); Quad(d,i,j,e); Quad(a,f,i,d); Quad(b,e,j,g);
        }
        internal void WallTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            Quad(a, b, c, c); Quad(c, b, a, a);
        }
        internal void RingShoulders(float outer, float radius, float bottom, float top, float z)
        {
            const int steps = 40;
            for (int i = 0; i < steps; i++)
            {
                float y0 = Mathf.Lerp(bottom, top, (float)i / steps);
                float y1 = Mathf.Lerp(bottom, top, (float)(i + 1) / steps);
                float nearest = y0 <= 0 && y1 >= 0 ? 0 : Mathf.Min(Mathf.Abs(y0), Mathf.Abs(y1));
                float inner = Mathf.Sqrt(Mathf.Max(0, radius * radius - nearest * nearest)) + .015f;
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 a = new Vector3(side * inner, y0, z), b = new Vector3(side * outer, y0, z);
                    Vector3 c = new Vector3(side * outer, y1, z), d = new Vector3(side * inner, y1, z);
                    Quad(a, b, c, d); Quad(d, c, b, a);
                }
            }
        }
        internal void Fracture(float x0, float x1, float z0, float z1, float y, float rise0, float rise1)
        {
            Vector3 a = new Vector3(x0, y, z0), b = new Vector3(x1, y, z0);
            Vector3 c = new Vector3(x1, y, z1), d = new Vector3(x0, y, z1);
            Vector3 e = new Vector3(x0, y + rise0, z0), f = new Vector3(x1, y + rise1, z0);
            Vector3 g = new Vector3(x1, y + rise1 * .4f, z1), h = new Vector3(x0, y + rise0 * .5f, z1);
            Quad(e, h, g, f); Quad(a, e, f, b); Quad(b, f, g, c);
            Quad(c, g, h, d); Quad(d, h, e, a);
        }
        internal void Cylinder(Vector3 c, float radius, float depth, int segments) => Annulus(c, 0, radius, depth, segments);
        internal void Annulus(Vector3 c, float inner, float outer, float depth, int segments)
        {
            for (int k = 0; k < segments; k++)
            {
                float a = 2 * Mathf.PI * k / segments, b = 2 * Mathf.PI * (k + 1) / segments;
                Segment(c, a, b, inner, inner, outer, outer, depth);
            }
        }
        internal void Gear(Vector3 c, int teeth, float pitch, float tooth, float otherRadius, float depth, bool internalTeeth, float phase)
        {
            int count = teeth * 4;
            for (int k = 0; k < count; k++)
            {
                float a = 2 * Mathf.PI * k / count + phase, b = 2 * Mathf.PI * (k + 1) / count + phase;
                float r1 = pitch + ((k % 4 == 1 || k % 4 == 2) ? tooth : -tooth);
                int next = (k + 1) % 4;
                float r2 = pitch + ((next == 1 || next == 2) ? tooth : -tooth);
                if (internalTeeth) Segment(c, a, b, r1, r2, otherRadius, otherRadius, depth);
                else Segment(c, a, b, otherRadius, otherRadius, r1, r2, depth);
            }
        }
        private void Segment(Vector3 c, float a, float b, float ia, float ib, float oa, float ob, float depth)
        {
            Vector3 P(float angle, float radius, float z) => c + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, z);
            float z0 = -depth * .5f, z1 = depth * .5f;
            Vector3 a0=P(a,ia,z0), b0=P(b,ib,z0), c0=P(b,ob,z0), d0=P(a,oa,z0);
            Vector3 a1=P(a,ia,z1), b1=P(b,ib,z1), c1=P(b,ob,z1), d1=P(a,oa,z1);
            Quad(a0,b0,c0,d0); Quad(a1,d1,c1,b1); Quad(d0,c0,c1,d1); Quad(b0,a0,a1,b1);
        }
        internal Mesh Render(Transform parent, string name, Material material, int layer)
        {
            var go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return mesh;
        }
    }
}

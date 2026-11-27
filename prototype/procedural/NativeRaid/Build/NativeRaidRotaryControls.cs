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

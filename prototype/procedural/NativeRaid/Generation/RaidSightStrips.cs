using System.Collections.Generic;
using Contour4.Procedural.Catalog;
using UnityEngine;
using UnityEngine.Rendering;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class RaidSightStrips
    {
        private const float RoofHalfWidth = 1f;
        private const float FloorHalfWidth = .50f;
        private const float FloorHalfLength = 1.20f;
        private const float Edge = .50f;
        private readonly Material roofGlass, floorGlass;
        private readonly ICollection<Mesh> ownedMeshes;
        private readonly Dictionary<Rect, Mesh> slabMeshes = new();

        internal RaidSightStrips(Shader standard, ICollection<Material> ownedMaterials, ICollection<Mesh> ownedMeshes)
        {
            this.ownedMeshes = ownedMeshes;
            roofGlass = Glazing(standard, "Contour-4 roof glazing", new Color(.29f, .46f, .54f, .15f));
            floorGlass = Glazing(standard, "Contour-4 floor glazing", new Color(.25f, .42f, .47f, .66f));
            ownedMaterials.Add(roofGlass);
            ownedMaterials.Add(floorGlass);
        }

        internal void Apply(GrayModule room, Block block, Bounds bounds, GameObject slab)
        {
            bool roof = IsRoof(block);
            if (!roof && !HasFloorWindow(room, block)) return;

            // Roof sections around a hatch share one center line. A closed hatch plug stays opaque.
            float centerZ = roof ? RoofCenter(room, bounds.min.y) : bounds.center.z;
            float halfWidth = roof ? RoofHalfWidth : FloorHalfWidth;
            float near = Mathf.Max(bounds.min.z, centerZ - halfWidth);
            float far = Mathf.Min(bounds.max.z, centerZ + halfWidth);
            if (far <= near) return;

            // Keep the floor opening in the middle of the slab, bounded by solid panels at both ends.
            float halfLength = roof ? Edge : FloorHalfLength / bounds.size.x;
            if (!roof && (halfLength >= Edge || far - near < 2 * FloorHalfWidth)) return;

            var window = Rect.MinMaxRect(-halfLength, (near - bounds.center.z) / bounds.size.z,
                halfLength, (far - bounds.center.z) / bounds.size.z);
            if (!slabMeshes.TryGetValue(window, out Mesh mesh))
            {
                mesh = GlazedSlab(window);
                ownedMeshes.Add(mesh);
                slabMeshes.Add(window, mesh);
            }

            // Only the cube's render mesh changes. The original BoxCollider remains the full slab.
            MeshRenderer renderer = slab.GetComponent<MeshRenderer>();
            slab.GetComponent<MeshFilter>().sharedMesh = mesh;
            renderer.sharedMaterials = new[] { renderer.sharedMaterial, roof ? roofGlass : floorGlass };
        }

        private static bool IsRoof(Block block) => block.Role == BlockRole.Shell &&
            block.Id is "roof" or "roof-west" or "roof-east" or "roof-near" or "roof-far" or "loading-bay-roof";

        private static bool HasFloorWindow(GrayModule room, Block block) => block.Role == BlockRole.Floor &&
            (block.Id == "ground" && room.Id == FreightServiceRoom.InstrumentService ||
                block.Id == "gallery" && room.Id is "observation-lab" or "observation-lab-broken-floor");

        private static float RoofCenter(GrayModule room, float bottom)
        {
            float near = float.PositiveInfinity;
            float far = float.NegativeInfinity;
            foreach (Block block in room.Geometry)
            {
                if (!IsRoof(block)) continue;
                var (minimum, maximum) = block.Bounds;
                if (Mathf.Abs(minimum.Y - bottom) > .001f) continue;
                near = Mathf.Min(near, minimum.Z);
                far = Mathf.Max(far, maximum.Z);
            }
            return (near + far) / 2;
        }

        private static Material Glazing(Shader standard, string name, Color tint)
        {
            // Configure Standard's Fade variant on a raid-owned material.
            var material = new Material(standard) { name = name, color = tint, renderQueue = (int)RenderQueue.Transparent };
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_Mode", 2f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.SetFloat("_Glossiness", .35f);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(.07f, .13f, .16f));
            return material;
        }

        private static Mesh GlazedSlab(Rect window)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var opaque = new List<int>();
            var glass = new List<int>();

            Panel(opaque, -Edge, window.xMin, -Edge, Edge);
            Panel(opaque, window.xMax, Edge, -Edge, Edge);
            Panel(opaque, window.xMin, window.xMax, -Edge, window.yMin);
            Panel(opaque, window.xMin, window.xMax, window.yMax, Edge);
            Panel(glass, window.xMin, window.xMax, window.yMin, window.yMax);

            var mesh = new Mesh { name = "Contour-4 glazed slab", subMeshCount = 2 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(opaque, 0, false);
            mesh.SetTriangles(glass, 1, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
            return mesh;

            void Panel(List<int> faces, float left, float right, float near, float far)
            {
                if (right <= left || far <= near) return;
                Vector3 a = new(left, -Edge, near), b = new(right, -Edge, near);
                Vector3 c = new(right, -Edge, far), d = new(left, -Edge, far);
                Vector3 e = a + Vector3.up, f = b + Vector3.up, g = c + Vector3.up, h = d + Vector3.up;
                Face(faces, e, h, g, f, Vector3.up);
                Face(faces, a, b, c, d, Vector3.down);
                // Join adjacent panels without overlapping faces along the glass edges.
                if (left == -Edge) Face(faces, a, d, h, e, Vector3.left);
                if (right == Edge) Face(faces, b, f, g, c, Vector3.right);
                if (near == -Edge) Face(faces, a, e, f, b, Vector3.back);
                if (far == Edge) Face(faces, d, c, g, h, Vector3.forward);
            }

            void Face(List<int> faces, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                int first = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                vertices.Add(d);
                for (int i = 0; i < 4; i++) normals.Add(normal);
                faces.Add(first);
                faces.Add(first + 1);
                faces.Add(first + 2);
                faces.Add(first);
                faces.Add(first + 2);
                faces.Add(first + 3);
            }
        }
    }
}

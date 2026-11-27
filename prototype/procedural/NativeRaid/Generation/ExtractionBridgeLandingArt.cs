using Contour4.Procedural.Catalog;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Contour4.NativeRaid.Runtime
{
    // Non-colliding dressing: the catalog decks and timber retain all physical support.
    internal sealed class ExtractionBridgeLandingArt : MonoBehaviour
    {
        private const string Name = "Ruined bridge landing art";
        private Material concrete, seam, paint, steel;

        internal static void Apply(Transform root, string moduleId)
        {
            bool bent = moduleId == RaidExtraction.BentBridgeId;
            if (!bent && moduleId != RaidExtraction.StraightBridgeId) return;
            if (root.Find(Name)) return;
            var holder = new GameObject(Name);
            holder.transform.SetParent(root, false);
            var art = holder.AddComponent<ExtractionBridgeLandingArt>();
            art.Build(bent);
        }

        private void Build(bool bent)
        {
            var shader = Shader.Find("Standard");
            concrete = Material(shader, "landing concrete", new Color(.31f, .30f, .28f));
            seam = Material(shader, "landing panel seams", new Color(.16f, .16f, .15f));
            paint = Material(shader, "worn safety ochre", new Color(.55f, .38f, .12f));
            steel = Material(shader, "bridge edge steel", new Color(.18f, .17f, .16f));
            Panels(0.15f, 4.72f, -5.8f, bent ? 4.6f : 5.8f);
            if (bent) Panels(6.25f, 16.85f, 6.25f, 15.9f);
            else Panels(12.4f, 22.35f, -5.8f, 5.8f);
            // Short, interrupted diagonal paint marks sit wholly on the entry apron.
            for (int i = -4; i <= 4; i++)
            {
                if (i == 0) continue;
                float z = i * .48f;
                Box("abraded threshold", new Vector3(4.42f, .011f, z), new Vector3(.14f, .009f, .29f), paint, 35f);
                if ((i & 1) == 0)
                    Box("paint wear", new Vector3(4.42f, .017f, z), new Vector3(.04f, .003f, .12f), concrete, 35f);
            }
            Span(4.8f, bent ? 11.2f : 12.3f, 0f, false);
            if (bent) Span(.4f, 6.15f, 10.8f, true);
        }

        private void Panels(float x0, float x1, float z0, float z1)
        {
            // Thin, slightly varied panels read as poured modular concrete without changing collision.
            const float step = 1.35f;
            for (float x = x0; x < x1; x += step)
                for (float z = z0; z < z1; z += step)
                {
                    float w = Mathf.Min(step, x1 - x), d = Mathf.Min(step, z1 - z);
                    Box("concrete panel", new Vector3(x + w / 2, .003f, z + d / 2),
                        new Vector3(w - .025f, .006f, d - .025f), concrete);
                }
            // Fine darker joints remain flat on the existing floor.
            for (float x = x0 + step; x < x1; x += step)
                Box("panel joint", new Vector3(x, .007f, (z0 + z1) / 2), new Vector3(.012f, .002f, z1-z0), seam);
        }

        private void Span(float start, float end, float axis, bool alongZ)
        {
            float length = end - start;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 p = alongZ ? new Vector3(axis + side * .365f, -.19f, (start + end) / 2)
                    : new Vector3((start + end) / 2, -.19f, axis + side * .365f);
                Vector3 size = alongZ ? new Vector3(.07f, .10f, length) : new Vector3(length, .10f, .07f);
                Box("under plank edge steel", p, size, steel);
            }
        }

        private void Box(string label, Vector3 position, Vector3 size, Material material, float yaw = 0)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = label;
            part.transform.SetParent(transform, false);
            part.transform.localPosition = position;
            part.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            part.transform.localScale = size;
            part.GetComponent<Renderer>().sharedMaterial = material;
            var collider = part.GetComponent<Collider>();
            collider.enabled = false;
            if (Application.isPlaying) Object.Destroy(collider);
            else Object.DestroyImmediate(collider);
        }

        private static Material Material(Shader shader, string name, Color color) =>
            new Material(shader) { name = "Contour-4 " + name, color = color };

        private void OnDestroy()
        {
            foreach (var material in new[] { concrete, seam, paint, steel })
                if (material) { if (Application.isPlaying) Object.Destroy(material); else Object.DestroyImmediate(material); }
        }
    }
}

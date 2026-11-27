using System.Collections.Generic;
using Contour4.Procedural.Catalog;
using UnityEngine;
using UnityEngine.Rendering;
using MeshBatch = Contour4.NativeRaid.Runtime.ExtractionBridgeVista.MeshBatch;

namespace Contour4.NativeRaid.Runtime
{
    // Deck overlays and trim are visual batches. Physical support remains in the catalog.
    internal static class ExtractionBridgeLandingArt
    {
        private const string Name = "Ruined bridge landing art";

        internal static void Apply(Transform room, bool bent, ExtractionBridgeVista finish,
            List<Material> materials, List<Mesh> meshes)
        {
            if (room.Find(Name)) return;
            var root = RaidAssembly.Child(room, Name);
            var concrete = new MeshBatch();
            var worn = new MeshBatch();
            var seams = new MeshBatch();
            var marks = new MeshBatch();
            var steel = new MeshBatch(ExtractionBridgeVista.SteelTileMeters);

            Panels(.15f, 4.72f, -5.8f, bent ? 4.6f : 5.8f);
            if (bent) Panels(6.25f, 16.85f, 6.25f, 15.9f);
            else Panels(12.4f, 22.35f, -5.8f, 5.8f);

            Quaternion stripe = Quaternion.Euler(0, 35f, 0);
            for (int i = -4; i <= 4; i++)
            {
                if (i == 0) continue;
                float z = i * .48f;
                marks.Box(new Vector3(4.42f, .011f, z), new Vector3(.14f, .009f, .29f), stripe);
                if ((i & 1) == 0)
                    worn.Box(new Vector3(4.42f, .017f, z), new Vector3(.04f, .003f, .12f), stripe);
            }
            Span(RaidExtractionBridge.GapStart, bent ? 11.2f : RaidExtractionBridge.StraightGapEnd, 0f, false);
            if (bent) Span(.4f, RaidExtractionBridge.BentLanding, RaidExtractionBridge.BentAxis, true);

            Publish("concrete panels", concrete, finish.DeckConcrete);
            Publish("worn concrete panels", worn, finish.WornDeckConcrete);
            Publish("panel joints", seams, Accent("landing panel seams", new Color(.135f, .131f, .123f)));
            Publish("abraded threshold", marks, Accent("worn safety ochre", new Color(.57f, .39f, .12f)));
            Publish("under plank edge steel", steel, finish.EdgeSteel);

            void Panels(float x0, float x1, float z0, float z1)
            {
                const float step = 1.35f;
                for (int row = 0; x0 + row * step < x1; row++)
                    for (int column = 0; z0 + column * step < z1; column++)
                    {
                        float x = x0 + row * step, z = z0 + column * step;
                        float w = Mathf.Min(step, x1 - x), d = Mathf.Min(step, z1 - z);
                        bool weathered = (row * 3 + column * 7) % 5 < 2;
                        MeshBatch tile = weathered ? worn : concrete;
                        tile.Box(new Vector3(x + w / 2f, .003f, z + d / 2f),
                            new Vector3(w - .025f, .006f, d - .025f), quarterTurns: (row + 3 * column) % 4);
                    }
                for (float x = x0 + step; x < x1; x += step)
                    seams.Box(new Vector3(x, .007f, (z0 + z1) / 2f), new Vector3(.012f, .002f, z1 - z0));
            }

            void Span(float start, float end, float axis, bool alongZ)
            {
                float length = end - start;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 point = alongZ ? new Vector3(axis + side * .365f, -.19f, (start + end) / 2f) :
                        new Vector3((start + end) / 2f, -.19f, axis + side * .365f);
                    Vector3 size = alongZ ? new Vector3(.07f, .10f, length) : new Vector3(length, .10f, .07f);
                    steel.Box(point, size);
                }
            }

            Material Accent(string label, Color color)
            {
                var material = new Material(finish.DeckConcrete.shader) { name = "Contour-4 " + label, color = color };
                material.SetFloat("_Glossiness", .10f);
                materials.Add(material);
                return material;
            }

            void Publish(string label, MeshBatch batch, Material material)
            {
                var child = RaidAssembly.Child(root, label);
                var mesh = batch.Finish(label);
                meshes.Add(mesh);
                child.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = child.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
    }
}

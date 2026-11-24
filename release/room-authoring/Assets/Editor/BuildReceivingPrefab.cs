using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Contour4.NativeRaid.Editor
{
    public static class BuildReceivingPrefab
    {
        private const string RoomFolder = "Assets/Contour4/Rooms";
        private const string Materials = RoomFolder + "/Materials";

        [MenuItem("Contour-4/Rebuild sample receiving prefabs")]
        public static void Rebuild()
        {
            Folder("Assets", "Contour4");
            Folder("Assets/Contour4", "Rooms");
            Folder(RoomFolder, "Materials");
            var palette = new Palette();
            Save(FreightServiceRoom.Create(FreightServiceRoom.SampleReceiving, portal: true),
                "sample-receiving", palette);
            Save(FreightServiceRoom.Create(FreightServiceRoom.SampleReceiving, portal: false),
                "sample-receiving-without-research", palette);
            AssetDatabase.SaveAssets();
            Debug.Log("CONTOUR4 RECEIVING PREFABS REBUILT");
        }

        private static void Save(GrayModule module, string name, Palette palette)
        {
            var root = new GameObject(name);
            try
            {
                var surfaces = module.Dressing.SelectMany(dressing =>
                    dressing.Parts.Select(part => (Id: dressing.Name(part.Id), part.Surface)))
                    .ToDictionary(part => part.Id, part => part.Surface);
                var detail = Child(root.transform, "Details");
                var fixtures = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (var block in module.Geometry.Where(block => block.Role != BlockRole.Free))
                {
                    var (min, max) = block.Bounds;
                    var size = V(max - min);
                    var position = V((min + max) / 2);
                    var box = Part(root.transform, block.Id, position, size,
                        palette.For(block, surfaces), solid: true);
                    fixtures.Add(block.Id, box);
                    if (block.Role is BlockRole.Shell or BlockRole.Plug && block.Id != "roof")
                        Cladding(box, position, size, palette);
                }
                FloorMarkings(detail, palette);
                DoorMarkings(fixtures, palette);
                EquipmentDetail(detail, palette);
                FixturesLights(root.transform, module);
                if (!PrefabUtility.SaveAsPrefabAsset(root, RoomFolder + "/" + name + ".prefab"))
                    throw new InvalidOperationException("Unity could not save the receiving prefab " + name);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void Cladding(Transform wall, Vector3 pos, Vector3 size, Palette palette)
        {
            bool alongX = size.x >= size.z;
            float inward = alongX ? -Mathf.Sign(pos.z) : pos.x < 4.625f ? 1 : -1;
            Vector3 face = alongX ? Vector3.forward : Vector3.right;
            float thickness = alongX ? size.z : size.x;
            Vector3 cross = alongX ? Vector3.right : Vector3.forward;
            float span = alongX ? size.x : size.z;
            for (int i = 0; i < 3; i++)
            {
                float y = .16f + i * .25f;
                if (pos.y - size.y / 2 > y || pos.y + size.y / 2 < y + .06f) continue;
                Vector3 surface = new Vector3(pos.x, y, pos.z) + face * (inward * (thickness / 2 + .006f));
                Vector3 trim = cross * (span - .012f) + face * .010f + Vector3.up * (i == 0 ? .21f : .028f);
                SurfaceDetail(wall, "Lower bulkhead rail", surface, trim, i == 0 ? palette.Base : palette.Edge);
            }
            int panels = Mathf.CeilToInt(span / 1.44f);
            float yBottom = Mathf.Max(1.01f, pos.y - size.y / 2 + .12f);
            float yTop = Mathf.Min(3.65f, pos.y + size.y / 2 - .12f);
            if (panels < 1 || yTop - yBottom < .25f) return;
            for (int i = 0; i < panels; i++)
            {
                float width = span / panels;
                Vector3 surface = new Vector3(pos.x, (yBottom + yTop) / 2, pos.z) +
                    face * (inward * (thickness / 2 + .004f)) + cross * (width * (i + .5f) - span / 2);
                SurfaceDetail(wall, "Inset cladding", surface,
                    cross * (width - .055f) + face * .006f + Vector3.up * (yTop - yBottom), palette.Cladding);
            }
        }

        private static void FloorMarkings(Transform detail, Palette palette)
        {
            foreach (int side in new[] { -1, 1 })
            {
                for (int i = 0; i < 10; i++)
                    Part(detail, "Freight lane dash", new Vector3(.49f + .92f * i, .0018f, side * 1.075f),
                        new Vector3(.48f, .002f, .035f), palette.Warm, solid: false);
                for (int i = 0; i < 3; i++)
                    Part(detail, "North turn dash", new Vector3(4.625f + side * 1.075f, .0018f, 1.52f + .9f * i),
                        new Vector3(.035f, .002f, .48f), palette.Warm, solid: false);
            }
            foreach (float x in new[] { 2.72f, 4.26f })
                Part(detail, "Assay alcove marker", new Vector3(x, .0018f, SampleReceivingDressing.AssayZ - 1.23f),
                    new Vector3(.025f, .002f, .56f), palette.Edge, solid: false);
            Part(detail, "Bench standing mat", new Vector3(1.47f, .0016f, -2.82f), new Vector3(1.72f, .002f, .42f),
                palette.Base, solid: false);
        }

        private static void DoorMarkings(IReadOnlyDictionary<string, Transform> fixtures, Palette palette)
        {
            foreach (string side in new[] { "left", "right" })
            {
                Transform door = fixtures["receiving-cold-locker-" + side + "-door"];
                Vector3 face = door.localPosition + Vector3.forward * (door.localScale.z / 2 + .005f);
                SurfaceDetail(door, "Cold chamber lower rib", face + Vector3.down * .46f,
                    new Vector3(door.localScale.x - .09f, .033f, .008f), palette.Edge);
                SurfaceDetail(door, "Cold chamber serial marker", face + Vector3.up * .56f,
                    new Vector3(.26f, .12f, .008f), palette.Steel);
                for (int i = 0; i < 5; i++)
                    SurfaceDetail(door, "Cold chamber intake slot",
                        face + new Vector3(i * .064f - .128f, -.64f, .001f),
                        new Vector3(.038f, .13f, .014f), palette.Base);
            }
            for (int i = 0; i < 3; i++)
            {
                Transform door = fixtures["receiving-manifest-drawers-drawer-" + i];
                SurfaceDetail(door, "Drawer identification tab", door.localPosition +
                    new Vector3(.31f, .04f, door.localScale.z / 2 + .006f),
                    new Vector3(.16f, .04f, .010f), palette.Steel);
            }
        }

        private static void EquipmentDetail(Transform detail, Palette palette)
        {
            for (int i = 0; i < 8; i++)
                Part(detail, "Analyzer heat sink", new Vector3(3.18f + i * .084f, 1.03f, -1.155f),
                    new Vector3(.039f, .31f, .017f), palette.Base, solid: false);
            for (int i = 0; i < 5; i++)
                Part(detail, "Sorting destination tab", new Vector3(7.23f + i * .32f, 2.13f, 3.221f),
                    new Vector3(.18f, .056f, .013f), i % 2 == 0 ? palette.Warm : palette.Steel, solid: false);

            foreach (float x in new[] { 1.45f, 5.38f, 7.88f })
            {
                float z = x < 7 ? -4.116f : 4.116f;
                float inward = -Mathf.Sign(z);
                Part(detail, "Work zone plate", new Vector3(x, 2.87f, z), new Vector3(.96f, .28f, .015f),
                    palette.Base, solid: false);
                for (int i = 0; i < 4; i++)
                    Part(detail, "Work zone plate stripe",
                        new Vector3(x - .35f + i * .19f, 2.87f, z + inward * .01f),
                        new Vector3(.105f, .038f, .006f), palette.Warm, solid: false);
            }
        }

        private static void FixturesLights(Transform root, GrayModule module)
        {
            Transform rig = Child(root, "Lighting");
            var fill = Child(rig, "Freight overhead fill");
            fill.localPosition = new Vector3(4.625f, 3.62f, .19f);
            var soft = fill.gameObject.AddComponent<Light>();
            soft.type = LightType.Point;
            soft.color = new Color(.86f, .92f, 1f);
            soft.range = 8.8f;
            soft.intensity = 1.45f;
            foreach (var dressing in module.Dressing)
            foreach (var lamp in dressing.Lamps)
            {
                if (lamp.Duty == LampDuty.Offline) continue;
                var node = Child(rig, dressing.Name(lamp.Id));
                node.localPosition = V(dressing.Point(lamp.Emitter)) + V(dressing.Direction(lamp.Direction)) * .09f;
                var light = node.gameObject.AddComponent<Light>();
                light.type = LightType.Spot;
                node.localRotation = Quaternion.LookRotation(V(dressing.Direction(lamp.Direction)));
                light.spotAngle = lamp.Optics == LampOptics.Task ? 84f : 104f;
                light.range = lamp.Duty == LampDuty.Guidance ? 3.4f : 5.2f;
                (light.intensity, light.color) = lamp.Duty switch
                {
                    LampDuty.Work => (2.65f, new Color(1f, .87f, .68f)),
                    LampDuty.Guidance => (1.26f, new Color(.70f, .88f, .99f)),
                    _ => (1.40f, new Color(.68f, .87f, .96f))
                };
                light.shadows = lamp.Duty == LampDuty.Work ? LightShadows.Soft : LightShadows.None;
            }
        }

        private static void SurfaceDetail(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            // Decoration is modeled in room coordinates. Reparenting keeps the mark attached when a leaf moves.
            Transform detail = Part(parent.parent, name, pos, size, mat, solid: false);
            detail.SetParent(parent, true);
        }

        private static Transform Part(Transform parent, string id, Vector3 center, Vector3 size, Material mat, bool solid)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = id;
            if (!solid) Object.DestroyImmediate(part.GetComponent<BoxCollider>());
            part.GetComponent<Renderer>().sharedMaterial = mat;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = center;
            part.transform.localScale = size;
            return part.transform;
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static void Folder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        private static Vector3 V(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);

        private sealed class Palette
        {
            internal readonly Material Base = Mat("Graphite skirting", 49, 61, 70, .18f);
            internal readonly Material Cladding = Textured("Blue enamel cladding", "painted_plaster_wall",
                new(.77f, .84f, .85f), new(2, 1));
            internal readonly Material Edge = Mat("Blued steel frame", 37, 64, 75, .26f);
            internal readonly Material Steel = Mat("Brushed steel fittings", 156, 173, 174, .40f);
            internal readonly Material Warm = Mat("Amber freight paint", 192, 132, 48, .12f);
            private readonly Material deck = Textured("Painted deck", "concrete_floor_worn_001",
                new(.68f, .70f, .64f), new(4, 4));
            private readonly Material shell = Textured("Shell enamel", "painted_plaster_wall",
                new(.82f, .87f, .88f), new(2, 1));
            private readonly Material ceiling = Mat("Ceiling liner", 87, 106, 111, .12f);
            private readonly Material cabinet = Textured("Cold cabinet enamel", "green_metal_rust",
                new(.65f, .80f, .65f), Vector2.one);
            private readonly Material equipment = Textured("Instrument enamel", "green_metal_rust",
                new(.65f, .80f, .65f), Vector2.one);
            private readonly Material laminate = Mat("Dark lab worktop", 44, 49, 58, .27f);
            private readonly Material crate = Mat("Reusable brown composite", 123, 93, 58, .13f);
            private readonly Material indicator = Mat("Cold status lens", 48, 146, 157, .3f, emission: .65f);
            private readonly Material fixture = Mat("Warm fixture diffuser", 195, 166, 110, .20f, emission: .72f);

            internal Material For(Block block, IReadOnlyDictionary<string, DressingSurface> surfaces)
            {
                if (block.Role == BlockRole.Floor) return deck;
                if (block.Role == BlockRole.Plug) return Edge;
                if (block.Role == BlockRole.Shell) return block.Id == "roof" ? ceiling : shell;
                DressingSurface surface = surfaces[block.Id];
                if (block.Id.Contains("-crate", StringComparison.Ordinal)) return crate;
                if (block.Id.Contains("-cold-locker-", StringComparison.Ordinal) ||
                    block.Id.Contains("-manifest-drawers-", StringComparison.Ordinal))
                {
                    if (surface == DressingSurface.Housing) return cabinet;
                    if (block.Id.EndsWith("-door", StringComparison.Ordinal) ||
                        block.Id.Contains("-drawer-", StringComparison.Ordinal)) return cabinet;
                    if (block.Id.EndsWith("-handle", StringComparison.Ordinal) ||
                        block.Id.Contains("-pull-", StringComparison.Ordinal)) return Warm;
                }
                return surface switch
                {
                    DressingSurface.Worktop => laminate,
                    DressingSurface.Conduit or DressingSurface.Seat or DressingSurface.DarkLens => Base,
                    DressingSurface.ActiveLens or DressingSurface.GuideLens => indicator,
                    DressingSurface.StandbyLens => fixture,
                    DressingSurface.SteelTrim => Steel,
                    _ => equipment
                };
            }

            private static Material Textured(string name, string textureName, Color tint, Vector2 tiling)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Materials}/SurfaceTextures/{textureName}_diff_1k.jpg");
                if (!texture)
                    throw new InvalidOperationException($"Missing diffuse texture for {name}: {textureName}.");
                var material = Mat(name, 255, 255, 255, .12f);
                material.mainTexture = texture;
                material.mainTextureScale = tiling;
                material.color = tint;
                return material;
            }

            private static Material Mat(string name, byte r, byte g, byte b, float gloss, float emission = 0f)
            {
                string path = Materials + "/" + name.Replace(' ', '-').ToLowerInvariant() + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material)
                {
                    material = new Material(Shader.Find("Standard")) { name = name };
                    AssetDatabase.CreateAsset(material, path);
                }
                material.color = new Color32(r, g, b, 255);
                material.SetFloat("_Glossiness", gloss);
                if (emission > 0)
                {
                    // The tested room uses the fixture lights without the Standard shader's emissive variant.
                    material.DisableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", material.color * emission);
                }
                EditorUtility.SetDirty(material);
                return material;
            }
        }
    }
}

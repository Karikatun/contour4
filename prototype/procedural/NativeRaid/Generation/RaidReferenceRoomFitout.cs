using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using UnityEngine;
using UnityEngine.Rendering;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed partial class RaidReferenceRoomFitout
    {
        private enum Finish
        {
            Steel, Edge, Graphite, Enamel, Teal, Wood, WoodEdge, Paper, Amber, Red, Brass,
            GreenPhosphor, AmberLens, WhiteTube, Glass, Frost, Circuit, CableBlue, CableGreen, Copper
        }

        private readonly Shader shader;
        private readonly ICollection<Material> materials;
        private readonly ICollection<Mesh> meshes;
        private readonly Func<PrimitiveType, Mesh> primitive;
        private readonly BrokenEquipment brokenEquipment;
        private readonly Dictionary<Finish, Material> palette = new();

        internal RaidReferenceRoomFitout(Shader shader, ICollection<Material> materials, ICollection<Mesh> meshes,
            Func<PrimitiveType, Mesh> primitive, BrokenEquipment brokenEquipment)
        {
            this.shader = shader;
            this.materials = materials;
            this.meshes = meshes;
            this.primitive = primitive;
            this.brokenEquipment = brokenEquipment;
        }

        internal Collider Install(string module, Transform room, int layer, List<Collider> colliders)
        {
            switch (module)
            {
                case "freight-dispatch": Dispatch(New("Timber dispatch desk and pendant"), colliders); break;
                case "sample-receiving": Receiving(New("Glazed sample analyzer"), room, colliders); break;
                case "sample-storage": Storage(New("Deep cassette archive"), false, colliders); break;
                case "sample-storage-narrow": Storage(New("Deep cassette archive"), true, colliders); break;
                case "instrument-service": Service(New("Instrument repair bench"), room, colliders); break;
                case "calibration-sector": Calibration(New("Calibration exciter and analog instruments")); break;
                case "observation-lab": Observation(New("Glazed observation gallery"), false, "", colliders); break;
                case "observation-lab-broken-floor": Observation(New("Observation gallery and damaged test bay"), true, "", colliders); break;
                case "gallery-chamber":
                    Observation(New("Glazed observation gallery"), false, "lab-", colliders);
                    GalleryTransfer(New("Lens transfer worktop", new Vector3(7.5f, 7.35f, -7.75f), -90f), colliders);
                    break;
                case "shaft-plant": Shaft(New("Shaft pump bank and maintenance levels"), colliders); break;
                case "service-cupboard": Cupboard(New("Service cupboard fixtures"), colliders); break;
            }

            return InstallCableRoutes(module, room, layer);

            Batch New(string name, Vector3 origin = default, float tilt = 0)
            {
                var group = RaidAssembly.Child(room, name);
                group.localPosition = origin;
                group.localRotation = Quaternion.Euler(tilt, 0, 0);
                group.gameObject.layer = layer;
                return new Batch(this, group);
            }
        }

        private Material Get(Finish finish)
        {
            if (palette.TryGetValue(finish, out Material material)) return material;
            Color color = finish switch
            {
                Finish.Steel => new(.28f, .32f, .34f),
                Finish.Edge => new(.47f, .53f, .54f),
                Finish.Graphite => new(.095f, .12f, .13f),
                Finish.Enamel => new(.69f, .71f, .61f),
                Finish.Teal => new(.21f, .37f, .37f),
                Finish.Wood => new(.42f, .24f, .13f),
                Finish.WoodEdge => new(.27f, .15f, .085f),
                Finish.Paper => new(.83f, .81f, .64f),
                Finish.Amber => new(.79f, .53f, .15f),
                Finish.Red => new(.52f, .18f, .13f),
                Finish.Brass => new(.52f, .40f, .22f),
                Finish.GreenPhosphor => new(.26f, .53f, .31f),
                Finish.AmberLens => new(.71f, .39f, .11f),
                Finish.WhiteTube => new(.81f, .81f, .62f),
                Finish.Glass => new(.38f, .57f, .57f, .24f),
                Finish.Frost => new(.65f, .73f, .67f, .68f),
                Finish.Circuit => new(.16f, .29f, .21f),
                Finish.CableBlue => new(.13f, .29f, .48f),
                Finish.CableGreen => new(.19f, .40f, .20f),
                Finish.Copper => new(.63f, .34f, .16f),
                _ => throw new ArgumentOutOfRangeException(nameof(finish))
            };
            material = new Material(shader) { name = "Contour-4 interior " + finish, color = color };
            material.SetFloat("_Glossiness", .26f);
            if (finish is Finish.Steel or Finish.Edge or Finish.Brass)
                material.SetFloat("_Metallic", .37f);
            if (finish is Finish.GreenPhosphor or Finish.AmberLens or Finish.WhiteTube)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * .44f);
            }
            if (finish is Finish.Glass or Finish.Frost)
            {
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetFloat("_Mode", 2f);
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            palette.Add(finish, material);
            materials.Add(material);
            return material;
        }

        private void Refinish(IEnumerable<Collider> colliders, Finish? finish, params string[] names)
        {
            foreach (var collider in colliders)
            {
                if (Array.IndexOf(names, collider.name) < 0) continue;
                var renderer = collider.GetComponent<MeshRenderer>();
                if (!renderer) continue;
                if (finish.HasValue) renderer.sharedMaterial = Get(finish.Value);
                else renderer.enabled = false;
            }
        }

        private sealed class Batch
        {
            private readonly RaidReferenceRoomFitout owner;
            private readonly Transform parent;
            private readonly Dictionary<Finish, List<CombineInstance>> parts = new();

            internal Batch(RaidReferenceRoomFitout owner, Transform parent)
            {
                this.owner = owner;
                this.parent = parent;
            }

            internal void Box(Finish finish, float x, float y, float z, float width, float height, float depth,
                float tilt = 0, float roll = 0)
            {
                Box(finish, new Vector3(x, y, z), new Vector3(width, height, depth),
                    Quaternion.Euler(tilt, 0, roll));
            }

            internal void Box(Finish finish, Vector3 center, Vector3 size, Quaternion rotation) =>
                Add(finish, PrimitiveType.Cube, center, size, rotation);

            internal void Rod(Finish finish, float diameter, Vector3 first, Vector3 second)
            {
                var axis = second - first;
                Add(finish, PrimitiveType.Cylinder, (first + second) / 2, new Vector3(diameter, axis.magnitude / 2, diameter),
                    Quaternion.FromToRotation(Vector3.up, axis));
            }

            internal Batch Mount(string name, Vector3 position, Quaternion rotation)
            {
                var mount = RaidAssembly.Child(parent, name);
                mount.localPosition = position;
                mount.localRotation = rotation;
                mount.gameObject.layer = parent.gameObject.layer;
                return new Batch(owner, mount);
            }

            private void Add(Finish finish, PrimitiveType type, Vector3 center, Vector3 scale, Quaternion rotation)
                => Add(finish, owner.primitive(type), Matrix4x4.TRS(center, rotation, scale));

            internal void AddCable(Finish finish, Mesh mesh)
            {
                owner.meshes.Add(mesh);
                Add(finish, mesh, Matrix4x4.identity);
            }

            private void Add(Finish finish, Mesh mesh, Matrix4x4 transform)
            {
                if (!parts.TryGetValue(finish, out var instances))
                    parts[finish] = instances = new List<CombineInstance>();
                instances.Add(new CombineInstance { mesh = mesh, transform = transform });
            }

            internal void FinishGeometry()
            {
                foreach (var (finish, instances) in parts)
                {
                    var model = RaidAssembly.Child(parent, finish.ToString()).gameObject;
                    model.layer = parent.gameObject.layer;
                    var mesh = new Mesh { name = parent.name + " " + finish };
                    owner.meshes.Add(mesh);
                    mesh.CombineMeshes(instances.ToArray(), true, true);
                    model.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = model.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = owner.Get(finish);
                    if (finish is Finish.Glass or Finish.Frost) renderer.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }

        private static void Gauge(Batch model, float x, float y, float z, float diameter, int face, float angle)
        {
            model.Rod(Finish.Graphite, diameter, new Vector3(x, y, z), new Vector3(x, y, z + face * .029f));
            model.Rod(Finish.Enamel, diameter * .82f, new Vector3(x, y, z + face * .029f), new Vector3(x, y, z + face * .036f));
            Vector2 direction = new(Mathf.Sin(angle), Mathf.Cos(angle));
            model.Box(Finish.Red, x + direction.x * diameter * .14f, y + direction.y * diameter * .14f,
                z + face * .039f, diameter * .052f, diameter * .44f, .007f, roll: -angle * Mathf.Rad2Deg);
            for (int mark = -2; mark <= 2; mark++)
            {
                float a = mark * .54f;
                model.Box(Finish.Graphite, x + Mathf.Sin(a) * diameter * .31f, y + Mathf.Cos(a) * diameter * .31f,
                    z + face * .040f, diameter * .037f, diameter * .064f, .006f, roll: -a * Mathf.Rad2Deg);
            }
        }

        private static void Crt(Batch model, float x, float y, float z, float width, int face, bool powered = true)
        {
            model.Box(Finish.Graphite, x, y, z, width, width * .72f, .24f);
            model.Box(Finish.Enamel, x, y, z + face * .126f, width * .91f, width * .61f, .035f);
            model.Box(Finish.Graphite, x - width * .075f, y, z + face * .146f, width * .69f, width * .47f, .014f);
            model.Box(powered ? Finish.GreenPhosphor : Finish.Steel, x - width * .075f, y, z + face * .154f,
                width * .60f, width * .37f, .014f);
            for (int row = 1; row <= 3; row++)
                model.Box(powered ? Finish.Teal : Finish.Graphite, x - width * .075f, y + (row - 2) * width * .08f,
                    z + face * .163f, width * .53f, .005f, .003f);
            if (powered)
                model.Box(Finish.WhiteTube, x - width * .115f, y - width * .035f, z + face * .166f,
                    width * .27f, .008f, .004f, roll: 8);
            model.Rod(Finish.Steel, width * .09f, new Vector3(x + width * .35f, y - width * .16f, z + face * .152f),
                new Vector3(x + width * .35f, y - width * .16f, z + face * .173f));
        }

        private static void Case(Batch model, float x, float bottom, float z, float width, float depth,
            float height, int face, Finish shell)
        {
            float front = z + face * depth / 2;
            model.Box(shell, x, bottom + height / 2, z, width, height, depth);
            model.Box(Finish.Edge, x, bottom + height * .78f, z, width * 1.02f, .027f, depth * 1.02f);
            foreach (int side in new[] { -1, 1 })
            {
                model.Box(Finish.Steel, x + side * width * .39f, bottom + height * .68f, front + face * .012f,
                    .056f, height * .31f, .028f);
                model.Box(Finish.Graphite, x + side * width * .19f, bottom + height * .44f, front + face * .029f,
                    .036f, height * .32f, .046f);
            }
            model.Box(Finish.Graphite, x, bottom + height * .35f, front + face * .049f, width * .41f, .028f, .031f);
            model.Box(Finish.Paper, x + width * .24f, bottom + height * .37f, front + face * .017f,
                width * .15f, height * .19f, .009f);
        }

        private static void WallPhone(Batch model, float x, float y, float z)
        {
            model.Box(Finish.Graphite, x, y, z, .28f, .33f, .11f);
            model.Rod(Finish.Edge, .10f, new Vector3(x + .058f, y - .032f, z + .057f), new Vector3(x + .058f, y - .032f, z + .064f));
            model.Box(Finish.Graphite, x - .09f, y, z + .076f, .056f, .27f, .06f);
            foreach (int side in new[] { -1, 1 })
                model.Box(Finish.Steel, x - .09f, y + side * .096f, z + .092f, .085f, .078f, .075f);
        }

        private static void PaperTray(Batch model, float x, float bottom, float z, bool amber)
        {
            var finish = amber ? Finish.Amber : Finish.Steel;
            model.Box(finish, x, bottom + .012f, z, .34f, .024f, .24f);
            foreach (int side in new[] { -1, 1 })
                model.Box(finish, x + side * .155f, bottom + .047f, z, .025f, .07f, .22f);
            model.Box(Finish.Paper, x, bottom + .032f, z, .265f, .017f, .20f);
            for (int line = 0; line < 3; line++)
                model.Box(Finish.Graphite, x - .026f, bottom + .042f, z - .056f + line * .026f, .15f, .002f, .0035f);
        }

        private static void WallFile(Batch model, float x, float y, float wall, Finish pocket)
        {
            model.Box(Finish.Steel, x, y, wall + .015f, .35f, .286f, .036f);
            model.Box(Finish.Paper, x, y + .023f, wall + .039f, .275f, .215f, .016f);
            model.Box(pocket, x, y - .065f, wall + .062f, .35f, .12f, .036f);
            foreach (int side in new[] { -1, 1 })
                model.Box(pocket, x + side * .164f, y + .027f, wall + .063f, .022f, .090f, .040f);
            for (int line = 0; line < 3; line++)
                model.Box(Finish.Graphite, x - .025f, y + .043f + line * .026f, wall + .048f, .163f, .004f, .002f);
        }

        private static void BrokenCable(Batch b, Vector3 a, Vector3 c1, Vector3 c2, Vector3 end, bool floor)
        {
            Vector3 previous = a;
            for (int i = 1; i <= 16; i++)
            {
                float t = i / 16f, u = 1 - t;
                Vector3 point = u * u * u * a + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * end;
                b.Rod(Finish.Graphite, .075f, previous, point);
                previous = point;
            }
            Finish[] colors = { Finish.Red, Finish.CableBlue, Finish.CableGreen, Finish.Amber };
            for (int i = 0; i < colors.Length; i++)
            {
                float fan = (i - 1.5f) * .045f;
                Vector3 tip = floor ? end + new Vector3(.16f, -.020f, fan) : end + new Vector3(fan, -.16f, 0);
                Vector3 root = floor ? new Vector3(end.x, .018f, end.z) : end;
                Vector3 copper = floor ? tip + new Vector3(.035f, 0, fan * .20f) : tip + new Vector3(fan * .15f, -.035f, 0);
                b.Rod(colors[i], .026f, root, tip);
                b.Rod(Finish.Copper, .017f, tip, copper);
            }
        }

        // Render-only groups have separate mounts for room-envelope checks.
        private static void SpareStool(Batch b, float y)
        {
            b.Box(Finish.Wood, 0, y + .47f, 0, .47f, .075f, .43f);
            foreach (float x in new[] { -.18f, .18f })
                foreach (float z in new[] { -.15f, .15f })
                    b.Box(Finish.Edge, x, y + .235f, z, .045f, .47f, .045f);
        }

        private void Dispatch(Batch model, IReadOnlyList<Collider> colliders)
        {
            Refinish(colliders, Finish.Wood, "freight-dispatch-dispatch-desk-top");
            Refinish(colliders, null, "freight-dispatch-dispatch-desk-instrument", "freight-dispatch-dispatch-desk-screen",
                "freight-dispatch-dispatch-task-light-housing");
            model.Box(Finish.WoodEdge, 4.18f, .965f, -2.998f, 2.32f, .053f, .045f);
            // Keep the console strip and forms behind the generated tabletop loot area.
            model.Box(Finish.Wood, 4.10f, 1.182f, -4.054f, 2.09f, .17f, .14f, tilt: 14f);
            model.Box(Finish.WoodEdge, 4.10f, 1.074f, -4.058f, 2.09f, .05f, .123f);
            model.Box(Finish.Graphite, 3.995f, 1.236f, -4.000f, .91f, .151f, .025f, tilt: 14f);
            for (int x = 0; x < 4; x++)
            {
                Gauge(model, 3.61f + x * .213f, 1.247f, -4.008f, .128f, 1, (x - 1.1f) * .51f);
                model.Box(x < 2 ? Finish.AmberLens : Finish.Red, 3.61f + x * .213f, 1.137f, -3.975f, .05f, .025f, .026f);
            }
            Crt(model, 4.76f, 1.25f, -3.775f, .48f, 1);
            WallPhone(model, 3.21f, 1.515f, -4.088f);
            WallFile(model, 4.97f, 1.69f, -4.120f, Finish.Amber);
            model.Box(Finish.Paper, 5.01f, 1.755f, -4.074f, .23f, .23f, .008f, roll: -11);
            model.Box(Finish.Graphite, 4.16f, 3.485f, -2.50f, 1.48f, .132f, .38f);
            model.Box(Finish.Enamel, 4.16f, 3.397f, -2.50f, 1.40f, .040f, .36f);
            foreach (int side in new[] { -1, 1 })
                model.Box(Finish.WhiteTube, 4.16f, 3.364f, -2.50f + side * .095f, 1.30f, .025f, .057f);
            // Dispatch's tag-and-seal rack holds an uneven working stock rather
            // than repeating the sample store's laboratory consumables.
            var tags = model.Mount("Dispatch sorting rack contents", new Vector3(8.92f, 0, 3.835f), Quaternion.identity);
            // Bound dispatch folders stand next to the existing lower bin.
            tags.Box(Finish.Paper, .19f, .78f, 0, .42f, .205f, .33f);
            tags.Box(Finish.Graphite, .19f, .886f, 0, .055f, .008f, .34f);
            tags.Box(Finish.Graphite, .19f, .886f, 0, .43f, .008f, .045f);
            tags.Box(Finish.Enamel, .47f, .78f, .02f, .10f, .205f, .29f);
            // Stacked forms and standing ledgers on the working shelf.
            tags.Box(Finish.Teal, -.38f, 1.08f, .015f, .34f, .085f, .33f);
            tags.Box(Finish.Paper, -.35f, 1.14f, .015f, .30f, .035f, .29f);
            tags.Box(Finish.Enamel, -.32f, 1.19f, .015f, .26f, .045f, .25f, roll: 4);
            foreach (float x in new[] { .02f, .11f, .20f, .29f })
            {
                tags.Box(Finish.WoodEdge, x, 1.22f, .02f, .07f, .365f, .26f);
                tags.Box(Finish.Paper, x, 1.22f, -.115f, .05f, .27f, .01f);
            }
            tags.Box(Finish.Paper, .45f, 1.072f, -.02f, .19f, .055f, .30f, roll: -3);
            // Roll of seal tape and a tray of tags, raised to shelf height.
            tags.Rod(Finish.Amber, .25f, new Vector3(-.36f, 1.782f, -.12f), new Vector3(-.36f, 1.782f, .12f));
            tags.Rod(Finish.Graphite, .07f, new Vector3(-.36f, 1.782f, -.14f), new Vector3(-.36f, 1.782f, .14f));
            tags.Box(Finish.Edge, .15f, 1.69f, 0, .48f, .065f, .33f);
            tags.Box(Finish.Paper, .12f, 1.765f, 0, .35f, .06f, .24f, roll: 4);
            tags.Box(Finish.Red, .21f, 1.802f, -.03f, .10f, .014f, .10f);
            tags.FinishGeometry();
            // Spare stools grouped between the desk and records locker.
            var stools = model.Mount("Dispatch spare stools", new Vector3(2.73f, 0, -3.845f), Quaternion.Euler(0, 17f, 0));
            SpareStool(stools, 0);
            SpareStool(stools, .51f);
            stools.FinishGeometry();
            model.FinishGeometry();
        }

        private void Receiving(Batch model, Transform room, IReadOnlyList<Collider> colliders)
        {
            Refinish(colliders, Finish.Enamel, "receiving-assay-plinth", "receiving-assay-console",
                "receiving-cold-locker-left-door", "receiving-cold-locker-right-door");
            Refinish(colliders, null, "receiving-assay-housing", "receiving-assay-display", "receiving-sorting-rack-crate");
            // Both authored receiving prefabs place these legacy visual slats in front of the new specimen window.
            var details = room.Find("Details");
            if (details)
            {
                foreach (Transform detail in details)
                    if (detail.name == "Analyzer heat sink" && detail.TryGetComponent(out MeshRenderer slat))
                        slat.enabled = false;
            }
            const float x = 3.48f, z = SampleReceivingDressing.AssayZ;
            model.Box(Finish.Enamel, x, .568f, z, .966f, .63f, .972f);
            model.Box(Finish.Graphite, x, 1.230f, z - .445f, .966f, .692f, .084f);
            foreach (int side in new[] { -1, 1 })
            {
                model.Box(Finish.Enamel, x + side * .437f, 1.225f, z, .09f, .695f, .97f);
                model.Box(Finish.Teal, x + side * .388f, 1.229f, z + .468f, .037f, .61f, .04f);
                model.Box(Finish.Red, x + side * .440f, .661f, z + .497f, .045f, .16f, .026f);
            }
            model.Box(Finish.Glass, x, 1.228f, z + .464f, .765f, .597f, .018f);
            model.Box(Finish.Steel, x, .950f, z + .015f, .73f, .056f, .82f);
            model.Box(Finish.WhiteTube, x, 1.539f, z + .296f, .651f, .024f, .09f);
            model.Box(Finish.Steel, x, 1.506f, z - .129f, .54f, .069f, .18f);
            foreach (float vial in new[] { -.23f, -.08f, .08f, .23f })
            {
                model.Rod(Finish.Graphite, .131f, new Vector3(x + vial, .974f, z + .23f), new Vector3(x + vial, 1.015f, z + .23f));
                model.Rod(Finish.Glass, .064f, new Vector3(x + vial, 1.013f, z + .23f), new Vector3(x + vial, 1.208f, z + .23f));
                model.Rod(Finish.Teal, .038f, new Vector3(x + vial, 1.024f, z + .23f), new Vector3(x + vial, 1.103f, z + .23f));
                model.Rod(Finish.Enamel, .079f, new Vector3(x + vial, 1.191f, z + .23f), new Vector3(x + vial, 1.228f, z + .23f));
            }
            model.Box(Finish.Teal, x, .642f, z + .495f, .72f, .25f, .04f);
            model.Box(Finish.Edge, x - .09f, .669f, z + .521f, .31f, .042f, .036f);
            model.Box(Finish.AmberLens, x + .263f, .665f, z + .521f, .066f, .032f, .016f);
            // The rear rim is outside both reserved assay-console loot envelopes.
            const float exhaustZ = z - .451f;
            model.Rod(Finish.Steel, .135f, new Vector3(x - .312f, 1.698f, exhaustZ), new Vector3(x - .312f, 1.875f, exhaustZ));
            model.Rod(Finish.Edge, .145f, new Vector3(x - .312f, 1.875f, exhaustZ), new Vector3(x - .082f, 1.875f, exhaustZ));
            model.Rod(Finish.Graphite, .112f, new Vector3(x - .080f, 1.875f, exhaustZ), new Vector3(x - .060f, 1.875f, exhaustZ));
            PaperTray(model, 1.55f, 1.001f, -3.80f, true);
            Case(model, 8.44f, 1.197f, 3.75f, .55f, .40f, .266f, -1, Finish.Teal);
            model.Box(Finish.Paper, 3.969f, 1.31f, z - .12f, .009f, .19f, .15f, tilt: -12);
            // Packing consumables hang from the wall above the sorting rack.  Shelf surfaces
            // and the open hiding bay below remain available for their original purposes.
            var sorting = model.Mount("Receiving sorting station", new Vector3(7.88f, 2.66f, 4.096f), Quaternion.identity);
            sorting.Box(Finish.Teal, 0, 0, 0, 1.56f, .58f, .055f);
            sorting.Box(Finish.Edge, 0, -.31f, -.035f, 1.62f, .06f, .12f);
            foreach (float railX in new[] { -.71f, .71f })
                sorting.Box(Finish.Edge, railX, 0, -.038f, .055f, .61f, .10f);
            // Paper rolls on a spindle, with a loose end hanging ready to tear.
            sorting.Rod(Finish.Steel, .045f, new Vector3(-.58f, .08f, -.16f), new Vector3(.26f, .08f, -.16f));
            sorting.Rod(Finish.Paper, .28f, new Vector3(-.54f, .08f, -.16f), new Vector3(-.16f, .08f, -.16f));
            sorting.Rod(Finish.Amber, .23f, new Vector3(-.11f, .08f, -.16f), new Vector3(.18f, .08f, -.16f));
            sorting.Box(Finish.Paper, -.35f, -.13f, -.31f, .34f, .28f, .015f, roll: -5);
            // Label pockets make the right side a sorting board, rather than another case.
            foreach (float y in new[] { -.17f, .04f, .22f })
            {
                sorting.Box(Finish.Graphite, .44f, y, -.043f, .33f, .14f, .025f);
                sorting.Box(Finish.Paper, .44f, y + .012f, -.061f, .27f, .075f, .012f);
                sorting.Box(Finish.Red, .56f, y + .012f, -.070f, .025f, .075f, .008f);
            }
            sorting.FinishGeometry();
            // An open packing carton is tucked below the packing table, never in
            // the sorting rack's open bay. Its uneven flaps and exposed roll show interrupted work.
            var carton = model.Mount("Receiving open packing carton", new Vector3(8.40f, 0, -3.43f), Quaternion.Euler(0, -11f, 0));
            carton.Box(Finish.Wood, 0, .035f, 0, .52f, .07f, .39f);
            carton.Box(Finish.Wood, -.245f, .20f, 0, .035f, .34f, .39f);
            carton.Box(Finish.Wood, .245f, .20f, 0, .035f, .34f, .39f);
            carton.Box(Finish.Wood, 0, .20f, -.18f, .47f, .34f, .035f);
            carton.Box(Finish.Wood, 0, .20f, .18f, .47f, .34f, .035f);
            carton.Box(Finish.Paper, 0, .37f, .26f, .40f, .02f, .16f, tilt: 24);
            carton.Rod(Finish.Paper, .16f, new Vector3(-.12f, .10f, -.04f), new Vector3(-.12f, .48f, -.04f));
            carton.Rod(Finish.Amber, .12f, new Vector3(.11f, .10f, .04f), new Vector3(.11f, .37f, .04f));
            carton.Box(Finish.Paper, .06f, .40f, .04f, .27f, .016f, .20f, roll: 14);
            carton.FinishGeometry();
            // A wall-mounted packing dispenser and open sample sleeves at the table end.
            var packing = model.Mount("Receiving packing station", new Vector3(8.70f, 1.45f, -4.087f), Quaternion.identity);
            packing.Box(Finish.Edge, 0, 0, 0, .52f, .66f, .075f);
            packing.Box(Finish.Enamel, 0, .03f, .052f, .45f, .54f, .035f);
            packing.Box(Finish.Teal, 0, -.25f, .12f, .48f, .09f, .24f);
            packing.Rod(Finish.Amber, .29f, new Vector3(-.18f, .10f, .12f), new Vector3(.10f, .10f, .12f));
            packing.Rod(Finish.Graphite, .075f, new Vector3(-.20f, .10f, .12f), new Vector3(.13f, .10f, .12f));
            packing.Box(Finish.Paper, -.04f, -.10f, .27f, .25f, .24f, .012f, roll: 7);
            packing.Box(Finish.Red, .18f, .18f, .085f, .055f, .12f, .06f);
            packing.FinishGeometry();
            // Keep the floor lead next to the solid side of the rack, clear of its lower opening.
            var floorLead = model.Mount("Cable Receiving floor", new Vector3(6.75f, 0, 3.64f), Quaternion.Euler(0, 90f, 0));
            BrokenCable(floorLead, new Vector3(0, .038f, -.15f), new Vector3(.22f, .038f, -.23f),
                new Vector3(.24f, .038f, .17f), new Vector3(.03f, .038f, .12f), true);
            floorLead.FinishGeometry();
            model.FinishGeometry();
        }

        private void Storage(Batch model, bool narrow, IReadOnlyList<Collider> colliders)
        {
            Refinish(colliders, null, "large-containers");
            var inner = narrow ? .65f : 1.10f;
            foreach (int face in new[] { -1, 1 })
            {
                string rack = face < 0 ? "near-rack" : "far-rack";
                Refinish(colliders, null, rack + "-upper-body", rack + "-shelter-back",
                    rack + "-base-left", rack + "-base-right");
                Refinish(colliders, Finish.Steel, rack + "-shelter-roof");
                float bay = face < 0 ? SampleStorage.NearShelterX : SampleStorage.FarShelterX;
                const float height = RoomDressing.RackShelterHeight;
                const float bayDepth = SampleStorage.RackShelterDepth;
                const float halfBay = RoomDressing.RackShelterWidth / 2;
                const float outer = SampleStorage.RackFront;
                float left = bay - halfBay, right = bay + halfBay;
                float center = face * (2.70f + inner) / 2;
                float depth = 2.70f - inner;
                model.Box(Finish.Teal, 8.50f, 1.17f, face * (inner + .055f), 3.28f, 2.28f, .11f);
                foreach (float x in new[] { 6.854f, 7.933f, 9.067f, 10.146f })
                {
                    float bottom = x + .052f > left && x - .052f < right ? height : .005f;
                    model.Box(Finish.Edge, x, (2.395f + bottom) / 2, center, .104f, 2.395f - bottom, depth);
                }
                foreach (float row in new[] { .84f, 1.54f, 2.22f })
                {
                    model.Box(Finish.Steel, 8.50f, row, center, 3.36f, .083f, depth - .04f);
                    model.Box(Finish.Edge, 8.50f, row, face * 2.658f, 3.36f, .102f, .079f);
                }
                LowerShelf(6.82f, left);
                if (right < 10.18f) LowerShelf(right, 10.18f);
                model.Box(Finish.Edge, bay, height + .036f, face * (outer - .024f), 2 * halfBay, .064f, .06f);
                model.Box(Finish.Teal, bay, height / 2, face * (outer - bayDepth - .036f), 2 * halfBay, height, .072f);
                model.Box(Finish.Edge, left - .032f, height / 2, face * (outer - bayDepth / 2), .064f, height, bayDepth);
                float rightCheek = Mathf.Min(.064f, SampleStorage.RackEnd - right);
                model.Box(Finish.Edge, right + rightCheek / 2, height / 2,
                    face * (outer - bayDepth / 2), rightCheek, height, bayDepth);
                foreach (float x in new[] { 7.39f, 8.50f, 9.61f })
                {
                    bool raised = x > left && x < right;
                    Case(model, x, raised ? height + SampleStorage.RackShelterBoard : .176f, face * (raised ? 2.36f : 2.14f),
                        .85f, raised ? .52f : .82f, raised ? .112f : .47f, face, Finish.Steel);
                    Case(model, x, .885f, face * 2.17f, .78f, .74f, .43f, face, Finish.Teal);
                    model.Box(Finish.Graphite, x, 1.641f, face * 2.53f, .82f, .105f, .17f);
                    for (int cassette = -1; cassette <= 1; cassette++)
                    {
                        float slot = x + cassette * .236f;
                        model.Box(Finish.Enamel, slot, 1.867f, face * 2.35f, .207f, .452f, .55f);
                        model.Box(Finish.Edge, slot, 1.818f, face * 2.627f, .17f, .041f, .022f);
                        model.Box(cassette == 1 ? Finish.AmberLens : Finish.GreenPhosphor, slot + .058f, 1.998f,
                            face * 2.636f, .027f, .038f, .02f);
                        model.Box(Finish.Graphite, slot - .024f, 1.994f, face * 2.635f, .09f, .026f, .018f);
                    }
                }
                if (face == 1)
                {
                    foreach (float x in new[] { 7.28f, 7.43f, 7.58f })
                    {
                        model.Rod(Finish.Glass, .093f, new Vector3(x, 2.27f, 2.35f), new Vector3(x, 2.355f, 2.35f));
                        model.Rod(Finish.Amber, .099f, new Vector3(x, 2.346f, 2.35f), new Vector3(x, 2.383f, 2.35f));
                    }
                }

                void LowerShelf(float start, float end)
                {
                    model.Box(Finish.Steel, (start + end) / 2, .13f, center, end - start, .083f, depth - .04f);
                    model.Box(Finish.Edge, (start + end) / 2, .13f, face * 2.658f, end - start, .102f, .079f);
                }
            }
            // Packing supplies at the separate wall rack. The rack's existing left
            // bin remains on the bottom shelf; each group is supported by a shelf.
            var supplies = model.Mount("Storage packing tray contents", new Vector3(13.70f, 0, 6.96f), Quaternion.identity);
            // Preserve the bottom shelf's free Tiny loot site. The two upper
            // shelves retain their central extraction corridor as well.
            supplies.Box(Finish.Teal, -.32f, 1.075f, .01f, .27f, .075f, .34f);
            supplies.Box(Finish.Edge, -.32f, 1.13f, .01f, .25f, .035f, .30f);
            supplies.Box(Finish.Enamel, -.32f, 1.17f, .01f, .23f, .045f, .26f, roll: 5);
            supplies.Rod(Finish.Paper, .14f, new Vector3(.27f, 1.108f, -.12f), new Vector3(.27f, 1.108f, .14f));
            supplies.Rod(Finish.Amber, .15f, new Vector3(.375f, 1.113f, -.11f), new Vector3(.375f, 1.113f, .13f));
            supplies.Box(Finish.Graphite, -.32f, 1.69f, 0, .26f, .065f, .30f);
            foreach (float vialX in new[] { -.39f, -.25f })
            {
                supplies.Rod(Finish.Glass, .06f, new Vector3(vialX, 1.723f, 0), new Vector3(vialX, 1.94f, 0));
                supplies.Rod(Finish.Enamel, .075f, new Vector3(vialX, 1.93f, 0), new Vector3(vialX, 1.97f, 0));
            }
            supplies.Box(Finish.Paper, .30f, 1.762f, .01f, .28f, .18f, .30f, roll: -4);
            supplies.Box(Finish.Red, .30f, 1.858f, -.03f, .12f, .012f, .12f);
            supplies.FinishGeometry();
            Case(model, 11.55f, 0, -1.89f, 1.46f, 2.23f, 1.70f, -1, Finish.Teal);
            model.Box(Finish.Paper, 9.61f, 2.09f, 2.650f, .22f, .17f, .008f, roll: 13);
            // Detached panel leans against the large container's outer end.
            var loose = model.Mount("Storage detached container panel", new Vector3(12.43f, 0, -1.91f), Quaternion.identity);
            loose.Box(Finish.Enamel, 0, .52f, 0, .075f, 1.04f, .80f, roll: 10);
            loose.Box(Finish.Graphite, .055f, .57f, 0, .025f, .14f, .55f, roll: 10);
            loose.Box(Finish.Amber, .07f, .83f, -.21f, .025f, .09f, .15f, roll: 10);
            loose.FinishGeometry();
            var floorLead = model.Mount("Cable Storage floor", new Vector3(5.13f, 0, -5.85f), Quaternion.Euler(0, 24f, 0));
            BrokenCable(floorLead, new Vector3(-.28f, .038f, -.05f), new Vector3(-.10f, .038f, .26f),
                new Vector3(.23f, .038f, -.20f), new Vector3(.30f, .038f, .03f), true);
            floorLead.FinishGeometry();
            model.FinishGeometry();
        }

        private void Service(Batch model, Transform room, List<Collider> colliders)
        {
            Refinish(colliders, Finish.Wood, "instrument-service-inspection-bench-top");
            Refinish(colliders, Finish.Teal, "instrument-service-probe-test-rig");
            Refinish(colliders, null, "instrument-service-probe-rig-indication");
            bool exposed = brokenEquipment.TryDressInstrument(room, colliders);
            if (exposed)
            {
                Refinish(colliders, Finish.Enamel, BrokenEquipment.Instrument);
                Refinish(colliders, Finish.Steel, BrokenEquipment.Display);
            }
            else
            {
                Refinish(colliders, null, BrokenEquipment.Instrument, BrokenEquipment.Display);
                Crt(model, 1.7325f, 1.205f, -3.775f, .53f, 1);
            }
            model.Box(Finish.WoodEdge, 1.34f, 1.68f, -4.105f, 1.46f, 1.17f, .069f);
            model.Box(Finish.Enamel, 1.34f, 1.68f, -4.063f, 1.37f, 1.08f, .02f);
            for (int column = 0; column < 13; column++)
                for (int row = 0; row < 8; row++)
                    model.Box(Finish.WoodEdge, .73f + column * .101f, 1.201f + row * .133f, -4.051f, .017f, .017f, .007f);
            foreach (float x in new[] { .86f, 1.08f, 1.31f })
            {
                model.Box(Finish.Steel, x, 1.75f, -4.015f, .035f, .36f, .045f, roll: x * 14 - 15);
                model.Box(Finish.Steel, x + .03f, 1.925f, -4.014f, .12f, .046f, .044f);
                model.Box(Finish.Red, x, 1.596f, -4.008f, .048f, .138f, .048f);
            }
            model.Box(Finish.Steel, 1.35f, 2.32f, -3.943f, 1.52f, .08f, .33f);
            model.Box(Finish.WhiteTube, 1.35f, 2.261f, -3.859f, 1.28f, .024f, .054f);
            model.Box(Finish.Edge, 1.35f, 2.237f, -3.83f, 1.39f, .015f, .016f);
            Case(model, .84f, 2.36f, -3.953f, .305f, .21f, .15f, 1, Finish.Teal);
            foreach (float x in new[] { 1.63f, 1.73f, 1.83f })
            {
                model.Rod(Finish.Graphite, .022f, new Vector3(x, 1.43f, -3.961f), new Vector3(x, 1.67f, -4.00f));
                model.Rod(Finish.Graphite, .022f, new Vector3(x, 1.67f, -4.00f), new Vector3(x + .20f, 1.92f, -4.015f));
            }
            // Face the freight spur from the rig's west side, beside the fixed front screen.
            var panel = model.Mount("Probe rig service side", new Vector3(6.72f, 0, 3.42f), Quaternion.Euler(0, -90f, 0));
            panel.Box(Finish.Graphite, -.094f, .74f, .010f, .72f, .90f, .037f);
            panel.Box(Finish.Circuit, -.094f, .74f, .033f, .54f, .74f, .02f);
            panel.Box(Finish.Enamel, -.434f, .74f, -.007f, .11f, .90f, .073f, roll: 4);
            panel.Box(Finish.Edge, -.094f, .318f, .014f, .69f, .05f, .051f);
            for (int circuit = 0; circuit < 6; circuit++)
            {
                float y = .485f + circuit * .097f;
                panel.Box(Finish.Brass, -.127f, y, .047f, .42f, .025f, .015f);
                panel.Box(circuit % 2 == 0 ? Finish.Graphite : Finish.Steel, -.118f + (circuit % 2) * .11f,
                    y + .03f, .061f, .17f, .066f, .028f);
            }
            Gauge(panel, .366f, 1.08f, .011f, .25f, 1, -.62f);
            panel.Box(Finish.AmberLens, .366f, .83f, .044f, .15f, .042f, .025f);
            panel.Box(Finish.Enamel, -.40f, .54f, .045f, .22f, .39f, .026f, roll: -18);
            // Interrupted repair: a disconnected cable drapes over the fixed service panel.
            panel.Rod(Finish.Graphite, .035f, new Vector3(.12f, .99f, .045f), new Vector3(.12f, .99f, .080f));
            panel.Rod(Finish.Graphite, .035f, new Vector3(.12f, .99f, .080f), new Vector3(.23f, .85f, .080f));
            panel.Rod(Finish.Graphite, .035f, new Vector3(.23f, .85f, .080f), new Vector3(.18f, .66f, .080f));
            panel.Rod(Finish.Graphite, .035f, new Vector3(.18f, .66f, .080f), new Vector3(.28f, .56f, .080f));
            panel.Rod(Finish.Edge, .055f, new Vector3(.28f, .56f, .080f), new Vector3(.31f, .52f, .080f));
            panel.FinishGeometry();
            // Keep the repair parts by the west wall, outside the approach under the bench.
            var repair = model.Mount("Service opened instrument chassis", new Vector3(.50f, 0, -2.65f), Quaternion.Euler(0, 90f, 0));
            repair.Box(Finish.Graphite, 0, .20f, 0, .52f, .40f, .38f);
            repair.Box(Finish.Circuit, 0, .414f, 0, .42f, .025f, .29f);
            foreach (float x in new[] { -.13f, 0f, .13f })
                repair.Box(Finish.Brass, x, .435f, 0, .035f, .025f, .23f);
            repair.Box(Finish.Enamel, .32f, .40f, .02f, .055f, .77f, .38f, roll: -13);
            repair.Box(Finish.Edge, .36f, .50f, .02f, .018f, .08f, .25f, roll: -13);
            repair.FinishGeometry();
            // A test cradle and removed components sit on the opened chassis, with the
            // diagnostic board fixed to the wall behind it. The bench shelter stays open.
            var repairStation = model.Mount("Service repair station", new Vector3(.50f, 0, -2.65f), Quaternion.Euler(0, 90f, 0));
            repairStation.Box(Finish.Steel, -.10f, .47f, -.04f, .25f, .08f, .25f);
            repairStation.Rod(Finish.Graphite, .17f, new Vector3(-.10f, .51f, -.04f), new Vector3(-.10f, .73f, -.04f));
            repairStation.Rod(Finish.Brass, .20f, new Vector3(-.10f, .58f, -.04f), new Vector3(-.10f, .62f, -.04f));
            repairStation.Rod(Finish.Brass, .20f, new Vector3(-.10f, .67f, -.04f), new Vector3(-.10f, .71f, -.04f));
            repairStation.Box(Finish.Enamel, .12f, .50f, .05f, .14f, .12f, .13f, roll: -12);
            repairStation.Box(Finish.AmberLens, .12f, .565f, .05f, .09f, .018f, .09f, roll: -12);
            repairStation.Rod(Finish.Red, .028f, new Vector3(.12f, .49f, -.11f), new Vector3(.19f, .58f, -.09f));
            repairStation.Rod(Finish.Red, .028f, new Vector3(.19f, .58f, -.09f), new Vector3(.12f, .66f, -.05f));
            repairStation.FinishGeometry();
            // Portable diagnostic instrument, kept on the unused left-hand part
            // of the bench clear of the fractured instrument's loot pocket.
            var tester = model.Mount("Service bench diagnostic tester", new Vector3(.99f, 1.00f, -3.60f), Quaternion.Euler(0, -8f, 0));
            tester.Box(Finish.Graphite, 0, .20f, 0, .53f, .36f, .39f);
            tester.Box(Finish.Enamel, 0, .20f, .205f, .49f, .32f, .025f);
            tester.Box(Finish.Edge, -.075f, .22f, .223f, .28f, .22f, .015f);
            tester.Box(Finish.GreenPhosphor, -.075f, .22f, .234f, .22f, .16f, .008f);
            tester.Rod(Finish.Amber, .045f, new Vector3(.16f, .25f, .22f), new Vector3(.16f, .25f, .25f));
            tester.Rod(Finish.Red, .045f, new Vector3(.16f, .15f, .22f), new Vector3(.16f, .15f, .25f));
            foreach (float x in new[] { -.18f, .18f })
            {
                tester.Box(Finish.Edge, x, .025f, 0, .08f, .05f, .33f);
                tester.Rod(Finish.Steel, .035f, new Vector3(x, .38f, -.10f), new Vector3(x, .48f, -.10f));
            }
            tester.Rod(Finish.Steel, .04f, new Vector3(-.18f, .48f, -.10f), new Vector3(.18f, .48f, -.10f));
            tester.Rod(Finish.Graphite, .025f, new Vector3(.23f, .11f, .12f), new Vector3(.31f, .055f, .14f));
            tester.Rod(Finish.Graphite, .025f, new Vector3(.31f, .055f, .14f), new Vector3(.38f, .035f, .04f));
            tester.FinishGeometry();
            var wallLead = model.Mount("Cable Service wall", new Vector3(.77f, 1.76f, -4.008f), Quaternion.identity);
            wallLead.Box(Finish.Edge, 0, 0, -.012f, .13f, .12f, .055f);
            BrokenCable(wallLead, new Vector3(0, -.04f, .045f), new Vector3(-.20f, -.15f, .045f),
                new Vector3(.14f, -.34f, .045f), new Vector3(.08f, -.48f, .045f), false);
            wallLead.FinishGeometry();
            model.FinishGeometry();
        }

        private static void Calibration(Batch model)
        {
            foreach (float x in new[] { 8.65f, 11.25f, 13.85f })
            {
                foreach (float flange in new[] { -.165f, .165f })
                    model.Rod(Finish.Edge, 1.337f, new Vector3(x + flange - .029f, 3.17f, 0),
                        new Vector3(x + flange + .029f, 3.17f, 0));
                // Thin magnetic winding detail crosses the face of the existing coil jacket.
                foreach (float winding in new[] { -.105f, -.067f, -.029f, .029f, .067f, .105f })
                    model.Rod(Finish.Brass, 1.289f, new Vector3(x + winding - .011f, 3.17f, 0),
                        new Vector3(x + winding + .011f, 3.17f, 0));
                foreach (float z in new[] { -.45f, .45f })
                    model.Box(Finish.Steel, x, 2.61f, z, .47f, .23f, .19f, tilt: Mathf.Sign(z) * 17f);
            }
            model.Rod(Finish.Steel, .79f, new Vector3(8.075f, 3.17f, 0), new Vector3(8.22f, 3.17f, 0));
            model.Rod(Finish.Enamel, .58f, new Vector3(8.049f, 3.17f, 0), new Vector3(8.08f, 3.17f, 0));
            model.Rod(Finish.Teal, .32f, new Vector3(8.005f, 3.17f, 0), new Vector3(8.05f, 3.17f, 0));
            model.Rod(Finish.Graphite, .15f, new Vector3(7.96f, 3.17f, 0), new Vector3(8.006f, 3.17f, 0));
            foreach (float z in new[] { -.73f, .73f })
            {
                model.Rod(Finish.Teal, .155f, new Vector3(8.51f, 2.56f, z), new Vector3(14.14f, 2.56f, z));
                model.Rod(Finish.Steel, .20f, new Vector3(11.22f, 2.56f, z), new Vector3(11.40f, 2.56f, z));
            }
            model.Box(Finish.Enamel, 10.85f, .87f, 2.853f, 1.73f, .49f, .051f);
            Crt(model, 10.53f, .874f, 2.774f, .55f, 1);
            Gauge(model, 11.00f, .943f, 2.874f, .20f, 1, -.54f);
            Gauge(model, 11.38f, .943f, 2.874f, .20f, 1, .81f);
            model.Box(Finish.Paper, 11.53f, .83f, 2.888f, .17f, .19f, .008f, roll: -12);
            for (int index = 0; index < 5; index++)
                model.Box(index % 2 == 0 ? Finish.AmberLens : Finish.Teal, 10.865f + index * .154f,
                    .710f, 2.890f, .068f, .042f, .025f);
            foreach (float x in new[] { 8.13f, 14.40f })
                model.Box(Finish.Amber, x, .006f, 2.31f, .061f, .009f, .48f);
            for (int stripe = 0; stripe < 11; stripe++)
                model.Box(Finish.Amber, 8.29f + stripe * .57f, .006f, 2.55f, .31f, .009f, .068f);
            model.FinishGeometry();
        }

        private void Observation(Batch model, bool broken, string prefix, IReadOnlyList<Collider> colliders)
        {
            string lower = prefix + "observation-lower-";
            Refinish(colliders, Finish.Wood, lower + "instrument-bench-top", prefix + "observation-gallery-operator-desk-top");
            Refinish(colliders, null, lower + "instrument-bench-instrument", lower + "instrument-bench-screen",
                lower + (broken ? "sealed-camera-cases" : "optics-alignment-cradle"));
            Refinish(colliders, Finish.Teal, prefix + "gallery-rail");
            Crt(model, 5.29f, 1.22f, 6.87f, .56f, -1, !broken);
            Case(model, 4.28f, 0, -2.742f, 2.43f, 1.12f, broken ? 1.26f : 1.06f, 1,
                broken ? Finish.Graphite : Finish.Teal);
            model.Box(Finish.Amber, 4.28f, .43f, -2.167f, .61f, .068f, .022f);

            // The control post occupies the outer rail. The rail's collider and the landing stay in place.
            foreach (float x in new[] { 10.31f, 11.12f, 11.94f, 12.76f, 13.67f, 14.66f })
                model.Box(Finish.Edge, x, 4.10f, -3.316f, .072f, 1.005f, .137f);
            model.Box(Finish.Steel, 12.50f, 4.606f, -3.325f, 4.48f, .092f, .175f);
            model.Box(Finish.Enamel, 13.70f, 6.390f, -3.329f, 2.01f, .092f, .14f);
            foreach (float x in new[] { 12.72f, 13.67f, 14.68f })
                model.Box(Finish.Enamel, x, 5.503f, -3.329f, .085f, 1.825f, .15f);
            foreach (float x in new[] { 13.195f, 14.175f })
            {
                model.Box(Finish.Glass, x, 5.617f, -3.335f, .874f, 1.443f, .016f);
                model.Box(Finish.Frost, x, 4.730f, -3.335f, .874f, .327f, .016f);
            }
            model.Box(Finish.Edge, 13.70f, 4.91f, -3.330f, 1.96f, .045f, .13f);
            Crt(model, 14.13f, 4.864f, -3.50f, .44f, -1, !broken);
            model.Box(Finish.Paper, 12.72f, 5.18f, -3.409f, .21f, .27f, .009f, roll: 9);
            model.Box(Finish.Teal, 11.07f, 4.674f, -3.263f, .36f, .091f, .25f);
            model.Box(Finish.Steel, 11.07f, 4.789f, -3.258f, .14f, .151f, .127f);
            model.Rod(Finish.Graphite, .187f, new Vector3(11.07f, 4.870f, -3.441f), new Vector3(11.07f, 4.804f, -3.026f));
            model.Rod(Finish.Edge, .215f, new Vector3(11.07f, 4.819f, -3.101f), new Vector3(11.07f, 4.799f, -3.003f));
            model.Rod(broken ? Finish.Graphite : Finish.Teal, .159f,
                new Vector3(11.07f, 4.8f, -3.006f), new Vector3(11.07f, 4.795f, -2.992f));

            if (broken)
            {
                // These lips follow the actual inner slab edges. They leave the jump and fall opening clear.
                foreach (int side in new[] { -1, 1 })
                {
                    float x = side < 0 ? 9.785f : 10.765f;
                    for (int stripe = 0; stripe < 6; stripe++)
                    {
                        float z = -1.35f + stripe * .48f;
                        model.Box(stripe % 2 == 0 ? Finish.Amber : Finish.Graphite, x, -.098f, z, .035f, .10f, .43f);
                    }
                }
                model.Box(Finish.Graphite, 13.70f, 5.15f, -3.405f, .72f, .12f, .028f, roll: -7);
                model.Box(Finish.Red, 13.70f, 5.15f, -3.422f, .62f, .047f, .01f, roll: -7);
                model.Rod(Finish.Red, .018f, new Vector3(10.68f, -.31f, 1.49f), new Vector3(10.49f, -.71f, 1.42f));
                model.Rod(Finish.Graphite, .023f, new Vector3(10.49f, -.71f, 1.42f), new Vector3(10.45f, -1.06f, 1.43f));
            }
            model.FinishGeometry();
        }

        private void GalleryTransfer(Batch model, IReadOnlyList<Collider> colliders)
        {
            const string bench = "small-lens-inspection-sample-transfer-table-";
            Refinish(colliders, null, bench + "instrument", bench + "screen");
            // Reuse the original monitor footprint and mount paperwork against the transfer wall.
            Crt(model, 4.075f, 1.25f, -3.15f, .43f, 1, false);
            WallFile(model, 2.86f, 1.48f, -3.496f, Finish.Steel);
            model.Box(Finish.Paper, 2.90f, 1.55f, -3.449f, .22f, .22f, .008f, roll: 12);
            model.Box(Finish.Teal, 6.348f, .722f, 0, .035f, .69f, 3.95f);
            foreach (float z in new[] { -1.23f, 0f, 1.23f })
                model.Box(Finish.Edge, 6.319f, .74f, z, .03f, .105f, .42f);
            model.FinishGeometry();
        }

        private void Shaft(Batch model, IReadOnlyList<Collider> colliders)
        {
            Refinish(colliders, Finish.Teal, "shaft-apparatus", "plant-feed-switchgear-left-door", "plant-feed-switchgear-right-door");
            Refinish(colliders, Finish.Teal, "plant-balcony-instrument-cabinet");
            Refinish(colliders, Finish.Graphite, "plant-gallery-spares-chest");
            Refinish(colliders, Finish.Graphite, "plant-balcony-rack-cases-back", "plant-gallery-table-drawers");
            Refinish(colliders, Finish.Steel, "plant-balcony-rack-shelf", "plant-balcony-rack-shelter-roof",
                "plant-balcony-rack-case-shelf", "plant-gallery-table-near-leg", "plant-gallery-table-far-leg");
            Refinish(colliders, Finish.Wood, "plant-gallery-table-top");
            Refinish(colliders, Finish.Teal, "plant-gallery-chair-end-seat", "plant-gallery-chair-end-back",
                "plant-gallery-chair-side-seat", "plant-gallery-chair-side-back");
            foreach (int side in new[] { -1, 1 })
            foreach (int face in new[] { -1, 1 })
                Refinish(colliders, Finish.Steel, $"plant-balcony-rack-upright-{side}-{face}",
                    $"plant-gallery-chair-end-leg-{side}-{face}", $"plant-gallery-chair-side-leg-{side}-{face}");
            Refinish(colliders, null, "middle-guard-front", "middle-guard-west-wing-front", "middle-guard-step",
                "middle-guard-west", "middle-guard-east",
                "middle-guard-west-return", "middle-guard-east-return", "upper-guard-front", "upper-guard-west",
                "upper-guard-west-return", "balcony-console", "gallery-lockers");
            foreach (float x in new[] { 8.45f, 11.00f, 13.52f })
            {
                model.Box(Finish.Graphite, x, 3.76f, 5.09f, 1.75f, .21f, 1.81f);
                model.Rod(Finish.Teal, 1.41f, new Vector3(x, 3.75f, 5.09f), new Vector3(x, 7.14f, 5.09f));
                foreach (float band in new[] { 3.95f, 5.43f, 6.86f })
                    model.Rod(Finish.Edge, 1.51f, new Vector3(x, band - .045f, 5.09f), new Vector3(x, band + .045f, 5.09f));
                model.Rod(Finish.Steel, .79f, new Vector3(x, 7.13f, 5.09f), new Vector3(x, 7.40f, 5.09f));
                model.Rod(Finish.Steel, .285f, new Vector3(x, 7.39f, 5.09f), new Vector3(x, 8.38f, 5.09f));
                model.Rod(Finish.Edge, .39f, new Vector3(x, 8.28f, 5.09f), new Vector3(x, 8.45f, 5.09f));
                model.Box(Finish.Steel, x, 1.72f, 3.494f, 1.69f, 2.76f, .039f);
                model.Box(Finish.Graphite, x, 1.98f, 3.465f, 1.48f, .50f, .034f);
                Gauge(model, x - .39f, 1.98f, 3.445f, .29f, -1, x * .32f);
                model.Box(Finish.AmberLens, x + .42f, 1.984f, 3.441f, .21f, .058f, .025f);
                for (int grille = 0; grille < 5; grille++)
                    model.Box(Finish.Edge, x, .51f + grille * .114f, 3.463f, 1.22f, .036f, .044f);
            }
            model.Box(Finish.Graphite, 11.00f, 7.83f, 5.09f, 7.98f, .54f, 1.74f);
            model.Box(Finish.Steel, 11.00f, 7.551f, 5.09f, 7.98f, .09f, 2.04f);
            foreach (float x in new[] { 7.29f, 14.70f })
            {
                model.Box(Finish.Edge, x, 9.48f, 5.09f, .28f, 2.88f, .48f);
                model.Box(Finish.Amber, x, 7.77f, 4.209f, .49f, .22f, .033f);
            }
            for (int line = 0; line < 3; line++)
            {
                float y = 4.35f + line * .31f;
                float z = 5.65f + line * .37f;
                var finish = line == 1 ? Finish.Edge : Finish.Steel;
                model.Rod(finish, .196f, new Vector3(7.37f, y, z), new Vector3(14.66f, y, z));
                model.Rod(finish, .196f, new Vector3(14.66f, y, z), new Vector3(14.66f, 8.45f, z));
                model.Rod(Finish.Edge, .264f, new Vector3(14.66f, y + .25f, z), new Vector3(14.66f, y + .42f, z));
            }
            var wallLead = model.Mount("Cable Shaft wall", new Vector3(13.16f, 1.36f, 3.414f), Quaternion.identity);
            wallLead.Box(Finish.Edge, 0, 0, .030f, .15f, .12f, .100f);
            BrokenCable(wallLead, new Vector3(0, -.04f, -.04f), new Vector3(.27f, -.06f, -.04f),
                new Vector3(.23f, -.38f, -.04f), new Vector3(.35f, -.47f, -.04f), false);
            wallLead.FinishGeometry();
            model.Box(Finish.Paper, 8.98f, 1.59f, 3.470f, .19f, .24f, .009f, roll: -14);
            // Loose fastener plate rests askew against the adjacent fixed pump fascia.
            model.Box(Finish.Edge, 11.45f, 1.12f, 3.460f, .42f, .30f, .025f, roll: 13);
            model.Box(Finish.Graphite, 11.45f, 1.12f, 3.443f, .30f, .055f, .008f, roll: 13);
            model.Rod(Finish.Brass, .055f, new Vector3(11.29f, 1.20f, 3.443f), new Vector3(11.29f, 1.20f, 3.432f));
            const float inset = .06f;
            const float middle = ShaftPlant.MiddleY, upper = ShaftPlant.UpperY;
            const float back = ShaftPlant.DeckBack + inset;
            const float west = ShaftPlant.BalconyWest + inset;
            const float step = ShaftPlant.BalconyStep + inset;
            const float east = ShaftPlant.BalconyEast - inset;
            const float wideFront = ShaftPlant.BalconyEdge - inset, wingFront = ShaftPlant.BalconyWestEdge - inset;
            const float galleryWest = ShaftPlant.GalleryWest + inset, galleryFront = ShaftPlant.GalleryEdge - inset;
            Rail(new(west, middle, back), new(west, middle, wingFront));
            Rail(new(west, middle, wingFront), new(step, middle, wingFront));
            Rail(new(step, middle, wingFront), new(step, middle, wideFront));
            Rail(new(step, middle, wideFront), new(east, middle, wideFront));
            Rail(new(east, middle, wideFront), new(east, middle, back));
            Rail(new(west, middle, back), new(9.69f, middle, back));
            Rail(new(east, middle, back), new(11.06f, middle, back));
            Rail(new(galleryWest, upper, galleryFront), new(22.25f, upper, galleryFront));
            Rail(new(galleryWest, upper, galleryFront), new(galleryWest, upper, back));
            Rail(new(galleryWest, upper, back), new(18.94f, upper, back));
            DeckEdge(west, ShaftPlant.BalconyStep, middle, ShaftPlant.BalconyWestEdge, back);
            DeckEdge(ShaftPlant.BalconyStep, east, middle, ShaftPlant.BalconyEdge, back);
            DeckEdge(galleryWest, 22.25f, upper, ShaftPlant.GalleryEdge, back);
            model.Box(Finish.Teal, ShaftPlant.BalconyStep - .009f, middle - .151f,
                (ShaftPlant.BalconyWestEdge + ShaftPlant.BalconyEdge) / 2, .038f, .185f,
                ShaftPlant.BalconyEdge - ShaftPlant.BalconyWestEdge);

            var console = model.Mount("Balcony pump control desk", new(13.17f, 3.60f, -2.10f),
                Quaternion.Euler(0, -90, 0));
            console.Box(Finish.Graphite, 0, .095f, -.57f, 1.71f, .19f, 1.11f);
            console.Box(Finish.Teal, 0, .51f, -.61f, 1.67f, .79f, 1.03f);
            console.Box(Finish.Edge, 0, .949f, -.57f, 1.78f, .115f, 1.14f);
            console.Box(Finish.Graphite, 0, 1.04f, -.75f, 1.72f, .14f, .76f);
            Crt(console, -.44f, 1.075f, -.46f, .45f, 1);
            Gauge(console, .11f, 1.085f, -.360f, .14f, 1, .49f);
            Gauge(console, .32f, 1.085f, -.360f, .14f, 1, -.41f);
            for (int i = 0; i < 4; i++)
            {
                float x = .04f + i * .18f;
                console.Box(Finish.Graphite, x, 1.013f, -.182f, .115f, .028f, .14f);
                console.Box(i == 3 ? Finish.Red : Finish.AmberLens, x, 1.039f, -.182f, .055f, .025f, .055f);
            }
            console.Box(Finish.Enamel, 0, .635f, -.087f, .53f, .12f, .022f);
            console.Box(Finish.Amber, .51f, .77f, -1.135f, .57f, .10f, .027f);
            console.FinishGeometry();

            model.Box(Finish.Graphite, 21.965f, 7.28f, -1.745f, .55f, .16f, 2.3f);
            model.Box(Finish.Steel, 21.984f, 8.286f, -1.745f, .53f, 1.87f, 2.33f);
            foreach (float z in new[] { -2.52f, -1.745f, -.97f })
            {
                model.Box(Finish.Enamel, 21.705f, 8.28f, z, .051f, 1.70f, .712f);
                model.Box(Finish.Graphite, 21.665f, 8.26f, z + .237f, .046f, .218f, .045f);
                model.Box(Finish.Teal, 21.668f, 8.92f, z, .032f, .082f, .26f);
                for (int vent = 0; vent < 3; vent++)
                    model.Box(Finish.Graphite, 21.67f, 7.69f + vent * .065f, z, .027f, .024f, .35f);
            }
            foreach (float z in new[] { -6.338f, -4.162f })
                model.Box(Finish.Amber, 22.244f, 8.446f, z, .040f, 2.42f, .095f);

            float cabinetX = ShaftPlant.InstrumentCabinetX, cabinetZ = ShaftPlant.InstrumentCabinetZ;
            float chestX = ShaftPlant.SparesChestX, chestZ = ShaftPlant.SparesChestZ;
            float cabinetTop = middle + ShaftPlant.ServiceCoverHeight;
            float chestTop = upper + ShaftPlant.ServiceCoverHeight;
            for (int row = 0; row < 3; row++)
            {
                float y = middle + .27f + row * .25f;
                model.Box(Finish.Enamel, cabinetX, y, cabinetZ + .362f, 1.28f, .208f, .016f);
                model.Box(Finish.Graphite, cabinetX, y + .034f, cabinetZ + .377f, .36f, .030f, .025f);
                model.Box(Finish.Teal, cabinetX - .48f, y, cabinetZ + .374f, .13f, .047f, .006f);
            }
            model.Box(Finish.Steel, cabinetX, cabinetTop + .002f, cabinetZ, 1.38f, .010f, .70f);
            model.Box(Finish.Graphite, cabinetX - .42f, cabinetTop + .035f, cabinetZ, .26f, .070f, .22f);
            Gauge(model, cabinetX - .42f, cabinetTop + .038f, cabinetZ + .110f, .057f, 1, .49f);
            model.Rod(Finish.Steel, .044f, new(cabinetX + .04f, cabinetTop + .025f, cabinetZ + .08f),
                new(cabinetX + .35f, cabinetTop + .025f, cabinetZ + .13f));
            model.Box(Finish.Brass, cabinetX + .43f, cabinetTop + .026f, cabinetZ - .07f, .15f, .052f, .073f, roll: 6);

            model.Box(Finish.Steel, chestX, chestTop + .002f, chestZ, 1.38f, .010f, .78f);
            model.Box(Finish.Amber, chestX, upper + .62f, chestZ + .402f, 1.27f, .59f, .016f);
            foreach (float x in new[] { chestX - .48f, chestX + .48f })
            {
                model.Box(Finish.Edge, x, upper + .62f, chestZ + .414f, .067f, .46f, .022f);
                model.Box(Finish.Graphite, x, upper + .86f, chestZ + .427f, .11f, .092f, .025f);
            }
            model.Box(Finish.Graphite, chestX, upper + .66f, chestZ + .418f, .32f, .048f, .026f);
            float caseX = chestX + .31f, caseZ = chestZ + .02f;
            model.Box(Finish.Edge, caseX, chestTop + .018f, caseZ, .44f, .036f, .26f);
            model.Box(Finish.Graphite, caseX, chestTop + .039f, caseZ, .39f, .018f, .213f);
            foreach (int side in new[] { -1, 1 })
                model.Box(Finish.Teal, caseX + side * .206f, chestTop + .049f, caseZ, .026f, .064f, .26f);
            model.Box(Finish.Teal, caseX, chestTop + .049f, caseZ + .117f, .40f, .064f, .026f);
            model.Box(Finish.Teal, caseX, chestTop + .099f, caseZ - .147f, .44f, .029f, .18f, tilt: 65);
            model.Rod(Finish.Steel, .038f, new(caseX - .13f, chestTop + .057f, caseZ + .04f),
                new(caseX + .13f, chestTop + .057f, caseZ + .04f));
            model.Box(Finish.Brass, chestX - .30f, chestTop + .036f, chestZ - .035f, .25f, .072f, .16f, roll: -5);

            float rackX = ShaftPlant.RackX, rackZ = ShaftPlant.RackZ;
            foreach (int side in new[] { -1, 1 })
            {
                Refinish(colliders, Finish.Graphite, $"plant-balcony-rack-cheek-{side}");
                Refinish(colliders, null, $"plant-balcony-rack-case-{side}");
                float x = rackX + .265f * side;
                for (int row = 0; row < 2; row++)
                    Case(model, x, middle + .67f + .155f * row, rackZ + .008f, .50f, .535f, .155f, 1,
                        row == 0 ? Finish.Teal : Finish.Enamel);
            }
            Case(model, rackX + .34f, middle + 1.055f, rackZ + .02f, .25f, .42f, .14f, 1, Finish.Enamel);

            float tableX = ShaftPlant.TableX, tableZ = ShaftPlant.TableZ, drawersX = ShaftPlant.TableDrawersX;
            for (int drawer = 0; drawer < 3; drawer++)
            {
                float y = upper + .22f + drawer * .265f;
                model.Box(Finish.Enamel, drawersX, y, tableZ - .341f, .77f, .228f, .015f);
                model.Box(Finish.Graphite, drawersX, y + .036f, tableZ - .353f, .24f, .034f, .025f);
            }
            model.Box(Finish.WoodEdge, tableX, upper + 1.032f, tableZ - .401f, 1.79f, .063f, .016f);
            PaperTray(model, tableX - .35f, upper + 1.07f, tableZ + .15f, amber: false);
            model.Box(Finish.Brass, tableX + .16f, upper + 1.086f, tableZ - .07f, .29f, .032f, .057f, roll: 5);
            // Instruments occupy only the supported part of the new gallery worktop.
            var benchTools = model.Mount("Shaft gallery bench tools", new Vector3(tableX, upper + 1.07f, tableZ), Quaternion.identity);
            benchTools.Box(Finish.Graphite, .13f, .105f, .15f, .34f, .21f, .25f);
            benchTools.Box(Finish.Enamel, .13f, .115f, .019f, .30f, .17f, .018f);
            benchTools.Box(Finish.Circuit, .095f, .142f, .008f, .17f, .068f, .008f);
            benchTools.Box(Finish.GreenPhosphor, .095f, .142f, .002f, .13f, .035f, .005f);
            benchTools.Rod(Finish.Edge, .055f, new Vector3(.222f, .105f, .006f), new Vector3(.222f, .105f, -.008f));
            benchTools.Rod(Finish.Red, .028f, new Vector3(.02f, .035f, .009f), new Vector3(-.07f, .02f, -.10f));
            benchTools.Rod(Finish.Red, .028f, new Vector3(-.07f, .02f, -.10f), new Vector3(-.18f, .018f, -.16f));
            benchTools.Rod(Finish.Steel, .018f, new Vector3(-.18f, .018f, -.16f), new Vector3(-.25f, .018f, -.19f));
            benchTools.Rod(Finish.Graphite, .025f, new Vector3(.08f, .035f, .009f), new Vector3(-.01f, .018f, -.19f));
            benchTools.Rod(Finish.Steel, .018f, new Vector3(-.01f, .018f, -.19f), new Vector3(-.08f, .018f, -.23f));
            // A shallow open parts box, with loose fasteners sitting on its bottom.
            benchTools.Box(Finish.Teal, .52f, .018f, .10f, .32f, .036f, .24f);
            foreach (float x in new[] { .37f, .67f })
                benchTools.Box(Finish.Teal, x, .074f, .10f, .02f, .076f, .24f);
            foreach (float z in new[] { -.01f, .21f })
                benchTools.Box(Finish.Teal, .52f, .074f, z, .28f, .076f, .02f);
            benchTools.Rod(Finish.Steel, .035f, new Vector3(.44f, .055f, .06f), new Vector3(.55f, .055f, .08f));
            benchTools.Rod(Finish.Brass, .03f, new Vector3(.49f, .055f, .16f), new Vector3(.59f, .055f, .13f));
            benchTools.FinishGeometry();

            // Two compact objects leave the centre and most of the open rack tier visible.
            var rackTools = model.Mount("Shaft balcony rack tools", new Vector3(rackX, middle + 1.055f, rackZ), Quaternion.identity);
            rackTools.Box(Finish.Enamel, -.32f, .018f, 0, .33f, .036f, .25f);
            foreach (float x in new[] { -.475f, -.165f })
                rackTools.Box(Finish.Enamel, x, .064f, 0, .02f, .056f, .25f);
            foreach (float z in new[] { -.115f, .115f })
                rackTools.Box(Finish.Enamel, -.32f, .064f, z, .29f, .056f, .02f);
            rackTools.Rod(Finish.Steel, .032f, new Vector3(-.42f, .052f, -.04f), new Vector3(-.23f, .052f, .015f));
            rackTools.Rod(Finish.Brass, .031f, new Vector3(-.39f, .052f, .07f), new Vector3(-.28f, .052f, .04f));
            rackTools.Box(Finish.Graphite, -.02f, .085f, .01f, .18f, .17f, .21f);
            rackTools.Box(Finish.Teal, -.02f, .10f, .122f, .14f, .09f, .014f);
            rackTools.Rod(Finish.Edge, .04f, new Vector3(-.02f, .07f, .13f), new Vector3(-.02f, .07f, .145f));
            rackTools.FinishGeometry();
            model.FinishGeometry();

            void Rail(Vector3 start, Vector3 end)
            {
                var center = (start + end) / 2;
                var span = end - start;
                float width = Mathf.Abs(span.x) + .095f;
                float depth = Mathf.Abs(span.z) + .095f;
                model.Box(Finish.Amber, center.x, center.y + 1.034f, center.z, width, .064f, depth);
                model.Box(Finish.Edge, center.x, center.y + .54f, center.z, width, .042f, depth);
                model.Box(Finish.Graphite, center.x, center.y + .082f, center.z, width, .164f, depth);
                int bays = Mathf.CeilToInt(span.magnitude / 1.45f);
                for (int i = 0; i <= bays; i++)
                {
                    Vector3 post = Vector3.Lerp(start, end, i / (float)bays);
                    model.Box(Finish.Steel, post.x, post.y + .53f, post.z, .081f, 1.06f, .081f);
                }
            }

            void DeckEdge(float start, float end, float y, float edge, float inward)
            {
                model.Box(Finish.Teal, (start + end) / 2, y - .151f, edge + .009f, end - start, .185f, .038f);
                foreach (float x in new[] { start + .68f, (start + end) / 2, end - .68f })
                {
                    model.Box(Finish.Steel, x, y - .263f, (edge + inward) / 2, .13f, .19f, edge - inward);
                    model.Box(Finish.Edge, x, y - .112f, edge + .034f, .205f, .12f, .018f);
                    model.Box(Finish.WhiteTube, x, y - .121f, edge + .048f, .12f, .036f, .011f);
                }
            }
        }

        private void Cupboard(Batch model, IReadOnlyList<Collider> colliders)
        {
            Refinish(colliders, Finish.Teal, "supplies", "service-tool-locker-door");
            Refinish(colliders, Finish.Wood, "service-maintenance-table-top");
            Refinish(colliders, Finish.Graphite, "service-battery-service-light-housing");
            // Open top bay is behind the existing supplies frontage along the hand-carry route.
            model.Box(Finish.Steel, 7.186f, 1.999f, 1.62f, .073f, 1.61f, 2.43f);
            foreach (float z in new[] { .445f, 1.60f, 2.795f })
                model.Box(Finish.Edge, 6.71f, 1.990f, z, .97f, 1.58f, .058f);
            foreach (float y in new[] { 1.251f, 1.765f, 2.272f, 2.772f })
                model.Box(Finish.Wood, 6.70f, y, 1.617f, .97f, .061f, 2.42f);
            Case(model, 6.65f, 1.80f, 2.09f, .60f, .35f, .27f, -1, Finish.Graphite);
            Case(model, 6.63f, 1.29f, .90f, .57f, .53f, .31f, -1, Finish.Enamel);
            Case(model, 6.63f, 2.303f, 1.15f, .46f, .38f, .236f, 1, Finish.Amber);
            model.Box(Finish.Enamel, 5.795f, .62f, -.78f, .032f, 1.03f, 2.47f);
            foreach (float z in new[] { -1.57f, -.79f, -.013f })
            {
                model.Box(Finish.Steel, 5.768f, .64f, z, .032f, .051f, .16f);
                model.Box(Finish.Graphite, 5.772f, .855f, z, .023f, .051f, .23f);
            }
            WallPhone(model, 4.78f, 1.69f, -3.415f);
            model.Box(Finish.Paper, 5.774f, .96f, -1.32f, .009f, .22f, .18f, tilt: 12);
            model.Box(Finish.WhiteTube, 3.60f, 2.92f, -3.302f, .42f, .094f, .033f);
            foreach (float x in new[] { 3.417f, 3.51f, 3.60f, 3.69f, 3.783f })
                model.Box(Finish.Graphite, x, 2.921f, -3.274f, .018f, .15f, .041f);
            foreach (float y in new[] { 2.843f, 2.998f })
                model.Box(Finish.Graphite, 3.60f, y, -3.274f, .487f, .026f, .044f);
            model.FinishGeometry();
        }
    }
}

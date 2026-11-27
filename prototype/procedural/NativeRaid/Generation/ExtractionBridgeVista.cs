using System;
using System.Collections.Generic;
using System.IO;
using Contour4.Procedural.Catalog;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Contour4.NativeRaid.Runtime
{
    // The render set fits inside protected-shaft-volume. The original shell still
    // owns fall containment, room placement and all sight/physics queries.
    internal sealed class ExtractionBridgeVista
    {
        private const string RootName = "Ruptured shaft";
        private const string PanoramaResource = "Contour4.BridgePanorama.jpg";
        private const int PanelPixels = 256;
        private const int PanelCount = 3;
        private const float Bottom = -7.73f;
        private const float Top = 7.98f;
        private static readonly Color LampColor = new(1f, .74f, .41f);
        private readonly List<Mesh> meshes;
        private readonly Material concrete, spall, gunmetal, rust, distant, middle, footing, aperture, pilot, sky;
        private readonly Texture2D surface, atmosphere;

        internal ExtractionBridgeVista(Shader standard, List<Material> materials, List<Mesh> meshes)
        {
            this.meshes = meshes;
            Shader unlit = Shader.Find("Unlit/Color");
            Shader unlitTexture = Shader.Find("Unlit/Texture");
            surface = MakeTexture("Ruptured shaft aggregate", 128, 128, false, (x, y) =>
            {
                float u = x / 127f, v = y / 127f;
                float grain = Mathf.PerlinNoise(u * 76f + 2.7f, v * 76f + 8.6f);
                float cloud = Mathf.PerlinNoise(u * 5f + .81f, v * 7f + 9.1f);
                float edge = Mathf.Abs(Mathf.PerlinNoise(u * 22f + 11.8f, v * 22f + 2.5f) - .5f);
                float value = .66f + .16f * cloud + .14f * grain - .12f * Mathf.SmoothStep(.76f, 1f, 1f - edge * 15f);
                return new Color(value, value * .984f, value * .958f);
            });
            atmosphere = BuildPanorama();

            concrete = Lit("Stained aggregate", new Color(.37f, .337f, .309f), .14f, .33f);
            spall = Lit("Exposed aggregate", new Color(.47f, .425f, .374f), .09f, .29f);
            gunmetal = Lit("Oxidized steel", new Color(.27f, .244f, .221f), .24f, .25f);
            rust = Lit("Weathered reinforcement", new Color(.33f, .233f, .145f), .20f, .27f);
            footing = Flat("Shaft bed shadow", new Color(.026f, .025f, .040f));
            distant = Flat("Steel behind the haze", new Color(.071f, .077f, .112f));
            middle = Flat("Steel in the haze", new Color(.050f, .054f, .074f));
            aperture = Flat("Amber work light", new Color(1f, .77f, .47f) * 1.55f);
            pilot = Flat("Distant service indicator", new Color(.58f, .212f, .059f));
            sky = Material("Shaft horizon", unlitTexture ? unlitTexture : standard, Color.white);
            sky.mainTexture = atmosphere;
            if (!unlitTexture)
            {
                sky.color = Color.black;
                sky.EnableKeyword("_EMISSION");
                sky.SetTexture("_EmissionMap", atmosphere);
                sky.SetColor("_EmissionColor", Color.white);
            }

            Material Material(string name, Shader shader, Color color)
            {
                var material = new Material(shader) { name = name, color = color };
                materials.Add(material);
                return material;
            }

            Material Lit(string name, Color color, float gloss, float bounce)
            {
                var material = Material(name, standard, color);
                material.mainTexture = surface;
                material.SetFloat("_Glossiness", gloss);
                material.SetColor("_EmissionColor", color * bounce);
                material.EnableKeyword("_EMISSION");
                return material;
            }

            Material Flat(string name, Color color)
            {
                var material = Material(name, unlit ? unlit : standard, color);
                if (!unlit)
                {
                    material.SetColor("_EmissionColor", color);
                    material.EnableKeyword("_EMISSION");
                }
                return material;
            }
        }

        internal void Apply(Transform room, bool bent)
        {
            if (room.Find(RootName)) return;
            var root = RaidAssembly.Child(room, RootName);
            float end = bent ? 17f : 22.5f;
            float far = bent ? RaidExtractionBridge.BentFarWall : RaidExtractionBridge.StraightFarWall;
            float near = RaidExtractionBridge.NearWall;
            float side = far - near;
            foreach (string name in new[] { "near-wall", "far-wall", "east-wall", "shaft-wall-entry", "shaft-wall-near",
                "shaft-wall-far", "shaft-wall-back", "shaft-bottom" })
            {
                var shell = room.Find(name);
                if (shell && shell.TryGetComponent(out MeshRenderer renderer)) renderer.enabled = false;
            }

            var depth = new MeshBatch();
            depth.Quad(new Vector3(.02f, Bottom, near + .035f), new Vector3(end - .02f, Bottom, near + .035f),
                new Vector3(end - .02f, Top, near + .035f), new Vector3(.02f, Top, near + .035f), Vector3.forward, panel: 0);
            depth.Quad(new Vector3(end - .02f, Bottom, far - .035f), new Vector3(.02f, Bottom, far - .035f),
                new Vector3(.02f, Top, far - .035f), new Vector3(end - .02f, Top, far - .035f), Vector3.back, panel: 2);
            depth.Quad(new Vector3(end - .021f, Bottom, near + .034f), new Vector3(end - .021f, Bottom, far - .034f),
                new Vector3(end - .021f, Top, far - .034f), new Vector3(end - .021f, Top, near + .034f), Vector3.left, panel: 1);
            depth.Quad(new Vector3(.03f, Bottom, far - .036f), new Vector3(.03f, Bottom, near + .036f),
                new Vector3(.03f, -.015f, near + .036f), new Vector3(.03f, -.015f, far - .036f), Vector3.right,
                panel: 1, topV: .38f);
            Publish(root, depth, "Cool industrial panorama", sky);

            var bed = new MeshBatch();
            bed.Box(new Vector3(end / 2f, Bottom + .008f, (near + far) / 2f), new Vector3(end - .045f, .013f, side - .070f));
            Publish(root, bed, "Deep shaft shadow", footing);

            var rearSteel = new MeshBatch();
            var middleSteel = new MeshBatch();
            var girders = new MeshBatch();
            var rods = new MeshBatch();
            var ruin = new MeshBatch();
            var exposed = new MeshBatch();
            var indicators = new MeshBatch();
            SideFrames(near, 1, false);
            SideFrames(far, -1, bent);
            BackFrames();

            // Tall torn jambs frame the broken opening from the parking apron.
            Breach(3.35f, near + .85f, 1, false);
            if (!bent) Breach(3.8f, far - .85f, -1, false);
            Breach(bent ? 13.65f : 14.65f, near + .85f, 1, true);
            if (!bent) Breach(15.15f, far - .85f, -1, true);
            BuildFacades();
            BuildServiceStacks();

            // Retain only fragments of the ceiling between the two banks, clear of
            // the native quota's working volume even when its platform is raised.
            girders.Beam(new Vector3(.85f, 6.67f, near + .55f), new Vector3(8.5f, 6.31f, near + 1.05f), .29f, .43f);
            girders.Beam(new Vector3(2.75f, 6.28f, -4.7f), new Vector3(3.55f, 6.4f, bent ? 3.7f : 4.85f), .29f, .44f);
            girders.Beam(new Vector3(4.4f, 6.61f, near + .56f), new Vector3(8.45f, 5.05f, near + 3.2f), .26f, .29f);
            girders.Beam(new Vector3(5.15f, 7.04f, bent ? 3.22f : far - .62f),
                new Vector3(10.45f, 6.47f, bent ? 2.91f : far - 1.05f), .20f, .35f);
            girders.Beam(new Vector3(8.31f, 6.47f, bent ? 3.18f : far - .72f),
                new Vector3(10.38f, 5.08f, bent ? 2.40f : far - 2.62f), .19f, .23f);
            ruin.Slab(new Vector3(4.24f, 6.99f, near + 1.32f), new Vector3(3.64f, .69f, 2.2f),
                Quaternion.Euler(13f, -18f, -8f), 13);
            ruin.Slab(new Vector3(7.55f, 7.19f, bent ? 3.39f : far - 1.18f), new Vector3(3.08f, .56f, 1.53f),
                Quaternion.Euler(-13f, 17f, 8f), 5);
            rods.Beam(new Vector3(6.09f, 6.83f, -4.03f), new Vector3(6.49f, 6.07f, -3.68f), .027f, .027f);
            rods.Beam(new Vector3(5.83f, 6.74f, -3.91f), new Vector3(6.04f, 5.78f, -3.67f), .028f, .027f);

            Publish(root, rearSteel, "Distant gantries and service galleries", distant);
            Publish(root, middleSteel, "Broken secondary gantries", middle);
            Publish(root, girders, "Exposed load-bearing girders", gunmetal);
            Publish(root, rods, "Torn reinforcement", rust);
            Publish(root, ruin, "Fractured concrete structure", concrete);
            Publish(root, exposed, "Exposed fracture faces", spall);
            Publish(root, indicators, "Service lights below the crossing", pilot);

            foreach (Transform part in room)
                if (part.name.StartsWith(RaidExtractionBridge.FracturePrefix, StringComparison.Ordinal))
                    FinishPhysicalFragment(part);

            if (bent) BentDebris();
            else StraightDebris();

            var lampFrames = new MeshBatch();
            var lenses = new MeshBatch();
            Practical("Apron left work light", new Vector3(4.0f, .41f, -4.64f), new Vector3(-.21f, -.42f, 1f));
            Practical("Apron right work light", new Vector3(4.12f, .46f, 4.12f), new Vector3(-.25f, -.46f, -1f));
            if (bent)
            {
                Practical("Loading left work light", new Vector3(7.42f, .38f, 7.15f), new Vector3(.81f, -.43f, -.61f));
                Practical("Loading right work light", new Vector3(14.22f, .37f, 7.35f), new Vector3(-.84f, -.45f, -.55f));
            }
            else
            {
                Practical("Loading left work light", new Vector3(13.64f, .39f, -4.46f), new Vector3(-.4f, -.40f, 1f));
                Practical("Loading right work light", new Vector3(13.82f, .38f, 4.54f), new Vector3(-.41f, -.43f, -1f));
            }
            Publish(root, lampFrames, "Recessed work light housings", gunmetal);
            Publish(root, lenses, "Work light aperture lenses", aperture);
            Spotlight(root, "Cold shaft spill", new Vector3(bent ? 8.75f : 8.60f, 5.9f, -2.15f),
                new Vector3(.13f, -1f, .28f), new Color(.65f, .72f, 1f), 2.45f, 10.2f, 113f);
            Spotlight(root, "Diffuse shaft front", new Vector3(5.1f, 3.21f, bent ? 3.14f : 2.81f),
                new Vector3(1f, -.06f, -.39f), new Color(.69f, .76f, 1f), 4.1f, 11.35f, 108f);
            Spotlight(root, "Loading area ceiling bounce", bent ? new Vector3(8.88f, 3.98f, 5.06f) :
                new Vector3(12.08f, 4.02f, -1.84f), bent ? new Vector3(.32f, -.62f, 1f) :
                new Vector3(1f, -.59f, .23f), new Color(.92f, .88f, .81f), 4.5f, 10f, 103f);
            Spotlight(root, "Doorway spill", new Vector3(2.31f, 5.68f, 0f), new Vector3(.19f, -1f, -.08f),
                new Color(1f, .84f, .68f), 1.95f, 7.5f, 99f);

            void BuildFacades()
            {
                // Large fractured faces interrupt the framing, with gaps to see the galleries beyond.
                float face = bent ? 10.66f : 10.85f;
                Panel(face, near + .23f, 1, .54f, concrete: ruin,
                    new(-3.18f, .15f, 1.71f), new(-1.21f, .25f, 2.32f), new(-.33f, .28f, 1.79f),
                    new(.86f, .19f, 2.16f), new(1.54f, .12f, 1.58f), new(2.79f, .17f, 1.01f),
                    new(3.26f, .15f, 1.62f), new(4.51f, .23f, 1.46f), new(5.31f, .13f, 2.28f),
                    new(6.26f, .20f, 2.01f), new(7.66f, .10f, 2.85f));
                girders.Beam(new Vector3(face - .23f, -3.55f, -5.11f),
                    new Vector3(face - .23f, 7.50f, -5.11f), .15f, .28f);
                rods.Beam(new Vector3(face - .18f, 3.09f, -4.43f), new Vector3(face - .31f, 2.57f, -3.72f), .034f, .039f);
                rods.Beam(new Vector3(face + .16f, 5.17f, -3.69f), new Vector3(face - .16f, 4.46f, -3.36f), .039f, .041f);
                rods.Beam(new Vector3(face - .13f, 1.62f, -4.19f), new Vector3(face - .28f, 1.23f, -3.64f), .034f, .04f);

                if (bent)
                {
                    Panel(14.76f, -2.23f, 1, .54f, concrete: ruin,
                        new(2.86f, .48f, 1.28f), new(3.45f, .08f, 2.04f), new(4.02f, -.24f, 1.63f),
                        new(5.07f, -.41f, 2.54f), new(5.93f, -.73f, 3.08f), new(7.52f, -.79f, 3.83f));
                    Panel(16.09f, 4.67f, -1, .38f, concrete: rearSteel,
                        new(-3.25f, .06f, 1.45f), new(-2.29f, .11f, 2.35f), new(.51f, .20f, 2.55f),
                        new(1.52f, .30f, 1.38f), new(3.01f, .39f, 1.82f), new(4.66f, .16f, 1.13f),
                        new(6.19f, .26f, 1.74f), new(7.47f, .20f, 2.65f));
                    UpperGallery(12.65f, 16.16f, 1.32f, 3.43f, -1);
                    UpperGallery(10.92f, 14.33f, 3.51f, -4.37f, 1);
                }
                else
                {
                    Panel(14.97f, far - .18f, -1, .45f, concrete: ruin,
                        new(-2.23f, .09f, 1.95f), new(-.50f, .14f, 2.84f), new(.75f, .16f, 3.03f),
                        new(1.41f, .41f, 2.18f), new(2.42f, .34f, 2.44f), new(3.01f, .27f, 1.33f),
                        new(4.34f, .13f, 1.46f), new(5.48f, .23f, 1.96f), new(7.66f, .14f, 2.42f));
                    Panel(20.08f, -3.82f, 1, .37f, concrete: rearSteel,
                        new(-.25f, .19f, 1.83f), new(.96f, .33f, 1.76f), new(2.56f, .46f, 2.31f),
                        new(3.24f, .29f, 1.63f), new(4.58f, .08f, 1.88f), new(5.81f, .16f, 2.65f),
                        new(7.24f, .03f, 2.81f));
                    UpperGallery(11.66f, 16.80f, 3.17f, -4.41f, 1);
                    UpperGallery(16.70f, 21.60f, 1.68f, 4.57f, -1);
                }
            }

            void BuildServiceStacks()
            {
                // These silhouettes sit between the torn wall and the hazy outside plane.
                var nearRack = new Vector3(bent ? 7.85f : 8.65f, 1.4f, near + .82f);
                middleSteel.Slab(nearRack + new Vector3(.24f, 1.72f, .07f), new Vector3(2.58f, 4.40f, .57f),
                    Quaternion.Euler(1f, -9f, -14f), 4, vertical: true);
                rearSteel.Box(nearRack + new Vector3(1.81f, 2.68f, -.12f), new Vector3(.72f, 4.76f, .45f));
                rearSteel.Beam(nearRack + new Vector3(1.85f, 4.42f, -.18f), nearRack + new Vector3(3.41f, 4.47f, .02f),
                    .44f, .42f);
                middleSteel.Box(nearRack + new Vector3(-.41f, -.53f, .27f), new Vector3(2.92f, .19f, 1.10f));
                GalleryRail(nearRack.x - 1.7f, nearRack.x + .91f, nearRack.y - .44f, nearRack.z + .75f);
                indicators.Box(nearRack + new Vector3(.62f, -.22f, .56f), new Vector3(.079f, .061f, .042f));

                if (bent)
                {
                    var farRack = new Vector3(7.25f, 2.2f, far - 1.38f);
                    middleSteel.Slab(farRack + new Vector3(.26f, 2.67f, .35f), new Vector3(2.98f, 4.28f, .56f),
                        Quaternion.Euler(4f, 24f, 12f), 3, vertical: true);
                    rearSteel.Beam(farRack + new Vector3(-.82f, -2.31f, .32f), farRack + new Vector3(-.82f, 2.16f, .41f),
                        .44f, .46f);
                    rearSteel.Beam(farRack + new Vector3(-.82f, 1.68f, .38f),
                        farRack + new Vector3(1.25f, 1.52f, .09f), .33f, .31f);
                }
                else
                {
                    var farRack = new Vector3(21.23f, 3.27f, 2.86f);
                    rearSteel.Box(farRack, new Vector3(.65f, 3.67f, 1.39f));
                    rearSteel.Beam(farRack + new Vector3(.18f, 1.60f, -.44f), farRack + new Vector3(.19f, 3.51f, -.36f),
                        .32f, .32f);
                    middleSteel.Box(farRack + new Vector3(-.36f, -.84f, .07f), new Vector3(.27f, .97f, 1.16f));
                    for (int i = 0; i < 4; i++)
                        rearSteel.Box(farRack + new Vector3(-.51f, -.52f - i * .17f, .07f), new Vector3(.096f, .06f, .94f));
                    indicators.Box(farRack + new Vector3(-.58f, -1.52f, .11f), new Vector3(.04f, .056f, .076f));
                }
            }

            // Vertically stepped sections give the opening a readable, concave edge.
            // Each band has altitude, outer offset and inner offset from the wall's datum.
            void Panel(float x, float datum, float inward, float thickness, MeshBatch concrete, params Vector3[] bands)
            {
                Vector3 Point(int band, bool inner, bool back)
                {
                    Vector3 section = bands[band];
                    float jog = inner ? Mathf.Sin(band * 2.1f) * .12f : 0f;
                    return new Vector3(x + jog + (back ? thickness : 0f),
                        section.x - (back && inner ? .05f : 0f),
                        datum + inward * ((inner ? section.z : section.y) + (back && inner ? -.12f : 0f)));
                }
                MeshBatch lips = concrete == ruin ? exposed : concrete;
                for (int i = 1; i < bands.Length; i++)
                {
                    Vector3 a = Point(i - 1, false, false), b = Point(i - 1, true, false);
                    Vector3 c = Point(i, true, false), d = Point(i, false, false);
                    Vector3 e = Point(i - 1, false, true), f = Point(i - 1, true, true);
                    Vector3 g = Point(i, true, true), h = Point(i, false, true);
                    concrete.Quad(a, b, c, d, Vector3.left, mapped: true);
                    concrete.Quad(e, f, g, h, Vector3.right, mapped: true);
                    lips.Quad(b, f, g, c, inward * Vector3.forward, mapped: true);
                    concrete.Quad(a, e, h, d, -inward * Vector3.forward, mapped: true);
                    if (i == 1) lips.Quad(a, b, f, e, Vector3.down, mapped: true);
                    if (i == bands.Length - 1) lips.Quad(d, c, g, h, Vector3.up, mapped: true);
                }
            }

            void UpperGallery(float x0, float x1, float y, float z, float inward)
            {
                float rail = z + inward * .54f;
                middleSteel.Box(new Vector3((x0 + x1) / 2f, y, z), new Vector3(x1 - x0, .19f, 1.28f));
                middleSteel.Beam(new Vector3(x0 + .59f, y - .2f, z - inward * .35f),
                    new Vector3(x0 + 1.59f, y - 1.37f, rail), .15f, .17f);
                middleSteel.Beam(new Vector3(x1 - .29f, y - .2f, z - inward * .35f),
                    new Vector3(x1 - 1.29f, y - 1.43f, rail), .17f, .18f);
                GalleryRail(x0 + .56f, Mathf.Lerp(x0, x1, .74f), y + .069f, rail);
                rearSteel.Box(new Vector3(x1 - .57f, y + .60f, z - inward * .25f), new Vector3(.63f, 1.06f, .63f));
                indicators.Box(new Vector3(x1 - .57f, y + .64f, z + inward * .077f), new Vector3(.074f, .046f, .024f));
                rods.Beam(new Vector3(x0, y - .068f, rail), new Vector3(x0 + .82f, y - .092f, rail), .081f, .064f);
            }

            void SideFrames(float edge, float inward, bool longBank)
            {
                float backZ = edge + inward * .68f;
                float postZ = edge + inward * (longBank ? 2.1f : 1.55f);
                float[] bays = bent ? new[] { 4.8f, 7.85f, 11.54f, 15.21f } :
                    new[] { 5.08f, 8.72f, 12.51f, 16.40f, 20.82f };
                for (int i = 0; i < bays.Length; i++)
                {
                    float x = bays[i];
                    float high = ((i + (inward < 0 ? 1 : 0)) % 3) switch
                    {
                        0 => 7.41f, 1 => 3.86f, _ => 5.88f
                    };
                    rearSteel.Beam(new Vector3(x, Bottom + .08f, backZ), new Vector3(x + .25f, high, backZ), .34f, .22f);
                    rearSteel.Box(new Vector3(x, -3.63f + .19f * (i % 2), backZ + inward * .17f), new Vector3(.63f, .35f, .57f));
                    if (i + 1 >= bays.Length) continue;

                    float next = bays[i + 1];
                    float level = -2.9f - .7f * (i % 3);
                    rearSteel.Beam(new Vector3(x, level, backZ), new Vector3(next, level - .13f, backZ), .29f, .23f);
                    rearSteel.Beam(new Vector3(x, level, backZ), new Vector3(next, level - 2.92f, backZ), .11f, .11f);
                    if (i % 2 == 0)
                        rearSteel.Beam(new Vector3(x, 4.1f, backZ), new Vector3(next, 4.5f, backZ), .19f, .26f);
                    if ((i + (inward < 0 ? 1 : 0)) % 3 == 1)
                    {
                        rearSteel.Box(new Vector3(x + .96f, -1.57f, backZ), new Vector3(1.62f, 2.4f, .47f));
                        rearSteel.Box(new Vector3(x + 1.62f, -.16f, backZ), new Vector3(.78f, .58f, .46f));
                    }

                    float pierX = x + .39f;
                    float pierTop = i % 2 == 0 ? .69f : -.92f;
                    middleSteel.Beam(new Vector3(pierX, Bottom + .12f, postZ),
                        new Vector3(pierX + .07f, pierTop, postZ), .41f, .27f);
                    float foot = level - .41f;
                    middleSteel.Box(new Vector3((pierX + next) / 2f, foot, postZ - inward * .38f),
                        new Vector3(next - pierX, .20f, 1.16f));
                    middleSteel.Beam(new Vector3(pierX, foot, postZ), new Vector3(next - .41f, foot - 1.71f, postZ), .16f, .14f);
                    GalleryRail(pierX + .21f, next - .15f, foot + .11f, postZ + inward * .17f);
                    if (i % 2 == 0) indicators.Box(new Vector3(pierX + .38f, foot + .47f, postZ + inward * .24f),
                        new Vector3(.119f, .046f, .05f));
                    if (i % 2 != 0)
                        middleSteel.Box(new Vector3(next - .82f, foot + .69f, postZ - inward * .31f),
                            new Vector3(.69f, 1.22f, .53f));
                }
            }

            void GalleryRail(float x0, float x1, float y, float z)
            {
                middleSteel.Beam(new Vector3(x0, y + .73f, z), new Vector3(x1, y + .73f, z), .047f, .047f);
                for (int i = 0; i <= 2; i++)
                {
                    float x = Mathf.Lerp(x0, x1, i * .5f);
                    middleSteel.Beam(new Vector3(x, y, z), new Vector3(x, y + .72f, z), .049f, .049f);
                }
            }

            void BackFrames()
            {
                float x = end - .46f;
                for (float z = near + 1.3f; z < far - .8f; z += 3.6f)
                {
                    rearSteel.Beam(new Vector3(x, Bottom + .04f, z), new Vector3(x - .06f, 7.83f, z), .29f, .48f);
                    float next = Mathf.Min(z + 3.56f, far - .47f);
                    rearSteel.Beam(new Vector3(x, 5.66f, z), new Vector3(x, 5.83f, next), .24f, .33f);
                    rearSteel.Beam(new Vector3(x, -1.58f, z), new Vector3(x, -1.58f, next), .32f, .28f);
                    rearSteel.Beam(new Vector3(x, -5.68f, z), new Vector3(x, -1.62f, next), .15f, .18f);
                    middleSteel.Box(new Vector3(x - .79f, -3.62f, z + .54f), new Vector3(1.46f, 2.1f, 1.16f));
                    indicators.Box(new Vector3(x - 1.54f, -3.05f, z + .46f), new Vector3(.04f, .055f, .12f));
                }
            }

            void Breach(float x, float z, float inward, bool distantBank)
            {
                float height = distantBank ? 7.45f : 7.2f;
                ruin.Slab(new Vector3(x - .58f, 3.54f, z), new Vector3(2.18f, height, .78f),
                    Quaternion.Euler(3.2f * inward, -8f * inward, distantBank ? 4.5f : -3.5f), distantBank ? 11 : 4,
                    vertical: true);
                ruin.Slab(new Vector3(x + .69f, 5.97f, z + inward * .12f), new Vector3(2.2f, 2.75f, .76f),
                    Quaternion.Euler(-5f * inward, 6f * inward, -11f), distantBank ? 9 : 3, vertical: true);
                if (!distantBank)
                    ruin.Slab(new Vector3(x + .63f, .65f, z + inward * .1f), new Vector3(2.88f, 1.78f, .86f),
                        Quaternion.Euler(5f * inward, 7f * inward, -21f), 8, vertical: true);
                girders.Beam(new Vector3(x - 1.02f, -1.36f, z + inward * .45f),
                    new Vector3(x - 1.1f, 7.68f, z + inward * .49f), .26f, .33f);
                rods.Beam(new Vector3(x + 1.03f, 4.89f, z + inward * .16f),
                    new Vector3(x + 1.72f, 4.49f, z + inward * .43f), .033f, .033f);
                rods.Beam(new Vector3(x + 1.72f, 4.49f, z + inward * .43f),
                    new Vector3(x + 2.07f, 3.93f, z + inward * .57f), .033f, .033f);
                rods.Beam(new Vector3(x + .65f, 5.36f, z + inward * .27f),
                    new Vector3(x + 1.61f, 5.23f, z + inward * .58f), .027f, .027f);
            }

            void StraightDebris()
            {
                Fragment(6.51f, 1.64f, -3.18f, 1.80f, .57f, .96f, 22f, -22f, -13f, 3);
                Fragment(8.70f, 3.51f, 2.49f, 1.27f, .61f, .84f, -19f, 18f, 25f, 7);
                Fragment(10.39f, 1.24f, 3.29f, 2.09f, .53f, .93f, 14f, 6f, -18f, 11);
                Fragment(6.32f, -2.51f, 3.15f, 2.13f, .62f, 1.32f, -14f, 27f, 17f, 4);
                Fragment(8.08f, -1.83f, -2.38f, 1.53f, .49f, 1.03f, 15f, -13f, 22f, 8);
                Fragment(10.77f, -3.49f, 3.75f, 1.94f, .56f, 1.22f, -15f, 42f, 14f, 13);
                Fragment(11.57f, -1.06f, -3.64f, .83f, .43f, .75f, 9f, 31f, -25f, 5);
                Fragment(6.93f, -3.91f, -.98f, .77f, .35f, .58f, 15f, 31f, -13f, 10);
                Fragment(14.13f, 2.87f, -4.12f, .73f, .43f, .58f, 13f, -13f, 23f, 1);
                Fragment(10.31f, 4.21f, -2.38f, .53f, .28f, .43f, -20f, 18f, 14f, 6);
            }

            void BentDebris()
            {
                Fragment(6.86f, 1.51f, -2.68f, 1.64f, .64f, .97f, 18f, -29f, 17f, 1);
                Fragment(9.16f, 2.64f, 2.44f, 1.55f, .50f, 1.07f, -10f, 29f, 13f, 7);
                Fragment(12.92f, 1.06f, .18f, 2.02f, .79f, 1.11f, -16f, 22f, -22f, 8);
                Fragment(14.36f, 3.47f, 3.69f, 1.58f, .72f, .81f, 11f, -30f, 22f, 9);
                Fragment(7.42f, -2.45f, 2.94f, 2.29f, .62f, 1.04f, 17f, 31f, -15f, 2);
                Fragment(13.67f, -1.46f, 3.60f, 1.84f, .49f, 1.04f, -23f, -29f, -23f, 4);
                Fragment(13.63f, -3.43f, -.66f, 1.95f, .60f, 1.03f, 12f, 27f, -14f, 13);
                Fragment(9.32f, -2.81f, -2.28f, 1.52f, .64f, .99f, 21f, 11f, 19f, 12);
                Fragment(14.62f, -3.82f, 4.78f, .86f, .37f, .60f, -10f, -11f, 27f, 11);
                Fragment(9.82f, 4.69f, -1.67f, .74f, .41f, .59f, -19f, 12f, 18f, 6);
            }

            void Fragment(float x, float y, float z, float width, float height, float depth, float pitch, float yaw, float roll, int kind)
            {
                var shape = new MeshBatch();
                shape.Slab(Vector3.zero, new Vector3(width, height, depth), Quaternion.identity, kind);
                var fragment = Publish(root, shape, $"Drifting slab {kind:00}", kind % 3 == 0 ? spall : concrete);
                fragment.localPosition = new Vector3(x, y, z);
                fragment.localRotation = Quaternion.Euler(pitch, yaw, roll);
                fragment.gameObject.AddComponent<ExtractionSlabDrift>().Initialize(kind);
            }

            void Practical(string name, Vector3 at, Vector3 direction)
            {
                Vector3 facing = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
                Quaternion mount = Quaternion.LookRotation(facing, Vector3.up);
                lampFrames.Box(at - Vector3.up * .21f, new Vector3(.67f, .41f, .27f), mount);
                lampFrames.Box(at, new Vector3(.72f, .264f, .33f), mount);
                lenses.Box(at + facing * .170f, new Vector3(.53f, .093f, .012f), mount);
                Spotlight(root, name, at + facing * .25f + Vector3.up * .06f, direction, LampColor, 3.2f, 6.2f, 107f);
            }
        }

        private void FinishPhysicalFragment(Transform part)
        {
            // Share the original geometry with the collider. Only the texture coordinates change.
            var mesh = part.GetComponent<MeshFilter>().sharedMesh;
            var points = mesh.vertices;
            var uv = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 p = points[i];
                uv[i] = new Vector2(p.x + p.y * .36f, p.z + p.y * .61f) * .67f;
            }
            mesh.uv = uv;
            part.GetComponent<MeshRenderer>().sharedMaterial = concrete;
        }

        private Transform Publish(Transform root, MeshBatch batch, string name, Material material)
        {
            var child = RaidAssembly.Child(root, name);
            var mesh = batch.Finish(name);
            meshes.Add(mesh);
            child.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return child;
        }

        private static void Spotlight(Transform root, string name, Vector3 position, Vector3 direction, Color color,
            float intensity, float range, float angle)
        {
            var child = RaidAssembly.Child(root, name);
            child.SetLocalPositionAndRotation(position, Quaternion.LookRotation(direction, Vector3.up));
            var light = child.gameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.spotAngle = angle;
            light.innerSpotAngle = angle * .59f;
            light.shadows = LightShadows.None;
        }

        private static Texture2D BuildPanorama()
        {
            byte[] jpeg;
            using (Stream stream = typeof(ExtractionBridgeVista).Assembly.GetManifestResourceStream(PanoramaResource))
            {
                if (stream != null)
                {
                    using var bytes = new MemoryStream();
                    stream.CopyTo(bytes);
                    jpeg = bytes.ToArray();
                }
                else
                {
                    // Unity's source-based editor checks stage the canonical JPEG as a TextAsset.
                    var resource = Resources.Load<TextAsset>("Contour4/BridgePanorama");
                    jpeg = resource ? resource.bytes : throw new InvalidOperationException($"Missing {PanoramaResource}.");
                }
            }

            var matte = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (!ImageConversion.LoadImage(matte, jpeg))
                    throw new InvalidOperationException($"Could not decode {PanoramaResource}.");

                return MakeTexture("Ruptured shaft industrial panorama", PanelCount * PanelPixels, PanelPixels,
                    true, (x, y) =>
                {
                    int panel = x / PanelPixels;
                    float u = (x % PanelPixels) / (PanelPixels - 1f), v = y / (PanelPixels - 1f);
                    float sampleU = panel switch
                    {
                        0 => Mathf.Lerp(.02f, .62f, 1f - u),
                        1 => Mathf.Lerp(.23f, .87f, 1f - u),
                        _ => Mathf.Lerp(.41f, .99f, 1f - u)
                    };
                    Color source = matte.GetPixelBilinear(sampleU, .045f + v * .93f);
                    float ground = Mathf.SmoothStep(.21f, .96f, Mathf.InverseLerp(0f, .54f, v));
                    float ceiling = Mathf.Lerp(1f, .27f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.72f, 1f, v)));
                    float edge = Mathf.Lerp(.28f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) * 5.7f));
                    float fog = Mathf.Lerp(.59f, .88f, Mathf.PerlinNoise(sampleU * 6.1f + 11.2f, v * 4.9f + 1.5f));
                    Color haze = new Color(.061f, .072f, .11f);
                    Color colored = new Color(source.r * .54f, source.g * .54f, source.b * .68f);
                    return Color.Lerp(haze, colored, fog) * (ground * ceiling * edge) + haze * .18f;
                });
            }
            finally
            {
                DestroyTexture(matte);
            }
        }

        private static Texture2D MakeTexture(string name, int width, int height, bool clamp, Func<int, int, Color> pixel)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGB24, true)
            {
                name = name, wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear
            };
            var colors = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    colors[y * width + x] = pixel(x, y);
            texture.SetPixels32(colors);
            texture.Apply(true, true);
            return texture;
        }

        internal void Release()
        {
            DestroyTexture(surface);
            DestroyTexture(atmosphere);
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (Application.isPlaying) Object.Destroy(texture);
            else Object.DestroyImmediate(texture);
        }

        private sealed class MeshBatch
        {
            private static readonly Vector2[] BreakLine =
            {
                new(-.53f, -.18f), new(-.33f, -.43f), new(.13f, -.48f), new(.47f, -.31f),
                new(.54f, .12f), new(.19f, .48f), new(-.16f, .53f), new(-.44f, .28f)
            };
            private readonly List<Vector3> vertices = new();
            private readonly List<Vector2> uv = new();
            private readonly List<int> triangles = new();

            internal void Box(Vector3 center, Vector3 size, Quaternion? rotation = null)
            {
                Quaternion q = rotation ?? Quaternion.identity;
                Vector3 x = q * (Vector3.right * size.x / 2f), y = q * (Vector3.up * size.y / 2f);
                Vector3 z = q * (Vector3.forward * size.z / 2f);
                Face(x, y, z); Face(-x, y, z);
                Face(y, x, z); Face(-y, x, z);
                Face(z, x, y); Face(-z, x, y);

                void Face(Vector3 normal, Vector3 u, Vector3 v) =>
                    Quad(center + normal - u - v, center + normal + u - v, center + normal + u + v,
                        center + normal - u + v, normal, mapped: true);
            }

            internal void Beam(Vector3 a, Vector3 b, float width, float height)
            {
                Vector3 axis = b - a;
                Vector3 up = Mathf.Abs(Vector3.Dot(axis.normalized, Vector3.up)) > .96f ? Vector3.forward : Vector3.up;
                Box((a + b) / 2f, new Vector3(width, height, axis.magnitude), Quaternion.LookRotation(axis, up));
            }

            internal void Slab(Vector3 center, Vector3 size, Quaternion rotation, int kind, bool vertical = false)
            {
                var upper = new Vector3[BreakLine.Length];
                var lower = new Vector3[BreakLine.Length];
                var random = new System.Random(kind);
                for (int i = 0; i < BreakLine.Length; i++)
                {
                    Vector2 p = BreakLine[(i + kind) % BreakLine.Length];
                    float u = p.x * size.x * (.9f + .19f * (float)random.NextDouble());
                    float v = p.y * (vertical ? size.y : size.z) * (.86f + .22f * (float)random.NextDouble());
                    float ridge = .26f + .23f * (float)random.NextDouble();
                    float baseTaper = .53f + .42f * (float)random.NextDouble();
                    upper[i] = Point(u, v, ridge);
                    lower[i] = Point(u * baseTaper + size.x * .08f, v * baseTaper, -.35f - .23f * (float)random.NextDouble());
                }
                Vector3 outwards = rotation * (vertical ? Vector3.forward : Vector3.up);
                Vector3 crown = Point(size.x * .07f, -(vertical ? size.y : size.z) * .11f, .53f);
                Vector3 underside = Point(size.x * .11f, 0f, -.63f);
                for (int i = 0; i < BreakLine.Length; i++)
                {
                    int next = (i + 1) % BreakLine.Length;
                    Triangle(upper[i], upper[next], crown, outwards);
                    Triangle(lower[i], lower[next], underside, -outwards);
                    Vector3 radial = Vector3.ProjectOnPlane((upper[i] + upper[next]) * .5f - center, outwards);
                    Triangle(upper[i], lower[i], upper[next], radial);
                    Triangle(lower[i], lower[next], upper[next], radial);
                }

                Vector3 Point(float u, float v, float thickness) =>
                    center + rotation * (vertical ? new Vector3(u, v, thickness * size.z) :
                        new Vector3(u, thickness * size.y, v));
            }

            internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, bool mapped = false,
                int panel = -1, float topV = 1f)
            {
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                if (mapped)
                {
                    uv.Add(Project(a, outward)); uv.Add(Project(b, outward));
                    uv.Add(Project(c, outward)); uv.Add(Project(d, outward));
                }
                else if (panel >= 0)
                {
                    float edge = .5f / PanelPixels;
                    float left = (panel + edge) / PanelCount;
                    float right = (panel + 1f - edge) / PanelCount;
                    uv.Add(new Vector2(left, 0f)); uv.Add(new Vector2(right, 0f));
                    uv.Add(new Vector2(right, topV)); uv.Add(new Vector2(left, topV));
                }
                else
                {
                    uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.one); uv.Add(Vector2.up);
                }
                Face(start, start + 1, start + 2, outward);
                Face(start, start + 2, start + 3, outward);
            }

            private void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                Vector3 normal = Vector3.Cross(b - a, c - a);
                uv.Add(Project(a, normal)); uv.Add(Project(b, normal)); uv.Add(Project(c, normal));
                Face(start, start + 1, start + 2, outward);
            }

            private void Face(int a, int b, int c, Vector3 outward)
            {
                bool direct = Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), outward) >= 0;
                triangles.Add(a); triangles.Add(direct ? b : c); triangles.Add(direct ? c : b);
            }

            private static Vector2 Project(Vector3 v, Vector3 normal)
            {
                float x = Mathf.Abs(normal.x), y = Mathf.Abs(normal.y), z = Mathf.Abs(normal.z);
                if (y >= x && y >= z) return new Vector2(v.x, v.z) * .67f;
                return (x >= z ? new Vector2(v.z, v.y) : new Vector2(v.x, v.y)) * .67f;
            }

            internal Mesh Finish(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }

    internal sealed class ExtractionSlabDrift : MonoBehaviour
    {
        private Vector3 start;
        private Quaternion orientation;
        private float phase;

        internal void Initialize(int variation)
        {
            start = transform.localPosition;
            orientation = transform.localRotation;
            phase = variation * 1.67f;
        }

        private void Update()
        {
            float motion = Time.time * .42f + phase;
            transform.SetLocalPositionAndRotation(start + new Vector3(.054f * Mathf.Sin(motion * .75f),
                .097f * Mathf.Sin(motion), .059f * Mathf.Cos(motion * .83f)),
                orientation * Quaternion.Euler(2.6f * Mathf.Sin(.64f * motion), 2.0f * Mathf.Cos(.78f * motion),
                    2.3f * Mathf.Sin(.78f * motion)));
        }
    }
}

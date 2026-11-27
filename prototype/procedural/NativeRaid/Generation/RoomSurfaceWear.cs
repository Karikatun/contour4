using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural.Catalog;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class RoomSurfaceWear
    {
        internal const string RootPath = "Assets/Contour4/Rooms/";
        internal const string ShaderPath = RootPath + "RoomSurfaceWear.shader";
        internal const string FloorPath = RootPath + "Materials/SurfaceTextures/concrete_floor_worn_001_diff_1k.jpg";
        internal const string PlasterPath = RootPath + "Materials/SurfaceTextures/painted_plaster_wall_diff_1k.jpg";
        internal const string EnamelPath = RootPath + "Materials/SurfaceTextures/green_metal_rust_diff_1k.jpg";
        internal const string FinishName = "Contour4/Room surface wear";
        internal const string WebName = "Protected corner cobweb";

        private enum Finish { Keep, Concrete, Plaster, Enamel, Wood, WoodEdge, Polymer, Metal, PaintedWall = 8 }

        private static Shader shader;
        private static Texture2D concrete, plaster, enamel;
        private static readonly int WearFrame = Shader.PropertyToID("_WearFrame");
        private static readonly int WearInverseFrame = Shader.PropertyToID("_WearInverseFrame");
        private static readonly int WearMin = Shader.PropertyToID("_WearMin");
        private static readonly int WearMax = Shader.PropertyToID("_WearMax");
        private static readonly int WearProfile = Shader.PropertyToID("_WearProfile");
        private static readonly int Oil = Shader.PropertyToID("_WearOil");
        private static readonly int Trace = Shader.PropertyToID("_WearTrace");
        private static readonly int WallChipA = Shader.PropertyToID("_WallChipA");
        private static readonly int WallChipB = Shader.PropertyToID("_WallChipB");
        private static readonly int WallChipC = Shader.PropertyToID("_WallChipC");
        private readonly Dictionary<(Material, Finish), Material> materials = new();
        private readonly List<Mesh> webs = new();
        private Material webMaterial, paintedWall;

        internal static void Configure(Shader surfaceShader, Texture2D floor, Texture2D wall, Texture2D paintedMetal)
        {
            shader = surfaceShader;
            concrete = floor;
            plaster = wall;
            enamel = paintedMetal;
        }

        internal void Apply(Transform root, string room, IEnumerable<Block> geometry = null, GrayModule module = null,
            Func<MeshRenderer, Bounds, PaintedRoomWalls.Projection?> authoredWall = null)
        {
            if (!shader) return;
            // A freight fold can use the same sill ID at both ends. Only use the catalog
            // role when every block with that name agrees; material/name rules cover the rest.
            var roles = geometry?.GroupBy(part => part.Id, StringComparer.Ordinal)
                .Select(parts => (parts.Key, Roles: parts.Select(part => part.Role).Distinct().ToArray()))
                .Where(entry => entry.Roles.Length == 1).ToDictionary(entry => entry.Key, entry => entry.Roles[0]);
            var (use, service) = room switch
            {
                "shaft-plant" or "maintenance-trench" or "service-cupboard" or "service-garage" => (1f, 1f),
                "sample-receiving" or "observation-lab" or "pressure-dock" => (.48f, 0f),
                "observation-lab-broken-floor" => (.88f, .35f),
                _ => (.71f, .23f)
            };
            Vector4 oil = room switch
            {
                "shaft-plant" => new(11.15f, 0, 2.92f, .88f),
                "service-garage" => new(5.28f, .012f, -6.14f, .63f),
                _ => Vector4.zero
            };
            // A lighter outline left by a relocated small wall cabinet.
            Vector4 trace = room switch
            {
                "service-cupboard" => new(2.30f, 1.72f, 3.50f, .62f),
                "service-garage" => new(7.25f, 1.48f, -7.46f, .64f),
                _ => Vector4.zero
            };
            var frame = root.worldToLocalMatrix;
            var block = new MaterialPropertyBlock();
            var walls = new PaintedRoomWalls(geometry, module);
            bool referenceRoom = room is FreightServiceRoom.SampleReceiving or FreightServiceRoom.InstrumentService;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled || renderer.name == WebName ||
                    renderer.name.EndsWith(" / " + BrokenEquipment.ExposedParts, StringComparison.Ordinal) ||
                    !renderer.TryGetComponent(out MeshFilter filter) || !filter.sharedMesh)
                    continue;
                string name = renderer.name.ToLowerInvariant();
                bool furnitureBatch = renderer.transform.parent && renderer.transform.parent.name == "Static furniture render meshes";
                BlockRole? role = roles != null && roles.TryGetValue(renderer.name, out var partRole) ? partRole : null;
                Material[] surfaces = renderer.sharedMaterials;
                bool changed = false;
                // A floating-origin shift cannot move the wear because this matrix is in room space.
                Matrix4x4 relative = frame * renderer.localToWorldMatrix;
                Bounds bounds = InFrame(filter.sharedMesh.bounds, relative);
                bool wall = walls.TryGet(renderer.name, out var projection);
                if (!wall && authoredWall?.Invoke(renderer, bounds) is { } authoredProjection)
                {
                    projection = authoredProjection;
                    wall = true;
                }
                string referenceSide = wall && referenceRoom && renderer.transform.parent == root
                    ? PaintedWallSide(name, role) : null;
                bool painted = false;
                for (int index = 0; index < surfaces.Length; index++)
                {
                    Material original = surfaces[index];
                    if (wall && PaintableWallMaterial(original))
                    {
                        surfaces[index] = PaintedWallMaterial();
                        painted = true;
                        changed = true;
                        continue;
                    }
                    Finish finish = Classify(original, name, role, furnitureBatch);
                    if (finish == Finish.Keep) continue;
                    var key = (original, finish);
                    if (!materials.TryGetValue(key, out Material material))
                    {
                        material = new Material(original) { shader = shader, name = original.name + " (Contour-4 wear)" };
                        material.SetFloat("_WearKind", (float)finish);
                        Texture2D texture = finish switch
                        {
                            Finish.Concrete => concrete,
                            Finish.Plaster => plaster,
                            Finish.Enamel => enamel,
                            _ => null
                        };
                        if (texture) material.SetTexture("_WearMap", texture);
                        materials.Add(key, material);
                    }
                    surfaces[index] = material;
                    changed = true;
                }
                if (!changed) continue;
                renderer.sharedMaterials = surfaces;
                renderer.GetPropertyBlock(block);
                block.SetMatrix(WearFrame, relative);
                block.SetMatrix(WearInverseFrame, relative.inverse);
                block.SetVector(WearMin, bounds.min);
                block.SetVector(WearMax, bounds.max);
                float lengthwise = bounds.size.x >= bounds.size.z ? 1f : -1f;
                block.SetVector(WearProfile, new Vector4(use, service, StablePhase(room), lengthwise));
                block.SetVector(Oil, oil);
                block.SetVector(Trace, trace);
                if (painted && referenceSide != null)
                {
                    bool alongX = !referenceSide.StartsWith("west-wall", StringComparison.Ordinal) &&
                        !referenceSide.StartsWith("east-wall", StringComparison.Ordinal);
                    block.SetVector(WearProfile, new Vector4(use, service, StablePhase(room), alongX ? 1 : -1));
                    PaintDamage(block, referenceSide, room == FreightServiceRoom.InstrumentService);
                    HideLegacyWallCladding(renderer.transform);
                }
                else if (painted)
                {
                    projection.Apply(block);
                    // Flat wall projection stores distance along the wall in p.x,
                    // regardless of its original world axis. The rotary facade
                    // runs along Z, so the unprojected bounds-based choice would
                    // sample its plaster at constant depth across the whole face.
                    if (room == RaidQuotaRotaryRoom.Id)
                        block.SetVector(WearProfile, new Vector4(use, service, StablePhase(room), 1));
                    HideLegacyWallCladding(renderer.transform);
                }
                renderer.SetPropertyBlock(block);
            }

            switch (room)
            {
                case "shaft-plant":
                    InstallWeb(root, new(.255f, 4.80f, 7.245f), Vector3.right, Vector3.back, .54f);
                    break;
                case "service-cupboard":
                    InstallWeb(root, new(7.245f, 2.53f, 3.495f), Vector3.left, Vector3.back, .41f);
                    break;
                case "service-garage":
                    InstallWeb(root, new(7.235f, 2.68f, -9.735f), Vector3.left, Vector3.forward, .47f);
                    break;
            }
        }

        private static Finish Classify(Material material, string name, BlockRole? role, bool furnitureBatch)
        {
            if (!material || material.shader.name != "Standard" || material.renderQueue >= 2450 ||
                material.color.a < .99f || material.IsKeywordEnabled("_EMISSION") ||
                material.IsKeywordEnabled("_NORMALMAP") || material.IsKeywordEnabled("_PARALLAXMAP") ||
                material.IsKeywordEnabled("_METALLICGLOSSMAP") || material.IsKeywordEnabled("_DETAIL_MULX2") ||
                (material.mainTexture && material.mainTexture.width > 4))
                return Finish.Keep;
            string source = material.name.ToLowerInvariant();
            if (name == RaidInteriorDetails.ObjectName.ToLowerInvariant())
                return source switch
                {
                    "contour-4 pressure door enamel" => Finish.Enamel,
                    "contour-4 olive wall paint" => Finish.Plaster,
                    "contour-4 gasket rubber" or "contour-4 service conduit" => Finish.Polymer,
                    "contour-4 porcelain finish" or "contour-4 ochre service tile" => Finish.Polymer,
                    _ => Finish.Keep
                };
            if (Has(source, "lens", "glass", "glazing", "warning", "amber", "aquamarine", "stencil", "diffuser", "numbering") ||
                source is "contour-4 interior paper" or "contour-4 interior red" or "contour-4 interior circuit" or
                "contour-4 terminal legend" or "contour-4 dusty silk")
                return Finish.Keep;
            if (name == "mobius-ribbon")
                return Finish.Metal;
            if (name is "ribbon-landing" or "ceiling-ribbon-landing")
                return Finish.Concrete;
            bool cover = Has(name, "housing", "casing", "bezel", "splash screen", "screen return", "side screen",
                "-bay-screen", "parking-screen", "lens carriage", "lens cap", "lens plinth", "portal-wall", "portal-door-plug");
            bool instrumentFace = name is "meter" or "lens" or "calibration stand dial" ||
                name.EndsWith("-meter-face", StringComparison.Ordinal) || name.EndsWith(" meter face", StringComparison.Ordinal) ||
                name.EndsWith("-lens", StringComparison.Ordinal) || name.EndsWith(" lens", StringComparison.Ordinal);
            if (!cover && (instrumentFace || Has(name, "glass", "signal", "status", "marking", "guide strip",
                "lane edge", "emitter", "screen", "display", "hazard", "aisle strip", "decal", "label", "legend",
                "placard", "marker", "nameplate", "numeral", "indicator", "readout", "badge", "scale graduation",
                "dart", "instrument face", "control fascia", "ribbon station", "portal surface")))
                return Finish.Keep;
            if (source == "contour-4 interior woodedge")
                return Finish.WoodEdge;
            if (source is "contour-4 interior wood" or "v2_wood" or "furniture_plywood" ||
                source.StartsWith("contour-4 bridge timber ", StringComparison.Ordinal))
                return Finish.Wood;
            if (source is "v2_floor" or "institute-floor" or "deck" or "painted-deck" || role == BlockRole.Floor ||
                Has(name, "apron surface", "__floor", "__walkable_deck", "roof deck", "upper gallery deck", "collar floor"))
                return Finish.Concrete;
            if (Has(name, "handrail", "railings", "guard upright", "guard top rail", "guard sill", "upper-guard-", "middle-guard-",
                    "__bridge_rail", "freight-pit-rail", "stair-landing-rail", "coil retaining band", "transfer switchboard rail",
                    "truck ramp toe plate") ||
                source is "contour-4 fitout trim metal" or "transition" or "v2_brass" or "institute-brass" or
                    "contour-4 interior brass" or "contour-4 interior copper" or "furniture_edge" or "fitout-steeltrim")
                return Finish.Metal;
            if (source is "v2_bakelite" or "institute-bakelite" or "contour-4 interior graphite" or
                    "contour-4 interior cableblue" or "contour-4 interior cablegreen" ||
                furnitureBatch && source == "v2_ivory" ||
                Has(name, "rubber", "gasket", "insulation", "cable jacket", "rubber sleeve"))
                return Finish.Polymer;
            if (source is "v2_plaster" or "institute-plaster" or "shell" or "ceiling-liner" ||
                role == BlockRole.Shell && Has(name, "wall", "ceiling", "roof") ||
                Has(name, "outer wall", "collar jamb", "bay ceiling"))
                return Finish.Plaster;
            if (Has(name, "housing", "cabinet", "analyzer", "console", "cupboard", "drawer", "threshold",
                "jamb", "header", "locker", "rack", "counter", "bench", "table", "chair", "apparatus", "switchgear",
                "duct", "backboard", "gantry", "pressure control", "frame", "shutter", "skirt", "bollard",
                "kick plate", "control face", "access cover", "service latch", "cooling vent", "pump",
                "partition", "panel", "casing", "-bay-screen", "parking-screen", "supply", "air return", "baseboard",
                "guard", "shield", "bezel", "fastener",
                "handoff", "pipe", "metering", "inlet", "coupler", "mount", "pedestal", "brace") ||
                Has(source, "enamel", "steel", "worktop", "skirting", "fitout-housing", "obstacle") ||
                source is "contour-4 interior teal" or "contour-4 interior edge" or "contour-4 fitout housing" or
                    "contour-4 fitout control housing" or "contour-4 fitout storage housing" or "contour-4 fitout faceplate" or
                    "furniture_painted_metal" ||
                role is BlockRole.Fixture or BlockRole.Obstacle or BlockRole.Plug)
                return Finish.Enamel;
            if (role == BlockRole.Shell ||
                Has(name, " wall", "-wall", "__wall", "ceiling", "roof"))
                return Finish.Plaster;
            return Finish.Keep;
        }

        private static string PaintedWallSide(string name, BlockRole? role)
        {
            if (role == BlockRole.Shell && name.Contains("-wall", StringComparison.Ordinal)) return name;
            if (role != BlockRole.Plug) return null;
            // An unused freight connection is filled with a solid part of the wall.
            // Connected plugs are inactive, and the separate ResearchDoor portal is never painted.
            return name switch
            {
                "west-plug" => "west-wall",
                "east-plug" => "east-wall",
                "supply-plug" => "near-wall",
                "spur-plug" => "far-wall",
                _ => null
            };
        }

        private static bool PaintableWallMaterial(Material material) =>
            material && material.renderQueue < (int)RenderQueue.AlphaTest &&
            (!material.HasProperty("_Color") || material.color.a >= .99f) &&
            !material.IsKeywordEnabled("_EMISSION") &&
            !Has(material.name.ToLowerInvariant(), "glass", "glazing", "lens", "diffuser", "decal");

        private Material PaintedWallMaterial()
        {
            if (paintedWall) return paintedWall;
            paintedWall = new Material(shader) { name = "Contour-4 painted plaster dado" };
            paintedWall.SetFloat("_WearKind", (float)Finish.PaintedWall);
            paintedWall.SetTexture("_WearMap", plaster);
            return paintedWall;
        }

        private static void PaintDamage(MaterialPropertyBlock block, string name, bool workshop)
        {
            // Coordinates run along the entire wall, including its jambs. Each tuple records
            // the centre and extent in metres of a loose patch of paint or a knock at the base.
            (Vector4 A, Vector4 B, Vector4 C) damage = name switch
            {
                string id when id.StartsWith("west-wall", StringComparison.Ordinal) =>
                    (new(-2.59f, .41f, .20f, .30f), new(-3.78f, .14f, .27f, .18f), new(2.34f, 1.17f, .14f, .24f)),
                string id when id.StartsWith("east-wall", StringComparison.Ordinal) =>
                    (new(-2.57f, .87f, .32f, .36f), new(2.66f, .55f, .34f, .39f), new(3.49f, .13f, .26f, .19f)),
                string id when id.StartsWith("near-wall", StringComparison.Ordinal) && workshop =>
                    (new(7.03f, .49f, .32f, .43f), new(1.91f, 1.23f, .35f, .27f), new(8.45f, .11f, .27f, .17f)),
                string id when id.StartsWith("near-wall", StringComparison.Ordinal) =>
                    (new(5.96f, .78f, .45f, .35f), new(7.03f, .17f, .29f, .22f), new(.84f, 1.34f, .25f, .26f)),
                _ => (new(2.17f, 1.07f, .29f, .46f), new(7.27f, .89f, .43f, .49f), new(8.78f, .13f, .35f, .22f))
            };
            block.SetVector(WallChipA, damage.A);
            block.SetVector(WallChipB, damage.B);
            block.SetVector(WallChipC, damage.C);
        }

        private static void HideLegacyWallCladding(Transform wall)
        {
            // Both receiving prefabs put decorative metal panels over each solid wall face.
            // The renderer is disabled without touching its mesh or its parent geometry.
            foreach (Transform child in wall)
                if (child.name is "Inset cladding" or "Lower bulkhead rail" &&
                    child.TryGetComponent(out MeshRenderer overlay))
                    overlay.enabled = false;
        }

        private static bool Has(string value, params string[] fragments)
        {
            foreach (string fragment in fragments)
                if (value.Contains(fragment, StringComparison.Ordinal)) return true;
            return false;
        }

        private static Bounds InFrame(Bounds source, Matrix4x4 matrix)
        {
            var bounds = new Bounds(matrix.MultiplyPoint3x4(source.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = new((i & 1) == 0 ? source.min.x : source.max.x,
                    (i & 2) == 0 ? source.min.y : source.max.y, (i & 4) == 0 ? source.min.z : source.max.z);
                bounds.Encapsulate(matrix.MultiplyPoint3x4(point));
            }
            return bounds;
        }

        private static float StablePhase(string room)
        {
            uint hash = 2166136261;
            foreach (char letter in room)
                hash = unchecked((hash ^ letter) * 16777619);
            return (hash % 1021) / 67f;
        }

        private void InstallWeb(Transform parent, Vector3 corner, Vector3 firstWall, Vector3 secondWall, float span)
        {
            Vector3[] anchor =
            {
                corner + firstWall * span + Vector3.up * (.44f * span),
                corner + firstWall * (.68f * span) + Vector3.down * (.50f * span),
                corner + firstWall * (.29f * span) + Vector3.down * (.72f * span),
                corner + secondWall * (.24f * span) + Vector3.down * (.66f * span),
                corner + secondWall * (.76f * span) + Vector3.down * (.32f * span),
                corner + secondWall * (.92f * span) + Vector3.up * (.57f * span),
                corner + firstWall * (.13f * span) + Vector3.up * (.67f * span)
            };
            Vector3 hub = corner + (firstWall * .29f + secondWall * .31f - Vector3.up * .08f) * span;
            Vector3 normal = (firstWall + secondWall).normalized;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int spoke = 0; spoke < anchor.Length; spoke++)
            {
                Vector3 end = anchor[spoke], next = anchor[(spoke + 1) % anchor.Length];
                Thread(hub, end, .0019f);
                for (int band = 1; band <= 4; band++)
                {
                    if ((spoke * 3 + band * 7) % 10 == 0) continue;
                    float distance = .19f + band * .16f;
                    Vector3 a = Vector3.Lerp(hub, end, distance);
                    Vector3 b = Vector3.Lerp(hub, next, distance + .025f * (spoke % 3 - 1));
                    Vector3 mid = Vector3.Lerp(a, b, .5f) + Vector3.down * (span * .031f * band / 4f);
                    Thread(a, mid, .00125f);
                    Thread(mid, b, .00125f);
                }
            }
            if (!webMaterial)
            {
                webMaterial = new Material(Shader.Find("Standard"))
                {
                    name = "Contour-4 dusty silk",
                    color = new Color(.47f, .47f, .40f)
                };
                webMaterial.SetFloat("_Glossiness", .05f);
            }
            var mesh = new Mesh { name = WebName };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            webs.Add(mesh);
            var node = RaidAssembly.Child(parent, WebName).gameObject;
            node.layer = parent.gameObject.layer;
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = webMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            void Thread(Vector3 a, Vector3 b, float width)
            {
                Vector3 edge = Vector3.Cross(b - a, normal).normalized * width;
                int index = vertices.Count;
                vertices.AddRange(new[] { a - edge, b - edge, b + edge, a + edge,
                    a - edge, b - edge, b + edge, a + edge });
                triangles.AddRange(new[] { index, index + 1, index + 2, index, index + 2, index + 3,
                    index + 4, index + 6, index + 5, index + 4, index + 7, index + 6 });
            }
        }

        internal void Release()
        {
            foreach (Material material in materials.Values) Destroy(material);
            foreach (Mesh mesh in webs) Destroy(mesh);
            if (webMaterial) Destroy(webMaterial);
            if (paintedWall) Destroy(paintedWall);
            materials.Clear();
            webs.Clear();
            webMaterial = null;
            paintedWall = null;
        }

        private static void Destroy(Object resource)
        {
            if (Application.isPlaying) Object.Destroy(resource); else Object.DestroyImmediate(resource);
        }
    }
}

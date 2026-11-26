using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Contour4.Procedural.Catalog;
using UnityEngine;

namespace SpaceJourney
{
    internal static class CatalogWallObservation
    {
        private static readonly Regex Shell = new(@"(?:^|-)wall(?:-|$)|^(?:[a-z-]+-)?(?:bridge|helix|turn)-\d+-(?:left|right)$|^(?:pit-(?:east|south|west|freight-fascia)|shaft-(?:east|west|near|far))$|^shutter-(?:north|south)-guide$|^(?:east|west)-niche-return$|^(?:[a-z-]+-)?(?:left|right)-jamb$|^[a-z-]+-bridge-jamb--?\d+$");
        private static readonly Regex RingWall = new(@"^(?:Quota|Loot[1-3])__\w+_(?:Wall|Lintel)(?:_|$)");
        private static readonly Regex RingClosure = new(@"^(?:Shaft closure|Bridge side (?:lower|upper)) (?:negative|positive) Z$");
        private static readonly Regex StarterWall = new(@"^(?:Outer wall|(?:Station|Truck) threshold (?:jamb|header))$");

        internal static string Classify(string stage, string room, Renderer renderer, Block block, GrayModule module)
        {
            if (room.StartsWith("spherical-chamber", StringComparison.Ordinal)) return "sphere";
            if (stage.StartsWith("packed-", StringComparison.Ordinal)) return "packed";
            string name = renderer.name;
            if (name is "Inset cladding" or "Lower bulkhead rail") return "legacy-overlay";
            if (stage == "starter" && StarterWall.IsMatch(name)) return "wall";
            if (stage == "ring-entrance" && name == "Collar jamb") return "wall";
            if (stage.StartsWith("assembled-ring", StringComparison.Ordinal) &&
                (RingWall.IsMatch(name) || RingClosure.IsMatch(name))) return "wall";
            if (block == null) return "other";
            if (block.Role == BlockRole.Plug && name != "common-hatch-plug" && module.Docks.Any(dock =>
                dock.PlugId == name && (dock.Use & (DockUse.Physical | DockUse.DoorPortal)) != 0))
                return "closed-plug";
            bool rail = room is "freight-gravity-rise" or "freight-gravity-drop";
            if (block.Role == BlockRole.Shell && !rail && name != "pit-catch-wall" && Shell.IsMatch(name)) return "wall";
            return "other";
        }

        internal static string Describe(Transform root, Renderer renderer, Material material, Block block, GrayModule module)
        {
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            string[] windows = { "_WearMap", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_EmissionMap" };
            string textures = string.Join(";", windows.Where(prop => material && material.HasProperty(prop))
                .Select(prop => $"{prop}={material.GetTexture(prop)?.name}"));
            var frame = properties.GetMatrix("_WearFrame");
            var rootFrame = root.worldToLocalMatrix * renderer.localToWorldMatrix;
            Vector4 upA = properties.GetVector("_WallUpA"), upB = properties.GetVector("_WallUpB");
            Vector4 fromA = properties.GetVector("_WallFromA"), fromB = properties.GetVector("_WallFromB");
            var localCenter = root.InverseTransformPoint(renderer.bounds.center);
            Vector3 v0 = block == null ? default : ToUnity(block.Vertices[0]);
            Vector3 v1 = block == null ? default : ToUnity(block.Vertices[1]);
            Vector3 v4 = block != null && block.Vertices.Length >= 5 ? ToUnity(block.Vertices[4]) : default;
            Vector3 v5 = block != null && block.Vertices.Length >= 6 ? ToUnity(block.Vertices[5]) : default;
            string floors = module == null ? "" : string.Join(";", module.Floors.Select(floor =>
                $"{floor.Id}:{Values(ToUnity(floor.Pose.Position))}:{Values(ToUnity(floor.Pose.Up))}"));
            string appearance = material ? string.Join(";", new[] { "_Color", "_EmissionColor", "_Glossiness", "_Metallic" }
                .Where(material.HasProperty).Select(prop => prop is "_Color" or "_EmissionColor" ?
                    $"{prop}={Values(material.GetColor(prop))}" : $"{prop}={material.GetFloat(prop):R}")) : "";
            return string.Join("\t", material && material.shader.isSupported, textures, appearance,
                Delta(frame, rootFrame).ToString("R", CultureInfo.InvariantCulture), Values(localCenter),
                Values(upA), Values(upB), Values(fromA), Values(fromB),
                Values(v0), Values(v1), Values(v4), Values(v5), floors,
                Values(properties.GetVector("_WearMin")), Values(properties.GetVector("_WearMax")),
                Values(properties.GetVector("_WearProfile")), Values(properties.GetVector("_WallChipA")),
                Values(properties.GetVector("_WallChipB")), Values(properties.GetVector("_WallChipC")),
                VectorString(frame), VectorString(properties.GetMatrix("_WearInverseFrame")));
        }

        private static float Delta(Matrix4x4 a, Matrix4x4 b) => Enumerable.Range(0, 16).Max(i => Math.Abs(a[i] - b[i]));
        private static Vector3 ToUnity(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
        private static string Values(Vector4 v) => $"{v.x:R},{v.y:R},{v.z:R},{v.w:R}";
        private static string Values(Vector3 v) => $"{v.x:R},{v.y:R},{v.z:R}";
        private static string VectorString(Matrix4x4 frame) => string.Join(";", Enumerable.Range(0, 4)
            .Select(index => Values(frame.GetColumn(index))));
    }
}

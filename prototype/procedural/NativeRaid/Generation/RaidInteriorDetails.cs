using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed partial class RaidInteriorDetails
    {
        internal const string ObjectName = "Institute surface details";
        private enum Design { Service, Controls, Storage, Safety, Dispatch }
        private enum Ink { Plate, Rim, Letter, Amber, Teal, Rubber, BlueSteel, Plaster, Olive, Tile, Conduit, CoolLens, WarmLens }
        private readonly List<Material> materials = new();
        private readonly List<Mesh> meshes = new();
        private readonly RoomSurfaceWear wear = new();

        // These are original stencil glyphs. The plate stays readable without a font asset.
        private static readonly Dictionary<char, string> Stencil = new()
        {
            ['A'] = "010101111101101", ['B'] = "110101110101110", ['C'] = "011100100100011",
            ['D'] = "110101101101110", ['E'] = "111100110100111", ['F'] = "111100110100100",
            ['G'] = "011100101101011", ['H'] = "101101111101101", ['I'] = "111010010010111",
            ['J'] = "001001001101010", ['K'] = "101101110101101", ['L'] = "100100100100111",
            ['M'] = "101111111101101", ['N'] = "101111111111101", ['O'] = "010101101101010",
            ['P'] = "110101110100100", ['Q'] = "010101101111011", ['R'] = "110101110101101",
            ['S'] = "011100010001110", ['T'] = "111010010010010", ['U'] = "101101101101111",
            ['V'] = "101101101101010", ['W'] = "101101111111101", ['X'] = "101101010101101",
            ['Y'] = "101101010010010", ['Z'] = "111001010100111",
            ['0'] = "111101101101111", ['1'] = "010110010010111", ['2'] = "110001010100111",
            ['3'] = "110001010001110", ['4'] = "101101111001001", ['5'] = "111100110001110",
            ['6'] = "011100111101111", ['7'] = "111001010010010", ['8'] = "111101111101111",
            ['9'] = "111101111001110"
        };

        internal void Room(BakedRoom built)
        {
            GrayModule module = built.Plan.Module;
            var shapes = built.Colliders.ToLookup(collider => collider.name, StringComparer.Ordinal);
            var walls = new List<Vector3[]>();
            var equipment = new List<Vector3[]>();
            var decks = new List<Vector3[]>();
            foreach (Block block in module.Geometry)
            {
                if (block.Vertices.Length != 8 || !shapes[block.Id].Any(VisibleShape)) continue;
                if (block.Role == BlockRole.Shell && !ExcludedWall(block.Id))
                    walls.Add(block.Vertices.Select(RaidPlan.Vector).ToArray());
                else if (block.Role == BlockRole.Floor)
                    decks.Add(block.Vertices.Select(RaidPlan.Vector).ToArray());
                else if (EquipmentName(block.Id) && block.Role is BlockRole.Fixture or BlockRole.Obstacle)
                    equipment.Add(block.Vertices.Select(RaidPlan.Vector).ToArray());
            }

            var obstructions = Obstructions(built.Root, built.Colliders);
            var (code, design) = Scheme(module.Id);
            var batch = new Batch();
            foreach (var floor in module.Floors.Take(2))
            {
                Basis basis = Basis.Of(floor);
                if (Choose(walls, basis, false, out Face sign, obstructions))
                    Plate(batch, sign, code, design);
                else if (Choose(decks, basis, true, out Face marking))
                    Plate(batch, marking, code, design);
            }

            if (module.Floors.Count != 0)
            {
                var basis = Basis.Of(module.Floors[0]);
                bool hasWall = Choose(walls, basis, false, out Face wall, obstructions);
                if (Choose(equipment, basis, false, out Face device, obstructions, 1.19f) ||
                    (hasWall && Choose(walls, basis, false, out device, obstructions, 1.57f, wall.Origin)))
                    Equipment(batch, device, design);
                if (hasWall)
                    ServiceStrip(batch, wall, design);
            }
            if (module.Id is "spiral-corridor" or "quarter-turn-bridge")
                MarkRails(batch, module.Geometry.Where(block => shapes[block.Id].Any(VisibleShape)));
            switch (module.Id)
            {
                case "right-angle-chambers":
                    TransferRooms(batch, module);
                    break;
                case RaidExtraction.OrdinaryId:
                    QuotaApron(batch, built.Plan);
                    break;
                case "straight-corridor-short":
                case "straight-corridor-long":
                    StraightPassage(batch, module);
                    break;
                case "elbow-corridor":
                case "junction-corridor":
                case "upper-exit-corridor":
                    BranchPassage(batch, module, shapes);
                    break;
                case "quarter-turn-bridge":
                case "spiral-corridor":
                case "u-shaft":
                    CurvedPassage(batch, module, shapes);
                    break;
                case "top-to-top-chambers":
                case "floor-to-floor-chambers":
                case "niche-chamber":
                    LabelDocks(batch, module);
                    break;
                case MaintenanceTrench.Id:
                    Trench(batch, module);
                    break;
                case string id when SphericalChamber.IsVariant(id):
                    SphereGuidance(batch, module);
                    break;
                case MobiusHall.Id:
                    RibbonStations(batch, module);
                    break;
            }
            Install(built.Root, batch);
            wear.Apply(built.Root, module.Id, module.Geometry, module);
        }

        internal void BridgeFinishes(BakedRoom built) =>
            wear.Apply(built.Root, built.Plan.Module.Id, built.Plan.Module.Geometry, built.Plan.Module);

        internal void Transit(Transform root, IEnumerable<Block> geometry)
        {
            var batch = new Batch();
            MarkRails(batch, geometry);
            Sweep(batch, geometry, "bridge-", "freight-turn-");
            Install(root, batch);
            wear.Apply(root, root.name, geometry);
        }

        internal void RingRoom(Transform room)
        {
            var geometry = room.Find("Model");
            if (!geometry) return;
            var supports = geometry.GetComponentsInChildren<Collider>().Where(VisibleShape).ToArray();
            var walls = supports.Where(collider => collider.name.Contains("wall", StringComparison.OrdinalIgnoreCase) &&
                !ExcludedWall(collider.name)).Select(collider => Shape(room, collider)).Where(shape => shape != null);
            var devices = supports.Where(collider => EquipmentName(collider.name))
                .Select(collider => Shape(room, collider)).Where(shape => shape != null);
            var basis = new Basis(Vector3.zero, Vector3.forward, Vector3.up, Vector3.right, Vector2.zero);
            var (code, design) = room.name switch
            {
                "Quota" => ("QUO", Design.Dispatch),
                "Loot1" => ("R01", Design.Storage),
                "Loot2" => ("R02", Design.Controls),
                _ => ("R03", Design.Safety)
            };
            var obstructions = Obstructions(room, supports);
            var batch = new Batch();
            if (Choose(walls, basis, false, out Face face, obstructions))
            {
                Plate(batch, face, code, design);
                if (Choose(devices, basis, false, out Face device, obstructions, 1.19f) ||
                    Choose(walls, basis, false, out device, obstructions, 1.57f, face.Origin))
                    Equipment(batch, device, design);
                ServiceStrip(batch, face, design);
            }
            else
            {
                var floor = supports.Where(collider => collider.name.EndsWith("__Floor", StringComparison.Ordinal))
                    .Select(collider => Shape(room, collider)).Where(shape => shape != null);
                if (Choose(floor, basis, true, out Face marking)) Plate(batch, marking, code, design);
            }
            Install(room, batch);
            wear.Apply(room, room.name, authoredWall: (renderer, bounds) => renderer.transform.IsChildOf(geometry) &&
                (renderer.name.Contains("_Wall", StringComparison.Ordinal) ||
                    renderer.name.Contains("_Lintel", StringComparison.Ordinal)) ? FlatWall(bounds) : null);
        }

        internal void RingBridge(Transform bridge) => wear.Apply(bridge, bridge.name);
        internal void RingCollar(Transform collar) => wear.Apply(collar, "ring entrance",
            authoredWall: (renderer, bounds) => renderer.transform.parent == collar && renderer.name == "Collar jamb"
                ? FlatWall(bounds) : null);
        internal void RingRemainder(Transform ring)
        {
            var closures = ring.Find("V10 shaft side closures");
            if (!closures)
            {
                wear.Apply(ring, "ring structure");
                return;
            }

            var lower = BridgeWall("BridgeQuotaLoot1");
            var upper = BridgeWall("BridgeLoot1Loot2");
            wear.Apply(ring, "ring structure", authoredWall: (renderer, bounds) =>
                renderer.transform.parent != closures ? null : renderer.name switch
                {
                    "Bridge side lower negative Z" or "Bridge side lower positive Z" => lower,
                    "Bridge side upper negative Z" or "Bridge side upper positive Z" => upper,
                    "Shaft closure negative Z" or "Shaft closure positive Z" => FlatWall(bounds, bounds.min.y),
                    _ => null
                });

            PaintedRoomWalls.Projection BridgeWall(string name)
            {
                var markers = ring.Find(name + "/Markers");
                return PaintedRoomWalls.Projection.Circular(Sample("04"), Sample("12"));

                PaintedRoomWalls.Frame Sample(string index)
                {
                    var marker = markers.Find(name + "__Sample_" + index);
                    return new PaintedRoomWalls.Frame(ring.InverseTransformPoint(marker.position),
                        ring.InverseTransformDirection(marker.up), Vector3.zero);
                }
            }
        }
        internal void StartFixtureFinishes(Transform luminaires) => wear.Apply(luminaires, "start luminaires");

        private static PaintedRoomWalls.Projection FlatWall(Bounds bounds, float elevation = 0) =>
            PaintedRoomWalls.Projection.Flat(new PaintedRoomWalls.Frame(Vector3.up * elevation,
                Vector3.up, Vector3.right), bounds.size);

        internal void StartBay(Transform root, bool serviceBay, float insideWall, float height,
            Vector2 stationOpening, Vector2 truckOpening, float depth, float wallThickness)
        {
            var batch = new Batch();
            var plate = new Face(new Vector3(-insideWall, 1.59f, -4.40f),
                Vector3.forward, Vector3.up, Vector3.right, 2.1f, 1.25f);
            Plate(batch, plate, serviceBay ? "SRV" : "PRS", serviceBay ? Design.Service : Design.Controls);
            ServiceStrip(batch, plate, Design.Dispatch);
            float panelHeight = Mathf.Min(height - .65f, 2.1f);
            var panel = new Face(new Vector3(insideWall, panelHeight, -3.25f),
                Vector3.back, Vector3.up, Vector3.left, 1.4f, .92f);
            Equipment(batch, panel, serviceBay ? Design.Service : Design.Controls);
            ShuttleApron(batch, serviceBay, stationOpening, truckOpening, depth, wallThickness);
            Install(root, batch);
            wear.Apply(root, serviceBay ? "service-garage" : "pressure-dock",
                authoredWall: (renderer, bounds) => renderer.transform.parent == root &&
                    renderer.name is "Outer wall" or "Station threshold jamb" or "Station threshold header" or
                        "Truck threshold jamb" or "Truck threshold header" ? FlatWall(bounds) : null);
        }

        private static (string Code, Design Design) Scheme(string id) => id switch
        {
            "calibration-sector" => ("MET", Design.Controls),
            "observation-lab" => ("OBS", Design.Controls),
            "observation-lab-broken-floor" => ("OBS", Design.Safety),
            "gallery-chamber" => ("GAL", Design.Controls),
            "shaft-plant" => ("PMP", Design.Service),
            "u-shaft" => ("U02", Design.Safety),
            "maintenance-trench" => ("DRN", Design.Service),
            "service-cupboard" => ("SPT", Design.Storage),
            "instrument-service" => ("INS", Design.Controls),
            "sample-storage" => ("STO", Design.Storage),
            "sample-storage-narrow" => ("ST2", Design.Storage),
            "sample-receiving" => ("IN", Design.Dispatch),
            "freight-dispatch" => ("OUT", Design.Dispatch),
            "elbow-corridor" => ("C02", Design.Service),
            "straight-corridor-long" => ("C03", Design.Service),
            "straight-corridor-short" => ("C01", Design.Service),
            "junction-corridor" => ("JCT", Design.Dispatch),
            "upper-exit-corridor" => ("UPR", Design.Safety),
            "quarter-turn-bridge" => ("B90", Design.Safety),
            "spiral-corridor" => ("B18", Design.Safety),
            "top-to-top-chambers" => ("T01", Design.Controls),
            "floor-to-floor-chambers" => ("T02", Design.Safety),
            "right-angle-chambers" => ("T03", Design.Controls),
            "niche-chamber" => ("T04", Design.Storage),
            "mobius-hall" => ("MOB", Design.Controls),
            RaidExtraction.OrdinaryId or RaidExtraction.StraightBridgeId or RaidExtraction.BentBridgeId => ("QUO", Design.Dispatch),
            _ when id == SphericalChamber.Id || id.StartsWith(SphericalChamber.Id + "-", StringComparison.Ordinal) => ("SPH", Design.Controls),
            _ => ("NR4", Design.Service)
        };

        private static bool VisibleShape(Collider collider) => collider && collider.enabled &&
            !collider.isTrigger && collider.gameObject.activeInHierarchy &&
            collider.TryGetComponent(out MeshRenderer renderer) && renderer.enabled;

        private static bool ExcludedWall(string id) => id.Contains("glass", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("sight", StringComparison.OrdinalIgnoreCase) || id.Contains("portal", StringComparison.OrdinalIgnoreCase);

        private static bool EquipmentName(string id) =>
            id.Contains("cabinet", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("console", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("instrument", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("counter", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("panel", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("switch", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("crate", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("duct", StringComparison.OrdinalIgnoreCase);

        private static Vector3[] Shape(Transform room, Collider collider)
        {
            if (collider is MeshCollider mesh && mesh.sharedMesh && mesh.sharedMesh.isReadable &&
                mesh.sharedMesh.vertexCount <= 24)
                return mesh.sharedMesh.vertices.Select(ToRoom).ToArray();
            if (collider is not BoxCollider box) return null;
            var half = box.size / 2;
            var points = new Vector3[8];
            for (int i = 0; i < points.Length; i++)
                points[i] = ToRoom(box.center + new Vector3((i & 1) == 0 ? -half.x : half.x,
                    (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z));
            return points;
            Vector3 ToRoom(Vector3 point) => room.InverseTransformPoint(collider.transform.TransformPoint(point));
        }

        private static Bounds[] Obstructions(Transform room, IEnumerable<Collider> colliders)
        {
            var obstruction = new List<Bounds>();
            foreach (Collider collider in colliders)
            {
                if (!collider || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy)
                    continue;
                Bounds local;
                if (collider is BoxCollider box)
                    local = new Bounds(box.center, box.size);
                else if (collider is MeshCollider mesh && mesh.sharedMesh && mesh.sharedMesh.vertexCount <= 160)
                    local = mesh.sharedMesh.bounds;
                else continue;

                Vector3 center = room.InverseTransformPoint(collider.transform.TransformPoint(local.center));
                var bounds = new Bounds(center, Vector3.zero);
                for (int corner = 0; corner < 8; corner++)
                {
                    var position = new Vector3((corner & 1) == 0 ? local.min.x : local.max.x,
                        (corner & 2) == 0 ? local.min.y : local.max.y,
                        (corner & 4) == 0 ? local.min.z : local.max.z);
                    bounds.Encapsulate(room.InverseTransformPoint(collider.transform.TransformPoint(position)));
                }
                obstruction.Add(bounds);
            }
            return obstruction.ToArray();
        }

        private static bool Exposed(Vector3 origin, Vector3 right, Vector3 normal, float width, Bounds[] obstructions)
        {
            if (obstructions == null) return true;
            float side = Mathf.Min(width, 1.38f) * .34f;
            for (int sample = -1; sample <= 1; sample++)
            {
                var ray = new Ray(origin + normal * .025f + right * (sample * side), normal);
                foreach (Bounds shape in obstructions)
                    if (shape.IntersectRay(ray, out float depth) && depth < 1.85f)
                        return false;
            }
            return true;
        }

        private static bool Choose(IEnumerable<Vector3[]> shapes, Basis basis, bool deck, out Face face,
            Bounds[] obstructions = null, float targetHeight = 1.80f, Vector3? avoid = null)
        {
            face = default;
            float best = float.NegativeInfinity;
            foreach (Vector3[] shape in shapes)
            {
                var local = Array.ConvertAll(shape, basis.Project);
                var bounds = new Bounds(local[0], Vector3.zero);
                foreach (Vector3 point in local) bounds.Encapsulate(point);
                Vector3 min = bounds.min, max = bounds.max, size = bounds.size, center = bounds.center;
                if (local.Any(point => !AtEnd(point.x, min.x, max.x) || !AtEnd(point.y, min.y, max.y) ||
                    !AtEnd(point.z, min.z, max.z))) continue;

                Vector3 facing, horizontal, vertical;
                float width, height;
                if (deck)
                {
                    if (size.x < .85f || size.z < .8f || Mathf.Abs(max.y) > .12f) continue;
                    facing = basis.Up;
                    horizontal = basis.Forward;
                    vertical = Vector3.Cross(horizontal, facing).normalized;
                    width = Mathf.Min(.9f, size.x - .2f);
                    height = Mathf.Min(.34f, size.z - .2f);
                    center.y = max.y;
                    center.z = min.z + .15f + height * .5f;
                }
                else
                {
                    if (size.y < .54f || min.y > 1.68f || max.y < .55f) continue;
                    bool end = size.x <= size.z;
                    width = (end ? size.z : size.x) - .18f;
                    if (width < .55f) continue;
                    float sign = end ? Mathf.Sign(basis.Interior.x - center.x) :
                        Mathf.Sign(basis.Interior.y - center.z);
                    if (end) center.x = sign > 0 ? max.x : min.x;
                    else center.z = sign > 0 ? max.z : min.z;
                    facing = (end ? basis.Forward : basis.Right) * sign;
                    vertical = basis.Up;
                    horizontal = Vector3.Cross(facing, vertical).normalized;
                    center.y = Mathf.Clamp(targetHeight, min.y + .26f, max.y - .26f);
                    height = Mathf.Min(1.22f, 2 * Mathf.Min(center.y - min.y, max.y - center.y) - .08f);
                }
                if (height < .3f) continue;
                var origin = basis.Point(center);
                float distance = Vector2.Distance(new Vector2(center.x, center.z), basis.Interior);
                for (int slot = deck ? 0 : -1; slot <= (deck ? 0 : 1); slot++)
                {
                    float shift = slot * Mathf.Min(1.10f, width * .29f);
                    float available = width - 2 * Mathf.Abs(shift);
                    if (available < .55f) continue;
                    float score = Mathf.Min(available, 4) + Mathf.Min(height, 1.2f) - distance * .13f - Mathf.Abs(shift) * .06f;
                    Vector3 candidate = origin + horizontal * shift;
                    if (score <= best || (avoid.HasValue && (candidate - avoid.Value).sqrMagnitude < 2.25f) ||
                        !Exposed(candidate, horizontal, facing, available, obstructions)) continue;
                    face = new Face(candidate, horizontal, vertical, facing, available, height);
                    best = score;
                }
            }
            return !float.IsNegativeInfinity(best);

            static bool AtEnd(float v, float a, float b) =>
                Mathf.Min(Mathf.Abs(v - a), Mathf.Abs(v - b)) < .006f;
        }

        private static void Plate(Batch batch, Face face, string code, Design design)
        {
            var plate = face.Sized(Mathf.Min(face.Width, 1.38f), Mathf.Min(face.Height, .57f));
            batch.Rect(plate, Ink.Plate, -.5f, -.5f, .5f, .5f);
            Ink accent = design is Design.Safety or Design.Dispatch ? Ink.Amber : Ink.Teal;
            batch.Rect(plate, accent, -.5f, .31f, .5f, .5f, .001f);
            batch.Rect(plate, Ink.Rim, -.5f, -.5f, .5f, -.41f, .001f);
            batch.Text(plate, code, Ink.Letter);
            batch.Rect(plate, accent, .25f, -.24f, .40f, -.12f, .001f);
            batch.Rect(plate, Ink.Rim, .25f, -.06f, .40f, .04f, .001f);
        }

        private static void Equipment(Batch batch, Face face, Design design)
        {
            var plate = face.Sized(Mathf.Min(face.Width, 1.01f), Mathf.Min(face.Height, .47f));
            batch.Rect(plate, Ink.Plate, -.5f, -.5f, .5f, .5f);
            batch.Rect(plate, Ink.Rim, -.47f, -.44f, .47f, -.34f, .001f);
            switch (design)
            {
                case Design.Controls:
                    batch.Rect(plate, Ink.Rim, -.43f, -.28f, .18f, .36f, .001f);
                    batch.Line(plate, Ink.Teal, new(-.37f, -.06f), new(-.22f, .22f), .045f, .002f);
                    batch.Line(plate, Ink.Teal, new(-.22f, .22f), new(-.09f, -.12f), .045f, .002f);
                    batch.Line(plate, Ink.Teal, new(-.09f, -.12f), new(.11f, .17f), .045f, .002f);
                    batch.Rect(plate, Ink.Amber, .30f, -.16f, .39f, .07f, .002f);
                    batch.Rect(plate, Ink.Letter, .30f, .15f, .39f, .31f, .002f);
                    break;
                case Design.Storage:
                case Design.Dispatch:
                    for (int row = 0; row < 3; row++)
                    {
                        float y = -.23f + row * .22f;
                        batch.Rect(plate, Ink.Letter, -.36f, y, -.24f, y + .12f, .001f);
                        batch.Rect(plate, row == 1 ? Ink.Amber : Ink.Rim, -.16f, y, .34f, y + .12f, .001f);
                    }
                    break;
                default:
                    for (int slot = 0; slot < 5; slot++)
                    {
                        float x = -.36f + slot * .13f;
                        batch.Rect(plate, Ink.Rim, x, -.20f, x + .072f, .32f, .001f);
                    }
                    batch.Rect(plate, Ink.Amber, .33f, -.04f, .40f, .20f, .001f);
                    break;
            }
        }

        private static void ServiceStrip(Batch batch, Face face, Design design)
        {
            if (face.Height < .65f || face.Width < 1.3f) return;
            float width = Mathf.Min(face.Width - .1f, 2.8f);
            var strip = new Face(face.Origin - face.Up * (face.Height * .44f), face.Right, face.Up, face.Normal, width, .095f);
            batch.Rect(strip, Ink.Plate, -.5f, -.5f, .5f, .5f);
            batch.Rect(strip, design is Design.Safety or Design.Dispatch ? Ink.Amber : Ink.Teal,
                -.48f, .03f, .48f, .38f, .001f);
            foreach (float position in new[] { -.34f, 0f, .34f })
                batch.Rect(strip, Ink.Rim, position - .017f, -.5f, position + .017f, .5f, .002f);
        }

        private static void MarkRails(Batch batch, IEnumerable<Block> geometry)
        {
            // Each band is painted within its actual inner shell triangle, including on a twisted rail.
            var rails = geometry.Where(block => block.Role == BlockRole.Shell && block.Vertices.Length == 8 &&
                (block.Id.EndsWith("-left", StringComparison.Ordinal) || block.Id.EndsWith("-right", StringComparison.Ordinal)))
                .ToArray();
            int stride = Mathf.Max(1, Mathf.CeilToInt(rails.Length / 24f));
            for (int index = 0; index < rails.Length; index += stride)
            {
                var rail = rails[index];
                bool left = rail.Id.EndsWith("-left", StringComparison.Ordinal);
                Vector3 bottom = RaidPlan.Vector(rail.Vertices[left ? 3 : 0]);
                Vector3 top = RaidPlan.Vector(rail.Vertices[left ? 2 : 1]);
                Vector3 end = RaidPlan.Vector(rail.Vertices[left ? 6 : 5]);
                Vector3 up = top - bottom, along = end - top;
                Vector3 normal = Vector3.Cross(up, along).normalized * (left ? -1f : 1f);
                float rise = up.magnitude;
                if (rise < .08f || along.sqrMagnitude < .0025f) continue;
                float high = Mathf.Min(.86f, .52f + .16f / rise);
                batch.SurfaceBand(bottom, up, along, normal, .08f, .40f, .52f, high, Ink.Plate);
                batch.SurfaceBand(bottom, up, along, normal, .12f, .35f, .54f, high - .018f, Ink.Amber, .001f);
            }
        }
        private void Install(Transform root, Batch batch)
        {
            if (batch.Vertices.Count == 0) return;
            if (materials.Count == 0)
            {
                Shader shader = Shader.Find("Standard");
                AddMaterial(shader, "enamel backing", new Color(.115f, .16f, .17f));
                AddMaterial(shader, "dull edge metal", new Color(.37f, .45f, .46f));
                AddMaterial(shader, "ivory stencil", new Color(.83f, .83f, .69f));
                AddMaterial(shader, "warning ochre", new Color(.74f, .48f, .18f));
                AddMaterial(shader, "institute enamel", new Color(.23f, .54f, .51f));
                AddMaterial(shader, "gasket rubber", new Color(.066f, .078f, .079f));
                AddMaterial(shader, "pressure door enamel", new Color(.30f, .43f, .48f));
                AddMaterial(shader, "porcelain finish", new Color(.79f, .77f, .69f));
                AddMaterial(shader, "olive wall paint", new Color(.34f, .39f, .30f));
                AddMaterial(shader, "ochre service tile", new Color(.46f, .32f, .20f));
                AddMaterial(shader, "service conduit", new Color(.24f, .22f, .19f));
                AddMaterial(shader, "cold opal lens", new Color(.69f, .86f, .87f), .52f);
                AddMaterial(shader, "warm opal lens", new Color(.93f, .71f, .43f), .50f);
            }

            var used = batch.Indices.Select((indices, index) => (indices, index))
                .Where(entry => entry.indices.Count > 0).ToArray();
            var mesh = new Mesh { name = root.name + " surface graphics", subMeshCount = used.Length };
            mesh.SetVertices(batch.Vertices);
            mesh.SetNormals(batch.Normals);
            for (int sub = 0; sub < used.Length; sub++) mesh.SetTriangles(used[sub].indices, sub);
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            var node = RaidAssembly.Child(root, ObjectName).gameObject;
            node.layer = root.gameObject.layer;
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = used.Select(entry => materials[entry.index]).ToArray();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private void AddMaterial(Shader shader, string name, Color color, float emission = 0)
        {
            var material = new Material(shader) { name = "Contour-4 " + name, color = color };
            material.SetFloat("_Glossiness", .18f);
            if (emission > 0)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * emission);
            }
            materials.Add(material);
        }

        internal void Release()
        {
            wear.Release();
            foreach (Material material in materials) Destroy(material);
            foreach (Mesh mesh in meshes) Destroy(mesh);
            materials.Clear();
            meshes.Clear();
        }

        private static void Destroy(Object resource)
        {
            if (Application.isPlaying) Object.Destroy(resource); else Object.DestroyImmediate(resource);
        }

        private readonly struct Basis
        {
            private readonly Vector3 origin;
            internal readonly Vector3 Forward, Up, Right;
            internal readonly Vector2 Interior;

            internal Basis(Vector3 origin, Vector3 forward, Vector3 up, Vector3 right, Vector2 interior)
            {
                this.origin = origin;
                Forward = forward;
                Up = up;
                Right = right;
                Interior = interior;
            }

            internal static Basis Of(Floor floor)
            {
                ModulePose pose = floor.Pose;
                var interior = new Vector2(floor.MapContour.Average(point => point.X),
                    floor.MapContour.Average(point => point.Y));
                return new Basis(RaidPlan.Vector(pose.Position), RaidPlan.Vector(pose.Forward), RaidPlan.Vector(pose.Up),
                    RaidPlan.Vector(pose.Right), interior);
            }

            internal Vector3 Project(Vector3 point)
            {
                point -= origin;
                return new Vector3(Vector3.Dot(point, Forward), Vector3.Dot(point, Up), Vector3.Dot(point, Right));
            }

            internal Vector3 Point(Vector3 point) => origin + Forward * point.x + Up * point.y + Right * point.z;
        }

        private readonly struct Face
        {
            internal readonly Vector3 Origin, Right, Up, Normal;
            internal readonly float Width, Height;

            internal Face(Vector3 origin, Vector3 right, Vector3 up, Vector3 normal, float width, float height)
            {
                Origin = origin;
                Right = right;
                Up = up;
                Normal = normal;
                Width = width;
                Height = height;
            }

            internal Face Sized(float width, float height) => new(Origin, Right, Up, Normal, width, height);
            internal Vector3 Point(Vector2 uv, float offset) =>
                Origin + Right * (uv.x * Width) + Up * (uv.y * Height) + Normal * (.004f + offset);
        }

        private sealed class Batch
        {
            internal readonly List<Vector3> Vertices = new();
            internal readonly List<Vector3> Normals = new();
            internal readonly List<int>[] Indices = Enumerable.Range(0, (int)Ink.WarmLens + 1)
                .Select(_ => new List<int>()).ToArray();

            internal void Rect(Face face, Ink ink, float x, float y, float endX, float endY, float offset = 0) =>
                Quad(face, ink, new(x, y), new(endX, y), new(endX, endY), new(x, endY), offset);

            internal void Line(Face face, Ink ink, Vector2 from, Vector2 to, float width, float offset)
            {
                Vector2 delta = to - from;
                Vector2 side = new Vector2(-delta.y, delta.x).normalized * (width * .5f);
                Quad(face, ink, from - side, to - side, to + side, from + side, offset);
            }

            internal void SurfaceBand(Vector3 bottom, Vector3 up, Vector3 along, Vector3 normal,
                float left, float right, float low, float high, Ink ink, float lift = 0f)
            {
                var basePoint = bottom + normal * (.004f + lift);
                Append(basePoint + up * low + along * left, basePoint + up * low + along * right,
                    basePoint + up * high + along * right, basePoint + up * high + along * left, normal, ink);
            }

            internal void Text(Face face, string text, Ink ink)
            {
                const float unit = .065f, yUnit = .107f, left = -.40f;
                for (int index = 0; index < text.Length; index++)
                {
                    string glyph = Stencil[text[index]];
                    for (int row = 0; row < 5; row++)
                    for (int col = 0; col < 3; col++)
                    {
                        if (glyph[row * 3 + col] == '0') continue;
                        float x = left + (index * 4 + col) * unit;
                        float y = .19f - row * yUnit;
                        Rect(face, ink, x, y - yUnit * .88f, x + unit * .88f, y, .002f);
                    }
                }
            }

            private void Quad(Face face, Ink ink, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float offset)
            {
                Append(face.Point(a, offset), face.Point(b, offset), face.Point(c, offset), face.Point(d, offset),
                    face.Normal, ink);
            }

            internal void Append(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Ink ink)
            {
                int start = Vertices.Count;
                Vertices.Add(a);
                Vertices.Add(b);
                Vertices.Add(c);
                Vertices.Add(d);
                for (int i = 0; i < 4; i++) Normals.Add(normal);
                bool front = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) >= 0;
                Indices[(int)ink].AddRange(front
                    ? new[] { start, start + 1, start + 2, start, start + 2, start + 3 }
                    : new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }
        }
    }
}

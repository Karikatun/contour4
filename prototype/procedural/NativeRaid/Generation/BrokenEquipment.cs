using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class BrokenEquipment
    {
        internal const string ExposedParts = "Exposed technical components";
        internal const string Instrument = "instrument-service-inspection-bench-instrument";
        internal const string Display = "instrument-service-inspection-bench-screen";
        internal const string Cabinet = "Ladder maintenance cabinet";
        private enum Finish { Case, Liner, Steel, Board, Copper, Ceramic, CableRed, CableBlue }
        private static readonly (string Name, Color Color, float Gloss)[] Finishes =
        {
            ("Graphite liner", new(.105f,.120f,.117f), .18f),
            ("Bare and folded steel", new(.38f,.40f,.39f), .31f),
            ("Green circuit board", new(.12f,.30f,.21f), .20f),
            ("Darkened copper", new(.48f,.237f,.121f), .31f),
            ("Terminal ceramic", new(.53f,.51f,.43f), .17f),
            ("Muted red insulation", new(.35f,.135f,.116f), .19f),
            ("Slate blue insulation", new(.142f,.247f,.29f), .19f)
        };
        private readonly List<Mesh> meshes = new();
        private Material[] materials;

        internal bool TryDressInstrument(Transform room, List<Collider> colliders)
        {
            if (!TryPart(room, colliders, Instrument, out var body, out var bounds) ||
                !TryPart(room, colliders, Display, out var screen, out var screenBounds) ||
                !Matches(bounds.size, new Vector3(.56f, .50f, .38f)))
                return false;
            var shell = new Geometry();
            var interior = new Geometry();
            Case(shell, interior, bounds.extents, cabinet: false);
            InstrumentParts(interior);
            AddLootTray(interior, BrokenEquipmentLoot.Instrument);
            Replace(body, shell, room, bounds.center, Instrument + " fractured housing");
            RetainDisplaySliver(room, screen, screenBounds);
            BrokenEquipmentLoot.FitHousing(room, body, bounds, BrokenEquipmentLoot.Instrument, colliders);
            BrokenEquipmentLoot.UseMeshCollider(screen, colliders);
            Install(room, body, bounds.center, interior, colliders);
            return true;
        }

        internal void DressCabinet(Transform room, List<Collider> colliders)
        {
            if (!TryPart(room, colliders, Cabinet, out var body, out var bounds) ||
                !Matches(bounds.size, new Vector3(1.80f, 1.65f, 1.20f)))
                return;
            var shell = new Geometry();
            var interior = new Geometry();
            Case(shell, interior, bounds.extents, cabinet: true);
            CabinetParts(interior);
            AddLootTray(interior, BrokenEquipmentLoot.Cabinet);
            Replace(body, shell, room, bounds.center, Cabinet + " opened housing");
            foreach (Transform panel in body.transform)
            {
                if (panel.localPosition.y < .14f && panel.name is "Drawer face" or "Drawer pull" &&
                    panel.TryGetComponent(out MeshRenderer renderer))
                    renderer.enabled = false;
            }
            BrokenEquipmentLoot.FitHousing(room, body, bounds, BrokenEquipmentLoot.Cabinet, colliders);
            Install(room, body, bounds.center, interior, colliders);
        }

        private static void AddLootTray(Geometry interior, BrokenEquipmentLoot.Pocket pocket) =>
            interior.Box(Finish.Steel, pocket.Tray.center, pocket.Tray.size);

        private static bool TryPart(Transform room, IEnumerable<Collider> colliders, string id, out MeshFilter filter, out Bounds bounds)
        {
            bounds = default;
            filter = null;
            foreach (Collider piece in colliders)
            {
                if (!piece || piece.name != id || !piece.enabled || piece.isTrigger ||
                    !piece.TryGetComponent(out MeshRenderer renderer) || !renderer.enabled ||
                    !piece.TryGetComponent(out filter) || !filter.sharedMesh || filter.sharedMesh.subMeshCount != 1)
                    continue;
                Matrix4x4 toRoom = room.worldToLocalMatrix * piece.transform.localToWorldMatrix;
                Bounds local = filter.sharedMesh.bounds;
                bounds = new Bounds(toRoom.MultiplyPoint3x4(local.min), Vector3.zero);
                for (int i = 1; i < 8; i++)
                    bounds.Encapsulate(toRoom.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? local.min.x : local.max.x,
                        (i & 2) == 0 ? local.min.y : local.max.y, (i & 4) == 0 ? local.min.z : local.max.z)));
                return true;
            }
            return false;
        }

        private static bool Matches(Vector3 actual, Vector3 expected) =>
            (actual - expected).sqrMagnitude < .000025f;

        private void Replace(MeshFilter filter, Geometry geometry, Transform room, Vector3 centre, string name)
        {
            Matrix4x4 toPart = filter.transform.worldToLocalMatrix * room.localToWorldMatrix *
                Matrix4x4.Translate(centre);
            filter.sharedMesh = geometry.Create(name, toPart);
            meshes.Add(filter.sharedMesh);
        }

        private void RetainDisplaySliver(Transform room, MeshFilter filter, Bounds bounds)
        {
            var remaining = new Geometry();
            float x = bounds.extents.x, y = bounds.extents.y, z = bounds.extents.z;
            remaining.Face(Finish.Case, Vector3.forward, new(-x, -y, z), new(-x * .19f, -y, z),
                new(-x * .52f, -y * .18f, z), new(-x, y * .82f, z));
            remaining.Face(Finish.Case, Vector3.back, new(-x, -y, -z), new(-x * .19f, -y, -z),
                new(-x * .52f, -y * .18f, -z), new(-x, y * .82f, -z));
            Replace(filter, remaining, room, bounds.center, Display + " fractured glass");
        }

        private static void Case(Geometry shell, Geometry inner, Vector3 half, bool cabinet)
        {
            float x = half.x, y = half.y, z = half.z;
            float wall = cabinet ? .041f : .016f;
            float width = 2 * (x - wall);
            float height = 2 * (y - wall);
            float depth = 2 * z;
            shell.Box(Finish.Case, new(-x + wall * .5f, 0, 0), new(wall, 2 * y, depth));
            shell.Box(Finish.Case, new(x - wall * .5f, 0, 0), new(wall, 2 * y, depth));
            shell.Box(Finish.Case, new(0, -y + wall * .5f, 0), new(width, wall, depth));
            shell.Box(Finish.Case, new(0, 0, -z + wall * .5f), new(width, height, wall));
            if (cabinet)
                shell.Box(Finish.Case, new(0, y - wall * .5f, 0), new(width, wall, depth));
            else
            {
                // A rear strip and folded section remain physically attached to the monitor lid.
                shell.Box(Finish.Case, new(0, y - wall * .5f, -z + .033f), new(width, wall, .066f));
                shell.Face(Finish.Case, Vector3.up, new(-x + wall, y, -.124f), new(-.12f, y, -.124f),
                    new(-.083f, y - .052f, .019f), new(-.177f, y - .085f, .126f), new(-x + wall, y - .012f, .112f));
                inner.Face(Finish.Steel, Vector3.up, new(-.12f, y - .001f, -.124f), new(-.112f, y - .001f, -.122f),
                    new(-.077f, y - .053f, .019f), new(-.172f, y - .085f, .127f), new(-.177f, y - .085f, .126f),
                    new(-.083f, y - .052f, .019f));
            }

            Vector2[] aperture = cabinet
                ? new Vector2[] { new(-.708f,-.651f), new(-.122f,-.691f), new(.657f,-.605f), new(.741f,-.220f),
                    new(.626f,.173f), new(.096f,.194f), new(-.654f,.152f), new(-.749f,-.194f) }
                : new Vector2[] { new(-.194f,-.156f), new(-.027f,-.178f), new(.164f,-.140f), new(.219f,.011f),
                    new(.167f,.177f), new(.040f,.209f), new(-.192f,.183f), new(-.218f,-.003f) };
            var outer = new Vector2[]
            {
                new(-x + wall, -y + wall), new(0, -y + wall), new(x - wall, -y + wall),
                new(x - wall, 0), new(x - wall, y - wall), new(0, y - wall),
                new(-x + wall, y - wall), new(-x + wall, 0)
            };
            for (int i = 0; i < aperture.Length; i++)
            {
                int next = (i + 1) % aperture.Length;
                var edgeA = aperture[i];
                var edgeB = aperture[next];
                Vector3 frontA = new(edgeA.x, edgeA.y, z), frontB = new(edgeB.x, edgeB.y, z);
                shell.Face(Finish.Case, Vector3.forward, new(outer[i].x, outer[i].y, z),
                    new(outer[next].x, outer[next].y, z), frontB, frontA);
                Vector3 toward = new(edgeB.y - edgeA.y, edgeA.x - edgeB.x, 0);
                float fold = cabinet ? .044f : .022f;
                inner.Face(Finish.Steel, -toward, frontA, frontB,
                    new(edgeB.x * .97f, edgeB.y * .96f, z - fold),
                    new(edgeA.x * .97f, edgeA.y * .96f, z - fold));
            }
            inner.Box(Finish.Liner, new(0,0,-z + 1.5f * wall), new(width - .01f, height - .01f, wall));
            if (cabinet)
            {
                Vector3 a = new(aperture[3].x, aperture[3].y, z), b = new(aperture[2].x, aperture[2].y, z);
                Vector3 tip = new(.452f,-.455f,z - .128f);
                inner.Face(Finish.Steel, Vector3.forward, a, b, tip, new(.50f,-.327f,z - .105f));
                inner.Face(Finish.Liner, Vector3.back, a - Vector3.forward * .013f, b - Vector3.forward * .013f,
                    tip - Vector3.forward * .013f, new(.50f,-.327f,z - .118f));
            }
        }

        private static void InstrumentParts(Geometry model)
        {
            Board(model, new(.037f, .027f, -.117f), .327f, .268f, .023f, .060f);
            Terminals(model, new(-.200f, .112f, -.018f), 4, .043f, .033f);
            model.Box(Finish.Steel, new(.152f, -.169f, -.084f), new(.139f,.029f,.172f));
            model.Box(Finish.Copper, new(.157f, -.122f, -.112f), new(.101f,.057f,.099f));
            model.Box(Finish.Liner, new(.157f, -.101f, -.112f), new(.103f,.026f,.101f));
            model.Wire(Finish.CableRed, .008f, new(.192f,-.116f,-.064f), new(.204f,-.058f,.070f),
                new(.194f,-.023f,.103f), new(.177f,-.041f,.128f));
            model.Wire(Finish.CableBlue, .007f, new(-.124f,.057f,-.074f), new(-.146f,.093f,.027f),
                new(-.132f,.133f,.074f), new(-.121f,.112f,.127f));
            model.Box(Finish.Steel, new(.179f,-.2255f,.125f), new(.089f,.017f,.059f), 16);
            model.Box(Finish.Steel, new(.086f,-.2275f,.144f), new(.038f,.013f,.032f), -20);
        }

        private static void CabinetParts(Geometry model)
        {
            model.Box(Finish.Steel, new(0,-.239f,-.333f), new(1.44f,1.12f,.051f));
            model.Box(Finish.Steel, new(-.674f,-.239f,-.442f), new(.049f,1.06f,.171f));
            model.Box(Finish.Steel, new(.674f,-.239f,-.442f), new(.049f,1.06f,.171f));
            Board(model, new(-.323f,-.006f,-.262f), .50f,.314f,.039f, .058f);
            Terminals(model, new(.316f,.018f,-.233f), 7, .080f,.058f);
            model.Box(Finish.Liner, new(-.232f,-.511f,-.147f), new(.46f,.146f,.366f));
            model.Box(Finish.Steel, new(-.228f,-.444f,-.137f), new(.411f,.045f,.337f));
            for (int coil = 0; coil < 9; coil++)
            {
                float x = -.383f + coil * .038f;
                model.Rod(Finish.Copper, .106f, new(x,-.349f,-.13f), new(x+.018f,-.349f,-.13f));
            }
            model.Rod(Finish.Liner, .121f, new(-.437f,-.349f,-.13f), new(-.402f,-.349f,-.13f));
            model.Rod(Finish.Liner, .121f, new(-.054f,-.349f,-.13f), new(-.018f,-.349f,-.13f));
            model.Box(Finish.Liner, new(.224f,-.456f,-.187f), new(.263f,.286f,.249f));
            model.Box(Finish.Steel, new(.224f,-.309f,-.179f), new(.272f,.04f,.26f));
            for (int vent = 0; vent < 4; vent++)
                model.Box(Finish.Steel, new(.224f,-.52f + vent*.043f,-.054f), new(.192f,.012f,.02f));
            model.Wire(Finish.CableRed, .014f, new(.164f,.013f,-.167f), new(.118f,-.078f,-.079f),
                new(.165f,-.203f,.08f), new(.106f,-.274f,.164f), new(.029f,-.25f,.197f));
            model.Wire(Finish.CableBlue, .012f, new(-.393f,-.348f,-.027f), new(-.410f,-.225f,.033f),
                new(-.32f,-.143f,.107f), new(-.197f,-.171f,.171f));
            model.Wire(Finish.CableRed, .013f, new(.318f,-.326f,-.134f), new(.376f,-.399f,.019f),
                new(.448f,-.456f,.202f), new(.406f,-.525f,.284f));
            model.Box(Finish.Steel, new(.288f,-.7705f,.211f), new(.191f,.027f,.156f), 13);
            model.Box(Finish.Steel, new(-.582f,-.7695f,.296f), new(.13f,.029f,.11f), -22);
        }

        private static void Board(Geometry model, Vector3 centre, float width, float height, float spacing, float standoff)
        {
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            {
                Vector3 screw = centre + new Vector3(x * width * .42f, y * height * .40f, .013f);
                model.Rod(Finish.Steel, spacing * .33f, screw - Vector3.forward * standoff, screw);
            }
            model.Box(Finish.Board, centre, new(width,height,.012f));
            model.Box(Finish.Liner, centre + new Vector3(-width*.06f,0,.026f),
                new(width*.35f,height*.37f,.043f));
            for (int row = -1; row <= 1; row += 2)
            for (int pin = -2; pin <= 2; pin++)
            {
                float x = -width * .06f + pin * width * .058f;
                model.Box(Finish.Copper, centre + new Vector3(x,row * height*.225f,.019f),
                    new(spacing * .35f,height*.205f,.012f));
            }
            for (int i = 0; i < 3; i++)
            {
                var capacitor = centre + new Vector3(width*(.26f + i*.067f), height*(.28f-i*.145f), .006f);
                model.Rod(Finish.Liner, spacing*.49f, capacitor, capacitor + Vector3.forward * (.051f+i*.006f));
                model.Rod(Finish.Steel, spacing*.49f, capacitor + Vector3.forward * (.051f+i*.006f),
                    capacitor + Vector3.forward * (.057f+i*.006f));
            }
            for (int trace = 0; trace < 3; trace++)
            {
                float y = -height * (.38f - trace * .092f);
                model.Box(Finish.Copper, centre + new Vector3(-width * .314f,y,.008f),
                    new(width*.15f, spacing*.095f,.006f));
            }
        }

        private static void Terminals(Geometry model, Vector3 start, int count, float pitch, float size)
        {
            Vector3 centre = start + Vector3.right * (pitch * (count - 1) * .5f);
            model.Box(Finish.Steel, centre, new(pitch * count + .03f, size * 1.5f, .031f));
            for (int contact = 0; contact < count; contact++)
            {
                Vector3 edge = start + Vector3.right * (pitch * contact) + Vector3.forward * .031f;
                model.Box(Finish.Ceramic, edge, new(size, size*1.34f, .059f));
                model.Rod(Finish.Copper, size*.22f, edge + Vector3.forward * .026f, edge + Vector3.forward * .039f);
            }
        }

        private void Install(Transform room, MeshFilter body, Vector3 centre, Geometry interior, List<Collider> colliders)
        {
            if (materials == null)
            {
                Shader standard = Shader.Find("Standard");
                materials = Finishes.Select(finish =>
                {
                    var material = new Material(standard) { name = "Contour-4 " + finish.Name, color = finish.Color };
                    material.SetFloat("_Glossiness", finish.Gloss);
                    return material;
                }).ToArray();
            }
            var display = RaidAssembly.Child(room, body.name + " / " + ExposedParts);
            display.localPosition = centre;
            display.gameObject.layer = body.gameObject.layer;
            Mesh mesh = interior.Create(ExposedParts + " – " + body.name, Matrix4x4.identity);
            meshes.Add(mesh);
            display.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            display.gameObject.AddComponent<MeshRenderer>().sharedMaterials =
                interior.Used.Select(finish => materials[(int)finish - 1]).ToArray();
            var contact = RaidAssembly.Child(display, body.name + " interior contacts");
            contact.gameObject.layer = body.gameObject.layer;
            var collider = contact.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            colliders.Add(collider);
        }

        internal void Release()
        {
            foreach (Mesh mesh in meshes) Destroy(mesh);
            meshes.Clear();
            if (materials == null) return;
            foreach (Material material in materials) Destroy(material);
            materials = null;
        }

        private static void Destroy(Object asset)
        {
            if (Application.isPlaying) Object.Destroy(asset); else Object.DestroyImmediate(asset);
        }

        private sealed class Geometry
        {
            private readonly List<Vector3> vertices = new();
            private readonly List<Vector2> uv = new();
            private readonly Dictionary<Finish, List<int>> indices = new();
            internal IEnumerable<Finish> Used => indices.Keys.OrderBy(finish => finish);

            internal void Face(Finish finish, Vector3 normal, params Vector3[] points)
            {
                if (!indices.TryGetValue(finish, out var triangles))
                {
                    triangles = new List<int>();
                    indices.Add(finish, triangles);
                }
                int start = vertices.Count;
                vertices.AddRange(points);
                foreach (Vector3 p in points)
                    uv.Add(Mathf.Abs(normal.y) > .5f ? new Vector2(p.x,p.z) :
                        Mathf.Abs(normal.x) > Mathf.Abs(normal.z) ? new Vector2(p.z,p.y) : new Vector2(p.x,p.y));
                for (int edge = 1; edge + 1 < points.Length; edge++)
                {
                    bool facing = Vector3.Dot(Vector3.Cross(points[edge] - points[0], points[edge + 1] - points[0]), normal) >= 0;
                    triangles.Add(start);
                    triangles.Add(start + (facing ? edge : edge + 1));
                    triangles.Add(start + (facing ? edge + 1 : edge));
                }
            }

            internal void Box(Finish finish, Vector3 centre, Vector3 size, float yaw = 0)
            {
                Vector3 h = size / 2;
                var rotation = Quaternion.Euler(0,yaw,0);
                Vector3 A(float x, float y, float z) => centre + rotation * new Vector3(x*h.x,y*h.y,z*h.z);
                Face(finish, rotation * Vector3.forward, A(-1,-1,1),A(1,-1,1),A(1,1,1),A(-1,1,1));
                Face(finish, rotation * Vector3.back, A(-1,-1,-1),A(1,-1,-1),A(1,1,-1),A(-1,1,-1));
                Face(finish, rotation * Vector3.right, A(1,-1,-1),A(1,-1,1),A(1,1,1),A(1,1,-1));
                Face(finish, rotation * Vector3.left, A(-1,-1,-1),A(-1,-1,1),A(-1,1,1),A(-1,1,-1));
                Face(finish, Vector3.up, A(-1,1,-1),A(1,1,-1),A(1,1,1),A(-1,1,1));
                Face(finish, Vector3.down, A(-1,-1,-1),A(1,-1,-1),A(1,-1,1),A(-1,-1,1));
            }

            internal void Rod(Finish finish, float radius, Vector3 first, Vector3 last)
            {
                const int sides = 8;
                Vector3 axis = (last - first).normalized;
                Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) > .9f ? Vector3.forward : Vector3.up).normalized;
                Vector3 up = Vector3.Cross(axis, side);
                var capStart = new Vector3[sides];
                var capEnd = new Vector3[sides];
                for (int index = 0; index < sides; index++)
                {
                    float angle = 2 * Mathf.PI * index / sides;
                    Vector3 rim = (Mathf.Cos(angle)*side + Mathf.Sin(angle)*up) * radius;
                    capStart[index] = first + rim;
                    capEnd[index] = last + rim;
                }
                for (int index = 0; index < sides; index++)
                {
                    int next = (index + 1) % sides;
                    Face(finish, capStart[index] + capStart[next] - first*2,
                        capStart[index], capStart[next], capEnd[next], capEnd[index]);
                }
                Face(finish, -axis, capStart);
                Face(finish, axis, capEnd);
            }

            internal void Wire(Finish finish, float radius, params Vector3[] knots)
            {
                for (int step = 1; step < knots.Length; step++)
                    Rod(finish, radius, knots[step-1], knots[step]);
                Vector3 bare = (knots[^1] - knots[^2]).normalized * (radius * 1.55f);
                Rod(Finish.Copper, radius * .52f, knots[^1], knots[^1] + bare);
            }

            internal Mesh Create(string name, Matrix4x4 transform)
            {
                var used = Used.ToArray();
                var mesh = new Mesh { name = name, subMeshCount = used.Length };
                mesh.SetVertices(vertices.Select(transform.MultiplyPoint3x4).ToList());
                mesh.SetUVs(0, uv);
                for (int index = 0; index < used.Length; index++)
                    mesh.SetTriangles(indices[used[index]], index);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}

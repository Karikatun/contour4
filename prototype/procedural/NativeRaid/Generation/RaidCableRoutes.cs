using System.Collections.Generic;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed partial class RaidReferenceRoomFitout
    {
        private static readonly Finish[] WireColors = { Finish.Red, Finish.CableBlue, Finish.CableGreen, Finish.Amber };

        private Collider InstallCableRoutes(string module, Transform room, int layer)
        {
            switch (module)
            {
                case "calibration-sector":
                    CalibrationCables(New("Cable Calibration overhead"), New("Cable Calibration severed wall"));
                    return CalibrationFloorCable(New("Cable Calibration aisle"), room, layer);
                case "instrument-service":
                    ServiceCables(New("Cable Service overhead"), New("Cable Service severed wall"));
                    break;
                case "observation-lab":
                case "observation-lab-broken-floor":
                case "gallery-chamber":
                    ObservationCables(New("Cable Observation overhead"), New("Cable Observation severed wall"));
                    break;
                case "shaft-plant":
                    ShaftCables(New("Cable Shaft overhead"), New("Cable Shaft severed wall"));
                    break;
            }
            return null;

            Batch New(string name)
            {
                var group = RaidAssembly.Child(room, name);
                group.gameObject.layer = layer;
                return new Batch(this, group);
            }
        }

        private static void CalibrationCables(Batch overhead, Batch broken)
        {
            Run(overhead, Finish.CableBlue, .084f, .14f,
                new(4.50f, 3.39f, -7.19f), new(4.50f, 3.93f, -7.19f), new(12.62f, 3.93f, -7.19f),
                new(12.62f, 3.93f, -1.65f), new(12.62f, 2.49f, -1.65f));
            Clips(overhead, Vector3.forward, new(5.20f, 3.93f, -7.19f), new(11.80f, 3.93f, -7.19f));
            Clips(overhead, Vector3.down, new(12.62f, 3.93f, -6.64f), new(12.62f, 3.93f, -2.30f));
            overhead.Box(Finish.Steel, 12.62f, 2.53f, -1.65f, .23f, .10f, .22f);
            overhead.FinishGeometry();

            Run(broken, Finish.Graphite, .092f, .18f,
                new(20.15f, 1.92f, -7.17f), new(20.15f, 3.28f, -7.17f), new(16.05f, 3.28f, -7.17f),
                new(15.60f, 3.17f, -7.12f), new(15.37f, 2.89f, -7.08f));
            Clips(broken, Vector3.forward, new(16.45f, 3.28f, -7.17f), new(19.70f, 3.28f, -7.17f));
            Fray(broken, new(15.37f, 2.89f, -7.08f), new(-.23f, -.28f, .04f), Vector3.forward);
            broken.FinishGeometry();
        }

        private Collider CalibrationFloorCable(Batch batch, Transform room, int layer)
        {
            const float x = 12.62f, start = -6.55f, end = -1.985f;
            Run(batch, Finish.CableBlue, .042f, 0, new(x - .031f, .022f, start), new(x - .031f, .022f, end));
            Run(batch, Finish.CableGreen, .031f, 0, new(x + .035f, .016f, start), new(x + .035f, .016f, -4.08f));
            Fray(batch, new(x + .035f, .015f, -4.08f), Vector3.forward, Vector3.up, flat: true);
            Run(batch, Finish.CableGreen, .031f, 0, new(x + .035f, .016f, end), new(x + .035f, .016f, -3.61f));
            Fray(batch, new(x + .035f, .015f, -3.61f), Vector3.back, Vector3.up, flat: true);
            // The low floor feed and stand boot stay inside the support's plateau.
            foreach (float z in new[] { start + .08f, end - .08f })
            {
                batch.Box(Finish.Graphite, x, .019f, z, .202f, .037f, .155f);
                batch.Box(Finish.Amber, x, .040f, z, .164f, .005f, .035f);
            }
            batch.FinishGeometry();

            // Five equal setbacks form shallow slopes on all four sides. The outer
            // perimeter lies under the finished floor so the seam has no exposed lip.
            float[] heights = { -.003f, .006f, .019f, .032f, .045f, .050f };
            var corners = new List<Vector3>(heights.Length * 4);
            var faces = new List<int>(126);
            for (int ring = 0; ring < heights.Length; ring++)
            {
                float margin = .166f * (heights.Length - 1 - ring);
                float half = .11f + margin;
                float y = heights[ring];
                corners.Add(new Vector3(x - half, y, start - margin));
                corners.Add(new Vector3(x - half, y, end + margin));
                corners.Add(new Vector3(x + half, y, end + margin));
                corners.Add(new Vector3(x + half, y, start - margin));
                if (ring == 0) continue;
                for (int edge = 0; edge < 4; edge++)
                {
                    int outer = 4 * (ring - 1) + edge;
                    int next = 4 * (ring - 1) + (edge + 1) % 4;
                    faces.AddRange(new[] { outer, next, outer + 4, next, next + 4, outer + 4 });
                }
            }
            int plateau = corners.Count - 4;
            faces.AddRange(new[] { plateau, plateau + 1, plateau + 2, plateau, plateau + 2, plateau + 3 });
            var mesh = new Mesh { name = "Calibration cable crossing support surface" };
            mesh.SetVertices(corners);
            mesh.SetTriangles(faces, 0);
            meshes.Add(mesh);
            var node = RaidAssembly.Child(room, "Cable Calibration floor support").gameObject;
            node.layer = layer;
            var collider = node.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            return collider;
        }

        private static void ServiceCables(Batch overhead, Batch broken)
        {
            // Run inside the ceiling beyond the full-height supply doorway's approach.
            const float supplyBypass = -3.40f;
            Run(overhead, Finish.CableBlue, .082f, .14f,
                new(1.35f, 2.32f, -3.98f), new(1.35f, 2.68f, -4.063f), new(1.35f, 3.92f, -4.063f),
                new(1.35f, 3.92f, supplyBypass), new(7.28f, 3.92f, supplyBypass),
                new(7.28f, 3.92f, 4.063f), new(7.28f, 1.15f, 4.063f),
                new(7.28f, 1.15f, 3.93f));
            Clips(overhead, Vector3.forward, new(1.35f, 2.75f, -4.063f), new(1.35f, 3.52f, -4.063f));
            Clips(overhead, Vector3.down, new(2.0f, 3.92f, supplyBypass), new(6.50f, 3.92f, supplyBypass));
            Clips(overhead, Vector3.down, new(7.28f, 3.92f, -3.2f), new(7.28f, 3.92f, 3.48f));
            Clips(overhead, Vector3.back, new(7.28f, 1.40f, 4.063f), new(7.28f, 3.43f, 4.063f));
            overhead.Box(Finish.Steel, 7.28f, 1.15f, 3.94f, .18f, .17f, .08f);
            overhead.FinishGeometry();

            Run(broken, Finish.Graphite, .076f, .15f,
                new(7.49f, 1.38f, -3.72f), new(7.49f, 1.71f, -4.053f), new(7.49f, 2.68f, -4.053f),
                new(8.47f, 2.68f, -4.053f), new(8.71f, 2.61f, -4.027f), new(8.75f, 2.41f, -3.98f));
            Clips(broken, Vector3.forward, new(7.49f, 1.89f, -4.053f), new(7.49f, 2.37f, -4.053f));
            Fray(broken, new(8.75f, 2.41f, -3.98f), new(.04f, -.20f, .047f), Vector3.forward);
            broken.FinishGeometry();
        }

        private static void ObservationCables(Batch overhead, Batch broken)
        {
            Run(overhead, Finish.CableBlue, .086f, .17f,
                new(4.90f, 1.14f, 6.92f), new(4.90f, 1.68f, 7.18f), new(4.90f, 7.37f, 7.18f),
                new(7.18f, 7.37f, 7.18f), new(7.18f, 7.37f, 1.08f), new(7.18f, 1.67f, 1.08f),
                new(7.12f, 1.67f, 1.08f));
            Clips(overhead, Vector3.back, new(4.90f, 1.84f, 7.18f), new(4.90f, 6.95f, 7.18f));
            Clips(overhead, Vector3.down, new(5.40f, 7.37f, 7.18f), new(6.84f, 7.37f, 7.18f));
            Clips(overhead, Vector3.down, new(7.18f, 7.37f, 1.65f), new(7.18f, 7.37f, 6.59f));
            overhead.Box(Finish.Steel, 7.18f, 1.69f, 1.08f, .17f, .17f, .17f);
            overhead.FinishGeometry();

            Run(broken, Finish.Graphite, .093f, .19f,
                new(1.85f, 1.69f, 7.17f), new(1.85f, 4.03f, 7.17f), new(3.21f, 4.03f, 7.17f),
                new(3.58f, 3.90f, 7.13f), new(3.74f, 3.64f, 7.07f));
            Clips(broken, Vector3.back, new(1.85f, 1.93f, 7.17f), new(1.85f, 3.55f, 7.17f));
            Fray(broken, new(3.74f, 3.64f, 7.07f), new(.16f, -.26f, -.06f), Vector3.back);
            broken.FinishGeometry();
        }

        private static void ShaftCables(Batch overhead, Batch broken)
        {
            Run(overhead, Finish.CableBlue, .112f, .20f,
                new(17.35f, 1.94f, 7.17f), new(17.35f, 11.10f, 7.17f), new(13.53f, 11.10f, 7.17f),
                new(13.53f, 11.10f, 4.44f), new(13.53f, 7.80f, 4.44f), new(13.53f, 7.80f, 4.24f));
            Clips(overhead, Vector3.back, new(17.35f, 2.22f, 7.17f), new(17.35f, 10.75f, 7.17f), .112f);
            Clips(overhead, Vector3.down, new(14.02f, 11.10f, 7.17f), new(16.79f, 11.10f, 7.17f), .112f);
            Clips(overhead, Vector3.down, new(13.53f, 11.10f, 4.81f), new(13.53f, 11.10f, 6.70f), .112f);
            overhead.Box(Finish.Steel, 13.53f, 7.8f, 4.25f, .24f, .23f, .20f);
            overhead.FinishGeometry();

            Run(broken, Finish.Graphite, .105f, .21f,
                new(17.82f, 1.97f, 7.17f), new(17.82f, 3.78f, 7.17f), new(20.86f, 3.78f, 7.17f),
                new(21.22f, 3.62f, 7.10f), new(21.28f, 3.32f, 7.00f));
            Clips(broken, Vector3.back, new(18.23f, 3.78f, 7.17f), new(20.36f, 3.78f, 7.17f), .105f);
            Fray(broken, new(21.28f, 3.32f, 7.00f), new(.06f, -.30f, -.10f), Vector3.back);
            broken.FinishGeometry();
        }

        private static void Clips(Batch batch, Vector3 normal, Vector3 start, Vector3 end, float diameter = .085f)
        {
            Vector3 direction = end - start;
            Quaternion rotation = Quaternion.LookRotation(direction, normal);
            int intervals = Mathf.Max(1, Mathf.CeilToInt(direction.magnitude / 1.45f));
            for (int i = 0; i <= intervals; i++)
            {
                Vector3 position = Vector3.Lerp(start, end, i / (float)intervals);
                batch.Box(Finish.Steel, position - normal * .017f,
                    new Vector3(diameter + .072f, diameter + .070f, .057f), rotation);
            }
        }

        private static void Fray(Batch batch, Vector3 cut, Vector3 direction, Vector3 normal, bool flat = false)
        {
            direction = direction.normalized;
            Vector3 side = Vector3.Cross(normal, direction).normalized;
            float length = flat ? .125f : .21f;
            float spread = flat ? .014f : .032f;
            for (int wire = 0; wire < WireColors.Length; wire++)
            {
                float fan = wire - 1.5f;
                var root = cut + side * (fan * spread * .29f);
                var elbow = root + direction * (length * .56f) + side * (fan * spread * .39f);
                var tip = cut + direction * (length + wire * .012f) + side * (fan * spread);
                var copper = tip + direction * (flat ? .028f : .047f) + side * (fan * spread * .12f);
                Run(batch, WireColors[wire], flat ? .010f : .022f, .025f, root, elbow, tip);
                Run(batch, Finish.Copper, flat ? .007f : .015f, 0, tip, copper);
            }
            // A short metallic band at the cut exposes the braided shield under the insulation.
            Run(batch, Finish.Edge, flat ? .028f : .068f, 0, cut - direction * .012f, cut + direction * .014f);
        }

        private static void Run(Batch batch, Finish finish, float diameter, float bend, params Vector3[] knots)
        {
            var path = new List<Vector3> { knots[0] };
            for (int i = 1; i < knots.Length - 1; i++)
            {
                Vector3 corner = knots[i];
                Vector3 incoming = knots[i - 1] - corner, outgoing = knots[i + 1] - corner;
                float radius = Mathf.Min(bend, incoming.magnitude * .45f, outgoing.magnitude * .45f);
                Vector3 entry = corner + incoming.normalized * radius;
                Vector3 exit = corner + outgoing.normalized * radius;
                path.Add(entry);
                for (int step = 1; step <= 4; step++)
                {
                    float t = step / 4f;
                    path.Add(Vector3.Lerp(Vector3.Lerp(entry, corner, t), Vector3.Lerp(corner, exit, t), t));
                }
            }
            path.Add(knots[^1]);
            const int sides = 8;
            var points = new List<Vector3>(path.Count * sides);
            var normals = new List<Vector3>(path.Count * sides);
            var faces = new List<int>((path.Count - 1) * sides * 6 + 36);
            Vector3 axis = (path[1] - path[0]).normalized;
            Vector3 tangent = Vector3.Cross(axis, Mathf.Abs(axis.y) > .9f ? Vector3.right : Vector3.up).normalized;
            for (int ring = 0; ring < path.Count; ring++)
            {
                Vector3 direction = (path[Mathf.Min(ring + 1, path.Count - 1)] - path[Mathf.Max(ring - 1, 0)]).normalized;
                tangent = Quaternion.FromToRotation(axis, direction) * tangent;
                axis = direction;
                Vector3 bitangent = Vector3.Cross(axis, tangent);
                for (int side = 0; side < sides; side++)
                {
                    float angle = side * (2 * Mathf.PI / sides);
                    Vector3 normal = Mathf.Cos(angle) * tangent + Mathf.Sin(angle) * bitangent;
                    points.Add(path[ring] + normal * (diameter / 2));
                    normals.Add(normal);
                    if (ring == 0) continue;
                    int previous = (ring - 1) * sides + side;
                    int next = (ring - 1) * sides + (side + 1) % sides;
                    faces.AddRange(new[] { previous, next, previous + sides, next, next + sides, previous + sides });
                }
            }
            int last = points.Count - sides;
            for (int side = 1; side < sides - 1; side++)
                faces.AddRange(new[] { 0, side + 1, side, last, last + side, last + side + 1 });
            var mesh = new Mesh { name = "Contour-4 cable " + finish };
            mesh.SetVertices(points);
            mesh.SetNormals(normals);
            mesh.SetTriangles(faces, 0);
            batch.AddCable(finish, mesh);
        }
    }
}

using System.Collections.Generic;
using System.Numerics;

namespace Contour4.Procedural.Catalog
{
    internal sealed class CurvedRoomMesh
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();

        internal int Add(Vector3 point)
        {
            vertices.Add(point);
            return vertices.Count - 1;
        }

        internal void Quad(int a, int b, int c, int d, Vector3 normal)
        {
            Triangle(a, b, c, normal);
            Triangle(a, c, d, normal);
        }

        private void Triangle(int a, int b, int c, Vector3 normal)
        {
            Vector3 cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (cross.LengthSquared() < 1e-12f) return;
            triangles.Add(a);
            bool clockwise = Vector3.Dot(cross, normal) < 0;
            triangles.Add(clockwise ? c : b);
            triangles.Add(clockwise ? b : c);
        }

        internal Block Build(string id, BlockRole role) => Block.FromMesh(id, role, vertices.ToArray(), triangles.ToArray());
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceJourney;

// A complementary pair of render meshes. The source asset is never modified.
internal sealed class PortalMeshSlice : IDisposable
{
    private const int MaxVertices = 12000;
    private const int MaxIndices = 24000;
    private readonly Vector3[] positions, normals;
    private readonly Vector4[] tangents;
    private readonly Color[] colors;
    private readonly List<Vector4>[] uvs = new List<Vector4>[8];
    private readonly int[][] triangles;
    private readonly Bounds bounds;
    private readonly Section near, far;
    private readonly float[] distances;

    internal Mesh Near => near.Mesh;
    internal Mesh Far => far.Mesh;
    internal int Work { get; }

    internal static bool SupportsShape(Mesh mesh)
    {
        if (!mesh || mesh.vertexCount == 0 || mesh.vertexCount > MaxVertices ||
            mesh.subMeshCount == 0 || mesh.subMeshCount > 32)
            return false;
        long count = 0;
        for (int index = 0; index < mesh.subMeshCount; index++)
        {
            if (mesh.GetTopology(index) != MeshTopology.Triangles)
                return false;
            count += mesh.GetIndexCount(index);
        }
        return count > 0 && count <= MaxIndices;
    }

    internal static PortalMeshSlice Create(Mesh mesh) =>
        mesh && mesh.isReadable && SupportsShape(mesh) ? new PortalMeshSlice(mesh) : null;

    private PortalMeshSlice(Mesh mesh)
    {
        bounds = mesh.bounds;
        positions = mesh.vertices;
        normals = mesh.normals;
        tangents = mesh.tangents;
        colors = mesh.colors;
        distances = new float[positions.Length];
        for (int channel = 0; channel < uvs.Length; channel++)
        {
            if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord0 + channel))
                continue;
            uvs[channel] = new List<Vector4>(positions.Length);
            mesh.GetUVs(channel, uvs[channel]);
        }
        triangles = new int[mesh.subMeshCount][];
        for (int index = 0; index < triangles.Length; index++)
        {
            triangles[index] = mesh.GetIndices(index);
            Work += triangles[index].Length;
        }
        near = new Section(this, "Contour-4 portal cargo near");
        far = new Section(this, "Contour-4 portal cargo far");
    }

    internal void Split(Vector4 plane)
    {
        near.Clear();
        far.Clear();
        var normal = new Vector3(plane.x, plane.y, plane.z);
        for (int index = 0; index < positions.Length; index++)
            distances[index] = Vector3.Dot(normal, positions[index]) + plane.w;

        Span<Vertex> polygon = stackalloc Vertex[4];
        for (int sub = 0; sub < triangles.Length; sub++)
        {
            int[] indices = triangles[sub];
            for (int index = 0; index < indices.Length; index += 3)
            {
                int a = indices[index], b = indices[index + 1], c = indices[index + 2];
                bool positive = distances[a] >= 0f;
                if (positive == (distances[b] >= 0f) && positive == (distances[c] >= 0f))
                {
                    polygon[0] = new Vertex(a, a, 0f);
                    polygon[1] = new Vertex(b, b, 0f);
                    polygon[2] = new Vertex(c, c, 0f);
                    (positive ? far : near).Append(sub, polygon[..3]);
                    continue;
                }
                int count = ClipTriangle(a, b, c, distances, false, polygon);
                if (count >= 3) near.Append(sub, polygon[..count]);
                count = ClipTriangle(a, b, c, distances, true, polygon);
                if (count >= 3) far.Append(sub, polygon[..count]);
            }
        }
        near.Publish(bounds);
        far.Publish(bounds);
    }

    private readonly struct Vertex(int from, int to, float amount)
    {
        internal readonly int From = from;
        internal readonly int To = to;
        internal readonly float Amount = amount;
    }

    private static int ClipTriangle(int a, int b, int c, float[] distances, bool positive, Span<Vertex> polygon)
    {
        Span<int> triangle = stackalloc int[3] { a, b, c };
        int count = 0;
        int previous = c;
        foreach (int current in triangle)
        {
            float from = distances[previous];
            float to = distances[current];
            bool previousInside = (from >= 0f) == positive;
            bool currentInside = (to >= 0f) == positive;
            if (previousInside != currentInside)
                polygon[count++] = new Vertex(previous, current, from / (from - to));
            if (currentInside)
                polygon[count++] = new Vertex(current, current, 0f);
            previous = current;
        }
        return count;
    }

    public void Dispose()
    {
        UnityEngine.Object.Destroy(near.Mesh);
        UnityEngine.Object.Destroy(far.Mesh);
    }

    private sealed class Section
    {
        private readonly PortalMeshSlice source;
        private readonly List<Vector3> positions = new();
        private readonly List<Vector3> normals = new();
        private readonly List<Vector4> tangents = new();
        private readonly List<Color> colors = new();
        private readonly List<Vector4>[] uvs = new List<Vector4>[8];
        private readonly List<int>[] indices;

        internal Mesh Mesh { get; }

        internal Section(PortalMeshSlice source, string name)
        {
            this.source = source;
            indices = new List<int>[source.triangles.Length];
            for (int sub = 0; sub < indices.Length; sub++)
                indices[sub] = new List<int>();
            for (int channel = 0; channel < uvs.Length; channel++)
                if (source.uvs[channel] != null)
                    uvs[channel] = new List<Vector4>();
            Mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            Mesh.MarkDynamic();
        }

        internal void Clear()
        {
            positions.Clear();
            normals.Clear();
            tangents.Clear();
            colors.Clear();
            foreach (var channel in uvs)
                channel?.Clear();
            foreach (var sub in indices)
                sub.Clear();
        }

        internal void Append(int sub, ReadOnlySpan<Vertex> polygon)
        {
            int start = positions.Count;
            foreach (var vertex in polygon)
            {
                int first = vertex.From, last = vertex.To;
                float amount = vertex.Amount;
                positions.Add(Vector3.LerpUnclamped(source.positions[first], source.positions[last], amount));
                if (source.normals.Length != 0)
                    normals.Add(Vector3.LerpUnclamped(source.normals[first], source.normals[last], amount));
                if (source.tangents.Length != 0)
                    tangents.Add(Vector4.LerpUnclamped(source.tangents[first], source.tangents[last], amount));
                if (source.colors.Length != 0)
                    colors.Add(Color.LerpUnclamped(source.colors[first], source.colors[last], amount));
                for (int channel = 0; channel < uvs.Length; channel++)
                    if (uvs[channel] != null)
                        uvs[channel].Add(Vector4.LerpUnclamped(source.uvs[channel][first], source.uvs[channel][last], amount));
            }

            for (int corner = 1; corner < polygon.Length - 1; corner++)
            {
                indices[sub].Add(start);
                indices[sub].Add(start + corner);
                indices[sub].Add(start + corner + 1);
            }
        }

        internal void Publish(Bounds bounds)
        {
            Mesh.Clear();
            Mesh.SetVertices(positions);
            if (normals.Count != 0) Mesh.SetNormals(normals);
            if (tangents.Count != 0) Mesh.SetTangents(tangents);
            if (colors.Count != 0) Mesh.SetColors(colors);
            for (int channel = 0; channel < uvs.Length; channel++)
                if (uvs[channel] != null)
                    Mesh.SetUVs(channel, uvs[channel]);
            Mesh.subMeshCount = indices.Length;
            for (int sub = 0; sub < indices.Length; sub++)
                Mesh.SetIndices(indices[sub], MeshTopology.Triangles, sub, calculateBounds: false);
            Mesh.bounds = bounds;
        }
    }
}

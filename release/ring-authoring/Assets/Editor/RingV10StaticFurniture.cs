using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

internal static class RingV10StaticFurniture
{
    private const string AssetFolder = "Assets/Contour4Ring/V10 furniture meshes";
    private static readonly HashSet<string> PreservedGroups = new()
        { "Model", "Markers", "Wall lights", "Catalog sites" };

    internal static int Bake(Transform root, IEnumerable<string> roomNames, TextWriter report)
    {
        if (!AssetDatabase.IsValidFolder(AssetFolder))
            AssetDatabase.CreateFolder("Assets/Contour4Ring", "V10 furniture meshes");

        int before = root.GetComponentsInChildren<MeshRenderer>(true).Length;
        int colliderCount = root.GetComponentsInChildren<Collider>(true).Length;
        int rigidbodyCount = root.GetComponentsInChildren<Rigidbody>(true).Length;
        int sourceCount = 0;
        int outputCount = 0;
        foreach (string name in roomNames)
        {
            var room = root.Find(name);
            var sources = room.Cast<Transform>()
                .Where(child => !PreservedGroups.Contains(child.name))
                .SelectMany(child => child.GetComponentsInChildren<MeshRenderer>())
                .Where(IsBatchable)
                .GroupBy(renderer => (renderer.sharedMaterial, renderer.shadowCastingMode, renderer.receiveShadows,
                    renderer.motionVectorGenerationMode, renderer.lightProbeUsage, renderer.reflectionProbeUsage))
                .Where(group => group.Count() > 1).ToArray();
            var output = new GameObject("Static furniture render meshes").transform;
            output.SetParent(room, false);
            int i = 0;
            foreach (var group in sources)
            {
                var renderers = group.ToArray();
                var sample = renderers[0];
                var mesh = Combine(room, renderers, name + " " + sample.sharedMaterial.name, $"{name}-{i++:00}");
                var node = new GameObject(mesh.name);
                node.transform.SetParent(output, false);
                node.AddComponent<MeshFilter>().sharedMesh = mesh;
                var combined = node.AddComponent<MeshRenderer>();
                combined.sharedMaterial = sample.sharedMaterial;
                combined.shadowCastingMode = sample.shadowCastingMode;
                combined.receiveShadows = sample.receiveShadows;
                combined.motionVectorGenerationMode = sample.motionVectorGenerationMode;
                combined.lightProbeUsage = sample.lightProbeUsage;
                combined.reflectionProbeUsage = sample.reflectionProbeUsage;
                foreach (var renderer in renderers)
                    Object.DestroyImmediate(renderer);
                sourceCount += renderers.Length;
                outputCount++;
            }
        }
        int after = root.GetComponentsInChildren<MeshRenderer>(true).Length;
        if (before - sourceCount + outputCount != after ||
            colliderCount != root.GetComponentsInChildren<Collider>(true).Length ||
            rigidbodyCount != root.GetComponentsInChildren<Rigidbody>(true).Length)
            throw new InvalidOperationException("Furniture export changed an unexpected renderer or physics component.");
        report.WriteLine($"Static opaque furniture: {sourceCount} renderers combined into {outputCount}; all MeshRenderers {before} → {after}.");
        report.WriteLine($"{colliderCount} colliders and {rigidbodyCount} rigidbodies retained. Structure, lights, marked sites and moving fronts excluded.");
        return after;
    }

    private static bool IsBatchable(MeshRenderer renderer)
    {
        if (!renderer.enabled || renderer.gameObject.layer != 0 || renderer.GetComponentInParent<Rigidbody>() ||
            renderer.probeAnchor || renderer.HasPropertyBlock() || renderer.lightmapIndex >= 0 ||
            renderer.sharedMaterials.Length != 1 || !renderer.sharedMaterial || renderer.sharedMaterial.renderQueue > 2500 ||
            renderer.sharedMaterial.GetTag("RenderType", false) != "Opaque" ||
            !renderer.TryGetComponent(out MeshFilter filter))
            return false;
        var mesh = filter.sharedMesh;
        return mesh && mesh.isReadable && mesh.subMeshCount == 1 && mesh.GetTopology(0) == MeshTopology.Triangles;
    }

    private static Mesh Combine(Transform room, IReadOnlyList<MeshRenderer> renderers, string name, string id)
    {
        var inputs = renderers.Select(renderer => new CombineInstance
        {
            mesh = renderer.GetComponent<MeshFilter>().sharedMesh,
            transform = room.worldToLocalMatrix * renderer.localToWorldMatrix
        }).ToArray();
        int vertexCount = inputs.Sum(item => item.mesh.vertexCount);
        int indexCount = inputs.Sum(item => (int)item.mesh.GetIndexCount(0));
        var mesh = new Mesh { name = name, indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.CombineMeshes(inputs);
        if (mesh.vertexCount != vertexCount || mesh.GetIndexCount(0) != indexCount)
            throw new InvalidOperationException($"Merged mesh topology changed for {name}.");
        var combined = mesh.vertices;
        int index = 0;
        foreach (var input in inputs)
        foreach (var point in input.mesh.vertices)
            if ((input.transform.MultiplyPoint3x4(point) - combined[index++]).sqrMagnitude > 1e-9f)
                throw new InvalidOperationException($"Merged mesh vertices changed for {name}.");

        string path = AssetFolder + "/" + id + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!existing)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        EditorUtility.CopySerialized(mesh, existing);
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(mesh);
        return existing;
    }
}

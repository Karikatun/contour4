using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal sealed class ArtCompositionAudit
{
    private readonly Dictionary<GameObject, Node> baseline;
    private readonly int colliderCount;

    internal ArtCompositionAudit(GameObject root)
    {
        baseline = root.GetComponentsInChildren<Transform>(true)
            .ToDictionary(transform => transform.gameObject, transform => new Node(transform));
        colliderCount = root.GetComponentsInChildren<Collider>(true).Length;
    }

    internal void Verify(GameObject root, string id)
    {
        var all = root.GetComponentsInChildren<Transform>(true).Select(transform => transform.gameObject).ToArray();
        if (baseline.Keys.Any(gameObject => !gameObject) || root.GetComponentsInChildren<Collider>(true).Length != colliderCount)
            throw new InvalidOperationException($"Art composition changed the structure or collider count for {id}.");

        int changedMaterials = 0;
        int addedRenderers = 0;
        foreach (GameObject gameObject in all)
        {
            if (baseline.TryGetValue(gameObject, out Node node))
            {
                changedMaterials += node.Verify(gameObject, id);
                continue;
            }

            if (gameObject.GetComponents<Component>().Any(component =>
                    !(component is Transform || component is MeshFilter || component is MeshRenderer)))
                throw new InvalidOperationException($"Art added a gameplay component to {id}/{gameObject.name}.");
            if (gameObject.TryGetComponent(out MeshRenderer renderer))
            {
                if (!renderer.sharedMaterial || !gameObject.TryGetComponent(out MeshFilter mesh) || !mesh.sharedMesh)
                    throw new InvalidOperationException($"Art has a missing material or mesh in {id}/{gameObject.name}.");
                addedRenderers++;
            }
        }

        if (changedMaterials == 0 && addedRenderers == 0)
            throw new InvalidOperationException($"No visible art was applied to {id}.");
        Debug.Log($"CONTOUR4 ART overlay {id}: preservedNodes={baseline.Count}; colliders={colliderCount}; " +
            $"restyledRenderers={changedMaterials}; addedRenderers={addedRenderers}.");
    }

    private sealed class Node
    {
        private readonly Transform parent;
        private readonly Vector3 position;
        private readonly Quaternion rotation;
        private readonly Vector3 scale;
        private readonly bool active;
        private readonly int layer;
        private readonly string name;
        private readonly string tag;
        private readonly Component[] components;
        private readonly string[] protectedComponents;
        private readonly Material[] materials;

        internal Node(Transform transform)
        {
            var gameObject = transform.gameObject;
            parent = transform.parent;
            position = transform.localPosition;
            rotation = transform.localRotation;
            scale = transform.localScale;
            active = gameObject.activeSelf;
            layer = gameObject.layer;
            name = gameObject.name;
            tag = gameObject.tag;
            components = gameObject.GetComponents<Component>();
            protectedComponents = components.Where(IsGameplayComponent).Select(component => EditorJsonUtility.ToJson(component)).ToArray();
            materials = gameObject.TryGetComponent(out Renderer renderer) ? renderer.sharedMaterials : Array.Empty<Material>();
        }

        internal int Verify(GameObject gameObject, string id)
        {
            var transform = gameObject.transform;
            if (!ReferenceEquals(transform.parent, parent) || !transform.localPosition.Equals(position) ||
                !transform.localRotation.Equals(rotation) || !transform.localScale.Equals(scale) ||
                active != gameObject.activeSelf || layer != gameObject.layer || name != gameObject.name || tag != gameObject.tag ||
                !components.SequenceEqual(gameObject.GetComponents<Component>()) ||
                !protectedComponents.SequenceEqual(components.Where(IsGameplayComponent).Select(component => EditorJsonUtility.ToJson(component))))
                throw new InvalidOperationException($"Art changed the gameplay state of {id}/{name}.");

            return gameObject.TryGetComponent(out Renderer renderer) && !materials.SequenceEqual(renderer.sharedMaterials) ? 1 : 0;
        }

        private static bool IsGameplayComponent(Component component) =>
            component && !(component is Transform || component is Renderer);
    }
}

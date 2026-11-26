using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural;
using UnityEngine;
using UnityEngine.Rendering;

namespace Contour4.NativeRaid;

internal sealed class RaidAuthoredLoot : MonoBehaviour
{
    internal RaidLootPlacement Placement { get; set; }
    internal LootSurfaceKind Kind { get; set; }
    internal int RoomIndex { get; set; }
    internal bool RingFloor { get; set; }
}

internal static class RaidLootPrefab
{
    private static readonly ConditionalWeakTable<GameObject, Footprint> Footprints = new();

    internal static bool Fits(GameObject prefab, RaidLootPlacement placement, Pose pose) =>
        prefab && Footprints.GetValue(prefab, Build).Fits(placement, pose);

    private static Footprint Build(GameObject prefab)
    {
        var shapes = new List<Shape>();
        Transform root = prefab.transform;
        Matrix4x4 toSpawn = Matrix4x4.Scale(root.localScale) * root.worldToLocalMatrix;
        foreach (var collider in prefab.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.enabled || collider.isTrigger || !ActiveWithin(root, collider.transform)) continue;
            Matrix4x4 frame = toSpawn * collider.transform.localToWorldMatrix;
            switch (collider)
            {
                case BoxCollider box:
                    shapes.Add(Shape.Box(frame, new Bounds(box.center, box.size)));
                    break;
                case SphereCollider sphere:
                    shapes.Add(Shape.Round(frame, sphere.center, sphere.radius, 0, -1));
                    break;
                case CapsuleCollider capsule:
                    shapes.Add(Shape.Round(frame, capsule.center, capsule.radius, capsule.height, capsule.direction));
                    break;
                case MeshCollider mesh when mesh.sharedMesh:
                    shapes.Add(Shape.Box(frame, mesh.sharedMesh.bounds));
                    break;
                default:
                    return new Footprint(new List<Shape>(), new List<Shape>());
            }
        }

        var rendered = new List<Shape>();
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || renderer.forceRenderingOff ||
                renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly ||
                !ActiveWithin(root, renderer.transform))
                continue;
            bool hasMesh = renderer switch
            {
                SkinnedMeshRenderer skinned => skinned.sharedMesh,
                MeshRenderer mesh => mesh.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh,
                _ => false
            };
            if (hasMesh)
                rendered.Add(Shape.Box(toSpawn * renderer.transform.localToWorldMatrix, renderer.localBounds));
        }
        return new Footprint(shapes, rendered);
    }

    private static bool ActiveWithin(Transform root, Transform node)
    {
        for (; node != root; node = node.parent)
            if (!node.gameObject.activeSelf) return false;
        return true;
    }

    private sealed class Footprint
    {
        private readonly List<Shape> shapes, rendered;
        private readonly Bounds occupied;
        private readonly Bounds visibleAndSolid;

        internal Footprint(List<Shape> shapes, List<Shape> rendered)
        {
            this.shapes = shapes;
            this.rendered = rendered;
            // Solid colliders determine physical support. Render-only cosmetics use
            // their mesh footprint to locate the support, too.
            var contact = shapes.Count > 0 ? shapes : rendered;
            if (contact.Count == 0) return;
            occupied = contact[0].Bounds;
            foreach (var shape in contact) occupied.Encapsulate(shape.Bounds);
            visibleAndSolid = occupied;
            foreach (var shape in rendered) visibleAndSolid.Encapsulate(shape.Bounds);
        }

        internal bool Fits(RaidLootPlacement placement, Pose pose)
        {
            if (shapes.Count == 0 && rendered.Count == 0) return false;
            Vector3 bottom = new(occupied.center.x, occupied.min.y, occupied.center.z);
            return placement.Supports(pose.position + pose.rotation * bottom, pose.up) &&
                Clear(shapes) && Clear(rendered) &&
                (!placement.EquipmentPocket || shapes.Count > 0 && placement.AllowsRetrieval(
                    pose.position + pose.rotation * visibleAndSolid.center, pose.rotation, visibleAndSolid.extents));

            bool Clear(List<Shape> geometry)
            {
                foreach (var shape in geometry)
                {
                    Vector3 center = pose.position + pose.rotation * shape.Center;
                    Quaternion rotation = pose.rotation * shape.Rotation;
                    if (!placement.Contains(center, rotation, shape.Half) || shape.Intersects(center, rotation))
                        return false;
                }
                return true;
            }
        }
    }

    private readonly struct Shape
    {
        private const float Skin = .002f;
        internal readonly Vector3 Center, Half;
        internal readonly Quaternion Rotation;
        private readonly float radius, segment;

        private Shape(Vector3 center, Quaternion rotation, Vector3 half, float radius = 0, float segment = 0)
        {
            Center = center;
            Rotation = rotation;
            Half = half;
            this.radius = radius;
            this.segment = segment;
        }

        internal Bounds Bounds
        {
            get
            {
                var half = Abs(Rotation * Vector3.right) * Half.x +
                    Abs(Rotation * Vector3.up) * Half.y + Abs(Rotation * Vector3.forward) * Half.z;
                return new Bounds(Center, half * 2);
            }
        }

        internal static Shape Box(Matrix4x4 frame, Bounds bounds)
        {
            Vector3 x = frame.MultiplyVector(Vector3.right), y = frame.MultiplyVector(Vector3.up);
            Vector3 z = frame.MultiplyVector(Vector3.forward);
            Vector3 center = frame.MultiplyPoint3x4(bounds.center);
            if (Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > .001f ||
                Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) > .001f ||
                Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) > .001f)
                return new Shape(center, Quaternion.identity,
                    Abs(x) * bounds.extents.x + Abs(y) * bounds.extents.y + Abs(z) * bounds.extents.z);
            return new Shape(center, Quaternion.LookRotation(z, y),
                Vector3.Scale(bounds.extents, new Vector3(x.magnitude, y.magnitude, z.magnitude)));
        }

        internal static Shape Round(Matrix4x4 frame, Vector3 local, float radius, float height, int direction)
        {
            Vector3 center = frame.MultiplyPoint3x4(local);
            Vector3 x = frame.MultiplyVector(Vector3.right);
            Vector3 y = frame.MultiplyVector(Vector3.up);
            Vector3 z = frame.MultiplyVector(Vector3.forward);
            float scale = direction switch
            {
                0 => Mathf.Max(y.magnitude, z.magnitude),
                1 => Mathf.Max(x.magnitude, z.magnitude),
                2 => Mathf.Max(x.magnitude, y.magnitude),
                _ => Mathf.Max(x.magnitude, y.magnitude, z.magnitude)
            };
            Vector3 axis = direction switch { 0 => x, 1 => y, 2 => z, _ => Vector3.up };
            float scaledRadius = radius * scale;
            float segment = Mathf.Max(0, height * axis.magnitude / 2 - scaledRadius);
            return new Shape(center, Quaternion.FromToRotation(Vector3.up, axis),
                Vector3.one * scaledRadius + Vector3.up * segment, scaledRadius, segment);
        }

        internal bool Intersects(Vector3 center, Quaternion rotation)
        {
            if (radius > 0 && segment > 0)
            {
                Vector3 offset = rotation * Vector3.up * segment;
                return Physics.CheckCapsule(center - offset, center + offset, Mathf.Max(.001f, radius - Skin), ~0,
                    QueryTriggerInteraction.Ignore);
            }
            if (radius > 0) return Physics.CheckSphere(center, Mathf.Max(.001f, radius - Skin), ~0,
                QueryTriggerInteraction.Ignore);
            return Physics.CheckBox(center, Vector3.Max(Vector3.one * .001f, Half - Vector3.one * Skin),
                rotation, ~0, QueryTriggerInteraction.Ignore);
        }

        private static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}

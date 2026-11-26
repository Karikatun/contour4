using System;
using System.Collections.Generic;
using Contour4.NativeRaid;
using Contour4.NativeRaid.Runtime;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceJourney;

internal sealed class PortalCargoPresentation
{
    private const float NearPlanePadding = .45f;
    private const float HandWarmupDepth = 1.8f;
    // Match PortalCargoDoor.Fits: stock cargo can rest slightly below the nominal sill.
    private const float SillAllowance = .12f;
    private const int MaxVisiblePairs = 4;
    private const int MaxActorsPerFrame = 6;
    private const int MaxVisualParts = 16;
    private const int MaxIndicesPerFrame = 24000;
    private static readonly AccessTools.FieldRef<PhysGrabber, PhysGrabObject> HeldObject =
        AccessTools.FieldRefAccess<PhysGrabber, PhysGrabObject>("grabbedPhysGrabObject");

    private readonly List<(NativeRaidPortal Near, NativeRaidPortal Far)> doors = new();
    private readonly List<(NativeRaidPortal Near, NativeRaidPortal Far, float Score)> visible = new(MaxVisiblePairs);
    private readonly HashSet<Transform> queried = new();
    private readonly Dictionary<PhysGrabObject, (NativeRaidPortal Door, float Distance)> candidates = new();
    private readonly List<(PhysGrabObject Item, NativeRaidPortal Door, float Distance)> nearest = new();
    private readonly Dictionary<PhysGrabObject, Cargo> visuals = new();
    private readonly List<PhysGrabObject> expired = new();
    private readonly Plane[] frustum = new Plane[6];
    private readonly Collider[] nearby = new Collider[48];
    private readonly int cargoMask = PortalCargoDoor.ActorMask;
    private readonly Scene scene;
    private int frame = -1;

    internal PortalCargoPresentation(Scene scene, IReadOnlyList<NativeRaidPortal> portals)
    {
        this.scene = scene;
        foreach (var door in portals)
        {
            if (!door.Door) continue;
            foreach (var reverse in portals)
            {
                if (!reverse.Door || door.Entrance != reverse.Exit || door.Exit != reverse.Entrance)
                    continue;
                doors.Add((door, reverse));
                break;
            }
        }
    }

    internal void Refresh(Camera camera)
    {
        if (frame == Time.frameCount)
            return;
        frame = Time.frameCount;
        Vector3 eye = camera.transform.position;
        GeometryUtility.CalculateFrustumPlanes(camera, frustum);
        var grabber = PhysGrabber.instance;
        var held = grabber && grabber.isLocal && grabber.grabbed ? HeldObject(grabber) : null;
        if (held && (!held.rb || held.gameObject.scene != scene))
            held = null;
        visible.Clear();
        queried.Clear();
        candidates.Clear();
        foreach (var (near, far) in doors)
        {
            if (!near.Entrance || !far.Entrance)
                continue;
            var mouth = near.Entrance;
            float depth = Vector3.Dot(eye - mouth.position, mouth.forward);
            if (depth > .2f || !GeometryUtility.TestPlanesAABB(frustum, Aperture(near)))
                continue;

            bool nearHand = held && InWarmupReach(held.rb.worldCenterOfMass, near);
            bool farHand = held && InWarmupReach(held.rb.worldCenterOfMass, far);
            if (nearHand) Consider(held, near);
            if (farHand) Consider(held, far);
            float score = nearHand || farHand ? -1f : (eye - mouth.position).sqrMagnitude;
            int index = 0;
            while (index < visible.Count && visible[index].Score <= score) index++;
            if (index >= MaxVisiblePairs) continue;
            if (visible.Count == MaxVisiblePairs) visible.RemoveAt(MaxVisiblePairs - 1);
            visible.Insert(index, (near, far, score));
        }
        foreach (var (near, far, _) in visible)
        {
            if (queried.Add(near.Entrance)) FindCargo(near);
            if (queried.Add(far.Entrance)) FindCargo(far);
        }

        int copyBudget = 3;
        int readbackBytes = PortalMeshReadback.MeshByteBudget;
        int indexBudget = MaxIndicesPerFrame;
        int actorBudget = MaxActorsPerFrame;
        if (held && candidates.TryGetValue(held, out var handRoute))
        {
            UpdateCargo(held, handRoute.Door, ref copyBudget, ref readbackBytes, ref indexBudget);
            actorBudget--;
        }
        nearest.Clear();
        foreach (var (item, route) in candidates)
            if (item != held)
                nearest.Add((item, route.Door, route.Distance));
        nearest.Sort(static (first, second) => first.Distance.CompareTo(second.Distance));
        for (int index = 0; index < actorBudget && index < nearest.Count; index++)
        {
            var (item, door, _) = nearest[index];
            UpdateCargo(item, door, ref copyBudget, ref readbackBytes, ref indexBudget);
        }

        expired.Clear();
        foreach (var (item, cargo) in visuals)
        {
            if (cargo.LastSeen == frame) continue;
            cargo.Hide();
            if (!item || frame - cargo.LastSeen > 90)
                expired.Add(item);
        }
        foreach (var item in expired)
        {
            visuals[item].Dispose();
            visuals.Remove(item);
        }
    }

    internal void Clear()
    {
        foreach (var cargo in visuals.Values) cargo.Dispose();
        visuals.Clear();
        candidates.Clear();
        visible.Clear();
        queried.Clear();
        nearest.Clear();
        expired.Clear();
        Array.Clear(nearby, 0, nearby.Length);
        frame = -1;
    }

    private static Bounds Aperture(NativeRaidPortal portal)
    {
        var mouth = portal.Entrance;
        Vector3 extents = Magnitudes(mouth.right * portal.HalfWidth) +
            Magnitudes(mouth.up * portal.HalfHeight) + Magnitudes(mouth.forward * NearPlanePadding);
        return new Bounds(mouth.position, extents * 2f);
    }

    private static Vector3 Magnitudes(Vector3 value) =>
        new(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

    private static bool InWarmupReach(Vector3 center, NativeRaidPortal portal)
    {
        Vector3 local = portal.Entrance.InverseTransformPoint(center);
        return Mathf.Abs(local.z) <= HandWarmupDepth && Mathf.Abs(local.x) <= portal.HalfWidth + .25f &&
            Mathf.Abs(local.y) <= portal.HalfHeight + .25f;
    }

    private void UpdateCargo(PhysGrabObject item, NativeRaidPortal door, ref int copies, ref int bytes, ref int indices)
    {
        if (!visuals.TryGetValue(item, out Cargo cargo))
            visuals.Add(item, cargo = new Cargo(item));
        cargo.Update(door, frame, ref copies, ref bytes, ref indices);
    }

    private void FindCargo(NativeRaidPortal portal)
    {
        Transform mouth = portal.Entrance;
        var extents = new Vector3(portal.HalfWidth, portal.HalfHeight, NearPlanePadding);
        int count = Physics.OverlapBoxNonAlloc(mouth.position, extents, nearby, mouth.rotation,
            cargoMask, QueryTriggerInteraction.Ignore);
        for (int index = 0; index < count; index++)
        {
            var body = nearby[index].attachedRigidbody;
            if (!body || body.gameObject.scene != scene || !PortalCargoDoor.TryActor(body, out var item))
                continue;
            Consider(item, portal);
        }
    }

    private void Consider(PhysGrabObject item, NativeRaidPortal portal)
    {
        Transform mouth = portal.Entrance;
        float distance = Mathf.Abs(Vector3.Dot(item.rb.worldCenterOfMass - mouth.position, mouth.forward));
        if (!candidates.TryGetValue(item, out var previous) || distance < previous.Distance)
            candidates[item] = (portal, distance);
    }

    private sealed class Cargo : IDisposable
    {
        private readonly List<Part> visualParts = new();
        internal int LastSeen { get; private set; }

        internal Cargo(PhysGrabObject item)
        {
            if (item.GetComponentInChildren<SkinnedMeshRenderer>(true) || item.GetComponentInChildren<LODGroup>(true))
                return;
            foreach (var renderer in item.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
            {
                if (renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh && !renderer.isPartOfStaticBatch)
                    visualParts.Add(new Part(renderer, filter));
                if (visualParts.Count <= MaxVisualParts && !renderer.isPartOfStaticBatch) continue;
                visualParts.Clear();
                return;
            }
        }

        internal void Update(NativeRaidPortal portal, int frame, ref int copies, ref int bytes, ref int indices)
        {
            LastSeen = frame;
            bool hasBounds = false;
            Bounds footprint = default;
            var mouth = portal.Entrance;
            foreach (var part in visualParts)
            {
                if (!part.IsVisible) continue;
                part.Prepare(ref copies, ref bytes);
                if (!part.TryGetBounds(mouth, out var bounds)) continue;
                if (hasBounds) footprint.Encapsulate(bounds);
                else footprint = bounds;
                hasBounds = true;
            }
            // A conservative footprint fits wholly inside the shared mouth. Wide/blocked props
            // retain stock rendering instead of projecting a fragment around the jamb.
            if (!hasBounds || footprint.min.z >= 0f || footprint.max.z <= 0f ||
                !FitsPortal(footprint, portal.HalfWidth, portal.HalfHeight))
            {
                Hide();
                return;
            }

            foreach (var part in visualParts)
            {
                if (!part.IsVisible)
                {
                    part.Hide();
                    continue;
                }
                if (part.Draw(portal, ref indices)) continue;
                // A complete stock draw is safer than hiding a portion of an unsupported mesh.
                Hide();
                return;
            }
        }

        internal void Hide()
        {
            foreach (var part in visualParts) part.Hide();
        }

        public void Dispose()
        {
            foreach (var part in visualParts) part.Dispose();
        }
    }

    internal static bool FitsPortal(Bounds footprint, float halfWidth, float halfHeight) =>
        Mathf.Abs(footprint.center.x) + footprint.extents.x <= halfWidth &&
        footprint.max.y <= halfHeight && footprint.min.y >= -halfHeight - SillAllowance;

    private sealed class Part(MeshRenderer renderer, MeshFilter filter) : IDisposable
    {
        private readonly List<Material> materials = new();
        private readonly List<Material> proxyMaterials = new();
        private MaterialPropertyBlock properties;
        private Mesh original;
        private MeshRenderer proxy;
        private MeshFilter proxyFilter;
        private PortalMeshSlice slice;
        private PortalMeshReadback readback;
        private Vector4 plane;
        private bool forceHidden;
        private bool sampled;

        internal bool IsVisible => renderer && filter && renderer.enabled && renderer.gameObject.activeInHierarchy &&
            (!renderer.forceRenderingOff || forceHidden);

        internal bool TryGetBounds(Transform mouth, out Bounds bounds)
        {
            bounds = default;
            if (!original) return false;
            Bounds local = original.bounds;
            Matrix4x4 matrix = mouth.worldToLocalMatrix * renderer.localToWorldMatrix;
            Vector3 extent = local.extents;
            Vector3 targetExtent = Magnitudes(matrix.MultiplyVector(Vector3.right)) * extent.x +
                Magnitudes(matrix.MultiplyVector(Vector3.up)) * extent.y +
                Magnitudes(matrix.MultiplyVector(Vector3.forward)) * extent.z;
            bounds = new Bounds(matrix.MultiplyPoint3x4(local.center), targetExtent * 2f);
            return true;
        }

        internal void Prepare(ref int copies, ref int bytes)
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh != original && (slice == null || mesh != slice.Near))
            {
                slice?.Dispose();
                readback?.Dispose();
                original = mesh;
                slice = null;
                readback = null;
                sampled = false;
            }
            if (!original || sampled || original.isReadable)
                return;
            if (readback == null && PortalMeshReadback.CanStart(original))
            {
                readback = PortalMeshReadback.Start(original);
                sampled = readback == null;
            }
            if (readback?.Ready != true || copies <= 0 || readback.ByteCount > bytes)
                return;
            copies--;
            bytes -= readback.ByteCount;
            slice = readback.CreateSlice();
            readback.Dispose();
            readback = null;
            sampled = true;
        }

        internal bool Draw(NativeRaidPortal portal, ref int indices)
        {
            Transform mouth = portal.Entrance;
            if (!TryGetBounds(mouth, out Bounds bounds) || bounds.max.z <= 0f)
            {
                Hide();
                return true;
            }

            if (bounds.min.z >= 0f)
            {
                RestoreMesh();
                renderer.forceRenderingOff = forceHidden = true;
                ShowProxy(original, portal);
                return true;
            }

            if (!sampled && original.isReadable)
            {
                slice = PortalMeshSlice.Create(original);
                sampled = true;
            }
            if (slice == null)
                return false;

            Matrix4x4 localToWorld = renderer.localToWorldMatrix;
            Vector3 normal = mouth.forward;
            Vector3 axis = localToWorld.transpose.MultiplyVector(normal);
            float offset = Vector3.Dot(localToWorld.MultiplyPoint3x4(Vector3.zero) - mouth.position, normal);
            var nextPlane = new Vector4(axis.x, axis.y, axis.z, offset);
            if (filter.sharedMesh != slice.Near || (plane - nextPlane).sqrMagnitude > 1e-10f)
            {
                if (slice.Work > indices)
                    return false;
                indices -= slice.Work;
                slice.Split(nextPlane);
                plane = nextPlane;
            }
            RestoreVisibility();
            filter.sharedMesh = slice.Near;
            ShowProxy(slice.Far, portal);
            return true;
        }

        private void ShowProxy(Mesh mesh, NativeRaidPortal portal)
        {
            if (!proxy)
            {
                var helper = new GameObject("Contour-4 portal cargo image") { hideFlags = HideFlags.DontSave };
                SceneManager.MoveGameObjectToScene(helper, renderer.gameObject.scene);
                proxyFilter = helper.AddComponent<MeshFilter>();
                proxy = helper.AddComponent<MeshRenderer>();
                proxy.lightProbeUsage = renderer.lightProbeUsage;
                proxy.reflectionProbeUsage = renderer.reflectionProbeUsage;
            }

            var source = renderer.transform;
            Transform target = proxy.transform;
            target.SetPositionAndRotation(portal.MapPoint(source.position), portal.Rotation * source.rotation);
            target.localScale = source.lossyScale;
            proxy.gameObject.layer = renderer.gameObject.layer;
            proxy.renderingLayerMask = renderer.renderingLayerMask;
            proxy.shadowCastingMode = renderer.shadowCastingMode;
            proxy.receiveShadows = renderer.receiveShadows;
            proxy.sortingLayerID = renderer.sortingLayerID;
            proxy.sortingOrder = renderer.sortingOrder;
            renderer.GetSharedMaterials(materials);
            if (!SameMaterials())
            {
                proxy.SetSharedMaterials(materials);
                proxyMaterials.Clear();
                proxyMaterials.AddRange(materials);
            }
            if (renderer.HasPropertyBlock() || proxy.HasPropertyBlock())
            {
                properties ??= new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                proxy.SetPropertyBlock(properties.isEmpty ? null : properties);
                for (int index = 0; index < materials.Count; index++)
                {
                    renderer.GetPropertyBlock(properties, index);
                    proxy.SetPropertyBlock(properties.isEmpty ? null : properties, index);
                }
            }
            proxyFilter.sharedMesh = mesh;
            proxy.enabled = true;
        }

        private bool SameMaterials()
        {
            if (materials.Count != proxyMaterials.Count)
                return false;
            for (int index = 0; index < materials.Count; index++)
                if (materials[index] != proxyMaterials[index])
                    return false;
            return true;
        }

        internal void Hide()
        {
            RestoreMesh();
            RestoreVisibility();
            if (proxy) proxy.enabled = false;
        }

        private void RestoreMesh()
        {
            if (filter && slice != null && filter.sharedMesh == slice.Near)
                filter.sharedMesh = original;
        }

        private void RestoreVisibility()
        {
            if (!forceHidden) return;
            if (renderer) renderer.forceRenderingOff = false;
            forceHidden = false;
        }

        public void Dispose()
        {
            Hide();
            slice?.Dispose();
            readback?.Dispose();
            if (proxy) UnityEngine.Object.Destroy(proxy.gameObject);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Contour4.Procedural.Catalog
{
    internal readonly struct PreparedNavProfile
    {
        internal readonly string Id;
        internal readonly float Radius;
        internal readonly float Height;
        internal readonly float Climb;
        internal readonly float Slope;

        internal PreparedNavProfile(string id, float radius, float height, float climb, float slope)
        {
            Id = id;
            Radius = radius;
            Height = height;
            Climb = climb;
            Slope = slope;
        }
    }

    // Bakes the actual exported colliders after rotating one family of floor normals to Unity up.
    // A temporary offset leaves the authoring scene and any unrelated loaded NavMesh alone.
    internal sealed class PreparedNavBake : IDisposable
    {
        private const int WalkArea = 31;
        private const int BlockedArea = 1;
        private const float Snap = .20f;
        private readonly Vector3 queryOffset;
        private readonly Matrix4x4 worldToBake;
        private readonly Matrix4x4 bakeToWorld;
        private readonly NavMeshData data;
        private NavMeshDataInstance instance;
        private readonly NavMeshQueryFilter filter;
        private readonly MeshCollider[] solids;
        private readonly HashSet<MeshCollider> support;
        private readonly PreparedNavProfile profile;
        private readonly Collider[] contacts = new Collider[256];
        private readonly UnityEngine.PhysicsScene physics;
        internal readonly Vector3 Up;

        internal PreparedNavBake(Transform root, Vector3 up, PreparedNavProfile profile, float slot = 0)
        {
            Up = up;
            this.profile = profile;
            queryOffset = new Vector3(256 + slot, 0, 256);
            physics = root.gameObject.scene.GetPhysicsScene();
            var rotation = Quaternion.FromToRotation(Vector3.up, up);
            worldToBake = Matrix4x4.TRS(root.position, rotation, Vector3.one).inverse;
            bakeToWorld = worldToBake.inverse;
            solids = root.GetComponentsInChildren<MeshCollider>()
                .Where(collider => collider.enabled && !collider.isTrigger && collider.sharedMesh).ToArray();
            support = root.Find("Geometry/Floors").GetComponentsInChildren<MeshCollider>().ToHashSet();
            if(root.name.StartsWith(MobiusHall.Id) && root.Find("Geometry/Shell/roof")) support.Add(root.Find("Geometry/Shell/roof").GetComponent<MeshCollider>());
            if (support.Count == 0 || solids.Length == 0)
                throw new InvalidOperationException($"{root.name} has no emitted collider floors or solids.");

            var bounds = new Bounds(Vector3.zero, Vector3.one);
            var sources = new List<NavMeshBuildSource>(solids.Length);
            foreach (var collider in solids)
            {
                var transform = worldToBake * collider.transform.localToWorldMatrix;
                var mesh = collider.sharedMesh;
                foreach (var vertex in mesh.vertices)
                    bounds.Encapsulate(transform.MultiplyPoint3x4(vertex));
                sources.Add(new NavMeshBuildSource
                {
                    component = collider,
                    sourceObject = mesh,
                    shape = NavMeshBuildSourceShape.Mesh,
                    area = support.Contains(collider) ? WalkArea : BlockedArea,
                    transform = transform
                });
            }

            // A standalone geometric bake. The offline dimensions are deliberately the full
            // Headman body envelope, even though the live stock agent has smaller serialized values.
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = profile.Radius;
            settings.agentHeight = profile.Height;
            settings.agentClimb = profile.Climb;
            settings.agentSlope = profile.Slope;
            settings.minRegionArea = .12f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = .075f;
            bounds.Expand(2 * profile.Height);
            filter = new NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = 1 << WalkArea };
            data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (!data) throw new InvalidOperationException($"{root.name}: {profile.Id} bake failed.");
            instance = NavMesh.AddNavMeshData(data, queryOffset, Quaternion.identity);
            if (!instance.valid)
            {
                UnityEngine.Object.DestroyImmediate(data);
                throw new InvalidOperationException($"{root.name}: {profile.Id} NavMesh registration failed.");
            }
        }

        internal bool Matches(Vector3 up) => Vector3.Dot(Up, up) > .9999f;

        internal bool TryPoint(Vector3 requested, out Vector3 point, out Vector3 query)
        {
            point = default;
            query = default;
            if (!TryFoot(requested, out Vector3 pad) ||
                !NavMesh.SamplePosition(ToQuery(pad), out var hit, Snap, filter)) return false;
            var snapped = ToWorld(hit.position);
            // Validate the snapped XZ again. A nearby polygon on the other side of a rim, at
            // a different height or above a solid prop is never an acceptable node.
            if (!TryFoot(snapped, out point) || Vector3.Distance(point, pad) > Snap || !BodyClear(point))
                return false;
            query = hit.position;
            return true;
        }

        private bool TryFoot(Vector3 requested, out Vector3 point)
        {
            point = default;
            var ray = new Ray(requested + Up * Snap, -Up);
            float nearest = 2 * Snap;
            foreach (var foot in support)
            {
                if (!foot.Raycast(ray, out RaycastHit hit, nearest) ||
                    Vector3.Dot(hit.normal, Up) < .50f) continue;
                nearest = hit.distance;
                point = hit.point;
            }
            return nearest < 2 * Snap;
        }

        private bool BodyClear(Vector3 feet)
        {
            const float skin = .035f;
            int count = physics.OverlapCapsule(feet + Up * (profile.Radius + skin),
                feet + Up * (profile.Height - profile.Radius + skin), profile.Radius, contacts,
                ~0, QueryTriggerInteraction.Ignore);
            if (count == contacts.Length) return false;
            for (int i = 0; i < count; i++)
                if (contacts[i] is MeshCollider mesh && Array.IndexOf(solids, mesh) >= 0) return false;
            return true;
        }

        internal bool TryPath(IReadOnlyList<Vector3> pads, out Vector3[] corners)
        {
            corners = Array.Empty<Vector3>();
            var result = new List<Vector3>();
            var path = new NavMeshPath();
            if (!TryPoint(pads[0], out _, out Vector3 previous)) return false;
            for (int i = 1; i < pads.Count; i++)
            {
                if (!TryPoint(pads[i], out _, out Vector3 next) ||
                    !NavMesh.CalculatePath(previous, next, filter, path) ||
                    path.status != NavMeshPathStatus.PathComplete || path.corners.Length < 2 ||
                    Vector3.Distance(path.corners[0], previous) > .08f ||
                    Vector3.Distance(path.corners[path.corners.Length - 1], next) > .08f) return false;
                foreach (Vector3 p in path.corners.Skip(i == 1 ? 0 : 1)) result.Add(ToWorld(p));
                previous = next;
            }
            corners = result.ToArray();
            return corners.Length >= 2 && PathLength(corners) >= .65f;
        }

        // The path query alone may legitimately take a bypass, even when a selected shortcut is shut.
        internal bool Direct(Vector3 start, Vector3 end) => TryPoint(start, out _, out Vector3 a) &&
            TryPoint(end, out _, out Vector3 b) && !NavMesh.Raycast(a, b, out _, filter);

        internal bool ContainsSavedCorner(Vector3 position) =>
            NavMesh.SamplePosition(ToQuery(position), out _, .12f, filter);

        internal static float PathLength(IReadOnlyList<Vector3> corners)
        {
            float length = 0;
            for (int i = 1; i < corners.Count; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }

        private Vector3 ToQuery(Vector3 world) => worldToBake.MultiplyPoint3x4(world) + queryOffset;
        private Vector3 ToWorld(Vector3 query) => bakeToWorld.MultiplyPoint3x4(query - queryOffset);

        public void Dispose()
        {
            if (instance.valid) instance.Remove();
            instance = default;
            UnityEngine.Object.DestroyImmediate(data);
        }
    }
}


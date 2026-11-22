using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Contour4.Procedural.Navigation
{
    internal sealed class RaidNavSurface : IDisposable
    {
        private const int QueryArea = 31;
        private const int NonWalkableArea = 1;
        private const float SnapDistance = .16f;
        private readonly RaidFloor floor;
        private readonly Vector3 offset;
        private readonly NavMeshData data;
        private NavMeshDataInstance instance;
        private readonly NavMeshQueryFilter filter;

        internal RaidNavSurface(RaidFloor floor, Vector3 offset, NavMeshBuildSettings settings,
            Scene scene, LayerMask solids)
        {
            this.floor = floor;
            this.offset = offset;
            filter = new NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = 1 << QueryArea };
            var sources = new List<NavMeshBuildSource>();
            foreach (var collider in floor.Geometry)
            {
                if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy ||
                    collider.isTrigger || collider.gameObject.scene != scene ||
                    (solids.value & (1 << collider.gameObject.layer)) == 0)
                    throw new ArgumentException("Raid navigation geometry must be active solid colliders in its scene.");
                sources.Add(Source(collider, floor.Support.Contains(collider)));
            }
            data = NavMeshBuilder.BuildNavMeshData(settings, sources, floor.LocalBakeBounds,
                Vector3.zero, Quaternion.identity);
            if (!data)
                throw new InvalidOperationException("Could not build raid floor " + floor.FloorId);
            instance = NavMesh.AddNavMeshData(data, offset, Quaternion.identity);
            if (!instance.valid)
            {
                ReleaseData();
                throw new InvalidOperationException("Could not register raid floor " + floor.FloorId);
            }
        }

        private NavMeshBuildSource Source(Collider collider, bool supportsWalking)
        {
            var frameToLocal = Matrix4x4.TRS(floor.Frame.position, floor.Frame.rotation, Vector3.one).inverse;
            var source = new NavMeshBuildSource
            {
                component = collider,
                area = supportsWalking ? QueryArea : NonWalkableArea,
                transform = frameToLocal * collider.transform.localToWorldMatrix
            };
            switch (collider)
            {
                case BoxCollider box:
                    source.shape = NavMeshBuildSourceShape.Box;
                    source.size = box.size;
                    source.transform *= Matrix4x4.Translate(box.center);
                    break;
                case SphereCollider sphere:
                    source.shape = NavMeshBuildSourceShape.Sphere;
                    source.size = Vector3.one * (2 * sphere.radius);
                    source.transform *= Matrix4x4.Translate(sphere.center);
                    break;
                case CapsuleCollider capsule:
                    source.shape = NavMeshBuildSourceShape.Capsule;
                    source.size = new Vector3(2 * capsule.radius, capsule.height, 2 * capsule.radius);
                    Vector3 axis = capsule.direction == 0 ? Vector3.right :
                        capsule.direction == 1 ? Vector3.up : Vector3.forward;
                    source.transform *= Matrix4x4.TRS(capsule.center, Quaternion.FromToRotation(Vector3.up, axis),
                        Vector3.one);
                    break;
                case MeshCollider mesh when mesh.sharedMesh && mesh.sharedMesh.isReadable:
                    source.shape = NavMeshBuildSourceShape.Mesh;
                    source.sourceObject = mesh.sharedMesh;
                    break;
                default:
                    throw new ArgumentException($"Expected primitive collider or readable mesh: {collider.name}");
            }
            return source;
        }

        private Vector3 Query(Vector3 world) => floor.Local(world) + offset;

        internal bool TrySample(Vector3 world, out Vector3 sampled) => TrySample(world, SnapDistance, out sampled);

        internal bool TrySample(Vector3 world, float maxDistance, out Vector3 sampled)
        {
            sampled = default;
            var query = Query(world);
            if (!instance.valid || !NavMesh.SamplePosition(query, out var hit, maxDistance, filter) ||
                !floor.LocalBakeBounds.Contains(hit.position - offset))
                return false;
            sampled = floor.World(hit.position - offset);
            return true;
        }

        internal bool TryPath(Vector3 start, Vector3 end, out Vector3[] points)
        {
            points = null;
            var path = new NavMeshPath();
            if (!TrySample(start, out var entry) || !TrySample(end, out var exit) ||
                !NavMesh.CalculatePath(Query(entry), Query(exit), filter, path) ||
                path.status != NavMeshPathStatus.PathComplete)
                return false;
            var corners = path.corners;
            for (int index = 0; index < corners.Length; index++)
            {
                Vector3 local = corners[index] - offset;
                if (!floor.LocalBakeBounds.Contains(local))
                    return false;
                corners[index] = floor.World(local);
            }
            points = corners;
            return true;
        }

        public void Dispose()
        {
            if (instance.valid)
                instance.Remove();
            instance = default;
            ReleaseData();
        }

        private void ReleaseData()
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(data);
            else
                UnityEngine.Object.DestroyImmediate(data);
        }
    }
}

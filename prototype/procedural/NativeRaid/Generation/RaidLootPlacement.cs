using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural.Catalog;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class RaidLootPlacement
    {
        private const float Skin = .002f;
        private const float SupportReach = .085f;
        private readonly Transform frame;
        private readonly Bounds interior;
        private readonly Collider support;

        internal string FloorId { get; }

        internal RaidLootPlacement(Transform frame, Bounds interior, Collider support, string floorId)
        {
            this.frame = frame;
            this.interior = interior;
            this.support = support;
            FloorId = floorId;
        }

        internal static Bounds StructuralBounds(BakedRoom room) => RaidPlan.BoundsOf(room.Plan.Module.Geometry
            .Where(block => block.Role is not (BlockRole.Free or BlockRole.Obstacle or BlockRole.Fixture))
            .SelectMany(block => block.Vertices).Select(RaidPlan.Vector));

        internal bool AllowsEnvelope(Pose pose, Vector3 size)
        {
            var center = pose.position + pose.rotation * new Vector3(0, size.y / 2, size.z / 2);
            var bottom = pose.position + pose.rotation * new Vector3(0, 0, size.z / 2);
            return Supports(bottom, pose.up) && Contains(center, pose.rotation, size / 2) &&
                !Physics.CheckBox(center, size / 2 - Vector3.one * Skin, pose.rotation, ~0,
                    QueryTriggerInteraction.Ignore);
        }

        internal bool Supports(Vector3 bottom, Vector3 up) =>
            support && support.enabled && support.gameObject.activeInHierarchy &&
            support.Raycast(new Ray(bottom + up * .025f, -up), out var hit, SupportReach) &&
            Vector3.Dot(hit.normal, up) >= .6f;

        internal bool Contains(Vector3 center, Quaternion rotation, Vector3 half)
        {
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = frame.InverseTransformPoint(center + rotation *
                    new Vector3(x * half.x, y * half.y, z * half.z));
                if (!interior.Contains(corner)) return false;
            }
            return true;
        }
    }
}

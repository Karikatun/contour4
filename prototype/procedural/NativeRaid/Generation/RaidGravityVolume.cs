using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural.Catalog;
using Contour4.Procedural.Navigation;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    // Room and floor frames move with RaidWorldRebase. Ownership is calculated in those
    // frames from structural geometry and deck outlines, independent of physics contact.
    internal sealed class RaidGravityVolume
    {
        private readonly Transform root;
        private readonly Bounds structure;
        private readonly (RaidFloor Floor, Vector2[] Outline)[] authoredFloors;
        private readonly RaidFloor defaultFloor;
        private readonly IReadOnlyList<Bounds> floorPieces;

        internal RaidGravityVolume(BakedRoom room)
        {
            root = room.Root;
            structure = RaidPlan.BoundsOf(room.Plan.Module.Geometry
                .Where(block => block.Role is BlockRole.Floor or BlockRole.Shell or BlockRole.Plug)
                .SelectMany(block => block.Vertices.Select(RaidPlan.Vector)));
            structure.Expand(.06f);
            authoredFloors = room.PreparedSurfaces?.GravityFloors.ToArray();
            defaultFloor = room.Floor;
            floorPieces = room.FloorBounds;
        }

        internal bool TryFloor(Vector3 point, out RaidFloor owner, out float distance)
        {
            owner = null;
            distance = float.PositiveInfinity;
            var local = root.InverseTransformPoint(point);
            if (!structure.Contains(local)) return false;

            if (authoredFloors == null)
            {
                foreach (Bounds bounds in floorPieces)
                {
                    float height = local.y - (bounds.center.y - .93f);
                    if (height < -.25f || local.x < bounds.min.x || local.x > bounds.max.x ||
                        local.z < bounds.min.z || local.z > bounds.max.z) continue;
                    float depth = FloorDistance(height);
                    if (depth >= distance) continue;
                    distance = depth;
                    owner = defaultFloor;
                }
                return owner != null;
            }

            foreach (var (floor, outline) in authoredFloors)
            {
                Vector3 projected = floor.Local(point);
                if (projected.y < -.25f ||
                    !PreparedRaidSurfaces.InOutline(outline, new Vector2(projected.x, projected.z))) continue;
                float depth = FloorDistance(projected.y);
                if (depth >= distance) continue;
                distance = depth;
                owner = floor;
            }
            return owner != null;
        }

        // A thin band below a threshold keeps its floor through incidental contacts.
        // When another floor owns that side of the seam, prefer the floor below the point.
        private static float FloorDistance(float height) => height >= -.015f
            ? Mathf.Abs(height) : GrayBuilder.Height + Mathf.Abs(height);
    }
}

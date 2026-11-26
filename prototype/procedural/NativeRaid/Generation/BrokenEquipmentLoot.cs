using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal static class BrokenEquipmentLoot
    {
        internal sealed class Pocket
        {
            internal readonly string SupportName;
            internal readonly Bounds Tray, Clearance;
            internal readonly Vector3 Pivot, Eye;

            internal Pocket(string id, Bounds tray, Bounds clearance, Vector3 pivot, Vector3 eye)
            {
                SupportName = id + " loot tray";
                Tray = tray;
                Clearance = clearance;
                Pivot = pivot;
                Eye = eye;
            }
        }

        internal static readonly Pocket Instrument = new("Fractured instrument",
            new Bounds(new(.010f, -.146f, .055f), new(.258f, .022f, .256f)),
            new Bounds(new(.013f, -.016f, .0575f), new(.246f, .238f, .247f)),
            new Vector3(.029f, -.145f, -.074f), new Vector3(.042f, .27f, 1.36f));

        internal static readonly Pocket Cabinet = new("Ladder maintenance cabinet",
            new Bounds(new(-.31f, -.459f, .36f), new(.460f, .032f, .420f)),
            new Bounds(new(-.31f, -.283f, .3575f), new(.420f, .320f, .415f)),
            new Vector3(-.31f, -.439f, .222f), new Vector3(-.31f, .435f, 1.72f));

        internal static void FitHousing(Transform room, MeshFilter housing, Bounds outer, Pocket pocket,
            List<Collider> colliders)
        {
            UseMeshCollider(housing, colliders);

            // Preserve cover behind the visible opening, with solid contact only on the tray
            // at the spawn site. The existing fractured shell owns the front and sides.
            float backFace = outer.center.z + pocket.Clearance.min.z;
            var back = new Bounds(new(outer.center.x, outer.center.y, (outer.min.z + backFace) / 2),
                new(outer.size.x, outer.size.y, backFace - outer.min.z));
            AddBox(housing.name + " rear cover", back);
            AddBox(pocket.SupportName, new Bounds(outer.center + pocket.Tray.center, pocket.Tray.size));

            void AddBox(string name, Bounds bounds)
            {
                Transform part = RaidAssembly.Child(room, name);
                part.gameObject.layer = housing.gameObject.layer;
                part.localPosition = bounds.center;
                var collider = part.gameObject.AddComponent<BoxCollider>();
                collider.size = bounds.size;
                colliders.Add(collider);
            }
        }

        internal static void UseMeshCollider(MeshFilter part, List<Collider> colliders)
        {
            var original = part.GetComponent<Collider>();
            if (original is MeshCollider mesh)
            {
                mesh.sharedMesh = part.sharedMesh;
                return;
            }
            original.enabled = false;
            colliders.Remove(original);
            var replacement = part.gameObject.AddComponent<MeshCollider>();
            replacement.sharedMesh = part.sharedMesh;
            colliders.Add(replacement);
        }

        internal static RaidLootSlot Find(BakedRoom room)
        {
            Pocket pocket = room.Plan.Module.Id switch
            {
                "instrument-service" => Instrument,
                "shaft-plant" => Cabinet,
                _ => null
            };
            if (pocket == null || !room.TryFloor("ground", out var floor)) return null;
            var support = room.Colliders.OfType<BoxCollider>().SingleOrDefault(part => part.name == pocket.SupportName);
            if (!support || !support.enabled) return null;

            Vector3 center = support.transform.localPosition - pocket.Tray.center;
            var bounds = new Bounds(center + pocket.Clearance.center, pocket.Clearance.size);
            float front = center.z + (pocket == Instrument ? .190f : .600f);
            var retrieval = new RaidLootPlacement.Retrieval(center + pocket.Eye, front);
            return new RaidLootSlot(room, support, LootSurfaceKind.Raised, RaidLootSize.Tiny,
                new Pose(center + pocket.Pivot, Quaternion.identity), bounds, floor.FloorId, retrieval);
        }
    }
}

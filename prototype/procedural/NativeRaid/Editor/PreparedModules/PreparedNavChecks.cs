using System;
using System.Linq;
using UnityEngine;

namespace Contour4.Procedural.Catalog
{
    internal static class PreparedNavChecks
    {
        internal static void Check(GrayModule module, Transform root, PreparedNavBake bake, PreparedNavProfile profile)
        {
            if (!bake.Matches(root.up)) return;
            Vector3 Point(float x, float z) => root.TransformPoint(new Vector3(x, 0, z));
            if (module.Id == "sample-storage-narrow")
            {
                bool aisle = bake.TryPoint(Point(8.5f, 0), out _, out _);
                bool bypass = bake.TryPath(new[] { Point(1.65f, 0), Point(8.5f, 4.65f), Point(13.35f, 3.9f) }, out _);
                Require(aisle == (profile.Radius < .65f) && bypass, $"{profile.Id}: narrow aisle={aisle}, wide bypass={bypass}");
                Debug.Log($"CONTOUR4 PREPARED NAV NEGATIVE {module.Id}/{profile.Id}: narrow-aisle={aisle}, wide-bypass={bypass}");
            }
            if (module.Id == "observation-lab-broken-floor")
            {
                bool pit = bake.TryPoint(Point(10.275f, 0), out _, out _);
                bool bypass = bake.TryPath(new[] { Point(1.65f, 0), Point(10.275f, 4.3f), Point(13.35f, 0) }, out _);
                Require(!pit && bypass, $"{profile.Id}: fall shaft point={pit}, bypass={bypass}");
                Debug.Log($"CONTOUR4 PREPARED NAV NEGATIVE {module.Id}/{profile.Id}: shaft-point={pit}, floor-bypass={bypass}");
            }
            if (module.Id != MaintenanceTrench.Id) return;
            Vector3 start = Point(5, MaintenanceTrench.BridgeZ);
            Vector3 end = Point(16.15f, MaintenanceTrench.BridgeZ);
            Vector3[] freight = { Point(1.65f, 0), Point(MaintenanceTrench.Length - 1.65f, 0) };
            bool openDirect = bake.Direct(start, end);
            bool openBypass = bake.TryPath(freight, out _);
            var shutter = root.GetComponentsInChildren<MeshCollider>().Single(c => c.name == MaintenanceTrench.Gate).transform;
            Vector3 orig = shutter.position;
            bool closedDirect, closedBypass;
            try
            {
                shutter.position = orig - root.up * MaintenanceTrench.GateTravel;
                Physics.SyncTransforms();
                using var closed = new PreparedNavBake(root, root.up, profile, slot: 128);
                closedDirect = closed.Direct(start, end);
                closedBypass = closed.TryPath(freight, out _);
            }
            finally
            {
                shutter.position = orig;
                Physics.SyncTransforms();
            }
            Require(openDirect == (profile.Radius < MaintenanceTrench.PlankHalfWidth) && openBypass && !closedDirect && closedBypass,
                $"{profile.Id}: shutter open direct={openDirect}, closed direct={closedDirect}, freight open={openBypass}, closed={closedBypass}");
            Debug.Log($"CONTOUR4 PREPARED NAV NEGATIVE {module.Id}/{profile.Id}: open-direct={openDirect}, " +
                $"closed-direct={closedDirect}, open-freight={openBypass}, closed-freight={closedBypass}");
        }

        internal static void CheckPortalPlug(Transform root, Dock dock)
        {
            var plug = root.Find("Geometry/Conditional plugs").GetComponentsInChildren<MeshCollider>()
                .Single(c => c.name == dock.PlugId && c.enabled && !c.isTrigger);
            var f = dock.Threshold.Forward;
            var c = dock.Threshold.Point(0, dock.Opening.Y / 2);
            Vector3 forward = root.TransformDirection(new Vector3(f.X, f.Y, f.Z));
            Vector3 center = root.TransformPoint(new Vector3(c.X, c.Y, c.Z));
            Require(plug.Raycast(new Ray(center - forward * .75f, forward), out _, 1.5f),
                $"{root.name}/{dock.Id}: sealed portal ray must contact the authored plug {dock.PlugId}");
        }

        private static void Require(bool condition, string details)
        {
            if (!condition) throw new InvalidOperationException("Prepared navigation check failed: " + details);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural.Catalog.Checks;
using UnityEngine;
using Object = UnityEngine.Object;
using static Contour4.Procedural.Catalog.BuildPreparedModules;

namespace Contour4.Procedural.Catalog
{
    internal static class PreparedModuleVerification
    {
        private const float PositionTolerance = .0001f;

        internal static void Verify(GrayModule module, GameObject instance)
        {
            Transform root = instance.transform;
            var blocks = module.Geometry.Where(block => block.Role != BlockRole.Free).ToArray();
            var colliders = instance.GetComponentsInChildren<MeshCollider>(true)
                .ToDictionary(collider => collider.name, StringComparer.Ordinal);
            Expect(colliders.Count == blocks.Length && instance.GetComponentsInChildren<Collider>(true).Length == blocks.Length,
                $"{module.Id}: collider set differs from catalog geometry.");
            Physics.SyncTransforms();
            var bounds = colliders.Values.First().bounds;
            int triangles = 0;
            foreach (Block block in blocks)
            {
                MeshCollider collider = colliders[block.Id];
                Mesh mesh = collider.sharedMesh;
                var renderer = collider.GetComponent<MeshRenderer>();
                Expect(collider.enabled && collider.gameObject.activeInHierarchy && !collider.isTrigger && renderer && renderer.sharedMaterial &&
                       collider.GetComponent<MeshFilter>().sharedMesh == mesh,
                    $"{module.Id}/{block.Id}: missing active geometry, surface or shared mesh reference.");
                Vector3[] vertices = mesh.vertices;
                bool welded = block.Id == MobiusHall.Ribbon;
                Expect(vertices.Length == (welded ? block.Vertices.Length : block.Faces.Length) &&
                       mesh.triangles.SequenceEqual(welded ? block.Faces : Enumerable.Range(0, block.Faces.Length)),
                    $"{module.Id}/{block.Id}: triangulation changed.");
                for (int i = 0; i < vertices.Length; i++)
                    Near(collider.transform.TransformPoint(vertices[i]), V(block.Vertices[welded ? i : block.Faces[i]]),
                        $"{module.Id}/{block.Id}: vertex {i} changed.");
                triangles += block.Faces.Length / 3;
                bounds.Encapsulate(collider.bounds);
            }
            Near(bounds.size, V(module.Size), $"{module.Id}: prefab size differs from the catalog.");
            VerifyMarkers(module, root);
            VerifyMechanisms(module, root, colliders);
            PreparedModuleLoot.Verify(module, root.Find("Loot candidates"));
            int pathCount = PreparedModuleEnemyNav.Verify(module, root.Find("Monster paths"));
            int lootCount = root.Find("Loot candidates").childCount;
            Expect(pathCount > 0, $"{module.Id}: missing saved monster paths.");

            var physical = module.Docks.Where(CatalogChecks.IsPhysical).ToArray();
            var roles = colliders.Values.ToDictionary(collider => (Collider)collider,
                collider => blocks.Single(block => block.Id == collider.name).Role);
            VerifyRoutes(module, colliders, physical, roles);
            if (SphericalChamber.IsVariant(module.Id)) SphericalChamberChecks.Verify(instance, SphericalChamber.GetMask(module.Id));
            if (module.Id == MobiusHall.Id) MobiusHallChecks.Verify(instance);

            string renderDirectory = Environment.GetEnvironmentVariable("CONTOUR4_PREPARED_RENDER");
            if (!string.IsNullOrEmpty(renderDirectory))
            {
                SceneDressing.Ambient();
                CatalogRoomRenders.Save(module.Id, renderDirectory);
                PreparedModulePreview.Cutaway(module.Id, root, renderDirectory);
            }
            Debug.Log(FormattableString.Invariant($"CONTOUR4 PREPARED {module.Id} | xyz={bounds.size.x:F3},{bounds.size.y:F3},{bounds.size.z:F3} | meshes={blocks.Length} | triangles={triangles} | floors={module.Floors.Count} | docks={module.Docks.Count} | physical={physical.Length} | haul={module.Routes.Count} | loot={lootCount} | monsterPaths={pathCount} | mechanisms={root.Find("Mechanisms").childCount}"));
        }

        private static void VerifyMarkers(GrayModule module, Transform root)
        {
            foreach (Floor floor in module.Floors)
                VerifyFrame(root.Find("Floor frames/" + floor.Id), floor.Pose, $"{module.Id}/{floor.Id}");
            foreach (Dock dock in module.Docks)
            {
                Transform marker = root.Find("Docks/" + dock.Id);
                VerifyFrame(marker, dock.Threshold, $"{module.Id}/{dock.Id}");
                Near(marker.Find("Contact start").position, V(dock.ContactStart), $"{module.Id}/{dock.Id}: contact start.");
                Near(marker.Find("Contact end").position, V(dock.ContactEnd), $"{module.Id}/{dock.Id}: contact end.");
                Near(marker.Find("Opening top").position, V(dock.Threshold.Point(0, dock.Opening.Y)),
                    $"{module.Id}/{dock.Id}: opening height.");
            }
            foreach (HaulRoute route in module.Routes)
            {
                Transform track = root.Find("Haul routes/" + route.Id);
                Expect(track && track.childCount == route.Knots.Length, $"{module.Id}/{route.Id}: missing haul knots.");
                for (int i = 0; i < route.Knots.Length; i++)
                    VerifyFrame(track.GetChild(i), route.Knots[i], $"{module.Id}/{route.Id}/{i + 1}");
            }
            foreach (string parent in new[] { "Floor frames", "Docks", "Haul routes", "Landmarks", "Loot candidates", "Monster paths" })
                Expect(root.Find(parent).GetComponentsInChildren<Collider>(true).Length == 0,
                    $"{module.Id}/{parent}: marker hierarchy added a gameplay collider.");
        }

        private static void VerifyMechanisms(GrayModule module, Transform root, Dictionary<string, MeshCollider> colliders)
        {
            foreach (RoomDressing dressing in module.Dressing)
            foreach (RoomDressing.MovingFront front in dressing.MovingFronts)
            {
                string id = dressing.Name(front.Panel);
                Transform pivot = root.Find("Mechanisms/" + id + " pivot");
                Vector3 position = V(dressing.Point(front.Pivot));
                Vector3 forward = V(dressing.Direction(System.Numerics.Vector3.UnitZ));
                Vector3 up = V(dressing.Frame.Up);
                Near(pivot.position, position, $"{module.Id}/{id}: hinge pivot.");
                Near(pivot.forward, forward, $"{module.Id}/{id}: slide axis.");
                Near(pivot.up, up, $"{module.Id}/{id}: hinge axis.");
                var move = new[] { colliders[id], colliders[dressing.Name(front.Handle)],
                    front.Tray == null ? null : colliders[dressing.Name(front.Tray)] }.Where(c => c).ToArray();
                Expect(move.All(c => c.transform.parent == pivot && c.convex), $"{module.Id}/{id}: detached moving part.");
                Vector3[] rest = move.Select(c => c.transform.position).ToArray();
                Quaternion rotation = pivot.rotation;
                pivot.SetPositionAndRotation(position + forward * front.Travel, Quaternion.AngleAxis(front.Angle, up) * rotation);
                for (int i = 0; i < rest.Length; i++)
                    Near(move[i].transform.position, pivot.position + Quaternion.AngleAxis(front.Angle, up) * (rest[i] - position),
                        $"{module.Id}/{id}: open travel.");
                pivot.SetPositionAndRotation(position, rotation);
            }
            if (module.Id == MaintenanceTrench.Id)
            {
                Transform slide = root.Find("Mechanisms/Inspection shutter slide");
                Transform gate = colliders[MaintenanceTrench.Gate].transform;
                Vector3 open = slide.position;
                Vector3 closed = slide.Find("Closed travel endpoint").position;
                Expect(gate.parent == slide, $"{module.Id}: detached inspection shutter.");
                slide.position = closed;
                Near(gate.position, open + Vector3.down * MaintenanceTrench.GateTravel, $"{module.Id}: shutter travel.");
                slide.position = open;
            }
        }

        private static void VerifyRoutes(GrayModule module, Dictionary<string, MeshCollider> colliders,
            Dock[] physical, Dictionary<Collider, BlockRole> roles)
        {
            var mateRoot = new GameObject("Temporary catalog threshold fixtures");
            var fixtures = new List<Mesh>();
            try
            {
                foreach (Dock dock in physical)
                {
                    MeshCollider plug = colliders[dock.PlugId];
                    plug.gameObject.SetActive(false);
                    roles.Remove(plug);
                    foreach (Block block in CatalogChecks.Fixture(dock))
                    {
                        Mesh mesh = CreateMesh(block);
                        fixtures.Add(mesh);
                        roles.Add(Part(mateRoot.transform, block, mesh), block.Role);
                    }
                }
                Physics.SyncTransforms();
                foreach (Dock dock in physical)
                {
                    ModulePose t = dock.Threshold;
                    BuildCatalogScenes.Verify(module.Id, new HaulRoute($"{dock.Id}/threshold", dock.Id, "mate",
                        dock.MaxCargo, new[] { new ModulePose(t.Point(-1.6f, 0), t.Forward, t.Up),
                            new ModulePose(t.Point(1.8f, 0), t.Forward, t.Up) }), roles);
                }
                foreach (HaulRoute route in module.Routes.Where(r => module.Id != MobiusHall.Id || r.Id != MobiusHall.CeilingRoute)) BuildCatalogScenes.Verify(module.Id, route, roles);
                BuildCatalogScenes.VerifyVariant(module, roles);
                foreach (RoomDressing dressing in module.Dressing)
                    SceneDressing.VerifyCover($"CONTOUR4 PREPARED {module.Id}", dressing, roles.Keys,
                        collider => roles.TryGetValue(collider, out BlockRole role) && role == BlockRole.Floor);
                if (module.Id == "upper-exit-corridor") DriveCatalogCart.Verify(module, roles);
            }
            finally
            {
                Object.DestroyImmediate(mateRoot);
                foreach (Mesh mesh in fixtures) Object.DestroyImmediate(mesh);
                foreach (Dock dock in physical) colliders[dock.PlugId].gameObject.SetActive(true);
                Physics.SyncTransforms();
            }
        }

        private static void VerifyFrame(Transform marker, ModulePose pose, string name)
        {
            Expect(marker, $"{name}: missing frame.");
            Near(marker.position, V(pose.Position), $"{name}: frame position.");
            Near(marker.forward, V(pose.Forward), $"{name}: frame forward.");
            Near(marker.up, V(pose.Up), $"{name}: frame up.");
        }

        private static void Near(Vector3 actual, Vector3 expected, string description) =>
            Expect(Vector3.Distance(actual, expected) <= PositionTolerance, $"{description} {actual} expected {expected}.");
    }
}

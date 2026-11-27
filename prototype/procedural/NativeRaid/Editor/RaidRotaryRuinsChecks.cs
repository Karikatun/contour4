using System;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Contour4.NativeRaid.Editor
{
    public static class RaidRotaryRuinsChecks
    {
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            foreach (float turn in new[] { -90f, 90f, 180f }) Check(turn);
            Debug.Log("CONTOUR4 ROTARY RUINS PASSED");
        }

        private static void Check(float turn)
        {
            var stage = new GameObject("Ruins geometry fixture").transform;
            var room = RaidQuotaRotaryRoom.Create(turn);
            foreach (string dependency in new[] { "ground", "near-rupture-left" })
                Require(room.Geometry.Count(b => b.Id == dependency && b.Role != BlockRole.Free) == 1,
                    "Install requires exactly one solid renderer source: " + dependency);
            foreach (var block in room.Geometry.Where(b => b.Role is not BlockRole.Free and not BlockRole.Plug))
            {
                var (low, high) = block.Bounds;
                var box = new GameObject(block.Id);
                box.transform.SetParent(stage, false);
                box.transform.localPosition = RaidPlan.Vector((low + high) / 2);
                var collider = box.AddComponent<BoxCollider>();
                collider.size = RaidPlan.Vector(high - low);
                if (block.Id == "approach-ramp-down" || block.Id == "approach-ramp-up")
                {
                    Vector3 a = RaidPlan.Vector(block.Vertices[0]);
                    Vector3 along = RaidPlan.Vector(block.Vertices[4]) - a;
                    Vector3 up = RaidPlan.Vector(block.Vertices[1]) - a;
                    Vector3 across = RaidPlan.Vector(block.Vertices[3]) - a;
                    box.transform.localPosition = (a + RaidPlan.Vector(block.Vertices[6])) * .5f;
                    box.transform.localRotation = Quaternion.LookRotation(across, up);
                    collider.size = new Vector3(along.magnitude, up.magnitude, across.magnitude);
                }
            }
            Physics.SyncTransforms();
            // Check both the occupiable space and the sight obstruction. A solid
            // housing by itself does not establish that a player can shelter behind it.
            foreach (var hide in new[] {
                (new Vector3(4.05f, 0, -3.8f), Vector3.left),
                (new Vector3(4.10f, 0, 3.8f), Vector3.right) })
            {
                Vector3 feet = hide.Item1;
                Require(!Physics.CheckCapsule(feet + Vector3.up * .32f, feet + Vector3.up * .90f, .25f),
                    "Crouched shelter has unobstructed body space");
                Require(Physics.Raycast(feet + Vector3.up * .8f, hide.Item2, .95f),
                    "Shelter housing obstructs sight from the approach");
                Require(Physics.Raycast(feet + Vector3.up, Vector3.down, 1.3f), "Shelter has floor support");
            }
            Require(!Physics.CheckBox(new Vector3(4.15f, .8f, 0), new Vector3(1.60f, .70f, 1.25f)),
                "Central cart lane stays clear of added fixtures");
            foreach (float x in new[] { 3.5f, 5.7f })
            {
                Require(Physics.Raycast(new Vector3(x, 1, 0), Vector3.down, out var hit, 2), "Ramp has support");
                Require(hit.point.y < -.035f && hit.point.y > -.10f && Vector3.Angle(hit.normal, Vector3.up) > 2f,
                    $"Ramp slope is physically inclined at x={x}: {hit.point}, {hit.normal}");
            }
            Require(Physics.Raycast(new Vector3(5.8f, 3.2f, -4.45f), Vector3.right, 1), "Standing facade above its damaged opening");
            Require(!Physics.Raycast(new Vector3(5.8f, 1.95f, -4.45f), Vector3.right, .9f), "Facade opening is real");
            Require(Physics.Raycast(new Vector3(5.8f, 5.1f, 0), Vector3.right, 1), "Upper spandrel frames the ring");
            Require(!Physics.Raycast(new Vector3(10, 1.65f, 0), Vector3.up, 4), "Shaft has no roof inside the ring");
            var cabin = RaidQuotaRotaryCabin.Create(stage, RaidQuotaRotaryRoom.Center, RaidQuotaRotaryRoom.NearRotation,
                RaidQuotaRotaryRoom.FarField, 0, null, null, turn, RaidQuotaRotaryRoom.RoomField);
            Physics.SyncTransforms();
            foreach (var collider in cabin.Rotor.GetComponentsInChildren<Collider>())
            foreach (float sign in new[] { -1f, 1f })
            foreach (float h in new[] { -.8f, .8f })
            foreach (float z in new[] { -1.5f, 1.5f })
            {
                var ray = new Ray(cabin.Rotor.TransformPoint(new Vector3(0, h, z)), cabin.Rotor.TransformDirection(Vector3.right * sign));
                Require(!collider.Raycast(ray, out _, 2.8f), "Side quadrant remains open");
                ray = new Ray(cabin.Rotor.TransformPoint(new Vector3(h, 0, z)), cabin.Rotor.up);
                Require(!collider.Raycast(ray, out _, 2f), "Roof quadrant remains open");
            }
            UnityEngine.Object.DestroyImmediate(stage.gameObject);
        }
        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("ROTARY RUINS: " + message);
        }
    }
}

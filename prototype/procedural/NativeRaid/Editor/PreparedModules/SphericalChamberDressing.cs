using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
namespace Contour4.Procedural.Catalog
{
    internal static class SphericalChamberDressing
    {
        const string Dir = "Assets/Contour4/PreparedModules/spherical-chamber";
        static Material[] mats;
        static Mesh cube;
        static List<CombineInstance>[] pieces;
        static Matrix4x4 frame;
        static Material Mat(string name, Color color)
        {
            string path = Dir + "/institute-" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Standard"));
                material.name = "Sphere institute " + name;
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            material.SetFloat("_Glossiness", .2f);
            material.SetFloat("_Metallic", 0);
            EditorUtility.SetDirty(material);
            return material;
        }
        static void Box(int material, Vector3 position, Vector3 size)
        {
            pieces[material].Add(new CombineInstance { mesh = cube, transform = frame * Matrix4x4.TRS(position, Quaternion.identity, size) });
        }
        static void ShellLootStation(Transform fixtures, Transform marker)
        {
            Vector3 inward = marker.localRotation * Vector3.up;
            Vector3 forward = marker.localRotation * Vector3.forward;
            Vector3 center = marker.localPosition + marker.localRotation * new Vector3(0, -.008f, .25f);
            Quaternion rotation = Quaternion.LookRotation(inward, forward);
            frame = Matrix4x4.TRS(center, rotation, Vector3.one);
            // Frame the existing contact patch with an open center so loot rests on the visible shell.
            foreach (float side in new[] { -1f, 1f })
            {
                Box(0, new Vector3(side * .40f, 0, .015f), new Vector3(.05f, .85f, .010f));
                Box(3, new Vector3(0, side * .40f, .015f), new Vector3(.71f, .04f, .010f));
            }
            ShellLamp(fixtures, marker.name.Replace("loot__", "Loot lamp "), center + rotation * Vector3.right * 1.05f,
                rotation, 10f, 2f);
        }

        static void ShellLamp(Transform fixtures, string name, Vector3 center, Quaternion orientation, float range, float intensity)
        {
            frame = Matrix4x4.TRS(center, orientation, Vector3.one);
            Box(0, new Vector3(0, 0, .050f), new Vector3(.68f, .20f, .095f));
            Box(1, new Vector3(0, 0, .105f), new Vector3(.55f, .13f, .025f));
            var lamp = new GameObject(name).AddComponent<Light>();
            lamp.transform.SetParent(fixtures, false);
            lamp.transform.SetLocalPositionAndRotation(center + orientation * Vector3.forward * .30f, orientation);
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, .88f, .71f);
            lamp.intensity = intensity;
            lamp.range = range;
            lamp.shadows = LightShadows.None;
        }
        static void BayPanel(float x, float z, int sign)
        {
            // Enclosed instrument rail on the outside edge of each existing service bay;
            // no legs or collision volume occupy the reserved loot floor.
            frame = Matrix4x4.TRS(new Vector3(x, 1.20f, z), Quaternion.Euler(0, sign > 0 ? 0 : 180, 0), Vector3.one);
            Box(0, new Vector3(0, 0, 0), new Vector3(2.55f, .72f, .17f));
            Box(2, new Vector3(0, .02f, -.10f), new Vector3(2.36f, .52f, .035f));
            for (int i = 0; i < 4; i++)
            {
                float a = -.85f + i * .57f;
                Box(1, new Vector3(a, .08f, -.125f), new Vector3(.39f, .29f, .025f));
                Box(3, new Vector3(a, -.18f, -.13f), new Vector3(.22f, .045f, .025f));
            }
            foreach (float a in new[] { -1.12f, 1.12f })
            {
                Box(0, new Vector3(a, -.47f, .02f), new Vector3(.09f, .42f, .12f));
                Box(3, new Vector3(a, .29f, -.13f), new Vector3(.06f, .06f, .02f));
            }
            foreach (float a in new[] { -1.05f, 1.05f })
                Box(0, new Vector3(a, .43f, 0), new Vector3(.06f, .26f, .09f));
            Box(0, new Vector3(0, .56f, 0), new Vector3(2.5f, .11f, .18f));
            Box(1, new Vector3(0, .49f, -.03f), new Vector3(2.3f, .035f, .11f));
        }
        static void RailLamp(float x, float z)
        {
            frame = Matrix4x4.identity;
            Box(0, new Vector3(x, 1.02f, z), new Vector3(.72f, .13f, .15f));
            Box(1, new Vector3(x, .94f, z), new Vector3(.58f, .035f, .12f));
        }
        static void Seam(float z, int mask)
        {
            const int seamSegments = 144;
            const float skinRadius = SphericalChamber.Radius - .06f;
            const float mouthClearance = 10.45f * SphericalChamber.Scale;
            float radius = Mathf.Sqrt(skinRadius * skinRadius - z * z);
            for (int i = 0; i < seamSegments; i++)
            {
                float startAngle = i * Mathf.PI * 2 / seamSegments;
                float endAngle = (i + 1) * Mathf.PI * 2 / seamSegments;
                Vector3 start = new Vector3(radius * Mathf.Cos(startAngle), radius * Mathf.Sin(startAngle), z);
                Vector3 end = new Vector3(radius * Mathf.Cos(endAngle), radius * Mathf.Sin(endAngle), z);
                Vector3 midpoint = (start + end) * .5f;
                int xMouth = midpoint.x < 0 ? SphericalChamber.WestMask : SphericalChamber.EastMask;
                int yMouth = midpoint.y > 0 ? SphericalChamber.UpperMask : SphericalChamber.LowerMask;
                if ((mask & xMouth) != 0 && Mathf.Abs(midpoint.x) > mouthClearance ||
                    (mask & yMouth) != 0 && Mathf.Abs(midpoint.y) > mouthClearance)
                    continue;
                Vector3 tangent = (end - start).normalized;
                frame = Matrix4x4.TRS(midpoint, Quaternion.LookRotation(-midpoint.normalized, tangent), Vector3.one);
                Box(0, Vector3.zero, new Vector3(.035f, Vector3.Distance(start, end) + .005f, .025f));
            }
        }
        static void Collar(Vector3 position, Vector3 forward, Vector3 up)
        {
            frame = Matrix4x4.TRS(position, Quaternion.LookRotation(forward, up), Vector3.one);
            foreach (float x in new[] { -2.30f, 2.30f })
                Box(0, new Vector3(x, 1.98f, 0), new Vector3(.12f, 3.95f, .13f));
            Box(0, new Vector3(0, 4.04f, 0), new Vector3(4.72f, .12f, .17f));
            Box(1, new Vector3(0, 3.97f, -.08f), new Vector3(3.4f, .045f, .05f));
        }
        static void SmoothShell(Transform root)
        {
            var filter = root.Find("Geometry/Shell/" + SphericalChamber.Shell).GetComponent<MeshFilter>();
            var source = filter.sharedMesh;
            var vertices = source.vertices;
            var normals = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                normals[i] = -(vertices[i] + filter.transform.localPosition).normalized;
            source.normals = normals;
            EditorUtility.SetDirty(source);
        }
        internal static void Add(Transform root, int mask)
        {
            bool east = (mask & SphericalChamber.EastMask) != 0;
            mats = new[]
            {
                Mat("enamel", new Color(.25f, .32f, .25f)),
                Mat("ivory", new Color(.70f, .67f, .53f)),
                Mat("bakelite", new Color(.075f, .09f, .08f)),
                Mat("brass", new Color(.48f, .42f, .26f)),
                Mat("plaster", new Color(.58f, .58f, .49f)),
                Mat("floor", new Color(.19f, .24f, .21f))
            };
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var name = renderer.name;
                Material material = null;
                if (name == SphericalChamber.Shell || renderer.transform.parent.name == "Conditional plugs")
                    material = mats[4];
                else if (renderer.transform.parent.name == "Floors" || name == SphericalChamber.Left || name == SphericalChamber.Right)
                    material = mats[5];
                else if (name.Contains("rail"))
                    material = mats[0];
                if (material)
                    renderer.sharedMaterial = material;
            }
            SmoothShell(root);
            var originalLights = root.Find("Sphere lighting");
            if (originalLights)
                originalLights.gameObject.SetActive(false);
            var parent = new GameObject("Sphere institute instrumentation").transform;
            parent.SetParent(root, false);
            pieces = Enumerable.Range(0, 4).Select(_ => new List<CombineInstance>()).ToArray();
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            var fixtures = new GameObject("Warm shell practicals").transform;
            fixtures.SetParent(root, false);
            var loot = root.Find("Loot candidates");
            ShellLootStation(fixtures, loot.Find("loot__east-shell-spares"));
            ShellLootStation(fixtures, loot.Find("loot__west-shell-spares"));
            var routeLights = new[]
            {
                new Vector3(-.72f, .40f, .58f), new Vector3(.72f, .40f, -.58f),
                new Vector3(-.62f, -.48f, -.62f), new Vector3(.62f, -.48f, .62f),
                new Vector3(-.55f, .65f, -.52f), new Vector3(.55f, .65f, .52f),
                new Vector3(-.55f, -.65f, .52f), new Vector3(.55f, -.65f, -.52f)
            };
            for (int index = 0; index < routeLights.Length; index++)
            {
                Vector3 outward = routeLights[index].normalized;
                ShellLamp(fixtures, $"Shell route lamp {index + 1:D2}", outward * SphericalChamber.Radius,
                    Quaternion.LookRotation(-outward, Vector3.up), 15f, 2f);
            }
            Seam(-6f * SphericalChamber.Scale, mask);
            Seam(0, mask);
            Seam(6f * SphericalChamber.Scale, mask);
            const float collar = SphericalChamber.HalfReservation - .25f;
            Collar(new Vector3(-collar, 0, 0), Vector3.left, Vector3.up);
            if (east) Collar(new Vector3(collar, 0, 0), Vector3.right, Vector3.up);
            if ((mask & SphericalChamber.UpperMask) != 0)
                Collar(new Vector3(0, collar, 0), Vector3.up, Vector3.back);
            if ((mask & SphericalChamber.LowerMask) != 0)
                Collar(new Vector3(0, -collar, 0), Vector3.down, Vector3.back);
            BayPanel(-SphericalChamber.ServiceBayCenter, SphericalChamber.ServiceBayEdge - .08f, 1);
            if (east) BayPanel(SphericalChamber.ServiceBayCenter, -SphericalChamber.ServiceBayEdge + .08f, -1);
            float[] railStations = east ? new[] { -15f, -5.1f, 5.1f, 15f } : new[] { -15f, -5.1f };
            foreach (float x in railStations)
                foreach (float z in new[] { -2.18f, 2.18f })
                    RailLamp(x, z);
            string path = $"Assets/Contour4/PreparedModules/{SphericalChamber.VariantId(mask)}/sphere-dressing-geometry.asset";
            var old = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToDictionary(x => x.name);
            bool initialized = old.Count > 0;
            for (int i = 0; i < 4; i++)
            {
                string name = "Sphere instrumentation " + i;
                var mesh = new Mesh { name = name };
                mesh.CombineMeshes(pieces[i].ToArray(), true, true);
                if (old.TryGetValue(name, out var previous))
                {
                    EditorUtility.CopySerialized(mesh, previous);
                    Object.DestroyImmediate(mesh);
                    mesh = previous;
                    old.Remove(name);
                    EditorUtility.SetDirty(mesh);
                }
                else if (!initialized)
                {
                    AssetDatabase.CreateAsset(mesh, path);
                    initialized = true;
                }
                else
                    AssetDatabase.AddObjectToAsset(mesh, path);
                var o = new GameObject(name);
                o.transform.SetParent(parent, false);
                o.AddComponent<MeshFilter>().sharedMesh = mesh;
                o.AddComponent<MeshRenderer>().sharedMaterial = mats[i];
            }
            foreach (var stale in old.Values)
                Object.DestroyImmediate(stale, true);
            foreach (float x in railStations)
            {
                var light = new GameObject("Rail practical emitter").AddComponent<Light>();
                light.transform.SetParent(fixtures, false);
                light.transform.localPosition = new Vector3(x, 1.04f, 2.18f);
                light.type = LightType.Point;
                light.color = new Color(1f, .88f, .71f);
                light.intensity = 1.7f;
                light.range = 9f;
                light.shadows = LightShadows.None;
            }
        }
    }
}

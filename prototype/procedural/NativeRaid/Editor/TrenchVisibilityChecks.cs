using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Contour4.NativeRaid.Editor
{
    internal static class TrenchVisibilityChecks
    {
        // Stock 0.5.2 level0 CameraPosition.playerOffset, bound to Player/Controller.
        private const float EyeHeight = 1.50f;
        // Stock 0.5.2 resources.assets: Valuable Manor Goblet/ Object/Valuable Collider.
        // The parent rotates -90° about Y. The stock box is unit-sized at zero center.
        private static readonly Vector3 GobletCenter = new(-.00004f, .12338f, .11770f);
        private static readonly Vector3 GobletSize = new(.14789f, .23702f, .14705f);

        internal static void Verify(BakedRoom trench, IReadOnlyList<RaidLootSlot> samples, TrenchShutterMode mode)
        {
            Transform root = trench.Root;
            var opaque = root.GetComponentsInChildren<MeshRenderer>().Where(renderer => renderer.enabled &&
                renderer.sharedMaterials.All(material => material && material.renderQueue < 3000))
                .Select(renderer => (renderer, mesh: renderer.GetComponent<MeshFilter>()?.sharedMesh))
                .Where(item => item.mesh && item.mesh.isReadable)
                .Select(item => new SightMesh(item.renderer, item.mesh)).ToArray();
            var indicators = root.GetComponentsInChildren<Renderer>().Where(renderer => renderer.name == "Shutter status")
                .OrderBy(renderer => root.InverseTransformPoint(renderer.transform.position).x).ToArray();
            Require(indicators.Length == 2, "Both approaches need a shutter lamp.");
            var entry = new Vector3(.15f, EyeHeight, 0);
            var output = Environment.GetEnvironmentVariable("CONTOUR4_PREPARED_EVIDENCE");
            if (mode == TrenchShutterMode.FixedOpen)
            {
                float[] levels = { -.055f, 0, .055f };
                foreach (var slot in samples)
                {
                    Vector3 center = slot.Pose.position + slot.Pose.rotation * GobletCenter;
                    int clear = (from y in levels from z in levels
                        where Visible(entry, center + new Vector3(0, y, z)) select 1).Count();
                    if (clear < 7)
                    {
                        string blocking = string.Join(", ", opaque.Where(mesh => mesh.Intersects(root.TransformPoint(entry),
                            root.TransformPoint(center))).Select(mesh => mesh.Name));
                        throw new InvalidOperationException($"West threshold sees just {clear}/9 probes on the stock goblet at {center:F3}. " +
                            $"Center ray blockers: {blocking}.");
                    }
                    Debug.Log($"CONTOUR4 TRENCH REPLAY west threshold at stock eye height {EyeHeight:F2} m → goblet at {center:F3}: {clear}/9 sightlines clear.");
                }
                var plank = new Vector3(MaintenanceTrench.PitStart + .48f, .004f, MaintenanceTrench.BridgeZ);
                Require(Visible(entry, plank), "West threshold cannot see the plank over the pit.");
                var freightArrow = root.Find("Freight detour arrows");
                Require(freightArrow, "Missing freight detour arrows.");
                var freightMarks = freightArrow.GetComponentsInChildren<Renderer>().Select(Top).ToArray();
                Require(freightMarks.Length == 4 && freightMarks.All(marker => Visible(entry, marker)),
                    "West threshold cannot see both freight detour arrows.");
                // The glazing changes visibility while the barrier still blocks a physical sight ray.
                Vector3 centerOfFirst = samples[0].Pose.position + samples[0].Pose.rotation * GobletCenter;
                Require(Physics.Linecast(root.TransformPoint(entry), root.TransformPoint(centerOfFirst), out var hit,
                    1, QueryTriggerInteraction.Ignore) && hit.collider.name == MaintenanceTrench.Stand,
                    "The optically open inspection stand no longer stops cargo.");
                if (!string.IsNullOrEmpty(output)) Render(root, entry, new Vector3(6, .34f, -1.65f),
                    Path.Combine(output, "trench-west-threshold.png"), samples.Select(slot =>
                        (slot.Pose.position + slot.Pose.rotation * GobletCenter, slot.Pose.rotation)),
                    freightMarks.Append(plank));
            }

            foreach (var (label, x, ordinal, sign) in new[] { ("West", 10.45f, 0, 1f), ("East", 16.56f, 1, -1f) })
            {
                var lane = new Vector3(x, 0, MaintenanceTrench.BridgeZ);
                var eye = lane + Vector3.up * EyeHeight;
                var group = root.Find(label + " shutter wait");
                Require(group && group.GetComponentsInChildren<Collider>(true).All(collider => !collider.enabled),
                    $"{label} waiting sign must be visible geometry with no active colliders.");
                Vector3 held = lane + new Vector3(.55f * sign, 1.02f, 0);
                var witness = new Bounds(held, GobletSize);
                var stripes = group.GetComponentsInChildren<Renderer>().Where(renderer => renderer.name == "Wait stripe").ToArray();
                Require(stripes.Length == 2 && stripes.All(marker => Visible(eye, Top(marker), witness)),
                    $"{label} return approach cannot see both wait stripes around the carried goblet.");
                Vector3 lamp = root.InverseTransformPoint(indicators[ordinal].transform.position);
                Require(Visible(eye, lamp - Vector3.right * (.066f * sign), witness),
                    $"{label} return approach cannot see its signal lamp with a goblet held centrally.");
                Debug.Log($"CONTOUR4 TRENCH REPLAY {label.ToLowerInvariant()} approach: both wait stripes and lamp visible with centered carried goblet.");
                if (mode == TrenchShutterMode.Working && !string.IsNullOrEmpty(output))
                {
                    var stripeCenters = stripes.Select(Top).ToArray();
                    Vector3 waitCenter = (stripeCenters[0] + stripeCenters[1]) * .5f;
                    Vector3 framing = (waitCenter - eye).normalized + (lamp - eye).normalized;
                    Render(root, eye, eye + framing, Path.Combine(output, $"trench-wait-{label.ToLowerInvariant()}.png"),
                        new[] { (held, Quaternion.identity) }, stripeCenters.Append(lamp));
                }
            }

            Vector3 Top(Renderer renderer) => root.InverseTransformPoint(renderer.transform.position +
                root.up * (renderer.transform.localScale.y * .5f + .002f));

            bool Visible(Vector3 from, Vector3 to, Bounds? held = null)
            {
                Vector3 segment = to - from;
                var ray = new Ray(from, segment.normalized);
                if (held.HasValue && held.Value.IntersectRay(ray, out float obstruction) && obstruction < segment.magnitude)
                    return false;
                Vector3 worldFrom = root.TransformPoint(from), worldTo = root.TransformPoint(to);
                return opaque.All(mesh => !mesh.Intersects(worldFrom, worldTo));
            }
        }

        private sealed class SightMesh
        {
            private readonly Transform transform;
            private readonly Bounds bounds;
            private readonly Vector3[] vertices;
            private readonly int[] triangles;
            internal string Name => transform.parent.name + "/" + transform.name;

            internal SightMesh(MeshRenderer renderer, Mesh mesh)
            {
                transform = renderer.transform;
                bounds = mesh.bounds;
                vertices = mesh.vertices;
                triangles = mesh.triangles;
            }

            internal bool Intersects(Vector3 from, Vector3 to)
            {
                Vector3 start = transform.InverseTransformPoint(from);
                Vector3 line = transform.InverseTransformPoint(to) - start;
                if (!bounds.IntersectRay(new Ray(start, line.normalized), out float closest) || closest >= line.magnitude)
                    return false;
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    Vector3 origin = vertices[triangles[t]];
                    Vector3 a = vertices[triangles[t + 1]] - origin, b = vertices[triangles[t + 2]] - origin;
                    Vector3 normal = Vector3.Cross(line, b);
                    float determinant = Vector3.Dot(a, normal);
                    if (Mathf.Abs(determinant) < .0000001f) continue;
                    Vector3 delta = start - origin;
                    float u = Vector3.Dot(delta, normal) / determinant;
                    if (u < 0 || u > 1) continue;
                    Vector3 cross = Vector3.Cross(delta, a);
                    float v = Vector3.Dot(line, cross) / determinant;
                    if (v < 0 || u + v > 1) continue;
                    float fraction = Vector3.Dot(b, cross) / determinant;
                    if (fraction > .0001f && fraction < .9995f) return true;
                }
                return false;
            }
        }

        private static void Render(Transform root, Vector3 eye, Vector3 target, string path,
            IEnumerable<(Vector3 Center, Quaternion Rotation)> samples, IEnumerable<Vector3> cues)
        {
            var camera = new GameObject("Trench observation camera").AddComponent<Camera>();
            var buffer = new RenderTexture(1600, 900, 24);
            var pixels = new Texture2D(buffer.width, buffer.height, TextureFormat.RGB24, false);
            var stockEnvelope = new Material(Shader.Find("Standard")) { color = new Color(.89f, .73f, .43f) };
            var objects = new List<GameObject>();
            var previous = RenderTexture.active;
            try
            {
                // QA witnesses show the measured stock envelope, using the installed room lighting.
                foreach (var (center, rotation) in samples)
                {
                    var witness = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    objects.Add(witness);
                    witness.name = "Stock goblet envelope witness";
                    witness.transform.SetPositionAndRotation(root.TransformPoint(center), root.rotation * rotation);
                    witness.transform.localScale = GobletSize;
                    witness.GetComponent<Collider>().enabled = false;
                    witness.GetComponent<Renderer>().sharedMaterial = stockEnvelope;
                }
                camera.transform.SetPositionAndRotation(root.TransformPoint(eye),
                    Quaternion.LookRotation(root.TransformDirection(target - eye), root.up));
                camera.fieldOfView = 70;
                camera.nearClipPlane = .045f;
                camera.farClipPlane = 27;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.targetTexture = buffer;
                foreach (Vector3 cue in cues.Concat(samples.Select(sample => sample.Center)))
                {
                    Vector3 frame = camera.WorldToViewportPoint(root.TransformPoint(cue));
                    Require(frame.z > 0 && frame.x > .02f && frame.x < .98f && frame.y > .02f && frame.y < .98f,
                        $"Verification camera crops cue at {cue:F3} in {path}.");
                }
                camera.Render();
                RenderTexture.active = buffer;
                pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0);
                pixels.Apply();
                Require(pixels.GetPixels32().Any(pixel => pixel.r > 50 || pixel.g > 50 || pixel.b > 50), $"Blank camera witness {path}.");
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                foreach (GameObject witness in objects) Object.DestroyImmediate(witness);
                Object.DestroyImmediate(stockEnvelope);
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(buffer);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Trench visibility: " + message);
        }
    }
}

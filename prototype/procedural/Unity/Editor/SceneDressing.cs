using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Numerics = System.Numerics;

namespace Contour4.Procedural
{
    internal sealed class SceneDressing
    {
        private readonly Dictionary<DressingSurface, Material> materials = new Dictionary<DressingSurface, Material>();

        internal SceneDressing(string assetFolder)
        {
            Add(DressingSurface.Housing, new Color(.36f, .405f, .44f));
            Add(DressingSurface.Worktop, new Color(.56f, .59f, .595f));
            Add(DressingSurface.Seat, new Color(.29f, .36f, .365f));
            Add(DressingSurface.Conduit, new Color(.72f, .44f, .21f));
            Add(DressingSurface.ActiveLens, new Color(.75f, .89f, .96f), litLens: true);
            Add(DressingSurface.StandbyLens, new Color(.71f, .51f, .26f), litLens: true);
            Add(DressingSurface.DarkLens, new Color(.14f, .18f, .18f));
            Add(DressingSurface.GuideLens, new Color(.67f, .88f, .79f), litLens: true);
            Add(DressingSurface.SteelTrim, new Color(.235f, .275f, .30f));

            void Add(DressingSurface surface, Color color, bool litLens = false)
            {
                string path = $"{assetFolder}/Fitout-{surface}.mat";
                Shader shader = Shader.Find(litLens ? "Unlit/Color" : "Standard");
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = surface.ToString() };
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;
                material.color = color;
                if (!litLens) material.SetFloat("_Glossiness", .21f);
                EditorUtility.SetDirty(material);
                materials.Add(surface, material);
            }
        }

        internal Material MaterialFor(DressingSurface surface) => materials[surface];

        internal static void Ambient()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.255f, .27f, .285f);
            RenderSettings.reflectionIntensity = .15f;
        }

        internal static void AddLights(RoomDressing dressing, Transform parent)
        {
            var lighting = new GameObject($"{dressing.Id}-lighting").transform;
            lighting.SetParent(parent, false);
            foreach (RoomDressing.Lamp lamp in dressing.Lamps)
            {
                var light = new GameObject(dressing.Name(lamp.Id)).AddComponent<Light>();
                light.transform.SetParent(lighting, false);
                light.transform.SetPositionAndRotation(Vec(dressing.Point(lamp.Emitter)),
                    Quaternion.LookRotation(Vec(dressing.Direction(lamp.Direction)), Vec(dressing.Frame.Forward)));
                light.type = LightType.Spot;
                light.lightmapBakeType = LightmapBakeType.Realtime;
                light.renderMode = LightRenderMode.ForcePixel;
                light.intensity = lamp.Duty == LampDuty.Work ? 3.25f : lamp.Duty == LampDuty.Standby ? .66f : 1.35f;
                light.range = lamp.Duty == LampDuty.Work ? 11f : lamp.Duty == LampDuty.Standby ? 5.6f : 8.2f;
                light.spotAngle = lamp.Duty == LampDuty.Guidance ? 82f : 100f;
                light.color = lamp.Duty == LampDuty.Work ? new Color(.83f, .91f, 1) :
                    lamp.Duty == LampDuty.Standby ? new Color(1, .77f, .52f) : new Color(.70f, .91f, .84f);
                if (lamp.Optics == LampOptics.Task)
                {
                    light.spotAngle = 62f;
                    light.intensity *= 1.35f;
                }
                else if (lamp.Optics == LampOptics.Aisle)
                {
                    light.spotAngle = 57f;
                    light.range = 4.25f;
                    light.intensity = 1.05f;
                }
                light.innerSpotAngle = light.spotAngle * .64f;
                light.shadows = lamp.Duty == LampDuty.Work ? LightShadows.Soft : LightShadows.None;
                light.shadowStrength = .62f;
            }
        }

        internal static void VerifyCover(string scope, RoomDressing dressing, IReadOnlyCollection<Collider> colliders,
            Func<Collider, bool> isFloor)
        {
            if (dressing.Covers.Count == 0) return;
            Vector3 up = Vec(dressing.Frame.Up);
            foreach (RoomDressing.Cover cover in dressing.Covers)
            {
                Vector3 from = Vec(dressing.Point(cover.From));
                Vector3 behind = Vec(dressing.Point(cover.Behind));
                Vector3 offset = Vec(dressing.Direction(cover.Offset));
                Vector3 distance = behind - from;
                string expected = dressing.Name(cover.Collider);
                VerifyWalkway(from, from + offset);
                VerifyWalkway(from + offset, behind + offset);
                VerifyWalkway(behind + offset, behind);
                if (cover.CrouchOnly)
                {
                    if (cover.Access.Count == 0)
                        throw new InvalidOperationException($"{scope}/{expected}: missing access from the room route.");
                    Vector3 access = from + offset;
                    foreach (Numerics.Vector3 waypoint in cover.Access)
                    {
                        Vector3 next = Vec(dressing.Point(waypoint));
                        VerifyWalkway(access, next);
                        access = next;
                    }
                    foreach (float lateral in new[] { -.16f, 0, .16f })
                    {
                        Vector3 eyeLine = from + offset.normalized * lateral;
                        foreach (float height in new[] { .55f, .78f, .94f })
                            VerifyRay(eyeLine, height, expected);
                        foreach (float height in new[] { 1.25f, 1.60f })
                            VerifyRay(eyeLine, height, null);
                    }
                    foreach (float height in new[] { .78f, 1.60f })
                        VerifyRay(from + offset, height, null);
                    Debug.Log($"{scope}: PhysX low cover {expected}, 9/9 crouch bands hit the cabinet first, " +
                        "6/6 standing rays pass over it, 2/2 bypass rays clear; supported room access and walkaround clear.");
                }
                else
                {
                    foreach (float height in new[] { .68f, 1.16f, 1.53f })
                    {
                        VerifyRay(from, height, expected);
                        VerifyRay(from + offset, height, null);
                    }
                    Debug.Log($"{scope}: PhysX cover {expected}, 3/3 body-band rays blocked, " +
                        "3/3 aisle rays clear; walkaround capsule clear.");
                }

                void VerifyRay(Vector3 start, float height, string required)
                {
                    string first = Physics.RaycastAll(start + up * height, distance.normalized, distance.magnitude,
                        ~0, QueryTriggerInteraction.Ignore).Where(hit => colliders.Contains(hit.collider))
                        .OrderBy(hit => hit.distance).Select(hit => hit.collider.name).FirstOrDefault();
                    if (first != required)
                        throw new InvalidOperationException($"{scope}/{expected}: {height:F2}m ray from {start} " +
                            $"expects {required ?? "a clear sightline"}, first hit {first ?? "nothing"}.");
                }

                void VerifyWalkway(Vector3 fromFoot, Vector3 toFoot)
                {
                    int samples = Mathf.CeilToInt(Vector3.Distance(fromFoot, toFoot) / .10f);
                    if (samples == 0)
                        throw new InvalidOperationException($"{scope}/{expected}: redundant walkaround waypoint at {fromFoot}.");
                    for (int i = 0; i <= samples; i++)
                    {
                        Vector3 foot = Vector3.Lerp(fromFoot, toFoot, (float)i / samples);
                        bool supported = Physics.RaycastAll(foot + up * .12f, -up, .2f, ~0, QueryTriggerInteraction.Ignore)
                            .Any(hit => isFloor(hit.collider) && Vector3.Dot(hit.normal, up) > .95f);
                        if (!supported)
                            throw new InvalidOperationException($"{scope}/{expected}: no local floor along walkaround at {foot}.");
                        Collider blocker = Physics.OverlapCapsule(foot + up * .32f, foot + up * 1.38f, .28f,
                            ~0, QueryTriggerInteraction.Ignore).FirstOrDefault(colliders.Contains);
                        if (blocker != null)
                            throw new InvalidOperationException($"{scope}/{expected}: walkaround hits {blocker.name} at {foot}.");
                    }
                }
            }
        }

        private static Vector3 Vec(Numerics.Vector3 value) => new Vector3(value.X, value.Y, value.Z);
    }
}

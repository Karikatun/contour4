using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal static class RaidLighting
    {
        private static readonly Color WorkLight = new(.88f, .94f, 1f);
        private static readonly Color TransferLight = new(1f, .91f, .76f);

        // Apply once to an instantiated room. The game and portal cameras share these sources.
        internal static void Brighten(Transform room)
        {
            foreach (Light light in room.GetComponentsInChildren<Light>())
            {
                if (!light.enabled || light.intensity <= 0f) continue;
                if (light.type == LightType.Point)
                {
                    light.intensity *= 1.48f;
                    light.range *= 1.18f;
                }
                else if (light.type == LightType.Spot)
                {
                    // Guide and standby lamps get a greater lift than bright task spotlights.
                    float strength = Mathf.InverseLerp(1f, 4.4f, light.intensity);
                    light.intensity *= Mathf.Lerp(1.70f, 1.24f, strength);
                    light.range *= Mathf.Lerp(1.24f, 1.13f, strength);
                    if (light.spotAngle >= 50f)
                    {
                        light.innerSpotAngle = Mathf.Min(light.innerSpotAngle + 7f, light.spotAngle);
                        light.spotAngle += 12f;
                    }
                }
            }
        }

        internal static void GrayRoom(Transform room, Bounds volume)
        {
            var center = new Vector3(volume.center.x, 3.45f, volume.center.z);
            float extent = Mathf.Max(volume.size.x, volume.size.z);
            AddFill(room, "Graybox work light", center, Mathf.Clamp(extent * 1.12f, 6f, 16f), 3f, WorkLight);
            if (room.name == "extraction-chamber")
                AddFill(room, "Quota ramp light", new Vector3(4.3f, 4.8f, 0), 11f, 2.35f, WorkLight);
        }

        internal static void DimPassage(BakedRoom room)
        {
            // One broad light per lighting gap. Long straight passages need light at both ends.
            Transform root = room.Root;
            switch (room.Plan.Module.Id)
            {
                case "straight-corridor-short":
                    AddFill(root, "Transit overhead", new Vector3(3.75f, 3.62f, 0), 7.8f, 2.4f, WorkLight);
                    break;
                case "straight-corridor-long":
                    AddFill(root, "West transit overhead", new Vector3(3.75f, 3.62f, 0), 8.3f, 2.2f, WorkLight);
                    AddFill(root, "East transit overhead", new Vector3(11.25f, 3.62f, 0), 8.3f, 2.2f, WorkLight);
                    break;
                case "elbow-corridor":
                    AddFill(root, "Turn overhead", new Vector3(6.2f, 3.62f, 2.65f), 9.5f, 2.65f, WorkLight);
                    break;
                case "junction-corridor":
                    AddFill(root, "Spur overhead", new Vector3(7.5f, 3.62f, 7.15f), 7f, 2.1f, WorkLight);
                    break;
                case "upper-exit-corridor":
                    var dock = room.Plan.Dock("upper");
                    AddFill(root, "Upper landing overhead", RaidPlan.Vector(dock.Threshold.Point(-1.6f, 3.5f)),
                        8f, 2.25f, WorkLight);
                    break;
                case "service-cupboard":
                    AddFill(root, "Service reserve lighting", new Vector3(3.8f, 3.62f, 0), 7.6f, 1.75f, TransferLight);
                    break;
            }
        }

        internal static void Fold(FreightFold fold)
        {
            var route = fold.Link.Fold.Module.Routes[0].Knots;
            bool enclosed = fold.Link.Fold.Module.Id == RaidFreightFold.LongSpan;
            if (enclosed)
            {
                AddAt(route.Length / 4);
                AddAt(3 * route.Length / 4);
            }
            else AddAt(route.Length / 2);

            void AddAt(int index) => AddFill(fold.Root, "Transfer lighting",
                RaidPlan.Vector(route[index].Point(0, enclosed ? 3.68f : .70f, -1.94f)),
                enclosed ? 9.4f : 7.4f, enclosed ? 2.35f : 1.8f, TransferLight);
        }

        private static void AddFill(Transform parent, string name, Vector3 position, float range,
            float intensity, Color color)
        {
            Transform fixture = RaidAssembly.Child(parent, name);
            fixture.localPosition = position;
            Light light = fixture.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.range = range;
            light.intensity = intensity;
            light.color = color;
        }
    }
}

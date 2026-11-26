using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal static class RaidLighting
    {
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

    }
}

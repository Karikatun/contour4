using System.Collections.Generic;
using UnityEngine;

namespace SpaceJourney;

internal sealed class PortalFlashlightAperture
{
    private const int Size = 128;
    private const int ShadowSize = 8;
    private const float RaySkin = .025f;
    private const string CookieName = "Contour-4 portal flashlight aperture";
    private static readonly Dictionary<Texture, Color32[]> GeneratedCookies = new();
    private static Texture nativeCookie;
    private static Color32[] nativePixels;
    private static Texture2D defaultSpotCookie;
    private static Color32[] fallbackSpotPixels;
    private static bool defaultSpotSearched;
    private static int? blockerMask;

    private Texture2D texture;
    private Color32[] pixels;
    private readonly byte[] shadow = new byte[ShadowSize * ShadowSize];

    // The emitter stays in virtual space to preserve the angle and range that
    // the stock light already traveled before the opening.
    internal bool Apply(Light light, PortalViews.Aperture entry, PortalViews.Aperture exit)
    {
        if (light.type != LightType.Spot)
            return true;
        Transform pose = light.transform;
        Quaternion entryRotation = entry.Orientation;
        Quaternion worldToEntry = Quaternion.Inverse(entryRotation);
        Vector3 origin = worldToEntry * (pose.position - entry.Center);
        if (origin.z >= 0f)
            return Mathf.Abs(origin.x) <= entry.Width * .5f && Mathf.Abs(origin.y) <= entry.Height * .5f;

        float tangent = Mathf.Tan(light.spotAngle * .5f * Mathf.Deg2Rad);
        Quaternion localRotation = worldToEntry * pose.rotation;
        Vector3 right = localRotation * Vector3.right * tangent;
        Vector3 up = localRotation * Vector3.up * tangent;
        Vector3 forward = localRotation * Vector3.forward;
        Vector2 half = new(Mathf.Min(entry.Width, exit.Width) * .5f, Mathf.Min(entry.Height, exit.Height) * .5f);
        float rangeSquared = light.range * light.range;
        if (origin.z * origin.z >= rangeSquared || forward.z + Mathf.Abs(right.z) + Mathf.Abs(up.z) <= 0f)
            return false;

        int blockers = blockerMask ??= LayerMask.GetMask("Default", "PhysGrabObject", "PhysGrabObjectHinge");
        bool sourceOccluded = false;
        for (int y = 0; y < ShadowSize; y++)
        {
            for (int x = 0; x < ShadowSize; x++)
            {
                int index = y * ShadowSize + x;
                shadow[index] = 255;
                Vector3 ray = forward + right * Sample(x, ShadowSize) + up * Sample(y, ShadowSize);
                if (!TryIntersect(origin, ray, half, rangeSquared, out float t))
                    continue;
                float distance = t * ray.magnitude;
                if (distance <= 2f * RaySkin)
                    continue;
                Vector3 direction = entryRotation * ray.normalized;
                if (Physics.Raycast(pose.position + RaySkin * direction, direction, distance - 2f * RaySkin,
                    blockers, QueryTriggerInteraction.Ignore))
                {
                    shadow[index] = 0;
                    sourceOccluded = true;
                }
            }
        }

        var opening = new ProjectedOpening(origin, forward, right, up, half, 2f / Size);
        if (!sourceOccluded && opening.Contains(-1f, -1f) && opening.Contains(-1f, 1f) &&
            opening.Contains(1f, -1f) && opening.Contains(1f, 1f))
            return true;

        EnsureTexture();
        Color32[] original = SourcePixels(light.cookie);
        bool lit = false;
        for (int y = 0; y < Size; y++)
        {
            float sampleY = Sample(y, Size);
            Vector3 row = forward + up * sampleY;
            Vector4 edge = opening.Row(sampleY) + opening.Horizontal * Sample(0, Size);
            Vector4 step = opening.Horizontal * (2f / Size);
            for (int x = 0; x < Size; x++)
            {
                int index = y * Size + x;
                byte alpha = 0;
                if (ProjectedOpening.Inside(edge) &&
                    TryIntersect(origin, row + right * Sample(x, Size), half, rangeSquared, out _))
                {
                    int sourceAlpha = original[index].a;
                    alpha = sourceOccluded ? (byte)(sourceAlpha * ShadowAt(x, y) / 255) : (byte)sourceAlpha;
                    lit |= alpha > 0;
                }
                pixels[index] = new Color32(255, 255, 255, alpha);
                edge += step;
            }
        }

        if (!lit)
            return false;
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        light.renderMode = LightRenderMode.ForcePixel;
        light.cookie = texture;
        return true;
    }

    internal static bool TryIntersect(Vector3 origin, Vector3 ray, Vector2 half, float rangeSquared, out float t)
    {
        t = ray.z > .00001f ? -origin.z / ray.z : -1f;
        if (t < 0f || t * t * ray.sqrMagnitude >= rangeSquared)
            return false;
        return Mathf.Abs(origin.x + t * ray.x) < half.x && Mathf.Abs(origin.y + t * ray.y) < half.y;
    }

    // Multiplying the four aperture inequalities by the positive plane
    // approach gives a linear test in cookie coordinates. A texel only
    // lights if its entire bilinear support stays inside those edges.
    internal readonly struct ProjectedOpening
    {
        internal readonly Vector4 Horizontal;
        private readonly Vector4 vertical;
        private readonly Vector4 center;

        internal ProjectedOpening(Vector3 origin, Vector3 forward, Vector3 right, Vector3 up,
            Vector2 half, float footprint)
        {
            Vector3 left = new(-origin.z, 0f, half.x + origin.x);
            Vector3 rightEdge = new(origin.z, 0f, half.x - origin.x);
            Vector3 bottom = new(0f, -origin.z, half.y + origin.y);
            Vector3 top = new(0f, origin.z, half.y - origin.y);
            Horizontal = Evaluate(right, left, rightEdge, bottom, top);
            vertical = Evaluate(up, left, rightEdge, bottom, top);
            Vector4 margin = new(
                Mathf.Abs(Horizontal.x) + Mathf.Abs(vertical.x),
                Mathf.Abs(Horizontal.y) + Mathf.Abs(vertical.y),
                Mathf.Abs(Horizontal.z) + Mathf.Abs(vertical.z),
                Mathf.Abs(Horizontal.w) + Mathf.Abs(vertical.w));
            center = Evaluate(forward, left, rightEdge, bottom, top) - margin * footprint;
        }

        internal bool Contains(float x, float y) => Inside(Row(y) + Horizontal * x);

        internal Vector4 Row(float y) => center + vertical * y;

        internal static bool Inside(Vector4 edge) => edge.x > 0f && edge.y > 0f && edge.z > 0f && edge.w > 0f;

        private static Vector4 Evaluate(Vector3 ray, Vector3 left, Vector3 right, Vector3 bottom, Vector3 top) =>
            new(Vector3.Dot(ray, left), Vector3.Dot(ray, right), Vector3.Dot(ray, bottom), Vector3.Dot(ray, top));
    }

    private static float Sample(int index, int size) => (index + .5f) * (2f / size) - 1f;

    private int ShadowAt(int x, int y)
    {
        float sx = Mathf.Clamp((x + .5f) * (ShadowSize / (float)Size) - .5f, 0f, ShadowSize - 1);
        float sy = Mathf.Clamp((y + .5f) * (ShadowSize / (float)Size) - .5f, 0f, ShadowSize - 1);
        int x0 = (int)sx, y0 = (int)sy;
        int x1 = Mathf.Min(x0 + 1, ShadowSize - 1), y1 = Mathf.Min(y0 + 1, ShadowSize - 1);
        float a = Mathf.Lerp(shadow[y0 * ShadowSize + x0], shadow[y0 * ShadowSize + x1], sx - x0);
        float b = Mathf.Lerp(shadow[y1 * ShadowSize + x0], shadow[y1 * ShadowSize + x1], sx - x0);
        return Mathf.RoundToInt(Mathf.Lerp(a, b, sy - y0));
    }

    private void EnsureTexture()
    {
        if (texture)
            return;
        texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true)
        {
            name = CookieName,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        pixels = new Color32[Size * Size];
        GeneratedCookies.Add(texture, pixels);
    }

    private static Color32[] SourcePixels(Texture source)
    {
        if (!source)
        {
            source = DefaultSpotCookie();
            if (!source)
                return FallbackSpotPixels();
        }
        if (GeneratedCookies.TryGetValue(source, out var generated))
            return generated;
        if (nativeCookie == source)
            return nativePixels;

        // Stock cookies can be compressed or not CPU-readable. Preserve their
        // actual alpha via one readback when the flashlight changes texture.
        var temp = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        RenderTexture previous = RenderTexture.active;
        var copy = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
        try
        {
            Graphics.Blit(source, temp);
            RenderTexture.active = temp;
            copy.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
            nativePixels = copy.GetPixels32();
            nativeCookie = source;
            return nativePixels;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temp);
            Object.Destroy(copy);
        }
    }

    private static Texture2D DefaultSpotCookie()
    {
        if (defaultSpotSearched)
            return defaultSpotCookie;
        defaultSpotSearched = true;

        // Built-in spot lights use the engine's Soft texture when cookie is
        // null; a white replacement would brighten the rim of the stock cone.
        foreach (var candidate in Resources.FindObjectsOfTypeAll<Texture2D>())
        {
            if (candidate.name != "Soft" || candidate.format != TextureFormat.Alpha8 ||
                candidate.width != Size || candidate.height != Size)
                continue;
            defaultSpotCookie = candidate;
            break;
        }
        return defaultSpotCookie;
    }

    private static Color32[] FallbackSpotPixels()
    {
        if (fallbackSpotPixels != null)
            return fallbackSpotPixels;
        fallbackSpotPixels = new Color32[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            float v = Sample(y, Size);
            for (int x = 0; x < Size; x++)
            {
                float u = Sample(x, Size);
                float edge = Mathf.Clamp01((.99f - Mathf.Sqrt(u * u + v * v)) / .333f);
                byte alpha = (byte)Mathf.RoundToInt(edge * edge * (3f - 2f * edge) * 255f);
                fallbackSpotPixels[y * Size + x] = new Color32(255, 255, 255, alpha);
            }
        }
        return fallbackSpotPixels;
    }

    internal void Dispose()
    {
        if (!texture)
            return;
        GeneratedCookies.Remove(texture);
        Object.Destroy(texture);
        texture = null;
        if (GeneratedCookies.Count == 0)
        {
            nativeCookie = null;
            nativePixels = null;
            defaultSpotCookie = null;
            defaultSpotSearched = false;
            fallbackSpotPixels = null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Contour4.NativeRaid.Runtime
{
    // Textures belong to one RaidAssembly and are rebuilt when a new raid is generated.
    internal sealed class ExtractionBridgeTextures
    {
        internal const string ConcreteKey = "BridgeConcrete";
        internal const string SteelKey = "BridgeSteel";
        internal const string TimberKey = "BridgeTimber";
        private readonly List<Texture2D> owned = new();

        internal (Texture2D Albedo, Texture2D Relief) Concrete { get; }
        internal (Texture2D Albedo, Texture2D Relief) Steel { get; }
        internal (Texture2D Albedo, Texture2D Relief) Timber { get; }

        internal ExtractionBridgeTextures()
        {
            try
            {
                Concrete = CreateTile(ConcreteKey, "concrete", 5f);
                Steel = CreateTile(SteelKey, "steel", 3.5f);
                Timber = CreateTile(TimberKey, "timber", 3.5f);
            }
            catch
            {
                Release();
                throw;
            }
        }

        private (Texture2D, Texture2D) CreateTile(string key, string kind, float contrast)
        {
            string name = $"Ruptured shaft {kind}";
            Texture2D albedo = DecodeJpeg(key, name + " tile", repeat: true);
            owned.Add(albedo);
            Texture2D relief = SurfaceRelief(albedo, name + " relief", contrast);
            owned.Add(relief);
            albedo.Apply(true, true);
            return (albedo, relief);
        }

        internal static Texture2D DecodeJpeg(string key, string name, bool repeat = false)
        {
            string resourceName = $"Contour4.{key}.jpg";
            byte[] jpeg;
            using (Stream stream = typeof(ExtractionBridgeTextures).Assembly.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    using var bytes = new MemoryStream();
                    stream.CopyTo(bytes);
                    jpeg = bytes.ToArray();
                }
                else
                {
                    // Source-only Unity checks stage the exact embedded input as a TextAsset.
                    var resource = Resources.Load<TextAsset>($"Contour4/{key}");
                    jpeg = resource ? resource.bytes : throw new InvalidOperationException($"Missing {resourceName}.");
                }
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGB24, repeat)
            {
                name = name,
                wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                filterMode = repeat ? FilterMode.Trilinear : FilterMode.Bilinear,
                anisoLevel = repeat ? 4 : 1,
            };
            if (ImageConversion.LoadImage(texture, jpeg)) return texture;
            DestroyTexture(texture);
            throw new InvalidOperationException($"Could not decode {resourceName}.");
        }

        private static Texture2D SurfaceRelief(Texture2D albedo, string name, float contrast)
        {
            const int size = 256;
            const float step = 1f / size;
            var height = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    height[y * size + x] = albedo.GetPixelBilinear((x + .5f) * step, (y + .5f) * step).grayscale;

            var pixels = new Color32[height.Length];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = height[y * size + (x + size - 1) % size] - height[y * size + (x + 1) % size];
                    float dy = height[(y + size - 1) % size * size + x] - height[(y + 1) % size * size + x];
                    Vector3 normal = new Vector3(dx * contrast, dy * contrast, 1f).normalized;
                    pixels[y * size + x] = new Color(normal.x * .5f + .5f, normal.y * .5f + .5f, normal.z * .5f + .5f);
                }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, linear: true)
            {
                name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        internal void Release()
        {
            foreach (Texture2D texture in owned) DestroyTexture(texture);
            owned.Clear();
        }

        internal static void DestroyTexture(Texture2D texture)
        {
            if (Application.isPlaying) Object.Destroy(texture);
            else Object.DestroyImmediate(texture);
        }
    }
}

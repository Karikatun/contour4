using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class RingV10Captures
{
    private const string PrefabAddress = "Assets/Contour4Ring/Contour4 Ring.prefab";
    private const int Width = 1200, Height = 800;
    private static readonly (string Room, Vector3 Eye, Vector3 Target)[] Interior =
    {
        ("Quota", new Vector3(.3f, 1.45f, -5.35f), new Vector3(.3f, 1.45f, 1.4f)),
        ("Loot1", new Vector3(4.8f, 1.75f, -4.85f), new Vector3(.1f, 1.45f, 2f)),
        ("Loot2", new Vector3(-4.8f, 1.75f, -4.85f), new Vector3(.1f, 1.45f, 2f)),
        ("Loot3", new Vector3(.35f, 1.65f, -5.15f), new Vector3(.85f, 1.45f, 2.1f))
    };

    public static void Compare()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            throw new InvalidOperationException("The comparison needs Unity's graphics device.");
        var directory = Directory.CreateDirectory("../captures");
        var prior = Capture("../contour4-ring-v10-unbatched.bundle", "unbatched", directory.FullName);
        var now = Capture("BundleOutput/contour4-ring.bundle", "bundled", directory.FullName);
        using var report = new StreamWriter("../capture-comparison.txt");
        foreach (var (name, before) in prior)
        {
            var after = now[name];
            int withinOne = 0, withinFour = 0;
            double totalDifference = 0;
            for (int p = 0; p < before.Length; p++)
            {
                var a = before[p];
                var b = after[p];
                int r = Math.Abs(a.r - b.r), g = Math.Abs(a.g - b.g), blue = Math.Abs(a.b - b.b);
                var max = Math.Max(r, Math.Max(g, blue));
                if (max <= 1) withinOne++;
                if (max <= 4) withinFour++;
                totalDifference += r + g + blue;
            }
            double almostIdentical = withinFour / (double)before.Length;
            report.WriteLine($"{name}: {withinOne / (double)before.Length:P3} within ±1, " +
                $"{almostIdentical:P3} within ±4, mean channel delta={totalDifference / (before.Length * 3):F3}/255.");
            if (almostIdentical < .98 || totalDifference / (before.Length * 3) > 2)
                throw new InvalidOperationException("Visible difference after static mesh batching: " + name);
        }
    }

    private static Dictionary<string, Color32[]> Capture(string bundlePath, string tag, string output)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.28f, .28f, .28f);
        var bundle = AssetBundle.LoadFromFile(Path.GetFullPath(bundlePath));
        if (!bundle) throw new InvalidOperationException("Cannot capture " + bundlePath);
        var instance = Object.Instantiate(bundle.LoadAsset<GameObject>(PrefabAddress));
        var results = new Dictionary<string, Color32[]>();
        try
        {
            foreach (var (name, eye, target) in Interior)
            {
                Transform room = instance.transform.Find(name);
                results[name] = Shot(room.TransformPoint(eye), room.TransformPoint(target), room.up, output, tag, name);
            }
            foreach (int side in new[] { -1, 1 })
            {
                string name = side > 0 ? "side-positive" : "side-negative";
                results[name] = Shot(new Vector3(0, 12.2875f, side * 54), new Vector3(0, 12.2875f, 0),
                    Vector3.up, output, tag, name, orthographic: true);
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
            bundle.Unload(true);
        }
        return results;
    }

    private static Color32[] Shot(Vector3 eye, Vector3 target, Vector3 up, string output, string tag, string name,
        bool orthographic = false)
    {
        var camera = new GameObject("V10 preview camera").AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, up));
        camera.orthographic = orthographic;
        camera.orthographicSize = 15.75f;
        camera.fieldOfView = 72;
        camera.nearClipPlane = .04f;
        camera.farClipPlane = 120;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.055f, .065f, .075f);
        var lamp = camera.gameObject.AddComponent<Light>();
        lamp.type = orthographic ? LightType.Directional : LightType.Point;
        lamp.range = 16;
        lamp.intensity = orthographic ? 1.0f : 1.25f;

        var framebuffer = new RenderTexture(Width, Height, 24);
        var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        var old = RenderTexture.active;
        try
        {
            camera.targetTexture = framebuffer;
            camera.Render();
            RenderTexture.active = framebuffer;
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(output, $"{tag}-{name}.png"), texture.EncodeToPNG());
            return texture.GetPixels32();
        }
        finally
        {
            RenderTexture.active = old;
            camera.targetTexture = null;
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(framebuffer);
            Object.DestroyImmediate(camera.gameObject);
        }
    }
}

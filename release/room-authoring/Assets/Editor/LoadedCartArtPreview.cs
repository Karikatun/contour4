using System;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class LoadedCartArtPreview
{
    public static void Capture()
    {
        string baseline = Environment.GetEnvironmentVariable("CONTOUR4_ART_BASELINE");
        string candidate = Environment.GetEnvironmentVariable("CONTOUR4_ART_CANDIDATE");
        string output = Environment.GetEnvironmentVariable("CONTOUR4_ART_CAPTURE");
        Directory.CreateDirectory(output);
        var storage = JsonUtility.FromJson<RoomRedesignBuilder.Layout>(File.ReadAllText("layouts/sample-storage.json")).eye;
        Capture(baseline, "sample-storage", storage.position, storage.look, output, "before");
        Capture(candidate, "sample-storage", storage.position, storage.look, output, "after");
        Capture(candidate, "calibration-sector", new(3, 1.65f, -4.3f), new(12, 1.35f, 0), output, "after");
        Capture(candidate, "spherical-chamber", new(-7.5f, 1.6f, 0), new(1.5f, 1.5f, 1.5f), output, "after");
    }

    private static void Capture(string bundlePath, string id, Vector3 position, Vector3 target, string output, string phase)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        using var image = new CaptureTarget();
        var bundle = AssetBundle.LoadFromFile(bundlePath);
        if (!bundle)
            throw new InvalidOperationException($"Unable to inspect native asset bundle {bundlePath}.");

        try
        {
            var prefab = bundle.LoadAsset<GameObject>($"assets/contour4/preparedmodules/{id}/{id}.prefab");
            if (!prefab)
                throw new InvalidOperationException($"Missing prefab {id} in {bundlePath}.");
            var room = Object.Instantiate(prefab);
            var camera = new GameObject("Preview camera").AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
            camera.fieldOfView = 76;
            camera.nearClipPlane = .03f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.08f, .09f, .10f);
            camera.targetTexture = image.Target;
            var lamp = camera.gameObject.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.intensity = .8f;
            lamp.range = 18;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.33f, .33f, .33f);
            camera.Render();
            image.Save(Path.Combine(output, $"{id}-{phase}.png"));
            Object.DestroyImmediate(camera.gameObject);
            Object.DestroyImmediate(room);
            Debug.Log($"CONTOUR4 ART captured {id} {phase}.");
        }
        finally
        {
            bundle.Unload(true);
        }
    }

    private sealed class CaptureTarget : IDisposable
    {
        internal RenderTexture Target { get; } = new(1280, 800, 24);
        private readonly Texture2D image = new(1280, 800, TextureFormat.RGB24, false);

        internal void Save(string path)
        {
            RenderTexture.active = Target;
            image.ReadPixels(new Rect(0, 0, Target.width, Target.height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, image.EncodeToPNG());
        }

        public void Dispose()
        {
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(Target);
        }
    }
}

using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SitePreview
{
    public static void Render()
    {
        EditorSceneManager.OpenScene("Assets/Contour4Ring/Contour4 Ring.unity");
        var root = GameObject.Find("Contour4 Ring").transform;
        Directory.CreateDirectory("ValidationOutput");
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.42f, .42f, .42f);
        var camera = new GameObject("QA Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.09f, .1f, .11f);
        camera.fieldOfView = 60;
        camera.nearClipPlane = .03f;
        camera.farClipPlane = 80;
        var lamp = camera.gameObject.AddComponent<Light>();
        lamp.type = LightType.Point; lamp.range = 15; lamp.intensity = 3;
        Shot(camera, root.Find("Loot2/Catalog sites"), "observation-gallery", new Vector3(6.7f, 2.7f, .2f), new Vector3(-.7f, 1.3f, 5.4f));
        Shot(camera, root.Find("Loot2/Catalog sites"), "sensor-rack", new Vector3(-.5f, 2.9f, 3.8f), new Vector3(-.5f, 2.05f, 6.3f));
        Shot(camera, root.Find("Loot2/Catalog sites"), "operator-table", new Vector3(4.7f, 1.9f, 3.1f), new Vector3(4.65f, .9f, 4.9f));
        Shot(camera, root.Find("Loot3/Catalog sites"), "numbered-shelf", new Vector3(-3.25f, 1.55f, 4.1f), new Vector3(-3.25f, .98f, 5.7f));
        Shot(camera, root.Find("Loot1/Catalog sites"), "calibration-pallet", new Vector3(3.5f, 1.5f, 3.7f), new Vector3(4.2f, .6f, 5.8f));
        Shot(camera, root.Find("Loot1/Catalog sites"), "second-batch-calibration", new Vector3(-1.6f,2.5f,1.7f),new Vector3(-1.6f,.7f,5.8f));
        Shot(camera, root.Find("Loot3/Catalog sites"), "demonstration-shelf", new Vector3(0,1.7f,3.8f),new Vector3(0,.8f,5.65f));
        Debug.Log("[Contour4Sites] Seven actual Unity geometry previews rendered.");
    }

    private static void Shot(Camera camera, Transform frame, string name, Vector3 eye, Vector3 target)
    {
        camera.transform.position = frame.TransformPoint(eye);
        camera.transform.rotation = Quaternion.LookRotation(frame.TransformPoint(target) - camera.transform.position, frame.up);
        var output = new RenderTexture(1280, 720, 24);
        camera.targetTexture = output; camera.Render();
        RenderTexture.active = output;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
        File.WriteAllBytes("ValidationOutput/" + name + ".png", image.EncodeToPNG());
        RenderTexture.active = null; camera.targetTexture = null;
        Object.DestroyImmediate(image); Object.DestroyImmediate(output);
    }
}

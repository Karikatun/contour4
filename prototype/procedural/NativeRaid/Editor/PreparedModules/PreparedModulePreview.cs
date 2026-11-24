using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Contour4.Procedural.Catalog
{
    internal static class PreparedModulePreview
    {
        internal static void Cutaway(string id, Transform root, string output)
        {
            Vector3 eye;
            Vector3 target;
            switch (id)
            {
                case "spiral-corridor":
                    eye = new Vector3(7, 14, 26);
                    target = new Vector3(13.5f, 2.4f, -1.4f);
                    break;
                case "upper-exit-corridor":
                    eye = new Vector3(3, 17, -21);
                    target = new Vector3(10, .4f, -2.5f);
                    break;
                case string sphere when SphericalChamber.IsVariant(sphere):
                    eye = new Vector3(-26, 19, 17) * SphericalChamber.Scale;
                    target = new Vector3(0, .7f, 0);
                    break;
                case MobiusHall.Id:
                    eye = new Vector3(-26, 29, 32);
                    target = new Vector3(0, 5.5f, 0);
                    break;
                default:
                    return;
            }
            Directory.CreateDirectory(output);
            var camera = new GameObject("Cutaway camera").AddComponent<Camera>();
            var key = new GameObject("Cutaway lighting").AddComponent<Light>();
            var image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            RenderTexture frame = RenderTexture.GetTemporary(image.width, image.height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Transform shell = root.Find("Geometry/Shell");
            Transform plugs = root.Find("Geometry/Conditional plugs");
            Color previousAmbient = RenderSettings.ambientLight;
            try
            {
                plugs.gameObject.SetActive(false);
                RenderSettings.ambientLight = new Color(.55f, .58f, .64f);
                key.type = LightType.Directional;
                key.intensity = 1.45f;
                key.transform.rotation = Quaternion.Euler(36, -30, 0);
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.14f, .16f, .19f);
                camera.fieldOfView = 54;
                camera.targetTexture = frame;
                if (SphericalChamber.IsVariant(id))
                    Capture(new Vector3(-9.3f * SphericalChamber.Scale, 1.9f, .9f), new Vector3(1.5f, 2f, 0), "interior");
                if (id == MobiusHall.Id)
                    Capture(new Vector3(-9.9f, 3.3f, 6.8f), new Vector3(2f, 3.6f, -1), "interior");
                shell.gameObject.SetActive(false);
                Capture(eye, target, "deck-cutaway");
                if(id==MobiusHall.Id) Capture(new Vector3(-30,19,0),new Vector3(0,5.5f,0),"separated-access");
            }
            finally
            {
                RenderSettings.ambientLight = previousAmbient;
                shell.gameObject.SetActive(true);
                plugs.gameObject.SetActive(true);
                camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(frame);
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(key.gameObject);
                Object.DestroyImmediate(camera.gameObject);
            }

            void Capture(Vector3 from, Vector3 to, string label)
            {
                camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from, Vector3.up));
                camera.Render();
                RenderTexture.active = frame;
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(output, $"{id}-{label}.png"), image.EncodeToPNG());
            }
        }
    }
}

using System.IO;
using UnityEngine;

namespace Contour4.Procedural.Catalog
{
    internal static class CatalogRoomRenders
    {
        internal static void Save(string module, string output)
        {
            switch (module)
            {
                case "service-cupboard":
                    Shot("stacked-drawers", new Vector3(2.65f, 1.65f, -.5f), new Vector3(1.95f, 1.05f, -3.15f));
                    break;
                case "observation-lab":
                    Shot("lower", new Vector3(2.15f, 2.61f, 2.58f), new Vector3(7.55f, 1.86f, 4.27f));
                    Shot("gallery", new Vector3(10.76f, 5.28f, -4.82f), new Vector3(14.19f, 4.87f, -6.62f));
                    Shot("film-drawers", new Vector3(14.28f, 1.90f, 4.45f), new Vector3(11.65f, 1.08f, 6.36f));
                    break;
                case "observation-lab-broken-floor":
                    Shot("lower", new Vector3(12.15f, 2.12f, 1.7f), new Vector3(8.35f, .1f, .15f));
                    Shot("isolated-pendant", new Vector3(3.8f, 2.9f, 3.2f), new Vector3(8.2f, 3.17f, 1.8f));
                    break;
                case "calibration-sector":
                    Shot("stand", new Vector3(2.66f, 2.52f, -2.7f), new Vector3(10.75f, 1.44f, -.5f));
                    Shot("bench-cable", new Vector3(6.55f, 1.85f, -4.12f), new Vector3(5.04f, 1.59f, -6.73f));
                    Shot("cover-crouch", new Vector3(7.78f, .78f, -6.72f), new Vector3(11.04f, .78f, -6.72f));
                    Shot("cover-standing", new Vector3(7.78f, 1.60f, -6.72f), new Vector3(11.04f, 1.60f, -6.72f));
                    Shot("wide-cabinet", new Vector3(16.2f, 1.82f, -3.65f), new Vector3(17.9f, 1.42f, -6.55f));
                    break;
                case "sample-storage":
                    Shot("intake", new Vector3(2.66f, 2.52f, -2.7f), new Vector3(10.75f, 1.44f, -.5f));
                    Shot("aisles", new Vector3(3.35f, 1.65f, 4.28f), new Vector3(9.6f, 1.44f, 4.49f));
                    Shot("archive-furniture", new Vector3(5.87f, 1.87f, -2.47f), new Vector3(6.1f, 1.01f, -6.55f));
                    break;
                case "shaft-plant":
                    Shot("ground", new Vector3(3.72f, 2.5f, -.35f), new Vector3(13.25f, 4.6f, -.95f));
                    break;
                case "right-angle-chambers":
                    Shot("airlock", new Vector3(-13.7f, 1.86f, .76f), new Vector3(-8.8f, 1.22f, -2.2f));
                    Shot("distribution", new Vector3(1.85f, -14.23f, -.55f), new Vector3(1.44f, -8.4f, 4.5f), Vector3.right);
                    Shot("distribution-fitout", new Vector3(1.83f, -12f, -5.06f),
                        new Vector3(1.25f, -7.58f, -6.06f), Vector3.right);
                    break;
                case "upper-exit-corridor":
                    Shot("branch", new Vector3(6.3f, 1.74f, .95f), new Vector3(12.3f, 1.05f, -5.7f));
                    Shot("upper-approach", new Vector3(10.15f, 1.77f, -6.45f), new Vector3(17.3f, 2.85f, -6.75f));
                    break;
            }

            void Shot(string label, Vector3 eye, Vector3 target, Vector3? up = null)
            {
                Directory.CreateDirectory(output);
                var camera = new GameObject("Room review camera").AddComponent<Camera>();
                var image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
                var frame = RenderTexture.GetTemporary(image.width, image.height, 24, RenderTextureFormat.ARGB32);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    camera.enabled = false;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.07f, .08f, .1f);
                    camera.nearClipPlane = .035f;
                    camera.fieldOfView = 66;
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, up ?? Vector3.up));
                    camera.targetTexture = frame;
                    camera.Render();
                    RenderTexture.active = frame;
                    image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
                    image.Apply();
                    File.WriteAllBytes(Path.Combine(output, $"{module}-{label}.png"), image.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active = previous;
                    camera.targetTexture = null;
                    RenderTexture.ReleaseTemporary(frame);
                    Object.DestroyImmediate(image);
                    Object.DestroyImmediate(camera.gameObject);
                }
            }
        }
    }
}

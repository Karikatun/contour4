using System.Linq;
using UnityEngine;

namespace SpaceJourney;

internal sealed class RingMap : MonoBehaviour
{
    private const int Width = 256;
    private const int Height = 128;
    private const float PixelsPerMetre = 4.25f;
    private static readonly Color32 Background = new(12, 25, 27, 255);
    private static readonly Color32 Outline = new(120, 172, 158, 255);
    private static readonly Color32[] RoomColors =
    {
        new(55, 116, 126, 255), new(121, 82, 46, 255), new(64, 108, 70, 255), new(90, 72, 119, 255)
    };

    private RingRuntime ring;
    private Texture2D texture;
    private Color32[] basePixels;
    private Color32[] pixels;
    private Transform[] flatRooms;
    private MeshRenderer display;
    private Material original;
    private Material material;
    private float nextTick;

    internal void Initialize(RingRuntime runtime)
    {
        ring = runtime;
        flatRooms = ring.Rooms.Select(room => ring.Ring.Find("Map positions/" + room.name)).ToArray();
        texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { name = "Contour-4 planar map" };
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        basePixels = new Color32[Width * Height];
        pixels = new Color32[basePixels.Length];
        System.Array.Fill(basePixels, Background);
        for (int i = 0; i < flatRooms.Length; i++)
        {
            var center = Ground(flatRooms[i].localPosition);
            PaintRoom(center, new Vector2(12, 12), RoomColors[i]);
        }

        var quota = flatRooms[0].localPosition;
        PaintRoom(Ground(quota + new Vector3(-10f, 0, 0)), new Vector2(8f, 4f), RoomColors[0]);
        PaintLine(basePixels, Ground(quota + Vector3.left * 6), Ground(quota + Vector3.left * 7), Outline, 3);
        PaintLine(basePixels, Project(0, new Vector3(6, 0, 0)), Project(1, new Vector3(-6, 0, 0)), Outline, 3);
        PaintLine(basePixels, Project(1, new Vector3(6, 0, 0)), Project(2, new Vector3(-6, 0, 0)), Outline, 3);
        foreach (Transform door in ring.Ring.Find("Map doors"))
        {
            Vector2 center = Ground(door.localPosition);
            Vector2 tangent = Ground(door.localRotation * Vector3.right) * 1.85f;
            PaintLine(basePixels, center - tangent, center + tangent, new Color32(242, 193, 98, 255), 2);
        }
        texture.SetPixels32(basePixels);
        texture.Apply(false);
    }

    private void LateUpdate()
    {
        if (!texture || Time.unscaledTime < nextTick)
            return;
        nextTick = Time.unscaledTime + .075f;
        if (!display)
        {
            var avatar = SemiFunc.PlayerAvatarLocal();
            var tool = avatar ? avatar.GetComponentInChildren<MapToolController>(true) : null;
            if (!tool)
                tool = FindFirstObjectByType<MapToolController>();
            if (!tool || !tool.DisplayMesh)
                return;

            display = tool.DisplayMesh;
            original = display.sharedMaterial;
            material = new Material(Contour4.MapShader) { mainTexture = texture };
            display.sharedMaterial = material;
        }

        var controller = PlayerController.instance;
        if (!controller || !controller.rb)
            return;
        System.Array.Copy(basePixels, pixels, pixels.Length);
        Vector2 point = ring.MapPoint(controller.rb.position, Project);
        PutDisc(pixels, Pixel(point), 4, new Color32(225, 255, 225, 255));
        PutDisc(pixels, Pixel(point), 2, new Color32(57, 232, 128, 255));
        texture.SetPixels32(pixels);
        texture.Apply(false);
    }

    private void OnDestroy()
    {
        if (display && display.sharedMaterial == material)
            display.sharedMaterial = original;
        if (material)
            Destroy(material);
        if (texture)
            Destroy(texture);
    }

    private Vector2 Project(int room, Vector3 local) =>
        Ground(flatRooms[room].localPosition + flatRooms[room].localRotation * local);

    private void PaintRoom(Vector2 center, Vector2 dimensions, Color32 fill)
    {
        Vector2Int lo = Pixel(center - dimensions * .5f);
        Vector2Int hi = Pixel(center + dimensions * .5f);
        for (int y = lo.y; y <= hi.y; y++)
        for (int x = lo.x; x <= hi.x; x++)
            Set(basePixels, x, y, x == lo.x || x == hi.x || y == lo.y || y == hi.y ? Outline : fill);
    }

    private static void PaintLine(Color32[] target, Vector2 from, Vector2 to, Color32 color, int thickness)
    {
        Vector2Int a = Pixel(from);
        Vector2Int b = Pixel(to);
        int count = Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        for (int i = 0; i <= count; i++)
        {
            Vector2 p = Vector2.Lerp(a, b, count == 0 ? 0 : (float)i / count);
            PutDisc(target, Vector2Int.RoundToInt(p), thickness, color);
        }
    }

    private static void PutDisc(Color32[] target, Vector2Int center, int radius, Color32 color)
    {
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
            if (x*x + y*y <= radius*radius)
                Set(target, center.x + x, center.y + y, color);
    }

    private static void Set(Color32[] target, int x, int y, Color32 color)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
            target[y * Width + x] = color;
    }

    private static Vector2Int Pixel(Vector2 p) =>
        Vector2Int.RoundToInt((p - new Vector2(-7f, -6f)) * PixelsPerMetre + new Vector2(Width / 2f, Height / 2f));

    private static Vector2 Ground(Vector3 p) => new(p.x, p.z);
}

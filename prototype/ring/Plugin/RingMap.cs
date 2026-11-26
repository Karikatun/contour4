using System;
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
    private static readonly Color32 UnexploredBridge = new(59, 112, 91, 255);
    private static readonly Color32 Door = new(242, 193, 98, 255);
    private static readonly Color32 UnknownFill = new(3, 12, 10, 255);
    private static readonly Color32 UnknownMark = new(52, 217, 82, 255);
    private static readonly string[] QuestionMark =
    {
        " ##### ", "##   ##", "     ##", "   ### ", "  ##   ", "  ##   ", "       ", "  ##   ", "  ##   "
    };
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
    private bool[] exploredRooms;
    private readonly bool[] exploredBridges = new bool[2];

    internal void Initialize(RingRuntime runtime)
    {
        ring = runtime;
        flatRooms = ring.Rooms.Select(room => ring.Ring.Find("Map positions/" + room.name)).ToArray();
        texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { name = "Contour-4 planar map" };
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        basePixels = new Color32[Width * Height];
        pixels = new Color32[basePixels.Length];
        exploredRooms = new bool[flatRooms.Length];
        PaintPlan();
        texture.SetPixels32(basePixels);
        texture.Apply(false);
    }

    private void LateUpdate()
    {
        if (!texture || Time.unscaledTime < nextTick)
            return;
        nextTick = Time.unscaledTime + .075f;

        var controller = NativeMapPlayer.PlayingController(LevelGenerator.Instance);
        if (!controller ||
            !ring.TryMapPoint(controller.rb.position, Project, out var point, out int room, out int bridge))
        {
            BindDisplay(null);
            return;
        }

        // The native handheld can stay hidden while the player explores the Ring.
        if (room >= 0 ? Reveal(exploredRooms, room) : Reveal(exploredBridges, bridge))
            PaintPlan();

        Array.Copy(basePixels, pixels, pixels.Length);
        PutDisc(pixels, Pixel(point), 4, new Color32(225, 255, 225, 255));
        PutDisc(pixels, Pixel(point), 2, new Color32(57, 232, 128, 255));
        texture.SetPixels32(pixels);
        texture.Apply(false);

        var tool = MapToolController.instance;
        BindDisplay(tool ? tool.DisplayMesh : null);
    }

    private static bool Reveal(bool[] explored, int index)
    {
        if (explored[index])
            return false;
        explored[index] = true;
        return true;
    }

    private void PaintPlan()
    {
        Array.Fill(basePixels, Background);
        for (int i = 0; i < flatRooms.Length; i++)
        {
            Vector2 center = Ground(flatRooms[i].localPosition);
            PaintRoom(center, new Vector2(12, 12), exploredRooms[i] ? RoomColors[i] : UnknownFill);
            if (!exploredRooms[i])
                PaintQuestionMark(center);
        }

        if (exploredRooms[0])
        {
            var quota = flatRooms[0].localPosition;
            PaintLine(basePixels, Ground(quota + Vector3.left * 6), Ground(quota + Vector3.left * 7), Outline, 3);
        }

        for (int i = 0; i < exploredBridges.Length; i++)
        {
            Vector2 from = Project(i, Vector3.right * 6f);
            Vector2 to = Project(i + 1, Vector3.left * 6f);
            if (exploredBridges[i])
                PaintLine(basePixels, from, to, Outline, 3);
            else
            {
                PaintLine(basePixels, from, to, UnexploredBridge, 2);
                Vector2 stub = Vector2.ClampMagnitude(to - from, 1f);
                if (exploredRooms[i])
                    PaintLine(basePixels, from, from + stub, Outline, 3);
                if (exploredRooms[i + 1])
                    PaintLine(basePixels, to - stub, to, Outline, 3);
            }
        }

        foreach (var portal in ring.Portals)
        {
            PaintDoor(portal.A);
            PaintDoor(portal.B);
        }
    }

    private void PaintDoor(PortalEnd mouth)
    {
        // The upper route is omitted from the authored map.
        int room = Array.IndexOf(ring.Rooms, mouth.Room);
        if (mouth.Upper || !exploredRooms[room])
            return;

        Vector3 local = mouth.Room.InverseTransformPoint(mouth.Marker.position);
        // The flat layout puts paired door ends on a shared boundary.
        // Inset each mark so its own room can reveal it independently.
        Vector3 inward = mouth.Room.InverseTransformDirection(-mouth.Marker.forward);
        Vector2 center = Project(room, local + inward);
        Vector3 tangent = mouth.Room.InverseTransformDirection(mouth.Marker.right) * 1.85f;
        Vector2 halfWidth = Ground(flatRooms[room].localRotation * tangent);
        PaintLine(basePixels, center - halfWidth, center + halfWidth, Door, 2);
    }

    private void BindDisplay(MeshRenderer target)
    {
        if (display == target)
            return;
        if (display && display.sharedMaterial == material)
            display.sharedMaterial = original;

        display = target;
        original = null;
        if (!display)
            return;

        original = display.sharedMaterial;
        if (!material)
            material = new Material(Contour4.MapShader) { mainTexture = texture };
        display.sharedMaterial = material;
    }

    private void OnDestroy()
    {
        BindDisplay(null);
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

    private void PaintQuestionMark(Vector2 center)
    {
        Vector2Int pixel = Pixel(center);
        for (int row = 0; row < QuestionMark.Length; row++)
        {
            string pattern = QuestionMark[row];
            for (int column = 0; column < pattern.Length; column++)
                if (pattern[column] == '#')
                    Set(basePixels, pixel.x + column - pattern.Length / 2,
                        pixel.y + QuestionMark.Length / 2 - row, UnknownMark);
        }
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

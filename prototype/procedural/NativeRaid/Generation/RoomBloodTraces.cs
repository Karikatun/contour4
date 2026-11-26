using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural.Catalog;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class RoomBloodTraces
    {
        private const int MarksPerSurface = 6;
        private const int Tile = 128;
        private const int Padding = 8;
        private static readonly int Atlas = Shader.PropertyToID("_BloodAtlas");
        private static readonly int Count = Shader.PropertyToID("_BloodCount");
        private static readonly int Centres = Shader.PropertyToID("_BloodCentres");
        private static readonly int Horizontal = Shader.PropertyToID("_BloodHorizontal");
        private static readonly int Vertical = Shader.PropertyToID("_BloodVertical");
        private const int LeftHand = 0, RightHand = 1, LeftSole = 2, RightSole = 3, DragA = 4, DragB = 5;
        private Texture2D atlas;

        private static readonly Mark[] Cupboard =
        {
            Wall("far-wall", new Vector3(5.43f, 1.31f, 3.50f), Vector3.left, .235f, .292f, -15, LeftHand, .90f),
            Wall("far-wall", new Vector3(5.12f, 1.05f, 3.50f), Vector3.left, .214f, .268f, 14, RightHand, .49f),
            Floor("ground", 2.07f, 1.78f, .154f, .337f, -12, LeftSole, .70f),
            Floor("ground", 2.66f, 1.99f, .151f, .326f, 8, RightSole, .83f),
            Floor("ground", 3.34f, 1.72f, .151f, .323f, -5, LeftSole, .74f),
            Floor("ground", 4.04f, 1.96f, .154f, .336f, 12, RightSole, .58f),
            Floor("ground", 4.76f, 1.81f, .151f, .334f, -3, LeftSole, .43f)
        };
        private static readonly Mark[] Trench =
        {
            Wall("near-wall", new Vector3(4.84f, 1.10f, -6.75f), Vector3.right, .246f, .303f, 24, RightHand, .88f),
            Wall("near-wall", new Vector3(4.49f, .82f, -6.75f), Vector3.right, .205f, .261f, -18, LeftHand, .43f),
            Floor("west-deck-near", 4.54f, -6.23f, .38f, 1.16f, 10, DragA, .90f),
            Floor("west-deck-near", 3.56f, -6.11f, .29f, .67f, 17, DragB, .73f),
            Floor("west-deck-near", 2.84f, -5.96f, .23f, .58f, 5, DragA, .53f),
            Floor("west-deck-near", 2.30f, -5.83f, .14f, .33f, 24, DragB, .36f)
        };

        internal void Apply(BakedRoom room)
        {
            Mark[] marks = room.Plan.Module.Id switch
            {
                "service-cupboard" => Cupboard,
                MaintenanceTrench.Id => Trench,
                _ => null
            };
            if (marks == null) return;
            var roomRoot = room.Root;
            foreach (var group in marks.GroupBy(mark => mark.Support, StringComparer.Ordinal))
            {
                Collider support = room.Colliders.FirstOrDefault(collider => collider.name == group.Key);
                if (!support || !support.enabled || !support.gameObject.activeInHierarchy ||
                    !support.TryGetComponent(out MeshRenderer renderer) || !renderer.enabled ||
                    !renderer.sharedMaterial || !renderer.sharedMaterial.HasProperty(Atlas))
                    continue;

                var centres = new Vector4[MarksPerSurface];
                var horizontal = new Vector4[MarksPerSurface];
                var vertical = new Vector4[MarksPerSurface];
                int count = 0;
                foreach (Mark mark in group)
                {
                    if (!Fits(roomRoot, support, mark)) continue;
                    centres[count] = new Vector4(mark.Position.x, mark.Position.y, mark.Position.z, mark.Opacity);
                    Vector3 u = mark.Right / mark.Width, v = mark.Up / mark.Height;
                    horizontal[count] = new Vector4(u.x, u.y, u.z, 0);
                    vertical[count] = new Vector4(v.x, v.y, v.z, mark.Kind);
                    count++;
                }
                if (count == 0) continue;
                if (!atlas) atlas = BuildAtlas();
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetTexture(Atlas, atlas);
                block.SetFloat(Count, count);
                block.SetVectorArray(Centres, centres);
                block.SetVectorArray(Horizontal, horizontal);
                block.SetVectorArray(Vertical, vertical);
                renderer.SetPropertyBlock(block);
            }
        }

        private static bool Fits(Transform root, Collider support, Mark mark)
        {
            Vector3 normal = root.TransformDirection(Vector3.Cross(mark.Right, mark.Up));
            Vector3 centre = root.TransformPoint(mark.Position);
            Vector3 right = root.TransformVector(mark.Right * (mark.Width * .5f));
            Vector3 up = root.TransformVector(mark.Up * (mark.Height * .5f));
            if (mark.Kind is LeftSole or RightSole &&
                Physics.CheckBox(centre + normal * .07f, new Vector3(right.magnitude, .05f, up.magnitude),
                    Quaternion.LookRotation(up, normal), ~0, QueryTriggerInteraction.Ignore))
                return false;
            return At(centre) && At(centre + right + up) && At(centre - right + up) &&
                At(centre + right - up) && At(centre - right - up);

            bool At(Vector3 centreOnPlane) => support.Raycast(new Ray(centreOnPlane + normal * .045f, -normal), out var hit, .09f) &&
                Mathf.Abs(hit.distance - .045f) <= .012f && Vector3.Dot(hit.normal, normal) >= .995f;
        }

        private static Mark Wall(string support, Vector3 position, Vector3 right, float width, float height,
            float rotation, int kind, float opacity) =>
            new(support, position, right, Vector3.up, width, height, rotation, kind, opacity);

        private static Mark Floor(string support, float x, float z, float width, float length,
            float rotation, int kind, float opacity) =>
            new(support, new Vector3(x, 0, z), Vector3.forward, Vector3.right, width, length, rotation, kind, opacity);

        private readonly struct Mark
        {
            internal readonly string Support;
            internal readonly Vector3 Position, Right, Up;
            internal readonly float Width, Height, Opacity;
            internal readonly int Kind;

            internal Mark(string support, Vector3 position, Vector3 right, Vector3 up, float width, float height,
                float rotation, int kind, float opacity)
            {
                Support = support;
                Position = position;
                var turn = Quaternion.AngleAxis(rotation, Vector3.Cross(right, up));
                Right = turn * right;
                Up = turn * up;
                Width = width;
                Height = height;
                Opacity = opacity;
                Kind = kind;
            }
        }

        private static Texture2D BuildAtlas()
        {
            int width = 3 * Tile, height = 2 * Tile;
            var pixels = new Color32[width * height];
            for (int kind = 0; kind < 6; kind++)
            for (int y = Padding; y < Tile - Padding; y++)
            for (int x = Padding; x < Tile - Padding; x++)
            {
                Vector2 uv = new((x + .5f - Padding) / (Tile - 2 * Padding) - .5f,
                    (y + .5f - Padding) / (Tile - 2 * Padding) - .5f);
                float grain = Grain(x, y, kind);
                float fleck = Grain(x / 3, y / 3, kind + 17);
                uv.x += (fleck - .5f) * .018f;
                uv.y += (grain - .5f) * .015f;
                float coverage = kind switch
                {
                    LeftHand => Palm(uv),
                    RightHand => Palm(new Vector2(-uv.x, uv.y)),
                    LeftSole => Sole(uv, grain),
                    RightSole => Sole(new Vector2(-uv.x, uv.y), grain),
                    _ => Smear(uv, kind - DragA)
                };
                coverage *= Mathf.Lerp(.61f, 1f, grain) * Mathf.Lerp(.62f, 1f, Ramp(.12f, .52f, fleck));
                byte pigment = (byte)Mathf.RoundToInt(Mathf.Lerp(130, 240, fleck));
                pixels[(kind / 3 * Tile + y) * width + kind % 3 * Tile + x] =
                    new Color32(pigment, pigment, pigment, (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(coverage)));
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: true, linear: true)
            {
                name = "Contour-4 contact traces",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return texture;
        }

        private static readonly (Vector2 Base, Vector2 Tip, float Width)[] Fingers =
        {
            (new(-.14f, -.05f), new(-.39f, .10f), .064f),
            (new(-.15f, .035f), new(-.215f, .337f), .048f),
            (new(-.050f, .055f), new(-.045f, .414f), .051f),
            (new(.063f, .045f), new(.123f, .350f), .047f),
            (new(.158f, .016f), new(.262f, .255f), .042f)
        };

        private static float Palm(Vector2 uv)
        {
            float pad = Ellipse(uv, new Vector2(.003f, -.102f), new Vector2(.20f, .189f), .10f);
            float heel = Ellipse(uv, new Vector2(.048f, -.249f), new Vector2(.139f, .111f), .19f);
            float hollow = Ellipse(uv, new Vector2(-.018f, -.121f), new Vector2(.092f, .083f), .38f);
            float joints = 0;
            foreach (var finger in Fingers)
            {
                Vector2 along = finger.Tip - finger.Base;
                float t = Mathf.Clamp01(Vector2.Dot(uv - finger.Base, along) / along.sqrMagnitude);
                float section = 1 - Ramp(.72f, 1.17f,
                    Vector2.Distance(uv, finger.Base + along * t) / (finger.Width * (1 - .21f * t)));
                float crease = 1 - .45f * (1 - Ramp(.018f, .060f, Mathf.Min(Mathf.Abs(t - .39f), Mathf.Abs(t - .74f))));
                joints = Mathf.Max(joints, section * crease);
            }
            return Mathf.Max(Mathf.Max(pad * (1 - .77f * hollow), heel * .84f), joints);
        }

        private static float Sole(Vector2 uv, float grain)
        {
            float x = uv.x + .048f * uv.y;
            float toe = Ellipse(new Vector2(x, uv.y), new Vector2(-.025f, .235f), new Vector2(.39f, .217f), .12f);
            float heel = Ellipse(new Vector2(x, uv.y), new Vector2(.027f, -.295f), new Vector2(.315f, .149f), .13f);
            float instep = Ellipse(new Vector2(x, uv.y), new Vector2(.15f, -.062f), new Vector2(.185f, .19f), .24f);
            float outline = Mathf.Max(toe, Mathf.Max(heel, instep * .58f));
            float treadY = uv.y + .16f * Mathf.Abs(x) + .035f * x;
            float grooves = Ramp(.17f, .30f, Mathf.Abs(Mathf.Repeat(treadY * 12.8f + .31f, 1) - .5f));
            float split = Ramp(.011f, .043f, Mathf.Abs(x + .038f * Mathf.Sin(uv.y * 31f)));
            float torn = Ramp(.09f, .49f, grain + .16f * Mathf.Cos(29 * uv.y + 11 * x));
            return outline * (grooves * split * torn + .14f) * Mathf.Lerp(.90f, 1.0f, toe);
        }

        private static float Smear(Vector2 uv, int variant)
        {
            float head = .53f + .47f * Ramp(-.39f, .29f, uv.y);
            float bend = (variant == 0 ? .081f : -.116f) * Mathf.Sin(uv.y * 6.1f + variant);
            float drag = Ellipse(new Vector2(uv.x - bend, uv.y), Vector2.zero, new Vector2(.268f * head, .455f), .24f);
            float ridgeA = Ellipse(new Vector2(uv.x, uv.y), new Vector2(-.17f + bend, .064f),
                new Vector2(.069f, .321f), .24f);
            float ridgeB = Ellipse(new Vector2(uv.x, uv.y), new Vector2(.186f + bend, .095f),
                new Vector2(.048f, .244f), .22f);
            float split = Ramp(.14f, .47f, Mathf.Abs(Mathf.Sin(uv.x * 40 + 2.8f * uv.y)));
            float dropA = Ellipse(uv, new Vector2(.32f, .297f), new Vector2(.043f, .064f), .23f);
            float dropB = Ellipse(uv, new Vector2(-.283f, -.211f), new Vector2(.035f, .034f), .22f);
            return head * Mathf.Max(drag * (.42f + .57f * split),
                Mathf.Max(Mathf.Max(ridgeA * .65f, ridgeB * .56f), Mathf.Max(dropA * .61f, dropB * .49f)));
        }

        private static float Ellipse(Vector2 uv, Vector2 centre, Vector2 radius, float feather)
        {
            float x = (uv.x - centre.x) / radius.x, y = (uv.y - centre.y) / radius.y;
            return 1 - Ramp(1 - feather, 1 + feather, x * x + y * y);
        }

        private static float Ramp(float low, float high, float value)
        {
            float t = Mathf.Clamp01((value - low) / (high - low));
            return t * t * (3 - 2 * t);
        }

        private static float Grain(int x, int y, int seed)
        {
            uint bits = unchecked((uint)x * 0x94D049BBu + (uint)y * 0x369DEA0Fu + (uint)seed * 0x9E3779B9u);
            bits ^= bits >> 16;
            bits *= 0x7FEB352Du;
            bits ^= bits >> 15;
            return (bits & 0xFFFFu) / 65535f;
        }

        internal void Release()
        {
            if (!atlas) return;
            if (Application.isPlaying) Object.Destroy(atlas); else Object.DestroyImmediate(atlas);
            atlas = null;
        }
    }
}

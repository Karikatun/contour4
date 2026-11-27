using System;
using System.Collections.Generic;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    internal static class RaidExtractionBridge
    {
        internal const float GapStart = 4.8f;
        internal const float StraightGapEnd = 12.3f;
        internal const float BridgeHalfWidth = .60f;
        internal const float BentLanding = 6.15f;
        internal const float BentAxis = 10.8f;
        internal const float NearWall = -6f;
        internal const float StraightFarWall = 6f;
        internal const float BentFarWall = 16.1f;
        internal const string TimberPrefix = "timber-plank-";
        internal const string FastenerPrefix = "timber-fastener-";
        internal const string FracturePrefix = "fracture-";
        internal const string CartStaging = "cart-staging";

        internal static GrayModule Create(string id, string entryId, string machineId, bool bent)
        {
            float length = bent ? 17f : 22.5f;
            float farWall = bent ? BentFarWall : StraightFarWall;
            float apronFar = bent ? 4.8f : farWall;
            var chamber = new GrayBuilder(id, farWall - NearWall, length, difficulty: bent ? 3 : 2);
            var entry = chamber.Door(entryId, "ground", Pose(0, 0, 0, -Vector3.UnitX), Cargo.Cart);
            chamber.RectangleWalls(0, length, 0, 8f, NearWall, farWall, west: entry);
            chamber.Box("entry-roof", BlockRole.Shell, 0, GapStart, 8f, 8.25f, NearWall, farWall);
            chamber.Box("protected-shaft-volume", BlockRole.Free, 0, length, -7.85f, 8f, NearWall, farWall);
            chamber.Box("shaft-bottom", BlockRole.Shell, 0, length, -8f, -7.75f, NearWall, farWall);
            chamber.Box("shaft-wall-entry", BlockRole.Shell, 0, Skirt, -7.75f, 0, NearWall, farWall);
            chamber.Box("shaft-wall-back", BlockRole.Shell, length - Skirt, length, -7.75f, 0, NearWall, farWall);
            chamber.Box("shaft-wall-near", BlockRole.Shell, Skirt, length - Skirt, -7.75f, 0, NearWall, NearWall + Skirt);
            chamber.Box("shaft-wall-far", BlockRole.Shell, Skirt, length - Skirt, -7.75f, 0, farWall - Skirt, farWall);
            chamber.FallVolume("extraction-shaft", 0, length, -7.74f, -4.2f, NearWall, farWall);

            chamber.Deck("safe-entry-apron", 0, GapStart, 0, NearWall, apronFar);
            chamber.Box("cart-cover", BlockRole.Obstacle, 3.0f, 3.45f, 0, 2.45f, 2.55f, 4.50f);
            chamber.Box("apron-shaft-guard", BlockRole.Shell, GapStart - .23f, GapStart, 0, .88f, NearWall, -BridgeHalfWidth);
            chamber.Box("apron-return-guard", BlockRole.Shell, GapStart - .23f, GapStart, 0, .88f, BridgeHalfWidth, apronFar);
            if (bent)
                chamber.Box("entry-pit-side-guard", BlockRole.Shell, 0, GapStart, 0, .88f, apronFar - .23f, apronFar);

            var staging = Pose(2.4f, 0, 0);
            chamber.Module.Landmarks.Add(CartStaging, staging.Position);
            chamber.Route("staging-apron", entry.Id, CartStaging, Cargo.Cart, Pose(0, 0, 0), staging);

            var route = new List<ModulePose> { Pose(0, 0, 0), staging };
            if (bent) MakeBent(chamber, route, length, farWall, apronFar);
            else MakeStraight(chamber, route, length, farWall);

            var ramp = route[route.Count - 1];
            chamber.Module.Landmarks.Add(machineId, ramp.Position + 4.6f * ramp.Forward);
            chamber.Route("hand-loading-ramp", entry.Id, machineId, Cargo.Hand, route.ToArray());
            return chamber.Module;
        }

        private static void MakeStraight(GrayBuilder chamber, List<ModulePose> route, float length, float farWall)
        {
            AddTimber(chamber, "long", Pose(GapStart, 0, 0), StraightGapEnd - GapStart, 0);
            chamber.Deck("extraction-landing", StraightGapEnd, length, 0, NearWall, farWall);
            chamber.Box("native-quota-working-volume", BlockRole.Free, StraightGapEnd, length, 0, 34f, -5f, 5f);

            AddFracture(chamber, "apron-break", 5.2f, -.57f, -2.13f, 2.5f, 1.6f, 18f);
            AddFracture(chamber, "drifting-slab", 8.3f, -1.18f, 2.01f, 2.45f, 1.13f, -29f);
            AddFracture(chamber, "fallen-corner", 10.3f, -2.40f, -2.65f, 1.52f, 1.30f, 47f);
            AddFracture(chamber, "landing-break", 11.94f, -.68f, 2.78f, 1.45f, 2.23f, -16f);
            chamber.MapFloor("ground", Level, new Vector2(0, NearWall), new Vector2(GapStart, NearWall),
                new Vector2(GapStart, -BridgeHalfWidth), new Vector2(StraightGapEnd, -BridgeHalfWidth),
                new Vector2(StraightGapEnd, NearWall), new Vector2(length, NearWall), new Vector2(length, farWall),
                new Vector2(StraightGapEnd, farWall), new Vector2(StraightGapEnd, BridgeHalfWidth),
                new Vector2(GapStart, BridgeHalfWidth), new Vector2(GapStart, farWall), new Vector2(0, farWall));
            route.Add(Pose(13.6f, 0, 0));
        }

        private static void MakeBent(GrayBuilder chamber, List<ModulePose> route, float length, float farWall, float apronFar)
        {
            float outer = BentAxis + BridgeHalfWidth, inner = BentAxis - BridgeHalfWidth;
            const float landingNear = 6.1f;
            AddTimber(chamber, "incoming", Pose(GapStart, 0, 0), outer - GapStart, 1);
            AddTimber(chamber, "outgoing", Pose(BentAxis, 0, BridgeHalfWidth, Vector3.UnitZ),
                BentLanding - BridgeHalfWidth, 2);
            chamber.Deck("extraction-landing", landingNear, length, 0, BentLanding, farWall);
            chamber.Box("native-quota-working-volume", BlockRole.Free, BentAxis - 5f, BentAxis + 5f, 0, 34f, 5.1f, farWall);

            AddFracture(chamber, "threshold-break", 5.15f, -.65f, -2.05f, 2.25f, 1.52f, -12f);
            AddFracture(chamber, "inside-turn", 8.56f, -1.30f, 2.35f, 2.83f, 1.56f, 34f);
            AddFracture(chamber, "outside-turn", 12.88f, -1.84f, 1.25f, 2.03f, 1.64f, -31f);
            AddFracture(chamber, "far-bank-break", 13.71f, -.72f, 5.85f, 2.51f, 1.67f, 20f);
            chamber.MapFloor("ground", Level, new Vector2(0, NearWall), new Vector2(GapStart, NearWall),
                new Vector2(GapStart, -BridgeHalfWidth), new Vector2(outer, -BridgeHalfWidth), new Vector2(outer, BentLanding),
                new Vector2(length, BentLanding), new Vector2(length, farWall), new Vector2(landingNear, farWall),
                new Vector2(landingNear, BentLanding), new Vector2(inner, BentLanding), new Vector2(inner, BridgeHalfWidth),
                new Vector2(GapStart, BridgeHalfWidth), new Vector2(GapStart, apronFar), new Vector2(0, apronFar));
            route.Add(Pose(BentAxis, 0, 0));
            route.Add(Pose(BentAxis, 0, 6.5f, Vector3.UnitZ));
        }

        private static void AddTimber(GrayBuilder chamber, string name, ModulePose frame, float length, int pattern)
        {
            // Three continuous rows, with the butt joints staggered like a patched walkway.
            for (int row = 0; row < 3; row++)
            {
                float left = -BridgeHalfWidth + row * .40f + (row == 0 ? 0 : .006f);
                float right = -BridgeHalfWidth + (row + 1) * .40f - (row == 2 ? 0 : .006f);
                int piece = 0;
                for (float start = 0; start < length; piece++)
                {
                    float boardLength = 2.44f + .29f * ((piece + row + pattern) % 3) + (piece == 0 ? .22f * (1 - row) : 0);
                    float end = Math.Min(length, start + boardLength);
                    if (length - end < .24f) end = length;
                    // Edge splinters are cut out of the collision mesh too.
                    float nick = .013f + (piece + pattern) % 3 * .013f;
                    float leftCut = row == 0 ? nick : 0;
                    float rightCut = row == 2 ? nick : 0;
                    var points = new[]
                    {
                        new Vector2(start, left + leftCut), new Vector2(start + .15f, left),
                        new Vector2(end - .12f, left), new Vector2(end, left + leftCut * .55f),
                        new Vector2(end, right - rightCut), new Vector2(end - .21f, right),
                        new Vector2(start + .19f, right), new Vector2(start, right - rightCut * .55f),
                    };
                    string pieceId = $"{name}-{piece}-{row}";
                    chamber.Module.Geometry.Add(FacetedPrism(TimberPrefix + pieceId, BlockRole.Floor, frame,
                        points, 0, -.165f, 0, 0));
                    for (int tip = 0; tip < 2; tip++)
                    {
                        float along = tip == 0 ? start + .096f : end - .091f;
                        var head = new ModulePose(frame.Point(along, 0), frame.Forward, frame.Up);
                        for (int side = 0; side < 2; side++)
                        {
                            float across = side == 0 ? left + .066f : right - .069f;
                            chamber.Module.Geometry.Add(Block.Box($"{FastenerPrefix}{pieceId}-{tip}-{side}", BlockRole.Fixture,
                                head, -.017f, .017f, -.003f, .002f, across - .016f, across + .016f));
                        }
                    }
                    start = end;
                }
            }
        }

        private static void AddFracture(GrayBuilder chamber, string id, float x, float y, float z,
            float length, float width, float yaw)
        {
            float angle = yaw * MathF.PI / 180f;
            var frame = Pose(x, y, z, new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)));
            var outline = new[]
            {
                new Vector2(-.53f * length, -.24f * width), new Vector2(-.21f * length, -.49f * width),
                new Vector2(.29f * length, -.54f * width), new Vector2(.51f * length, -.18f * width),
                new Vector2(.39f * length, .27f * width), new Vector2(.10f * length, .52f * width),
                new Vector2(-.37f * length, .39f * width),
            };
            chamber.Module.Geometry.Add(FacetedPrism(FracturePrefix + id, BlockRole.Obstacle, frame,
                outline, 0, -.34f, .21f, .24f));
        }

        private static Block FacetedPrism(string id, BlockRole role, ModulePose frame, Vector2[] outline,
            float top, float bottom, float taper, float relief)
        {
            var mesh = new CurvedRoomMesh();
            var upper = new int[outline.Length];
            var lower = new int[outline.Length];
            Vector2 center = Vector2.Zero;
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 p = outline[i];
                center += p;
                upper[i] = mesh.Add(frame.Point(p.X, top + (i % 3 - 1) * relief * .45f, p.Y));
                float contraction = 1f - taper * (i % 2 == 0 ? 1f : .45f);
                lower[i] = mesh.Add(frame.Point(p.X * contraction, bottom - ((i + 1) % 3) * relief, p.Y * contraction));
            }
            center /= outline.Length;
            int crown = mesh.Add(frame.Point(center.X, top + .22f * relief, center.Y));
            int underside = mesh.Add(frame.Point(center.X, bottom - .45f * relief, center.Y));
            for (int i = 0; i < outline.Length; i++)
            {
                int next = (i + 1) % outline.Length;
                Vector2 edge = outline[next] - outline[i];
                mesh.Quad(upper[i], upper[next], crown, crown, frame.Up);
                mesh.Quad(lower[i], lower[next], underside, underside, -frame.Up);
                mesh.Quad(upper[i], lower[i], lower[next], upper[next], edge.Y * frame.Forward - edge.X * frame.Right);
            }
            return mesh.Build(id, role);
        }
    }
}

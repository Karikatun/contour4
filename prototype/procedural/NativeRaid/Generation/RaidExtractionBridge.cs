using System.Collections.Generic;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    internal static class RaidExtractionBridge
    {
        internal const float GapStart = 4.8f;
        internal const float StraightGapEnd = 11f;
        internal const float BridgeHalfWidth = 1.45f;
        internal const float BentLanding = 6.15f;
        internal const float BentAxis = 10.8f;
        internal const float NearWall = -6f;
        internal const float StraightFarWall = 6f;
        internal const float BentFarWall = 16.1f;

        internal static GrayModule Create(string id, string entryId, string machineId, bool bent)
        {
            float length = bent ? 17f : 22.5f;
            float farWall = bent ? BentFarWall : StraightFarWall;
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

            // The straight path stays clear from the docking opening to the first damaged rail.
            chamber.Deck("safe-entry-apron", 0, GapStart, 0, NearWall, bent ? 4.8f : farWall);
            chamber.Box("cart-cover", BlockRole.Obstacle, 3.0f, 3.45f, 0, 2.45f, 2.55f, 4.50f);
            chamber.Box("apron-shaft-guard", BlockRole.Shell, GapStart - .16f, GapStart, 0, .98f, NearWall, -BridgeHalfWidth);
            chamber.Box("apron-return-guard", BlockRole.Shell, GapStart - .16f, GapStart, 0, .98f,
                BridgeHalfWidth, bent ? 4.8f : farWall);
            if (bent)
                chamber.Box("entry-pit-side-guard", BlockRole.Shell, 0, GapStart, 0, .98f, 4.64f, 4.8f);

            var route = new List<ModulePose> { Pose(0, 0, 0) };
            if (bent) MakeBent(chamber, route, length, farWall);
            else MakeStraight(chamber, route, length, farWall);

            var ramp = route[route.Count - 1];
            chamber.Module.Landmarks.Add(machineId, ramp.Position + 4.6f * ramp.Forward);
            chamber.Route("freight-ramp", entry.Id, machineId, Cargo.Cart, route.ToArray());
            return chamber.Module;
        }

        private static void MakeStraight(GrayBuilder chamber, List<ModulePose> route, float length, float farWall)
        {
            chamber.Deck("straight-bridge-spine", GapStart, StraightGapEnd, 0, -BridgeHalfWidth, BridgeHalfWidth);
            chamber.Deck("extraction-landing", StraightGapEnd, length, 0, NearWall, farWall);
            chamber.Box("native-quota-working-volume", BlockRole.Free, 11.5f, length, 0, 34f, -5f, 5f);
            chamber.Box("severed-deck-entry", BlockRole.Obstacle, GapStart, 6.5f, -.56f, -.22f, -2.6f, -BridgeHalfWidth);
            chamber.Box("severed-deck-extraction", BlockRole.Obstacle, 9.05f, StraightGapEnd, -.95f, -.31f, BridgeHalfWidth, 2.95f);
            HangFragment(chamber, "left", 7.20f, 8.8f, -3.45f, -2.18f, -1.6f);
            HangFragment(chamber, "right", 6.55f, 7.38f, 2.3f, 3.7f, -.92f);
            chamber.Box("bridge-rail-entry", BlockRole.Shell, GapStart, 6.15f, 0, .92f, -1.58f, -BridgeHalfWidth);
            chamber.Box("bridge-rail-extraction", BlockRole.Shell, 9.75f, StraightGapEnd, 0, .92f, BridgeHalfWidth, 1.58f);
            chamber.MapFloor("ground", Level, new Vector2(0, NearWall), new Vector2(GapStart, NearWall),
                new Vector2(GapStart, -BridgeHalfWidth), new Vector2(StraightGapEnd, -BridgeHalfWidth),
                new Vector2(StraightGapEnd, NearWall), new Vector2(length, NearWall), new Vector2(length, farWall),
                new Vector2(StraightGapEnd, farWall), new Vector2(StraightGapEnd, BridgeHalfWidth),
                new Vector2(GapStart, BridgeHalfWidth), new Vector2(GapStart, farWall), new Vector2(0, farWall));
            route.Add(Pose(12.9f, 0, 0));
        }

        private static void MakeBent(GrayBuilder chamber, List<ModulePose> route, float length, float farWall)
        {
            const float turnStart = 6.6f, turnNear = -1.65f, turnFar = 3.8f;
            const float outer = 12.65f, inner = 8.9f, landingNear = 6.1f, apronFar = 4.8f;
            chamber.Deck("bent-bridge-entry-span", GapStart, turnStart, 0, -BridgeHalfWidth, BridgeHalfWidth);
            chamber.Deck("bent-bridge-turn-pad", turnStart, outer, 0, turnNear, turnFar);
            chamber.Deck("bent-bridge-extraction-span", inner, outer, 0, turnFar, BentLanding);
            chamber.Deck("extraction-landing", landingNear, length, 0, BentLanding, farWall);
            chamber.Box("native-quota-working-volume", BlockRole.Free, BentAxis - 5f, BentAxis + 5f, 0, 34f, 5.1f, farWall);
            chamber.Box("severed-deck-entry", BlockRole.Obstacle, GapStart, 6.3f, -.64f, -.25f, -3.12f, -BridgeHalfWidth);
            chamber.Box("severed-deck-turn", BlockRole.Obstacle, 11.75f, 13.6f, -.78f, -.39f, 3.8f, 5.15f);
            HangFragment(chamber, "inside-turn", 7.05f, 8.38f, 3.97f, 5.62f, -1.12f);
            HangFragment(chamber, "outside-turn", 12.99f, 14.54f, .38f, 1.55f, -1.6f);
            chamber.Box("bridge-rail-turn", BlockRole.Shell, 7.1f, 9.35f, 0, .92f, -1.78f, turnNear);
            chamber.Box("bridge-rail-extraction", BlockRole.Shell, outer, outer + .13f, 0, .92f, 4.9f, BentLanding);
            chamber.MapFloor("ground", Level, new Vector2(0, NearWall), new Vector2(GapStart, NearWall),
                new Vector2(GapStart, -BridgeHalfWidth), new Vector2(turnStart, -BridgeHalfWidth),
                new Vector2(turnStart, turnNear), new Vector2(outer, turnNear), new Vector2(outer, BentLanding),
                new Vector2(length, BentLanding), new Vector2(length, farWall), new Vector2(landingNear, farWall),
                new Vector2(landingNear, BentLanding), new Vector2(inner, BentLanding), new Vector2(inner, turnFar),
                new Vector2(turnStart, turnFar), new Vector2(turnStart, BridgeHalfWidth),
                new Vector2(GapStart, BridgeHalfWidth), new Vector2(GapStart, apronFar), new Vector2(0, apronFar));
            route.AddRange(Bend(Pose(7f, 0, 0), 3.8f, Vector3.UnitZ));
            route.Add(Pose(BentAxis, 0, 6.5f, Vector3.UnitZ));
        }

        private static void HangFragment(GrayBuilder chamber, string id, float x0, float x1, float z0, float z1, float top)
        {
            chamber.Box($"hanging-{id}-panel", BlockRole.Obstacle, x0, x1, top - .32f, top, z0, z1);
            chamber.Box($"hanging-{id}-torn-edge", BlockRole.Obstacle, x0 + .31f, x1 - .16f,
                top - .57f, top - .32f, z0 + .06f, z1 - .17f);
            chamber.Box($"hanging-{id}-cable-a", BlockRole.Shell, x0 + .14f, x0 + .20f, top, 6f, z0 + .17f, z0 + .23f);
            chamber.Box($"hanging-{id}-cable-b", BlockRole.Shell, x1 - .21f, x1 - .15f, top, 6f, z1 - .19f, z1 - .13f);
            chamber.Box($"hanging-{id}-crossmember", BlockRole.Shell, x0 + .14f, x1 - .15f, 5.94f, 6.12f, z0, z1);
        }
    }
}

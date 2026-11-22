using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class MaintenanceTrench
    {
        public const string Id = "maintenance-trench";
        public const string Bridge = "service-plank";
        public const string Gate = "inspection-shutter";
        public const float BridgeZ = -4.7f;
        public const float GateX = 13.4f;
        public const float Opening = 1.50f;
        public const float GateTravel = 2.35f;
        public const float Length = 18;
        public const float PitStart = 6;
        public const float PitEnd = 11.5f;
        public const float PlankHalfWidth = .50f;
        public const float PlankThickness = .14f;

        public static GrayModule Create()
        {
            const float halfWidth = 7;
            const float height = 5;
            const float pitNear = -6.75f;
            const float pitFar = -2.55f;

            var room = new GrayBuilder(Id, 2 * halfWidth, Length, difficulty: 2);
            room.MapFloor("main", Level, new Vector2(0, -halfWidth), new Vector2(PitStart, -halfWidth), new Vector2(PitStart, pitFar),
                new Vector2(PitEnd, pitFar), new Vector2(PitEnd, -halfWidth), new Vector2(Length, -halfWidth),
                new Vector2(Length, halfWidth), new Vector2(0, halfWidth));
            float plankNear = BridgeZ - PlankHalfWidth, plankFar = BridgeZ + PlankHalfWidth;
            room.MapRectangle("plank", PitStart, PitEnd, 0, plankNear, plankFar);
            room.Deck("west-deck-near", 0, PitStart, 0, -halfWidth, plankNear);
            room.Deck("west-deck-far", 0, PitStart, 0, plankFar, pitFar);
            room.Deck("east-deck-near", PitEnd, Length, 0, -halfWidth, plankNear);
            room.Deck("east-deck-far", PitEnd, Length, 0, plankFar, pitFar);
            // Each lane owns a continuous support face through both landings.
            room.Deck("freight-bypass-deck", 0, Length, 0, pitFar, halfWidth);
            room.Box(Bridge, BlockRole.Floor, 0, Length, -PlankThickness, 0, plankNear, plankFar);
            room.Box("pit-west", BlockRole.Shell, PitStart - .25f, PitStart, -8, -.25f, pitNear, pitFar);
            room.Box("pit-east", BlockRole.Shell, PitEnd, PitEnd + .25f, -8, -.25f, pitNear, pitFar);
            room.Box("pit-south", BlockRole.Shell, PitStart, PitEnd, -8, 0, -halfWidth, pitNear);
            room.Box("pit-freight-fascia", BlockRole.Shell, PitStart, PitEnd, -8, -.25f, pitFar, pitFar + .25f);
            room.Box("pit-catch-wall", BlockRole.Shell, PitStart, PitEnd, -8, -7.75f, pitNear, pitFar);
            room.FallVolume("service-pit", PitStart, PitEnd, -7.60f, -4.2f, pitNear, pitFar);
            room.Box("freight-pit-rail", BlockRole.Shell, PitStart, PitEnd, 0, 1.10f, pitFar, pitFar + .09f);

            var west = room.Door("west", "main", Pose(0, 0, 0, -Vector3.UnitX), Cargo.Cart);
            var east = room.Door("east", "main", Pose(Length, 0, 0), Cargo.Cart);
            room.Roof(0, Length, height, -halfWidth, halfWidth);
            room.RectangleWalls(0, Length, 0, height, -halfWidth, halfWidth, west: west, east: east);
            float left = BridgeZ - Opening / 2, right = BridgeZ + Opening / 2;
            room.Box("shutter-south-guide", BlockRole.Shell, 12.15f, 14.75f, 0, height, left - .18f, left);
            room.Box("shutter-north-guide", BlockRole.Shell, 12.15f, 14.75f, 0, height, right, right + .18f);
            room.Box(Gate, BlockRole.Obstacle, GateX - .11f, GateX + .11f,
                GateTravel + .025f, GateTravel + 2.50f, left, right);

            room.Module.Landmarks.Add("plank-approach", new Vector3(5, 0, BridgeZ));
            room.Module.Landmarks.Add("inspection-bay", new Vector3(16.15f, 0, BridgeZ));
            room.Module.Landmarks.Add("hand-samples", new Vector3(16.15f, 0, BridgeZ));
            room.Route("freight-bypass", west.Id, east.Id, Cargo.Cart, Pose(-1.8f, 0, 0), Pose(Length + 1.8f, 0, 0));
            room.Route("entry-to-plank", west.Id, "plank-approach", Cargo.Hand,
                Pose(-1.8f, 0, 0), Pose(3.6f, 0, 0), Pose(3.6f, 0, 0, -Vector3.UnitZ),
                Pose(3.6f, 0, BridgeZ, -Vector3.UnitZ), Pose(3.6f, 0, BridgeZ), Pose(5, 0, BridgeZ));
            room.Module.Routes.Add(new HaulRoute("inspection-shortcut", "plank-approach", "inspection-bay", Cargo.Hand,
                new[] { Pose(5, 0, BridgeZ), Pose(16.15f, 0, BridgeZ) }, condition: "Shutter open"));
            room.Route("inspection-bay-bypass", west.Id, "inspection-bay", Cargo.Hand,
                Pose(-1.8f, 0, 0), Pose(16.15f, 0, 0), Pose(16.15f, 0, 0, -Vector3.UnitZ),
                Pose(16.15f, 0, BridgeZ, -Vector3.UnitZ));

            var fittings = new RoomDressing("trench", Level);
            fittings.Cabinet("isolation-cabinet", 4.4f, 0, 6.75f, 1, 1.3f, .92f, powered: true);
            fittings.Cabinet("inspection-spares", 16.9f, 0, 6.75f, 1, 1.5f, 1.02f);
            fittings.WallLamp("bypass-guide", 9.0f, 2.90f, 6.75f, 1, LampDuty.Guidance);
            fittings.WallLamp("trench-west-light", 4.3f, 3.3f, -6.75f, -1, LampDuty.Standby);
            fittings.WallLamp("trench-east-light", 15.0f, 3.3f, -6.75f, -1, LampDuty.Standby);
            room.AddDressing(fittings);
            return room.Module;
        }
    }
}

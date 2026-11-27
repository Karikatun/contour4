using System.Linq;
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
        public const float WestWait = 12.35f;
        public const float EastWait = 15.25f;
        public const string SampleSiteId = "inspection-wall-spares";
        public const string SampleReserveSiteId = "inspection-wall-reserve";
        public static readonly Vector3 SampleSite = new Vector3(17.20f, 0, BridgeZ);
        public static readonly Vector3 SampleReserveSite = new Vector3(17.20f, 0, -5.75f);
        public static readonly Vector3 SamplePickup = new Vector3(16.45f, 0, BridgeZ);
        internal const string Stand = "drain-inspection-stand";

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
            // The shutter crosses the whole hand passage. Both ways around its blade join the freight detour.
            const float standEdge = 2.35f;
            const float freightLane = 3.75f;
            room.Box(Stand, BlockRole.Obstacle, 7.55f, 13.59f, 0, 1.42f, pitFar + .08f, standEdge);
            room.Box("shutter-south-return", BlockRole.Shell, GateX - .17f, GateX + .17f, 0, height, -halfWidth, left);
            room.Box("shutter-stand-return", BlockRole.Shell, GateX - .17f, GateX + .17f, 0, height, right, standEdge);
            room.Box(Gate, BlockRole.Obstacle, GateX - .11f, GateX + .11f,
                GateTravel + .025f, GateTravel + 2.50f, left, right);

            const float approach = 5.25f;
            const float cartX = 15.68f;
            const float cartZ = -2.75f;
            var northEast = Vector3.Normalize(new Vector3(1, 0, 1));
            var southEast = Vector3.Normalize(new Vector3(1, 0, -1));
            var entry = Pose(-1.8f, 0, 0);
            var pickup = Pose(SamplePickup.X, 0, SamplePickup.Z);
            room.Module.Landmarks.Add("plank-approach", new Vector3(approach, 0, BridgeZ));
            room.Module.Landmarks.Add("shutter-wait-west", new Vector3(WestWait, 0, BridgeZ));
            room.Module.Landmarks.Add("shutter-wait-east", new Vector3(EastWait, 0, BridgeZ));
            room.Module.Landmarks.Add("hand-samples", SamplePickup);
            room.Module.Landmarks.Add("sample-loading", new Vector3(cartX, 0, cartZ));
            var freight = new[] { entry, Pose(2.25f, 0, 0), Pose(3.65f, 0, .55f, northEast),
                Pose(5.35f, 0, 2.80f, northEast), Pose(6.55f, 0, freightLane), Pose(14.50f, 0, freightLane) };
            room.Route("freight-bypass", west.Id, east.Id, Cargo.Cart,
                freight.Concat(new[] { Pose(15.65f, 0, 2.90f, southEast),
                    Pose(16.45f, 0, 1.02f, southEast), Pose(17.20f, 0, 0), Pose(Length + 1.8f, 0, 0) }).ToArray());
            room.Route("freight-sample-loading", west.Id, "sample-loading", Cargo.Cart,
                freight.Concat(new[] { Pose(cartX, 0, 2.80f, southEast),
                    Pose(cartX, 0, 1.50f, -Vector3.UnitZ), Pose(cartX, 0, cartZ, -Vector3.UnitZ) }).ToArray());
            room.Route("entry-to-plank", west.Id, "plank-approach", Cargo.Hand,
                entry, Pose(1.20f, 0, 0), Pose(3.50f, 0, -2.25f, southEast),
                Pose(approach, 0, BridgeZ, southEast), Pose(approach, 0, BridgeZ));
            room.Module.Routes.Add(new HaulRoute("inspection-shortcut", "plank-approach", "hand-samples", Cargo.Hand,
                new[] { Pose(approach, 0, BridgeZ), Pose(WestWait, 0, BridgeZ), Pose(EastWait, 0, BridgeZ), pickup },
                condition: "Shutter open"));
            room.Route("inspection-bay-bypass", west.Id, "hand-samples", Cargo.Hand,
                entry, Pose(2.10f, 0, 0), Pose(7.04f, 0, 2.86f, northEast), Pose(7.40f, 0, 2.90f),
                Pose(13.95f, 0, 2.90f), Pose(14.25f, 0, 2.60f, southEast),
                Pose(16.12f, 0, -3.10f, -Vector3.UnitZ), Pose(16.12f, 0, -3.10f), pickup);

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

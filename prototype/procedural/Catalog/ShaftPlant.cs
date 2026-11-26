using System.Linq;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class ShaftPlant
    {
        internal const float MiddleY = 3.6f;
        internal const float UpperY = 7.2f;
        internal const float DeckBack = -3.25f;
        internal const float BalconyWest = 6.15f;
        internal const float BalconyStep = 7.90f;
        internal const float BalconyEast = 16.20f;
        internal const float BalconyEdge = 2.94f;
        internal const float BalconyWestEdge = .80f;
        internal const float GalleryWest = 12.40f;
        internal const float GalleryEdge = 3.51f;
        internal const float InstrumentCabinetX = 14.95f;
        internal const float InstrumentCabinetZ = -.20f;
        internal const float SparesChestX = 20.70f;
        internal const float SparesChestZ = .88f;
        internal const float ServiceCoverHeight = 1.06f;
        internal const float RackX = 8.78f;
        internal const float RackZ = -1.05f;
        internal const float RackDepth = .72f;
        internal const float TableX = 15.60f;
        internal const float TableZ = 1.10f;
        internal const float TableDrawersX = TableX + .42f;

        public static GrayModule Create() => Create(cupboardAnnex: false);

        internal static GrayModule Create(bool cupboardAnnex)
        {
            const float length = 22.5f;
            const float ceiling = 11.2f;
            const float stairLine = -5.25f;
            const float middleY = MiddleY;
            const float topY = UpperY;
            var room = new GrayBuilder("shaft-plant", 15, length);
            room.Floor("ground", 0, length, 0, -7.5f, 7.5f);
            room.Deck("middle-landing", 9.75f, 11, middleY, -7.5f, DeckBack);
            room.Deck("control-balcony-west-wing", BalconyWest, BalconyStep, middleY, DeckBack, BalconyWestEdge);
            room.Deck("control-balcony", BalconyStep, BalconyEast, middleY, DeckBack, BalconyEdge);
            room.MapFloor("middle-landing", Pose(0, middleY, 0),
                new Vector2(9.75f, -7.5f), new Vector2(11, -7.5f), new Vector2(11, DeckBack),
                new Vector2(BalconyEast, DeckBack), new Vector2(BalconyEast, BalconyEdge),
                new Vector2(BalconyStep, BalconyEdge), new Vector2(BalconyStep, BalconyWestEdge),
                new Vector2(BalconyWest, BalconyWestEdge), new Vector2(BalconyWest, DeckBack),
                new Vector2(9.75f, DeckBack));
            room.Deck("upper-landing", 19, length, topY, -7.5f, DeckBack);
            room.Deck("service-gallery", GalleryWest, length, topY, DeckBack, GalleryEdge);
            room.MapFloor("upper-landing", Pose(0, topY, 0),
                new Vector2(19, -7.5f), new Vector2(length, -7.5f), new Vector2(length, GalleryEdge),
                new Vector2(GalleryWest, GalleryEdge), new Vector2(GalleryWest, DeckBack), new Vector2(19, DeckBack));
            room.Roof(0, length, ceiling, -7.5f, 7.5f);
            var west = room.Door("freight-entry", "ground", Pose(0, 0, 0, -Vector3.UnitX), Cargo.Cart);
            var upper = room.Door("upper-exit", "upper-landing", Pose(length, topY, stairLine),
                Cargo.Hand, width: cupboardAnnex ? Carry : 1.8f, height: 2.5f, depth: 2.5f);
            room.RectangleWalls(0, length, 0, ceiling, -7.5f, 7.5f, west: west, east: upper);
            if (cupboardAnnex)
            {
                // The walkable upper deck already fills the top of this wall segment.
                room.Module.Geometry.RemoveAll(block => block.Id == "east-wall-sill");
                room.Box("east-wall-sill", BlockRole.Shell, length - Skirt, length, 0, topY - Skirt,
                    stairLine - Carry / 2, stairLine + Carry / 2);
            }
            room.Box("shaft-apparatus", BlockRole.Obstacle, 7, 15, 0, 3.8f, 3.5f, 6.75f);
            const float rail = .12f;
            Guard("middle-guard-front", BalconyStep, BalconyEast, middleY, BalconyEdge - rail, BalconyEdge);
            Guard("middle-guard-west-wing-front", BalconyWest, BalconyStep, middleY, BalconyWestEdge - rail, BalconyWestEdge);
            Guard("middle-guard-step", BalconyStep, BalconyStep + rail, middleY, BalconyWestEdge - rail, BalconyEdge - rail);
            Guard("middle-guard-west", BalconyWest, BalconyWest + rail, middleY, DeckBack, BalconyWestEdge - rail);
            Guard("middle-guard-east", BalconyEast - rail, BalconyEast, middleY, DeckBack, BalconyEdge - rail);
            Guard("middle-guard-west-return", BalconyWest + rail, 9.75f, middleY, DeckBack, DeckBack + rail);
            Guard("middle-guard-east-return", 11, BalconyEast - rail, middleY, DeckBack, DeckBack + rail);
            Guard("upper-guard-front", GalleryWest, length - Skirt, topY, GalleryEdge - rail, GalleryEdge);
            Guard("upper-guard-west", GalleryWest, GalleryWest + rail, topY, DeckBack, GalleryEdge - rail);
            Guard("upper-guard-west-return", GalleryWest + rail, 19, topY, DeckBack, DeckBack + rail);
            room.Box("balcony-console", BlockRole.Obstacle, 13.17f, 14.33f, middleY, 4.84f, -2.99f, -1.21f);
            room.Box("gallery-lockers", BlockRole.Obstacle, 21.66f, 22.25f, topY, 9.22f, -2.91f, -.58f);
            room.Box("plant-balcony-instrument-cabinet", BlockRole.Obstacle, InstrumentCabinetX - .70f,
                InstrumentCabinetX + .70f, middleY, middleY + ServiceCoverHeight,
                InstrumentCabinetZ - .36f, InstrumentCabinetZ + .36f);
            room.Box("plant-gallery-spares-chest", BlockRole.Obstacle, SparesChestX - .70f,
                SparesChestX + .70f, topY, topY + ServiceCoverHeight, SparesChestZ - .40f, SparesChestZ + .40f);
            const float rackWidth = 1.20f, rackSide = .06f;
            const float bayHeight = RoomDressing.RackShelterHeight;
            Furniture("balcony-rack-cases-back", RackX, middleY, RackZ - (RackDepth - .05f) / 2,
                rackWidth, 1.055f, .05f);
            Furniture("balcony-rack-shelter-roof", RackX, middleY + bayHeight, RackZ, rackWidth, .07f, RackDepth);
            Furniture("balcony-rack-case-shelf", RackX, middleY + .995f, RackZ, rackWidth, .06f, RackDepth);
            Furniture("balcony-rack-shelf", RackX, middleY + 1.46f, RackZ, rackWidth, .06f, RackDepth);
            foreach (int side in new[] { -1, 1 })
            {
                Furniture($"balcony-rack-cheek-{side}", RackX + side * .57f, middleY, RackZ, rackSide, bayHeight, RackDepth);
                Furniture($"balcony-rack-case-{side}", RackX + side * .265f, middleY + .67f, RackZ + .008f,
                    .50f, .31f, .535f);
                foreach (int face in new[] { -1, 1 })
                    Furniture($"balcony-rack-upright-{side}-{face}", RackX + side * .57f, middleY + bayHeight,
                        RackZ + face * (RackDepth - rackSide) / 2, rackSide, .86f, rackSide);
            }
            Furniture("gallery-table-top", TableX, topY + .99f, TableZ, 1.80f, .08f, .80f);
            Furniture("gallery-table-drawers", TableDrawersX, topY, TableZ, .84f, .99f, .68f);
            Furniture("gallery-table-near-leg", TableX - .84f, topY, TableZ - .34f, .08f, .99f, .08f);
            Furniture("gallery-table-far-leg", TableX - .84f, topY, TableZ + .34f, .08f, .99f, .08f);
            Chair("gallery-chair-end", 13.97f, TableZ, backAlongX: true);
            Chair("gallery-chair-side", 14.98f, .18f, backAlongX: false);
            room.Module.Landmarks.Add("heavy-parts", new Vector3(14, 0, 1.35f));
            room.Module.Landmarks.Add("stairs-bottom", new Vector3(1.1f, 0, stairLine));
            room.Module.Landmarks.Add("intermediate-loot", new Vector3(10.35f, middleY, stairLine));
            room.Module.Landmarks.Add("pump-controls", new Vector3(12.45f, middleY, -2.12f));
            room.Module.Landmarks.Add("upper-loot", new Vector3(20, topY, stairLine));
            room.Module.Landmarks.Add("gallery-spares", new Vector3(17.1f, topY, -1.72f));
            var lowerStairs = room.Stairs("lower-stair", 1.75f, 9.75f, 0, middleY, -6.5f, -4, 20);
            var upperStairs = room.Stairs("upper-stair", 11, 19, middleY, topY, -6.5f, -4, 20);
            room.Route("freight-access", west.Id, "heavy-parts", Cargo.Cart,
                new[] { Pose(-1.8f, 0, 0) }.Concat(LaneChange(1, 6, 0, 1.35f)).Append(Pose(14, 0, 1.35f)).ToArray());
            room.Route("entry-to-stairs", west.Id, "stairs-bottom", Cargo.Hand, Pose(-1.8f, 0, 0),
                Pose(1.1f, 0, 0), Pose(1.1f, 0, 0, -Vector3.UnitZ),
                Pose(1.1f, 0, stairLine, -Vector3.UnitZ), Pose(1.1f, 0, stairLine));
            room.Module.Routes.Add(new HaulRoute("first-flight", "stairs-bottom", "intermediate-loot", Cargo.Hand,
                new[] { Pose(1.1f, 0, stairLine) }.Concat(lowerStairs).Append(Pose(10.35f, middleY, stairLine)).ToArray(),
                stepClearance: .39f));
            room.Module.Routes.Add(new HaulRoute("second-flight", "intermediate-loot", "upper-loot", Cargo.Hand,
                new[] { Pose(10.35f, middleY, stairLine) }.Concat(upperStairs).Append(Pose(20, topY, stairLine)).ToArray(),
                stepClearance: .39f));
            room.Route("upper-exit-traverse", "upper-loot", upper.Id, Cargo.Hand,
                Pose(20, topY, stairLine), Pose(24.3f, topY, stairLine));
            room.Route("balcony-controls", "intermediate-loot", "pump-controls", Cargo.Hand,
                Pose(10.35f, middleY, stairLine, Vector3.UnitZ), Pose(10.35f, middleY, -2.12f, Vector3.UnitZ),
                Pose(10.35f, middleY, -2.12f), Pose(12.45f, middleY, -2.12f));
            room.Route("upper-gallery-service", "upper-loot", "gallery-spares", Cargo.Hand,
                Pose(20, topY, stairLine, Vector3.UnitZ), Pose(20, topY, -1.72f, Vector3.UnitZ),
                Pose(20, topY, -1.72f, -Vector3.UnitX), Pose(17.1f, topY, -1.72f, -Vector3.UnitX));
            var fitout = new RoomDressing("plant", Level);
            fitout.Bench("service-workbench", 17.35f, 0, -7.25f, -1, 2.7f, FixedSeat.Chair,
                powered: true, active: true);
            fitout.Cabinet("feed-switchgear", 17.35f, 0, 7.25f, 1, 2.45f, 1.58f, powered: true);
            fitout.LowCabinet("pump-service-drawers", 4.82f, 0, 5.42f, 1, 1.77f);
            fitout.PartsRack("pump-spares-rack", 20.7f, 0, 7.25f, 1, 1.53f);
            fitout.Box("shaft-apparatus-meter", 10.8f, 3.56f, 6.82f, 1.18f, .43f, .18f);
            fitout.EquipmentFeed("shaft-apparatus-feed", 10.8f, 3.61f, 6.86f, 4.42f, 7.25f, 1);
            fitout.WallLamp("pump-task-west", 9.25f, 4.75f, 7.25f, 1, LampDuty.Work);
            fitout.WallLamp("pump-task-east", 15.9f, 4.2f, 7.25f, 1, LampDuty.Work, -.36f);
            fitout.WallLamp("bench-task", 17.2f, 2.62f, -7.25f, -1, LampDuty.Work);
            fitout.WallLamp("first-flight-marker", 4.2f, 2.4f, -7.25f, -1, LampDuty.Guidance, .55f);
            fitout.WallLamp("mid-landing-marker", 10.38f, middleY + 1.65f, -7.25f, -1, LampDuty.Guidance, .52f);
            fitout.WallLamp("second-flight-marker", 15.4f, middleY + 3.1f, -7.25f, -1, LampDuty.Guidance, .55f);
            fitout.AisleStrip("upper-exit-marker", 20.9f, topY + .44f, -7.25f, -1, .74f);
            fitout.AisleStrip("pump-service-strip", 4.65f, .41f, 7.25f, 1, .80f);
            fitout.VerifyCover("feed-switchgear-body", new Vector3(15.6f, 0, 6.78f),
                new Vector3(19.2f, 0, 6.78f), new Vector3(0, 0, -1.58f));
            fitout.VerifyLowCover("pump-service-drawers-body", new Vector3(3.32f, 0, 5.42f),
                new Vector3(6.36f, 0, 5.42f), new Vector3(0, 0, 1.02f), new Vector3(3.32f, 0, 1.35f));
            fitout.VerifyLowCover("balcony-instrument-cabinet", new Vector3(InstrumentCabinetX, middleY, -.91f),
                new Vector3(InstrumentCabinetX, middleY, .76f), new Vector3(-1.15f, 0, 0),
                new Vector3(12.45f, middleY, -.91f), room.Module.Landmarks["pump-controls"]);
            fitout.VerifyLowCover("gallery-spares-chest", new Vector3(SparesChestX, topY, -.12f),
                new Vector3(SparesChestX, topY, 1.88f), new Vector3(-1.20f, 0, 0),
                new Vector3(19.50f, topY, -1.72f), new Vector3(20, topY, -1.72f));
            fitout.VerifyLowCover("balcony-rack-cases-back", new Vector3(RackX, middleY, -1.95f),
                new Vector3(RackX, middleY, -.15f), new Vector3(1.04f, 0, 0),
                new Vector3(10.35f, middleY, -1.95f), new Vector3(10.35f, middleY, -2.12f));
            fitout.VerifyLowCover("gallery-table-drawers", new Vector3(TableDrawersX, topY, .05f),
                new Vector3(TableDrawersX, topY, 2.12f), new Vector3(1.03f, 0, 0),
                new Vector3(17.05f, topY, -1.72f), room.Module.Landmarks["gallery-spares"]);
            room.AddDressing(fitout);
            var galleryLights = new RoomDressing("gallery", Pose(length - Skirt, topY, -1.72f, -Vector3.UnitZ));
            galleryLights.WallLamp("locker-task", 0, 2.38f, 0, 1, LampDuty.Work);
            room.AddDressing(galleryLights);
            return room.Module;

            void Guard(string id, float x0, float x1, float y, float z0, float z1) =>
                room.Box(id, BlockRole.Shell, x0, x1, y, y + 1.08f, z0, z1);

            void Furniture(string id, float x, float y, float z, float width, float height, float depth) =>
                room.Box("plant-" + id, BlockRole.Obstacle, x - width / 2, x + width / 2, y, y + height,
                    z - depth / 2, z + depth / 2);

            void Chair(string id, float x, float z, bool backAlongX)
            {
                Furniture(id + "-seat", x, topY + .44f, z, .49f, .07f, .49f);
                Furniture(id + "-back", x - (backAlongX ? .21f : 0), topY + .65f,
                    z - (backAlongX ? 0 : .21f), backAlongX ? .07f : .45f, .30f, backAlongX ? .45f : .07f);
                foreach (int side in new[] { -1, 1 })
                foreach (int face in new[] { -1, 1 })
                {
                    bool underBack = backAlongX ? side < 0 : face < 0;
                    Furniture($"{id}-leg-{side}-{face}", x + side * .21f, topY, z + face * .21f,
                        .06f, underBack ? .66f : .44f, .06f);
                }
            }
        }
    }
}

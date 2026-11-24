using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class NicheChamber
    {
        public static GrayModule Create()
        {
            const float width = 15;
            const float length = 22.5f;
            const float half = width / 2;
            const float nicheNear = 2.25f;
            const float nicheX = 11.25f;
            var group = new GrayBuilder("niche-chamber", width, length);
            group.MapFloor("hall", Level, new Vector2(0, -half), new Vector2(length, -half),
                new Vector2(length, half), new Vector2(15, half), new Vector2(15, nicheNear),
                new Vector2(7.5f, nicheNear), new Vector2(7.5f, half), new Vector2(0, half));
            group.Deck("hall-deck", 0, length, 0, -half, nicheNear);
            group.Deck("left-wing-deck", 0, 7.5f, 0, nicheNear, half);
            group.Deck("right-wing-deck", 15, length, 0, nicheNear, half);
            group.Roof(0, length, Height, -half, nicheNear);
            group.Box("left-wing-roof", BlockRole.Shell, 0, 7.5f, Height, Height + Skirt, nicheNear, half);
            group.Box("right-wing-roof", BlockRole.Shell, 15, length, Height, Height + Skirt, nicheNear, half);
            var west = group.Door("hall-west", "hall", Pose(0, 0, 0, -Vector3.UnitX), Cargo.Cart);
            var east = group.Door("hall-east", "hall", Pose(length, 0, 0), Cargo.Cart);
            var portal = group.Door("hall-niche-portal", "hall", Pose(nicheX, 0, nicheNear, Vector3.UnitZ),
                Cargo.Hand, DockUse.DoorPortal, Carry, 2.5f);
            group.Wall("west-wall", west.Threshold, half, Height, west);
            group.Wall("east-wall", east.Threshold, half, Height, east);
            group.Wall("niche-wall", portal.Threshold, 3.75f, Height, portal);
            group.Box("hall-near-wall", BlockRole.Shell, Skirt, length - Skirt, 0, Height, -half, -half + Skirt);
            group.Box("west-wing-wall", BlockRole.Shell, Skirt, 7.5f - Skirt, 0, Height, half - Skirt, half);
            group.Box("east-wing-wall", BlockRole.Shell, 15 + Skirt, length - Skirt, 0, Height, half - Skirt, half);
            group.Box("west-niche-return", BlockRole.Shell, 7.5f - Skirt, 7.5f, 0, Height, nicheNear, half);
            group.Box("east-niche-return", BlockRole.Shell, 15, 15 + Skirt, 0, Height, nicheNear, half);
            group.Route("hall-cart-past-niche", west.Id, east.Id, Cargo.Cart,
                Pose(-1.8f, 0, 0), Pose(length + 1.8f, 0, 0));
            group.Route("hall-hand-to-niche", west.Id, portal.Id, Cargo.Hand,
                Pose(-1.8f, 0, 0), Pose(nicheX, 0, 0), Pose(nicheX, 0, 0, Vector3.UnitZ),
                Pose(nicheX, 0, nicheNear - .70f, Vector3.UnitZ));
            var wing = new RoomDressing("freight-hall", Level);
            wing.Bench("intake-manifest", 4.55f, 0, 7.25f, 1, 2.25f, FixedSeat.Chair, powered: true);
            wing.Cabinet("isolated-breakers", 3.6f, 0, -7.25f, -1, 1.6f, 1.06f, powered: true);
            wing.LowCabinet("freight-manifest-drawers", 18.28f, 0, 5.69f, 1, 1.82f);
            wing.WallLamp("manifest-standby", 4.55f, 3.31f, 7.25f, 1, LampDuty.Standby);
            wing.WallLamp("freight-west-guide", 3.65f, 2.51f, -7.25f, -1, LampDuty.Guidance, .62f);
            wing.AisleStrip("freight-east-guide", 18.9f, .43f, 7.25f, 1, .82f);
            wing.TaskPendant("secondary-inventory-pendant", 18.28f, Height, 5.7f, .62f, LampDuty.Standby);
            wing.WallLamp("side-cell-portal-light", 9.55f, 2.97f, 2f, 1, LampDuty.Guidance, .65f);
            wing.VerifyLowCover("freight-manifest-drawers-body", new Vector3(16.73f, 0, 5.69f),
                new Vector3(19.86f, 0, 5.69f), new Vector3(0, 0, -1.02f), new Vector3(16.73f, 0, 0));
            group.AddDressing(wing);
            group.AddPart("cell", SideCell(),
                new ModulePose(new Vector3(nicheX, 0, nicheNear + Skirt), Vector3.UnitY, Vector3.UnitZ));
            group.Module.InternalPortals.Add((portal.Id, "cell-portal"));
            return group.Module;
        }

        private static GrayModule SideCell()
        {
            var cell = new GrayBuilder("side-cell", 7.5f, 7.5f);
            cell.Floor("floor", 0, 7.5f, 0, -3.75f, 3.75f);
            cell.Roof(0, 7.5f, Height, -3.75f, 3.75f);
            var portal = cell.Door("portal", "floor", Pose(0, 0, 0, -Vector3.UnitX),
                Cargo.Hand, DockUse.DoorPortal, Carry, 2.5f);
            var service = cell.Door("service", "floor", Pose(7.5f, 0, 0), Cargo.Cart);
            cell.RectangleWalls(0, 7.5f, 0, Height, -3.75f, 3.75f, west: portal, east: service);
            cell.Module.Landmarks.Add("service-dropoff", new Vector3(3.6f, 0, 0));
            cell.Route("service-cart-access", service.Id, "service-dropoff", Cargo.Cart,
                Pose(9.3f, 0, 0, -Vector3.UnitX), Pose(3.6f, 0, 0, -Vector3.UnitX));
            cell.Route("service-hand-to-portal", service.Id, portal.Id, Cargo.Hand,
                Pose(9.3f, 0, 0, -Vector3.UnitX), Pose(.70f, 0, 0, -Vector3.UnitX));
            var fitout = new RoomDressing("side-instrument-room", Level);
            fitout.Bench("calibration-records-station", 3.36f, 0, -3.5f, -1, 1.95f, FixedSeat.Chair,
                powered: true, active: true);
            fitout.Cabinet("instrument-cabinet", 4.14f, 0, 3.5f, 1, 1.32f, .92f);
            fitout.LockerBank("record-lockers", 2.12f, 0, 3.5f, 1, 2);
            fitout.WallLamp("instrument-task-light", 3.35f, 3.41f, -3.5f, -1, LampDuty.Work);
            fitout.WallLamp("freight-threshold-marker", 5.75f, 2.41f, 3.5f, 1, LampDuty.Guidance, .61f);
            cell.AddDressing(fitout);
            return cell.Module;
        }
    }
}

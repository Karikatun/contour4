using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class GalleryChamber
    {
        public static GrayModule Create()
        {
            var group = new GrayBuilder("gallery-chamber", 19.5f, 15);
            group.AddPart("lab", ObservationLab.Create(), Level);
            var chamber = new GrayBuilder("upper-test-room", 7.5f, 7.5f);
            chamber.Floor("floor", 0, 7.5f, 0, -3.75f, 3.75f);
            chamber.RoofHatch("hatch", "floor", 0, 7.5f, Height, -3.75f, 3.75f, 3.5f, -1.2f, 2.4f, 2.75f);
            var entry = chamber.Door("freight", "floor", Pose(0, 0, 0, -Vector3.UnitX), Cargo.Cart);
            chamber.RectangleWalls(0, 7.5f, 0, Height, -3.75f, 3.75f, west: entry);
            chamber.Box("heavy-crates", BlockRole.Obstacle, 6.35f, 7.25f, 0, 1.6f, -2.25f, 2.25f);
            chamber.Module.Landmarks.Add("crate-approach", new Vector3(4.7f, 0, 0));
            chamber.Route("own-freight-approach", entry.Id, "crate-approach", Cargo.Cart,
                Pose(-1.8f, 0, 0), Pose(4.7f, 0, 0));
            chamber.Route("hatch-approach", entry.Id, "hatch", Cargo.Hand, Pose(-1.8f, 0, 0), Pose(4.7f, 0, 0));
            var fitout = new RoomDressing("lens-inspection", Level);
            fitout.Bench("sample-transfer-table", 3.53f, 0, -3.5f, -1, 2.18f, FixedSeat.Chair, powered: true);
            fitout.Cabinet("transfer-locker", 5.25f, 0, 3.5f, 1, 1.3f, .8f);
            fitout.PartsRack("lens-trays", 2.7f, 0, 3.5f, 1, 1.6f);
            fitout.WallLamp("transfer-station-standby", 3.5f, 3.3f, -3.5f, -1, LampDuty.Standby);
            fitout.AisleStrip("transfer-hatch-route", 4.2f, .42f, 3.5f, 1, .74f);
            chamber.AddDressing(fitout);
            group.AddPart("small", chamber.Module,
                new ModulePose(new Vector3(7.5f, 7.35f, -7.75f), Vector3.UnitX, -Vector3.UnitZ));
            group.Module.InternalPortals.Add(("lab-gallery-hatch", "small-hatch"));
            group.Route("gallery-hatch-landing", "lab-gallery-loot", "lab-gallery-hatch", Cargo.Hand,
                Pose(11.75f, 3.6f, -5.25f), Pose(12.35f, 3.6f, -5.25f));
            return group.Module;
        }
    }
}

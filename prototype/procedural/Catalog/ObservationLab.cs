using System.Linq;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class ObservationLab
    {
        public static GrayModule Create(bool brokenFloor = false)
        {
            var room = new GrayBuilder(brokenFloor ? "observation-lab-broken-floor" : "observation-lab", 15, 15,
                brokenFloor ? 2 : 0);
            const float upperY = 3.6f;
            const float roofY = 7.45f;
            if (brokenFloor)
                BrokenFloor(room);
            else
                room.Floor("lower", 0, 15, 0, -7.5f, 7.5f);
            room.Floor("gallery", 10.25f, 15, upperY, -7.5f, -3.25f);
            room.RoofHatch("gallery-hatch", "gallery", 0, 15, roofY, -7.5f, 7.5f, 11.15f, -6.45f, 2.4f, 3.35f);
            var west = room.Door("freight-entry", "lower", Pose(0, 0, 0, -Vector3.UnitX), Cargo.Cart);
            var east = room.Door("lower-portal-door", "lower", Pose(15, 0, 0), Cargo.Cart,
                DockUse.Physical | DockUse.DoorPortal);
            room.RectangleWalls(0, 15, 0, roofY, -7.5f, 7.5f, west: west, east: east);
            room.Box("observation-stand", BlockRole.Obstacle, 6.3f, 8.7f, 0, 1.35f, -1.55f, 1.55f);
            room.Box("gallery-rail", BlockRole.Shell, 10.25f, 14.75f, upperY, upperY + 1, -3.4f, -3.25f);
            room.Box("stair-landing-rail-west", BlockRole.Shell, 10.25f, 10.4f, upperY, upperY + 1, -7.25f, -6.5f);
            room.Box("stair-landing-rail-east", BlockRole.Shell, 10.25f, 10.4f, upperY, upperY + 1, -4f, -3.4f);
            room.Module.Landmarks.Add("stand-approach", new Vector3(7.5f, 0, 4.3f));
            room.Module.Landmarks.Add("stair-bottom", new Vector3(1.1f, 0, -5.25f));
            room.Module.Landmarks.Add("gallery-loot", new Vector3(12.35f, upperY, -5.25f));
            room.Module.Landmarks.Add("gallery-view", new Vector3(12.35f, upperY, -3.8f));
            var lower = new[] { Pose(-1.8f, 0, 0) }.Concat(LaneChange(.65f, 5.9f, 0, 4.3f))
                .Append(Pose(7.5f, 0, 4.3f)).ToArray();
            room.Route("lower-stand-approach", west.Id, "stand-approach", Cargo.Cart, lower);
            room.Route("lower-cart-bypass", west.Id, east.Id, Cargo.Cart, lower
                .Concat(LaneChange(9.1f, 14.35f, 4.3f, 0)).Append(Pose(16.8f, 0, 0)).ToArray());
            var stairs = room.Stairs("galley-stair", 2.25f, 10.25f, 0, upperY, -6.5f, -4, 20);
            room.Route("entry-to-stairs", west.Id, "stair-bottom", Cargo.Hand, Pose(-1.8f, 0, 0),
                Pose(1.1f, 0, 0), Pose(1.1f, 0, 0, -Vector3.UnitZ),
                Pose(1.1f, 0, -5.25f, -Vector3.UnitZ), Pose(1.1f, 0, -5.25f));
            room.Module.Routes.Add(new HaulRoute("stair-to-gallery", "stair-bottom", "gallery-loot", Cargo.Hand,
                new[] { Pose(1.1f, 0, -5.25f) }.Concat(stairs).Append(Pose(12.35f, upperY, -5.25f)).ToArray(),
                stepClearance: .39f));
            var lowerFitout = new RoomDressing("observation-lower", Level);
            lowerFitout.Bench("instrument-bench", 4.65f, 0, 7.25f, 1, 2.65f, FixedSeat.Chair,
                powered: true, active: !brokenFloor);
            lowerFitout.Cabinet("recording-cabinet", 1.85f, 0, 7.25f, 1, 1.52f, 1.28f, powered: true);
            lowerFitout.LowCabinet("film-drawers", 12.88f, 0, 6.38f, 1, 1.74f);
            // Service equipment uses the space below the gallery, away from the stair and test shaft.
            lowerFitout.LockerBank("under-gallery-records", 12.40f, 0, -7.25f, -1, 3);
            lowerFitout.PartsRack("film-transport-rack", 14.14f, 0, -7.25f, -1, 1.22f);
            lowerFitout.WallLamp("records-worklight", 12.4f, 2.91f, -7.25f, -1, LampDuty.Standby);
            lowerFitout.LootBlock(brokenFloor ? "sealed-camera-cases" : "optics-alignment-cradle",
                4.28f, 0, -2.73f, -1, 2.45f, 1.20f, brokenFloor ? 1.26f : 1.06f);
            lowerFitout.Box("optics-cradle-lock", 4.93f, .78f, -2.12f, .24f, .15f, .06f, DressingSurface.SteelTrim);
            lowerFitout.TaskPendant("optics-service-light", 4.28f, roofY, -2.73f, 2.76f,
                brokenFloor ? LampDuty.Standby : LampDuty.Work);
            lowerFitout.Box("observation-stand-sensor", 6.79f, 1.55f, 1.06f, .72f, .40f, .56f);
            lowerFitout.Box("observation-stand-indicator", 6.81f, 1.56f, 1.348f, .36f, .11f, .02f,
                DressingSurface.StandbyLens);
            lowerFitout.OverheadFeed("observation-stand", 6.79f, 1.65f, 1.06f, roofY, 7.25f, 1);
            lowerFitout.TaskPendant("stand-overhead-light", 7.65f, roofY, 1.55f, 1.62f,
                brokenFloor ? LampDuty.Offline : LampDuty.Work);
            lowerFitout.TaskPendant("freight-work-area-overhead", 5.05f, roofY, 3.6f, 1.62f,
                brokenFloor ? LampDuty.Standby : LampDuty.Work);
            lowerFitout.WallLamp("instrument-work-light", 4.65f, 3.35f, 7.25f, 1,
                brokenFloor ? LampDuty.Standby : LampDuty.Work);
            lowerFitout.WallLamp("stand-work-light-west", 5.8f, 4.25f, 7.25f, 1, LampDuty.Work, .3f);
            lowerFitout.WallLamp("stand-work-light-east", 10.7f, 4.25f, 7.25f, 1,
                brokenFloor ? LampDuty.Standby : LampDuty.Work, -.3f);
            lowerFitout.AisleStrip("stair-lower-marker", 2.85f, .62f, -7.25f, -1, .78f);
            lowerFitout.WallLamp("stair-mid-marker", 7.0f, 3.28f, -7.25f, -1, LampDuty.Guidance, .55f);
            if (brokenFloor)
                lowerFitout.WallLamp("shaft-isolation-marker", 10.25f, 2.45f, 7.25f, 1, LampDuty.Standby);
            lowerFitout.VerifyCover("recording-cabinet-body", new Vector3(.57f, 0, 6.65f),
                new Vector3(2.98f, 0, 6.65f), new Vector3(0, 0, -1.14f));
            lowerFitout.VerifyLowCover("film-drawers-body", new Vector3(11.39f, 0, 6.38f),
                new Vector3(14.39f, 0, 6.38f), new Vector3(0, 0, -.96f), new Vector3(11.39f, 0, 3.7f));
            room.AddDressing(lowerFitout);
            var galleryFitout = new RoomDressing("observation-gallery", Level);
            galleryFitout.Bench("operator-desk", 14.23f, upperY, -7.25f, -1, .96f, FixedSeat.OperatorChair,
                powered: true, active: true);
            galleryFitout.WallLamp("gallery-console-task", 14.13f, 6.8f, -7.25f, -1, LampDuty.Work);
            galleryFitout.WallLamp("hatch-threshold-marker", 11.2f, 6.05f, -7.25f, -1, LampDuty.Guidance, .35f);
            galleryFitout.AisleStrip("gallery-baseboard-strip", 12.93f, upperY + .42f, -7.25f, -1, .78f);
            room.AddDressing(galleryFitout);
            return room.Module;
        }

        private static void BrokenFloor(GrayBuilder room)
        {
            room.MapRectangle("lower", 0, 15, 0, -7.5f, 7.5f);
            room.Deck("west-lower-deck", 0, 9.8f, 0, -7.5f, 7.5f);
            room.Deck("east-lower-deck", 10.75f, 15, 0, -7.5f, 7.5f);
            room.Deck("near-shaft-deck", 9.8f, 10.75f, 0, -7.5f, -1.5f);
            room.Deck("far-shaft-deck", 9.8f, 10.75f, 0, 1.5f, 7.5f);
            room.Deck("shaft-bottom", 9.55f, 11, -2.25f, -1.75f, 1.75f);
            room.FallVolume("observation-shaft", 9.8f, 10.75f, -2.40f, -1.15f, -1.5f, 1.5f);
            room.Box("shaft-west", BlockRole.Shell, 9.55f, 9.8f, -2.25f, -.25f, -1.75f, 1.75f);
            room.Box("shaft-east", BlockRole.Shell, 10.75f, 11, -2.25f, -.25f, -1.75f, 1.75f);
            room.Box("shaft-near", BlockRole.Shell, 9.8f, 10.75f, -2.25f, -.25f, -1.75f, -1.5f);
            room.Box("shaft-far", BlockRole.Shell, 9.8f, 10.75f, -2.25f, -.25f, 1.5f, 1.75f);
            room.Module.Landmarks.Add("shaft-near-lip", new Vector3(9.4f, 0, 0));
            var landing = Pose(9.4f, 0, 0, -Vector3.UnitX);
            var launch = Pose(11.55f, 0, 0, -Vector3.UnitX);
            ModulePose[] arc = Enumerable.Range(0, 21).Select(i =>
            {
                float t = i / 20f;
                Vector3 point = Vector3.Lerp(launch.Position, landing.Position, t);
                return Pose(point.X, 2.24f * t * (1 - t), 0, -Vector3.UnitX);
            }).ToArray();
            room.Module.Routes.Add(new HaulRoute("jump-with-hand-cargo", "lower-portal-door", "shaft-near-lip",
                Cargo.Hand, new[] { Pose(16.8f, 0, 0, -Vector3.UnitX) }.Concat(arc).ToArray(),
                condition: "Jump", flight: (launch.Position, landing.Position)));
        }
    }
}

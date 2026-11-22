using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class OpposedChambers
    {
        private const float Length = 7.5f;
        private const float Half = 3.75f;
        private const float HatchWidth = 2.4f;
        private const float HatchX = 4.75f;
        private const float Ceiling = 4.25f;
        private const string Plug = "common-hatch-plug";

        public static GrayModule Create(bool floorToFloor = false)
        {
            var group = new GrayBuilder(floorToFloor ? "floor-to-floor-chambers" : "top-to-top-chambers", 7.5f, Length);
            float slabBase = floorToFloor ? -Skirt : Ceiling;
            float slabTop = slabBase + Skirt;
            AddCommonPlate(group, slabBase, slabTop, floorToFloor ? BlockRole.Floor : BlockRole.Shell);
            AddChamber(group, "a", new ModulePose(Vector3.Zero, Vector3.UnitX, Vector3.UnitY), floorToFloor,
                floorToFloor ? slabBase : slabTop);
            float otherY = floorToFloor ? -Skirt : slabTop + Ceiling;
            AddChamber(group, "b", new ModulePose(Vector3.UnitY * otherY, Vector3.UnitX, -Vector3.UnitY), floorToFloor,
                floorToFloor ? slabTop : slabBase);
            group.Module.InternalPortals.Add(("a-hatch", "b-hatch"));
            return group.Module;
        }

        private static void AddCommonPlate(GrayBuilder group, float y0, float y1, BlockRole role)
        {
            float halfHatch = HatchWidth / 2;
            float endHatch = HatchX + HatchWidth;
            group.Box("common-plate-west", role, 0, HatchX, y0, y1, -Half, Half);
            group.Box("common-plate-east", role, endHatch, Length, y0, y1, -Half, Half);
            group.Box("common-plate-near", role, HatchX, endHatch, y0, y1, -Half, -halfHatch);
            group.Box("common-plate-far", role, HatchX, endHatch, y0, y1, halfHatch, Half);
            group.Box(Plug, BlockRole.Plug, HatchX, endHatch, y0, y1, -halfHatch, halfHatch);
        }

        private static void AddChamber(GrayBuilder group, string id, ModulePose chamber, bool floorToFloor, float hatchY)
        {
            ModulePose At(float forward, float up, float lateral = 0) =>
                new ModulePose(chamber.Point(forward, up, lateral), chamber.Forward, chamber.Up);
            void Box(string name, BlockRole role, float x0, float x1, float y0, float y1, float z0, float z1) =>
                group.Module.Geometry.Add(Block.Box($"{id}-{name}", role, chamber, x0, x1, y0, y1, z0, z1));
            var west = group.Door($"{id}-west", id, new ModulePose(chamber.Position, -chamber.Forward, chamber.Up), Cargo.Cart);
            var hatchPose = new ModulePose(new Vector3(HatchX, hatchY, 0),
                floorToFloor ? -chamber.Up : chamber.Up, chamber.Forward);
            var hatch = group.Door($"{id}-hatch", id, hatchPose, Cargo.Hand,
                floorToFloor ? DockUse.LowerPortal : DockUse.UpperPortal, HatchWidth, HatchWidth,
                depth: floorToFloor ? 2.7f : 2.9f, mapPoint: new Vector2(HatchX + HatchWidth / 2, 0), plugId: Plug);
            group.MapFloor(id, chamber, new Vector2(0, -Half), new Vector2(Length, -Half),
                new Vector2(Length, Half), new Vector2(0, Half));
            if (floorToFloor)
                Box("roof", BlockRole.Shell, 0, Length, Ceiling, Ceiling + Skirt, -Half, Half);
            else
                Box("floor", BlockRole.Floor, 0, Length, -Skirt, 0, -Half, Half);
            group.Wall($"{id}-west-wall", west.Threshold, Half, Ceiling, west);
            group.Wall($"{id}-east-wall", At(Length, 0), Half, Ceiling);
            Box("near-wall", BlockRole.Shell, Skirt, Length - Skirt, 0, Ceiling, -Half, -Half + Skirt);
            Box("far-wall", BlockRole.Shell, Skirt, Length - Skirt, 0, Ceiling, Half - Skirt, Half);
            var dropoff = At(floorToFloor ? 2.9f : 1.9f, 0);
            group.Module.Landmarks.Add($"{id}-cargo-staging", dropoff.Position);
            group.Route($"{id}-cargo-staging", west.Id, $"{id}-cargo-staging", Cargo.Cart, At(-1.8f, 0), dropoff);
            var handFrames = new List<ModulePose> { At(-1.8f, 0), At(3.25f, 0) };
            float support = .03f;
            if (floorToFloor)
                handFrames.Add(At(HatchX - .45f, 0));
            else
            {
                const float rampEnd = 5.65f;
                const float rampHeight = 1.08f;
                const int steps = 6;
                for (int i = 0; i < steps; i++)
                    Box($"step-{i + 1:D2}", BlockRole.Floor, 3.25f + .4f * i, 3.25f + .4f * (i + 1),
                        0, (i + 1) * rampHeight / steps, -1.2f, 1.2f);
                Box("hatch-landing", BlockRole.Floor, rampEnd, 7.15f, rampHeight - Skirt, rampHeight, -1.2f, 1.2f);
                handFrames.Add(At(rampEnd, rampHeight));
                handFrames.Add(At(6.1f, rampHeight));
                support = .39f;
            }
            group.Module.Routes.Add(new HaulRoute($"{id}-hatch-approach", west.Id, hatch.Id, Cargo.Hand,
                handFrames.ToArray(), stepClearance: support));
            var fitout = new RoomDressing($"{id}-test-bay", chamber);
            fitout.Bench("hatch-test-console", 3.8f, 0, -3.5f, -1, 2.12f,
                id == "a" ? FixedSeat.OperatorChair : FixedSeat.Chair, powered: true, active: id == "a");
            fitout.Cabinet("isolation-locker", 4.12f, 0, 3.5f, 1, 1.15f, .94f);
            if (id == "a")
                fitout.PartsRack("open-parts-trays", 6.12f, 0, -3.5f, -1, 1.13f);
            else
                fitout.LowCabinet("stacked-sensor-cases", 6.08f, 0, -3.07f, -1, 1.14f, stacked: true);
            fitout.WallLamp("console-light", 3.65f, 3.11f, -3.5f, -1,
                id == "a" ? LampDuty.Work : LampDuty.Standby);
            fitout.AisleStrip("hatch-landing-marker", 6.15f, .45f, 3.5f, 1, -.8f);
            fitout.VerifyCover("isolation-locker-body", new Vector3(2.92f, 0, 3.14f),
                new Vector3(5.35f, 0, 3.14f), new Vector3(0, 0, -1.10f));
            group.AddDressing(fitout);
        }
    }
}

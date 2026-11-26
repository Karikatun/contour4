using System.Linq;
using System.Numerics;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.NativeRaid.Runtime
{
    internal static class FreightServiceRoom
    {
        internal const string Dispatch = "freight-dispatch";
        internal const string InstrumentService = "instrument-service";
        internal const string SampleReceiving = "sample-receiving";
        internal const string ResearchDoor = "research-door";
        internal const string CartParking = "protected-cart-parking";

        internal static GrayModule Create(string purpose, bool portal, bool portalAnnex = false)
        {
            float length = purpose == Dispatch ? 11f : 9.25f;
            const float halfWidth = 4.375f;
            float center = length / 2f;
            const float turnRadius = 2.85f;
            var room = new GrayBuilder(purpose, halfWidth * 2f, length);
            room.Floor("ground", 0, length, 0, -halfWidth, halfWidth);
            room.Roof(0, length, Height, -halfWidth, halfWidth);
            var west = room.Door("west", "ground", Pose(0, 0, 0, -Vector3.UnitX),
                portalAnnex ? Cargo.Hand : Cargo.Cart, portalAnnex ? DockUse.DoorPortal : DockUse.Physical);
            var east = room.Door("east", "ground", Pose(length, 0, 0), Cargo.Cart);
            var spur = room.Door("spur", "ground", Pose(center, 0, halfWidth, Vector3.UnitZ), Cargo.Cart);
            var supply = purpose == InstrumentService
                ? room.Door("supply", "ground", Pose(center, 0, -halfWidth, -Vector3.UnitZ), Cargo.Cart)
                : null;
            room.Wall("west-wall", west.Threshold, halfWidth, Height, west);
            room.Wall("east-wall", east.Threshold, halfWidth, Height, east);
            room.Wall("near-wall", Pose(center, 0, -halfWidth, -Vector3.UnitZ), center - Skirt, Height, supply);
            if (portal)
            {
                const float researchCenter = 1.22f;
                float split = (researchCenter + Carry / 2 + center - Wide / 2) / 2;
                var shortcut = room.Door(ResearchDoor, "ground", Pose(researchCenter, 0, halfWidth, Vector3.UnitZ),
                    Cargo.Hand, DockUse.DoorPortal, Carry, 2.5f);
                room.Wall("research-wall", Pose((Skirt + split) / 2, 0, halfWidth, Vector3.UnitZ),
                    (split - Skirt) / 2, Height, shortcut);
                room.Wall("spur-wall", Pose((split + length - Skirt) / 2, 0, halfWidth, Vector3.UnitZ),
                    (length - Skirt - split) / 2, Height, spur);
                foreach (Dock dock in new[] { west, east, spur, supply }.OfType<Dock>())
                {
                    Vector3 start = dock.Threshold.Position;
                    Vector3 inward = -dock.Threshold.Forward;
                    Vector3 end = shortcut.Threshold.Position;
                    const float margin = 1.25f;
                    var approach = start + inward * margin;
                    var landing = end - Vector3.UnitZ * margin;
                    room.Route("research-from-" + dock.Id, dock.Id, shortcut.Id, Cargo.Hand,
                        new ModulePose(start, inward, Vector3.UnitY),
                        new ModulePose(approach, Vector3.Normalize(landing - approach), Vector3.UnitY),
                        new ModulePose(landing, Vector3.UnitZ, Vector3.UnitY), shortcut.Threshold);
                }
            }
            else
                room.Wall("far-wall", spur.Threshold, center - Skirt, Height, spur);
            room.Route("cross-floor", west.Id, east.Id, Cargo.Cart, Pose(-1.25f, 0, 0), Pose(length + 1.25f, 0, 0));
            foreach (Dock side in new[] { spur, supply }.OfType<Dock>())
            {
                Vector3 outward = side.Threshold.Forward;
                ModulePose landing = Pose(center, 0, (halfWidth + 1.25f) * outward.Z, outward);
                room.Route(side.Id + "-from-west", west.Id, side.Id, Cargo.Cart, new[] { Pose(-1.25f, 0, 0) }
                    .Concat(Bend(Pose(center - turnRadius, 0, 0), turnRadius, outward)).Append(landing).ToArray());
                room.Route(side.Id + "-from-east", east.Id, side.Id, Cargo.Cart,
                    new[] { Pose(length + 1.25f, 0, 0, -Vector3.UnitX) }
                    .Concat(Bend(Pose(center + turnRadius, 0, 0, -Vector3.UnitX), turnRadius, outward))
                    .Append(landing).ToArray());
            }
            if (supply != null)
                room.Route("cross-service-aisles", supply.Id, spur.Id, Cargo.Cart,
                    Pose(center, 0, -halfWidth, Vector3.UnitZ), Pose(center, 0, halfWidth, Vector3.UnitZ));

            if (purpose == SampleReceiving)
            {
                var receivingFixtures = SampleReceivingDressing.Create();
                if (!portal) AddCartParking(room, receivingFixtures, 2.85f);
                room.AddDressing(receivingFixtures);
                return room.Module;
            }

            const float workWall = -halfWidth + Skirt;
            const float farWall = halfWidth - Skirt;
            var fixtures = new RoomDressing(purpose, Level);
            if (purpose == Dispatch)
            {
                fixtures.LockerBank("route-records", 1.53f, 0, workWall, -1, 3);
                fixtures.Bench("dispatch-desk", 4.18f, 0, workWall, -1, 2.32f, FixedSeat.OperatorChair,
                    powered: true, active: true);
                fixtures.LootBlock("outbound-manifest-bin", 8.15f, 0, -3.05f, -1, 1.90f, 1.40f, 1.18f);
                fixtures.PartsRack("tag-and-seal-rack", 8.92f, 0, farWall, 1, 1.48f);
                fixtures.TaskPendant("dispatch-task-light", 4.16f, Height, -2.50f, .52f, LampDuty.Work);
                if (!portal) AddCartParking(room, fixtures, 3.25f);
            }
            else if (purpose == InstrumentService)
            {
                fixtures.Bench("inspection-bench", 1.34f, 0, workWall, -1, 1.57f, FixedSeat.None,
                    powered: true, active: true);
                fixtures.LowCabinet("instrument-spares", 7.49f, 0, workWall + .41f, -1, 1.38f, stacked: true);
                fixtures.LootBlock("probe-test-rig", 7.57f, 0, 3.42f, 1, 1.70f, 1.05f, 1.34f);
                fixtures.Box("probe-rig-indication", 7.57f, 1.20f, 2.89f, .48f, .12f, .036f, DressingSurface.ActiveLens);
                fixtures.OverheadFeed("probe-rig-supply", 7.84f, 1.28f, 3.80f, Height, farWall, 1);
                fixtures.WallLamp("probe-rig-worklight", 7.58f, 3.36f, farWall, 1, LampDuty.Work);
                // Fixed screens divide the equipment bays from the crossing without taking up the cargo lanes.
                fixtures.Box("inspection-bay-screen", 2.28f, .80f, -3.22f, .16f, 1.60f, 1.78f);
                fixtures.Box("spares-bay-screen", 7.55f, .90f, -2.72f, 1.18f, 1.80f, .30f);
                fixtures.Box("probe-bay-screen", 7.55f, .90f, 2.72f, 1.18f, 1.80f, .30f);
            }
            fixtures.WallLamp("west-guidance", .94f, 2.44f, workWall, -1, LampDuty.Guidance, .28f);
            fixtures.AisleStrip("east-guidance", length - 1.1f, .39f, workWall, -1, -.60f);
            room.AddDressing(fixtures);
            return room.Module;
        }

        private static void AddCartParking(GrayBuilder room, RoomDressing fixtures, float screenEnd)
        {
            const float screenStart = .24f;
            fixtures.Box("cart-parking-screen", (screenStart + screenEnd) / 2, Height / 2, 1.73f,
                screenEnd - screenStart, Height, .15f);
            room.Module.Landmarks.Add(CartParking, new Vector3(screenEnd - .85f, 0, 2.90f));
        }
    }
}

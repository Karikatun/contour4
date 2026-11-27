using System.Linq;
using Contour4.Procedural;
using Contour4.Procedural.Catalog;
using UnityEngine;
using static Contour4.Procedural.Catalog.GrayBuilder;
using Vector3 = System.Numerics.Vector3;

namespace Contour4.NativeRaid.Runtime
{
    internal enum QuotaLayout
    {
        Ordinary,
        Progressive,
        Rotary,
    }

    internal static class RaidExtraction
    {
        internal const string Machine = "native-extraction";
        internal const string Entry = "freight-entry";
        internal const string OrdinaryId = "extraction-chamber";
        internal const string StraightBridgeId = "extraction-ruined-bridge";
        internal const string BentBridgeId = "extraction-ruined-bridge-bent";
        internal const int StraightBridgeUnlock = 5;
        internal const int BentBridgeUnlock = 9;
        internal const float Length = 17f;
        internal const float Width = 12f;
        internal const float ShaftTop = 34f;
        internal static readonly Vector3 MachineOrigin = new Vector3(12f, 0, 0);

        internal static int AdditionalCount(int moduleCapacity) =>
            moduleCapacity >= 15 ? 4 : moduleCapacity >= 10 ? 3 : moduleCapacity >= 8 ? 2 : moduleCapacity >= 6 ? 1 : 0;

        internal static HaulRoute LoadingRoute(GrayModule module) =>
            module.Routes.Single(route => route.To == Machine);

        internal static Quaternion FaceLoading(ModulePose approach, UnityEngine.Vector3 loadingDirection)
        {
            return Quaternion.LookRotation(-RaidPlan.Vector(approach.Forward), RaidPlan.Vector(approach.Up)) *
                Quaternion.Inverse(Quaternion.LookRotation(loadingDirection, UnityEngine.Vector3.up));
        }

        internal static GrayModule CreateForSlot(int completedLevels, int slot, QuotaLayout layout, int seed) => slot switch
        {
            0 when layout == QuotaLayout.Rotary => RaidQuotaRotaryRoom.Create(RaidQuotaRotaryRoom.TurnDegrees(seed)),
            0 when layout != QuotaLayout.Ordinary && completedLevels >= StraightBridgeUnlock =>
                RaidExtractionBridge.Create(StraightBridgeId, Entry, Machine, bent: false),
            1 when layout != QuotaLayout.Ordinary && completedLevels >= BentBridgeUnlock =>
                RaidExtractionBridge.Create(BentBridgeId, Entry, Machine, bent: true),
            _ => Create(),
        };

        internal static GrayModule Create()
        {
            var chamber = new GrayBuilder(OrdinaryId, Width, Length);
            var entry = chamber.Door(Entry, "ground", Pose(0, 0, 0, -Vector3.UnitX), Cargo.Cart);
            chamber.Floor("ground", 0, Length, 0, -Width / 2, Width / 2);
            chamber.RectangleWalls(0, Length, 0, 8f, -Width / 2, Width / 2, west: entry);
            chamber.Box("loading-bay-roof", BlockRole.Shell, 0, 6, 8f, 8.25f, -Width / 2, Width / 2);
            chamber.Box("native-quota-working-volume", BlockRole.Free, 6, Length, 0, ShaftTop, -5, 5);
            chamber.Module.Landmarks.Add(Machine, MachineOrigin);
            chamber.Route("freight-ramp", entry.Id, Machine, Cargo.Cart, Pose(0, 0, 0), Pose(7.4f, 0, 0));
            return chamber.Module;
        }
    }
}

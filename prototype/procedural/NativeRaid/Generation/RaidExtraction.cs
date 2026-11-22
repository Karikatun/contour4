using System.Numerics;
using Contour4.Procedural.Catalog;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.NativeRaid.Runtime
{
    internal static class RaidExtraction
    {
        internal const string Machine = "native-extraction";
        internal const string Entry = "freight-entry";
        internal const float Length = 17f;
        internal const float Width = 12f;
        internal const float ShaftTop = 34f;
        internal static readonly Vector3 MachineOrigin = new Vector3(12f, 0, 0);

        internal static int AdditionalCount(int moduleCapacity) =>
            moduleCapacity >= 15 ? 4 : moduleCapacity >= 10 ? 3 : moduleCapacity >= 8 ? 2 : moduleCapacity >= 6 ? 1 : 0;

        internal static GrayModule Create()
        {
            var chamber = new GrayBuilder("extraction-chamber", Width, Length);
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

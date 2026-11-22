using System.Linq;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class ElbowCorridor
    {
        public static GrayModule Create()
        {
            var corridor = new GrayBuilder("elbow-corridor", 13.25f, 9.25f);
            corridor.Deck("west-arm-floor", 0, 9.25f, 0, -3.25f, 3.25f);
            corridor.Deck("north-arm-floor", 2.75f, 9.25f, 0, 3.25f, 10);
            corridor.MapFloor("ground", Level, new Vector2(0, -3.25f), new Vector2(9.25f, -3.25f),
                new Vector2(9.25f, 10), new Vector2(2.75f, 10), new Vector2(2.75f, 3.25f), new Vector2(0, 3.25f));
            corridor.Box("west-arm-roof", BlockRole.Shell, 0, 9.25f, Height, Height + Skirt, -3.25f, 3.25f);
            corridor.Box("north-arm-roof", BlockRole.Shell, 2.75f, 9.25f, Height, Height + Skirt, 3.25f, 10);
            var west = corridor.Door("west", "ground", Pose(0, 0, 0, -Vector3.UnitX), Cargo.Cart);
            var north = corridor.Door("north", "ground", Pose(6, 0, 10, Vector3.UnitZ), Cargo.Cart);
            corridor.Wall("west-wall", west.Threshold, 3.25f, Height, west);
            corridor.Wall("north-wall", north.Threshold, 3.25f, Height, north);
            corridor.Box("near-wall", BlockRole.Shell, Skirt, 9.25f, 0, Height, -3.25f, -3.25f + Skirt);
            corridor.Box("east-wall", BlockRole.Shell, 9.25f - Skirt, 9.25f, 0, Height, -3.25f + Skirt, 10 - Skirt);
            corridor.Box("inner-west-arm-wall", BlockRole.Shell, Skirt, 2.75f + Skirt, 0, Height, 3.25f - Skirt, 3.25f);
            corridor.Box("inner-north-arm-wall", BlockRole.Shell, 2.75f, 2.75f + Skirt, 0, Height, 3.25f, 10 - Skirt);
            corridor.Route("right-angle-turn", west.Id, north.Id, Cargo.Cart, new[] { Pose(-1.8f, 0, 0) }
                .Concat(Bend(Pose(1.5f, 0, 0), 4.5f, Vector3.UnitZ))
                .Append(Pose(6, 0, 11.8f, Vector3.UnitZ)).ToArray());
            var approach = new RoomDressing("turn-approach", Level);
            approach.AisleStrip("corner-guidance", 4.6f, .43f, -3, -1, .86f);
            corridor.AddDressing(approach);
            var exitLight = new RoomDressing("turn-exit-light", Pose(9, 0, 0, Vector3.UnitZ));
            exitLight.WallLamp("north-standby", 6.25f, 2.42f, 0, -1, LampDuty.Standby);
            corridor.AddDressing(exitLight);
            return corridor.Module;
        }
    }
}

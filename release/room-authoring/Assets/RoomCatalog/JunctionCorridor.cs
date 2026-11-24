using System.Linq;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class JunctionCorridor
    {
        public static GrayModule Create()
        {
            var corridor = new GrayBuilder("junction-corridor", 14.5f, 15);
            corridor.Deck("main-floor", 0, 15, 0, -3.75f, 3.75f);
            corridor.Deck("spur-floor", 4, 11, 0, 3.75f, 10.75f);
            corridor.MapFloor("ground", Level, new Vector2(0, -3.75f), new Vector2(15, -3.75f),
                new Vector2(15, 3.75f), new Vector2(11, 3.75f), new Vector2(11, 10.75f),
                new Vector2(4, 10.75f), new Vector2(4, 3.75f), new Vector2(0, 3.75f));
            corridor.Roof(0, 15, Height, -3.75f, 3.75f);
            corridor.Box("spur-roof", BlockRole.Shell, 4, 11, Height, Height + Skirt, 3.75f, 10.75f);
            var west = corridor.Door("west", "ground", Pose(0, 0, 0, -Vector3.UnitX), Cargo.Cart);
            var east = corridor.Door("east", "ground", Pose(15, 0, 0), Cargo.Cart);
            var north = corridor.Door("spur", "ground", Pose(7.5f, 0, 10.75f, Vector3.UnitZ), Cargo.Cart);
            corridor.Wall("west-wall", west.Threshold, 3.75f, Height, west);
            corridor.Wall("east-wall", east.Threshold, 3.75f, Height, east);
            corridor.Wall("spur-wall", north.Threshold, 3.5f, Height, north);
            corridor.Box("near-wall", BlockRole.Shell, Skirt, 15 - Skirt, 0, Height, -3.75f, -3.75f + Skirt);
            corridor.Box("far-west-wall", BlockRole.Shell, Skirt, 4 + Skirt, 0, Height, 3.75f - Skirt, 3.75f);
            corridor.Box("far-east-wall", BlockRole.Shell, 11 - Skirt, 15 - Skirt, 0, Height, 3.75f - Skirt, 3.75f);
            corridor.Box("left-spur-wall", BlockRole.Shell, 4, 4 + Skirt, 0, Height, 3.75f, 10.75f - Skirt);
            corridor.Box("right-spur-wall", BlockRole.Shell, 11 - Skirt, 11, 0, Height, 3.75f, 10.75f - Skirt);
            corridor.Route("through", west.Id, east.Id, Cargo.Cart, Pose(-1.8f, 0, 0), Pose(16.8f, 0, 0));
            corridor.Route("west-to-spur", west.Id, north.Id, Cargo.Cart, new[] { Pose(-1.8f, 0, 0) }
                .Concat(Bend(Pose(3, 0, 0), 4.5f, Vector3.UnitZ))
                .Append(Pose(7.5f, 0, 12.55f, Vector3.UnitZ)).ToArray());
            corridor.Route("east-to-spur", east.Id, north.Id, Cargo.Cart, new[] { Pose(16.8f, 0, 0, -Vector3.UnitX) }
                .Concat(Bend(Pose(12, 0, 0, -Vector3.UnitX), 4.5f, Vector3.UnitZ))
                .Append(Pose(7.5f, 0, 12.55f, Vector3.UnitZ)).ToArray());
            var main = new RoomDressing("crossroad", Level);
            main.AisleStrip("west-aisle-marker", 3.2f, .42f, -3.5f, -1, .8f);
            main.WallLamp("branch-task-marker", 7.5f, 3.45f, -3.5f, -1, LampDuty.Work);
            main.WallLamp("east-aisle-marker", 11.8f, 2.35f, -3.5f, -1, LampDuty.Guidance, -.65f);
            corridor.AddDressing(main);
            var sideLight = new RoomDressing("spur", Pose(4.25f, 0, 0, Vector3.UnitZ));
            sideLight.WallLamp("service-threshold-marker", 7.05f, 2.36f, 0, 1, LampDuty.Guidance, .45f);
            corridor.AddDressing(sideLight);
            return corridor.Module;
        }
    }
}

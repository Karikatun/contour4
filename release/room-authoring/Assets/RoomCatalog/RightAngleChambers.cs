using System;
using System.Linq;
using System.Numerics;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog
{
    public static class RightAngleChambers
    {
        public static GrayModule Create()
        {
            var source = new CornerSlice();
            var group = new GrayBuilder("right-angle-chambers", 15, 15);
            AddRoom(group, source.Airlock);
            AddRoom(group, source.Hall);
            foreach (Solid solid in source.Turn)
                group.Module.Geometry.Add(Block.FromPrism(solid.Id,
                    solid.Id.StartsWith("deck-", StringComparison.Ordinal) ? BlockRole.Floor : BlockRole.Shell, solid));
            group.Module.Geometry.Add(Block.FromPrism(source.ContactRail.Id, BlockRole.Shell, source.ContactRail));
            foreach (Solid solid in source.TurnClearance)
                group.Module.Approaches.Add(Block.FromPrism(solid.Id, BlockRole.Free, solid));
            foreach (RoomDressing dressing in source.Dressing)
                group.AddDressing(dressing);
            group.Module.InternalPortals.Add(("airlock-door-portal", "distribution-hall-door-portal"));
            const float approach = (CornerSlice.RoomDepth - CornerSlice.Radius) + 1.8f;
            const int intervals = 80;
            group.Route("loaded-quarter-turn", "airlock-entry", "distribution-hall-entry", Cargo.Cart,
                Enumerable.Range(0, intervals + 1).Select(i => CornerSlice.Frame(-approach +
                    (CornerSlice.ArcLength + 2 * approach) * i / intervals)).ToArray());
            return group.Module;
        }

        private static void AddRoom(GrayBuilder group, Room room)
        {
            Socket source = room.Sockets.Single(s => s.Id == "external-door");
            ModulePose pose = room.Floors.Single().Pose;
            float half = room.NominalPlan.X / 2;
            const float cutout = Wide / 2 + Skirt;
            const float portalX = -13;
            var entry = group.Door($"{room.Id}-entry", room.Id, source.Threshold, Cargo.Cart,
                mapPoint: new Vector2(-CornerSlice.RoomDepth, 0));
            var portal = group.Door($"{room.Id}-door-portal", room.Id,
                new ModulePose(pose.Point(portalX, 0, half), pose.Right, pose.Up),
                Cargo.Hand, DockUse.DoorPortal, Carry, 2.5f, mapPoint: new Vector2(portalX, half));
            foreach (Solid solid in room.Solids.Where(s => s.Id != "outer-wall-right"))
            {
                BlockRole role = solid.Plug ? BlockRole.Plug :
                    solid.Id.StartsWith("floor", StringComparison.Ordinal) ? BlockRole.Floor : BlockRole.Shell;
                group.Module.Geometry.Add(Block.FromPrism(solid.Plug ? entry.PlugId : $"{room.Id}-{solid.Id}", role, solid));
            }
            group.Wall($"{room.Id}-portal-wall", new ModulePose(pose.Point(-7.5f, 0, half), pose.Right, pose.Up),
                7.5f - Skirt, Height, portal);
            group.MapFloor(room.Id, pose, new Vector2(-15, -half), new Vector2(0, -half), new Vector2(0, -cutout),
                new Vector2(-CornerSlice.Radius, -cutout), new Vector2(-CornerSlice.Radius, cutout),
                new Vector2(0, cutout), new Vector2(0, half), new Vector2(-15, half));
            group.Route($"{room.Id}-hand-to-portal", entry.Id, portal.Id, Cargo.Hand,
                new ModulePose(pose.Point(-16.8f, 0), pose.Forward, pose.Up),
                new ModulePose(pose.Point(portalX, 0), pose.Forward, pose.Up),
                new ModulePose(pose.Point(portalX, 0), pose.Right, pose.Up),
                new ModulePose(pose.Point(portalX, 0, half - .70f), pose.Right, pose.Up));
        }
    }
}

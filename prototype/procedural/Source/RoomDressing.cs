using System;
using System.Collections.Generic;
using System.Numerics;

namespace Contour4.Procedural
{
    public enum DressingSurface { Housing, Worktop, Seat, Conduit, ActiveLens, StandbyLens, DarkLens, GuideLens, SteelTrim }
    public enum LampDuty { Work, Standby, Guidance, Offline }
    public enum LampOptics { Wide, Task, Aisle }
    public enum FixedSeat { None, Chair, OperatorChair }
    public enum LootSurfaceKind { Cabinet, Table, Raised, Floor }

    public sealed class RoomDressing
    {
        public readonly struct Part
        {
            public readonly string Id;
            public readonly Vector3 Center;
            public readonly Vector3 Size;
            public readonly DressingSurface Surface;

            public Part(string id, Vector3 center, Vector3 size, DressingSurface surface)
            {
                Id = id;
                Center = center;
                Size = size;
                Surface = surface;
            }
        }

        public readonly struct Lamp
        {
            public readonly string Id;
            public readonly Vector3 Emitter;
            public readonly Vector3 Direction;
            public readonly LampDuty Duty;
            public readonly LampOptics Optics;

            public Lamp(string id, Vector3 emitter, Vector3 direction, LampDuty duty, LampOptics optics = LampOptics.Wide)
            {
                Id = id;
                Emitter = emitter;
                Direction = Vector3.Normalize(direction);
                Duty = duty;
                Optics = optics;
            }
        }

        public readonly struct MovingFront
        {
            public readonly string Panel;
            public readonly string Handle;
            public readonly string Tray;
            public readonly Vector3 Pivot;
            public readonly float Angle;
            public readonly float Travel;

            public MovingFront(string panel, string handle, Vector3 pivot, float angle, float travel = 0, string tray = null)
            {
                Panel = panel;
                Handle = handle;
                Tray = tray;
                Pivot = pivot;
                Angle = angle;
                Travel = travel;
            }
        }

        public readonly struct Cover
        {
            public readonly string Collider;
            public readonly Vector3 From;
            public readonly Vector3 Behind;
            public readonly Vector3 Offset;
            public readonly IReadOnlyList<Vector3> Access;
            public bool CrouchOnly => Access != null;

            public Cover(string collider, Vector3 from, Vector3 behind, Vector3 offset, Vector3[] access = null)
            {
                Collider = collider;
                From = from;
                Behind = behind;
                Offset = offset;
                Access = access;
            }
        }

        public readonly struct LootSurface
        {
            public readonly string Support;
            public readonly LootSurfaceKind Kind;
            public readonly int WallSide;

            internal LootSurface(string support, LootSurfaceKind kind, int wallSide)
            {
                Support = support;
                Kind = kind;
                WallSide = wallSide;
            }
        }

        public string Id { get; }
        public ModulePose Frame { get; }
        private readonly List<Part> parts = new List<Part>();
        private readonly List<Lamp> lamps = new List<Lamp>();
        private readonly List<Cover> covers = new List<Cover>();
        private readonly List<MovingFront> fronts = new List<MovingFront>();
        private readonly List<LootSurface> lootSurfaces = new List<LootSurface>();
        public IReadOnlyList<Part> Parts => parts;
        public IReadOnlyList<Lamp> Lamps => lamps;
        public IReadOnlyList<Cover> Covers => covers;
        public IReadOnlyList<MovingFront> MovingFronts => fronts;
        public IReadOnlyList<LootSurface> LootSurfaces => lootSurfaces;

        public RoomDressing(string id, ModulePose frame)
        {
            Id = id;
            Frame = frame;
        }

        public string Name(string id) => $"{Id}-{id}";
        public Vector3 Point(Vector3 local) => Frame.Point(local.X, local.Y, local.Z);
        public Vector3 Direction(Vector3 local) => Frame.Forward * local.X + Frame.Up * local.Y + Frame.Right * local.Z;

        public Solid AsCornerPrism(Part part)
        {
            Vector3 min = part.Center - part.Size / 2;
            Vector3 max = part.Center + part.Size / 2;
            return Solid.Rectangle(Name(part.Id), Frame, min.X, max.X, min.Y, max.Y, min.Z, max.Z);
        }

        public RoomDressing Reframe(string prefix, ModulePose frame)
        {
            Vector3 Axis(Vector3 v) => frame.Forward * v.X + frame.Up * v.Y + frame.Right * v.Z;
            Vector3 p = Frame.Position;
            var placed = new RoomDressing($"{prefix}-{Id}", new ModulePose(frame.Point(p.X, p.Y, p.Z),
                Axis(Frame.Forward), Axis(Frame.Up)));
            placed.parts.AddRange(parts);
            placed.lamps.AddRange(lamps);
            placed.covers.AddRange(covers);
            placed.fronts.AddRange(fronts);
            placed.lootSurfaces.AddRange(lootSurfaces);
            return placed;
        }

        public void Box(string id, float x, float y, float z, float width, float height, float depth,
            DressingSurface surface = DressingSurface.Housing) =>
            parts.Add(new Part(id, new Vector3(x, y, z), new Vector3(width, height, depth), surface));

        public void Bench(string id, float x, float y, float wall, int side, float width, FixedSeat seat,
            bool powered = false, bool active = false)
        {
            float back = wall - side * .15f;
            float center = back - side * .48f;
            foreach (int end in new[] { -1, 1 })
                Box($"{id}-leg-{end}", x + end * (width / 2 - .15f), y + .44f, center, .14f, .88f, .75f);
            Box($"{id}-top", x, y + .94f, center, width, .12f, .96f, DressingSurface.Worktop);
            lootSurfaces.Add(new LootSurface($"{id}-top", LootSurfaceKind.Table, side));
            Box($"{id}-back-brace", x, y + .42f, back - side * .14f, width - .18f, .16f, .12f);
            if (powered)
            {
                float inlet = x + width / 4;
                Box($"{id}-instrument", inlet, y + 1.25f, back - side * .20f, .56f, .5f, .38f);
                Box($"{id}-screen", inlet, y + 1.30f, back - side * .394f, .42f, .25f, .018f,
                    active ? DressingSurface.StandbyLens : DressingSurface.DarkLens);
                Cable(id, inlet, y + 1.27f, back - side * .02f, y + 2.44f, wall, side,
                    MathF.Min(1.28f, width * .43f));
            }
            if (seat != FixedSeat.None) Seat(id, x, y, center - side * .83f, side, seat);
        }

        private void Seat(string id, float x, float y, float seatZ, int side, FixedSeat seat)
        {
            float widthSeat = seat == FixedSeat.OperatorChair ? .64f : .51f;
            Box($"{id}-seat-anchor", x, y + .045f, seatZ, .43f, .09f, .44f);
            Box($"{id}-seat-post", x, y + .25f, seatZ, .12f, .40f, .12f);
            Box($"{id}-seat-pan", x, y + .49f, seatZ, widthSeat, .10f, .49f, DressingSurface.Seat);
            float backHeight = seat == FixedSeat.OperatorChair ? .63f : .39f;
            Box($"{id}-seat-back", x, y + .49f + backHeight / 2, seatZ - side * .245f,
                widthSeat, backHeight, .10f, DressingSurface.Seat);
            if (seat == FixedSeat.OperatorChair)
                foreach (int end in new[] { -1, 1 })
                    Box($"{id}-armrest-{end}", x + end * .3f, y + .64f, seatZ, .09f, .25f, .39f,
                        DressingSurface.Seat);
        }

        public void Cabinet(string id, float x, float y, float wall, int side, float width, float depth = .8f,
            bool powered = false)
        {
            float center = wall - side * depth / 2;
            float face = wall - side * depth;
            Box($"{id}-anchor", x, y + .09f, center, width - .1f, .18f, depth - .08f);
            CabinetCase(id, x, y + .16f, center, side, width, 2.06f, depth);
            for (int shelf = 0; shelf < 2; shelf++)
            {
                string support = $"{id}-shelf-{shelf + 1}";
                Box(support, x, y + .72f + .60f * shelf, center, width - .14f, .055f, depth - .12f);
                lootSurfaces.Add(new LootSurface(support, LootSurfaceKind.Cabinet, side));
            }
            if (width < 1.55f)
                CabinetDoor(id, x, y, face, side, width, 1);
            else
            {
                CabinetDoor($"{id}-left", x - width / 4, y, face, side, width / 2, 1);
                CabinetDoor($"{id}-right", x + width / 4, y, face, side, width / 2, -1);
            }
            if (powered)
            {
                Box($"{id}-status", x, y + 2.19f, face - side * .014f, .31f, .04f, .025f,
                    DressingSurface.StandbyLens);
                Cable(id, x + width / 4, y + 2.2f, wall - side * .21f, y + 2.62f, wall, side);
            }
        }

        public void LootBlock(string id, float x, float y, float z, int side, float width, float depth, float height)
        {
            Box(id, x, y + height / 2, z, width, height, depth, DressingSurface.Worktop);
            lootSurfaces.Add(new LootSurface(id, LootSurfaceKind.Raised, side));
        }

        private void CabinetDoor(string id, float x, float y, float face, int side, float width, int hinge)
        {
            float closedZ = face - side * .045f;
            string panel = $"{id}-door", handle = $"{id}-handle";
            Box(panel, x, y + 1.19f, closedZ, width - .11f, 1.88f, .035f,
                DressingSurface.Worktop);
            Box(handle, x + hinge * (width / 2 - .13f), y + 1.14f, face - side * .095f,
                .055f, .24f, .07f, DressingSurface.SteelTrim);
            fronts.Add(new MovingFront(panel, handle,
                new Vector3(x - hinge * (width / 2 - .055f), y + 1.19f, closedZ), side * hinge * 101f));
        }

        // The left jamb retains the original cover collider identifier.
        private void CabinetCase(string id, float x, float bottom, float z, int side,
            float width, float height, float depth)
        {
            const float panel = .065f;
            float middle = bottom + height / 2;
            Box($"{id}-body", x - (width - panel) / 2, middle, z, panel, height, depth);
            Box($"{id}-body-right", x + (width - panel) / 2, middle, z, panel, height, depth);
            Box($"{id}-body-back", x, middle, z + side * (depth - panel) / 2, width - 2 * panel, height, panel);
            Box($"{id}-body-top", x, bottom + height - panel / 2, z, width - 2 * panel, panel, depth);
            Box($"{id}-body-bottom", x, bottom + panel / 2, z, width - 2 * panel, panel, depth);
        }

        public void LockerBank(string id, float x, float y, float wall, int side, int count)
        {
            const float width = .58f;
            const float spacing = .63f;
            for (int i = 0; i < count; i++)
                Cabinet($"{id}-{i + 1}", x + (i - (count - 1) / 2f) * spacing, y, wall, side, width, .56f);
        }

        public void LowCabinet(string id, float x, float y, float z, int side, float width,
            float depth = .84f, bool stacked = false)
        {
            const float unitHeight = 1.08f;
            float face = z - side * depth / 2;
            LowUnit(id, y);
            if (!stacked) return;
            LowUnit($"{id}-upper", y + unitHeight);
            foreach (int edge in new[] { -1, 1 })
                Box($"{id}-stack-cleat-{edge}", x + edge * (width / 2 - .055f), y + unitHeight,
                    face - side * .025f, .065f, .33f, .065f, DressingSurface.Conduit);

            void LowUnit(string name, float floor)
            {
                Box($"{name}-plinth", x, floor + .07f, z, width - .13f, .14f, depth - .10f);
                CabinetCase(name, x, floor + .14f, z, side, width, .94f, depth);
                for (int drawer = 0; drawer < 3; drawer++)
                {
                    float drawerHeight = floor + .31f + .27f * drawer;
                    string panel = $"{name}-drawer-{drawer}", handle = $"{name}-pull-{drawer}";
                    Box(panel, x, drawerHeight, face - side * .024f,
                        width - .20f, .23f, .036f, DressingSurface.Worktop);
                    Box(handle, x, drawerHeight + .055f, face - side * .065f,
                        .32f, .048f, .072f, DressingSurface.SteelTrim);
                    string tray = $"{name}-tray-{drawer}";
                    Box(tray, x, drawerHeight - .070f, face + side * .19f, width - .24f, .044f, .38f,
                        DressingSurface.Conduit);
                    fronts.Add(new MovingFront(panel, handle, new Vector3(x, drawerHeight, face), 0, -side * .34f, tray));
                }
            }
        }

        public void PartsRack(string id, float x, float y, float wall, int side, float width)
        {
            float center = wall - side * .29f;
            foreach (int end in new[] { -1, 1 })
            {
                float edge = x + end * (width / 2 - .055f);
                Box($"{id}-anchor-{end}", edge, y + .04f, center, .18f, .08f, .58f);
                Box($"{id}-upright-{end}", edge, y + 1.01f, center, .11f, 1.86f, .49f);
            }
            for (int level = 0; level < 3; level++)
            {
                float height = y + .38f + .62f * level;
                Box($"{id}-shelf-{level}", x, height, center, width - .11f, .075f, .54f, DressingSurface.Worktop);
                Box($"{id}-lip-{level}", x, height + .06f, center - side * .255f,
                    width - .16f, .065f, .035f, DressingSurface.SteelTrim);
            }
            Box($"{id}-retained-bin", x - width * .18f, y + .54f, center, .33f, .26f, .38f);
        }

        public void WallLamp(string id, float x, float y, float wall, int side, LampDuty duty,
            float along = 0)
        {
            float z = wall - side * .085f;
            Box($"{id}-housing", x, y, z, duty == LampDuty.Work ? .96f : .54f, .19f, .17f);
            Box($"{id}-lens", x, y - .02f, z - side * .089f, duty == LampDuty.Work ? .84f : .43f,
                .09f, .025f, Lens(duty));
            if (duty != LampDuty.Offline)
                lamps.Add(new Lamp(id, new Vector3(x, y - .055f, z - side * .135f),
                    new Vector3(along, duty == LampDuty.Guidance ? -.52f : -.72f, -side), duty));
        }

        public void CeilingLamp(string id, float x, float y, float z, LampDuty duty)
        {
            Box($"{id}-housing", x, y - .07f, z, 1.08f, .14f, .30f);
            Box($"{id}-lens", x, y - .15f, z, .94f, .035f, .23f,
                Lens(duty));
            if (duty != LampDuty.Offline)
                lamps.Add(new Lamp(id, new Vector3(x, y - .19f, z), -Vector3.UnitY, duty));
        }

        public void TaskPendant(string id, float x, float ceiling, float z, float drop, LampDuty duty, float across = 0)
        {
            float housing = ceiling - drop;
            foreach (int end in new[] { -1, 1 })
                Box($"{id}-hanger-{end}", x + end * .59f, ceiling - drop / 2, z, .075f, drop, .075f);
            Box($"{id}-housing", x, housing, z, 1.48f, .15f, .36f, DressingSurface.SteelTrim);
            foreach (int side in new[] { -1, 1 })
                Box($"{id}-tube-{side}", x, housing - .087f, z + side * .091f, 1.34f, .038f, .064f, Lens(duty));
            if (duty != LampDuty.Offline)
                lamps.Add(new Lamp(id, new Vector3(x, housing - .14f, z), new Vector3(0, -1, across), duty, LampOptics.Task));
        }

        public void AisleStrip(string id, float x, float y, float wall, int side, float along)
        {
            float z = wall - side * .053f;
            Box($"{id}-housing", x, y, z, .99f, .11f, .106f, DressingSurface.SteelTrim);
            Box($"{id}-lens", x, y - .018f, z - side * .055f, .82f, .034f, .02f, DressingSurface.GuideLens);
            lamps.Add(new Lamp(id, new Vector3(x, y - .026f, z - side * .09f),
                new Vector3(along, -.29f, -side), LampDuty.Guidance, LampOptics.Aisle));
        }

        private static DressingSurface Lens(LampDuty duty) => duty switch
        {
            LampDuty.Work => DressingSurface.ActiveLens,
            LampDuty.Guidance => DressingSurface.GuideLens,
            LampDuty.Offline => DressingSurface.DarkLens,
            _ => DressingSurface.StandbyLens,
        };

        public void GuideFromWall(string id, float height, float lateral, int side, float along = .5f) =>
            lamps.Add(new Lamp(id, new Vector3(0, height, lateral), new Vector3(along, -.65f, -side),
                LampDuty.Guidance));

        public void EquipmentFeed(string id, float x, float y, float inletZ, float trayY, float wall, int side) =>
            Cable(id, x, y, inletZ, trayY, wall, side);

        public void OverheadFeed(string id, float x, float y, float inletZ, float ceiling, float wall, int side)
        {
            float trayY = ceiling - .075f;
            float wallZ = wall - side * .065f;
            Box($"{id}-power-rail", x, trayY - .18f, wallZ, 1.25f, .14f, .13f, DressingSurface.Conduit);
            Box($"{id}-branch-breaker", x + .47f, trayY - .17f, wallZ - side * .046f, .24f, .24f, .21f);
            Box($"{id}-wall-junction", x, trayY - .09f, wallZ, .21f, .30f, .13f, DressingSurface.Conduit);
            Box($"{id}-suspended-cable-tray", x, trayY, (inletZ + wallZ) / 2, .14f, .15f,
                MathF.Abs(wallZ - inletZ) + .13f, DressingSurface.Conduit);
            Box($"{id}-equipment-riser", x, (trayY + y) / 2, inletZ, .104f, trayY - y, .104f,
                DressingSurface.Conduit);
            Box($"{id}-equipment-inlet", x, y, inletZ, .22f, .17f, .20f, DressingSurface.Conduit);
        }

        public void VerifyCover(string collider, Vector3 from, Vector3 behind, Vector3 offset) =>
            covers.Add(new Cover(collider, from, behind, offset));

        public void VerifyLowCover(string collider, Vector3 from, Vector3 behind, Vector3 offset,
            params Vector3[] access) => covers.Add(new Cover(collider, from, behind, offset, access));

        // Room-local wiring. The wall-mounted rail feeds a junction, then a bent rigid conduit reaches the inlet.
        private void Cable(string id, float x, float y, float inletZ, float railY, float wall, int side,
            float railWidth = 1.28f)
        {
            const float diameter = .055f;
            float railZ = wall - side * .055f;
            Box($"{id}-power-rail", x, railY, railZ, railWidth, .13f, .11f, DressingSurface.Conduit);
            Box($"{id}-branch-breaker", x + railWidth / 2 - .065f, railY + .013f, railZ - side * .028f,
                .15f, .20f, .16f);
            Box($"{id}-rail-junction", x, railY, railZ, .18f, .23f, .11f, DressingSurface.Conduit);
            Box($"{id}-junction-to-elbow", x, (railY + y) / 2, railZ, diameter, railY - y, diameter,
                DressingSurface.Conduit);
            Box($"{id}-elbow-to-inlet", x, y, (railZ + inletZ) / 2, diameter, diameter,
                MathF.Abs(inletZ - railZ) + diameter, DressingSurface.Conduit);
            Box($"{id}-equipment-inlet", x, y, inletZ, .105f, .105f, .102f, DressingSurface.Conduit);
        }

        public static RoomDressing CornerRoom(Room room)
        {
            bool airlock = room.Id == "airlock";
            var set = new RoomDressing($"{room.Id}-fitout", room.Floors[0].Pose);
            float negativeWall = -room.NominalPlan.X / 2 + CornerSlice.Thickness;
            if (airlock)
            {
                set.Bench("airlock-control", -12.82f, 0, negativeWall, -1, 2.0f, FixedSeat.None,
                    powered: true, active: true);
                set.Seat("oversight-rest-seat", -11.25f, 0, -3.02f, -1, FixedSeat.Chair);
                set.WallLamp("airlock-service", -12.1f, 3.42f, negativeWall, -1, LampDuty.Work, .28f);
                set.WallLamp("airlock-egress", -7.5f, 2.82f, negativeWall, -1, LampDuty.Guidance, .32f);
                set.AisleStrip("outer-inspection-strip", -5.25f, .39f, -negativeWall, 1, -.72f);
            }
            else
            {
                set.Bench("dispatch-desk", -12, 0, negativeWall, -1, 2.5f, FixedSeat.Chair, powered: true);
                set.Cabinet("distribution-cabinet", -9, 0, negativeWall, -1, 1.4f, 1.55f);
                set.WallLamp("distribution-reserve", -11.15f, 3.18f, negativeWall, -1, LampDuty.Standby);
                set.WallLamp("distribution-egress", -5.6f, 2.82f, negativeWall, -1, LampDuty.Guidance, .32f);
                set.VerifyCover("distribution-cabinet-body", new Vector3(-10.46f, 0, -6.03f),
                    new Vector3(-7.56f, 0, -6.03f), new Vector3(0, 0, 1.5f));
                set.LowCabinet("manifest-drawers", -8.7f, 0, 4.8f, 1, 1.62f);
                set.VerifyLowCover("manifest-drawers-body", new Vector3(-10.1f, 0, 4.8f),
                    new Vector3(-7.3f, 0, 4.8f), new Vector3(0, 0, 1.08f),
                    new Vector3(-12.4f, 0, 5.88f), new Vector3(-12.4f, 0, 1.25f));
                set.LockerBank("inspection-lockers", -6f, 0, -negativeWall, 1, 3);
                set.TaskPendant("manifest-reserve", -8.58f, CornerSlice.Headroom, 3.43f, .68f, LampDuty.Standby, .53f);
                set.TaskPendant("inspection-lockers-reserve", -5.9f, CornerSlice.Headroom, 5.12f, .55f,
                    LampDuty.Standby, .73f);
                set.AisleStrip("distribution-service-strip", -9.4f, .42f, -negativeWall, 1, .8f);
            }
            return set;
        }
    }
}

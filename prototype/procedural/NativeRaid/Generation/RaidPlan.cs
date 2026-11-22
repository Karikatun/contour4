using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural.Catalog;
using UnityEngine;
using ModulePose = Contour4.Procedural.ModulePose;
using Numerics = System.Numerics;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class RaidPlan
    {
        internal sealed class Room
        {
            internal GrayModule Module { get; }
            internal Pose Frame { get; }
            internal HashSet<string> OpenPlugs { get; } = new();
            internal IReadOnlyList<Bounds> Solids { get; }
            internal Bounds Volume { get; }
            internal int Index { get; }
            internal Vector3 Up => Frame.rotation * Vector3.up;

            internal Room(GrayModule module, Pose frame, Pose planningSpace, int index)
            {
                Module = module;
                Frame = frame;
                Index = index;
                Quaternion toPlanning = Quaternion.Inverse(planningSpace.rotation);
                var portalExterior = module.ExternalDocks.Where(dock => (dock.Use & DockUse.DoorPortal) != 0)
                    .Select(dock => Block.Box(dock.Id + "-exterior-clearance", BlockRole.Free, dock.Threshold,
                        0f, .65f, 0f, dock.Opening.Y, -dock.Opening.X / 2, dock.Opening.X / 2));
                Solids = module.Geometry.Concat(module.Approaches).Concat(portalExterior)
                    .Select(block => BoundsOf(block.Vertices.Select(point =>
                        toPlanning * (Point(point) - planningSpace.position)))).ToArray();
                Volume = BoundsOf(Solids.SelectMany(bounds => new[] { bounds.min, bounds.max }));
            }

            internal Vector3 Point(Numerics.Vector3 local) => Frame.position + Frame.rotation * Vector(local);
            internal Vector3 Direction(Numerics.Vector3 local) => Frame.rotation * Vector(local);
            internal Dock Dock(string id) => Module.ExternalDocks.Single(dock => dock.Id == id);
        }

        internal readonly struct End
        {
            internal readonly Room Room;
            internal readonly Dock Dock;
            internal End(Room room, Dock dock) { Room = room; Dock = dock; }
            internal Vector3 Sill => Room.Point(Dock.Threshold.Position);
            internal Vector3 Outward => Room.Direction(Dock.Threshold.Forward);
            internal Vector3 Up => Room.Direction(Dock.Threshold.Up);
            internal Vector3 Pad(float setback) => Sill - Outward * setback;
        }

        internal sealed class FreightLink
        {
            internal End From { get; }
            internal End To { get; }
            internal Room Fold { get; }
            internal FreightLink(End from, End to, Room fold)
            {
                From = from;
                To = to;
                Fold = fold;
            }
        }

        internal Room ShaftTransit { get; private set; }
        internal int Seed { get; }
        internal int CompletedLevels { get; }
        internal int Attempt { get; }
        internal int QuotaCount => rooms.Count(room => room.Module.Landmarks.ContainsKey(RaidExtraction.Machine));
        internal IReadOnlyList<Room> Rooms => rooms;
        internal IReadOnlyList<FreightLink> Freight => freight;
        internal IReadOnlyList<(End From, End To)> Passages => freight
            .Where(link => link.Fold == null && link.To.Room != RingRoom)
            .Select(link => (link.From, link.To)).ToArray();
        internal IReadOnlyList<(End A, End B)> Portals => portals;
        internal (End A, End B) Portal => portals[0];
        internal End Entrance { get; }
        internal (End Entrance, Pose Placement, RaidPrefabBranch Descriptor)? Branch { get; }

        private readonly List<Room> rooms = new();
        private readonly List<FreightLink> freight = new();
        private readonly List<(End A, End B)> portals = new();
        private readonly Dictionary<Room, FreightLink> inbound = new();
        private readonly Pose planningSpace;
        // Reserve the entire stock starter half-space around its 15 m native module width,
        // including its rear extension. Far lateral folded rooms can use the other side of the plane.
        private static readonly Bounds TruckReservation = new Bounds(new Vector3(-64f, 0, 0), new Vector3(128f, 40f, 28f));
        private uint random;
        private const float FitTolerance = .005f;

        private sealed class PlacementException : InvalidOperationException
        {
            internal PlacementException(string message) : base(message) { }
        }

        private RaidPlan(int seed, Pose start, int completedLevels, int attempt, RaidPrefabBranch branch,
            Func<GrayModule> maintenance)
        {
            Seed = seed;
            CompletedLevels = completedLevels;
            Attempt = attempt;
            planningSpace = start;
            random = unchecked((uint)seed + (uint)attempt * 0x85ebca6bu);
            int target = CatalogCapacity(completedLevels);
            bool fullRaid = target == CatalogCapacity(14);
            var root = new Room(FreightServiceRoom.Create(FreightServiceRoom.Dispatch, portal: fullRaid), start, planningSpace, 0);
            Entrance = new End(root, root.Dock("west"));
            root.OpenPlugs.Add(Entrance.Dock.PlugId);
            rooms.Add(root);

            int quotaCount = RaidExtraction.AdditionalCount(target);
            bool useBranch = branch != null && completedLevels >= 6 && ((seed ^ completedLevels) & 1) != 0;
            int laboratories = target >= 10 ? 2 : target >= 8 ? 1 : 0;
            int contentCount = 2 + laboratories + (maintenance != null && target >= 7 ? 1 : 0);
            int branchRooms = Math.Max(target - contentCount, contentCount + quotaCount + (useBranch ? 1 : 0) - 1);
            int span = target >= 7 ? 3 + Next(2) : -1;
            int serviceOrder = Next(2);
            int servicePortalPairs = fullRaid ? 5 : target >= 10 ? Math.Max(2, 1 + (target - 9) * 3 / 5) : 0;
            // Grow two independent arms with staffed rooms. The room at the selected spine position
            // gets the single long gravity span for this raid.
            while (rooms.Count < branchRooms)
            {
                var service = FreightServiceRoom.Create((rooms.Count + serviceOrder) % 2 == 0 ?
                    FreightServiceRoom.InstrumentService : FreightServiceRoom.SampleReceiving, portal: servicePortalPairs > 0);
                var candidates = Exits().Where(end => rooms.Count >= 3 || end.Room == root)
                    .OrderBy(end => RootArmSize(end)).ThenBy(end => Next(int.MaxValue));
                if (!TryAttach(candidates, service, "west", out _, longLink: rooms.Count == span))
                    throw new PlacementException($"Freight network exhausted at staffed room {rooms.Count} of {branchRooms} (seed {Seed}).");
            }

            var quotas = new List<Room>();
            for (int i = 0; i < quotaCount; i++)
            {
                var quota = RaidExtraction.Create();
                // Native 0.5.2 uses maximin spacing from the truck and previously chosen extraction tiles.
                // Physical freight distance and branch diversity keep that intent in the folded 3-D layout.
                var usedArms = quotas.Select(Arm).ToHashSet();
                var preferred = Exits().Where(end => end.Room != root)
                    .Select(end => (End: end, Arm: Arm(end), Separation: QuotaSeparation(end, quotas), Tie: Next(int.MaxValue)))
                    .OrderBy(entry => usedArms.Contains(entry.Arm)).ThenByDescending(entry => entry.Separation)
                    .ThenBy(entry => entry.Tie).Select(entry => entry.End);
                if (!TryAttach(preferred, quota, RaidExtraction.Entry, out Room placed))
                    throw new PlacementException($"Freight network cannot fit quota {i + 1} (seed {Seed}).");
                quotas.Add(placed);
            }

            AttachShaftBranch();
            var calibration = Attach(CalibrationSector.Create(), "main-entry");
            var storage = Attach(SampleStorage.Create(completedLevels >= 2 && Next(2) == 1), "main-entry");
            AddPortal(new End(calibration, calibration.Dock("equipment-exit")), new End(storage, storage.Dock("sample-door")));
            if (laboratories > 0)
            {
                bool broken = completedLevels >= 5 && Next(2) == 1;
                Room first = Attach(ObservationLab.Create(broken), "freight-entry");
                if (laboratories == 2)
                {
                    Room second = Attach(ObservationLab.Create(!broken && completedLevels >= 5), "freight-entry");
                    AddPortal(new End(first, first.Dock("lower-portal-door")),
                        new End(second, second.Dock("lower-portal-door")));
                }
            }
            if (maintenance != null && target >= 7) Attach(maintenance(), "west");
            if (useBranch)
            {
                // The ring remains a compound authored module, with its own live bridge/portal gravity.
                Room authored = Attach(branch.Module, RaidPrefabBranch.Entry, initialUp: true);
                Branch = (inbound[authored].From, authored.Frame, branch);
                rooms.Remove(authored);
            }
            AddServicePortals(servicePortalPairs);
        }

        internal static int CatalogCapacity(int completedLevels)
        {
            int progression = Mathf.Clamp(completedLevels, 0, 14);
            return Mathf.Min(5 + progression, 10) + Mathf.Max(0, progression - 9);
        }

        internal static RaidPlan Generate(int seed, Pose start, int completedLevels = 0, RaidPrefabBranch branch = null,
            Func<GrayModule> maintenance = null)
        {
            const int layouts = 48;
            for (int attempt = 0; attempt < layouts - 1; attempt++)
            {
                try { return new RaidPlan(seed, start, completedLevels, attempt, branch, maintenance); }
                catch (PlacementException) { }
            }
            return new RaidPlan(seed, start, completedLevels, layouts - 1, branch, maintenance);
        }

        internal End Arrival(Room room) => inbound.TryGetValue(room, out FreightLink link) ? link.To : Entrance;
        internal Room RingRoom => Branch.HasValue ? freight[freight.Count - 1].To.Room : null;

        internal static bool Parallel(Vector3 a, Vector3 b) => Mathf.Abs(Vector3.Dot(a, b)) > .999f;

        private int SamePlaneNeighbors(Room room)
        {
            int count = room == Entrance.Room ? 1 : 0; // Native truck.
            foreach (FreightLink link in freight)
            {
                if (link.From.Room == room && Parallel(room.Up, link.To.Room.Up)) count++;
                if (link.To.Room == room && Parallel(room.Up, link.From.Room.Up)) count++;
            }
            foreach (var (a, b) in portals)
            {
                if (a.Room == room && Parallel(room.Up, b.Room.Up)) count++;
                if (b.Room == room && Parallel(room.Up, a.Room.Up)) count++;
            }
            return count;
        }

        private void AddPortal(End a, End b)
        {
            if ((a.Dock.Use & b.Dock.Use & DockUse.DoorPortal) == 0 || a.Dock.Opening != b.Dock.Opening ||
                a.Room.OpenPlugs.Contains(a.Dock.PlugId) || b.Room.OpenPlugs.Contains(b.Dock.PlugId))
                throw new InvalidOperationException("The raid portal needs a free pair of compatible catalog doors.");
            if (Parallel(a.Up, b.Up) && (SamePlaneNeighbors(a.Room) != 0 || SamePlaneNeighbors(b.Room) != 0))
                throw new PlacementException($"Portal shortcut would create three parallel rooms in seed {Seed}.");
            a.Room.OpenPlugs.Add(a.Dock.PlugId);
            b.Room.OpenPlugs.Add(b.Dock.PlugId);
            portals.Add((a, b));
        }

        private void AddServicePortals(int count)
        {
            if (count == 0) return;
            var doors = rooms.SelectMany(room => room.Module.ExternalDocks
                .Where(dock => dock.Id == FreightServiceRoom.ResearchDoor)
                .Select(dock => new End(room, dock))).ToList();
            bool AddsJunction(End end) => end.Room.OpenPlugs.Count == 2;
            var eligible = doors.SelectMany((a, index) => doors.Skip(index + 1)
                .Where(b => !Parallel(a.Up, b.Up) && !freight.Any(link =>
                    (link.From.Room == a.Room && link.To.Room == b.Room) ||
                    (link.From.Room == b.Room && link.To.Room == a.Room)))
                .Select(b => (A: a, B: b, Distance: WalkingDistance(a, b), AcrossArms: Arm(a) != Arm(b), Tie: Next(int.MaxValue))))
                .Where(pair => float.IsFinite(pair.Distance))
                .OrderByDescending(pair => pair.Distance).ThenByDescending(pair => pair.AcrossArms).ThenBy(pair => pair.Tie)
                .Select(pair => (pair.A, pair.B, Junctions: (AddsJunction(pair.A) ? 1 : 0) + (AddsJunction(pair.B) ? 1 : 0)))
                .ToList();
            // Keep the longest shortcut that leaves enough other compatible endpoints. Use
            // the remaining pairs to turn as many through rooms as possible into junctions.
            foreach (var pair in eligible)
            {
                if (Match(WithoutEndpoints(eligible, pair.A, pair.B), count - 1, out var pairs) < 0) continue;
                AddPortal(pair.A, pair.B);
                foreach (var (a, b) in pairs) AddPortal(a, b);
                return;
            }
            throw new PlacementException($"Not enough independent service portal pairs in seed {Seed}.");

            static List<(End A, End B, int Junctions)> WithoutEndpoints(IEnumerable<(End A, End B, int Junctions)> pairs,
                End a, End b) => pairs.Where(other => other.A.Room != a.Room && other.A.Room != b.Room &&
                    other.B.Room != a.Room && other.B.Room != b.Room).ToList();

            static int Match(List<(End A, End B, int Junctions)> candidates, int remaining, out List<(End A, End B)> result)
            {
                if (remaining == 0)
                {
                    result = new List<(End, End)>();
                    return 0;
                }
                int best = -1;
                result = null;
                for (int index = 0; index < candidates.Count; index++)
                {
                    var pair = candidates[index];
                    var free = WithoutEndpoints(candidates.Skip(index + 1), pair.A, pair.B);
                    int rest = Match(free, remaining - 1, out var matches);
                    if (rest < 0 || rest + pair.Junctions <= best) continue;
                    best = rest + pair.Junctions;
                    result = matches;
                    result.Insert(0, (pair.A, pair.B));
                    if (best == 2 * remaining) break;
                }
                return best;
            }
        }

        // The portal can skip this many metres of the walkable freight tree. Measure each
        // room sill-to-sill, so neither closed approaches nor world proximity count.
        internal float WalkingDistance(End a, End b)
        {
            var ancestor = a.Room;
            var parents = new HashSet<Room> { ancestor };
            while (inbound.TryGetValue(ancestor, out var edge))
            {
                ancestor = edge.From.Room;
                parents.Add(ancestor);
            }
            ancestor = b.Room;
            while (!parents.Contains(ancestor)) ancestor = inbound[ancestor].From.Room;
            float first = DistanceToAncestor(a, ancestor, out End exitA);
            float second = DistanceToAncestor(b, ancestor, out End exitB);
            return first + second + DistanceWithin(exitA, exitB);
        }

        private float DistanceToAncestor(End end, Room ancestor, out End reached)
        {
            float distance = 0f;
            while (end.Room != ancestor)
            {
                var edge = inbound[end.Room];
                distance += DistanceWithin(end, edge.To);
                if (edge.Fold != null) distance += RouteLength(edge.Fold.Module.Routes[0]);
                end = edge.From;
            }
            reached = end;
            return distance;
        }

        private static float DistanceWithin(End a, End b)
        {
            if (a.Dock == b.Dock) return 0f;
            return a.Room.Module.Routes.Where(route => route.Flight == null &&
                    ((route.From == a.Dock.Id && route.To == b.Dock.Id) || (route.From == b.Dock.Id && route.To == a.Dock.Id)))
                .Select(route => route.Knots.Zip(route.Knots.Skip(1), (start, end) =>
                    ClippedLength(start.Position, end.Position, a.Dock.Threshold, b.Dock.Threshold)).Sum())
                .DefaultIfEmpty(float.PositiveInfinity).Min();
        }

        private static float ClippedLength(Numerics.Vector3 from, Numerics.Vector3 to, ModulePose first, ModulePose second)
        {
            float lo = 0f, hi = 1f;
            foreach (var sill in new[] { first, second })
            {
                float start = Numerics.Vector3.Dot(from - sill.Position, sill.Forward);
                float end = Numerics.Vector3.Dot(to - sill.Position, sill.Forward);
                if (start > 0f && end > 0f) return 0f;
                if (start > 0f) lo = Mathf.Max(lo, start / (start - end));
                if (end > 0f) hi = Mathf.Min(hi, start / (start - end));
            }
            return Numerics.Vector3.Distance(from, to) * Mathf.Max(0f, hi - lo);
        }

        private string Arm(End end) => end.Room == Entrance.Room ? end.Dock.Id : Arm(inbound[end.Room].From);
        private string Arm(Room room) => Arm(inbound[room].From);
        private int RootArmSize(End end) => rooms.Skip(1).Count(room => Arm(room) == Arm(end));

        private float QuotaSeparation(End end, List<Room> quotas)
        {
            // Measure a weighted route through the unique freight tree. The optional portal edges carry no carts.
            var origin = Ancestors(end.Room);
            float nearest = origin[Entrance.Room];
            foreach (Room quota in quotas)
            {
                var other = Ancestors(quota);
                nearest = Mathf.Min(nearest, origin.Where(entry => other.ContainsKey(entry.Key))
                    .Min(entry => entry.Value + other[entry.Key]));
            }
            return nearest;
        }

        private Dictionary<Room, float> Ancestors(Room room)
        {
            float distance = 0;
            var result = new Dictionary<Room, float> { { room, distance } };
            while (inbound.TryGetValue(room, out FreightLink edge))
            {
                room = edge.From.Room;
                var route = FreightRoute(room, Arrival(room).Dock, edge.From.Dock);
                distance += RouteLength(route);
                if (edge.Fold != null) distance += RouteLength(edge.Fold.Module.Routes[0]);
                result.Add(room, distance);
            }
            return result;
        }

        private static float RouteLength(HaulRoute route) => route.Knots.Zip(route.Knots.Skip(1),
            (a, b) => Numerics.Vector3.Distance(a.Position, b.Position)).Sum();

        internal static HaulRoute FreightRoute(Room room, Dock from, Dock to) =>
            room.Module.Routes.FirstOrDefault(route => route.Cargo == Cargo.Cart && route.Flight == null &&
                ((route.From == from.Id && route.To == to.Id) || (route.From == to.Id && route.To == from.Id)));

        private IEnumerable<End> Exits() =>
            rooms.SelectMany(room => room.Module.ExternalDocks
                .Where(dock => !room.OpenPlugs.Contains(dock.PlugId) && (dock.Use & DockUse.Physical) != 0 &&
                    (dock.Use & DockUse.DoorPortal) == 0 &&
                    dock.MaxCargo == Cargo.Cart && FreightRoute(room, Arrival(room).Dock, dock) != null)
                .Select(dock => new End(room, dock)));

        private void AttachShaftBranch()
        {
            // Enumerate only the new branch, including every valid connector orientation.
            // Failed reservations restore existing rooms/openings and consume no RNG.
            var candidates = Exits().OrderBy(end => Next(int.MaxValue)).ToArray();
            var transit = FreightServiceRoom.Create(FreightServiceRoom.InstrumentService, portal: false);
            var entry = transit.ExternalDocks.Single(dock => dock.Id == "west");
            foreach (var from in candidates)
            foreach (var placement in Attachments(from, transit, entry, false, false))
            {
                var corridor = Commit(from, new End(placement.Target, entry), placement.Fold);
                var corridorLink = inbound[corridor];
                var onward = new End(corridor, corridor.Dock("east"));
                if (TryAttach(new[] { onward }, ShaftPlant.Create(), "freight-entry", out _))
                { ShaftTransit = corridor; return; }
                from.Room.OpenPlugs.Remove(from.Dock.PlugId);
                inbound.Remove(corridor);
                freight.Remove(corridorLink);
                rooms.Remove(corridor);
            }
            throw new PlacementException($"Freight network cannot fit shaft transit branch (seed {Seed}).");
        }

        private Room Attach(GrayModule module, string entry, bool initialUp = false)
        {
            // At full progression, keep staffed rooms on the way to destinations when their
            // remaining sill fits. This gives unopened branches a reason to explore.
            var candidates = Exits().OrderBy(end => CompletedLevels >= 14 && end.Room.OpenPlugs.Count == 1 ? 0 : 1)
                .ThenBy(end => Next(int.MaxValue));
            if (TryAttach(candidates, module, entry, out Room room, initialUp))
                return room;
            throw new PlacementException($"Freight network cannot fit {module.Id} (seed {Seed}).");
        }

        private bool TryAttach(IEnumerable<End> candidates, GrayModule module, string entry, out Room result,
            bool initialUp = false, bool longLink = false)
        {
            Dock entrance = module.ExternalDocks.Single(candidate => candidate.Id == entry);
            foreach (End from in candidates)
            foreach (var placement in Attachments(from, module, entrance, initialUp, longLink))
            {
                result = Commit(from, new End(placement.Target, entrance), placement.Fold);
                return true;
            }
            result = null;
            return false;
        }

        private IEnumerable<(Room Target, Room Fold)> Attachments(End from, GrayModule module, Dock entrance,
            bool initialUp, bool longLink)
        {
            if (from.Dock.Opening != entrance.Opening || entrance.MaxCargo != Cargo.Cart) yield break;
            if (!longLink && SamePlaneNeighbors(from.Room) == 0)
            {
                Room flat = Align(from, module, entrance, rooms.Count);
                if (Fits(flat) && (!initialUp || Vector3.Dot(Entrance.Up, flat.Up) > .999f))
                    yield return (flat, null);
            }
            // Same order and rejection rules as the original single-attachment search.
            foreach (GrayModule foldModule in Turns(from, module, entrance, longLink))
            {
                Dock foldEntry = foldModule.Docks[0];
                Room fold = Align(from, foldModule, foldEntry, -freight.Count - 1);
                if (!Fits(fold)) continue;
                var exit = new End(fold, foldModule.Docks[1]);
                Room turned = Align(exit, module, entrance, rooms.Count);
                if (!Fits(turned) || Intersects(turned, fold) ||
                    (initialUp && Vector3.Dot(Entrance.Up, turned.Up) < .999f)) continue;
                fold.OpenPlugs.Add(foldEntry.PlugId);
                fold.OpenPlugs.Add(exit.Dock.PlugId);
                yield return (turned, fold);
            }
        }

        private IEnumerable<GrayModule> Turns(End from, GrayModule module, Dock entry, bool longLink)
        {
            if (longLink)
            {
                yield return RaidFreightFold.LongDescent(3f);
                yield return RaidFreightFold.LongDescent(8f);
                yield break;
            }
            float source = RaidFreightFold.UpperEdge(from.Room.Module, from.Dock);
            float target = RaidFreightFold.UpperEdge(module, entry);
            if (source > 0f && target > 0f)
                yield return RaidFreightFold.Rise(source, target);
            yield return RaidFreightFold.Descend();
        }

        private Room Commit(End from, End to, Room fold)
        {
            from.Room.OpenPlugs.Add(from.Dock.PlugId);
            to.Room.OpenPlugs.Add(to.Dock.PlugId);
            var link = new FreightLink(from, to, fold);
            freight.Add(link);
            inbound.Add(to.Room, link);
            rooms.Add(to.Room);
            return to.Room;
        }

        private Room Align(End parent, GrayModule module, Dock entry, int index)
        {
            Quaternion rotation = Quaternion.LookRotation(-parent.Outward, parent.Up) *
                Quaternion.Inverse(Quaternion.LookRotation(Vector(entry.Threshold.Forward), Vector(entry.Threshold.Up)));
            return new Room(module, new Pose(parent.Sill - rotation * Vector(entry.Threshold.Position), rotation),
                planningSpace, index);
        }

        private bool Fits(Room room) => !Overlaps(room.Volume, TruckReservation) &&
            rooms.All(other => !Intersects(room, other)) &&
            freight.All(link => link.Fold == null || !Intersects(room, link.Fold));

        private static bool Intersects(Room a, Room b) => Overlaps(a.Volume, b.Volume) &&
            a.Solids.Any(first => b.Solids.Any(second => Overlaps(first, second)));

        private static bool Overlaps(Bounds a, Bounds b)
        {
            Vector3 overlap = Vector3.Min(a.max, b.max) - Vector3.Max(a.min, b.min);
            return overlap.x > FitTolerance && overlap.y > FitTolerance && overlap.z > FitTolerance;
        }

        private int Next(int count)
        {
            uint value = unchecked(random += 0x9e3779b9u);
            value = unchecked((value ^ (value >> 16)) * 0x21f0aaadu);
            value = unchecked((value ^ (value >> 15)) * 0x735a2d97u);
            value ^= value >> 15;
            return (int)(value % (uint)count);
        }

        internal static Bounds BoundsOf(IEnumerable<Vector3> vertices)
        {
            using var enumerator = vertices.GetEnumerator();
            if (!enumerator.MoveNext()) throw new ArgumentException("A raid volume needs geometry.", nameof(vertices));
            var bounds = new Bounds(enumerator.Current, Vector3.zero);
            while (enumerator.MoveNext()) bounds.Encapsulate(enumerator.Current);
            return bounds;
        }

        internal static Vector3 Vector(Numerics.Vector3 vector) => new(vector.X, vector.Y, vector.Z);
    }
}

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
            internal GrayModule Module { get; private set; }
            internal Pose Frame { get; }
            internal HashSet<string> OpenPlugs { get; } = new();
            internal IReadOnlyList<Bounds> Solids { get; private set; }
            internal Bounds Volume { get; private set; }
            internal int Index { get; }
            internal Vector3 Up => Frame.rotation * Vector3.up;

            internal Room(GrayModule module, Pose frame, Pose planningSpace, int index, bool draft = false)
            {
                Module = module;
                Frame = frame;
                Index = index;
                if (draft)
                {
                    Solids = Array.Empty<Bounds>();
                    return;
                }
                MeasureVolume(planningSpace);
            }

            internal void FinalizeSphere(GrayModule module, Pose planningSpace)
            {
                Module = module;
                MeasureVolume(planningSpace);
            }

            private void MeasureVolume(Pose planningSpace)
            {
                Quaternion toPlanning = Quaternion.Inverse(planningSpace.rotation);
                var module = Module;
                var portalExterior = module.ExternalDocks.Where(dock => (dock.Use & DockUse.DoorPortal) != 0)
                    .Select(dock => Block.Box(dock.Id + "-exterior-clearance", BlockRole.Free, dock.Threshold,
                        0f, .65f, 0f, dock.Opening.Y, -dock.Opening.X / 2, dock.Opening.X / 2));
                var placement = module.LayoutReservation is Block reservation
                    ? new[] { reservation } : module.Geometry.Concat(module.Approaches);
                Solids = placement.Concat(portalExterior)
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
            private readonly string moduleId;
            private readonly Dock dock;
            internal Dock Dock => Room == null || Room.Module.Id == moduleId ? dock : Room.Dock(dock.Id);
            internal End(Room room, Dock dock) { Room = room; this.dock = dock; moduleId = room.Module.Id; }
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
            .Select(link => (link.From, link.To)).Concat(handPassages).ToArray();
        internal IReadOnlyList<(End From, End To)> HandPassages => handPassages;
        internal IReadOnlyList<(End A, End B)> Portals => portals;
        internal (End A, End B) Portal => portals[0];
        internal End Entrance { get; }
        internal (End Entrance, Pose Placement, RaidPrefabBranch Descriptor)? Branch { get; }
        internal FreightLink RingLink { get; }
        internal RaidPlanarLayout Planar { get; private set; }
        internal const int MaximumPortalPairs = 4;
        internal int PortalPairCount => portals.Count + RequiredPortalPairs;

        private readonly List<Room> rooms = new();
        private readonly List<FreightLink> freight = new();
        private readonly List<(End From, End To)> handPassages = new();
        private readonly List<(End A, End B)> portals = new();
        private readonly Dictionary<Room, FreightLink> inbound = new();
        private readonly Pose planningSpace;
        private readonly bool drafting;
        private readonly QuotaLayout quotaLayout;
        private readonly Func<Room, bool> canPrepare;
        private readonly int sphereBranches;
        // Reserve the entire stock starter half-space around its 15 m native module width,
        // including its rear extension. Far lateral folded rooms can use the other side of the plane.
        private static readonly Bounds TruckReservation = new Bounds(new Vector3(-64f, 0, 0), new Vector3(128f, 40f, 28f));
        private readonly DiversityScope diversity;
        private uint random;
        private int catalogBranchesPlaced;
        private readonly HashSet<int> catalogBranchRoots = new();
        private const float FitTolerance = .005f;
        private const int CatalogTransitLayouts = 12;
        private static readonly string[] ResearchDocks = { "east", "supply", "spur" };
        private const int BothSphereBranches = SphericalChamber.UpperMask | SphericalChamber.LowerMask;
        private static readonly int[] SphereBranchChoices =
            { BothSphereBranches, SphericalChamber.UpperMask, SphericalChamber.LowerMask, 0 };
        private static readonly (int Unlock, Func<GrayModule> Module, string Entry, string[] Exits)[] CatalogBranches =
        {
            (3, UpperExitCorridor.Create, "lower-west", new[] { "lower-east", "upper" }),
            (3, RightAngleChambers.Create, "airlock-entry", new[] { "distribution-hall-entry" }),
            (4, () => OpposedChambers.Create(), "a-west", Array.Empty<string>()),
            (4, () => OpposedChambers.Create(floorToFloor: true), "a-west", Array.Empty<string>()),
            (4, NicheChamber.Create, "hall-west", new[] { "hall-east" }),
            (5, UShapeShaft.Create, "entry", new[] { "west-top", "east-top" }),
            (5, GalleryChamber.Create, "lab-freight-entry", Array.Empty<string>()),
            (5, QuarterTurnBridge.Create, "flat-entry", new[] { "turned-exit" }),
            (5, SpiralCorridor.Create, "flat-entry", new[] { "inverted-exit" }),
            (5, SphericalChamber.Create, "west", Array.Empty<string>()),
            (5, MobiusHall.Create, "west", new[] { "east" }),
        };

        private enum DiversityScope
        {
            None,
            BranchRooms,
            ShaftAndBranches,
            EntireLayout,
        }

        internal sealed class PlacementException : InvalidOperationException
        {
            internal PlacementException(string message) : base(message) { }
        }

        private RaidPlan(int seed, Pose start, int completedLevels, int attempt, RaidPrefabBranch branch,
            Func<GrayModule> maintenance, DiversityScope diversity, Func<Room, bool> canPrepare, bool drafting, bool spreadQuotas = false,
            int sphereBranches = BothSphereBranches, QuotaLayout quotaLayout = QuotaLayout.Ordinary)
        {
            Seed = seed;
            CompletedLevels = completedLevels;
            Attempt = attempt;
            this.diversity = diversity;
            this.drafting = drafting;
            this.quotaLayout = quotaLayout;
            this.canPrepare = canPrepare;
            this.sphereBranches = sphereBranches;
            planningSpace = start;
            random = unchecked((uint)seed + (uint)attempt * 0x85ebca6bu);
            int target = CatalogCapacity(completedLevels);
            bool fullRaid = target == CatalogCapacity(14);
            var root = new Room(FreightServiceRoom.Create(FreightServiceRoom.Dispatch, portal: fullRaid), start, planningSpace, 0, drafting);
            Entrance = new End(root, root.Dock("west"));
            root.OpenPlugs.Add(Entrance.Dock.PlugId);
            rooms.Add(root);

            int quotaCount = RaidExtraction.AdditionalCount(target);
            bool useBranch = branch != null && completedLevels >= 6 && ((seed ^ completedLevels) & 1) != 0;
            int laboratories = target >= 10 ? 2 : target >= 8 ? 1 : 0;
            int contentCount = 2 + laboratories + (maintenance != null && target >= 7 ? 1 : 0);
            int branchRooms = Math.Max(target - contentCount, contentCount + quotaCount + (useBranch ? 1 : 0) - 1);
            int span = target >= 7 ? 3 + Next(2) : -1;
            bool useLongDescent = (unchecked((uint)seed) & 3u) == 0u || attempt >= CatalogTransitLayouts;
            int serviceOrder = Next(2);
            // The physical spine does not reserve a quota of service rooms for portals.
            // A later shortcut may use any remaining compatible doorway.
            int staffedMinimum = completedLevels >= 5 ? 6 : 2;
            bool diversifyArms = diversity == DiversityScope.EntireLayout;
            AttachFreightCluster(root, fullRaid || target >= 10);

            // Receiving is a staffed room and storage is already included in contentCount.
            int staffed = 1;
            int neededExits = quotaCount + (useBranch ? 1 : 0) + (maintenance != null && target >= 7 ? 1 : 0) + 1;
            while (rooms.Count < branchRooms + 1)
            {
                bool instrumentFirst = (rooms.Count + serviceOrder) % 2 == 0;
                var choices = StaffedRooms(fullRaid || target >= 10, instrumentFirst);
                if (!diversifyArms) choices = choices.Take(1);
                int remaining = branchRooms - rooms.Count;
                if (diversifyArms && staffed + remaining >= staffedMinimum)
                    choices = choices.Concat(CorridorTransits().Select(transit => transit.Module));
                var free = Exits().ToArray();
                var candidates = free.Where(end => rooms.Count >= 4 || end.Room == root)
                    .OrderBy(end => RootArmSize(end)).ThenBy(end => Next(int.MaxValue)).ToArray();
                bool attached = false;
                foreach (GrayModule module in choices.OrderBy(Instances))
                {
                    int exits = module.ExternalDocks.Count(dock => dock.Id != "west" && dock.MaxCargo == Cargo.Cart);
                    if (diversifyArms && free.Length - 1 + exits + remaining < neededExits) continue;
                    if (!TryAttach(candidates, module, "west", out _, longLink: useLongDescent && rooms.Count - 1 == span)) continue;
                    if (IsStaffed(module)) staffed++;
                    attached = true;
                    break;
                }
                if (!attached)
                    throw new PlacementException($"Freight network exhausted at staffed room {rooms.Count} of {branchRooms} (seed {Seed}).");
            }
            bool quotasAttached = spreadQuotas ? TryAttachQuotas(quotaCount) :
                TryAttachOriginalQuotas(quotaCount, new List<Room>(quotaCount));
            if (!quotasAttached)
                throw new PlacementException($"Freight network cannot fit {quotaCount} quotas across its starting arms (seed {Seed}).");
            AttachResearchCluster(laboratories);

            if (maintenance != null && target >= 7) Attach(maintenance(), "west");
            if (useBranch)
            {
                // The ring remains a compound authored module, with its own live bridge/portal gravity.
                Room authored = Attach(branch.Module, RaidPrefabBranch.Entry, initialUp: true);
                RingLink = inbound[authored];
                Branch = (RingLink.From, authored.Frame, branch);
                rooms.Remove(authored);
            }
            AddCatalogBranches();
            if (!drafting) CompleteConnections();
        }

        private RaidPlan(RaidPlan draft, bool requireEnemyStartRoom = false)
        {
            Seed = draft.Seed;
            CompletedLevels = draft.CompletedLevels;
            Attempt = draft.Attempt;
            diversity = draft.diversity;
            sphereBranches = draft.sphereBranches;
            canPrepare = draft.canPrepare;
            random = draft.random;
            catalogBranchesPlaced = draft.catalogBranchesPlaced;
            catalogBranchRoots.UnionWith(draft.catalogBranchRoots);
            planningSpace = draft.planningSpace;
            var root = new Room(draft.Entrance.Room.Module, planningSpace, planningSpace, 0);
            Entrance = new End(root, draft.Entrance.Dock);
            root.OpenPlugs.Add(Entrance.Dock.PlugId);
            rooms.Add(root);
            var destinations = draft.rooms.Skip(1)
                .Concat(draft.RingRoom == null ? Array.Empty<Room>() : new[] { draft.RingRoom })
                .OrderBy(room => room.Index).ToArray();
            int remaining = 320;
            if (!Place(0))
                throw new PlacementException($"The planar freight tree cannot be folded in seed {Seed}, attempt {Attempt}.");
            ShaftTransit = rooms[draft.ShaftTransit.Index];
            if (draft.Branch is { } authored)
            {
                Room ring = rooms[draft.RingRoom.Index];
                RingLink = inbound[ring];
                Branch = (RingLink.From, ring.Frame, authored.Descriptor);
                rooms.Remove(ring);
            }
            CompleteConnections();
            if (requireEnemyStartRoom)
            {
                // Local floor contours and physical connections retain their keys and docks.
                Planar = draft.Planar;
            }
            else
            {
                if (!RaidPlanarLayout.TryCreate(this, out var planar))
                    throw new PlacementException($"The freight tree cannot be laid out in the plane for seed {Seed}, attempt {Attempt}.");
                Planar = planar;
            }

            bool Place(int index)
            {
                if (index == destinations.Length) return !requireEnemyStartRoom || HasEnemyStartRoom();
                if (remaining <= 0) return false;
                remaining--;
                Room original = destinations[index];
                var edge = draft.inbound[original];
                Room parent = rooms[edge.From.Room.Index];
                var from = new End(parent, parent.Module == edge.From.Room.Module ? edge.From.Dock : parent.Dock(edge.From.Dock.Id));
                Dock target = edge.To.Dock;
                if (!draft.freight.Contains(edge))
                {
                    if (SamePlaneNeighbors(from) != 0) return false;
                    if (PlaceHandVariant(original.Module, target)) return true;
                    int mask = SphericalChamber.GetMask(original.Module.Id);
                    if ((mask & SphericalChamber.EastMask) == 0 || remaining <= 0) return false;
                    // The draft made sure both portal outcomes have prepared prefabs.
                    // Dropping optional east clearance can fit the same physical annexes.
                    var sealedEast = SphericalChamber.Create(mask & ~SphericalChamber.EastMask);
                    return PlaceHandVariant(sealedEast, sealedEast.ExternalDocks.Single(dock => dock.Id == target.Id));

                    bool PlaceHandVariant(GrayModule module, Dock entry)
                    {
                        Room room = Align(from, module, entry, original.Index);
                        if (!Fits(room)) return false;
                        CommitHand(from, new End(room, entry));
                        if (Place(index + 1)) return true;
                        handPassages.RemoveAt(handPassages.Count - 1);
                        RemoveRoom(room);
                        return false;
                    }
                }

                bool ring = original == draft.RingRoom;
                bool extended = edge.Fold?.Module.Id == RaidFreightFold.LongSpan;
                bool needsFold = original.Module.Id == "shaft-plant";
                var options = Attachments(from, original.Module, target, ring, extended);
                if (requireEnemyStartRoom)
                    options = options.OrderBy(placement => (placement.Target.Frame.position - original.Frame.position).sqrMagnitude);
                foreach (var placement in options)
                {
                    if (remaining <= 0) break;
                    if (needsFold && placement.Fold == null) continue;
                    Room room = Commit(from, new End(placement.Target, target), placement.Fold);
                    if (Place(index + 1)) return true;
                    Undo(room);
                }
                return false;
            }
        }

        private RaidPlan(RaidPlan layout, int reservedTransit, float originalSpacing)
        {
            Seed = layout.Seed;
            CompletedLevels = layout.CompletedLevels;
            Attempt = layout.Attempt;
            diversity = layout.diversity;
            this.canPrepare = layout.canPrepare;
            sphereBranches = layout.sphereBranches;
            random = layout.random;
            catalogBranchesPlaced = layout.catalogBranchesPlaced;
            catalogBranchRoots.UnionWith(layout.catalogBranchRoots);
            planningSpace = layout.planningSpace;
            var root = new Room(layout.Entrance.Room.Module, planningSpace, planningSpace, 0);
            Entrance = new End(root, layout.Entrance.Dock);
            root.OpenPlugs.Add(Entrance.Dock.PlugId);
            rooms.Add(root);

            // Retain the selected rooms and their authored connections while reserving station
            // footprints before the last transit rooms and the optional branches are placed.
            var originals = layout.rooms.Skip(1)
                .Concat(layout.RingRoom == null ? Array.Empty<Room>() : new[] { layout.RingRoom })
                .OrderBy(room => room.Index).ToArray();
            var quotaSlots = originals.Where(room => room.Module.Landmarks.ContainsKey(RaidExtraction.Machine)).ToArray();
            var quotaApproaches = quotaSlots.Select(room =>
            {
                var ramp = RaidExtraction.LoadingRoute(room.Module);
                return (Length: RouteLength(ramp), Anchor: ramp.Knots.Last().Position);
            }).ToArray();
            var destinations = originals.Except(quotaSlots).ToArray();
            var occupiedDocks = destinations.Select(room => layout.inbound[room].From)
                .Select(from => (from.Room.Index, from.Dock.Id)).ToHashSet();
            var placed = new Room[originals.Length + 1];
            placed[0] = root;
            var reservations = new List<FreightLink>(quotaSlots.Length);
            int reserveAt = quotaSlots[0].Index - reservedTransit;
            const float spacingTolerance = .01f;
            int reservationsLeft = 800;
            int transitPlacementsLeft = 1800;
            bool catalogRelocated = false;
            if (!Place(0))
                throw new PlacementException($"Freight frontier could not preserve a more widely spaced set of quotas in seed {Seed}.");

            rooms.Sort((a, b) => a.Index.CompareTo(b.Index));
            ShaftTransit = placed[layout.ShaftTransit.Index];
            if (layout.Branch is { } authored)
            {
                Room ring = placed[layout.RingRoom.Index];
                RingLink = inbound[ring];
                Branch = (RingLink.From, ring.Frame, authored.Descriptor);
                rooms.Remove(ring);
            }
            CompleteConnections();
            if (layout.Planar != null)
            {
                if (!RaidPlanarLayout.TryCreate(this, out var planar))
                    throw new PlacementException($"The reserved freight tree cannot be laid out in the plane in seed {Seed}.");
                Planar = planar;
            }

            bool Place(int index)
            {
                if (index == destinations.Length) return reservations.Count == quotaSlots.Length;
                if (transitPlacementsLeft-- <= 0 || reservationsLeft <= 0) return false;
                Room original = destinations[index];
                // Protect one pad on each arm before placing the last transit room(s).
                if (original.Index == reserveAt && reservations.Count == 0)
                    return Reserve(index, 2);
                if (original.Index > quotaSlots[quotaSlots.Length - 1].Index && reservations.Count < quotaSlots.Length)
                    return Reserve(index, quotaSlots.Length);

                var edge = layout.inbound[original];
                Room parent = placed[edge.From.Room.Index];
                var from = new End(parent, parent.Dock(edge.From.Dock.Id));
                Dock target = edge.To.Dock;
                if (!layout.freight.Contains(edge))
                {
                    if (SamePlaneNeighbors(from) != 0) return false;
                    Room room = Align(from, original.Module, target, original.Index);
                    if (!Fits(room)) return false;
                    CommitHand(from, new End(room, target));
                    placed[room.Index] = room;
                    if (Place(index + 1)) return true;
                    handPassages.RemoveAt(handPassages.Count - 1);
                    RemoveRoom(room);
                    return false;
                }

                bool ring = original == layout.RingRoom;
                bool extended = edge.Fold?.Module.Id == RaidFreightFold.LongSpan;
                bool needsFold = original.Module.Id == "shaft-plant";
                var options = Attachments(from, original.Module, target, ring, extended, original.Index)
                    .Where(placement => !needsFold || placement.Fold != null)
                    .OrderBy(placement => (placement.Target.Frame.position - original.Frame.position).sqrMagnitude)
                    .ToArray();
                foreach (var placement in options)
                    if (PlaceFreight(from, placement)) return true;
                if (options.Length > 0 || catalogRelocated || !catalogBranchRoots.Contains(original.Index) ||
                    transitPlacementsLeft <= 0) return false;

                // A reserved station may block the original approach to an optional branch. Give
                // that entire branch one other dock while retaining its rooms and their connections.
                catalogRelocated = true;
                var alternatives = Exits().Where(end => end.Room != root &&
                        !occupiedDocks.Contains((end.Room.Index, end.Dock.Id)))
                    .OrderBy(end => Arm(end) != layout.Arm(edge.From)).ThenBy(end => (end.Sill - from.Sill).sqrMagnitude);
                foreach (End alternative in alternatives.ToArray())
                {
                    if (transitPlacementsLeft <= 0) break;
                    foreach (var placement in Attachments(alternative, original.Module, target, ring, extended, original.Index))
                        if (PlaceFreight(alternative, placement)) return true;
                }
                catalogRelocated = false;
                return false;

                bool PlaceFreight(End source, (Room Target, Room Fold) placement)
                {
                    if (transitPlacementsLeft <= 0) return false;
                    Room room = Commit(source, new End(placement.Target, target), placement.Fold);
                    placed[room.Index] = room;
                    if (Place(index + 1)) return true;
                    Undo(room);
                    return false;
                }
            }

            bool Reserve(int index, int target)
            {
                if (reservations.Count == target) return Place(index);
                if (reservationsLeft <= 0 || transitPlacementsLeft <= 0) return false;
                Room slot = quotaSlots[reservations.Count];
                var entry = layout.Arrival(slot).Dock;
                var (approach, anchor) = quotaApproaches[reservations.Count];
                string usedArm = reservations.Count == 1 ? Arm(reservations[0].From) : null;
                var options = new List<(End From, Room Room, Room Fold, float World, float Cart)>();
                foreach (End from in Exits())
                {
                    if (from.Room == root || (usedArm != null && Arm(from) == usedArm) ||
                        occupiedDocks.Contains((from.Room.Index, from.Dock.Id))) continue;
                    float toTruck = TreeDistance(from, Entrance, cartOnly: true) + approach;
                    float toQuotas = reservations.Select((link, i) => TreeDistance(from, link.To, cartOnly: true) +
                            approach + quotaApproaches[i].Length)
                        .DefaultIfEmpty(float.PositiveInfinity).Min();
                    if (!float.IsFinite(toTruck) || (reservations.Count > 0 && !float.IsFinite(toQuotas))) continue;
                    foreach (var placement in Attachments(from, slot.Module, entry, initialUp: false, longLink: false,
                        roomIndex: slot.Index))
                    {
                        float foldLength = placement.Fold == null ? 0f : RouteLength(placement.Fold.Module.Routes[0]);
                        Vector3 pad = placement.Target.Point(anchor);
                        float worldDistance = reservations.Select((link, i) => Vector3.Distance(pad,
                                link.To.Room.Point(quotaApproaches[i].Anchor)))
                            .DefaultIfEmpty(Vector3.Distance(pad, Entrance.Sill)).Min();
                        if (worldDistance <= originalSpacing + spacingTolerance) continue;
                        options.Add((from, placement.Target, placement.Fold, worldDistance,
                            Mathf.Min(toQuotas, toTruck) + foldLength));
                    }
                }
                foreach (var option in options.OrderByDescending(option => option.World).ThenByDescending(option => option.Cart))
                {
                    if (reservationsLeft-- <= 0 || transitPlacementsLeft <= 0) return false;
                    Room quota = Commit(option.From, new End(option.Room, entry), option.Fold);
                    reservations.Add(inbound[quota]);
                    if (Reserve(index, target)) return true;
                    reservations.RemoveAt(reservations.Count - 1);
                    Undo(quota);
                }
                return false;
            }
        }

        internal static int CatalogCapacity(int completedLevels)
        {
            int progression = Mathf.Clamp(completedLevels, 0, 14);
            return Mathf.Min(5 + progression, 10) + Mathf.Max(0, progression - 9);
        }

        internal static RaidPlan Generate(int seed, Pose start, int completedLevels = 0, RaidPrefabBranch branch = null,
            Func<GrayModule> maintenance = null, Func<Room, bool> canPrepare = null, bool rotaryQuota = false)
        {
            var layout = GenerateWithQuotaFallback(seed, completedLevels, rotaryQuota,
                selection => GenerateLayoutWithReservedQuotas(seed, start, completedLevels, branch, maintenance, canPrepare, selection));
            if (layout.HasEnemyStartRoom()) return layout;
            try
            {
                // Keep the selected modules and freight tree. Revisit only the physical folds
                // when every deep room with a floor usable by stock enemies is turned away from world up.
                var candidate = new RaidPlan(layout, requireEnemyStartRoom: true);
                if (candidate.PortalPairCount < layout.PortalPairCount ||
                    !candidate.rooms.Select(room => room.Module.Id).SequenceEqual(layout.rooms.Select(room => room.Module.Id)))
                    return layout;
                if (layout.QuotaCount >= 2)
                {
                    var spacing = layout.MeasureQuotaSpacing();
                    var revised = candidate.MeasureQuotaSpacing();
                    const float tolerance = .01f;
                    if (revised.World + tolerance < Mathf.Min(spacing.World, RaidExtraction.Width) ||
                        revised.Map + tolerance < Mathf.Min(spacing.Map, RaidExtraction.Width) ||
                        revised.PairCart + tolerance < spacing.PairCart ||
                        revised.StockCart + tolerance < spacing.StockCart)
                        return layout;
                }
                return candidate;
            }
            catch (PlacementException) { return layout; }
        }

        internal static RaidPlan GenerateWithQuotaFallback(int seed, int completedLevels, bool rotaryQuota,
            Func<QuotaLayout, RaidPlan> generate)
        {
            bool progression = completedLevels >= RaidExtraction.StraightBridgeUnlock;
            QuotaLayout selection = rotaryQuota ? QuotaLayout.Rotary : progression ? QuotaLayout.Progressive : QuotaLayout.Ordinary;
            while (true)
            {
                try { return generate(selection); }
                catch (PlacementException error) when (selection != QuotaLayout.Ordinary)
                {
                    Debug.LogWarning($"[Contour4 Procedural] {selection} quotas could not fit seed {seed}. {error.Message}");
                    selection = selection == QuotaLayout.Rotary && progression ? QuotaLayout.Progressive : QuotaLayout.Ordinary;
                }
            }
        }

        private bool HasEnemyStartRoom() => rooms.Any(room => Vector3.Dot(room.Up, Vector3.up) > .9999f &&
            IsEnemyStartInterior(room.Module) && inbound[room].From.Room != Entrance.Room);

        private static bool IsEnemyStartInterior(GrayModule module) =>
            module.Id is "calibration-sector" or "observation-lab" or "observation-lab-broken-floor" or
                "shaft-plant" or "sample-storage" or "sample-storage-narrow" or
                FreightServiceRoom.InstrumentService or FreightServiceRoom.SampleReceiving;

        internal static RaidPlan GenerateLayoutWithReservedQuotas(int seed, Pose start, int completedLevels,
            RaidPrefabBranch branch, Func<GrayModule> maintenance, Func<Room, bool> canPrepare, QuotaLayout quotaLayout)
        {
            var baseline = GenerateLayoutWithQuotaSpacing(seed, start, completedLevels, branch, maintenance, canPrepare, quotaLayout);
            if (baseline.QuotaCount < 2) return baseline;

            var spacing = baseline.MeasureQuotaSpacing();
            if (spacing.World >= RaidExtraction.Width) return baseline;

            RaidPlan best = baseline;
            float bestWorld = spacing.World;
            const float tolerance = .01f;
            foreach (int reservedTransit in new[] { 1, 2 })
            {
                try
                {
                    var candidate = new RaidPlan(baseline, reservedTransit, spacing.World);
                    var spread = candidate.MeasureQuotaSpacing();
                    if (candidate.PortalPairCount < baseline.PortalPairCount ||
                        !candidate.rooms.Select(room => room.Module.Id).SequenceEqual(baseline.rooms.Select(room => room.Module.Id)) ||
                        spread.World <= bestWorld + tolerance || spread.Map + tolerance < Mathf.Min(spacing.Map, RaidExtraction.Width))
                        continue;
                    best = candidate;
                    bestWorld = spread.World;
                    if (bestWorld >= RaidExtraction.Width) break;
                }
                catch (PlacementException) { }
            }
            return best;
        }

        private static RaidPlan GenerateLayoutWithQuotaSpacing(int seed, Pose start, int completedLevels,
            RaidPrefabBranch branch, Func<GrayModule> maintenance, Func<Room, bool> canPrepare, QuotaLayout quotaLayout)
        {
            var baseline = GenerateLayout(seed, start, completedLevels, branch, maintenance, canPrepare, quotaLayout);
            if (baseline.QuotaCount < 2) return baseline;

            var spacing = baseline.MeasureQuotaSpacing();
            float approach = RouteLength(RaidExtraction.LoadingRoute(RaidExtraction.Create()));
            // Only revisit a layout when loading pads are crowded by the scale of their own chambers.
            if (Mathf.Min(spacing.World, spacing.Map) >= RaidExtraction.Width &&
                spacing.PairCart >= 2f * (RaidExtraction.Length + approach)) return baseline;

            // Keep the existing attempt allowance. Probe this successful attempt with its room selection
            // and, when different, the base room selection.
            foreach (var diversity in new[] { baseline.diversity, DiversityScope.None }.Distinct())
            {
                try
                {
                    var candidate = baseline.Planar == null
                        ? new RaidPlan(seed, start, completedLevels, baseline.Attempt, branch, maintenance,
                            diversity, canPrepare, drafting: false, spreadQuotas: true, quotaLayout: quotaLayout)
                        : GeneratePlanar(seed, start, completedLevels, baseline.Attempt, branch, maintenance,
                            diversity, canPrepare, spreadQuotas: true, quotaLayout: quotaLayout);
                    if (candidate.QuotaCount != baseline.QuotaCount || candidate.Rooms.Count < baseline.Rooms.Count ||
                        candidate.catalogBranchesPlaced < baseline.catalogBranchesPlaced ||
                        candidate.PortalPairCount < baseline.PortalPairCount ||
                        candidate.RepeatedRooms() > baseline.RepeatedRooms()) continue;

                    var spread = candidate.MeasureQuotaSpacing();
                    const float tolerance = .01f;
                    if (spread.PairCart + tolerance < spacing.PairCart || spread.StockCart + tolerance < spacing.StockCart ||
                        spread.World + tolerance < spacing.World || spread.Map + tolerance < spacing.Map) continue;
                    if (spread.PairCart > spacing.PairCart + tolerance || spread.World > spacing.World + tolerance ||
                        spread.Map > spacing.Map + tolerance) return candidate;
                }
                catch (PlacementException) { }
            }
            return baseline;
        }

        private static RaidPlan GenerateLayout(int seed, Pose start, int completedLevels, RaidPrefabBranch branch,
            Func<GrayModule> maintenance, Func<Room, bool> canPrepare, QuotaLayout quotaLayout)
        {
            var baseline = GenerateBaseline(seed, start, completedLevels, branch, maintenance, canPrepare, quotaLayout);
            int bestRepeats = baseline.RepeatedRooms();
            RaidPlan best = baseline;
            var alternatives = CatalogBranchTarget(completedLevels) == 0
                ? Enumerable.Range(0, 4).Select(attempt => (attempt, DiversityScope.EntireLayout))
                : new[]
            {
                (baseline.Attempt, DiversityScope.BranchRooms),
                (baseline.Attempt, DiversityScope.ShaftAndBranches),
                (baseline.Attempt, DiversityScope.EntireLayout),
                (baseline.Attempt + 1, DiversityScope.EntireLayout),
            };
            // Only compare complete layouts with the same size and mandatory connections.
            foreach (var (attempt, diversity) in alternatives)
            {
                if (bestRepeats == 0) break;
                try
                {
                    // Rank alternatives within the generation path that produced the baseline.
                    var candidate = baseline.Planar == null
                        ? new RaidPlan(seed, start, completedLevels, attempt, branch, maintenance, diversity, canPrepare,
                            drafting: false, quotaLayout: quotaLayout)
                        : GeneratePlanar(seed, start, completedLevels, attempt, branch, maintenance, diversity, canPrepare,
                            quotaLayout: quotaLayout);
                    if (candidate.Rooms.Count != baseline.Rooms.Count || candidate.QuotaCount != baseline.QuotaCount ||
                        candidate.PortalPairCount < baseline.PortalPairCount || candidate.catalogBranchesPlaced < baseline.catalogBranchesPlaced)
                        continue;
                    int repeats = candidate.RepeatedRooms();
                    if (repeats >= bestRepeats) continue;
                    best = candidate;
                    bestRepeats = repeats;
                }
                catch (PlacementException) { }
            }
            return best;
        }

        private static RaidPlan GeneratePlanar(int seed, Pose start, int completedLevels, int attempt,
            RaidPrefabBranch branch, Func<GrayModule> maintenance, DiversityScope diversity, Func<Room, bool> canPrepare,
            bool spreadQuotas = false, QuotaLayout quotaLayout = QuotaLayout.Ordinary)
        {
            var draft = new RaidPlan(seed, start, completedLevels, attempt, branch, maintenance, diversity, canPrepare,
                drafting: true, spreadQuotas: spreadQuotas, quotaLayout: quotaLayout);
            try
            {
                return new RaidPlan(draft);
            }
            catch (PlacementException) when (draft.rooms.Any(room => SphericalChamber.IsVariant(room.Module.Id)))
            {
                var sphere = draft.rooms.Single(room => SphericalChamber.IsVariant(room.Module.Id));
                int usedBranches = SphericalChamber.GetMask(sphere.Module.Id) & BothSphereBranches;
                foreach (int branches in SphereBranchChoices.Skip(Array.IndexOf(SphereBranchChoices, usedBranches) + 1))
                {
                    try
                    {
                        var reduced = new RaidPlan(seed, start, completedLevels, attempt, branch, maintenance, diversity,
                            canPrepare, drafting: true, spreadQuotas: spreadQuotas, sphereBranches: branches, quotaLayout: quotaLayout);
                        return new RaidPlan(reduced);
                    }
                    catch (PlacementException) { }
                }
                throw;
            }
        }

        private int RepeatedRooms()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return rooms.Skip(1).Count(room => !room.Module.Landmarks.ContainsKey(RaidExtraction.Machine) &&
                !seen.Add(room.Module.Id));
        }

        private static RaidPlan GenerateBaseline(int seed, Pose start, int completedLevels, RaidPrefabBranch branch,
            Func<GrayModule> maintenance, Func<Room, bool> canPrepare, QuotaLayout quotaLayout)
        {
            const int planarLayouts = 4;
            const int layouts = 48;
            const int additionalLayouts = 4;
            int lastAttempt = layouts - 1;
            int desired = CatalogBranchTarget(completedLevels);
            RaidPlan best = null;
            for (int attempt = 0; attempt < planarLayouts; attempt++)
            {
                try
                {
                    var plan = GeneratePlanar(seed, start, completedLevels, attempt, branch, maintenance, DiversityScope.None, canPrepare,
                        quotaLayout: quotaLayout);
                    if (desired <= 1 || plan.catalogBranchesPlaced == desired) return plan;
                    if (best == null || plan.catalogBranchesPlaced > best.catalogBranchesPlaced) best = plan;
                }
                catch (PlacementException) { }
            }
            if (best != null) return best;

            // Keep previously supported seeds playable while the independent flat solver
            // covers more catalogue combinations. Portal budgeting remains shared.
            for (int attempt = 0; attempt <= lastAttempt; attempt++)
            {
                try
                {
                    var plan = new RaidPlan(seed, start, completedLevels, attempt, branch, maintenance, DiversityScope.None, canPrepare,
                        drafting: false, quotaLayout: quotaLayout);
                    if (desired <= 1 || plan.catalogBranchesPlaced == desired) return plan;
                    if (best == null) lastAttempt = Math.Min(lastAttempt, attempt + additionalLayouts);
                    if (best == null || plan.catalogBranchesPlaced > best.catalogBranchesPlaced) best = plan;
                }
                catch (PlacementException) when (best != null || attempt < lastAttempt) { }
            }
            return best;
        }

        private static int CatalogBranchTarget(int completedLevels)
        {
            int firstUnlock = CatalogBranches[0].Unlock;
            return completedLevels < firstUnlock ? 0 : Math.Min(4, 1 + (completedLevels - firstUnlock) / 3);
        }

        internal End Arrival(Room room) => inbound.TryGetValue(room, out FreightLink link) ? link.To : Entrance;
        internal Room RingRoom => RingLink?.To.Room;
        private int NextRoomIndex => rooms.Count + (RingRoom == null ? 0 : 1);

        internal static bool Parallel(Vector3 a, Vector3 b) => Mathf.Abs(Vector3.Dot(a, b)) > .999f;

        internal int SamePlaneNeighbors(End end)
        {
            int count = OnSameFloor(Entrance) ? 1 : 0; // Native truck.
            foreach (FreightLink link in freight)
            {
                if (link.Fold != null) continue;
                if (OnSameFloor(link.From)) count++;
                if (OnSameFloor(link.To)) count++;
            }
            foreach (var (from, to) in handPassages)
            {
                if (OnSameFloor(from)) count++;
                if (OnSameFloor(to)) count++;
            }
            foreach (var (a, b) in portals)
            {
                if (!Parallel(a.Up, b.Up)) continue;
                if (OnSameFloor(a)) count++;
                if (OnSameFloor(b)) count++;
            }
            return count;

            // A curved module can have two orientations on one swept floor (e.g. the spiral).
            bool OnSameFloor(End other) => other.Room == end.Room && other.Dock.FloorId == end.Dock.FloorId &&
                Vector3.Dot(other.Up, end.Up) > .999f;
        }

        private void AddPortal(End a, End b)
        {
            if ((a.Dock.Use & b.Dock.Use & DockUse.DoorPortal) == 0 || a.Dock.Opening != b.Dock.Opening ||
                a.Room.OpenPlugs.Contains(a.Dock.PlugId) || b.Room.OpenPlugs.Contains(b.Dock.PlugId))
                throw new InvalidOperationException("The raid portal needs a free pair of compatible catalog doors.");
            if (Parallel(a.Up, b.Up) && (SamePlaneNeighbors(a) != 0 || SamePlaneNeighbors(b) != 0))
                throw new PlacementException($"Portal shortcut would create three parallel rooms in seed {Seed}.");
            a.Room.OpenPlugs.Add(a.Dock.PlugId);
            b.Room.OpenPlugs.Add(b.Dock.PlugId);
            portals.Add((a, b));
        }

        private int RequiredPortalPairs => rooms.Sum(room => room.Module.InternalPortals.Count) +
            (Branch?.Descriptor.PortalPairCount ?? 0);

        private void CompleteConnections()
        {
            SelectPortals();
            foreach (Room room in rooms)
            {
                int mask = SphericalChamber.GetMask(room.Module.Id);
                if ((mask & SphericalChamber.EastMask) != 0 && !room.OpenPlugs.Contains(room.Dock("east").PlugId))
                    room.FinalizeSphere(SphericalChamber.Create(mask & ~SphericalChamber.EastMask), planningSpace);
            }
        }

        private void SelectPortals()
        {
            int available = MaximumPortalPairs - RequiredPortalPairs;
            if (available < 0)
                throw new PlacementException($"Mandatory portal pairs exceed the raid budget in seed {Seed}.");
            if (available == 0) return;

            var doors = rooms.SelectMany(room => room.Module.ExternalDocks
                .Where(dock => (dock.Use & DockUse.DoorPortal) != 0 && !room.OpenPlugs.Contains(dock.PlugId))
                .Select(dock => new End(room, dock))).ToArray();
            var candidates = doors.SelectMany((a, index) => doors.Skip(index + 1)
                    .Where(b => a.Room != b.Room && a.Dock.Opening == b.Dock.Opening)
                    .Select(b => (A: a, B: b, Distance: WalkingDistance(a, b), AcrossArms: Arm(a) != Arm(b))))
                .Where(pair => float.IsFinite(pair.Distance) && pair.Distance >= 12f)
                .OrderByDescending(pair => pair.Distance)
                .ThenByDescending(pair => pair.AcrossArms)
                .ThenBy(pair => pair.A.Room.Index).ThenBy(pair => pair.B.Room.Index);
            foreach (var pair in candidates)
            {
                if (pair.A.Room.OpenPlugs.Contains(pair.A.Dock.PlugId) || pair.B.Room.OpenPlugs.Contains(pair.B.Dock.PlugId))
                    continue;
                if (Parallel(pair.A.Up, pair.B.Up) && (SamePlaneNeighbors(pair.A) != 0 || SamePlaneNeighbors(pair.B) != 0))
                    continue;
                AddPortal(pair.A, pair.B);
                if (--available == 0) break;
            }
        }

        // The portal can skip this many metres of the walkable freight tree. Measure each
        // room sill-to-sill, so neither closed approaches nor world proximity count.
        internal float WalkingDistance(End a, End b) => TreeDistance(a, b, cartOnly: false);

        private float TreeDistance(End a, End b, bool cartOnly)
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
            float first = DistanceToAncestor(a, ancestor, cartOnly, out End exitA);
            float second = DistanceToAncestor(b, ancestor, cartOnly, out End exitB);
            float middle = DistanceWithin(exitA, exitB, cartOnly);
            if (cartOnly && float.IsPositiveInfinity(middle))
            {
                End arrival = Arrival(ancestor);
                middle = DistanceWithin(exitA, arrival, cartOnly: true) +
                    DistanceWithin(arrival, exitB, cartOnly: true);
            }
            return first + second + middle;
        }

        private float DistanceToAncestor(End end, Room ancestor, bool cartOnly, out End reached)
        {
            float distance = 0f;
            while (end.Room != ancestor)
            {
                var edge = inbound[end.Room];
                distance += DistanceWithin(end, edge.To, cartOnly);
                if (edge.Fold != null) distance += RouteLength(edge.Fold.Module.Routes[0]);
                end = edge.From;
            }
            reached = end;
            return distance;
        }

        private static float DistanceWithin(End a, End b, bool cartOnly)
        {
            if (a.Dock == b.Dock) return 0f;
            if (cartOnly)
            {
                var route = FreightRoute(a.Room, a.Dock, b.Dock);
                return route == null ? float.PositiveInfinity : LengthBetweenSills(route);
            }
            return a.Room.Module.Routes.Where(route => route.Flight == null &&
                    ((route.From == a.Dock.Id && route.To == b.Dock.Id) || (route.From == b.Dock.Id && route.To == a.Dock.Id)))
                .Select(LengthBetweenSills).DefaultIfEmpty(float.PositiveInfinity).Min();

            float LengthBetweenSills(HaulRoute route) => route.Knots.Zip(route.Knots.Skip(1), (start, end) =>
                ClippedLength(start.Position, end.Position, a.Dock.Threshold, b.Dock.Threshold)).Sum();
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

        private bool TryAttachOriginalQuotas(int count, List<Room> quotas)
        {
            if (quotas.Count == count) return true;
            // Keep the previous allocation available for layouts that already have adequate spacing.
            // Reserve a fitting quota on each start arm before filling extra extraction slots.
            string usedArm = quotas.Count == 1 ? Arm(quotas[0]) : null;
            var candidates = Exits().Where(end => end.Room != Entrance.Room && Arm(end) != usedArm)
                .Select(end => (End: end, Separation: OriginalQuotaSeparation(end, quotas), Tie: Next(int.MaxValue)))
                .OrderByDescending(entry => entry.Separation).ThenBy(entry => entry.Tie)
                .Select(entry => entry.End).ToArray();
            return TryAttach(candidates, RaidExtraction.CreateForSlot(CompletedLevels, quotas.Count, quotaLayout, Seed),
                RaidExtraction.Entry, out _, placeNext: room =>
            {
                quotas.Add(room);
                if (TryAttachOriginalQuotas(count, quotas)) return true;
                quotas.RemoveAt(quotas.Count - 1);
                return false;
            });
        }

        private float OriginalQuotaSeparation(End end, List<Room> quotas)
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

        private bool TryAttachQuotas(int count)
        {
            if (count == 0) return true;

            const int improvementPlacements = 24;
            var slots = Enumerable.Range(0, count).Select(slot =>
            {
                var module = RaidExtraction.CreateForSlot(CompletedLevels, slot, quotaLayout, Seed);
                var ramp = RaidExtraction.LoadingRoute(module);
                return (Module: module, Entry: module.ExternalDocks.Single(dock => dock.Id == RaidExtraction.Entry),
                    Length: RouteLength(ramp), Anchor: ramp.Knots.Last().Position);
            }).ToArray();
            var chosen = new List<FreightLink>(count);
            (float Cart, float Spatial) bestSpacing = (float.NegativeInfinity, float.NegativeInfinity);
            FreightLink[] best = null;
            uint bestRandom = random;
            int improvementsLeft = improvementPlacements;

            Search(float.PositiveInfinity, float.PositiveInfinity);
            if (best == null) return false;
            foreach (FreightLink link in best) Commit(link.From, link.To, link.Fold);
            random = bestRandom;
            return true;

            void Search(float cart, float spatial)
            {
                if (chosen.Count == count)
                {
                    best = chosen.ToArray();
                    bestSpacing = (cart, spatial);
                    bestRandom = random;
                    return;
                }

                // The first two extractions reserve one station on each starter arm.
                var slot = slots[chosen.Count];
                string usedArm = chosen.Count == 1 ? Arm(chosen[0].To.Room) : null;
                var candidates = Exits().Where(end => end.Room != Entrance.Room && Arm(end) != usedArm)
                    .Select(end => (End: end, Cart: DistanceToStations(end, slot.Length), Tie: Next(int.MaxValue)))
                    .OrderByDescending(candidate => candidate.Cart).ThenBy(candidate => candidate.Tie).ToArray();
                uint nextRandom = random;
                foreach (var candidate in candidates)
                foreach (var placement in Attachments(candidate.End, slot.Module, slot.Entry, initialUp: false, longLink: false))
                {
                    if (best != null && improvementsLeft-- <= 0) return;
                    float fold = placement.Fold == null ? 0f : RouteLength(placement.Fold.Module.Routes[0]);
                    float nearestCart = Mathf.Min(cart, candidate.Cart + fold);
                    float nearestSpatial = spatial;
                    if (!drafting)
                    {
                        var anchor = placement.Target.Point(slot.Anchor);
                        for (int i = 0; i < chosen.Count; i++)
                            nearestSpatial = Mathf.Min(nearestSpatial,
                                Vector3.Distance(anchor, chosen[i].To.Room.Point(slots[i].Anchor)));
                    }
                    if (CompareQuotaSpacing((nearestCart, nearestSpatial), bestSpacing) <= 0) continue;

                    random = nextRandom;
                    Room room = Commit(candidate.End, new End(placement.Target, slot.Entry), placement.Fold);
                    chosen.Add(inbound[room]);
                    Search(nearestCart, nearestSpatial);
                    chosen.RemoveAt(chosen.Count - 1);
                    Undo(room);
                    // The playable fold shapes are solved separately for the planar draft.
                    if (drafting || improvementsLeft <= 0) break;
                }
            }

            float DistanceToStations(End end, float approach)
            {
                // The native truck connects at Entrance. Its interior is beyond the planned route.
                float nearest = TreeDistance(end, Entrance, cartOnly: true);
                for (int i = 0; i < chosen.Count; i++)
                    nearest = Mathf.Min(nearest, TreeDistance(end, chosen[i].To, cartOnly: true) + slots[i].Length);
                return nearest + approach;
            }
        }

        private (float PairCart, float StockCart, float World, float Map) MeasureQuotaSpacing()
        {
            var stations = rooms.Where(room => room.Module.Landmarks.ContainsKey(RaidExtraction.Machine))
                .Select(room =>
                {
                    var entry = Arrival(room);
                    var ramp = RaidExtraction.LoadingRoute(room.Module);
                    var last = ramp.Knots.Last();
                    var anchor = last.Position;
                    var loadingFloor = room.Module.Floors.First(floor => Numerics.Vector3.Dot(floor.Pose.Up, last.Up) > .999f);
                    Vector2? map = null;
                    if (Planar != null && Planar.TryGetPose(room.Index, loadingFloor.Id, out var floor))
                    {
                        var local = anchor - loadingFloor.Pose.Position;
                        float x = Numerics.Vector3.Dot(local, loadingFloor.Pose.Forward);
                        float z = Numerics.Vector3.Dot(local, loadingFloor.Pose.Right);
                        float angle = floor.Angle * Mathf.Deg2Rad;
                        float sine = Mathf.Sin(angle);
                        float cosine = Mathf.Cos(angle);
                        map = floor.Position + new Vector2(x * cosine - z * sine, x * sine + z * cosine);
                    }
                    return (Entry: entry, Approach: RouteLength(ramp), World: room.Point(anchor), Map: map);
                }).ToArray();

            float pairCart = float.PositiveInfinity;
            float stockCart = float.PositiveInfinity;
            float world = float.PositiveInfinity;
            float mapDistance = float.PositiveInfinity;
            for (int i = 0; i < stations.Length; i++)
            {
                var station = stations[i];
                stockCart = Mathf.Min(stockCart, TreeDistance(station.Entry, Entrance, cartOnly: true) + station.Approach);
                for (int j = 0; j < i; j++)
                {
                    var other = stations[j];
                    pairCart = Mathf.Min(pairCart, TreeDistance(station.Entry, other.Entry, cartOnly: true) +
                        station.Approach + other.Approach);
                    world = Mathf.Min(world, Vector3.Distance(station.World, other.World));
                    if (station.Map is Vector2 map && other.Map is Vector2 otherMap)
                        mapDistance = Mathf.Min(mapDistance, Vector2.Distance(map, otherMap));
                }
            }
            return (pairCart, stockCart, world, mapDistance);
        }

        private static int CompareQuotaSpacing((float Cart, float Spatial) candidate, (float Cart, float Spatial) current)
        {
            if (candidate.Cart != current.Cart && !Mathf.Approximately(candidate.Cart, current.Cart))
                return candidate.Cart.CompareTo(current.Cart);
            return candidate.Spatial.CompareTo(current.Spatial);
        }

        private static float RouteLength(HaulRoute route) => route.Knots.Zip(route.Knots.Skip(1),
            (a, b) => Numerics.Vector3.Distance(a.Position, b.Position)).Sum();

        internal static HaulRoute FreightRoute(Room room, Dock from, Dock to)
        {
            var route = room.Module.Routes.FirstOrDefault(route => route.Cargo == Cargo.Cart && route.Flight == null &&
                ((route.From == from.Id && route.To == to.Id) || (route.From == to.Id && route.To == from.Id)));
            if (route != null || room.Module.Id != "upper-exit-corridor" ||
                !((from.Id == "lower-west" && to.Id == "upper") || (to.Id == "lower-west" && from.Id == "upper")))
                return route;

            // The upper route starts at the surveyed branching point of the lower cart lane.
            var lower = room.Module.Routes.Single(candidate => candidate.Id == "lower-cart-through");
            var upper = room.Module.Routes.Single(candidate => candidate.Id == "upper-cart-branch");
            var approach = lower.Knots.TakeWhile(knot => knot.Position != upper.Knots[0].Position);
            return new HaulRoute("lower-west-to-upper", lower.From, upper.To, Cargo.Cart, approach.Concat(upper.Knots).ToArray(),
                stepClearance: upper.StepClearance);
        }

        internal bool HasCartAccess(Room room, string floorId)
        {
            if (!TryCartArrival(room, out End arrival)) return false;
            return arrival.Dock.FloorId == floorId || room.Module.ExternalDocks.Any(dock =>
                dock.FloorId == floorId && dock.MaxCargo == Cargo.Cart && FreightRoute(room, arrival.Dock, dock) != null);
        }

        private bool TryCartArrival(Room room, out End arrival)
        {
            arrival = Arrival(room);
            while (room != Entrance.Room)
            {
                if (!inbound.TryGetValue(room, out var link) || !freight.Contains(link)) return false;
                room = link.From.Room;
                if (FreightRoute(room, Arrival(room).Dock, link.From.Dock) == null) return false;
            }
            return true;
        }

        private bool IsFreeCartExit(End end) => (end.Dock.Use & DockUse.Physical) != 0 &&
            (end.Dock.Use & DockUse.DoorPortal) == 0 && end.Dock.MaxCargo == Cargo.Cart &&
            !end.Room.OpenPlugs.Contains(end.Dock.PlugId) && TryCartArrival(end.Room, out End arrival) &&
            FreightRoute(end.Room, arrival.Dock, end.Dock) != null;

        private IEnumerable<End> Exits() =>
            rooms.SelectMany(room => room.Module.ExternalDocks.Select(dock => new End(room, dock))).Where(IsFreeCartExit);

        private void AttachFreightCluster(Room dispatch, bool receivingPortal)
        {
            var receiving = FreightServiceRoom.Create(FreightServiceRoom.SampleReceiving, receivingPortal);
            var storage = SampleStorage.Create(CompletedLevels >= 2 && Next(2) == 1);
            var dispatchEast = new End(dispatch, dispatch.Dock("east"));
            if (!TryAttach(new[] { dispatchEast }, receiving, "west", out _, placeNext: intake =>
                TryAttach(Exits().Where(end => end.Room == intake).OrderBy(end => end.Dock.Id != "east"),
                    storage, "main-entry", out _)))
                throw new PlacementException($"Freight network cannot fit dispatch, receiving and sample storage (seed {Seed}).");
        }

        private void AttachResearchCluster(int laboratories)
        {
            // Early wings can rotate the two research connections across the three cart docks.
            // Keep the observation spur available once the gallery unlocks.
            int shaftDock = laboratories == 0 ? Next(ResearchDocks.Length) : 0;
            int calibrationDock = (shaftDock + 1 + (laboratories == 0 ? Next(2) : 0)) % ResearchDocks.Length;
            var candidates = Exits().OrderBy(end => Next(int.MaxValue)).ToArray();
            var service = FreightServiceRoom.Create(FreightServiceRoom.InstrumentService, portal: false);
            var shaft = ShaftPlant.Create();
            var cupboard = StorageCupboard.Create();
            var cupboardEntry = cupboard.ExternalDocks.Single();
            var calibration = CalibrationSector.Create();
            bool broken = laboratories > 0 && CompletedLevels >= 5 && Next(2) == 1;
            var mainLab = laboratories > 0 ? ObservationLab.Create(broken) : null;
            var secondaryLab = laboratories == 2 ? ObservationLab.Create(!broken) : null;

            foreach (var (transit, exit) in ShaftTransits())
            {
                if (transit.Id == service.Id)
                {
                    foreach (End from in candidates)
                        if (TryAttach(new[] { from }, service, "west", out Room hub,
                            placeNext: room => PlaceShaft(room, from.Room)))
                        {
                            ShaftTransit = hub;
                            return;
                        }
                }
                else
                {
                    foreach (End from in candidates)
                        if (TryAttach(new[] { from }, transit, "west", out Room corridor,
                            placeNext: approach => TryAttach(new[] { new End(approach, approach.Dock(exit)) },
                                service, "west", out _,
                                placeNext: hub => PlaceShaft(hub, transit.Id == "junction-corridor" ? approach : from.Room))))
                        {
                            ShaftTransit = corridor;
                            return;
                        }
                }
            }
            throw new PlacementException($"Freight network cannot fit the shaft research cluster (seed {Seed}).");

            bool PlaceShaft(Room hub, Room observationApproach) =>
                TryAttach(HubExits(hub, ResearchDocks[shaftDock]), shaft, "freight-entry", out _, requireFold: true,
                    placeNext: plant =>
                    {
                        // Only hand cargo reaches the upper cupboard. The field core uses the freight entrance.
                        var upper = new End(plant, plant.Dock("upper-exit"));
                        var annex = Align(upper, cupboard, cupboardEntry, NextRoomIndex);
                        if (!Fits(annex)) return false;
                        CommitHand(upper, new End(annex, cupboardEntry));
                        if (TryAttach(HubExits(hub, ResearchDocks[calibrationDock]), calibration, "main-entry", out _,
                            placeNext: _ => PlaceObservation(hub, observationApproach)))
                            return true;
                        handPassages.RemoveAt(handPassages.Count - 1);
                        RemoveRoom(annex);
                        return false;
                    });

            bool PlaceObservation(Room hub, Room approach) => mainLab == null ||
                TryAttach(HubExits(hub, "spur"), mainLab, "freight-entry", out _, placeNext: _ =>
                    secondaryLab == null || TryAttach(Exits().Where(end => end.Room == approach),
                        secondaryLab, "freight-entry", out _));

            IEnumerable<End> HubExits(Room hub, string preferred) =>
                Exits().Where(end => end.Room == hub).OrderBy(end => end.Dock.Id != preferred);
        }

        private int Instances(GrayModule module) => rooms.Count(room => room.Module.Id == module.Id);

        private static bool IsStaffed(GrayModule module) =>
            module.Id is FreightServiceRoom.InstrumentService or FreightServiceRoom.SampleReceiving;

        private static IEnumerable<GrayModule> StaffedRooms(bool portal, bool instrumentFirst)
        {
            string first = instrumentFirst ? FreightServiceRoom.InstrumentService : FreightServiceRoom.SampleReceiving;
            string second = instrumentFirst ? FreightServiceRoom.SampleReceiving : FreightServiceRoom.InstrumentService;
            yield return FreightServiceRoom.Create(first, portal);
            yield return FreightServiceRoom.Create(second, portal);
        }

        private IEnumerable<(GrayModule Module, string Exit)> CorridorTransits()
        {
            yield return (StraightCorridor.Create(), "east");
            if (CompletedLevels < 2) yield break;
            yield return (StraightCorridor.Create(longForm: true), "east");
            yield return (ElbowCorridor.Create(), "north");
            if (CompletedLevels >= 5) yield return (JunctionCorridor.Create(), "east");
        }

        private IEnumerable<(GrayModule Module, string Exit)> ShaftTransits()
        {
            if (diversity is DiversityScope.ShaftAndBranches or DiversityScope.EntireLayout)
            {
                var choices = CorridorTransits().Append((
                    Module: FreightServiceRoom.Create(FreightServiceRoom.InstrumentService, portal: false), Exit: "east"));
                var ranked = choices.OrderBy(choice => Instances(choice.Module));
                if (CatalogBranchTarget(CompletedLevels) == 0)
                    ranked = ranked.ThenBy(_ => Next(int.MaxValue));
                foreach (var choice in ranked)
                    yield return choice;
                yield break;
            }

            if (CompletedLevels >= 2 && Attempt < CatalogTransitLayouts)
            {
                uint variation = unchecked((uint)Seed);
                switch (variation % 8u)
                {
                    case 4: yield return (StraightCorridor.Create(), "east"); break;
                    case 5: yield return (StraightCorridor.Create(longForm: true), "east"); break;
                    case 6: yield return (ElbowCorridor.Create(), "north"); break;
                    case 7 when CompletedLevels >= 5: yield return (JunctionCorridor.Create(), "east"); break;
                }
            }
            yield return (FreightServiceRoom.Create(FreightServiceRoom.InstrumentService, portal: false), "east");
        }
        private bool TryAttachBranchRoom(End exit, out Room room)
        {
            if (diversity == DiversityScope.None)
                return TryAttach(new[] { exit }, FreightServiceRoom.Create(FreightServiceRoom.InstrumentService, portal: false),
                    "west", out room);
            var choices = CorridorTransits().Select(transit => transit.Module)
                .Concat(StaffedRooms(portal: false, instrumentFirst: true))
                .OrderBy(Instances);
            foreach (GrayModule module in choices)
                if (TryAttach(new[] { exit }, module, "west", out room)) return true;
            room = null;
            return false;
        }
        private void AddCatalogBranches()
        {
            int eligible = CatalogBranches.Count(branch => branch.Unlock <= CompletedLevels);
            if (eligible == 0) return;
            int target = CatalogBranchTarget(CompletedLevels);
            int first = (int)(unchecked((uint)(Seed ^ CompletedLevels)) % (uint)eligible);
            var choices = Enumerable.Range(0, eligible).Where(index => index != first)
                .OrderBy(_ => Next(int.MaxValue)).Prepend(first);
            foreach (int choice in choices)
            {
                var descriptor = CatalogBranches[choice];
                GrayModule module = descriptor.Module();
                if (module.InternalPortals.Count > MaximumPortalPairs - RequiredPortalPairs) continue;
                if (module.InternalPortals.Count != 0 && canPrepare != null &&
                    !canPrepare(new Room(module, planningSpace, planningSpace, NextRoomIndex, draft: true))) continue;
                Dock entry = module.ExternalDocks.Single(dock => dock.Id == descriptor.Entry);
                // Recompute free docks after each placement, so new catalog exits can lead into another branch.
                var candidates = Exits().Where(end => end.Room != Entrance.Room).OrderBy(end => Next(int.MaxValue)).ToArray();
                int rootIndex = NextRoomIndex;
                bool placed = SphericalChamber.IsVariant(module.Id) ? TryPlaceSphereTransit(candidates, module) : entry.MaxCargo == Cargo.Hand
                    ? TryPlaceHandTransit(candidates, module, entry, descriptor.Exits)
                    : TryPlaceCartBranch(candidates, module, entry, descriptor.Exits);
                if (!placed) continue;
                catalogBranchRoots.Add(rootIndex);
                if (++catalogBranchesPlaced == target) break;
            }
        }

        private bool TryPlaceCartBranch(IEnumerable<End> candidates, GrayModule module, Dock entry, string[] continuations)
        {
            foreach (End from in candidates)
            foreach (var placement in Attachments(from, module, entry, false, false))
            {
                Room transit = Commit(from, new End(placement.Target, entry), placement.Fold);
                if (continuations.Length == 0) return true;

                var destinations = new List<Room>(continuations.Length);
                foreach (string continuation in continuations)
                {
                    var exit = new End(transit, transit.Dock(continuation));
                    if (!TryAttachBranchRoom(exit, out Room room)) break;
                    destinations.Add(room);
                }
                if (destinations.Count == continuations.Length) return true;
                for (int i = destinations.Count - 1; i >= 0; i--) Undo(destinations[i]);
                Undo(transit);
            }
            return false;
        }

        private bool TryPlaceSphereTransit(End[] candidates, GrayModule fullSphere)
        {
            var annex = StraightCorridor.Create();
            foreach (int branches in SphereBranchChoices.Skip(Array.IndexOf(SphereBranchChoices, sphereBranches)))
            {
                int mask = SphericalChamber.WestMask | branches;
                var sealedEast = SphericalChamber.Create(mask);
                if (!Prepared(sealedEast)) continue;
                // The east opening stays eligible for shortcut selection only when
                // both outcomes have a prepared room for this physical topology.
                var openEast = (mask | SphericalChamber.EastMask) == SphericalChamber.FullMask ? fullSphere :
                    SphericalChamber.Create(mask | SphericalChamber.EastMask);
                if (Prepared(openEast) && Place(openEast)) return true;
                if (Place(sealedEast)) return true;
            }
            return false;

            bool Prepared(GrayModule module) => canPrepare == null ||
                canPrepare(new Room(module, planningSpace, planningSpace, NextRoomIndex, draft: true));

            bool Place(GrayModule module)
            {
                var docks = module.ExternalDocks;
                return TryPlaceHandTransit(candidates, module, docks.Single(dock => dock.Id == "west"),
                    docks.Where(dock => dock.Id != "west" && (dock.Use & DockUse.DoorPortal) == 0).ToArray(),
                    annex, "west");
            }
        }

        private bool TryPlaceHandTransit(IEnumerable<End> candidates, GrayModule module, Dock entry, string[] continuations) =>
            TryPlaceHandTransit(candidates, module, entry, continuations.Select(id =>
                module.ExternalDocks.Single(dock => dock.Id == id))
                .Where(dock => (dock.Use & DockUse.DoorPortal) == 0).ToArray(), StorageCupboard.Create(), "service-entry");

        private bool TryPlaceHandTransit(IEnumerable<End> candidates, GrayModule module, Dock entry, Dock[] physicalDocks,
            GrayModule annex, string annexDoor)
        {
            Dock annexEntry = annex.ExternalDocks.Single(dock => dock.Id == annexDoor);
            foreach (End from in candidates)
            {
                if (SamePlaneNeighbors(from) != 0 || !HandCompatible(from.Dock, entry)) continue;
                Room transit = Align(from, module, entry, NextRoomIndex);
                if (!Fits(transit)) continue;
                var physical = new List<(End Exit, End Arrival)>(physicalDocks.Length);
                foreach (Dock dock in physicalDocks)
                {
                    var exit = new End(transit, dock);
                    if (!HandCompatible(exit.Dock, annexEntry)) break;
                    Room destination = Align(exit, annex, annexEntry, NextRoomIndex + 1 + physical.Count);
                    if (!Fits(destination) || Intersects(transit, destination) ||
                        physical.Any(link => Intersects(link.Arrival.Room, destination))) break;
                    physical.Add((exit, new End(destination, annexEntry)));
                }
                if (physical.Count != physicalDocks.Length) continue;
                CommitHand(from, new End(transit, entry));
                foreach (var (exit, arrival) in physical) CommitHand(exit, arrival);
                return true;
            }
            return false;
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
            bool initialUp = false, bool longLink = false, bool requireFold = false, Func<Room, bool> placeNext = null)
        {
            Dock entrance = module.ExternalDocks.Single(candidate => candidate.Id == entry);
            foreach (End from in candidates)
            foreach (var placement in Attachments(from, module, entrance, initialUp, longLink))
            {
                if (requireFold && placement.Fold == null) continue;
                result = Commit(from, new End(placement.Target, entrance), placement.Fold);
                if (placeNext == null || placeNext(result)) return true;
                Undo(result);
            }
            result = null;
            return false;
        }

        private IEnumerable<(Room Target, Room Fold)> Attachments(End from, GrayModule module, Dock entrance,
            bool initialUp, bool longLink, int? roomIndex = null)
        {
            if (!IsFreeCartExit(from) || from.Dock.Opening != entrance.Opening ||
                (entrance.Use & DockUse.Physical) == 0 || entrance.MaxCargo != Cargo.Cart) yield break;
            if (!longLink && SamePlaneNeighbors(from) == 0)
            {
                Room flat = Align(from, module, entrance, roomIndex ?? NextRoomIndex);
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
                Room turned = Align(exit, module, entrance, roomIndex ?? NextRoomIndex);
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
                if (Attempt < CatalogTransitLayouts) yield return RaidFreightFold.LongDescent(1.5f);
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

        internal static bool HandCompatible(Dock from, Dock to) => (from.Use & to.Use & DockUse.Physical) != 0 &&
            (from.MaxCargo == Cargo.Hand || to.MaxCargo == Cargo.Hand) &&
            MathF.Min(from.Opening.X, to.Opening.X) >= GrayBuilder.Carry && MathF.Min(from.Opening.Y, to.Opening.Y) >= 2.5f;

        private void CommitHand(End from, End to)
        {
            if (!HandCompatible(from.Dock, to.Dock))
                throw new InvalidOperationException("The physical connection needs consistent hand-cargo clearance.");
            from.Room.OpenPlugs.Add(from.Dock.PlugId);
            to.Room.OpenPlugs.Add(to.Dock.PlugId);
            handPassages.Add((from, to));
            inbound.Add(to.Room, new FreightLink(from, to, null));
            rooms.Add(to.Room);
        }

        private void Undo(Room room)
        {
            freight.RemoveAt(freight.Count - 1);
            RemoveRoom(room);
        }

        private void RemoveRoom(Room room)
        {
            var link = inbound[room];
            link.From.Room.OpenPlugs.Remove(link.From.Dock.PlugId);
            inbound.Remove(room);
            rooms.RemoveAt(rooms.Count - 1);
        }

        private Room Align(End parent, GrayModule module, Dock entry, int index)
        {
            Quaternion rotation = Quaternion.LookRotation(-parent.Outward, parent.Up) *
                Quaternion.Inverse(Quaternion.LookRotation(Vector(entry.Threshold.Forward), Vector(entry.Threshold.Up)));
            return new Room(module, new Pose(parent.Sill - rotation * Vector(entry.Threshold.Position), rotation),
                planningSpace, index, drafting);
        }

        private bool Fits(Room room) => drafting || !Overlaps(room.Volume, TruckReservation) &&
            rooms.All(other => !Intersects(room, other)) &&
            (RingRoom == null || !Intersects(room, RingRoom)) &&
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

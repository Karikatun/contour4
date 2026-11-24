using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Contour4.Procedural.Catalog;
using static Contour4.Procedural.Catalog.GrayBuilder;

namespace Contour4.Procedural.Catalog.Checks
{
    internal static class CatalogChecks
    {
        private static int Main(string[] args)
        {
            try
            {
                Expect(args.Length <= 1, "Expected at most one module identifier.");
                int checkedModules = 0;
                foreach (GrayModule module in ModuleCatalog.Rooms())
                {
                    if (args.Length == 1 && args[0] != module.Id) continue;
                    Verify(module);
                    checkedModules++;
                }
                Expect(checkedModules > 0, "No catalog modules selected.");
                Console.WriteLine($"CATALOG GEOMETRY PASS ({checkedModules} modules)");
                return 0;
            }
            catch (InvalidOperationException error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }
        }

        private static void Verify(GrayModule module)
        {
            // Non-convex sheets are probed with the prepared prefab's Unity colliders.
            var geometry = module.Geometry.Where(b => b.Role != BlockRole.Plug && b.Convex).Select(b => new Hull(b)).ToArray();
            var removedPlugs = module.ExternalDocks.Where(IsPhysical).Select(d => d.PlugId).ToHashSet();
            var connected = geometry.Concat(module.Geometry.Where(b => b.Role == BlockRole.Plug && !removedPlugs.Contains(b.Id))
                .Select(b => new Hull(b))).Concat(module.ExternalDocks.Where(IsPhysical).SelectMany(Fixture)
                .Select(b => new Hull(b))).ToArray();
            var all = module.Geometry.Concat(module.Approaches).ToArray();
            Expect(all.Select(b => b.Id).Distinct().Count() == all.Length, $"{module.Id}: duplicated geometry ID");
            Expect(all.All(b => b.Vertices.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z))),
                $"{module.Id}: non-finite geometry");
            Expect(module.Floors.All(f => MathF.Abs(Solid.SignedArea(f.MapContour)) > .1f),
                $"{module.Id}: degenerate floor contour");
            foreach (var (a, b) in module.InternalPortals)
            {
                Dock first = module.Docks.Single(d => d.Id == a);
                Dock second = module.Docks.Single(d => d.Id == b);
                Expect(first.Use == second.Use && first.Opening == second.Opening && first.FloorId != second.FloorId &&
                    first.MaxCargo == second.MaxCargo && !IsPhysical(first) && !IsPhysical(second),
                    $"{module.Id}: incompatible internal portal {a}/{b}");
            }
            foreach (Dock dock in module.Docks)
                VerifyDock(module, dock, geometry);
            foreach (HaulRoute route in module.Routes)
            {
                Expect(HasEndpoint(module, route.From, route.Cargo) && HasEndpoint(module, route.To, route.Cargo),
                    $"{module.Id}/{route.Id}: endpoint missing or incapable of the declared cargo");
                // PreparedModuleVerification checks these paths against the curved PhysX mesh.
                if ((module.Id != MobiusHall.Id || route.Id != MobiusHall.CeilingRoute) && (!SphericalChamber.IsVariant(module.Id) || route.Id is "diameter-through" or "diameter-terminal"))
                    VerifyRoute(module.Id, route, connected);
            }
            VerifyModuleBounds(module);
            if (module.Id == "calibration-sector")
                Expect(module.Routes.Count == 3 &&
                    module.Docks.Any(d => (d.Use & DockUse.DoorPortal) != 0),
                    "Calibration sector catalogue dimensions/dock changed.");
            if (module.Id == "sample-storage" || module.Id == "sample-storage-narrow")
            {
                Expect(module.Routes.Count == 4 &&
                    module.Docks.Any(d => d.Use == DockUse.UpperPortal), "Sample storage catalogue mismatch.");
                var cart = new Hull(Block.Box("cart-in-shortcut", BlockRole.Free, Pose(8.5f, 0, 0),
                    -1.3f, 1.3f, .03f, 2.08f, -.9f, .9f));
                bool blocked = geometry.Any(b => Intersects(cart, b));
                Expect(blocked == (module.Difficulty == 1), "Storage shortcut width does not match its difficulty.");
            }
            if (module.Id == "observation-lab" || module.Id == "observation-lab-broken-floor")
            {
                Expect(module.Floors.All(f => f.Pose.Up == Vector3.UnitY) &&
                    MathF.Abs(module.Floors.Single(f => f.Id == "gallery").Pose.Position.Y - 3.6f) < .001f,
                    "Observation lab local gravity or gallery elevation changed.");
                Expect(module.Geometry.Count(b => b.Id.StartsWith("galley-stair-", StringComparison.Ordinal)) == 20,
                    "Observation lab missing physical stair treads.");
                Vector3 from = module.Landmarks["gallery-view"] + Vector3.UnitY * 1.62f;
                Vector3 to = new Vector3(7.5f, 1.36f, 0);
                for (int i = 0; i <= 120; i++)
                {
                    Vector3 p = Vector3.Lerp(from, to, i / 120f);
                    var probe = new Hull(Block.Box("view-ray", BlockRole.Free, Pose(p.X, p.Y, p.Z),
                        -.005f, .005f, -.005f, .005f, -.005f, .005f));
                    Expect(!geometry.Any(b => Intersects(probe, b)), "Observation lab no longer has a view over the stand.");
                }
                if (module.Difficulty == 2)
                {
                    Vector3 pit = new Vector3(10.275f, .1f, 0);
                    Expect(!geometry.Any(b => b.Block.Role == BlockRole.Floor && b.Supports(pit, Vector3.UnitY, .5f)) &&
                        geometry.Any(b => b.Block.Id == "shaft-bottom" && b.Supports(pit, Vector3.UnitY, 3)),
                        "Broken lab must have an actual hole with a shaft below it.");
                }
            }
            if (module.Id == "upper-exit-corridor")
            {
                Dock west = module.Docks.Single(d => d.Id == "lower-west");
                Dock east = module.Docks.Single(d => d.Id == "lower-east");
                Dock upper = module.Docks.Single(d => d.Id == "upper");
                Expect(module.Docks.Count == 3 && module.Docks.All(d => d.MaxCargo == Cargo.Cart && IsPhysical(d) &&
                    d.Opening == new Vector2(4.5f, 4)), "Upper corridor dock contracts changed.");
                Expect(west.Threshold.Up == east.Threshold.Up && west.Threshold.Forward == -east.Threshold.Forward &&
                    upper.Threshold.Up == -Vector3.UnitX && upper.Threshold.Forward == Vector3.UnitY &&
                    upper.Threshold.Position.Y == 10.5f, "Upper corridor exit is not the end of the quarter-turn bridge.");
                Expect(module.Routes.Count == 2 && module.Routes[0].Envelope == module.Routes[1].Envelope,
                    "Upper corridor routes use different loads.");
                VerifyUpperBranchReservation(module, geometry);
            }
            if (module.Id == MaintenanceTrench.Id)
            {
                var board = module.Geometry.Single(b => b.Id == MaintenanceTrench.Bridge);
                var (minimum, maximum) = board.Bounds;
                Expect(MathF.Abs(maximum.Z - minimum.Z - 1f) < .001f &&
                    maximum.Z - minimum.Z < CornerSlice.CartEnvelope.Z && maximum.Y == 0,
                    "Maintenance plank must be flush with the deck and narrower than the loaded cart.");
                var (fallMin, fallMax) = module.FallVolumes.Single().Bounds;
                Expect(fallMax.Y < -4 && fallMin.Y < fallMax.Y && fallMin.Z < minimum.Z && fallMax.Z > maximum.Z,
                    "Both sides of the maintenance plank must be above the same deep fall volume.");
                foreach (float z in new[] { -6.1f, -3.45f })
                    Expect(geometry.All(block => block.Block.Role != BlockRole.Floor ||
                        !block.Supports(new Vector3(8, .12f, z), Vector3.UnitY, 4.5f)), "Unexpected support in the maintenance pit.");
                Expect(module.Routes.Any(route => route.Id == "freight-bypass" && route.Cargo == Cargo.Cart) &&
                    module.Routes.Any(route => route.Id == "inspection-shortcut" && route.Cargo == Cargo.Hand &&
                        route.Condition == "Shutter open"), "Maintenance route cargo/condition contract changed.");
            }
            if (module.Id == "shaft-plant")
                Expect(module.Floors.All(f => f.Pose.Up == Vector3.UnitY) &&
                    module.Docks.Single(d => d.Id == "upper-exit").Threshold.Position.Y == 7.2f &&
                    module.Geometry.Count(b => b.Id.Contains("-stair-")) == 40, "Shaft plant catalogue mismatch.");
            if (module.Id == "spiral-corridor")
            {
                Dock[] ends = module.Docks.ToArray();
                Expect(ends.Length == 2 && ends[0].Threshold.Forward == -ends[1].Threshold.Forward &&
                    ends[0].Threshold.Up == -ends[1].Threshold.Up && module.Routes.Single().Cargo == Cargo.Hand,
                    "Spiral must connect matching axial exits with opposite floor normals.");
                Expect(Vector3.Distance(SpiralCorridor.FrameAt(SpiralCorridor.Length / 2).Up, Vector3.UnitZ) < .001f &&
                    SpiralCorridor.FrameAt(2.999f).Up == SpiralCorridor.FrameAt(3).Up &&
                    SpiralCorridor.FrameAt(24).Up == SpiralCorridor.FrameAt(24.001f).Up,
                    "Spiral gravity profile is discontinuous at a straight lead or misses the sidewall at halfway.");
            }
            if (module.Id == "top-to-top-chambers" || module.Id == "floor-to-floor-chambers")
            {
                var (a, b) = module.InternalPortals.Single();
                var hatches = module.Docks.Where(d => d.Id == a || d.Id == b).ToArray();
                Expect(module.Floors[0].Pose.Up == -module.Floors[1].Pose.Up &&
                    hatches[0].PlugId == hatches[1].PlugId && hatches[0].Threshold.Forward == -hatches[1].Threshold.Forward &&
                    Vector3.Distance(hatches[0].Threshold.Position, hatches[1].Threshold.Position) == .25f &&
                    module.Geometry.Count(p => p.Id.StartsWith("common-plate-")) == 4,
                    "Opposed chambers must meet at one common 0.25m face with opposed aperture frames.");
                VerifyParts(module.Id, geometry, "a-", "b-");
            }
            if (module.Id == "right-angle-chambers")
            {
                VerifyParts(module.Id, geometry, "airlock-", "distribution-hall-");
                Expect(module.Floors[0].Pose.Up == Vector3.UnitY && module.Floors[1].Pose.Up == Vector3.UnitX &&
                    module.Geometry.Count(b => b.Id == "common-edge") == 1,
                    "Right-angle group lost the source corner and its one common edge.");
            }
            if (module.Id == "gallery-chamber")
                VerifyParts(module.Id, geometry, "lab-", "small-");
            if (module.Id == "niche-chamber")
                VerifyParts(module.Id, geometry, "cell-");
            if (module.LayoutReservation is Block reserve)
            {
                var (minimum, maximum) = reserve.Bounds;
                Expect(reserve.Role == BlockRole.Free && module.Geometry.Concat(module.Approaches)
                    .SelectMany(block => block.Vertices).All(v => Vector3.Distance(v, Vector3.Clamp(v, minimum, maximum)) < .001f),
                    $"{module.Id}: geometry or an approach extends outside its placement reservation.");
            }
            Console.WriteLine($"{module.Id}: plan={module.Plan.X:F2}m × {module.Plan.Y:F2}m; " +
                $"bounds (x/y/z)={module.Size}; floors={module.Floors.Count}; docks={module.Docks.Count}");
        }

        private static void VerifyUpperBranchReservation(GrayModule module, IReadOnlyList<Hull> geometry)
        {
            Block Reserve(string id) => module.Approaches.SingleOrDefault(b => b.Id == id && b.Role == BlockRole.Free) ??
                throw new InvalidOperationException($"Upper corridor is missing the {id} reservation.");
            Block lower = Reserve("lower-free-lane");
            Block branch = Reserve("branch-approach-free");
            Block throat = Reserve("branch-throat-free");
            Block bridge = Reserve("turn-01-free");
            Expect(branch.Bounds == (new Vector3(4.25f, 0, -9), new Vector3(13.75f, 4, -2.25f)) &&
                throat.Bounds == (new Vector3(13.75f, 0, -9), new Vector3(14, 4, -4.5f)),
                "Upper corridor's reserved branch is outside its wall and jamb clearance.");
            Expect(branch.Bounds.Max.Z == lower.Bounds.Min.Z && branch.Bounds.Max.X == throat.Bounds.Min.X &&
                bridge.Vertices.Take(4).All(v => v.X == throat.Bounds.Max.X &&
                    v.Y >= throat.Bounds.Min.Y && v.Y <= throat.Bounds.Max.Y &&
                    v.Z >= throat.Bounds.Min.Z && v.Z <= throat.Bounds.Max.Z),
                "Upper corridor's reserved branch must meet the lower lane and the first bridge span.");
            foreach (Block part in new[] { lower, branch, throat })
            {
                var reserve = new Hull(part);
                foreach (Hull hull in geometry)
                    Expect(!Intersects(reserve, hull), $"Upper corridor's {part.Id} is blocked by {hull.Block.Id}.");
            }
            Console.WriteLine("  upper-exit-corridor: lower lane and widened branch reserves are open and meet the bridge.");
        }

        private static void VerifyModuleBounds(GrayModule module)
        {
            float hallLength = 2 * (MobiusHall.HalfInteriorX + Skirt);
            float hallWidth = 2 * (MobiusHall.HalfInteriorZ + Skirt);
            var (plan, size, floors, docks, external) = module.Id switch
            {
                "service-cupboard" => (new Vector2(7.5f, 7.5f), new Vector3(7.5f, 4.5f, 7.5f), 1, 1, 1),
                "calibration-sector" => (new Vector2(15, 22.5f), new Vector3(22.5f, 4.5f, 15), 1, 2, 2),
                "sample-storage" or "sample-storage-narrow" => (new Vector2(15, 15), new Vector3(15, 4.5f, 15), 1, 3, 3),
                "straight-corridor-short" => (new Vector2(5.5f, 7.5f), new Vector3(7.5f, 4.5f, 5.5f), 1, 2, 2),
                "straight-corridor-long" => (new Vector2(5.5f, 15), new Vector3(15, 4.5f, 5.5f), 1, 2, 2),
                "elbow-corridor" => (new Vector2(13.25f, 9.25f), new Vector3(9.25f, 4.5f, 13.25f), 1, 2, 2),
                "junction-corridor" => (new Vector2(14.5f, 15), new Vector3(15, 4.5f, 14.5f), 1, 3, 3),
                "observation-lab" => (new Vector2(15, 15), new Vector3(15, 7.95f, 15), 2, 3, 3),
                "upper-exit-corridor" => (new Vector2(13, 24), new Vector3(24, 10.75f, 13), 3, 3, 3),
                "shaft-plant" => (new Vector2(15, 22.5f), new Vector3(22.5f, 11.7f, 15), 3, 2, 2),
                "observation-lab-broken-floor" => (new Vector2(15, 15), new Vector3(15, 10.2f, 15), 2, 3, 3),
                "quarter-turn-bridge" => (new Vector2(5, 15.42478f), new Vector3(13.25f, 13.25f, 5), 1, 2, 2),
                "spiral-corridor" => (new Vector2(5, 27), new Vector3(27, 7.58217f, 6.78610f), 1, 2, 2),
                "top-to-top-chambers" or "floor-to-floor-chambers" =>
                    (new Vector2(7.5f, 7.5f), new Vector3(7.5f, 9.25f, 7.5f), 2, 4, 2),
                "right-angle-chambers" => (new Vector2(15, 15), new Vector3(19.25f, 19.25f, 15), 2, 4, 2),
                "u-shaft" => (new Vector2(7.5f, 26), new Vector3(26.5f, 16.25f, 7.5f), 5, 5, 3),
                "gallery-chamber" => (new Vector2(19.5f, 15), new Vector3(15, 11.35f, 19.5f), 3, 5, 3),
                "niche-chamber" => (new Vector2(15, 22.5f), new Vector3(22.5f, 7.75f, 15), 2, 5, 3),
                MaintenanceTrench.Id => (new Vector2(14, 18), new Vector3(18, 13.25f, 14), 2, 2, 2),
                _ when SphericalChamber.IsVariant(module.Id) => SphereLimits(SphericalChamber.GetMask(module.Id)),
                MobiusHall.Id => (new Vector2(hallWidth, hallLength), new Vector3(hallLength, 12.54f, hallWidth), 2, 3, 3),
                _ => throw new InvalidOperationException($"No measured limits declared for {module.Id}."),
            };
            Expect(Vector2.Distance(module.Plan, plan) < .002f && Vector3.Distance(module.Size, size) < .002f &&
                module.Floors.Count == floors && module.Docks.Count == docks && module.ExternalDocks.Count() == external,
                $"{module.Id}: plan, 3D dimensions, floors or dock counts changed.");
        }

        private static (Vector2, Vector3, int, int, int) SphereLimits(int mask)
        {
            const float radius = SphericalChamber.Radius;
            const float length = SphericalChamber.HalfReservation;
            bool east = (mask & SphericalChamber.EastMask) != 0;
            bool upper = (mask & SphericalChamber.UpperMask) != 0;
            bool lower = (mask & SphericalChamber.LowerMask) != 0;
            int floors = 1 + (upper ? 1 : 0) + (lower ? 1 : 0);
            int docks = floors + (east ? 1 : 0);
            return (new Vector2(2 * length, 2 * length), new Vector3(length + (east ? length : radius),
                (upper ? length : radius) + (lower ? length : radius), 2 * radius), floors, docks, docks);
        }

        private static void VerifyParts(string module, IReadOnlyList<Hull> hulls, string first, string second = null)
        {
            bool IsFirst(Hull hull) => hull.Block.Id.StartsWith(first, StringComparison.Ordinal);
            Hull[] partB = hulls.Where(b => second == null ? !IsFirst(b) :
                b.Block.Id.StartsWith(second, StringComparison.Ordinal)).ToArray();
            foreach (Hull a in hulls.Where(IsFirst))
            foreach (Hull b in partB)
                Expect(!Intersects(a, b), $"{module}: {a.Block.Id} and {b.Block.Id} overlap across parts.");
        }

        private static bool HasEndpoint(GrayModule module, string id, Cargo cargo) =>
            module.Landmarks.ContainsKey(id) || module.Docks.Any(d => d.Id == id && d.MaxCargo >= cargo);

        internal static bool IsPhysical(Dock dock) => (dock.Use & DockUse.Physical) != 0;

        // Isolated test chamber outside the actual bind plane. Its short length keeps fixtures out of module layouts.
        internal static IEnumerable<Block> Fixture(Dock dock)
        {
            const float length = 3.4f;
            float half = dock.Opening.X / 2;
            yield return Block.Box($"{dock.Id}-test-floor", BlockRole.Floor, dock.Threshold, 0, length,
                -Skirt, 0, -half, half);
            yield return Block.Box($"{dock.Id}-test-left", BlockRole.Shell, dock.Threshold, 0, length,
                0, dock.Opening.Y, -half - Skirt, -half);
            yield return Block.Box($"{dock.Id}-test-right", BlockRole.Shell, dock.Threshold, 0, length,
                0, dock.Opening.Y, half, half + Skirt);
            yield return Block.Box($"{dock.Id}-test-ceiling", BlockRole.Shell, dock.Threshold, 0, length,
                dock.Opening.Y, dock.Opening.Y + Skirt, -half, half);
        }

        private static void VerifyDock(GrayModule module, Dock dock, IReadOnlyList<Hull> geometry)
        {
            string id = $"{module.Id}/{dock.Id}";
            Expect(module.Floors.Any(f => f.Id == dock.FloorId), $"{id}: bad floor binding");
            Expect(Unit(dock.Threshold), $"{id}: bad dock frame");
            var approach = new Hull(module.Approaches.Single(b => b.Id == dock.Id + "-approach"));
            var plug = new Hull(module.Geometry.Single(b => b.Id == dock.PlugId));
            Expect(Intersects(approach, plug), $"{id}: plug misses opening");
            foreach (Hull hull in geometry)
                Expect(!Intersects(approach, hull), $"{id}: approach meets {hull.Block.Id}");
            if (IsPhysical(dock))
            {
                ModulePose t = dock.Threshold;
                var facing = new Dock("fixture", "fixture-floor", new ModulePose(t.Position, -t.Forward, t.Up),
                    DockUse.Physical, dock.Opening, Vector2.Zero, dock.MaxCargo);
                Expect(dock.FitsPhysical(facing) && !dock.FitsPhysical(dock), $"{id}: faulty matching rule");
                Expect(Vector3.Distance(dock.ContactStart, facing.ContactEnd) < .001f &&
                    Vector3.Distance(dock.ContactEnd, facing.ContactStart) < .001f, $"{id}: contact misses mate");
                Hull[] mate = Fixture(dock).Select(b => new Hull(b)).ToArray();
                foreach (Hull outside in mate)
                foreach (Hull inside in geometry)
                    Expect(!Intersects(outside, inside), $"{id}: fixture overlaps {inside.Block.Id}");
                var crossing = new HaulRoute("threshold", dock.Id, "fixture", dock.MaxCargo, new[]
                {
                    new ModulePose(t.Point(-1.6f, 0), t.Forward, t.Up),
                    new ModulePose(t.Point(1.8f, 0), t.Forward, t.Up),
                });
                VerifyRoute(id, crossing, geometry.Concat(mate).ToArray());
            }
            Console.WriteLine($"  {dock.Id}: {dock.Use}; {dock.Opening.X:F2}m × {dock.Opening.Y:F2}m; " +
                $"cargo={dock.MaxCargo}; pos={dock.Threshold.Position}; forward={dock.Threshold.Forward}; up={dock.Threshold.Up}");
        }

        private static void VerifyRoute(string scope, HaulRoute route, IReadOnlyList<Hull> hulls)
        {
            int poses = 0;
            int supportPoses = 0;
            float length = 0;
            Vector3 last = route.Knots[0].Position;
            Vector3 extent = route.Envelope / 2;
            foreach (ModulePose pose in route.Samples(.065f))
            {
                length += Vector3.Distance(last, pose.Position);
                last = pose.Position;
                Expect(Unit(pose), $"{scope}/{route.Id}: invalid frame");
                var body = new Hull(Block.Box("load", BlockRole.Free, pose,
                    -extent.X, extent.X, route.StepClearance, route.StepClearance + route.Envelope.Y, -extent.Z, extent.Z));
                foreach (Hull hull in hulls)
                    Expect(!Intersects(body, hull), $"{scope}/{route.Id}: body hits {hull.Block.Id} at {pose.Position}");

                float[] longitudinal = route.Cargo == Cargo.Cart ? new[] { -.85f, .85f } : new[] { 0f };
                float[] lateral = route.Cargo == Cargo.Cart ? new[] { -.65f, .65f } : new[] { 0f };
                bool onDeck = route.NeedsSupport(pose);
                if (onDeck) supportPoses++;
                foreach (float x in onDeck ? longitudinal : Array.Empty<float>())
                foreach (float z in lateral)
                {
                    Vector3 origin = pose.Point(x, route.StepClearance, z);
                    Expect(hulls.Any(h => h.Block.Role == BlockRole.Floor && h.Supports(origin, pose.Up,
                            route.StepClearance + (route.Cargo == Cargo.Cart ? .11f : .26f))),
                        $"{scope}/{route.Id}: no support at {pose.Position}, offset {x:F2}/{z:F2}");
                }
                poses++;
            }
            Console.WriteLine($"  {scope}/{route.Id}: {route.Cargo} {length:F2}m; " +
                $"{poses} poses clear, {supportPoses} checked for support");
        }

        private static bool Unit(ModulePose pose) => MathF.Abs(pose.Forward.LengthSquared() - 1) < .001f &&
            MathF.Abs(pose.Up.LengthSquared() - 1) < .001f && MathF.Abs(Vector3.Dot(pose.Forward, pose.Up)) < .001f;

        private static bool Intersects(Hull a, Hull b)
        {
            const float epsilon = .001f;
            if (a.Max.X <= b.Min.X + epsilon || a.Min.X >= b.Max.X - epsilon ||
                a.Max.Y <= b.Min.Y + epsilon || a.Min.Y >= b.Max.Y - epsilon ||
                a.Max.Z <= b.Min.Z + epsilon || a.Min.Z >= b.Max.Z - epsilon) return false;

            bool Separates(Vector3 axis)
            {
                if (axis.LengthSquared() < .0000001f) return false;
                axis = Vector3.Normalize(axis);
                (float minA, float maxA) = a.Projection(axis);
                (float minB, float maxB) = b.Projection(axis);
                return maxA <= minB + epsilon || maxB <= minA + epsilon;
            }
            if (a.Normals.Any(Separates) || b.Normals.Any(Separates)) return false;
            foreach (Vector3 edge in a.Edges)
            foreach (Vector3 other in b.Edges)
                if (Separates(Vector3.Cross(edge, other))) return false;
            return true;
        }

        private sealed class Hull
        {
            public readonly Block Block;
            public readonly Vector3 Min;
            public readonly Vector3 Max;
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector3> Edges = new List<Vector3>();

            public Hull(Block block)
            {
                Block = block;
                (Min, Max) = block.Bounds;
                var keys = new HashSet<(int, int)>();
                foreach (var (a, b, c) in Faces())
                {
                    Normals.Add(Vector3.Normalize(Vector3.Cross(block.Vertices[b] - block.Vertices[a],
                        block.Vertices[c] - block.Vertices[a])));
                    AddEdge(a, b); AddEdge(b, c); AddEdge(c, a);
                }
                void AddEdge(int from, int to)
                {
                    if (keys.Add((Math.Min(from, to), Math.Max(from, to))))
                        Edges.Add(Vector3.Normalize(block.Vertices[to] - block.Vertices[from]));
                }
            }

            public (float, float) Projection(Vector3 axis)
            {
                float min = float.PositiveInfinity;
                float max = float.NegativeInfinity;
                foreach (Vector3 vertex in Block.Vertices)
                {
                    float p = Vector3.Dot(axis, vertex);
                    min = MathF.Min(min, p);
                    max = MathF.Max(max, p);
                }
                return (min, max);
            }

            public bool Supports(Vector3 origin, Vector3 up, float depth)
            {
                Vector3 end = origin - up * depth;
                Vector3 min = Vector3.Min(origin, end);
                Vector3 max = Vector3.Max(origin, end);
                if (Max.X < min.X || Min.X > max.X || Max.Y < min.Y || Min.Y > max.Y ||
                    Max.Z < min.Z || Min.Z > max.Z) return false;
                foreach (var (a, b, c) in Faces())
                {
                    Vector3 start = Block.Vertices[a];
                    Vector3 first = Block.Vertices[b] - start;
                    Vector3 second = Block.Vertices[c] - start;
                    if (Vector3.Dot(up, Vector3.Normalize(Vector3.Cross(first, second))) < .65f) continue;
                    Vector3 p = Vector3.Cross(-up, second);
                    float det = Vector3.Dot(first, p);
                    if (MathF.Abs(det) < .000001f) continue;
                    Vector3 delta = origin - start;
                    float u = Vector3.Dot(delta, p) / det;
                    Vector3 q = Vector3.Cross(delta, first);
                    float v = Vector3.Dot(-up, q) / det;
                    float d = Vector3.Dot(second, q) / det;
                    if (u >= -.00001f && v >= -.00001f && u + v <= 1.00001f && d >= 0 && d <= depth)
                        return true;
                }
                return false;
            }

            private IEnumerable<(int A, int B, int C)> Faces()
            {
                for (int i = 0; i < Block.Faces.Length; i += 3)
                    yield return (Block.Faces[i], Block.Faces[i + 1], Block.Faces[i + 2]);
            }
        }

        private static void Expect(bool pass, string message)
        {
            if (!pass) throw new InvalidOperationException(message);
        }
    }
}


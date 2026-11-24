using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using N = System.Numerics;

namespace Contour4.Procedural.Catalog
{
    internal static class PreparedModuleEnemyNav
    {
        private static readonly PreparedNavProfile[] Profiles =
        {
            new PreparedNavProfile("Reference geometry (r0.35 h1.80)", .35f, 1.80f, .30f, 45f),
            new PreparedNavProfile("Headman body geometry (r0.86 h2.86)", .86f, 2.86f, .40f, 60f)
        };
        private const string TransitionGroup = "Transition candidates - runtime unverified";
        private const string VerifiedPrefix = "Path ";

        internal static int Add(GrayModule module, Transform parent)
        {
            var root = parent.parent;
            int transitionCount = AddTransitions(module, Group(parent, TransitionGroup));
            var regions = Regions(module).ToArray();
            foreach (var profile in Profiles)
            {
                var paths = Group(parent, profile.Id);
                int count = 0;
                var normals = regions.Select(f => Vec(f.Pose.Up))
                    .Distinct().ToArray();
                foreach (var localUp in normals)
                {
                    using var bake = new PreparedNavBake(root, root.TransformDirection(localUp), profile);
                    foreach (Region region in regions.Where(f => bake.Matches(root.TransformDirection(Vec(f.Pose.Up)))))
                        count += AddRegion(module, root, paths, region, bake);
                    count += AddCatalogRoutes(module, root, paths, bake);
                    PreparedNavChecks.Check(module, root, bake, profile);
                }
                if (count == 0)
                    throw new InvalidOperationException($"{module.Id}: {profile.Id} has no verified patrol path.");
                Debug.Log($"CONTOUR4 PREPARED NAV {module.Id}: {profile.Id} verified-routes={count} " +
                    $"transition-candidates={transitionCount}");
            }
            return WalkPaths(parent).Count();
        }

        internal static int Verify(GrayModule module, Transform parent)
        {
            var root = parent.parent;
            var transitions = parent.Find(TransitionGroup);
            var expectedTransitions = Transitions(module).ToArray();
            if ((transitions ? transitions.childCount : 0) != expectedTransitions.Length)
                throw new InvalidOperationException($"{module.Id}: saved transition count differs from catalog.");
            foreach (var (name, knots) in expectedTransitions)
            {
                var track = transitions.Find(name);
                if (!track || track.childCount != knots.Length)
                    throw new InvalidOperationException($"{module.Id}/{name}: saved transition knots are missing.");
                for (int i = 0; i < knots.Length; i++)
                {
                    var k = track.GetChild(i);
                    var pose = knots[i];
                    if (k.name != $"{i + 1:D3}" || Vector3.Distance(k.localPosition, Vec(pose.Position)) > .0001f ||
                        Vector3.Dot(k.forward, root.TransformDirection(Vec(pose.Forward))) < .9999f ||
                        Vector3.Dot(k.up, root.TransformDirection(Vec(pose.Up))) < .9999f)
                        throw new InvalidOperationException($"{module.Id}/{name}/{i + 1}: saved local pose differs from catalog.");
                }
            }

            var rows = new List<string> { "profile\tgroup\troute\tknot\tx\ty\tz\tup_x\tup_y\tup_z" };
            int count = 0;
            foreach (var profile in Profiles)
            {
                var family = parent.Find(profile.Id);
                var paths = WalkPaths(family).ToArray();
                if (paths.Length == 0) throw new InvalidOperationException($"{module.Id}: missing {profile.Id} prefab paths.");
                var normals = Regions(module).Select(r => root.TransformDirection(Vec(r.Pose.Up))).Distinct().ToArray();
                foreach (var set in paths.GroupBy(path => normals.Single(up => Vector3.Dot(up, path.GetChild(0).up) > .9999f)))
                {
                    using var bake = new PreparedNavBake(root, set.Key, profile);
                    foreach (var path in set)
                    {
                        var knots = Enumerable.Range(0, path.childCount).Select(path.GetChild).ToArray();
                        if (knots.Length < 2 || !bake.TryPath(new[] { knots[0].position, knots.Last().position }, out _))
                            throw new InvalidOperationException($"{module.Id}/{profile.Id}/{path.name}: reloaded endpoints disconnected.");
                        for (int i = 0; i < knots.Length; i++)
                        {
                            var k = knots[i];
                            if (k.name != $"{i + 1:D3}" || !bake.ContainsSavedCorner(k.position) ||
                                Vector3.Dot(k.up, set.Key) < .9999f)
                                throw new InvalidOperationException($"{module.Id}/{profile.Id}/{path.name}: invalid saved waypoint {k.name}.");
                            Vector3 p = root.InverseTransformPoint(k.position), u = root.InverseTransformDirection(k.up);
                            rows.Add(FormattableString.Invariant($"{profile.Id}\t{path.parent.name}\t{path.name}\t{i + 1}\t{p.x:F4}\t{p.y:F4}\t{p.z:F4}\t{u.x:F4}\t{u.y:F4}\t{u.z:F4}"));
                        }
                        count++;
                    }
                }
            }
            string evidence = Environment.GetEnvironmentVariable("CONTOUR4_PREPARED_NAV_EVIDENCE");
            if (!string.IsNullOrEmpty(evidence))
            {
                Directory.CreateDirectory(evidence);
                File.WriteAllLines(Path.Combine(evidence, module.Id + ".tsv"), rows);
            }
            Debug.Log($"CONTOUR4 PREPARED NAV RELOADED {module.Id}: verified-routes={count}, transition-candidates={expectedTransitions.Length}");
            return count;
        }

        private static int AddRegion(GrayModule module, Transform root, Transform parent, Region region, PreparedNavBake bake)
        {
            var points = region.Candidates.Select(root.TransformPoint)
                .Select(p => Sample(bake, p)).Where(p => p.HasValue).Select(p => p.Value).Distinct().ToArray();
            var story = Group(parent, region.Id);
            int count = 0;
            if (points.Length >= 2)
            {
                var candidates = points.OrderByDescending(point => Vector3.Distance(point, points[0]));
                foreach (var companion in candidates)
                {
                    if (!bake.TryPath(new[] { points[0], companion }, out var corners)) continue;
                    AddPath(story, "patrol", root, bake.Up, corners);
                    count++;
                    break;
                }

                foreach (Dock dock in module.Docks.Where(d => d.FloorId == region.FloorId))
                {
                    if (!TryEndpoint(module, root, bake, dock.Id, out var approach) ||
                        !region.Contains(root.InverseTransformPoint(approach))) continue;
                    foreach (var start in points)
                    {
                        if (!bake.TryPath(new[] { start, approach }, out var corners)) continue;
                        AddPath(story, $"approach {dock.Id}", root, bake.Up, corners);
                        count++;
                        break;
                    }
                }
            }
            if (count == 0)
                throw new InvalidOperationException($"{module.Id}/{parent.name}/{region.Id}: no supported local patrol route.");
            Debug.Log($"CONTOUR4 PREPARED NAV {module.Id}/{parent.name}/{region.Id}: " +
                $"sampled-nodes={points.Length}, routes={count}, local-up={Vec(region.Pose.Up)}");
            return count;
        }

        private static int AddCatalogRoutes(GrayModule module, Transform root, Transform parent, PreparedNavBake bake)
        {
            int count = 0;
            foreach (HaulRoute route in module.Routes.Where(r => !IsTransition(r)))
            {
                if (!bake.Matches(root.TransformDirection(Vec(route.Knots[0].Up)))) continue;
                if (!TryEndpoint(module, root, bake, route.From, out var start) ||
                    !TryEndpoint(module, root, bake, route.To, out var end))
                {
                    Debug.Log($"CONTOUR4 PREPARED NAV {module.Id}/{parent.name}/{route.Id}: endpoint unavailable");
                    continue;
                }
                var pads = new List<Vector3> { start };
                if (module.Id == "calibration-sector" && (route.Id == "near-aisle" || route.Id == "far-aisle"))
                    pads.Add(root.TransformPoint(new Vector3(11.25f, 0, route.Id == "near-aisle" ? -4.5f : 4.5f)));
                if (module.Id.StartsWith("sample-storage", StringComparison.Ordinal) && route.Id == "short-aisle-hand-cargo")
                    pads.Add(root.TransformPoint(new Vector3(8.50f, 0, 0)));
                if (module.Id == MobiusHall.Id && route.Id == "ground-through")
                    pads.AddRange(MobiusHall.NativeBypassPads.Select(point => root.TransformPoint(Vec(point))));
                pads.Add(end);
                if (!bake.TryPath(pads, out var corners))
                {
                    Debug.Log($"CONTOUR4 PREPARED NAV {module.Id}/{parent.name}/{route.Id}: disconnected or clearance failed");
                    continue;
                }
                AddPath(parent, route.Id, root, bake.Up, corners);
                Debug.Log($"CONTOUR4 PREPARED NAV {module.Id}/{parent.name}/{route.Id}: complete " +
                    $"corners={corners.Length} length={PreparedNavBake.PathLength(corners):F2}m");
                count++;
            }
            return count;
        }

        private static bool TryEndpoint(GrayModule module, Transform root, PreparedNavBake bake, string id, out Vector3 point)
        {
            point = default;
            Dock dock = module.Docks.FirstOrDefault(d => d.Id == id);
            if (dock != null)
            {
                Floor floor = module.Floors.Single(f => f.Id == dock.FloorId);
                var up = root.TransformDirection(Vec(floor.Pose.Up));
                bool transverse = Math.Abs(N.Vector3.Dot(dock.Threshold.Forward, floor.Pose.Up)) > .1f;
                if (!transverse) up = root.TransformDirection(Vec(dock.Threshold.Up));
                if (!bake.Matches(up)) return false;
                if (!transverse)
                {
                    foreach (float setback in new[] { 1.35f, 1.65f, 1.95f, 2.25f, 2.65f })
                        if (bake.TryPoint(root.TransformPoint(Vec(dock.Threshold.Point(-setback, 0))), out point, out _))
                            return true;
                    return false;
                }
                // Hatch threshold is vertical to its floor. Use the authored floor-side pad if present,
                // then try the point below the hatch on the declared story. Keep the hatch itself sealed.
                foreach (var approach in module.Routes.Where(r => r.To == id && !IsTransition(r)))
                    if (bake.TryPoint(root.TransformPoint(Vec(approach.Knots.Last().Position)), out point, out _)) return true;
                var center = dock.Threshold.Point(0, dock.Opening.Y / 2);
                center -= floor.Pose.Up * N.Vector3.Dot(center - floor.Pose.Position, floor.Pose.Up);
                return Nearby(bake, root.TransformPoint(Vec(center)), root.TransformDirection(Vec(floor.Pose.Forward)), out point);
            }

            if (!module.Landmarks.TryGetValue(id, out var landmark)) return false;
            var frame = module.Floors.OrderBy(f => Math.Abs(N.Vector3.Dot(landmark - f.Pose.Position, f.Pose.Up)))
                .FirstOrDefault(f => f.Id.StartsWith(id.Split('-')[0] + "-", StringComparison.Ordinal) &&
                    Contains(f, landmark)) ?? module.Floors.FirstOrDefault(f => Contains(f, landmark));
            return frame != null && bake.Matches(root.TransformDirection(Vec(frame.Pose.Up))) &&
                Nearby(bake, root.TransformPoint(Vec(landmark)), root.TransformDirection(Vec(frame.Pose.Forward)), out point);
        }

        private static bool Nearby(PreparedNavBake bake, Vector3 target, Vector3 forward, out Vector3 point)
        {
            if (bake.TryPoint(target, out point, out _)) return true;
            Vector3 side = Vector3.Cross(forward, bake.Up);
            foreach (float offset in new[] { .35f, .65f, .95f, 1.25f })
            foreach (var dir in new[] { forward, -forward, side, -side })
                if (bake.TryPoint(target + offset * dir, out point, out _)) return true;
            return false;
        }

        private static Vector3? Sample(PreparedNavBake bake, Vector3 point) =>
            bake.TryPoint(point, out var sampled, out _) ? sampled : (Vector3?)null;

        private sealed class Region
        {
            internal readonly string Id;
            internal readonly string FloorId;
            internal readonly ModulePose Pose;
            internal readonly Vector3[] Candidates;
            internal readonly Func<Vector3, bool> Contains;

            internal Region(string id, string floorId, ModulePose pose, Vector3[] candidates, Func<Vector3, bool> contains)
            {
                Id = id;
                FloorId = floorId;
                Pose = pose;
                Candidates = candidates;
                Contains = contains;
            }
        }

        private static IEnumerable<Region> Regions(GrayModule module)
        {
            foreach (Floor floor in module.Floors)
            {
                // Curves have an unrolled map outline. They receive oriented transition knots and
                // separately baked straight leads where available. The trench plank is conditional.
                if (floor.Id == "helix" || floor.Id == "bridge" || floor.Id.EndsWith("-bridge", StringComparison.Ordinal) ||
                    floor.Id == "plank") continue;
                var contour = floor.MapContour;
                float minX = contour.Min(p => p.X), maxX = contour.Max(p => p.X);
                float minZ = contour.Min(p => p.Y), maxZ = contour.Max(p => p.Y);
                var center = (contour.Aggregate(N.Vector2.Zero, (a, p) => a + p) / contour.Length);
                var samples = new List<Vector3> { Vec(floor.Pose.Point(center.X, 0, center.Y)) };
                for (float x = minX + .50f; x <= maxX - .45f; x += .50f)
                for (float z = minZ + .50f; z <= maxZ - .45f; z += .50f)
                    samples.Add(Vec(floor.Pose.Point(x, 0, z)));
                yield return new Region(floor.Id, floor.Id, floor.Pose,
                    samples.Where(p => Contains(floor, Num(p))).ToArray(), p => Contains(floor, Num(p)));
            }
            if (module.Id != "quarter-turn-bridge" && module.Id != "spiral-corridor") yield break;
            foreach (Dock dock in module.Docks)
            {
                ModulePose pose = dock.Threshold;
                var samples = new[] { 1.30f, 1.75f, 2.25f, 2.50f }.Select(s => Vec(pose.Point(-s, 0))).ToArray();
                yield return new Region(dock.Id + " lead", dock.FloorId, pose, samples, p =>
                {
                    var delta = Num(p) - pose.Position;
                    float forward = N.Vector3.Dot(delta, pose.Forward);
                    return forward < -.75f && forward > -2.80f && Math.Abs(N.Vector3.Dot(delta, pose.Up)) < .20f;
                });
            }
        }

        private static bool Contains(Floor floor, N.Vector3 point)
        {
            var local = point - floor.Pose.Position;
            if (Math.Abs(N.Vector3.Dot(local, floor.Pose.Up)) > .22f) return false;
            float x = N.Vector3.Dot(local, floor.Pose.Forward), y = N.Vector3.Dot(local, floor.Pose.Right);
            var outline = floor.MapContour;
            bool inside = false;
            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
            {
                var a = outline[i];
                var b = outline[j];
                if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        private static bool IsTransition(HaulRoute route) => route.Condition != null || route.Flight.HasValue ||
            route.Knots.Any(p => N.Vector3.Dot(p.Up, route.Knots[0].Up) < .9999f);

        private static int AddTransitions(GrayModule module, Transform parent)
        {
            int count = 0;
            foreach (var (name, knots) in Transitions(module))
            {
                var group = Group(parent, name);
                int index = 0;
                foreach (ModulePose knot in knots) AddKnot(group, ++index, Vec(knot.Position), Vec(knot.Forward), Vec(knot.Up));
                count++;
            }
            foreach (var (a, b) in module.InternalPortals)
            {
                PreparedNavChecks.CheckPortalPlug(parent.parent.parent, module.Docks.Single(d => d.Id == a));
                PreparedNavChecks.CheckPortalPlug(parent.parent.parent, module.Docks.Single(d => d.Id == b));
                Debug.Log($"CONTOUR4 PREPARED NAV NEGATIVE {module.Id}/{a} to {b}: sealed-plugs=true, cross-gravity-link=false");
            }
            if (count == 0) UnityEngine.Object.DestroyImmediate(parent.gameObject);
            return count;
        }

        private static IEnumerable<(string Name, ModulePose[] Knots)> Transitions(GrayModule module)
        {
            foreach (HaulRoute route in module.Routes.Where(IsTransition))
            {
                string kind = route.Flight.HasValue ? "jump" : route.Condition ?? "rotating-gravity";
                var knots = route.Knots.AsEnumerable();
                // The cargo courses include poses in mating rooms for the threshold tests.
                var first = module.Docks.FirstOrDefault(d => d.Id == route.From);
                var last = module.Docks.FirstOrDefault(d => d.Id == route.To);
                if (first != null) knots = knots.SkipWhile(p => Ahead(first, p));
                if (last != null) knots = knots.Reverse().SkipWhile(p => Ahead(last, p)).Reverse();
                var poses = knots.ToArray();
                if (poses.Length < 2) throw new InvalidOperationException($"{module.Id}: {route.Id} lost its transition knots.");
                yield return ($"{kind} - {route.Id}", poses);
            }
            foreach (var (a, b) in module.InternalPortals)
            {
                ModulePose PoseFor(string id)
                {
                    var dock = module.Docks.Single(d => d.Id == id);
                    var threshold = dock.Threshold;
                    return new ModulePose(threshold.Point(0, dock.Opening.Y / 2), threshold.Forward, threshold.Up);
                }
                yield return ($"sealed portal - {a} to {b}", new[] { PoseFor(a), PoseFor(b) });
            }
        }

        private static bool Ahead(Dock dock, ModulePose pose) =>
            N.Vector3.Dot(pose.Position - dock.Threshold.Position, dock.Threshold.Forward) > .025f;

        private static void AddPath(Transform parent, string id, Transform root, Vector3 worldUp, Vector3[] corners)
        {
            var path = Group(parent, VerifiedPrefix + id);
            Vector3 up = root.InverseTransformDirection(worldUp);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 point = root.InverseTransformPoint(corners[i]);
                Vector3 tangent = i < corners.Length - 1 ? corners[i + 1] - corners[i] : corners[i] - corners[i - 1];
                Vector3 forward = Vector3.ProjectOnPlane(root.InverseTransformDirection(tangent), up);
                if (forward.sqrMagnitude < .00001f) forward = Vector3.ProjectOnPlane(Vector3.right, up);
                if (forward.sqrMagnitude < .00001f) forward = Vector3.ProjectOnPlane(Vector3.forward, up);
                AddKnot(path, i + 1, point, forward, up);
            }
        }

        private static void AddKnot(Transform parent, int index, Vector3 point, Vector3 forward, Vector3 up)
        {
            var knot = Group(parent, $"{index:D3}");
            knot.localPosition = point;
            knot.localRotation = Quaternion.LookRotation(forward, up);
        }

        private static Transform Group(Transform parent, string name)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        private static IEnumerable<Transform> WalkPaths(Transform parent) => parent.GetComponentsInChildren<Transform>()
            .Where(t => t.name.StartsWith(VerifiedPrefix, StringComparison.Ordinal));

        private static Vector3 Vec(N.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
        private static N.Vector3 Num(Vector3 v) => new N.Vector3(v.x, v.y, v.z);
    }
}

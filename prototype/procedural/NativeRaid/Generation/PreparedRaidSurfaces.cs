using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural.Catalog;
using Contour4.Procedural.Navigation;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class PreparedRaidSurfaces
    {
        private static readonly RaycastHit[] coincidentContacts = new RaycastHit[16];
        private readonly Dictionary<string, RaidFloor> byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RaidFloor> byDock = new(StringComparer.Ordinal);
        private readonly List<(RaidFloor Floor, Vector2[] Outline)> surfaces = new();
        private readonly HashSet<Collider> decks;
        private readonly List<PreparedRaidTurn> turns = new();
        private readonly List<PreparedRaidTurn> stairs = new();
        private readonly Transform root;
        private readonly Bounds occupied;
        internal IEnumerable<RaidFloor> Floors => surfaces.Select(item => item.Floor);
        internal IReadOnlyList<(RaidFloor Floor, Vector2[] Outline)> GravityFloors => surfaces;
        internal IReadOnlyList<PreparedRaidTurn> Turns => turns;

        internal PreparedRaidSurfaces(RaidPlan.Room room, Transform root, Dictionary<string, Collider> parts,
            Collider[] geometry, Collider cableSupport)
        {
            this.root = root;
            occupied = RaidPlan.BoundsOf(room.Module.Geometry.Where(block => block.Role != BlockRole.Free)
                .SelectMany(block => block.Vertices.Select(RaidPlan.Vector)));
            occupied.Expand(.6f);
            string roomId = $"{room.Module.Id}-{room.Index}";
            var support = room.Module.Geometry.Where(block => block.Role == BlockRole.Floor ||
                    room.Module.Id == MobiusHall.Id && block.Id == "roof")
                .ToDictionary(block => block, block => parts[block.Id]);
            decks = support.Values.ToHashSet();
            if (cableSupport) decks.Add(cableSupport);
            // The Möbius ribbon is deliberately excluded from the planar floor
            // bake. It is nevertheless a genuine support for oriented traversal.
            if (room.Module.Id == MobiusHall.Id) decks.Add(parts[MobiusHall.Ribbon]);
            foreach (var floor in room.Module.Floors)
            {
                Transform marker = Required(root, "Floor frames/" + floor.Id, floor.Pose);
                bool turn = floor.Id is "helix" or "bridge" || floor.Id.EndsWith("-bridge", StringComparison.Ordinal);
                if (turn) continue;
                var outline = floor.MapContour.Select(point => new Vector2(-point.Y, point.X)).ToArray();
                byId.Add(floor.Id, AddFloor(roomId, floor.Id, "floor", marker, outline, geometry, support,
                    floor.Id == "ground" ? cableSupport : null));
            }

            foreach (var dock in room.Module.Docks)
            {
                Transform marker = Required(root, "Docks/" + dock.Id, dock.Threshold);
                if (byId.TryGetValue(dock.FloorId, out var existing) &&
                    room.Module.Floors.Single(floor => floor.Id == dock.FloorId).Id is not ("helix" or "bridge"))
                {
                    byDock.Add(dock.Id, existing);
                    continue;
                }

                float width = dock.Opening.X * .5f;
                const float lead = 3f;
                var outline = new[] { new Vector2(-width, -lead), new Vector2(width, -lead),
                    new Vector2(width, 0), new Vector2(-width, 0) };
                var local = AddFloor(roomId, dock.FloorId, dock.Id, marker, outline, geometry, support);
                byId.TryAdd(dock.FloorId, local);
                byDock.Add(dock.Id, local);
            }

            foreach (var route in room.Module.Routes)
            {
                bool changesUp = route.Knots.Any(knot =>
                    System.Numerics.Vector3.Dot(knot.Up, route.Knots[0].Up) < .9999f);
                bool stepped = !changesUp && route.StepClearance > .2f &&
                    MathF.Abs(System.Numerics.Vector3.Dot(route.Knots[^1].Position - route.Knots[0].Position,
                        route.Knots[0].Up)) > .3f;
                if (!changesUp && !stepped) continue;
                Transform track = root.Find("Haul routes/" + route.Id);
                if (!track || track.childCount != route.Knots.Length)
                    throw new InvalidOperationException($"{roomId}/{route.Id}: missing authored route knots.");
                var knots = route.Knots.Select((pose, index) => Required(root,
                    $"Haul routes/{route.Id}/{index + 1:D3}", pose)).ToArray();
                (stepped ? stairs : turns).Add(new PreparedRaidTurn(route, root, knots, decks));
            }
        }

        internal bool TryFloor(string id, out RaidFloor floor) => byId.TryGetValue(id, out floor);
        internal RaidFloor DockFloor(Dock dock) => byDock[dock.Id];
        internal bool Contains(Vector3 point) => occupied.Contains(root.InverseTransformPoint(point));

        internal IEnumerable<RaidDeckPassage> StairPassages(int mask)
        {
            foreach (var stair in stairs)
            {
                var course = stair.DenseSamples(.06f).ToArray();
                Pose start = stair.World(course[0]), finish = stair.World(course[^1]);
                if (!TryNearFloor(start.position, .08f, mask, out var from, out _) ||
                    !TryNearFloor(finish.position, .08f, mask, out var to, out _) ||
                    from == to || Vector3.Dot(from.Up, to.Up) < .9998f)
                    continue;
                yield return new RaidDeckPassage(from, to, stair.Root, course, decks, stepped: true);
                var reverse = course.Reverse().Select(pose => new Pose(pose.position,
                    pose.rotation * Quaternion.AngleAxis(180f, Vector3.up))).ToArray();
                yield return new RaidDeckPassage(to, from, stair.Root, reverse, decks, stepped: true);
            }
        }

        internal IEnumerable<RaidDeckPassage> DeckPassages(int mask)
        {
            foreach (var turn in turns)
            {
                var poses = turn.DenseSamples().ToArray();
                int start = -1, finish = -1;
                RaidFloor from = null, to = null;
                for (int i = 0; i < poses.Length; i++)
                {
                    Pose world = turn.World(poses[i]);
                    if (!TryNearFloor(world.position, .18f, mask, out var floor, out _) ||
                        Vector3.Dot(floor.Up, world.up) < .9998f) continue;
                    // The local walk mesh erodes at the tile boundary even when the physical
                    // door beyond it is open. Keep the crossing's anchor inside the lead.
                    Vector3 p = floor.Local(world.position);
                    Bounds bake = floor.LocalBakeBounds;
                    float margin = Mathf.Min(p.x - bake.min.x, bake.max.x - p.x, p.z - bake.min.z, bake.max.z - p.z);
                    if (margin < 1.0f) continue;
                    // A closed optional Möbius outlet remains a real plug. Its
                    // corridor course can extend through that plug for generation,
                    // while the interior floor↔ceiling deck must end before it.
                    if (turn.Route.Id == MobiusHall.CeilingRoute && Physics.CheckCapsule(
                        world.position + world.up * (.86f + RaidGeometry.Skin),
                        world.position + world.up * (2.86f - .86f + RaidGeometry.Skin), .86f,
                        mask, QueryTriggerInteraction.Ignore)) continue;
                    if (start < 0) { start = i; from = floor; }
                    if (floor == from) continue;
                    finish = i;
                    to = floor;
                }
                if (finish <= start) continue;
                var course = poses.Skip(start).Take(finish + 1 - start).ToArray();
                yield return new RaidDeckPassage(from, to, turn.Root, course, decks);
                var reverse = course.Reverse().Select(pose => new Pose(pose.position,
                    pose.rotation * Quaternion.AngleAxis(180f, Vector3.up))).ToArray();
                yield return new RaidDeckPassage(to, from, turn.Root, reverse, decks);
            }
        }

        internal static bool StockLink(RaidFloor floor, Vector3 from, Vector3 to, int mask, float maximumLength = 2.65f)
        {
            if (Vector3.Dot(floor.Up, Vector3.up) < .999f || Vector3.Distance(from, to) > maximumLength) return false;
            const float radius = .86f, height = 2.86f;
            Vector3 up = floor.Up;
            Vector3 lower = up * (radius + .035f), upper = up * (height - radius + .035f);
            Vector3 travel = to - from;
            if (Physics.CheckCapsule(from + lower, from + upper, radius, mask, QueryTriggerInteraction.Ignore) ||
                Physics.CheckCapsule(to + lower, to + upper, radius, mask, QueryTriggerInteraction.Ignore) ||
                travel.sqrMagnitude > .00001f && Physics.CapsuleCast(from + lower, from + upper, radius,
                    travel.normalized, travel.magnitude, mask, QueryTriggerInteraction.Ignore)) return false;
            int samples = Mathf.Max(1, Mathf.CeilToInt(travel.magnitude / .12f));
            for (int i = 0; i <= samples; i++)
                if (!Probe(Vector3.Lerp(from, to, i / (float)samples), up, .18f, mask, floor.Support, out _)) return false;
            return true;
        }

        private RaidFloor AddFloor(string room, string id, string island, Transform frame, Vector2[] outline,
            Collider[] geometry, Dictionary<Block, Collider> candidates, Collider cableSupport = null)
        {
            var root = frame.parent.parent;
            Bounds bounds = RaidPlan.BoundsOf(outline.Select(point => new Vector3(point.x, 0, point.y)));
            var crop = bounds;
            crop.Expand(new Vector3(.08f, 0, .08f));
            var support = candidates.Where(item => Supports(item.Key, root, frame, crop)).Select(item => item.Value).ToList();
            if (support.Count == 0) throw new InvalidOperationException($"{room}/{id}/{island}: no authored support faces.");
            if (cableSupport) support.Add(cableSupport);
            bounds.center = new Vector3(bounds.center.x, 1.52f, bounds.center.z);
            bounds.size = new Vector3(bounds.size.x + .28f, 3.88f, bounds.size.z + .28f);
            var value = new RaidFloor(room, id, island, new Pose(frame.position, frame.rotation),
                bounds, geometry, support, frame, prepared: true);
            surfaces.Add((value, outline));
            return value;
        }

        private static bool Supports(Block block, Transform root, Transform floor, Bounds crop)
        {
            Vector3[] local = block.Vertices.Select(point => floor.InverseTransformPoint(root.TransformPoint(RaidPlan.Vector(point)))).ToArray();
            var faces = block.Faces;
            for (int i = 0; i < faces.Length; i += 3)
            {
                var a = local[faces[i]];
                var b = local[faces[i + 1]];
                var c = local[faces[i + 2]];
                if (Vector3.Cross(b - a, c - a).normalized.y < .65f || Mathf.Max(a.y, b.y, c.y) < -.23f ||
                    Mathf.Min(a.y, b.y, c.y) > 2.15f) continue;
                if (Mathf.Max(a.x, b.x, c.x) < crop.min.x || Mathf.Min(a.x, b.x, c.x) > crop.max.x ||
                    Mathf.Max(a.z, b.z, c.z) < crop.min.z || Mathf.Min(a.z, b.z, c.z) > crop.max.z) continue;
                return true;
            }
            return false;
        }

        internal bool TryNearFloor(Vector3 point, float maxHeight, int mask, out RaidFloor result, out float distance)
        {
            result = null;
            distance = float.PositiveInfinity;
            if (!Contains(point)) return false;
            foreach (var (floor, outline) in surfaces)
            {
                Vector3 local = floor.Local(point);
                if (local.y < -.25f || local.y > maxHeight + 2.15f || !InOutline(outline, new Vector2(local.x, local.z)) ||
                    !Probe(point, floor.Up, maxHeight, mask, floor.Support, out float hit) || hit >= distance) continue;
                result = floor;
                distance = hit;
            }
            return result != null;
        }

        internal static bool Probe(Vector3 point, Vector3 up, float maxHeight, int mask, HashSet<Collider> supports,
            out float distance)
        {
            const float lift = .25f;
            distance = float.PositiveInfinity;
            Vector3 origin = point + up * lift;
            if (Physics.Raycast(origin, -up, out var hit, maxHeight + lift, mask, QueryTriggerInteraction.Ignore))
            {
                if (!RaidGeometry.Supports(Physics.defaultPhysicsScene, hit, origin, up, .65f, mask, supports, null, coincidentContacts))
                    return false;
                distance = Mathf.Abs(hit.distance - lift);
                return true;
            }

            // Contact can straddle coincident triangle edges on the swept deck.
            const float foot = .04f;
            if (!Physics.SphereCast(origin, foot, -up, out hit, maxHeight + lift, mask, QueryTriggerInteraction.Ignore) ||
                !supports.Contains(hit.collider) || Vector3.Dot(hit.normal, up) < .65f) return false;
            distance = Mathf.Abs(hit.distance + foot - lift);
            return true;
        }

        internal static bool InOutline(Vector2[] outline, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
            {
                var a = outline[i];
                var b = outline[j];
                var side = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(point - a, side) / side.sqrMagnitude);
                if ((point - Vector2.Lerp(a, b, t)).sqrMagnitude < .0049f) return true;
                if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        internal static Transform Required(Transform root, string path, Contour4.Procedural.ModulePose pose)
        {
            Transform marker = root.Find(path);
            if (!marker || Vector3.Distance(root.InverseTransformPoint(marker.position), RaidPlan.Vector(pose.Position)) > .001f ||
                Vector3.Dot(root.InverseTransformDirection(marker.forward), RaidPlan.Vector(pose.Forward)) < .9999f ||
                Vector3.Dot(root.InverseTransformDirection(marker.up), RaidPlan.Vector(pose.Up)) < .9999f)
                throw new InvalidOperationException($"{root.name}/{path}: prepared anchor differs from the catalog pose.");
            return marker;
        }
    }

    internal sealed class PreparedRaidTurn
    {
        internal HaulRoute Route { get; }
        private readonly Transform root;
        private readonly HashSet<Collider> decks;
        private readonly Pose[] samples;
        internal IReadOnlyList<Pose> Samples => samples;
        internal Transform Root => root;

        internal PreparedRaidTurn(HaulRoute route, Transform root, Transform[] knots, HashSet<Collider> decks)
        {
            Route = route;
            this.root = root;
            this.decks = decks;
            samples = knots.Select(kn => new Pose(root.InverseTransformPoint(kn.position),
                Quaternion.Inverse(root.rotation) * kn.rotation)).ToArray();
        }

        internal Pose World(Pose local) => new(root.TransformPoint(local.position), root.rotation * local.rotation);

        internal IEnumerable<Pose> DenseSamples(float stepSize = .13f) => RaidDeckPassage.DenseSamples(samples, stepSize);

        internal bool TryUp(Vector3 point, int mask, bool allowsHandOnly, out Vector3 up, out float distance)
        {
            up = default;
            distance = float.PositiveInfinity;
            if (!allowsHandOnly && Route.Cargo == Cargo.Hand) return false;
            Vector3 local = root.InverseTransformPoint(point);
            for (int i = 1; i < samples.Length; i++)
            {
                Pose a = samples[i - 1], b = samples[i];
                var along = Vector3.Slerp(a.forward, b.forward, .5f);
                float length = Vector3.Dot(b.position - a.position, along);
                if (length < .01f) continue;
                float depth = Vector3.Dot(local - a.position, along);
                if (depth < -.028f || depth > length + .028f) continue;
                float t = Mathf.Clamp01(depth / length);
                Vector3 offset = local - Vector3.Lerp(a.position, b.position, t);
                Quaternion rotation = Quaternion.Slerp(a.rotation, b.rotation, t);
                Vector3 normal = rotation * Vector3.up;
                float height = Vector3.Dot(offset, normal);
                if (height < -.23f || height > GrayBuilder.Height + .12f ||
                    Mathf.Abs(Vector3.Dot(offset, rotation * Vector3.right)) > GrayBuilder.Wide / 2 + .04f) continue;
                Vector3 worldUp = root.TransformDirection(normal);
                if (!PreparedRaidSurfaces.Probe(point, worldUp, GrayBuilder.Height, mask, decks, out float hit) || hit >= distance)
                    continue;
                up = worldUp;
                distance = hit;
            }
            return distance < float.PositiveInfinity;
        }
    }
}

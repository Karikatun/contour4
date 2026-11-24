using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using UnityEngine;

namespace Contour4.Procedural.Catalog
{
    internal static partial class PreparedModuleLoot
    {
        private static void VerifyRibbonSpawn(Transform parent, IEnumerable<Site> sites,
            IReadOnlyDictionary<string, Transform> markers)
        {
            var room = parent.parent;
            SimulationMode simulation = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                foreach (Site site in sites)
                {
                    Transform marker = markers[MarkerPrefix + site.Id];
                    CheckSettling(site, marker, room, compound: false);
                    CheckSettling(site, marker, room, compound: true);
                    CheckFieldBoundary(site, room);
                }
            }
            finally { Physics.simulationMode = simulation; }
        }

        private static void CheckSettling(Site site, Transform marker, Transform room, bool compound)
        {
            GameObject witness = new("Ribbon loot physics witness");
            try
            {
                witness.transform.SetPositionAndRotation(marker.position, marker.rotation);
                Rigidbody body = witness.AddComponent<Rigidbody>();
                Vector3 size = site.Dimensions;
                if (compound)
                {
                    // Two separated feet and an asymmetric housing with a narrower footprint.
                    AddBox(new Vector3(0, .08f, .24f), new Vector3(.74f, .16f, .27f));
                    AddBox(new Vector3(0, .08f, .72f), new Vector3(.68f, .16f, .31f));
                    AddBox(new Vector3(.07f, .43f, .51f), new Vector3(.66f, .54f, .78f));
                }
                else AddBox(new Vector3(0, .5f, .5f), Vector3.one);
                Physics.SyncTransforms();

                const float tick = .02f;
                float maximumDrift = 0;
                float firstDrift = 0;
                for (int step = 0; step < 150; step++)
                {
                    if (!MobiusWalk.TryFieldUp(room, body.worldCenterOfMass, out Vector3 up) || Vector3.Dot(up, marker.up) < .999f)
                        throw new InvalidOperationException($"{site.Id}: the loot left the field at tick {step}.");
                    if (!body.IsSleeping()) ApplyGravity(body, up);
                    Physics.Simulate(tick);
                    float displacement = Vector3.Distance(body.position, marker.position);
                    if (step == 0) firstDrift = displacement;
                    maximumDrift = Mathf.Max(maximumDrift, displacement);
                }
                if (firstDrift > .002f || maximumDrift > .006f || body.linearVelocity.magnitude > .01f ||
                    Quaternion.Angle(marker.rotation, body.rotation) > 2f)
                    throw new InvalidOperationException($"{site.Id}: loot moved on spawn: first={firstDrift:F5} m, " +
                        $"max={maximumDrift:F5} m, final velocity={body.linearVelocity.magnitude:F5} m/s.");
                Debug.Log($"CONTOUR4 MOBIUS LOOT SETTLED {site.Id} {site.Capacity} {(compound ? "compound" : "box")}: " +
                    $"first={firstDrift * 1000:F3} mm, max={maximumDrift * 1000:F3} mm, 150 ticks");

                void AddBox(Vector3 center, Vector3 extent)
                {
                    var shape = witness.AddComponent<BoxCollider>();
                    shape.center = Vector3.Scale(center, size);
                    shape.size = Vector3.Scale(extent, size);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(witness); }
        }

        private static void CheckFieldBoundary(Site site, Transform room)
        {
            var witness = new GameObject("Ribbon gravity boundary witness");
            try
            {
                Rigidbody body = witness.AddComponent<Rigidbody>();
                Vector3 surface = room.TransformPoint(site.Bottom);
                Vector3 normal = room.TransformDirection(site.Up);
                foreach (float height in new[] { .99f, 1.01f })
                {
                    body.position = surface + normal * height;
                    body.linearVelocity = Vector3.zero;
                    bool inside = MobiusWalk.TryFieldUp(room, body.worldCenterOfMass, out Vector3 up);
                    if (inside != (height < MobiusHall.GravityReach))
                        throw new InvalidOperationException($"{site.Id}: invalid field membership at {height:F2} m.");
                    ApplyGravity(body, inside ? up : room.up);
                    const float tick = .02f;
                    Physics.Simulate(tick);
                    Vector3 expected = (inside ? -normal : -room.up) * (Physics.gravity.magnitude * tick);
                    if (Vector3.Distance(body.linearVelocity, expected) > .001f)
                        throw new InvalidOperationException($"{site.Id}: wrong rigidbody gravity at {height:F2} m.");
                }
                Debug.Log($"CONTOUR4 MOBIUS LOOT FIELD {site.Id}: ribbon at .99 m, hall at 1.01 m");
            }
            finally { UnityEngine.Object.DestroyImmediate(witness); }
        }

        private static void ApplyGravity(Rigidbody body, Vector3 up) =>
            body.AddForce(MobiusWalk.TakeoffGravityCorrection(up, Physics.gravity), ForceMode.Acceleration);

        private readonly struct Volume
        {
            internal readonly Vector3 Center, Half, Right, Up, Forward;
            internal readonly Quaternion Rotation;

            internal Volume(Vector3 center, Vector3 half, Quaternion rotation)
            {
                Center = center;
                Half = half;
                Rotation = rotation;
                Right = rotation * Vector3.right;
                Up = rotation * Vector3.up;
                Forward = rotation * Vector3.forward;
            }

            private float Extent(Vector3 axis) => Mathf.Abs(Vector3.Dot(Right, axis)) * Half.x +
                Mathf.Abs(Vector3.Dot(Up, axis)) * Half.y + Mathf.Abs(Vector3.Dot(Forward, axis)) * Half.z;

            internal bool Intersects(Volume other)
            {
                Vector3 delta = other.Center - Center;
                return !Separated(Right, other, delta) && !Separated(Up, other, delta) && !Separated(Forward, other, delta) &&
                    !Separated(other.Right, other, delta) && !Separated(other.Up, other, delta) && !Separated(other.Forward, other, delta) &&
                    !Separated(Vector3.Cross(Right, other.Right), other, delta) &&
                    !Separated(Vector3.Cross(Right, other.Up), other, delta) &&
                    !Separated(Vector3.Cross(Right, other.Forward), other, delta) &&
                    !Separated(Vector3.Cross(Up, other.Right), other, delta) &&
                    !Separated(Vector3.Cross(Up, other.Up), other, delta) &&
                    !Separated(Vector3.Cross(Up, other.Forward), other, delta) &&
                    !Separated(Vector3.Cross(Forward, other.Right), other, delta) &&
                    !Separated(Vector3.Cross(Forward, other.Up), other, delta) &&
                    !Separated(Vector3.Cross(Forward, other.Forward), other, delta);
            }

            private bool Separated(Vector3 axis, Volume other, Vector3 delta) =>
                Mathf.Abs(Vector3.Dot(delta, axis)) > Extent(axis) + other.Extent(axis) + .00001f;
        }

        private sealed partial class RoomQuery
        {
            private const float PlayerHeight = 1.73f;
            private const float PlayerRadius = .25f;
            private readonly Transform frame;
            private readonly GrayModule module;
            private readonly HashSet<Collider> colliders;
            private readonly HashSet<string> decks;
            private readonly (Block Block, Bounds Bounds)[] solids;
            private readonly Volume[] reserved;
            private readonly (Vector3 Feet, Vector3 Up)[] walkEntries;

            internal RoomQuery(GrayModule module, Transform frame)
            {
                this.module = module;
                this.frame = frame;
                colliders = frame.parent.GetComponentsInChildren<Collider>().ToHashSet();
                decks = module.Geometry.Where(block => block.Role == BlockRole.Floor || module.Id == MobiusHall.Id && block.Id == "roof").Select(block => block.Id).ToHashSet();
                solids = module.Geometry.Where(block => block.Role != BlockRole.Free && block.Convex).Select(block =>
                {
                    var (min, max) = block.Bounds;
                    return (block, new Bounds(V((min + max) * .5f), V(max - min)));
                }).ToArray();
                var routes = module.Routes.Select(route => (Route: route, Samples: route.Samples(.13f).ToArray())).ToArray();
                walkEntries = routes.SelectMany(entry => entry.Samples.Where(entry.Route.NeedsSupport).Where((_, i) => i % 4 == 0))
                    .Select(pose => (V(pose.Position), V(pose.Up))).Distinct().ToArray();
                var fullHeight = module.Id == MaintenanceTrench.Id ? new[]
                {
                    new Volume(new Vector3(MaintenanceTrench.GateX, MaintenanceTrench.GateTravel / 2, MaintenanceTrench.BridgeZ),
                        new Vector3(.13f, MaintenanceTrench.GateTravel / 2, MaintenanceTrench.Opening / 2 + .02f), Quaternion.identity)
                } : Array.Empty<Volume>();
                reserved = routes.SelectMany(entry => entry.Samples.Select(pose =>
                {
                    Vector3 size = V(entry.Route.Envelope);
                    var half = new Vector3(size.z, size.y, size.x) / 2;
                    // Include the deck-to-clearance gap, including when the route profile
                    // carries a hand item clear of the player's feet on stairs.
                    half.y += entry.Route.StepClearance * .5f;
                    var rotation = Quaternion.LookRotation(V(pose.Forward), V(pose.Up));
                    return new Volume(V(pose.Position) + V(pose.Up) * half.y, half, rotation);
                })).Concat(module.Approaches.Select(PrismBounds)).Concat(fullHeight).ToArray();
            }

            // Approach blocks are reserved as a whole. Their enclosing bounds conservatively
            // cover the few non-rectangular approach blocks around composed transitions.
            private static Volume PrismBounds(Block block)
            {
                var (min, max) = block.Bounds;
                return new Volume(V((min + max) / 2), V((max - min) / 2), Quaternion.identity);
            }

            internal string DeckAt(Vector3 point, Vector3 up)
                => DeckAt(point, up, false);

            private string DeckAt(Vector3 point, Vector3 up, bool onRibbon)
            {
                var hit = Hit(point + up * .045f, -up, .10f);
                return hit.collider && (onRibbon ? hit.collider.name == MobiusHall.Ribbon : decks.Contains(hit.collider.name)) &&
                    Vector3.Dot(hit.normal, frame.TransformDirection(up)) >= .95f ? hit.collider.name : null;
            }

            internal bool Fits(Site site)
            {
                var item = site.Item;
                if (reserved.Any(box => box.Intersects(item)) || Overlaps(item).Any() || EnclosedBySolid(item.Center)) return false;
                Vector3 size = site.Dimensions;
                foreach (float x in new[] { -.49f, 0, .49f })
                foreach (float z in new[] { -.49f, 0, .49f })
                {
                    Vector3 sample = site.Bottom + site.Right * (x * size.x) + site.Forward * (z * size.z);
                    var hit = Hit(sample + site.Up * .04f, -site.Up, .075f);
                    if (!hit.collider || hit.collider.name != site.Support ||
                        Vector3.Dot(hit.normal, frame.TransformDirection(site.Up)) < .98f) return false;
                    if (site.OnRibbon && Mathf.Abs(hit.distance - .04f) > .0005f) return false;
                }
                return true;
            }

            internal void VerifyRibbonCenterLane(IReadOnlyList<Volume> accepted)
            {
                foreach (float lap in new[] { 0f, 1f })
                for (int section = -8; section <= 8; section++)
                {
                    var pose = MobiusHall.Surface(lap + section / (float)MobiusHall.RibbonSections, 0);
                    if (!CanStand(V(pose.Position), V(pose.Up), accepted, true))
                        throw new InvalidOperationException("A loaded loot slot obstructs the ribbon's center walking lane.");
                }
            }

            // Floor and wall solids use triangle colliders, which may miss a fully enclosed item.
            // Test source convex solids here. Concave surfaces use the physics clearance probes.
            private bool EnclosedBySolid(Vector3 center)
            {
                foreach (var (block, bounds) in solids)
                {
                    if (!bounds.Contains(center)) continue;
                    bool inside = true;
                    Vector3 interior = block.Vertices.Select(V).Aggregate(Vector3.zero, (sum, point) => sum + point) / block.Vertices.Length;
                    for (int i = 0; i < block.Faces.Length; i += 3)
                    {
                        Vector3 a = V(block.Vertices[block.Faces[i]]);
                        Vector3 b = V(block.Vertices[block.Faces[i + 1]]);
                        Vector3 c = V(block.Vertices[block.Faces[i + 2]]);
                        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                        if (Vector3.Dot(normal, interior - a) > 0) normal = -normal;
                        if (Vector3.Dot(normal, center - a) <= .001f) continue;
                        inside = false;
                        break;
                    }
                    if (inside) return true;
                }
                return false;
            }

            internal bool CanRetrieve(Site site, IReadOnlyList<Volume> accepted, out Vector3 stance, out string front)
            {
                stance = default;
                front = null;
                if (site.Support == SphericalChamber.Shell)
                    return CanRetrieveFromShell(site, accepted, out stance);
                Vector3 deck = site.OnRibbon ? site.Bottom :
                    V(module.Floors.Single(floor => floor.Id == site.Floor).Pose.Position);
                Vector3 point = site.Bottom - site.Up * Vector3.Dot(site.Bottom - deck, site.Up);
                if (site.Cabinet == null)
                    return FindReach(site, point, Stances(site, point, accepted), accepted, out stance);

                string caseName = site.Cabinet.Name(site.Case);
                var doors = site.Cabinet.MovingFronts.Where(leaf => leaf.Travel == 0 &&
                    (leaf.Panel == site.Case + "-door" || leaf.Panel == site.Case + "-left-door" ||
                        leaf.Panel == site.Case + "-right-door")).ToArray();
                var hiddenBy = Hit(site.Item.Center - site.Forward * 1.1f, site.Forward, 1.1f).collider;
                if (!hiddenBy || !doors.Any(door => site.Cabinet.Name(door.Panel) == hiddenBy.name)) return false;
                front = hiddenBy.name;
                var stands = Stances(site, point, accepted).ToList();
                if (stands.Count == 0) return false;

                var moving = doors.SelectMany(door => new[] { door.Panel, door.Handle }.Select(part =>
                {
                    var node = colliders.Single(value => value.name == site.Cabinet.Name(part)).transform;
                    return (Node: node, Position: node.position, Rotation: node.rotation,
                        Pivot: frame.TransformPoint(V(site.Cabinet.Point(door.Pivot))), door.Angle);
                })).ToArray();
                Vector3 axis = frame.TransformDirection(site.Up);
                try
                {
                    // Match the existing hinge parameters. Test the supports at rest, each
                    // independently opening leaf, and the retrieval path with all leaves open.
                    for (int step = 1; step <= 20; step++)
                    {
                        foreach (var part in moving)
                        {
                            Quaternion rotation = Quaternion.AngleAxis(part.Angle * step / 20, axis);
                            part.Node.SetPositionAndRotation(part.Pivot + rotation * (part.Position - part.Pivot), rotation * part.Rotation);
                        }
                        Physics.SyncTransforms();
                        if (Overlaps(site.Item).Any()) return false;
                        stands.RemoveAll(feet => Overlaps(Stance(feet, site.Up)).Any());
                        if (stands.Count == 0) return false;
                    }
                    return FindReach(site, point, stands, accepted, out stance);
                }
                finally
                {
                    foreach (var part in moving) part.Node.SetPositionAndRotation(part.Position, part.Rotation);
                    Physics.SyncTransforms();
                }
            }

            private IEnumerable<Vector3> Stances(Site site, Vector3 point, IReadOnlyList<Volume> accepted)
            {
                foreach (float ahead in new[] { .39f, .58f, .78f, .97f })
                foreach (float side in new[] { 0, -.23f, .23f, -.47f, .47f })
                {
                    Vector3 feet = point - site.Forward * (site.Dimensions.z * .5f + ahead) + site.Right * side;
                    if (!CanStand(feet, site.Up, accepted, site.OnRibbon) || Stance(feet, site.Up).Intersects(site.Item))
                        continue;
                    bool connected = site.OnRibbon
                        ? ClearWalk(feet, site.RibbonLaneCenter, site.Up, accepted, site.Item, true)
                        : ConnectsToRoute(feet, site.Up, accepted, site.Item);
                    if (connected) yield return feet;
                }
            }

            private bool FindReach(Site site, Vector3 point, IEnumerable<Vector3> stands,
                IReadOnlyList<Volume> accepted, out Vector3 stance)
            {
                stance = default;
                Vector3 target = site.Item.Center - site.Forward * site.Dimensions.z * .38f;
                float hand = Mathf.Clamp(Vector3.Dot(target - point, site.Up), .40f, 1.86f);
                foreach (Vector3 feet in stands)
                {
                    Vector3 reach = feet + site.Up * hand;
                    Vector3 path = target - reach;
                    if (path.magnitude > 1.25f || Hit(reach, path.normalized, path.magnitude).collider) continue;
                    var reachVolume = new Volume((reach + target) * .5f, new Vector3(.02f, .02f, path.magnitude * .5f),
                        Quaternion.LookRotation(path, site.Up));
                    if (accepted.Any(volume => volume.Intersects(reachVolume))) continue;
                    stance = feet;
                    return true;
                }
                return false;
            }

            private bool ConnectsToRoute(Vector3 feet, Vector3 up, IReadOnlyList<Volume> accepted, Volume item)
            {
                var nearby = walkEntries.Where(entry => Vector3.Dot(entry.Up, up) > .999f &&
                    Mathf.Abs(Vector3.Dot(entry.Feet - feet, up)) < .035f)
                    .OrderBy(entry => (entry.Feet - feet).sqrMagnitude).Where(entry => (entry.Feet - feet).sqrMagnitude < 100);
                foreach (var entry in nearby)
                    if (ClearWalk(feet, entry.Feet, up, accepted, item)) return true;
                return false;
            }

            private bool ClearWalk(Vector3 start, Vector3 end, Vector3 up, IReadOnlyList<Volume> accepted,
                Volume item, bool onRibbon = false)
            {
                int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) / .22f));
                for (int i = 1; i <= count; i++)
                {
                    Vector3 step = Vector3.Lerp(start, end, i / (float)count);
                    if (Stance(step, up).Intersects(item) || !CanStand(step, up, accepted, onRibbon)) return false;
                }
                return true;
            }

            internal static Volume Stance(Vector3 feet, Vector3 up)
            {
                Vector3 forward = Mathf.Abs(Vector3.Dot(up, Vector3.forward)) > .9f ? Vector3.right : Vector3.forward;
                return new Volume(feet + up * (PlayerHeight / 2 + .025f),
                    new Vector3(PlayerRadius, PlayerHeight / 2, PlayerRadius), Quaternion.LookRotation(forward, up));
            }

            private bool CanStand(Vector3 feet, Vector3 up, IReadOnlyList<Volume> accepted, bool onRibbon)
            {
                var stance = Stance(feet, up);
                if (accepted.Any(volume => volume.Intersects(stance)) || Overlaps(stance).Any()) return false;
                foreach (Vector3 offset in new[] { Vector3.zero, stance.Right * .20f, -stance.Right * .20f,
                    stance.Forward * .20f, -stance.Forward * .20f })
                    if (DeckAt(feet + offset, up, onRibbon) == null) return false;
                return true;
            }

            private IEnumerable<Collider> Overlaps(Volume volume) => Physics.OverlapBox(frame.TransformPoint(volume.Center),
                volume.Half - Vector3.one * .001f, frame.rotation * volume.Rotation, ~0, QueryTriggerInteraction.Ignore)
                .Where(colliders.Contains);

            private RaycastHit Hit(Vector3 origin, Vector3 direction, float distance) => Physics.RaycastAll(
                frame.TransformPoint(origin), frame.TransformDirection(direction), distance, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => colliders.Contains(hit.collider)).OrderBy(hit => hit.distance).FirstOrDefault();
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Contour4.Procedural.Navigation
{
    public enum RaidRouteKind { Local, PhysicalPassage, Portal, OrientedDeck, Stairs }
    public enum RaidSightKind { None, Direct, Portal }

    [DisallowMultipleComponent]
    public sealed partial class RaidNavigation : MonoBehaviour
    {
        private readonly Dictionary<RaidFloor, RaidNavSurface> surfaces = new();
        private readonly Dictionary<RaidFloor, RaidNavSurface> referenceSurfaces = new();
        private readonly Dictionary<RaidFloor, List<Edge>> outgoing = new();
        private readonly List<RaidTarget> targets = new();
        private readonly List<RaidEnemyChaser> enemies = new();
        private readonly Collider[] probe = new Collider[1];
        private readonly Collider[] portalOverlaps = new Collider[16];
        private readonly RaycastHit[] portalHits = new RaycastHit[16];
        private RaidLayout layout;
        private PhysicsScene physics;

        public bool Ready => layout != null;
        public int BakedFloorCount => surfaces.Count;
        internal IReadOnlyList<RaidTarget> Targets => targets;

        // Install after placing all colliders. A raid root owns its meshes and registrations.
        public void Install(RaidLayout placedLayout)
        {
            if (placedLayout == null)
                throw new ArgumentNullException(nameof(placedLayout));
            if (!isActiveAndEnabled || Ready)
                throw new InvalidOperationException("Install once on an active raid navigation component.");
            physics = gameObject.scene.GetPhysicsScene();
            if (!physics.IsValid())
                throw new InvalidOperationException("The raid has no physics scene.");

            var settings = NavMesh.GetSettingsByID(0);
            if (settings.agentTypeID == -1)
                throw new InvalidOperationException("The built-in humanoid NavMesh type is unavailable.");
            settings.agentRadius = placedLayout.BakeRadius;
            settings.agentHeight = placedLayout.BakeHeight;
            settings.agentClimb = .15f;
            settings.agentSlope = 35;
            settings.overrideVoxelSize = true;
            settings.voxelSize = Mathf.Clamp(settings.agentRadius / 3f, .05f, .15f);
            settings.minRegionArea = 0.1f;

            Physics.SyncTransforms();
            float shelf = 0;
            var identities = new HashSet<(string Room, string Floor, string Island)>();
            var apertures = new HashSet<RaidAperture>();
            try
            {
                foreach (var floor in placedLayout.Floors)
                {
                    if (floor == null || !identities.Add((floor.RoomId, floor.FloorId, floor.IslandId)))
                        throw new ArgumentException("Each placed navigation floor needs a unique identity.");

                    // Build in gravity-local coordinates. Separate the query tiles horizontally, with no links.
                    var localSettings = settings;
                    if (floor.Prepared)
                    {
                        localSettings.agentClimb = .40f;
                        localSettings.agentSlope = 60f;
                        // Match the verified Mobius authoring grid at its narrow floor apron.
                        localSettings.voxelSize = floor.RoomId.StartsWith("mobius-hall-", StringComparison.Ordinal) ? .075f : .12f;
                    }
                    surfaces.Add(floor, Bake(floor, localSettings));
                    if (floor.Prepared)
                    {
                        localSettings.agentRadius = .35f;
                        localSettings.agentHeight = 1.80f;
                        localSettings.agentClimb = .30f;
                        localSettings.agentSlope = 45f;
                        localSettings.voxelSize = .08f;
                        referenceSurfaces.Add(floor, Bake(floor, localSettings));
                    }
                    outgoing.Add(floor, new List<Edge>());
                }
                foreach (var portal in placedLayout.Portals)
                {
                    if (portal == null || !apertures.Add(portal.A) || !apertures.Add(portal.B) ||
                        !outgoing.ContainsKey(portal.A.Floor) || !outgoing.ContainsKey(portal.B.Floor))
                        throw new ArgumentException("Each unique portal aperture must belong to a placed floor.");
                    var forward = Edge.ForPortal(portal.A, portal.B, portal.AtoB);
                    var reverse = Edge.ForPortal(portal.B, portal.A, portal.BtoA);
                    forward.Reverse = reverse;
                    reverse.Reverse = forward;
                    outgoing[forward.From].Add(forward);
                    outgoing[reverse.From].Add(reverse);
                }
                foreach (var passage in placedLayout.Passages)
                {
                    if (passage == null || !outgoing.ContainsKey(passage.From) || !outgoing.ContainsKey(passage.To))
                        throw new ArgumentException("Each physical passage must connect placed floors.");
                    outgoing[passage.From].Add(Edge.ForPassage(passage));
                }
                foreach (var deck in placedLayout.Decks)
                {
                    if (deck == null || deck.Count < 2 || !outgoing.ContainsKey(deck.From) || !outgoing.ContainsKey(deck.To))
                        throw new ArgumentException("Each oriented deck must connect two placed floors.");
                    outgoing[deck.From].Add(Edge.ForDeck(deck));
                }
                layout = placedLayout;
            }
            catch
            {
                Close();
                throw;
            }

            RaidNavSurface Bake(RaidFloor floor, NavMeshBuildSettings floorSettings)
            {
                var offset = placedLayout.QueryOrigin + Vector3.right * shelf - floor.LocalBakeBounds.min;
                shelf += floor.LocalBakeBounds.size.x + 8f;
                return new RaidNavSurface(floor, offset, floorSettings, gameObject.scene, placedLayout.SolidMask);
            }
        }

        public void Close()
        {
            layout = null;
            while (enemies.Count > 0)
            {
                int last = enemies.Count - 1;
                var enemy = enemies[last];
                enemies.RemoveAt(last);
                if (enemy)
                    enemy.Stop();
            }
            while (targets.Count > 0)
            {
                int last = targets.Count - 1;
                var target = targets[last];
                targets.RemoveAt(last);
                if (target)
                    target.Unbind();
            }
            foreach (var surface in surfaces.Values)
                surface.Dispose();
            surfaces.Clear();
            foreach (var surface in referenceSurfaces.Values)
                surface.Dispose();
            referenceSurfaces.Clear();
            outgoing.Clear();
        }

        private void OnDisable() => Close();
        private void OnDestroy() => Close();
        internal bool Includes(RaidFloor floor) => Ready && floor != null && surfaces.ContainsKey(floor);
        internal void Register(RaidTarget target) => targets.Add(target);
        internal void Unregister(RaidTarget target)
        {
            targets.Remove(target);
            foreach (var enemy in enemies)
            {
                if (enemy)
                    enemy.TargetRemoved(target);
            }
        }
        internal void Register(RaidEnemyChaser enemy) => enemies.Add(enemy);
        internal void Unregister(RaidEnemyChaser enemy) => enemies.Remove(enemy);
        internal bool Supports(RaidEnemyProfile profile) => Ready && profile != null &&
            profile.Radius <= layout.BakeRadius && profile.Height <= layout.BakeHeight;

        public RaidSightKind Sight(RaidFloor observerFloor, Vector3 eye, Vector3 forward, RaidEnemyProfile profile,
            RaidTarget player) => Sight(observerFloor, eye, forward, profile, player, null);

        internal RaidSightKind Sight(RaidFloor observerFloor, Vector3 eye, Vector3 forward, RaidEnemyProfile profile,
            RaidTarget player, Rigidbody observerBody)
        {
            if (!Includes(observerFloor) || !player || player.Owner != this || !player.HasSightPoint || profile == null ||
                !RaidGeometry.Finite(eye) || !RaidGeometry.Finite(forward) || forward.sqrMagnitude < .000001f)
                return RaidSightKind.None;

            var target = player.SightPoint;
            if (!RaidGeometry.Finite(target))
                return RaidSightKind.None;
            var links = outgoing[observerFloor];
            if (player.Floor == observerFloor && InSight(target - eye, forward, profile) && RayIsClear(eye, target))
                return RaidSightKind.Direct;

            foreach (var edge in links)
            {
                if (edge.To != player.Floor)
                    continue;
                if ((edge.Kind is RaidRouteKind.PhysicalPassage or RaidRouteKind.OrientedDeck ||
                    edge.Kind == RaidRouteKind.Stairs && profile.MaximumStepHeight > 0f) &&
                    InSight(target - eye, forward, profile) && RayIsClear(eye, target))
                    return RaidSightKind.Direct;
                if ((edge.Use & RaidPortalUse.Vision) != 0 &&
                    SeesThrough(edge, eye, forward, profile, target, observerBody))
                    return RaidSightKind.Portal;
            }
            return RaidSightKind.None;
        }

        private static bool InSight(Vector3 delta, Vector3 forward, RaidEnemyProfile profile)
        {
            return delta.sqrMagnitude <= profile.SightRange * profile.SightRange &&
                (delta.sqrMagnitude < .000001f || Vector3.Dot(delta.normalized, forward.normalized) >=
                    Mathf.Cos(profile.SightAngle * .5f * Mathf.Deg2Rad) - 1e-5f);
        }

        private bool SeesThrough(Edge edge, Vector3 eye, Vector3 forward, RaidEnemyProfile profile, Vector3 target,
            Rigidbody observerBody)
        {
            var entry = edge.Entry;
            var exit = edge.Exit;
            const float planeMargin = .005f;
            float eyeDepth = entry.Local(eye).z;
            if (eyeDepth >= -planeMargin || exit.Local(target).z >= -planeMargin)
                return false;

            Vector3 towardVirtualTarget = edge.Reverse.Map(target) - eye;
            float depthDelta = Vector3.Dot(towardVirtualTarget, entry.Normal);
            if (depthDelta <= -eyeDepth || !InSight(towardVirtualTarget, forward, profile))
                return false;
            Vector3 apertureHit = eye - towardVirtualTarget * (eyeDepth / depthDelta);
            Vector3 otherHit = edge.Map(apertureHit);
            return entry.Contains(apertureHit) && exit.Contains(otherHit) &&
                PortalEntryIsClear(eye, apertureHit, observerBody) && RayIsClear(otherHit, target);
        }

        private bool PortalEntryIsClear(Vector3 from, Vector3 to, Rigidbody observerBody)
        {
            if (!observerBody) return RayIsClear(from, to);
            if (HasForeignSightOverlap(from, observerBody) || HasForeignSightOverlap(to, observerBody)) return false;
            Vector3 delta = to - from;
            int count = physics.Raycast(from, delta.normalized, portalHits, delta.magnitude,
                layout.SolidMask, QueryTriggerInteraction.Ignore);
            return !HasForeignHit(count, observerBody);
        }

        private bool HasForeignHit(int count, Rigidbody traveller)
        {
            // A full buffer may have omitted another solid farther along the query.
            if (count == portalHits.Length) return true;
            for (int index = 0; index < count; index++)
            {
                var collider = portalHits[index].collider;
                if (!collider || collider.attachedRigidbody != traveller) return true;
            }
            return false;
        }

        private bool HasForeignSightOverlap(Vector3 point, Rigidbody observerBody)
        {
            int count = physics.OverlapSphere(point, .002f, portalOverlaps, layout.SolidMask, QueryTriggerInteraction.Ignore);
            return HasForeignOverlap(count, observerBody);
        }

        private bool HasForeignOverlap(int count, Rigidbody traveller)
        {
            if (count == portalOverlaps.Length) return true;
            for (int index = 0; index < count; index++)
            {
                var collider = portalOverlaps[index];
                if (!collider || collider.attachedRigidbody != traveller) return true;
            }
            return false;
        }

        private bool RayIsClear(Vector3 from, Vector3 to)
        {
            const float pointRadius = .002f;
            var delta = to - from;
            return physics.OverlapSphere(from, pointRadius, probe, layout.SolidMask, QueryTriggerInteraction.Ignore) == 0 &&
                !physics.Raycast(from, delta.normalized, delta.magnitude, layout.SolidMask, QueryTriggerInteraction.Ignore) &&
                physics.OverlapSphere(to, pointRadius, probe, layout.SolidMask, QueryTriggerInteraction.Ignore) == 0;
        }

        internal bool TryStand(RaidFloor floor, Vector3 feet, out Vector3 supported)
            => TryStand(floor, feet, null, out supported);

        internal bool TryStand(RaidFloor floor, Vector3 feet, RaidEnemyProfile profile, out Vector3 supported)
        {
            supported = default;
            return Includes(floor) && RaidGeometry.Finite(feet) && Surface(floor, profile).TrySample(feet, out supported) &&
                HasFloor(floor, supported);
        }

        private RaidNavSurface Surface(RaidFloor floor, RaidEnemyProfile profile) =>
            profile != null && profile.Radius <= .35f && profile.Height <= 1.80f &&
                referenceSurfaces.TryGetValue(floor, out var reference) ? reference : surfaces[floor];

        // Player feet can stand inside the baked capsule's margin along a wall. Only pursuit goals use this projection.
        internal bool TryProjectPursuitGoal(RaidFloor floor, Vector3 feet, RaidEnemyProfile profile, out Vector3 goal)
        {
            goal = default;
            if (!Includes(floor) || !RaidGeometry.Finite(feet) ||
                !Surface(floor, profile).TrySample(feet, layout.BakeRadius + .16f, out goal) ||
                Mathf.Abs(Vector3.Dot(goal - feet, floor.Up)) > .16f ||
                !BodyClear(goal, floor.Up, profile) || !SegmentHasFloor(floor, null, feet, goal))
                return false;

            var rayHeight = floor.Up * .18f;
            return RayIsClear(feet + rayHeight, goal + rayHeight);
        }

        private bool HasFloor(RaidFloor floor, Vector3 feet, RaidFloor neighbor = null)
        {
            Vector3 up = floor.Up;
            Vector3 origin = feet + up * .14f;
            return physics.Raycast(origin, -up, out var hit, .3f, layout.SolidMask, QueryTriggerInteraction.Ignore) &&
                RaidGeometry.Supports(physics, hit, origin, up, .8f, layout.SolidMask, floor.Support, neighbor?.Support, portalHits);
        }

        public bool TryGetRouteKind(RaidFloor from, Vector3 start, RaidFloor to, Vector3 goal,
            RaidEnemyProfile profile, out RaidRouteKind kind)
        {
            kind = default;
            if (!TryPlan(from, start, to, goal, profile, null, out var plan))
                return false;
            kind = plan.Edge?.Kind ?? RaidRouteKind.Local;
            return true;
        }

        internal bool TryPlan(RaidFloor from, Vector3 start, RaidFloor to, Vector3 goal, RaidEnemyProfile profile,
            Edge blocked, out Plan result, Rigidbody portalTraveller = null)
        {
            result = default;
            if (!Supports(profile) || !TryStand(from, start, profile, out _) || !TryStand(to, goal, profile, out _))
                return false;
            var queue = new Queue<SearchNode>();
            var reached = new HashSet<Edge>();
            queue.Enqueue(new SearchNode(from, start, default));
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (node.Floor == to && Surface(to, profile).TryPath(node.Position, goal, out var path))
                {
                    result = node.First.Edge == null ? new Plan(path, null) : node.First;
                    return true;
                }
                foreach (var edge in outgoing[node.Floor])
                {
                    if (reached.Contains(edge) || edge == blocked ||
                        !TryTransition(edge, profile, out var approach, out var landing, portalTraveller) ||
                        !Surface(node.Floor, profile).TryPath(node.Position, approach, out var local))
                        continue;
                    reached.Add(edge);
                    var first = node.First.Edge == null ? new Plan(local, edge) : node.First;
                    queue.Enqueue(new SearchNode(edge.To, landing, first));
                }
            }
            return false;
        }

        internal bool TryTransition(Edge edge, RaidEnemyProfile profile, out Vector3 approach, out Vector3 landing,
            Rigidbody portalTraveller = null) =>
            TryTransition(edge, profile, out approach, out landing, out _, portalTraveller);

        internal bool TryTransition(Edge edge, RaidEnemyProfile profile, out Vector3 approach, out Vector3 landing,
            out Vector3[] stairCourse, Rigidbody portalTraveller = null)
        {
            approach = landing = default;
            stairCourse = null;
            if (edge.Kind == RaidRouteKind.PhysicalPassage)
                return TryStand(edge.From, edge.Approach, profile, out approach) &&
                    TryStand(edge.To, edge.Landing, profile, out landing) && PhysicalIsClear(edge, approach, landing, profile);

            if (edge.Kind == RaidRouteKind.Stairs)
                return profile.MaximumStepHeight > 0f &&
                    TryStand(edge.From, edge.Approach, profile, out approach) &&
                    TryStand(edge.To, edge.Landing, profile, out landing) &&
                    TryStairPath(edge.Deck, approach, landing, profile, out stairCourse);

            if (edge.Kind == RaidRouteKind.OrientedDeck)
            {
                if (!profile.Reorients || !TryStand(edge.From, edge.Approach, profile, out approach) ||
                    !TryStand(edge.To, edge.Landing, profile, out landing)) return false;
                for (int i = 0; i < edge.Deck.Count; i++)
                    if (!DeckIsClear(edge.Deck, edge.Deck.World(i), profile)) return false;
                return true;
            }

            if (!profile.WalksPortals || (edge.Use & RaidPortalUse.Walking) == 0 ||
                (!profile.Reorients && Vector3.Dot(edge.From.Up, edge.To.Up) < .999f) ||
                !TryStand(edge.From, edge.Approach, profile, out approach) || !TryStand(edge.To, edge.Landing, profile, out landing) ||
                !PortalPad(edge.Entry, approach, profile, out var entryThreshold) ||
                !PortalPad(edge.Exit, landing, profile, out var exitThreshold) ||
                Vector3.Distance(edge.Map(entryThreshold), exitThreshold) > .18f)
                return false;
            entryThreshold -= edge.Entry.Normal * edge.Entry.WalkingInset;
            exitThreshold -= edge.Exit.Normal * edge.Exit.WalkingInset;
            return PortalEntryTravel(approach, entryThreshold, edge.From.Up, profile, portalTraveller) &&
                BodyTravel(exitThreshold, landing, edge.To.Up, profile) &&
                SegmentHasFloor(edge.From, null, approach, entryThreshold - edge.Entry.Normal * .04f) &&
                SegmentHasFloor(edge.To, null, landing, exitThreshold - edge.Exit.Normal * .04f);
        }

        private static bool PortalPad(RaidAperture end, Vector3 feet, RaidEnemyProfile profile, out Vector3 threshold)
        {
            var local = end.Local(feet);
            threshold = feet - local.z * end.Normal;
            float halfHeight = end.Size.y * .5f;
            return local.z <= -profile.Radius - RaidGeometry.Skin && local.z >= -1.5f &&
                Mathf.Abs(local.x) + profile.Radius + RaidGeometry.Skin <= end.Size.x * .5f &&
                local.y >= -halfHeight - .055f &&
                local.y + profile.Height + RaidGeometry.Skin <= halfHeight;
        }

        internal bool PhysicalIsClear(Edge edge, Vector3 from, Vector3 to, RaidEnemyProfile profile)
        {
            return Mathf.Abs(Vector3.Dot(to - from, edge.From.Up)) <= .12f &&
                BodyTravel(from, to, edge.From.Up, profile) && SegmentHasFloor(edge.From, edge.To, from, to);
        }

        internal bool DeckIsClear(RaidDeckPassage deck, Pose sample, RaidEnemyProfile profile)
        {
            Vector3 up = sample.up;
            return BodyClear(sample.position, up, profile) &&
                physics.SphereCast(sample.position + up * .115f, .045f, -up, out var hit, .26f, layout.SolidMask,
                    QueryTriggerInteraction.Ignore) && deck.Supports.Contains(hit.collider) &&
                Vector3.Dot(hit.normal, up) >= .66f;
        }

        private bool SegmentHasFloor(RaidFloor first, RaidFloor second, Vector3 from, Vector3 to)
        {
            Vector3 segment = to - from;
            int samples = Mathf.Max(1, Mathf.CeilToInt(segment.magnitude / .075f));
            Vector3 seamOffset = segment.normalized * .00015f;
            for (int index = 0; index <= samples; index++)
            {
                var point = Vector3.Lerp(from, to, index / (float)samples);
                if (Supported(point)) continue;
                // A ray exactly on the common boundary of two oriented meshes can miss both.
                // Require genuine contact on both sides of that sample and keep the capsule sweep.
                if (second == null || index == 0 || index == samples ||
                    !Supported(point - seamOffset) || !Supported(point + seamOffset)) return false;
            }
            return true;

            bool Supported(Vector3 point) => HasFloor(first, point, second) ||
                (second != null && HasFloor(second, point, first));
        }

        internal bool BodyClear(Vector3 feet, Vector3 up, RaidEnemyProfile profile)
        {
            Capsule(feet, up, profile, out var lower, out var upper);
            return physics.OverlapCapsule(lower, upper, profile.Radius, probe, layout.SolidMask,
                QueryTriggerInteraction.Ignore) == 0;
        }

        internal bool BodyTravel(Vector3 from, Vector3 to, Vector3 up, RaidEnemyProfile profile)
        {
            if (!BodyClear(from, up, profile) || !BodyClear(to, up, profile))
                return false;
            var delta = to - from;
            Capsule(from, up, profile, out var lower, out var upper);
            return delta.sqrMagnitude < .000001f || !physics.CapsuleCast(lower, upper, profile.Radius, delta.normalized,
                out _, delta.magnitude, layout.SolidMask, QueryTriggerInteraction.Ignore);
        }

        private bool PortalEntryTravel(Vector3 from, Vector3 to, Vector3 up, RaidEnemyProfile profile, Rigidbody traveller)
        {
            if (!traveller) return BodyTravel(from, to, up, profile);
            Capsule(from, up, profile, out var lower, out var upper);
            var delta = to - from;
            int atStart = physics.OverlapCapsule(lower, upper, profile.Radius, portalOverlaps,
                layout.SolidMask, QueryTriggerInteraction.Ignore);
            if (HasForeignOverlap(atStart, traveller)) return false;
            int atEnd = physics.OverlapCapsule(lower + delta, upper + delta, profile.Radius, portalOverlaps,
                layout.SolidMask, QueryTriggerInteraction.Ignore);
            if (HasForeignOverlap(atEnd, traveller)) return false;
            if (delta.sqrMagnitude < .000001f) return true;
            int hits = physics.CapsuleCast(lower, upper, profile.Radius, delta.normalized, portalHits,
                delta.magnitude, layout.SolidMask, QueryTriggerInteraction.Ignore);
            return !HasForeignHit(hits, traveller);
        }

        private static void Capsule(Vector3 feet, Vector3 up, RaidEnemyProfile profile, out Vector3 lower,
            out Vector3 upper)
        {
            lower = feet + up * (profile.Radius + RaidGeometry.Skin);
            upper = feet + up * (profile.Height - profile.Radius + RaidGeometry.Skin);
        }

        internal sealed class Edge
        {
            internal RaidFloor From { get; private set; }
            internal RaidFloor To { get; private set; }
            private RaidWalkPassage Passage { get; set; }
            internal RaidDeckPassage Deck { get; private set; }
            internal Vector3 Approach => Passage?.Approach ?? Deck?.World(0).position ?? Entry.Approach;
            internal Vector3 Landing => Passage?.Landing ?? Deck?.World(Deck.Count - 1).position ?? Exit.Approach;
            internal RaidRouteKind Kind { get; private set; }
            internal RaidAperture Entry { get; private set; }
            internal RaidAperture Exit { get; private set; }
            internal RaidPortalUse Use { get; private set; }
            internal Edge Reverse { get; set; }
            internal Quaternion Rotation => Exit.Frame.rotation * Quaternion.AngleAxis(180, Vector3.up) *
                Quaternion.Inverse(Entry.Frame.rotation);

            internal static Edge ForPortal(RaidAperture from, RaidAperture to, RaidPortalUse use) => new Edge
            {
                From = from.Floor, To = to.Floor, Kind = RaidRouteKind.Portal, Entry = from, Exit = to, Use = use
            };

            internal static Edge ForPassage(RaidWalkPassage passage) => new Edge
            {
                From = passage.From, To = passage.To, Passage = passage,
                Kind = RaidRouteKind.PhysicalPassage
            };

            internal static Edge ForDeck(RaidDeckPassage deck) => new Edge
            {
                From = deck.From, To = deck.To, Deck = deck,
                Kind = deck.Stepped ? RaidRouteKind.Stairs : RaidRouteKind.OrientedDeck
            };

            internal Vector3 Map(Vector3 position) => Exit.Frame.position + Rotation * (position - Entry.Frame.position);
        }

        internal readonly struct Plan
        {
            internal Plan(Vector3[] corners, Edge edge) { Corners = corners; Edge = edge; }
            internal Vector3[] Corners { get; }
            internal Edge Edge { get; }
        }

        private readonly struct SearchNode
        {
            internal SearchNode(RaidFloor floor, Vector3 position, Plan first)
            {
                Floor = floor;
                Position = position;
                First = first;
            }
            internal RaidFloor Floor { get; }
            internal Vector3 Position { get; }
            internal Plan First { get; }
        }
    }
}

using System;
using UnityEngine;

namespace Contour4.Procedural.Navigation
{
    [DisallowMultipleComponent]
    public sealed class RaidEnemyChaser : MonoBehaviour
    {
        private const float ThinkInterval = .18f;
        private const float ArrivalDistance = .05f;
        private const float ReturnDelay = .75f;
        private const float MotionEpsilon = .001f;
        private RaidNavigation raid;
        private RaidEnemyProfile profile;
        private Func<bool> hasAuthority;
        private Func<Pose, bool> writePose;
        private RaidNavigation.Plan plan;
        private int nextCorner;
        private float thinkIn;
        private float remembersFor;
        private RaidFloor lastSeenFloor;
        private Vector3 lastSeenGoal;
        private RaidNavigation.Edge blockedReturn;
        private float returnsIn;
        private RaidNavigation.Edge passage;
        private Vector3 passageLanding;
        private int deckSample;

        public RaidFloor Floor { get; private set; }
        public RaidTarget Target { get; private set; }
        public RaidSightKind LastSight { get; private set; }
        public int PortalCrossings { get; private set; }
        private bool HasRememberedGoal => Target && Target.Owner == raid && lastSeenFloor != null && remembersFor > 0;

        // The actor root is its feet pose. Its gameplay colliders are outside the raid's solid query mask.
        // Any alternate writer applies the entire body pose synchronously and returns false without mutation on failure.
        public void Begin(RaidNavigation navigation, RaidFloor spawnFloor, RaidEnemyProfile enemy,
            Func<bool> authority, Func<Pose, bool> poseWriter = null)
        {
            if (raid || !isActiveAndEnabled || !navigation || authority == null ||
                !navigation.Supports(enemy) || !navigation.TryStand(spawnFloor, transform.position, enemy, out _) ||
                Vector3.Dot(transform.up, spawnFloor.Up) < .999f ||
                !navigation.BodyClear(transform.position, spawnFloor.Up, enemy))
                throw new InvalidOperationException("The enemy needs a supported unoccupied spawn, matching up and authority.");
            if (poseWriter == null && TryGetComponent<Rigidbody>(out var body) && !body.isKinematic)
                throw new InvalidOperationException("A dynamic body requires a synchronous pose writer.");

            raid = navigation;
            Floor = spawnFloor;
            profile = enemy;
            hasAuthority = authority;
            writePose = poseWriter ?? WriteTransformPose;
            PortalCrossings = 0;
            raid.Register(this);
        }

        public void Stop()
        {
            var owner = raid;
            raid = null;
            profile = null;
            hasAuthority = null;
            writePose = null;
            Floor = null;
            passage = null;
            blockedReturn = null;
            returnsIn = 0;
            thinkIn = 0;
            Forget();
            if (owner)
                owner.Unregister(this);
        }

        private void OnDisable() => Stop();
        private void OnDestroy() => Stop();
        private void FixedUpdate() => Advance(Time.fixedDeltaTime);
        internal void TargetRemoved(RaidTarget target)
        {
            if (ReferenceEquals(Target, target))
            {
                Forget();
                thinkIn = 0;
            }
        }

        // Uses the same driver in offline sim and during the raid, with a caller-supplied simulation step.
        public void Advance(float seconds)
        {
            if (!raid || !isActiveAndEnabled || !float.IsFinite(seconds) || seconds <= 0)
                return;
            var owner = raid;
            bool authorized = hasAuthority();
            if (!raid || !ReferenceEquals(raid, owner))
                return;
            if (!authorized)
            {
                Forget();
                thinkIn = 0;
                return;
            }
            if (!ReferenceEquals(Target, null) && (!Target || Target.Owner != raid || !Target.isActiveAndEnabled))
                raid.Unregister(Target);

            thinkIn -= seconds;
            remembersFor -= seconds;
            returnsIn -= seconds;
            if (returnsIn <= 0)
                blockedReturn = null;

            if (passage != null)
            {
                FollowPassage(profile.Speed * seconds);
                return;
            }
            if (thinkIn <= 0)
            {
                Observe();
                RefreshPath();
                thinkIn = ThinkInterval;
            }
            if (HasRememberedGoal)
                FollowPlan(profile.Speed * seconds);
        }

        private void Observe()
        {
            var eye = transform.position + Floor.Up * profile.EyeHeight;
            if (Target && Target.Owner == raid && Target.isActiveAndEnabled)
            {
                var sight = raid.Sight(Floor, eye, transform.forward, profile, Target);
                if (sight != RaidSightKind.None)
                {
                    Remember(Target, sight);
                    return;
                }
                LastSight = RaidSightKind.None;
                if (remembersFor > 0)
                    return;
            }
            Forget();

            RaidTarget nearest = null;
            RaidSightKind nearestSight = RaidSightKind.None;
            float nearestDistance = float.PositiveInfinity;
            foreach (var player in raid.Targets)
            {
                var sight = raid.Sight(Floor, eye, transform.forward, profile, player);
                if (sight == RaidSightKind.None)
                    continue;
                float distance = (player.SightPoint - eye).sqrMagnitude;
                if (distance >= nearestDistance)
                    continue;
                nearest = player;
                nearestSight = sight;
                nearestDistance = distance;
            }
            if (nearest)
                Remember(nearest, nearestSight);
        }

        private void Remember(RaidTarget player, RaidSightKind sight)
        {
            if (Target != player || lastSeenFloor != player.Floor)
                lastSeenFloor = null;
            Target = player;
            LastSight = sight;
            remembersFor = profile.MemorySeconds;
            if (raid.TryProjectPursuitGoal(player.Floor, player.Feet, profile, out var goal))
            {
                lastSeenFloor = player.Floor;
                lastSeenGoal = goal;
            }
        }

        private void Forget()
        {
            Target = null;
            LastSight = RaidSightKind.None;
            lastSeenFloor = null;
            remembersFor = 0;
            plan = default;
            nextCorner = 0;
        }

        private void RefreshPath()
        {
            plan = default;
            nextCorner = 0;
            if (HasRememberedGoal)
                raid.TryPlan(Floor, transform.position, lastSeenFloor, lastSeenGoal, profile, blockedReturn, out plan);
        }

        private void FollowPlan(float distance)
        {
            if (plan.Corners == null)
                return;
            while (nextCorner < plan.Corners.Length && distance > MotionEpsilon)
            {
                Vector3 position = transform.position;
                Vector3 corner = plan.Corners[nextCorner];
                if (Vector3.Distance(position, corner) <= .008f)
                {
                    nextCorner++;
                    continue;
                }

                var next = Vector3.MoveTowards(position, corner, Mathf.Min(distance, .125f));
                if (!raid.TryStand(Floor, next, profile, out var supported) ||
                    !raid.BodyTravel(position, supported, Floor.Up, profile) ||
                    !TryWrite(new Pose(supported, Heading(supported - position, Floor.Up))))
                {
                    plan = default;
                    return;
                }
                if (plan.Corners == null)
                    return;
                var progress = Vector3.Distance(transform.position, position);
                if (progress < MotionEpsilon)
                {
                    plan = default;
                    return;
                }
                distance -= progress;
            }

            if (nextCorner >= plan.Corners.Length && plan.Edge != null)
                EnterTransition(plan.Edge);
        }

        private void EnterTransition(RaidNavigation.Edge edge)
        {
            // Door/prop transforms may have moved since the path was chosen.
            Physics.SyncTransforms();
            bool canCross = HasRememberedGoal &&
                raid.TryPlan(Floor, transform.position, lastSeenFloor, lastSeenGoal, profile, blockedReturn, out var check) &&
                check.Edge == edge;
            plan = default;
            if (!canCross || !raid.TryTransition(edge, profile, out var approach, out var landing) ||
                Vector3.Distance(transform.position, approach) > ArrivalDistance ||
                !raid.BodyTravel(transform.position, approach, Floor.Up, profile))
                return;

            if (edge.Kind is RaidRouteKind.PhysicalPassage or RaidRouteKind.OrientedDeck)
            {
                passage = edge;
                passageLanding = landing;
                deckSample = 0;
                return;
            }
            if (!TryWrite(new Pose(landing, edge.Rotation * transform.rotation)))
                return;

            Floor = edge.To;
            blockedReturn = edge.Reverse;
            returnsIn = ReturnDelay;
            PortalCrossings++;
            thinkIn = 0;
        }

        private void FollowPassage(float distance)
        {
            if (passage.Deck is { } deck)
            {
                FollowDeck(deck, distance);
                return;
            }
            var position = transform.position;
            var next = Vector3.MoveTowards(position, passageLanding, Mathf.Min(distance, .125f));
            if (!raid.PhysicalIsClear(passage, position, next, profile) ||
                !TryWrite(new Pose(next, Heading(next - position, Floor.Up))))
                return;
            if (Vector3.Distance(transform.position, passageLanding) > .008f)
                return;
            Floor = passage.To;
            passage = null;
            thinkIn = 0;
        }

        private void FollowDeck(RaidDeckPassage deck, float distance)
        {
            while (deckSample < deck.Count && distance > MotionEpsilon)
            {
                var target = deck.World(deckSample);
                var position = transform.position;
                float remaining = Vector3.Distance(position, target.position);
                float step = Mathf.Min(distance, .125f, remaining);
                float amount = remaining > MotionEpsilon ? step / remaining : 1f;
                var next = new Pose(Vector3.Lerp(position, target.position, amount),
                    Quaternion.Slerp(transform.rotation, target.rotation, amount));
                if (!raid.DeckIsClear(deck, next, profile) || !TryWrite(next)) return;
                distance -= step;
                if (amount >= 1) deckSample++;
            }
            if (deckSample != deck.Count) return;
            Floor = passage.To;
            passage = null;
            thinkIn = 0;
        }

        private Quaternion Heading(Vector3 delta, Vector3 up)
        {
            var forward = Vector3.ProjectOnPlane(delta, up);
            return forward.sqrMagnitude > .000001f ? Quaternion.LookRotation(forward, up) : transform.rotation;
        }

        private bool TryWrite(Pose pose)
        {
            var owner = raid;
            if (!writePose(pose) || !this || !raid || !ReferenceEquals(owner, raid))
                return false;
            if (Vector3.Distance(transform.position, pose.position) <= .005f &&
                Quaternion.Angle(transform.rotation, pose.rotation) <= .25f)
                return true;
            Stop();
            return false;
        }

        private bool WriteTransformPose(Pose pose)
        {
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            return true;
        }
    }
}

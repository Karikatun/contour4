using System;
using Contour4.Procedural.Navigation;
using UnityEngine;
using UnityEngine.AI;
using Navigation = Contour4.Procedural.Navigation.RaidNavigation;

namespace Contour4.NativeRaid.Enemies
{
    // Headman's serialized NavMeshAgent and the envelope of its two solid, moving-body capsules.
    internal static class StockPortalPursuit
    {
        internal const int AgentType = -334000983;
        internal const float Radius = .86f;
        internal const float Height = 2.86f;
        internal const float PositionTolerance = .12f;
        // R.E.P.O. 0.5.2 TagManager: Enemy, Player, PlayerOnlyCollision.
        private const int ActorLayers = (1 << 10) | (1 << 11) | (1 << 18);
        private static readonly Collider[] LandingHits = new Collider[16];
        internal static readonly RaidEnemyProfile Headman =
            new RaidEnemyProfile(Radius, Height, 1.87f, 6f, 10f, 360f, 1f, true, false);

        internal static bool WorldUp(RaidFloor floor) =>
            floor != null && Vector3.Dot(floor.Up, Vector3.up) > .9999f;

        internal static bool WorldGravity(Vector3 gravity) =>
            gravity.y < 0f && Mathf.Abs(gravity.x) < .001f && Mathf.Abs(gravity.z) < .001f;

        internal static bool SeesPortal(Navigation navigation, RaidFloor from, Vector3 eye,
            RaidTarget target, Vector3 apparentPoint, Rigidbody observerBody = null)
        {
            Vector3 direction = apparentPoint - eye;
            return WorldUp(from) && target && WorldUp(target.Floor) && from != target.Floor &&
                navigation.Sight(from, eye, direction, Headman, target, observerBody) == RaidSightKind.Portal;
        }

        internal static bool TryRoute(Navigation navigation, RaidFloor from, Vector3 start,
            RaidFloor to, Vector3 goal, out Navigation.Plan plan, Rigidbody portalTraveller = null)
        {
            plan = default;
            return WorldUp(from) && WorldUp(to) &&
                navigation.TryProjectPursuitGoal(to, goal, Headman, out Vector3 supportedGoal) &&
                navigation.TryPlan(from, start, to, supportedGoal, Headman, null, out plan, portalTraveller);
        }

        internal static bool TryNativePoint(Vector3 position, int agentType, int areaMask, out Vector3 supported)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = agentType, areaMask = areaMask };
            supported = position;
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, PositionTolerance, filter))
                return false;
            supported = hit.position;
            return true;
        }

        internal static bool TryNativePath(Vector3 from, Vector3 to, int agentType, int areaMask,
            out NavMeshPath path)
        {
            path = new NavMeshPath();
            var filter = new NavMeshQueryFilter { agentTypeID = agentType, areaMask = areaMask };
            return NavMesh.CalculatePath(from, to, filter, path) && path.status == NavMeshPathStatus.PathComplete &&
                path.corners.Length > 0 &&
                Vector3.Distance(path.corners[path.corners.Length - 1], to) <= PositionTolerance;
        }

        internal static bool LandingOccupied(PhysicsScene physics, Vector3 feet, Rigidbody pursuingBody)
        {
            if (!physics.IsValid())
                return true;
            Vector3 lower = feet + Vector3.up * (Radius + RaidGeometry.Skin);
            Vector3 upper = feet + Vector3.up * (Height - Radius + RaidGeometry.Skin);
            int count = physics.OverlapCapsule(lower, upper, Radius, LandingHits, ActorLayers,
                QueryTriggerInteraction.Ignore);
            bool occupied = count == LandingHits.Length;
            for (int i = 0; i < count && !occupied; i++)
            {
                var hit = LandingHits[i];
                occupied = hit && (!pursuingBody || hit.attachedRigidbody != pursuingBody);
            }
            Array.Clear(LandingHits, 0, count);
            return occupied;
        }

        internal static bool Warp(NavMeshAgent agent, Vector3 destination)
        {
            if (!agent || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
                return false;
            Vector3 original = agent.nextPosition;
            if (!agent.Warp(destination))
                return false;
            if (agent.isOnNavMesh && Vector3.Distance(agent.nextPosition, destination) <= PositionTolerance)
                return true;
            if (!agent.Warp(original))
                throw new System.InvalidOperationException("Could not restore the stock agent to its verified source polygon.");
            return false;
        }
    }
}

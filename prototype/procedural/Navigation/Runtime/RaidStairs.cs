using UnityEngine;

namespace Contour4.Procedural.Navigation
{
    public sealed partial class RaidNavigation
    {
        private const float StairSkin = .012f;
        private const float StairContactTolerance = .0001f;

        internal bool TryStairPath(RaidDeckPassage deck, Vector3 approach, Vector3 landing,
            RaidEnemyProfile profile, out Vector3[] course)
        {
            course = null;
            if (profile.MaximumStepHeight <= 0f || !deck.Stepped) return false;
            Vector3 up = deck.From.Up;
            if (!BodyClear(approach, up, profile)) return false;
            var points = new Vector3[deck.Count];
            points[0] = deck.Local(approach);
            Vector3 previous = approach;
            Vector3 support = approach;
            for (int index = 1; index < deck.Count; index++)
            {
                bool finish = index == deck.Count - 1;
                Vector3 sample = finish ? landing : deck.World(index).position;
                if (!StairSupport(deck, sample, up, profile, out Vector3 tread) ||
                    Mathf.Abs(Vector3.Dot(tread - support, up)) > profile.MaximumStepHeight + StairContactTolerance)
                    return false;
                support = tread;
                if (!finish && !LiftOverRiser(tread, up, profile, out sample))
                    return false;
                if (!StairTravel(deck, previous, sample, up, profile)) return false;
                points[index] = deck.Local(sample);
                previous = sample;
            }
            course = points;
            return true;
        }

        private bool LiftOverRiser(Vector3 tread, Vector3 up, RaidEnemyProfile profile, out Vector3 feet)
        {
            // The lower cap meets the next riser before the centre of the body reaches it.
            // Find the lowest clearance from above the tread using this profile's own capsule.
            float lift = profile.MaximumStepHeight;
            Vector3 high = tread + up * lift;
            feet = default;
            if (!BodyClear(high, up, profile)) return false;
            Capsule(high, up, profile, out var lower, out var upper);
            if (physics.CapsuleCast(lower, upper, profile.Radius, -up, out var hit, lift,
                layout.SolidMask, QueryTriggerInteraction.Ignore))
                lift = Mathf.Min(lift, lift - hit.distance + StairSkin);
            else
                lift = StairSkin;
            feet = tread + up * lift;
            return true;
        }

        private bool StairSupport(RaidDeckPassage deck, Vector3 feet, Vector3 up, RaidEnemyProfile profile,
            out Vector3 tread)
        {
            tread = default;
            float reach = profile.MaximumStepHeight + StairSkin;
            Vector3 origin = feet + up * reach;
            if (Cast(origin, out var hit))
            {
                if (!Supports(hit, origin)) return false;
                tread = hit.point;
                return true;
            }

            // Raycasts can miss both faces exactly at a shared stair/landing edge.
            // Require actual coplanar support on both sides and keep the original horizontal point.
            var offset = Vector3.ProjectOnPlane(deck.World(0).forward, up).normalized * .00015f;
            Vector3 before = origin - offset, after = origin + offset;
            if (!Cast(before, out var left) || !Supports(left, before) ||
                !Cast(after, out var right) || !Supports(right, after) ||
                Mathf.Abs(Vector3.Dot(left.point - right.point, up)) > .006f)
                return false;
            tread = feet + up * Vector3.Dot((left.point + right.point) * .5f - feet, up);
            return true;

            bool Cast(Vector3 start, out RaycastHit contact) =>
                physics.Raycast(start, -up, out contact, 2f * reach, layout.SolidMask, QueryTriggerInteraction.Ignore);

            bool Supports(RaycastHit contact, Vector3 start) =>
                RaidGeometry.Supports(physics, contact, start, up, .8f, layout.SolidMask, deck.Supports, null, portalHits);
        }

        internal bool StairTravel(RaidDeckPassage deck, Vector3 from, Vector3 to, Vector3 up, RaidEnemyProfile profile) =>
            StairSupport(deck, from, up, profile, out var startTread) &&
            StairSupport(deck, to, up, profile, out var endTread) &&
            Mathf.Abs(Vector3.Dot(endTread - startTread, up)) <= profile.MaximumStepHeight + StairContactTolerance &&
            Vector3.Dot(to - endTread, up) >= -RaidGeometry.Skin &&
            BodyTravel(from, to, up, profile);
    }
}

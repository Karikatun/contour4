using System;
using Contour4.Procedural.Navigation;
using UnityEngine;
using Navigation = Contour4.Procedural.Navigation.RaidNavigation;

namespace Contour4.NativeRaid.Runtime
{
    // The native chase keeps its target/authority. This consumer handles only the
    // physically supported local courses inside a single Möbius room; no portal
    // edge or endpoint teleport can be consumed here.
    internal sealed class MobiusPursuit
    {
        private readonly Navigation navigation;
        private readonly RaidEnemyProfile profile;
        private readonly Transform actor;
        private readonly Func<Pose, bool> write;
        private Navigation.Plan plan;
        private Navigation.Edge deck;
        private int corner, sample;
        private float replansIn;
        internal RaidFloor Floor { get; private set; }
        internal bool OnDeck => deck != null;
        internal int CompletedDecks { get; private set; }
        internal float Travelled { get; private set; }

        internal MobiusPursuit(Navigation navigation, RaidFloor floor, RaidEnemyProfile profile,
            Transform actor, Func<Pose, bool> write)
        {
            this.navigation = navigation;
            Floor = floor;
            this.profile = profile;
            this.actor = actor;
            this.write = write;
        }

        internal bool Advance(RaidFloor goalFloor, Vector3 goal, float seconds)
        {
            if (seconds <= 0 || seconds > .25f || goalFloor == null || goalFloor.RoomId != Floor.RoomId) return false;
            float budget = profile.Speed * seconds;
            if (deck != null) return FollowDeck(ref budget);
            replansIn -= seconds;
            if (replansIn <= 0 || plan.Corners == null)
            {
                plan = default;
                corner = 0;
                replansIn = .25f;
                if (!navigation.TryProjectPursuitGoal(goalFloor, goal, profile, out var supported) ||
                    !navigation.TryPlan(Floor, actor.position, goalFloor, supported, profile, null, out plan) ||
                    plan.Edge != null && plan.Edge.Kind != RaidRouteKind.OrientedDeck)
                { plan = default; return false; }
            }
            while (corner < plan.Corners.Length && budget > .0001f)
            {
                Vector3 before = actor.position;
                Vector3 next = Vector3.MoveTowards(before, plan.Corners[corner], Mathf.Min(budget, .12f));
                if (!navigation.TryStand(Floor, next, profile, out var feet) ||
                    !navigation.BodyTravel(before, feet, Floor.Up, profile)) return false;
                Vector3 forward = Vector3.ProjectOnPlane(feet - before, Floor.Up);
                var rotation = forward.sqrMagnitude > .000001f ? Quaternion.LookRotation(forward, Floor.Up) : actor.rotation;
                if (!Write(new Pose(feet, rotation), ref budget)) return false;
                if (Vector3.Distance(feet, plan.Corners[corner]) <= .008f) corner++;
            }
            if (corner < plan.Corners.Length || plan.Edge == null) return true;
            if (!navigation.TryTransition(plan.Edge, profile, out var approach, out _) ||
                Vector3.Distance(actor.position, approach) > .15f) return false;
            deck = plan.Edge;
            sample = 0;
            plan = default;
            return FollowDeck(ref budget);
        }

        private bool FollowDeck(ref float budget)
        {
            while (sample < deck.Deck.Count && budget > .0001f)
            {
                Pose target = deck.Deck.World(sample);
                float length = Vector3.Distance(actor.position, target.position);
                // A turn in place also consumes time, bounded by the authored normal
                // interpolation. It cannot rotate a tall body through an unchecked seam.
                float step = Mathf.Min(budget, .12f, length);
                float amount = length > .0001f ? step / length : 1f;
                var next = new Pose(Vector3.Lerp(actor.position, target.position, amount),
                    Quaternion.Slerp(actor.rotation, target.rotation, amount));
                if (!navigation.DeckIsClear(deck.Deck, next, profile) || !Write(next, ref budget)) return false;
                if (amount >= 1f) sample++;
            }
            if (sample < deck.Deck.Count) return true;
            Floor = deck.To;
            deck = null;
            CompletedDecks++;
            replansIn = 0;
            return true;
        }

        private bool Write(Pose pose, ref float budget)
        {
            float distance = Vector3.Distance(actor.position, pose.position);
            if (distance > .121f || !write(pose)) return false;
            if (Vector3.Distance(actor.position, pose.position) > .005f ||
                Quaternion.Angle(actor.rotation, pose.rotation) > .25f) return false;
            budget -= distance;
            Travelled += distance;
            return true;
        }
    }
}

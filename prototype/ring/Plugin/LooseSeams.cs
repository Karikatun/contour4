using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SpaceJourney;

internal sealed class LooseSeams : IDisposable
{
    private readonly Dictionary<Rigidbody, ItemImage> images = new();
    private readonly List<Rigidbody> removed = new();

    internal void Refresh(IEnumerable<Rigidbody> bodies, IReadOnlyDictionary<Rigidbody, Vector3> lastCenters,
        Transform ring, IReadOnlyList<Crossing> doors, CartSeams carts)
    {
        foreach (var body in bodies)
        {
            if (!body || body.isKinematic || carts.IsCart(body) || carts.TryGetSupport(body, out _))
                continue;
            Crossing? seam = null;
            Vector3 center = body.worldCenterOfMass;
            bool pendingTransfer = false;
            foreach (var door in doors)
            {
                if (!door.Entry.NearAperture(center))
                    continue;
                // PhysX can have advanced the center since the last script physics step.
                pendingTransfer = lastCenters.TryGetValue(body, out Vector3 previous) &&
                    door.Entry.Crossed(ring.TransformPoint(previous), center);
                if (door.Entry.DistanceFromPlane(center) > 0 && !pendingTransfer)
                    continue;
                seam = door;
                break;
            }
            if (seam is not Crossing crossing)
                continue;
            if (!images.TryGetValue(body, out var image))
            {
                image = new ItemImage(body);
                images.Add(body, image);
            }
            image.Refresh(crossing, pendingTransfer);
        }

        foreach (var (body, image) in images)
        {
            if (body && image.Refreshed)
            {
                image.Refreshed = false;
                continue;
            }
            image.Dispose();
            removed.Add(body);
        }
        foreach (var body in removed)
            images.Remove(body);
        removed.Clear();
    }

    public void Dispose()
    {
        foreach (var image in images.Values)
            image.Dispose();
        images.Clear();
    }

    internal bool TryHitExposed(Vector3 origin, Vector3 direction, float range, int mask, QueryTriggerInteraction triggers,
        out RaycastHit hit, out Crossing crossing)
    {
        hit = default;
        crossing = default;
        bool found = false;
        foreach (var (body, image) in images)
        {
            if (!body || !image.TryHitExposed(origin, direction, range, mask, triggers, out var candidate, out var link))
                continue;
            hit = candidate;
            range = hit.distance;
            crossing = link;
            found = true;
        }
        return found;
    }

    private sealed class ItemImage : IDisposable
    {
        private readonly MeshRenderer[] originals;
        private readonly Collider[] colliders;
        private CartSeams.SeamImage[] copies;
        private Crossing? linked;
        internal bool Refreshed { get; set; }

        internal ItemImage(Rigidbody body)
        {
            originals = body.GetComponentsInChildren<MeshRenderer>()
                .Where(renderer => renderer.GetComponent<MeshFilter>()?.sharedMesh).ToArray();
            colliders = body.GetComponentsInChildren<Collider>().Where(collider => collider.attachedRigidbody == body).ToArray();
        }

        internal void Refresh(Crossing crossing, bool pendingTransfer)
        {
            Refreshed = true;
            if (!pendingTransfer && !originals.Any(renderer => renderer && renderer.enabled && crossing.Entry.Straddles(renderer.bounds)))
            {
                Dispose();
                return;
            }
            if (copies == null || linked?.Exit != crossing.Exit)
            {
                Dispose();
                linked = crossing;
                copies = originals.Where(renderer => renderer).Select(renderer =>
                    new CartSeams.SeamImage(renderer, crossing.Exit.Marker, "Portal item image")).ToArray();
            }
            foreach (var copy in copies)
                copy.Update(crossing);
        }

        internal bool TryHitExposed(Vector3 origin, Vector3 direction, float range, int mask,
            QueryTriggerInteraction triggers, out RaycastHit hit, out Crossing crossing)
        {
            hit = default;
            crossing = default;
            if (linked is not Crossing seam || copies.Length == 0 || seam.Exit.DistanceFromPlane(origin) > 0)
                return false;

            crossing = seam.Reverse();
            var mapped = new Ray(crossing.TransformPoint(origin), crossing.Rotation * direction);
            bool includeTriggers = triggers == QueryTriggerInteraction.Collide ||
                (triggers == QueryTriggerInteraction.UseGlobal && Physics.queriesHitTriggers);
            bool found = false;
            foreach (var collider in colliders)
            {
                if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy ||
                    (mask & (1 << collider.gameObject.layer)) == 0 || (collider.isTrigger && !includeTriggers) ||
                    !collider.Raycast(mapped, out var candidate, range) || seam.Entry.DistanceFromPlane(candidate.point) <= 0)
                    continue;
                hit = candidate;
                range = hit.distance;
                found = true;
            }
            return found;
        }

        public void Dispose()
        {
            if (copies == null)
                return;
            foreach (var copy in copies)
                copy.Dispose();
            copies = null;
            linked = null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.NativeRaid;
using UnityEngine;

namespace SpaceJourney;

internal sealed class RingFurnitureMounts
{
    private const string AnchorName = "V6 room-local joint anchor";
    private readonly HashSet<Rigidbody> fronts = new();
    private readonly List<Rigidbody> rebaseBodies = new();

    internal IReadOnlyList<Rigidbody> RebaseBodies => rebaseBodies;

    internal bool OwnsFront(Rigidbody body) => fronts.Contains(body);

    internal void Bind(IEnumerable<Transform> rooms)
    {
        if (fronts.Count != 0) return;
        var bindings = new List<(Rigidbody Body, Rigidbody Anchor, Collider[] Parts)>();
        foreach (var room in rooms)
        {
            var anchorNode = room.Find(AnchorName);
            if (!anchorNode) continue;
            var anchor = anchorNode.GetComponent<Rigidbody>();
            if (!anchor || !anchor.isKinematic || anchor.useGravity)
                throw new InvalidOperationException($"{room.name}: invalid Ring furniture anchor.");

            foreach (var joint in room.GetComponentsInChildren<Joint>())
            {
                if (joint.connectedBody != anchor) continue;
                var body = joint.GetComponent<Rigidbody>();
                if (joint is not HingeJoint and not ConfigurableJoint || !body || body.isKinematic || body.useGravity)
                    throw new InvalidOperationException($"{joint.name}: invalid Ring furniture front body.");

                var parts = body.GetComponentsInChildren<Collider>()
                    .Where(collider => collider.attachedRigidbody == body).ToArray();
                if (parts.Length < 2 || parts.Any(part => part is not BoxCollider || part.isTrigger || !part.enabled))
                    throw new InvalidOperationException($"{joint.name}: expected solid Ring furniture panels and grip.");
                bindings.Add((body, anchor, parts));
            }

            if (!bindings.Any(binding => binding.Anchor == anchor))
                throw new InvalidOperationException($"{room.name}: Ring furniture anchor has no fronts.");
        }

        if (bindings.Select(binding => binding.Body).Distinct().Count() != bindings.Count)
            throw new InvalidOperationException("A Ring furniture front has more than one joint to its anchor.");

        foreach (var binding in bindings)
        {
            NativeRaidCabinetGrab.Install(binding.Body, binding.Parts);
            fronts.Add(binding.Body);
        }
        rebaseBodies.AddRange(bindings.Select(binding => binding.Anchor).Distinct());
        rebaseBodies.AddRange(fronts);
    }

    internal bool ReleaseHeld()
    {
        if (fronts.Count == 0 || !GameManager.instance || !PlayerController.instance) return false;
        var grabber = PhysGrabber.instance;
        if (!grabber || !grabber.grabbed || !grabber.grabbedObjectTransform) return false;
        if (!grabber.grabbedObjectTransform.TryGetComponent<Rigidbody>(out var body) || !OwnsFront(body)) return false;
        grabber.ReleaseObject(-1);
        return true;
    }
}

using System.Collections.Generic;
using Contour4.NativeRaid.Runtime;
using UnityEngine;

namespace Contour4.NativeRaid;

internal sealed class NativeRaidCargoTraversal
{
    private readonly struct Sample(Vector3 center, Rigidbody seat, NativeRaidPortal pending)
    {
        internal Vector3 Center => center;
        internal Rigidbody Seat => seat;
        internal NativeRaidPortal Pending => pending;
    }

    private readonly List<(NativeRaidPortal Entry, NativeRaidPortal Exit)> pairs = new();
    private readonly Dictionary<PhysGrabObject, PhysGrabCart> actors = new();
    private Dictionary<Rigidbody, Sample> previous = new();
    private Dictionary<Rigidbody, Sample> current = new();
    private readonly Dictionary<Rigidbody, Rigidbody> seats = new();
    private readonly HashSet<Rigidbody> load = new();
    private readonly HashSet<Rigidbody> moved = new();
    private readonly Collider[] nearby = new Collider[96];
    private RaidNavigationContext context;

    internal void Clear()
    {
        context = null;
        pairs.Clear();
        actors.Clear();
        previous.Clear();
        current.Clear();
        seats.Clear();
        load.Clear();
        moved.Clear();
        System.Array.Clear(nearby, 0, nearby.Length);
    }

    internal void Step(RaidAssembly raid, RaidNavigationContext nextContext, HashSet<Rigidbody> alreadyMoved)
    {
        if (!ReferenceEquals(context, nextContext))
        {
            Clear();
            context = nextContext;
            foreach (NativeRaidPortal portal in context.Portals)
            {
                if (!portal.Door) continue;
                foreach (NativeRaidPortal reverse in context.Portals)
                {
                    if (!reverse.Door || reverse.Entrance != portal.Exit || reverse.Exit != portal.Entrance) continue;
                    pairs.Add((portal, reverse));
                    break;
                }
            }
        }

        actors.Clear();
        current.Clear();
        seats.Clear();
        moved.Clear();
        moved.UnionWith(alreadyMoved);
        foreach (var (portal, _) in pairs)
            FindActors(raid, Mouth(portal));

        // Reserve seated cargo before handling independent crossings. A piece joining a basket
        // this step can already have crossed independently from last step's free position.
        foreach (var (actor, cart) in actors)
        {
            if (!PortalCargoDoor.IsValidCart(cart, actor.rb) || moved.Contains(actor.rb)) continue;
            var cartBody = actor.rb;
            load.Clear();
            PortalCargoDoor.CaptureBasket(cart, cartBody, load);
            load.RemoveWhere(body => moved.Contains(body) || seats.ContainsKey(body) || CrossedWhileLoose(raid.transform, body, cartBody));
            foreach (Rigidbody body in load)
                seats.Add(body, cartBody);

            load.Add(cartBody);
            NativeRaidPortal pending = TryMove(raid, cartBody, cart, load);
            current.Add(cartBody, new Sample(raid.transform.InverseTransformPoint(cartBody.worldCenterOfMass), null, pending));
        }

        foreach (var (actor, cart) in actors)
        {
            Rigidbody body = actor.rb;
            if (current.ContainsKey(body)) continue;
            bool seated = seats.TryGetValue(body, out Rigidbody seat);
            NativeRaidPortal pending = null;
            if (!cart && !seated && !moved.Contains(body))
            {
                load.Clear();
                load.Add(body);
                pending = TryMove(raid, body, null, load);
            }
            if (seated && !moved.Contains(body) && previous.TryGetValue(body, out var last) && last.Seat == seat)
            {
                Vector3 finish = body.worldCenterOfMass;
                if (PortalCargoDoor.Continuous(raid.transform, last.Center, finish, out var start))
                {
                    pending = last.Pending;
                    if (pending != null && !StillAcross(pending, finish)) pending = null;
                    if (pending == null && FindFirst(start, finish, out var pair, out _))
                        pending = pair.Entry;
                }
            }
            current.Add(body, new Sample(raid.transform.InverseTransformPoint(body.worldCenterOfMass), seat, pending));
        }
        (previous, current) = (current, previous);
    }

    private void FindActors(RaidAssembly raid, CargoMouth mouth)
    {
        Vector3 center = mouth.Point(new Vector3(0f, 0f, mouth.Depth));
        var half = new Vector3(mouth.HalfWidth + .5f, mouth.HalfHeight + .5f, PortalCargoDoor.MaxStep + .5f);
        int mask = PortalCargoDoor.ActorMask;
        int count = Physics.OverlapBoxNonAlloc(center, half, nearby, mouth.Rotation, mask, QueryTriggerInteraction.Ignore);
        var hits = count < nearby.Length ? nearby :
            Physics.OverlapBox(center, half, mouth.Rotation, mask, QueryTriggerInteraction.Ignore);
        for (int index = 0; index < (hits == nearby ? count : hits.Length); index++)
        {
            Rigidbody body = hits[index].attachedRigidbody;
            if (!body || body.gameObject.scene != raid.gameObject.scene ||
                !PortalCargoDoor.TryActor(body, out var actor) || PortalCargoDoor.IsHeld(actor) || actors.ContainsKey(actor))
                continue;
            actors.Add(actor, PortalCargoDoor.CartFor(actor));
        }
    }

    private bool CrossedWhileLoose(Transform frame, Rigidbody body, Rigidbody cart)
    {
        if (!previous.TryGetValue(body, out var last)) return false;
        if (last.Seat && last.Seat != cart) return true;
        if (last.Seat == cart) return false;
        Vector3 finish = body.worldCenterOfMass;
        if (last.Pending != null && StillAcross(last.Pending, finish)) return true;
        return PortalCargoDoor.Continuous(frame, last.Center, finish, out var start) &&
            FindFirst(start, finish, out _, out _);
    }

    private NativeRaidPortal TryMove(RaidAssembly raid, Rigidbody body, PhysGrabCart cart, HashSet<Rigidbody> assembly)
    {
        if (!previous.TryGetValue(body, out var last)) return null;
        Vector3 finish = body.worldCenterOfMass;
        if (!PortalCargoDoor.Continuous(raid.transform, last.Center, finish, out var start)) return null;

        (NativeRaidPortal Entry, NativeRaidPortal Exit) pair;
        Vector3 crossingPoint;
        if (last.Pending != null && StillAcross(last.Pending, finish))
        {
            pair = pairs.Find(pair => pair.Entry == last.Pending);
            if (pair.Entry == null) return null;
            Vector3 offset = last.Pending.Entrance.forward * (Mouth(last.Pending).Local(finish).z - last.Pending.DoorCrossingDepth);
            crossingPoint = finish - offset;
        }
        else
        {
            if (!FindFirst(start, finish, out pair, out float fraction)) return null;
            crossingPoint = Vector3.LerpUnclamped(start, finish, fraction);
        }

        NativeRaidPortal entry = pair.Entry;
        NativeRaidPortal exit = pair.Exit;
        var result = PortalCargoDoor.Traverse(Mouth(entry), Mouth(exit), entry.Rotation, body, cart, assembly,
            crossingPoint, entry.AllowsCart && exit.AllowsCart,
            point => raid.HasPortalSupport(entry.EntryFloor, point),
            point => raid.HasPortalSupport(exit.EntryFloor, point));
        if (result == CargoTraversalResult.Blocked) return entry;
        moved.UnionWith(assembly);
        return null;
    }

    private bool FindFirst(Vector3 start, Vector3 finish, out (NativeRaidPortal Entry, NativeRaidPortal Exit) pair,
        out float fraction)
    {
        pair = default;
        fraction = float.PositiveInfinity;
        foreach (var candidate in pairs)
        {
            if (!Mouth(candidate.Entry).CrossingFraction(start, finish, out float t) || t >= fraction) continue;
            pair = candidate;
            fraction = t;
        }
        return pair.Entry != null;
    }

    private static bool StillAcross(NativeRaidPortal portal, Vector3 point)
    {
        Vector3 local = portal.Entrance.InverseTransformPoint(point);
        return local.z >= portal.DoorCrossingDepth && Mathf.Abs(local.x) < portal.HalfWidth &&
            Mathf.Abs(local.y) < portal.HalfHeight;
    }

    private static CargoMouth Mouth(NativeRaidPortal portal) =>
        new(portal.Entrance.position, portal.Entrance.rotation, portal.HalfWidth, portal.HalfHeight, portal.DoorCrossingDepth);
}

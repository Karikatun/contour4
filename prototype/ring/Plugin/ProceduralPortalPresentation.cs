using System.Collections.Generic;
using System.Linq;
using Contour4.NativeRaid;
using Contour4.NativeRaid.Runtime;
using UnityEngine;

namespace SpaceJourney;

internal static class ProceduralPortalPresentation
{
    internal static GrabContext Grab { get; private set; }

    internal static PortalViews Attach(Transform host, IReadOnlyList<NativeRaidPortal> portals)
    {
        if (portals.Count == 0)
            return null;

        var crossings = new List<(PortalViews.Aperture Entry, PortalViews.Aperture Exit)>(portals.Count);
        foreach (NativeRaidPortal portal in portals)
        {
            float width = portal.HalfWidth * 2f;
            float height = portal.HalfHeight * 2f;
            crossings.Add((Aperture(portal.Entrance, portal.EntryFloor.Up, width, height),
                Aperture(portal.Exit, portal.ExitFloor.Up, width, height)));
        }

        var views = host.gameObject.AddComponent<PortalViews>();
        var module = host.GetComponent<NativeRaidModule>();
        views.Initialize(crossings, module ? module.CameraSide : null);
        if (module)
        {
            Grab?.Hold.Clear();
            Grab = new GrabContext(module, views, portals);
        }
        return views;
    }

    private static PortalViews.Aperture Aperture(Transform frame, Vector3 floorUp, float width, float height) =>
        new(frame, frame.parent, width, height, Mathf.Abs(Vector3.Dot(frame.forward, floorUp)) > .999f);

    internal sealed class GrabContext
    {
        internal NativeRaidModule Module { get; }
        internal PortalViews Views { get; }
        internal IReadOnlyList<NativeRaidPortal> Doors { get; }
        internal PortalGrab.MappedHold Hold { get; } = new();

        internal GrabContext(NativeRaidModule module, PortalViews views, IReadOnlyList<NativeRaidPortal> portals)
        {
            Module = module;
            Views = views;
            Doors = portals.Where(door => door.Door && portals.Any(reverse => reverse.Door &&
                reverse.Entrance == door.Exit && reverse.Exit == door.Entrance)).ToArray();
            module.MappedHandFor = MappedHandFor;
            module.ClearMappedHand = Hold.Clear;
            module.UnbindMappedHand = Unbind;
        }

        private (bool CanCross, Rigidbody Body)? MappedHandFor(PhysGrabber grabber, NativeRaidPortal portal)
        {
            if (!Hold.IsHeldBy(grabber) || Hold.Door?.Raid is not NativeRaidPortal heldDoor)
                return null;
            return (!Hold.Expired && heldDoor == portal, Hold.Held.rb);
        }

        private void Unbind()
        {
            Hold.Clear();
            if (ReferenceEquals(Grab, this))
                Grab = null;
        }
    }
}

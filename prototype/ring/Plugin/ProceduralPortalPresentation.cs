using System.Collections.Generic;
using Contour4.NativeRaid;
using Contour4.NativeRaid.Runtime;
using UnityEngine;

namespace SpaceJourney;

internal static class ProceduralPortalPresentation
{
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
        return views;
    }

    private static PortalViews.Aperture Aperture(Transform frame, Vector3 floorUp, float width, float height) =>
        new(frame, frame.parent, width, height, Mathf.Abs(Vector3.Dot(frame.forward, floorUp)) > .999f);
}

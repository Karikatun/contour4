using HarmonyLib;
using UnityEngine;

namespace SpaceJourney;

internal sealed class PortalBeamPresentation : MonoBehaviour
{
    private LineRenderer exitLine, exitOvercharge;
    private Vector3[] entryPoints, exitPoints;
    private Vector3[] nativePoints, nativeOvercharge;
    private Vector3[] entryOvercharge, exitOverchargePoints;

    [HarmonyPatch(typeof(PhysGrabBeam), "LateUpdate")]
    private static class NativeBeam
    {
        private static void Postfix(PhysGrabBeam __instance)
        {
            var player = __instance.playerAvatar;
            var grabber = player ? player.physGrabber : null;
            if (!grabber || !grabber.isLocal)
                return;

            __instance.TryGetComponent(out PortalBeamPresentation split);
            var line = __instance.lineRenderer;
            if (!line || !line.enabled || GrabRoute(grabber) is not PortalGrab.GrabCrossing crossing)
            {
                if (split) split.Hide();
                return;
            }

            if (!__instance.PhysGrabPointOrigin || !__instance.PhysGrabPoint || !crossing.Entry || !crossing.Exit)
            {
                // The stock curve may still have endpoints in different rooms during cleanup.
                line.positionCount = 0;
                if (__instance.lineRendererOverCharge) __instance.lineRendererOverCharge.positionCount = 0;
                if (split) split.Hide();
                return;
            }

            if (!split) split = __instance.gameObject.AddComponent<PortalBeamPresentation>();
            split.Draw(__instance, crossing);
        }
    }

    [HarmonyPatch(typeof(PhysGrabBeam), "OnDisable")]
    private static class DisableNativeBeam
    {
        private static void Postfix(PhysGrabBeam __instance)
        {
            if (__instance.TryGetComponent(out PortalBeamPresentation split))
                split.Hide();
        }
    }

    private static PortalGrab.GrabCrossing? GrabRoute(PhysGrabber grabber)
    {
        var raid = ProceduralPortalPresentation.Grab;
        if (raid?.Module && raid.Module.isActiveAndEnabled && raid.Hold.IsHeldBy(grabber))
            return raid.Hold.Door;
        var ring = RingRuntime.Active;
        return ring && ring.MappedGrab.IsHeldBy(grabber) ? ring.MappedGrab.Door : null;
    }

    private void Draw(PhysGrabBeam beam, PortalGrab.GrabCrossing crossing)
    {
        var line = beam.lineRenderer;
        int count = Mathf.Max(2, beam.CurveResolution);
        if (entryPoints == null || entryPoints.Length != count)
        {
            entryPoints = new Vector3[count];
            exitPoints = new Vector3[count];
        }

        Quaternion frame;
        Vector2 halfSize;
        if (crossing.Ring is Crossing ring)
        {
            frame = ring.Entry.Orientation;
            halfSize = new Vector2(ring.Entry.Width * .5f, ring.Entry.Height * .5f);
        }
        else
        {
            frame = crossing.Entry.rotation;
            halfSize = new Vector2(crossing.Raid.HalfWidth, crossing.Raid.HalfHeight);
        }

        Vector3 center = crossing.Center;
        Vector3 exitCenter = crossing.MapPoint(center);
        Quaternion inverseFrame = Quaternion.Inverse(frame);
        Quaternion across = crossing.Rotation * frame;
        Vector3 start = inverseFrame * (beam.PhysGrabPointOrigin.position - center);
        Vector3 finish = Quaternion.Inverse(across) * (beam.PhysGrabPoint.position - exitCenter);
        Vector3 pull = inverseFrame * (beam.physGrabPointPullerSmoothPosition - center);
        float split = WritePath(start, pull, finish, halfSize, beam.CurveStrength, center, frame, exitCenter, across);
        var overcharge = beam.lineRendererOverCharge;
        bool charged = overcharge && overcharge.enabled;
        if (charged)
            WriteOvercharge(line, overcharge, split, center, frame * Vector3.forward, exitCenter, crossing.Rotation);

        DrawPair(line, ref exitLine, "Contour-4 Portal Beam Exit", entryPoints, exitPoints);
        if (charged)
            DrawPair(overcharge, ref exitOvercharge, "Contour-4 Portal Beam Overcharge Exit",
                entryOvercharge, exitOverchargePoints);
        else if (exitOvercharge)
            exitOvercharge.enabled = false;
    }

    private float WritePath(Vector3 start, Vector3 pull, Vector3 finish, Vector2 halfSize, float strength,
        Vector3 center, Quaternion frame, Vector3 exitCenter, Quaternion across)
    {
        // Unfold both sides into the entry frame, so a curve can meet the aperture exactly once.
        start.z = Mathf.Min(start.z, 0f);
        finish.z = Mathf.Max(finish.z, 0f);
        float length = Vector3.Distance(start, finish);
        Vector3 control = Vector3.Lerp(start, pull, strength);
        // The native smoothing cache can still be in the other room on a traversal frame.
        if ((control - start).sqrMagnitude > length * length * 4f)
            control = Vector3.Lerp(start, finish, .5f);
        control.z = Mathf.Clamp(control.z, start.z, finish.z);

        float from = 0f, to = 1f;
        for (int step = 0; step < 12; step++)
        {
            float middle = (from + to) * .5f;
            if (Quadratic(start, control, finish, middle).z < 0f) from = middle;
            else to = middle;
        }
        float split = (from + to) * .5f;
        Vector3 seam = Quadratic(start, control, finish, split);
        Vector3 leftControl = Vector3.Lerp(start, control, split);
        Vector3 rightControl = Vector3.Lerp(control, finish, split);

        // A held object can move off-axis. Bend towards the jamb without ever drawing through it.
        Vector3 clipped = new(Mathf.Clamp(seam.x, -.995f * halfSize.x, .995f * halfSize.x),
            Mathf.Clamp(seam.y, -.995f * halfSize.y, .995f * halfSize.y), 0f);
        Vector3 correction = clipped - seam;
        leftControl += correction;
        rightControl += correction;
        leftControl.z = Mathf.Clamp(leftControl.z, start.z, 0f);
        rightControl.z = Mathf.Clamp(rightControl.z, 0f, finish.z);
        for (int index = 0; index < entryPoints.Length; index++)
        {
            float t = index / (float)(entryPoints.Length - 1);
            entryPoints[index] = center + frame * Quadratic(start, leftControl, clipped, t);
            exitPoints[index] = exitCenter + across * Quadratic(clipped, rightControl, finish, t);
        }
        return split;
    }

    private void WriteOvercharge(LineRenderer line, LineRenderer overlay, float split, Vector3 center,
        Vector3 outward, Vector3 exitCenter, Quaternion rotation)
    {
        int count = entryPoints.Length;
        if (nativePoints == null || nativePoints.Length != count)
        {
            nativePoints = new Vector3[count];
            nativeOvercharge = new Vector3[count];
            entryOvercharge = new Vector3[count];
            exitOverchargePoints = new Vector3[count];
        }

        // Retain the stock flicker as a displacement from its curve, without retaining the world-space connector.
        int baseCount = line.GetPositions(nativePoints);
        int overlayCount = overlay.GetPositions(nativeOvercharge);
        if (baseCount != count || overlayCount != count)
        {
            System.Array.Copy(entryPoints, entryOvercharge, count);
            System.Array.Copy(exitPoints, exitOverchargePoints, count);
            return;
        }

        Vector3 exitOutward = -(rotation * outward);
        for (int index = 0; index < count; index++)
        {
            float t = index / (float)(count - 1);
            // Keep the exact endpoints and matched seam on the aperture while the interior flickers.
            float fade = 2f * Mathf.Min(t, 1f - t);
            Vector3 entryNoise = OverchargeOffset(t * split) * fade;
            Vector3 exitNoise = rotation * OverchargeOffset(Mathf.Lerp(split, 1f, t)) * fade;
            entryOvercharge[index] = KeepInRoom(entryPoints[index] + entryNoise, center, outward);
            exitOverchargePoints[index] = KeepInRoom(exitPoints[index] + exitNoise, exitCenter, exitOutward);
        }
    }

    private Vector3 OverchargeOffset(float t)
    {
        float sample = t * (nativePoints.Length - 1);
        int first = (int)sample;
        int next = Mathf.Min(first + 1, nativePoints.Length - 1);
        return Vector3.Lerp(nativeOvercharge[first] - nativePoints[first],
            nativeOvercharge[next] - nativePoints[next], sample - first);
    }

    private static Vector3 KeepInRoom(Vector3 point, Vector3 center, Vector3 outward) =>
        point - outward * Mathf.Max(0f, Vector3.Dot(point - center, outward));

    private static Vector3 Quadratic(Vector3 start, Vector3 control, Vector3 finish, float t)
    {
        float left = 1f - t;
        return start * (left * left) + control * (2f * left * t) + finish * (t * t);
    }

    private void DrawPair(LineRenderer entry, ref LineRenderer exit, string name, Vector3[] nearPoints, Vector3[] farPoints)
    {
        if (!exit) exit = CreateExit(entry, name);
        if (exit.sharedMaterial != entry.sharedMaterial) exit.sharedMaterial = entry.sharedMaterial;
        if (exit.gameObject.layer != entry.gameObject.layer) exit.gameObject.layer = entry.gameObject.layer;
        exit.widthMultiplier = entry.widthMultiplier;
        exit.enabled = true;
        Write(entry, nearPoints);
        Write(exit, farPoints);
    }

    private LineRenderer CreateExit(LineRenderer entry, string name)
    {
        var segment = new GameObject(name) { layer = entry.gameObject.layer };
        segment.transform.SetParent(transform, false);
        var line = segment.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.sharedMaterial = entry.sharedMaterial;
        line.colorGradient = entry.colorGradient;
        line.widthCurve = entry.widthCurve;
        line.textureMode = entry.textureMode;
        line.textureScale = entry.textureScale;
        line.alignment = entry.alignment;
        line.numCornerVertices = entry.numCornerVertices;
        line.numCapVertices = entry.numCapVertices;
        line.generateLightingData = entry.generateLightingData;
        line.shadowCastingMode = entry.shadowCastingMode;
        line.receiveShadows = entry.receiveShadows;
        line.renderingLayerMask = entry.renderingLayerMask;
        line.sortingLayerID = entry.sortingLayerID;
        line.sortingOrder = entry.sortingOrder;
        return line;
    }

    private static void Write(LineRenderer line, Vector3[] points)
    {
        line.positionCount = points.Length;
        if (line.useWorldSpace)
            line.SetPositions(points);
        else
            for (int index = 0; index < points.Length; index++)
                line.SetPosition(index, line.transform.InverseTransformPoint(points[index]));
    }

    private void Hide()
    {
        if (exitLine) exitLine.enabled = false;
        if (exitOvercharge) exitOvercharge.enabled = false;
    }
}

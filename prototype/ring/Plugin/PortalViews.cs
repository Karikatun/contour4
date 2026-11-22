using System.Collections.Generic;
using Contour4.NativeRaid.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace SpaceJourney;

[DefaultExecutionOrder(1000)]
internal sealed class PortalViews : MonoBehaviour
{
    private readonly List<View> views = new();
    private readonly Plane[] frustum = new Plane[6];
    private CommandBuffer panels;
    private Camera source;
    private RingRuntime ring;
    private System.Func<Vector3, float, NativeRaidPortal> proceduralCameraSide;
    private Light portalFlashlight;
    private Light shiftedFlashlight;
    private Matrix4x4 originalMainView;
    private Vector3 originalEye;
    private bool wasDefaultView;
    private bool shiftedEye;
    private bool listening;

    internal void Initialize(RingRuntime ring)
    {
        this.ring = ring;
        var crossings = new List<(Aperture Entry, Aperture Exit)>();
        foreach (var portal in ring.Portals)
        {
            var a = new Aperture(portal.A);
            var b = new Aperture(portal.B);
            crossings.Add((a, b));
            crossings.Add((b, a));
        }
        Initialize(crossings);
    }

    internal void Initialize(IReadOnlyList<(Aperture Entry, Aperture Exit)> crossings,
        System.Func<Vector3, float, NativeRaidPortal> proceduralCameraSide = null)
    {
        this.proceduralCameraSide = proceduralCameraSide;
        panels = new CommandBuffer { name = "Contour-4 portal surfaces" };
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.SetActive(false);
        Mesh mesh = quad.GetComponent<MeshFilter>().sharedMesh;
        Destroy(quad);
        foreach (var (entry, exit) in crossings)
            views.Add(new View(transform, mesh, entry, exit));
        StartRendering();
    }

    private void OnEnable() => StartRendering();

    private void StartRendering()
    {
        if (listening || views.Count == 0 || !isActiveAndEnabled)
            return;
        listening = true;
        Camera.onPreCull += RenderPortals;
        Camera.onPostRender += RestoreMainView;
    }

    private void RenderPortals(Camera main)
    {
        if (!GameDirector.instance || main != SemiFunc.MainCamera())
            return;
        if (main != source)
        {
            if (source)
                source.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, panels);
            source = main;
            source.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, panels);
        }
        panels.Clear();
        if (ring)
            ring.RefreshPortalImages();
        Matrix4x4 cameraToWorld = source.cameraToWorldMatrix;
        Vector3 eye = cameraToWorld.GetColumn(3);
        Quaternion viewRotation = Quaternion.LookRotation(-(Vector3)cameraToWorld.GetColumn(2), cameraToWorld.GetColumn(1));
        float nearCorner = RingRuntime.NearCorner(source);
        var flashlight = FlashlightController.Instance ? FlashlightController.Instance.spotlight : null;
        var postProcessing = source.GetComponent<PostProcessLayer>();
        Fog fog = source.actualRenderingPath == RenderingPath.DeferredShading && postProcessing && postProcessing.isActiveAndEnabled
            && postProcessing.fog != null && postProcessing.fog.enabled && RenderSettings.fog ? postProcessing.fog : null;
        Matrix4x4 projection = source.projectionMatrix;
        Light beam = null;
        bool flashlightPrepared = false;
        bool flashlightWasEnabled = flashlight && flashlight.enabled;
        Crossing? ringSide = ring ? ring.CameraSide(eye, nearCorner) : null;
        NativeRaidPortal nativeSide = !ring ? proceduralCameraSide?.Invoke(eye, nearCorner) : null;
        if (ringSide.HasValue || nativeSide != null)
        {
            // The camera can enter the linked room before the player's center reorients the ring.
            Quaternion turn = ringSide is Crossing eyeSide ? eyeSide.Rotation : nativeSide.Rotation;
            Vector3 Map(Vector3 point) => ringSide is Crossing crossing ? crossing.TransformPoint(point) : nativeSide.MapPoint(point);
            originalEye = eye;
            originalMainView = source.worldToCameraMatrix;
            wasDefaultView = NearDefaultView(originalMainView, source.transform);
            shiftedEye = true;
            beam = PrepareFlashlight(flashlight);
            flashlightPrepared = true;
            if (beam)
            {
                shiftedFlashlight = flashlight;
                flashlight.enabled = false;
                Transform light = beam.transform;
                light.SetPositionAndRotation(Map(light.position), turn * light.rotation);
            }
            eye = Map(eye);
            viewRotation = turn * viewRotation;
            source.worldToCameraMatrix = WorldToCamera(eye, viewRotation);
        }
        GeometryUtility.CalculateFrustumPlanes(source, frustum);
        try
        {
            foreach (var view in views)
            {
                if (!view.TryGetSurface(eye, nearCorner, frustum, out Matrix4x4 surface, out bool atDoor))
                    continue;
                if (!flashlightPrepared)
                {
                    beam = PrepareFlashlight(flashlight);
                    if (beam)
                        flashlight.enabled = false;
                    flashlightPrepared = true;
                }
                view.Draw(source, projection, eye, viewRotation, nearCorner, surface, atDoor, panels, beam, fog, views);
            }
            if (shiftedEye && beam)
                beam.enabled = true;
        }
        catch
        {
            RestoreMainView(main);
            throw;
        }
        finally
        {
            if (!shiftedEye && beam && flashlight)
                flashlight.enabled = flashlightWasEnabled;
        }
    }

    private void RestoreMainView(Camera main)
    {
        if (!shiftedEye || main != source)
            return;
        shiftedEye = false;
        if (wasDefaultView)
            source.ResetWorldToCameraMatrix();
        else
            source.worldToCameraMatrix = originalMainView;
        if (shiftedFlashlight)
            shiftedFlashlight.enabled = true;
        shiftedFlashlight = null;
        if (portalFlashlight)
            portalFlashlight.enabled = false;
    }

    internal Vector3 UnshiftedEye(Camera camera) => shiftedEye && source == camera ? originalEye :
        (Vector3)camera.cameraToWorldMatrix.GetColumn(3);

    private static Matrix4x4 WorldToCamera(Vector3 eye, Quaternion rotation) =>
        Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(eye, rotation, Vector3.one).inverse;

    private static bool NearDefaultView(Matrix4x4 view, Transform camera)
    {
        Matrix4x4 native = WorldToCamera(camera.position, camera.rotation);
        for (int index = 0; index < 16; index++)
            if (Mathf.Abs(view[index] - native[index]) > .0005f)
                return false;
        return true;
    }

    private Light PrepareFlashlight(Light native)
    {
        if (!native || !native.isActiveAndEnabled)
            return null;
        if (!portalFlashlight)
        {
            var helper = new GameObject("Contour-4 portal flashlight");
            helper.SetActive(false);
            portalFlashlight = helper.AddComponent<Light>();
            portalFlashlight.enabled = false;
            helper.SetActive(true);
        }

        // The game owns the native flashlight pose. The portal camera needs a light with its own world transform.
        Light beam = portalFlashlight;
        beam.transform.SetPositionAndRotation(native.transform.position, native.transform.rotation);
        beam.gameObject.layer = native.gameObject.layer;
        beam.type = native.type;
        beam.color = native.color;
        beam.colorTemperature = native.colorTemperature;
        beam.useColorTemperature = native.useColorTemperature;
        beam.intensity = native.intensity;
        beam.range = native.range;
        beam.spotAngle = native.spotAngle;
        beam.innerSpotAngle = native.innerSpotAngle;
        beam.cullingMask = native.cullingMask;
        beam.renderingLayerMask = native.renderingLayerMask;
        beam.renderMode = native.renderMode;
        beam.cookie = native.cookie;
        beam.cookieSize2D = native.cookieSize2D;
        beam.shadows = native.shadows;
        beam.shadowStrength = native.shadowStrength;
        beam.shadowBias = native.shadowBias;
        beam.shadowNormalBias = native.shadowNormalBias;
        beam.shadowNearPlane = native.shadowNearPlane;
        beam.shadowResolution = native.shadowResolution;
        beam.shadowCustomResolution = native.shadowCustomResolution;
        beam.lightShadowCasterMode = native.lightShadowCasterMode;
        return beam;
    }

    private void OnDisable()
    {
        if (!listening)
            return;
        listening = false;
        Camera.onPreCull -= RenderPortals;
        Camera.onPostRender -= RestoreMainView;
        if (source)
        {
            RestoreMainView(source);
            source.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, panels);
        }
        source = null;
        panels.Clear();
    }

    private void OnDestroy()
    {
        OnDisable();
        panels?.Dispose();
        foreach (var view in views)
            view.Dispose();
        if (portalFlashlight)
            Destroy(portalFlashlight.gameObject);
    }

    internal sealed class Aperture
    {
        private readonly PortalEnd ring;
        private readonly float width;
        private readonly float height;
        internal Transform Marker { get; }
        internal Transform Room { get; }
        internal bool Upper { get; }
        internal float Width => ring != null ? ring.Width : width;
        internal float Height => ring != null ? ring.Height : height;
        internal Vector3 Center => ring != null ? ring.Center : Marker.position;
        internal Quaternion Orientation => ring != null ? ring.Orientation : Marker.rotation;
        internal Vector3 Normal => ring != null ? ring.Normal : Marker.forward;
        internal Vector3 ScreenUp => ring != null ? ring.ScreenUp : Marker.up;

        internal Aperture(PortalEnd ring)
        {
            this.ring = ring;
            Marker = ring.Marker;
            Room = ring.Room;
            Upper = ring.Upper;
        }

        internal Aperture(Transform marker, Transform room, float width, float height, bool upper)
        {
            Marker = marker;
            Room = room;
            this.width = width;
            this.height = height;
            Upper = upper;
        }

        internal bool NearAperture(Vector3 point)
        {
            if (ring != null)
                return ring.NearAperture(point);
            Vector3 local = Quaternion.Inverse(Orientation) * (point - Center);
            return Mathf.Abs(local.x) <= Width * .5f + .75f && Mathf.Abs(local.y) <= Height * .5f + .75f &&
                Mathf.Abs(local.z) <= 2.5f;
        }
    }

    private sealed class View
    {
        private readonly Aperture entry;
        private readonly Aperture exit;
        private readonly Transform cameraParent;
        private readonly Material material;
        private readonly Mesh mesh;
        private readonly Dictionary<Aperture, View> nestedViews = new();
        private readonly MaterialPropertyBlock nearSurface = new();
        private readonly Plane[] nestedFrustum = new Plane[6];
        private readonly CommandBuffer overlays = new() { name = "Contour-4 portal effects and surfaces" };
        private static readonly int FogCopy = Shader.PropertyToID("_Contour4PortalFogCopy");
        private Camera camera;
        private RenderTexture target;
        private RenderTexture depth;
        private Material fogMaterial;

        internal View(Transform cameraParent, Mesh mesh, Aperture entry, Aperture exit)
        {
            this.cameraParent = cameraParent;
            this.mesh = mesh;
            this.entry = entry;
            this.exit = exit;
            material = new Material(Contour4.PortalShader);
        }

        internal void Draw(Camera source, Matrix4x4 projection, Vector3 eye, Quaternion viewRotation, float nearCorner,
            Matrix4x4 surface, bool atDoor, CommandBuffer panels, Light flashlight, Fog fog, IReadOnlyList<View> availableViews)
        {
            PrepareCamera(source);
            var pose = camera.transform;
            Quaternion rotation = exit.Orientation * Quaternion.AngleAxis(180f, Vector3.up) * Quaternion.Inverse(entry.Orientation);
            Vector3 entryCenter = entry.Center;
            Vector3 exitCenter = exit.Center;
            pose.SetPositionAndRotation(exitCenter + rotation * (eye - entryCenter), rotation * viewRotation);
            Vector3 inward = -exit.Normal;
            Matrix4x4 worldToCamera = camera.worldToCameraMatrix;
            Vector3 normal = worldToCamera.MultiplyVector(inward).normalized;
            float clipDepth = exit.Upper ? .035f : .001f;
            Vector3 point = worldToCamera.MultiplyPoint(exitCenter + inward * clipDepth);
            camera.projectionMatrix = projection;
            camera.projectionMatrix = camera.CalculateObliqueMatrix(
                new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, point)));
            Render(projection, nearCorner, flashlight, fog, availableViews, rotation, entryCenter, exitCenter);
            panels.DrawMesh(mesh, surface, material, 0, 0);
            if (atDoor)
                DrawNearSurface(source, eye, panels);
        }

        private void DrawNearSurface(Camera source, Vector3 eye, CommandBuffer panels)
        {
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(source.projectionMatrix, source.targetTexture);
            nearSurface.SetMatrix("_PortalClipToWorld", (projection * source.worldToCameraMatrix).inverse);
            nearSurface.SetVector("_PortalEye", eye);
            nearSurface.SetVector("_PortalCenter", entry.Center);
            nearSurface.SetVector("_PortalNormal", entry.Normal);
            nearSurface.SetVector("_PortalRight", entry.Orientation * Vector3.right);
            nearSurface.SetVector("_PortalUp", entry.ScreenUp);
            nearSurface.SetVector("_PortalHalfSize", new Vector4(entry.Width * .5f, entry.Height * .5f, 0, 0));
            panels.DrawMesh(mesh, Matrix4x4.identity, material, 0, 2, nearSurface);
        }

        internal bool TryGetSurface(Vector3 eye, float nearCorner, Plane[] frustum, out Matrix4x4 surface, out bool atDoor)
        {
            surface = default;
            atDoor = false;
            if (!entry.Marker || !exit.Marker)
                return false;
            float eyeDepth = Vector3.Dot(eye - entry.Center, entry.Normal);
            atDoor = !entry.Upper && eyeDepth >= -nearCorner && entry.NearAperture(eye);
            if (eyeDepth > 0)
                return false;
            Vector3 center = entry.Center;
            Quaternion orientation = entry.Orientation;
            Vector3 right = orientation * Vector3.right * entry.Width;
            Vector3 up = orientation * Vector3.up * entry.Height;
            Vector3 size = new(Mathf.Abs(right.x) + Mathf.Abs(up.x), Mathf.Abs(right.y) + Mathf.Abs(up.y),
                Mathf.Abs(right.z) + Mathf.Abs(up.z));
            if (!atDoor && !GeometryUtility.TestPlanesAABB(frustum, new Bounds(center, size + Vector3.one * .1f)))
                return false;
            surface = Matrix4x4.TRS(center, orientation, new Vector3(entry.Width, entry.Height, 1));
            return true;
        }

        private void Render(Matrix4x4 projection, float nearCorner, Light flashlight, Fog fog, IReadOnlyList<View> availableViews,
            Quaternion rotation, Vector3 entryCenter, Vector3 exitCenter)
        {
            overlays.Clear();
            if (fog != null)
                AddFog(fog);

            Transform lightPose = flashlight ? flashlight.transform : null;
            bool lightWasEnabled = flashlight && flashlight.enabled;
            Vector3 oldPosition = lightPose ? lightPose.position : default;
            Quaternion oldRotation = lightPose ? lightPose.rotation : default;
            bool fogWasEnabled = RenderSettings.fog;
            try
            {
                if (lightPose)
                {
                    lightPose.SetPositionAndRotation(exitCenter + rotation * (oldPosition - entryCenter), rotation * oldRotation);
                    flashlight.enabled = true;
                }
                if (availableViews != null)
                    DrawNested(projection, nearCorner, flashlight, fog, availableViews);
                if (fog != null)
                    RenderSettings.fog = false;
                camera.Render();
            }
            finally
            {
                if (lightPose)
                {
                    lightPose.SetPositionAndRotation(oldPosition, oldRotation);
                    flashlight.enabled = lightWasEnabled;
                }
                if (fog != null)
                    RenderSettings.fog = fogWasEnabled;
            }
        }

        private void AddFog(Fog fog)
        {
            if (!fogMaterial)
                fogMaterial = new Material(Contour4.PortalShader);
            Color color = RenderSettings.fogColor;
            fogMaterial.SetVector("_PortalFogColor", QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color);
            fogMaterial.SetVector("_PortalFogSettings", new Vector4(RenderSettings.fogStartDistance,
                RenderSettings.fogEndDistance, RenderSettings.fogDensity, fog.excludeSkybox ? 1 : 0));
            fogMaterial.SetFloat("_PortalNear", camera.nearClipPlane);
            fogMaterial.SetFloat("_FogMode", (int)RenderSettings.fogMode);
            fogMaterial.SetTexture("_PortalDepth", depth);
            fogMaterial.SetMatrix("_PortalInverseProjection", GL.GetGPUProjectionMatrix(camera.projectionMatrix, true).inverse);
            // Apply opaque fog before compositing the next portal and drawing transparent geometry.
            overlays.GetTemporaryRT(FogCopy, target.descriptor, FilterMode.Point);
            overlays.Blit(target, FogCopy);
            overlays.Blit(FogCopy, target, fogMaterial, 1);
            overlays.SetRenderTarget(target, depth);
            overlays.ReleaseTemporaryRT(FogCopy);
        }

        private void DrawNested(Matrix4x4 projection, float nearCorner, Light flashlight, Fog fog, IReadOnlyList<View> availableViews)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, nestedFrustum);
            var pose = camera.transform;
            foreach (var next in availableViews)
            {
                Aperture nextEntry = next.entry;
                if (nextEntry.Room != exit.Room || nextEntry.Marker == exit.Marker ||
                    !next.TryGetSurface(pose.position, nearCorner, nestedFrustum, out Matrix4x4 surface, out bool atDoor))
                    continue;

                if (!nestedViews.TryGetValue(nextEntry, out var nested))
                {
                    nested = new View(cameraParent, mesh, nextEntry, next.exit);
                    nestedViews.Add(nextEntry, nested);
                }
                nested.Draw(camera, projection, pose.position, pose.rotation, nearCorner, surface, atDoor,
                    overlays, flashlight, fog, null);
            }
        }

        private void PrepareCamera(Camera source)
        {
            if (!camera)
            {
                var helper = new GameObject("Contour-4 portal camera");
                helper.transform.SetParent(cameraParent, false);
                camera = helper.AddComponent<Camera>();
                camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, overlays);
            }
            camera.CopyFrom(source);
            // Copy camera settings without inheriting any temporary main-camera view.
            camera.ResetWorldToCameraMatrix();
            camera.ResetCullingMatrix();
            camera.enabled = false; // The portal texture must be ready before the main camera draws the panel.
            RenderTexture gameTarget = source.targetTexture;
            int width = gameTarget ? gameTarget.width : source.pixelWidth;
            int height = gameTarget ? gameTarget.height : source.pixelHeight;
            if (!target || target.width != width || target.height != height)
            {
                ReleaseTarget();
                target = new RenderTexture(width, height, 0)
                {
                    name = "Contour-4 portal view"
                };
                depth = new RenderTexture(width, height, 24, RenderTextureFormat.Depth)
                {
                    name = "Contour-4 portal depth",
                    filterMode = FilterMode.Point
                };
                target.Create();
                depth.Create();
                material.mainTexture = target;
            }
            camera.targetTexture = target;
            camera.SetTargetBuffers(target.colorBuffer, depth.depthBuffer);
            camera.cullingMask = source.cullingMask;
            camera.aspect = source.aspect;
            camera.fieldOfView = source.fieldOfView;
            camera.farClipPlane = source.farClipPlane;
            camera.useOcclusionCulling = false;
            camera.allowMSAA = false;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
        }

        internal void Dispose()
        {
            foreach (var nested in nestedViews.Values)
                nested.Dispose();
            ReleaseTarget();
            if (camera)
            {
                camera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, overlays);
                Object.Destroy(camera.gameObject);
            }
            overlays.Dispose();
            Object.Destroy(fogMaterial);
            Object.Destroy(material);
        }

        private void ReleaseTarget()
        {
            if (!target)
                return;
            camera.targetTexture = null;
            target.Release();
            Object.Destroy(target);
            depth.Release();
            Object.Destroy(depth);
        }
    }
}

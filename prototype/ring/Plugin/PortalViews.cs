using System.Collections.Generic;
using Contour4.NativeRaid.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace SpaceJourney;

[DefaultExecutionOrder(1000)]
internal sealed class PortalViews : MonoBehaviour
{
    private static readonly List<PortalViews> active = new();
    private static int lastBudgetRender = -1;
    private readonly List<View> views = new();
    private readonly List<VisibleSurface> visible = new();
    private int budgetWidth;
    private int budgetHeight;
    private Texture2D budgetCap;
    private readonly List<View> rendered = new();
    private readonly TargetPool targets = new();
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
        foreach (var view in views)
            view.PrepareNestedViews(views);
        visible.Capacity = views.Count;
        if (UsesBudget() && GameDirector.instance && SemiFunc.MainCamera() is Camera main && main)
            PrepareBudget(main);
        StartRendering();
    }

    private void OnEnable() => StartRendering();

    private void StartRendering()
    {
        if (listening || views.Count == 0 || !isActiveAndEnabled)
            return;
        listening = true;
        active.Add(this);
        Camera.onPreCull += RenderPortals;
        Camera.onPostRender += RestoreMainView;
    }

    // On the pinned game build the stock Mirror Quality setting defaults to 2.
    // Apply the shared portal budget at its lowest value.
    private static bool UsesBudget() => DataDirector.instance &&
        DataDirector.instance.SettingValueFetch(DataDirector.Setting.MirrorQuality) == 0;

    private void BeginFrame(Camera main)
    {
        if (main != source)
        {
            if (source)
            {
                RestoreMainView(source);
                source.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, panels);
            }
            source = main;
            source.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, panels);
        }
        panels.Clear();
        if (ring)
            ring.RefreshPortalImages();
    }

    private void RenderPortals(Camera main)
    {
        if (!GameDirector.instance || main != SemiFunc.MainCamera())
            return;
        bool budgeted = UsesBudget();
        if (budgeted)
        {
            if (active.Count == 0 || active[0] != this)
                return;
            foreach (var host in active)
            {
                host.BeginFrame(main);
                host.PrepareBudget(main);
            }
        }
        else
        {
            BeginFrame(main);
            targets.Prepare(main);
        }

        Matrix4x4 cameraToWorld = main.cameraToWorldMatrix;
        Vector3 eye = cameraToWorld.GetColumn(3);
        Quaternion viewRotation = Quaternion.LookRotation(-(Vector3)cameraToWorld.GetColumn(2), cameraToWorld.GetColumn(1));
        float nearCorner = RingRuntime.NearCorner(main);
        var flashlight = FlashlightController.Instance ? FlashlightController.Instance.spotlight : null;
        var postProcessing = main.GetComponent<PostProcessLayer>();
        Fog fog = main.actualRenderingPath == RenderingPath.DeferredShading && postProcessing && postProcessing.isActiveAndEnabled
            && postProcessing.fog != null && postProcessing.fog.enabled && RenderSettings.fog ? postProcessing.fog : null;
        Matrix4x4 projection = main.projectionMatrix;
        Light beam = null;
        bool flashlightPrepared = false;
        bool flashlightWasEnabled = flashlight && flashlight.enabled;
        PortalViews owner = this;
        Crossing? ringSide = null;
        NativeRaidPortal nativeSide = null;
        if (budgeted)
        {
            foreach (var host in active)
            {
                if (!host.TryFindCameraSide(eye, nearCorner, out ringSide, out nativeSide))
                    continue;
                owner = host;
                break;
            }
        }
        else
            TryFindCameraSide(eye, nearCorner, out ringSide, out nativeSide);

        if (ringSide.HasValue || nativeSide != null)
        {
            // The camera can enter the linked room before the player's center reorients the ring.
            Quaternion turn = ringSide is Crossing eyeSide ? eyeSide.Rotation : nativeSide.Rotation;
            Vector3 Map(Vector3 point) => ringSide is Crossing crossing ? crossing.TransformPoint(point) : nativeSide.MapPoint(point);
            owner.originalEye = eye;
            owner.originalMainView = main.worldToCameraMatrix;
            owner.wasDefaultView = NearDefaultView(owner.originalMainView, main.transform);
            owner.shiftedEye = true;
            beam = owner.PrepareFlashlight(flashlight);
            flashlightPrepared = true;
            if (beam)
            {
                owner.shiftedFlashlight = flashlight;
                flashlight.enabled = false;
                Transform light = beam.transform;
                light.SetPositionAndRotation(Map(light.position), turn * light.rotation);
            }
            eye = Map(eye);
            viewRotation = turn * viewRotation;
            main.worldToCameraMatrix = WorldToCamera(eye, viewRotation);
        }

        try
        {
            GeometryUtility.CalculateFrustumPlanes(main, frustum);
            if (budgeted)
            {
                owner.DrawBudgeted(main, projection, eye, viewRotation, nearCorner, frustum, flashlight, beam, fog);
            }
            else
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
                    rendered.Add(view);
                    view.Draw(main, projection, eye, viewRotation, nearCorner, surface, atDoor, panels, beam, fog, targets);
                }
            }
            if (owner.shiftedEye && beam)
                beam.enabled = true;
        }
        catch
        {
            owner.RestoreMainView(main);
            if (owner != this)
                RestoreMainView(main);
            throw;
        }
        finally
        {
            if (!owner.shiftedEye && flashlight)
                flashlight.enabled = flashlightWasEnabled;
        }
    }

    private bool TryFindCameraSide(Vector3 eye, float nearCorner, out Crossing? ringSide, out NativeRaidPortal nativeSide)
    {
        ringSide = ring ? ring.CameraSide(eye, nearCorner) : null;
        nativeSide = !ring ? proceduralCameraSide?.Invoke(eye, nearCorner) : null;
        return ringSide.HasValue || nativeSide != null;
    }

    private void PrepareBudget(Camera main)
    {
        RenderTexture gameTarget = main.targetTexture;
        int width = Mathf.Max(1, gameTarget ? gameTarget.width : main.pixelWidth);
        int height = Mathf.Max(1, gameTarget ? gameTarget.height : main.pixelHeight);
        if (width == budgetWidth && height == budgetHeight)
            return;
        budgetWidth = width;
        budgetHeight = height;

        // Two fixed sizes for this viewport. Moving through a doorway only switches textures.
        float scale = Mathf.Min(.5f, Mathf.Min(512f / width, 288f / height));
        int nearWidth = Mathf.Max(1, Mathf.RoundToInt(width * scale));
        int nearHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));
        foreach (var view in views)
            view.PrepareBudgetTargets(nearWidth, nearHeight);
        if (!budgetCap)
        {
            budgetCap = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "Contour-4 portal cap" };
            budgetCap.SetPixel(0, 0, new Color32(34, 41, 48, 255));
            budgetCap.Apply(false, true);
        }
        EnsurePortalFlashlight();
    }

    private void DrawBudgeted(Camera main, Matrix4x4 projection, Vector3 eye, Quaternion rotation, float nearCorner,
        Plane[] planes, Light flashlight, Light shiftedBeam, Fog fog)
    {
        int frame = Time.frameCount;
        Matrix4x4 worldToClip = projection * main.worldToCameraMatrix;
        View selected = null;
        BudgetPriority best = default;
        foreach (var host in active)
        {
            host.visible.Clear();
            foreach (var view in host.views)
            {
                if (!view.TryGetSurface(eye, nearCorner, planes, out Matrix4x4 surface, out bool atDoor))
                    continue;
                float coverage = atDoor ? 1 : ScreenCoverage(worldToClip * surface);
                int age = view.CacheAge(frame);
                host.visible.Add(new VisibleSurface(view, surface, atDoor, coverage));
                var priority = new BudgetPriority(atDoor && !view.HasCompatibleImage(eye, rotation, projection, age, true),
                    age, coverage);
                if (selected != null && !priority.Beats(best))
                    continue;
                selected = view;
                best = priority;
            }
        }

        bool refresh = selected != null && lastBudgetRender != frame;
        Light beam = shiftedBeam;
        if (refresh && !shiftedEye)
        {
            beam = PrepareFlashlight(flashlight);
            if (beam)
                flashlight.enabled = false;
        }

        foreach (var host in active)
        {
            foreach (var item in host.visible)
            {
                if (refresh && item.View == selected)
                {
                    lastBudgetRender = frame;
                    item.View.RefreshBudgeted(main, projection, eye, rotation, nearCorner,
                        item.Coverage >= .08f, beam, fog, host.budgetCap, frame);
                }
                item.View.DrawBudgetedPanel(main, eye, rotation, projection, item.Surface, item.AtDoor,
                    host.panels, host.budgetCap, frame);
            }
        }
    }

    private static float ScreenCoverage(Matrix4x4 clip)
    {
        float left = 1, right = -1, bottom = 1, top = -1;
        for (int corner = 0; corner < 4; corner++)
        {
            Vector4 point = clip * new Vector4((corner & 1) == 0 ? -.5f : .5f, (corner & 2) == 0 ? -.5f : .5f, 0, 1);
            float divisor = Mathf.Max(.0001f, point.w);
            float x = Mathf.Clamp(point.x / divisor, -1, 1);
            float y = Mathf.Clamp(point.y / divisor, -1, 1);
            left = Mathf.Min(left, x);
            right = Mathf.Max(right, x);
            bottom = Mathf.Min(bottom, y);
            top = Mathf.Max(top, y);
        }
        return (right - left) * (top - bottom) * .25f;
    }

    private readonly struct VisibleSurface
    {
        internal readonly View View;
        internal readonly Matrix4x4 Surface;
        internal readonly bool AtDoor;
        internal readonly float Coverage;

        internal VisibleSurface(View view, Matrix4x4 surface, bool atDoor, float coverage)
        {
            View = view;
            Surface = surface;
            AtDoor = atDoor;
            Coverage = coverage;
        }
    }

    private readonly struct BudgetPriority
    {
        private readonly bool urgent;
        private readonly int age;
        private readonly float coverage;

        internal BudgetPriority(bool urgent, int age, float coverage)
        {
            this.urgent = urgent;
            this.age = age;
            this.coverage = coverage;
        }

        internal bool Beats(BudgetPriority best)
        {
            if (urgent != best.urgent)
                return urgent;
            bool due = age >= 3;
            bool bestDue = best.age >= 3;
            if (due != bestDue)
                return due;
            if (due && age != best.age)
                return age > best.age;
            return coverage > best.coverage;
        }
    }

    private void RestoreMainView(Camera main)
    {
        if (main != source)
            return;
        foreach (var view in rendered)
            view.ReturnTarget(targets);
        rendered.Clear();
        if (!shiftedEye)
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

    internal Vector3 UnshiftedEye(Camera camera)
    {
        foreach (var host in active)
            if (host.shiftedEye && host.source == camera)
                return host.originalEye;
        return camera.cameraToWorldMatrix.GetColumn(3);
    }

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

    private void EnsurePortalFlashlight()
    {
        if (!portalFlashlight)
        {
            var helper = new GameObject("Contour-4 portal flashlight");
            helper.SetActive(false);
            portalFlashlight = helper.AddComponent<Light>();
            portalFlashlight.enabled = false;
            helper.SetActive(true);
        }

    }

    private Light PrepareFlashlight(Light native)
    {
        if (!native || !native.isActiveAndEnabled)
            return null;
        EnsurePortalFlashlight();

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
        active.Remove(this);
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
        targets.Dispose();
        if (budgetCap)
            Destroy(budgetCap);
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

    // A render target is kept until the camera that composites it finishes. One small pool
    // serves the visible views, so crossing a previously unseen door doesn't allocate a new
    // full-screen color and depth texture just for that doorway.
    private sealed class TargetPool
    {
        private readonly Stack<Target> free = new();
        private int width, height;

        internal void Prepare(Camera source)
        {
            RenderTexture gameTarget = source.targetTexture;
            int nextWidth = gameTarget ? gameTarget.width : source.pixelWidth;
            int nextHeight = gameTarget ? gameTarget.height : source.pixelHeight;
            if (nextWidth == width && nextHeight == height)
                return;

            Dispose();
            width = nextWidth;
            height = nextHeight;
            // Provision two main views and one nested view before the player reaches a door.
            while (free.Count < 3)
                free.Push(new Target(width, height));
        }

        internal Target Take() => free.Count == 0 ? new Target(width, height) : free.Pop();
        internal void Return(Target target) => free.Push(target);

        internal void Dispose()
        {
            while (free.Count > 0)
                free.Pop().Dispose();
        }
    }

    private sealed class Target
    {
        internal readonly RenderTexture Color;
        internal readonly RenderTexture Depth;
        internal readonly RenderTexture FogCopy;

        internal Target(int width, int height, bool persistent = false)
        {
            Color = new RenderTexture(width, height, 0) { name = "Contour-4 portal view" };
            Depth = new RenderTexture(width, height, 24, RenderTextureFormat.Depth)
            {
                name = "Contour-4 portal depth",
                filterMode = FilterMode.Point
            };
            Color.Create();
            Depth.Create();
            if (persistent)
            {
                FogCopy = new RenderTexture(Color.descriptor) { name = "Contour-4 portal fog", filterMode = FilterMode.Point };
                FogCopy.Create();
            }
        }

        internal void Dispose()
        {
            Color.Release();
            Object.Destroy(Color);
            Depth.Release();
            Object.Destroy(Depth);
            if (FogCopy)
            {
                FogCopy.Release();
                Object.Destroy(FogCopy);
            }
        }
    }

    private sealed class View
    {
        private readonly Aperture entry;
        private readonly Aperture exit;
        private readonly Transform cameraParent;
        private readonly Material material;
        private readonly Mesh mesh;
        private readonly List<View> nestedViews = new();
        private readonly List<View> renderedNested = new();
        private readonly MaterialPropertyBlock nearSurface = new();
        private readonly Plane[] nestedFrustum = new Plane[6];
        private readonly CommandBuffer overlays = new() { name = "Contour-4 portal effects and surfaces" };
        private static readonly int FogCopy = Shader.PropertyToID("_Contour4PortalFogCopy");
        private readonly Camera camera;
        private Target target;
        private Target budgetNear;
        private Target budgetFar;
        private Target cachedBudget;
        private Vector3 cachedEye;
        private Quaternion cachedRotation;
        private int cachedFrame;
        private Vector2 cachedProjectionScale;
        private readonly Material fogMaterial;

        internal View(Transform cameraParent, Mesh mesh, Aperture entry, Aperture exit)
        {
            this.cameraParent = cameraParent;
            this.mesh = mesh;
            this.entry = entry;
            this.exit = exit;
            material = new Material(Contour4.PortalShader);
            fogMaterial = new Material(Contour4.PortalShader);
            var helper = new GameObject("Contour-4 portal camera");
            helper.transform.SetParent(cameraParent, false);
            camera = helper.AddComponent<Camera>();
            camera.enabled = false;
            camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, overlays);
        }

        internal void PrepareNestedViews(IReadOnlyList<View> available)
        {
            foreach (var next in available)
            {
                if (next.entry.Room == exit.Room && next.entry.Marker != exit.Marker)
                    nestedViews.Add(new View(cameraParent, mesh, next.entry, next.exit));
            }
        }

        internal void Draw(Camera source, Matrix4x4 projection, Vector3 eye, Quaternion viewRotation, float nearCorner,
            Matrix4x4 surface, bool atDoor, CommandBuffer panels, Light flashlight, Fog fog, TargetPool targets)
        {
            target = targets.Take();
            RenderView(source, projection, eye, viewRotation, nearCorner, flashlight, fog, targets, null);
            DrawPanel(source, eye, surface, atDoor, panels, target.Color);
        }

        internal void PrepareBudgetTargets(int width, int height)
        {
            budgetNear?.Dispose();
            budgetFar?.Dispose();
            budgetNear = new Target(width, height, true);
            budgetFar = new Target(Mathf.Max(1, width / 2), Mathf.Max(1, height / 2), true);
            cachedBudget = null;
        }

        internal int CacheAge(int frame) => cachedBudget != null ? frame - cachedFrame : int.MaxValue;

        internal bool HasCompatibleImage(Vector3 eye, Quaternion rotation, Matrix4x4 projection, int age, bool atDoor) =>
            age <= 8 && cachedBudget.Color.IsCreated() && (eye - cachedEye).sqrMagnitude <= (atDoor ? .0225f : .2025f) &&
            Mathf.Abs(Quaternion.Dot(rotation, cachedRotation)) >= (atDoor ? .99905f : .9945f) &&
            (new Vector2(projection.m00, projection.m11) - cachedProjectionScale).sqrMagnitude <= .0025f;

        internal void RefreshBudgeted(Camera source, Matrix4x4 projection, Vector3 eye, Quaternion rotation,
            float nearCorner, bool large, Light flashlight, Fog fog, Texture cap, int frame)
        {
            target = large ? budgetNear : budgetFar;
            cachedBudget = null;
            try
            {
                RenderView(source, projection, eye, rotation, nearCorner, flashlight, fog, null, cap);
                cachedBudget = target;
                cachedEye = eye;
                cachedRotation = rotation;
                cachedFrame = frame;
                cachedProjectionScale = new Vector2(projection.m00, projection.m11);
            }
            finally
            {
                camera.targetTexture = null;
                target = null;
            }
        }

        internal void DrawBudgetedPanel(Camera source, Vector3 eye, Quaternion rotation, Matrix4x4 projection,
            Matrix4x4 surface, bool atDoor, CommandBuffer panels, Texture cap, int frame) =>
            DrawPanel(source, eye, surface, atDoor, panels,
                HasCompatibleImage(eye, rotation, projection, CacheAge(frame), atDoor) ? cachedBudget.Color : cap);

        private void RenderView(Camera source, Matrix4x4 projection, Vector3 eye, Quaternion viewRotation,
            float nearCorner, Light flashlight, Fog fog, TargetPool targets, Texture cap)
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
            Render(projection, nearCorner, flashlight, fog, targets, cap, rotation, entryCenter, exitCenter);
        }

        private void DrawPanel(Camera source, Vector3 eye, Matrix4x4 surface, bool atDoor, CommandBuffer panels, Texture image)
        {
            material.mainTexture = image;
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

        private void Render(Matrix4x4 projection, float nearCorner, Light flashlight, Fog fog, TargetPool targets,
            Texture cap, Quaternion rotation, Vector3 entryCenter, Vector3 exitCenter)
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
                if (nestedViews.Count > 0)
                    DrawNested(projection, nearCorner, flashlight, fog, targets, cap);
                if (fog != null)
                    RenderSettings.fog = false;
                camera.Render();
            }
            finally
            {
                foreach (var nested in renderedNested)
                    nested.ReturnTarget(targets);
                renderedNested.Clear();
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
            Color color = RenderSettings.fogColor;
            fogMaterial.SetVector("_PortalFogColor", QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color);
            fogMaterial.SetVector("_PortalFogSettings", new Vector4(RenderSettings.fogStartDistance,
                RenderSettings.fogEndDistance, RenderSettings.fogDensity, fog.excludeSkybox ? 1 : 0));
            fogMaterial.SetFloat("_PortalNear", camera.nearClipPlane);
            fogMaterial.SetFloat("_FogMode", (int)RenderSettings.fogMode);
            fogMaterial.SetTexture("_PortalDepth", target.Depth);
            fogMaterial.SetMatrix("_PortalInverseProjection", GL.GetGPUProjectionMatrix(camera.projectionMatrix, true).inverse);
            // Apply opaque fog before compositing the next portal and drawing transparent geometry.
            if (target.FogCopy)
            {
                overlays.Blit(target.Color, target.FogCopy);
                overlays.Blit(target.FogCopy, target.Color, fogMaterial, 1);
                overlays.SetRenderTarget(target.Color, target.Depth);
            }
            else
            {
                overlays.GetTemporaryRT(FogCopy, target.Color.descriptor, FilterMode.Point);
                overlays.Blit(target.Color, FogCopy);
                overlays.Blit(FogCopy, target.Color, fogMaterial, 1);
                overlays.SetRenderTarget(target.Color, target.Depth);
                overlays.ReleaseTemporaryRT(FogCopy);
            }
        }

        private void DrawNested(Matrix4x4 projection, float nearCorner, Light flashlight, Fog fog, TargetPool targets, Texture cap)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, nestedFrustum);
            var pose = camera.transform;
            foreach (var nested in nestedViews)
            {
                if (!nested.TryGetSurface(pose.position, nearCorner, nestedFrustum, out Matrix4x4 surface, out bool atDoor))
                    continue;

                if (cap)
                    nested.DrawPanel(camera, pose.position, surface, atDoor, overlays, cap);
                else
                {
                    renderedNested.Add(nested);
                    nested.Draw(camera, projection, pose.position, pose.rotation, nearCorner, surface, atDoor,
                        overlays, flashlight, fog, targets);
                }
            }
        }

        private void PrepareCamera(Camera source)
        {
            camera.CopyFrom(source);
            // Copy camera settings without inheriting any temporary main-camera view.
            camera.ResetWorldToCameraMatrix();
            camera.ResetCullingMatrix();
            camera.enabled = false; // The portal texture must be ready before the main camera draws the panel.
            camera.targetTexture = target.Color;
            camera.SetTargetBuffers(target.Color.colorBuffer, target.Depth.depthBuffer);
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
            foreach (var nested in nestedViews)
                nested.Dispose();
            if (target != null)
                target.Dispose();
            budgetNear?.Dispose();
            budgetFar?.Dispose();
            if (camera)
            {
                camera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, overlays);
                Object.Destroy(camera.gameObject);
            }
            overlays.Dispose();
            Object.Destroy(fogMaterial);
            Object.Destroy(material);
        }

        internal void ReturnTarget(TargetPool targets)
        {
            if (target == null)
                return;
            camera.targetTexture = null;
            material.mainTexture = null;
            targets.Return(target);
            target = null;
        }
    }
}

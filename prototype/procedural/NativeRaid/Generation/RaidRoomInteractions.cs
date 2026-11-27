using System;
using System.Collections.Generic;
using System.Linq;
using Contour4.Procedural.Catalog;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal enum TrenchShutterMode { Campaign, FixedOpen, Working }

    internal sealed class RaidRoomInteractions : MonoBehaviour
    {
        private readonly List<CabinetFront> panels = new();
        private readonly List<FallZone> pits = new();
        private readonly List<InspectionShutter> shutters = new();
        private readonly List<Material> materials = new();
        private AudioClip shutterWarning;
        internal IReadOnlyList<CabinetFront> Cabinets => panels;
        internal IReadOnlyList<InspectionShutter> Shutters => shutters;

        internal static RaidRoomInteractions Attach(RaidAssembly raid, TrenchShutterMode mode = TrenchShutterMode.Campaign)
        {
            var existing = raid.GetComponent<RaidRoomInteractions>();
            if (existing) return existing;
            var runtime = raid.gameObject.AddComponent<RaidRoomInteractions>();
            runtime.Mount(raid.Rooms, ResolveTrenchShutter(mode, raid.Plan.CompletedLevels));
            return runtime;
        }

        internal static TrenchShutterMode ResolveTrenchShutter(TrenchShutterMode mode, int completedLevels) =>
            mode == TrenchShutterMode.Campaign ? (completedLevels >= 9 ? TrenchShutterMode.Working : TrenchShutterMode.FixedOpen) : mode;

        private void Mount(IEnumerable<BakedRoom> rooms, TrenchShutterMode mode)
        {
            Material leaf = Tinted("Standard", new Color(.33f, .43f, .45f));
            Material handle = Tinted("Standard", new Color(.79f, .56f, .24f));
            foreach (BakedRoom room in rooms)
            {
                foreach (var volume in room.Plan.Module.FallVolumes)
                {
                    var (min, max) = volume.Bounds;
                    Vector3 low = RaidPlan.Vector(min), high = RaidPlan.Vector(max);
                    pits.Add(new FallZone(room.Root, new Bounds((low + high) / 2, high - low)));
                }
                Rigidbody cabinetAnchor = null;
                foreach (var dressing in room.Plan.Module.Dressing)
                foreach (var front in dressing.MovingFronts)
                {
                    var panel = Find(room.Root, dressing.Name(front.Panel));
                    var pull = Find(room.Root, dressing.Name(front.Handle));
                    var pivot = room.Prepared ? room.Root.Find("Mechanisms/" + dressing.Name(front.Panel) + " pivot") :
                        RaidAssembly.Child(room.Root, dressing.Name(front.Panel) + " pivot");
                    if (!pivot) throw new InvalidOperationException($"{room.Plan.Module.Id}: missing prepared cabinet pivot.");
                    if (!room.Prepared)
                    {
                        pivot.localPosition = RaidPlan.Vector(dressing.Point(front.Pivot));
                        pivot.localRotation = Quaternion.LookRotation(RaidPlan.Vector(dressing.Direction(System.Numerics.Vector3.UnitZ)),
                            RaidPlan.Vector(dressing.Frame.Up));
                    }
                    var parts = new List<Collider> { panel.GetComponent<Collider>(), pull.GetComponent<Collider>() };
                    if (front.Tray != null) parts.Add(Find(room.Root, dressing.Name(front.Tray)).GetComponent<Collider>());
                    if (!room.Authored)
                    {
                        panel.GetComponent<Renderer>().sharedMaterial = leaf;
                        pull.GetComponent<Renderer>().sharedMaterial = handle;
                    }
                    foreach (var part in parts)
                    {
                        if (room.Prepared && (part.transform.parent != pivot || part is not MeshCollider { convex: true }))
                            throw new InvalidOperationException($"{room.Plan.Module.Id}/{part.name}: invalid prepared moving collider.");
                        if (!room.Prepared) part.transform.SetParent(pivot, true);
                    }
                    cabinetAnchor ??= CabinetFront.MountAnchor(room.Root);
                    panels.Add(new CabinetFront(pivot, parts.ToArray(), cabinetAnchor, front.Angle, front.Travel));
                }
                if (room.Plan.Module.Id != MaintenanceTrench.Id) continue;
                var support = Find(room.Root, MaintenanceTrench.Bridge).GetComponent<Renderer>();
                support.enabled = false;
                float halfThickness = MaintenanceTrench.PlankThickness / 2;
                void Surface(string name, float from, float to, Material material) => Indicator(room.Root, name,
                    new Vector3((from + to) / 2, -halfThickness, MaintenanceTrench.BridgeZ),
                    new Vector3(to - from, MaintenanceTrench.PlankThickness, 2 * MaintenanceTrench.PlankHalfWidth), material);
                Surface("Plank landing west", 0, MaintenanceTrench.PitStart, support.sharedMaterial);
                Surface("Plank landing east", MaintenanceTrench.PitEnd, MaintenanceTrench.Length, support.sharedMaterial);
                Surface("Wood over shaft", MaintenanceTrench.PitStart, MaintenanceTrench.PitEnd,
                    Tinted("Standard", new Color(.39f, .23f, .12f)));
                MarkTrenchRoutes(room);
                Transform gate = Find(room.Root, MaintenanceTrench.Gate);
                gate.GetComponent<Renderer>().sharedMaterial = Tinted("Standard", new Color(.27f, .29f, .29f));
                bool working = mode == TrenchShutterMode.Working;
                Material signal = Tinted("Unlit/Color", working ? InspectionShutter.ReadyColor : InspectionShutter.LockedColor);
                foreach (float sign in new[] { -1f, 1f })
                {
                    Indicator(room.Root, "Shutter status", new Vector3(MaintenanceTrench.GateX + sign * .17f,
                        2.24f, MaintenanceTrench.BridgeZ), new Vector3(.13f, .14f, .68f), signal);
                    if (!working)
                        Indicator(room.Root, "Shutter retaining bar", new Vector3(MaintenanceTrench.GateX + sign * .18f,
                            2.60f, MaintenanceTrench.BridgeZ), new Vector3(.15f, .12f, MaintenanceTrench.Opening + .20f), handle);
                    Indicator(room.Root, "Trench edge", new Vector3(sign < 0 ? 5.98f : 11.52f, -.18f,
                        -3.28f), new Vector3(.045f, .15f, 1.3f), handle);
                    Indicator(room.Root, "Trench edge", new Vector3(sign < 0 ? 5.98f : 11.52f, -.18f,
                        -6.0f), new Vector3(.045f, .15f, 1.45f), handle);
                }
                shutters.Add(new InspectionShutter(room.Root, gate, signal, working ? MountWarning(room.Root) : null));
            }
        }

        private void MarkTrenchRoutes(BakedRoom room)
        {
            var glass = RaidSightStrips.Glazing(Shader.Find("Standard"), "Trench safety glazing", new Color(.28f, .38f, .43f, .085f));
            materials.Add(glass);
            Glaze("freight-pit-rail", .13f, 1.02f);
            Glaze(MaintenanceTrench.Stand, .15f, 1.34f, -.92f);
            Glaze("shutter-north-guide", .17f, 1.14f);
            Glaze("shutter-stand-return", .17f, 1.14f, -2.28f);

            var wait = Tinted("Unlit/Color", new Color(.96f, .62f, .11f));
            var cargo = Tinted("Unlit/Color", new Color(.24f, .65f, .87f));
            const float z = MaintenanceTrench.BridgeZ;
            foreach (var (x, label) in new[] { (MaintenanceTrench.WestWait, "West shutter wait"),
                (MaintenanceTrench.EastWait, "East shutter wait") })
            {
                // Open brackets leave the standing area and the hand-lane centerline readable.
                var group = RaidAssembly.Child(room.Root, label);
                foreach (float side in new[] { -1f, 1f })
                {
                    var corner = new Vector3(x, .010f, z + side * .53f);
                    Indicator(group, "Wait stripe", corner, new Vector3(.94f, .011f, .09f), wait);
                    foreach (float end in new[] { -.43f, .43f })
                        Indicator(group, "Wait bracket", corner + new Vector3(end, 0, -side * .10f),
                            new Vector3(.08f, .011f, .20f), wait);
                }
            }
            Vector3 heading = new Vector3(1, 0, 1).normalized;
            Vector3 across = Vector3.Cross(Vector3.up, heading);
            var entry = RaidAssembly.Child(room.Root, "Freight detour arrows");
            foreach (Vector3 tip in new[] { new Vector3(3.65f, .010f, .83f), new Vector3(5.15f, .010f, 2.48f) })
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 tail = tip - heading * .48f + across * (side * .35f);
                Vector3 line = tip - tail;
                Indicator(entry, "Freight chevron", (tip + tail) * .5f, new Vector3(.10f, .011f, line.magnitude), cargo)
                    .localRotation = Quaternion.LookRotation(line, Vector3.up);
            }

            void Glaze(string id, float sill, float header, float? edge = null)
            {
                var solid = Find(room.Root, id).GetComponent<Renderer>();
                var (minimum, maximum) = room.Plan.Module.Geometry.Single(block => block.Id == id).Bounds;
                var min = RaidPlan.Vector(minimum);
                var max = RaidPlan.Vector(maximum);
                float limit = edge ?? max.z;
                solid.enabled = false;
                // The original full-length collider still retains cargo, closes the shutter's side and protects the pit.
                var window = RaidAssembly.Child(room.Root, id + " sight window");
                Panel("Sill", min, new Vector3(max.x, sill, limit), solid.sharedMaterial);
                Panel("Header", new Vector3(min.x, header, min.z), new Vector3(max.x, max.y, limit), solid.sharedMaterial);
                if (edge.HasValue)
                    Panel("Panel", new Vector3(min.x, min.y, limit), max, solid.sharedMaterial);
                const float upright = .035f;
                foreach (float x in new[] { min.x, max.x - upright })
                foreach (float z in new[] { min.z, limit - upright })
                    Panel("Frame upright", new Vector3(x, sill, z), new Vector3(x + upright, header, z + upright), solid.sharedMaterial);
                Panel("Glazing", new Vector3(min.x, sill, min.z), new Vector3(max.x, header, limit), glass);

                void Panel(string name, Vector3 low, Vector3 high, Material material) =>
                    Indicator(window, name, (low + high) / 2, high - low, material);
            }
        }

        private AudioSource MountWarning(Transform room)
        {
            var marker = RaidAssembly.Child(room, "Shutter warning speaker");
            marker.localPosition = new Vector3(MaintenanceTrench.GateX, 2.10f, MaintenanceTrench.BridgeZ);
            var source = marker.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1;
            source.dopplerLevel = 0;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2.8f;
            source.maxDistance = 12f;
            source.volume = .65f;
            source.clip = shutterWarning ? shutterWarning : (shutterWarning = CreateWarning());
            return source;
        }

        private static AudioClip CreateWarning()
        {
            const int sampleRate = 22050;
            var samples = new float[(int)(sampleRate * .62f)];
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)sampleRate;
                float pulse = time < .31f ? time : time - .31f;
                float envelope = Mathf.Clamp01(Mathf.Min(pulse, .22f - pulse) / .02f);
                float hz = time < .31f ? 620 : 860;
                samples[i] = .55f * envelope * Mathf.Sin(2 * Mathf.PI * hz * pulse);
            }
            var clip = AudioClip.Create("Contour-4 shutter warning", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static Transform Find(Transform root, string id)
        {
            var transform = root.Find(id);
            if (!transform)
                foreach (var part in root.GetComponentsInChildren<Collider>(true))
                    if (part.name == id) { transform = part.transform; break; }
            return transform ? transform : throw new InvalidOperationException($"Missing raid fixture {root.name}/{id}.");
        }

        private Material Tinted(string shaderName, Color color)
        {
            var shader = Shader.Find(shaderName);
            if (!shader) shader = Shader.Find("Standard");
            var material = new Material(shader) { color = color };
            materials.Add(material);
            return material;
        }

        private static Transform Indicator(Transform root, string name, Vector3 center, Vector3 size, Material color)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = name;
            marker.transform.SetParent(root, false);
            marker.transform.localPosition = center;
            marker.transform.localScale = size;
            marker.GetComponent<BoxCollider>().enabled = false;
            var renderer = marker.GetComponent<Renderer>();
            renderer.sharedMaterial = color;
            if (color.renderQueue >= 3000) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return marker.transform;
        }

        internal bool CatchesFall(Vector3 previous, Vector3 current)
        {
            foreach (var pit in pits)
                if (pit.Catches(previous, current)) return true;
            return false;
        }

        internal bool TryShaftUp(Vector3 point, out Vector3 up)
        {
            foreach (var pit in pits)
                if (pit.TryUp(point, out up)) return true;
            up = default;
            return false;
        }

        internal bool Advance(Vector3 feet, float height, float elapsed, int occupiedLayers)
        {
            bool hurt = false;
            foreach (var shutter in shutters) hurt |= shutter.Move(feet, height, elapsed, occupiedLayers);
            return hurt;
        }

        private void OnDestroy()
        {
            foreach (Material material in materials)
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            if (shutterWarning)
            {
                if (Application.isPlaying) Destroy(shutterWarning); else DestroyImmediate(shutterWarning);
            }
        }

        private readonly struct FallZone
        {
            private readonly Transform root;
            private readonly Bounds bounds;

            internal FallZone(Transform root, Bounds bounds)
            {
                this.root = root;
                this.bounds = bounds;
            }

            internal bool Catches(Vector3 previous, Vector3 current)
            {
                Vector3 end = root.InverseTransformPoint(current);
                if (bounds.Contains(end)) return true;
                Vector3 start = root.InverseTransformPoint(previous);
                Vector3 move = end - start;
                // A bounded sweep catches high-speed falls without connecting unrelated teleported positions.
                return move.y < 0 && move.sqrMagnitude <= 25 && bounds.IntersectRay(new Ray(start, move), out float distance) &&
                    distance <= move.magnitude;
            }

            internal bool TryUp(Vector3 point, out Vector3 up)
            {
                Vector3 local = root.InverseTransformPoint(point);
                Vector3 min = bounds.min, max = bounds.max;
                up = root.up;
                return local.y >= min.y && local.y <= .5f && local.x >= min.x && local.x <= max.x &&
                    local.z >= min.z && local.z <= max.z;
            }
        }
    }

    internal sealed class InspectionShutter
    {
        internal static readonly Color ReadyColor = new(.16f, .72f, .42f);
        internal static readonly Color LockedColor = new(.28f, .62f, .93f);
        private static readonly Color WarningColor = new(1f, .66f, .08f);
        private static readonly Color ClosedColor = new(.92f, .18f, .07f);
        internal enum Stage { FixedOpen, Armed, Warning, Closing, Closed, Opening, Rearming }

        private readonly Transform room, gate;
        private readonly Vector3 raised, bladeCenter, bladeSize;
        private readonly Material signal;
        private readonly AudioSource warning;
        private float timer, depth;
        internal Stage State { get; private set; }
        internal int WarningCues { get; private set; }

        internal InspectionShutter(Transform room, Transform gate, Material signal, AudioSource warning)
        {
            this.room = room;
            this.gate = gate.parent == room ? gate : gate.parent;
            this.signal = signal;
            this.warning = warning;
            State = warning ? Stage.Armed : Stage.FixedOpen;
            raised = this.gate.localPosition;
            var collider = gate.GetComponent<Collider>();
            Bounds shape = collider switch
            {
                BoxCollider box => new Bounds(box.center, box.size),
                MeshCollider mesh => mesh.sharedMesh.bounds,
                _ => throw new InvalidOperationException("The inspection shutter requires a solid blade."),
            };
            bladeCenter = room.InverseTransformPoint(gate.TransformPoint(shape.center));
            bladeSize = Vector3.Scale(shape.size, gate.localScale);
        }

        internal bool Move(Vector3 feet, float height, float elapsed, int occupiedLayers)
        {
            if (State == Stage.FixedOpen) return false;
            Vector3 local = room.InverseTransformPoint(feet);
            float offset = local.x - MaintenanceTrench.GateX;
            float z = Mathf.Abs(local.z - MaintenanceTrench.BridgeZ);
            bool approached = offset > -1.25f && offset < 2.05f && z < MaintenanceTrench.Opening / 2 &&
                local.y > -.18f && local.y < 1.4f;
            switch (State)
            {
                case Stage.Armed:
                    if (approached)
                    {
                        timer = 1.35f;
                        Set(Stage.Warning, WarningColor);
                        WarningCues++;
                        if (Application.isPlaying) warning.Play();
                    }
                    break;
                case Stage.Warning:
                    if ((timer -= elapsed) <= 0) Set(Stage.Closing, WarningColor);
                    break;
                case Stage.Closing:
                    float next = Mathf.MoveTowards(depth, MaintenanceTrench.GateTravel, elapsed * 2.25f);
                    float edge = bladeCenter.y - next - bladeSize.y / 2;
                    bool hit = local.y + height >= edge && local.y < edge + bladeSize.y &&
                        Mathf.Abs(offset) < bladeSize.x / 2 + .32f && z < MaintenanceTrench.Opening / 2 + .24f;
                    if (hit || Physics.CheckBox(room.TransformPoint(bladeCenter - Vector3.up * next), bladeSize / 2,
                        room.rotation, occupiedLayers, QueryTriggerInteraction.Ignore))
                    {
                        Set(Stage.Opening, ClosedColor);
                        return hit;
                    }
                    Slide(next);
                    if (next == MaintenanceTrench.GateTravel) { timer = 1.65f; Set(Stage.Closed, ClosedColor); }
                    break;
                case Stage.Closed:
                    if ((timer -= elapsed) <= 0) Set(Stage.Opening, ClosedColor);
                    break;
                case Stage.Opening:
                    Slide(Mathf.MoveTowards(depth, 0, elapsed * 3f));
                    if (depth == 0) Set(Stage.Rearming, ReadyColor);
                    break;
                case Stage.Rearming:
                    if (!approached) Set(Stage.Armed, ReadyColor);
                    break;
            }
            return false;
        }

        private void Slide(float extension)
        {
            depth = extension;
            gate.localPosition = raised - Vector3.up * depth;
        }

        private void Set(Stage stage, Color color)
        {
            State = stage;
            signal.color = color;
        }
    }
}

using System;
using System.Collections.Generic;
using Contour4.Procedural.Catalog;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class RaidRoomInteractions : MonoBehaviour
    {
        private readonly List<CabinetFront> panels = new();
        private readonly List<FallZone> pits = new();
        private readonly List<InspectionShutter> shutters = new();
        private readonly List<Material> materials = new();
        internal IReadOnlyList<CabinetFront> Cabinets => panels;
        internal IReadOnlyList<InspectionShutter> Shutters => shutters;

        internal static RaidRoomInteractions Attach(RaidAssembly raid)
        {
            var existing = raid.GetComponent<RaidRoomInteractions>();
            if (existing) return existing;
            var runtime = raid.gameObject.AddComponent<RaidRoomInteractions>();
            runtime.Mount(raid.Rooms);
            return runtime;
        }

        private void Mount(IEnumerable<BakedRoom> rooms)
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
                    var pivot = RaidAssembly.Child(room.Root, dressing.Name(front.Panel) + " pivot");
                    pivot.localPosition = RaidPlan.Vector(dressing.Point(front.Pivot));
                    pivot.localRotation = Quaternion.LookRotation(RaidPlan.Vector(dressing.Direction(System.Numerics.Vector3.UnitZ)),
                        RaidPlan.Vector(dressing.Frame.Up));
                    var parts = new List<BoxCollider> { panel.GetComponent<BoxCollider>(), pull.GetComponent<BoxCollider>() };
                    if (front.Tray != null) parts.Add(Find(room.Root, dressing.Name(front.Tray)).GetComponent<BoxCollider>());
                    panel.GetComponent<Renderer>().sharedMaterial = leaf;
                    pull.GetComponent<Renderer>().sharedMaterial = handle;
                    foreach (var part in parts) part.transform.SetParent(pivot, true);
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
                Transform gate = Find(room.Root, MaintenanceTrench.Gate);
                gate.GetComponent<Renderer>().sharedMaterial = Tinted("Standard", new Color(.27f, .29f, .29f));
                Material signal = Tinted("Unlit/Color", InspectionShutter.ReadyColor);
                foreach (float sign in new[] { -1f, 1f })
                {
                    Indicator(room.Root, "Shutter status", new Vector3(MaintenanceTrench.GateX + sign * .17f,
                        2.24f, MaintenanceTrench.BridgeZ), new Vector3(.13f, .14f, .68f), signal);
                    Indicator(room.Root, "Trench edge", new Vector3(sign < 0 ? 5.98f : 11.52f, -.18f,
                        -3.28f), new Vector3(.045f, .15f, 1.3f), handle);
                    Indicator(room.Root, "Trench edge", new Vector3(sign < 0 ? 5.98f : 11.52f, -.18f,
                        -6.0f), new Vector3(.045f, .15f, 1.45f), handle);
                }
                shutters.Add(new InspectionShutter(room.Root, gate, signal));
            }
        }

        private static Transform Find(Transform root, string id)
        {
            var transform = root.Find(id);
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

        private static void Indicator(Transform root, string name, Vector3 center, Vector3 size, Material color)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = name;
            marker.transform.SetParent(root, false);
            marker.transform.localPosition = center;
            marker.transform.localScale = size;
            marker.GetComponent<BoxCollider>().enabled = false;
            marker.GetComponent<Renderer>().sharedMaterial = color;
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
        private static readonly Color WarningColor = new(1f, .66f, .08f);
        private static readonly Color ClosedColor = new(.92f, .18f, .07f);
        internal enum Stage { Armed, Warning, Closing, Closed, Opening, Rearming }

        private readonly Transform room, gate;
        private readonly Vector3 raised;
        private readonly BoxCollider blade;
        private readonly Material signal;
        private float timer, depth;
        internal Stage State { get; private set; }

        internal InspectionShutter(Transform room, Transform gate, Material signal)
        {
            this.room = room;
            this.gate = gate;
            this.signal = signal;
            raised = gate.localPosition;
            blade = gate.GetComponent<BoxCollider>();
        }

        internal bool Move(Vector3 feet, float height, float elapsed, int occupiedLayers)
        {
            Vector3 local = room.InverseTransformPoint(feet);
            float x = Mathf.Abs(local.x - MaintenanceTrench.GateX);
            float z = Mathf.Abs(local.z - MaintenanceTrench.BridgeZ);
            bool approached = x < 1.8f && z < MaintenanceTrench.Opening / 2 && local.y > -.18f && local.y < 1.4f;
            switch (State)
            {
                case Stage.Armed:
                    if (approached) { timer = 1.35f; Set(Stage.Warning, WarningColor); }
                    break;
                case Stage.Warning:
                    if ((timer -= elapsed) <= 0) Set(Stage.Closing, WarningColor);
                    break;
                case Stage.Closing:
                    float next = Mathf.MoveTowards(depth, MaintenanceTrench.GateTravel, elapsed * 2.25f);
                    float edge = raised.y - next - gate.localScale.y / 2;
                    bool hit = local.y + height >= edge && local.y < edge + gate.localScale.y &&
                        x < gate.localScale.x / 2 + .32f && z < MaintenanceTrench.Opening / 2 + .24f;
                    Vector3 half = Vector3.Scale(blade.size, gate.localScale) / 2;
                    if (hit || Physics.CheckBox(room.TransformPoint(raised - Vector3.up * next), half,
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

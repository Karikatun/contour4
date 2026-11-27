using System;
using System.Collections.Generic;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class RaidQuotaRotaryCabin : MonoBehaviour
    {
        internal const float HalfWidth = 2.2f;
        internal const float HalfHeight = 1.65f;
        internal const float HalfLength = 3.1f;
        internal const float Shell = .16f;
        internal const float Duration = 2.8f;
        private const float GateClearance = .25f;
        private readonly HashSet<Rigidbody> passengers = new();
        private readonly List<PassengerPose> poses = new();
        private Transform rotor, farDock;
        private GameObject nearGate, farGate;
        private Bounds farField;
        private Rigidbody rider;
        private float travel, turnDegrees, interruptedTurn;
        private bool atFar, turning, returning, recalling;

        internal Transform Button { get; private set; }
        internal Transform Rotor => rotor;
        internal bool IsTurning => turning;
        internal bool NearOpen => !nearGate.activeSelf;
        internal bool FarOpen => !farGate.activeSelf;
        internal IEnumerable<Rigidbody> Passengers => passengers;
        internal Transform DockFrame(bool far) => far ? farDock : nearGate.transform.parent;

        // Floor centers are coplanar with the cabin deck at the two docking planes.
        // The supplied far gravity bounds are measured from its floor center in the dock frame.
        internal static Pose Doorway(Vector3 center, Quaternion nearRotation, bool far, float turnDegrees = 90f)
        {
            Quaternion rotation = nearRotation * Quaternion.AngleAxis(far ? turnDegrees : 0f, Vector3.forward);
            Vector3 position = center + rotation * new Vector3(0, -HalfHeight, far ? HalfLength : -HalfLength);
            return new Pose(position, rotation);
        }

        internal static RaidQuotaRotaryCabin Create(Transform room, Vector3 center, Quaternion nearRotation,
            Bounds farField, int layer, Material frame, Material gate, Material indicator, float turnDegrees = 90f)
        {
            var anchor = RaidAssembly.Child(room, "Quota rotary transfer chamber");
            anchor.SetLocalPositionAndRotation(center, nearRotation);
            var cabin = anchor.gameObject.AddComponent<RaidQuotaRotaryCabin>();
            cabin.turnDegrees = turnDegrees;
            cabin.farField = farField;
            cabin.rotor = RaidAssembly.Child(anchor, "Rotating cabin");
            cabin.farDock = RaidAssembly.Child(anchor, "Far landing frame");
            Pose end = Doorway(Vector3.zero, Quaternion.identity, true, turnDegrees);
            cabin.farDock.SetLocalPositionAndRotation(end.position, end.rotation);

            Transform rotor = cabin.rotor;
            Vector3 deckSize = new Vector3(2 * (HalfWidth + Shell), Shell, 2 * HalfLength);
            Box(rotor, "Cabin deck", Vector3.down * (HalfHeight + Shell / 2), deckSize, layer, frame);
            Box(rotor, "Cabin canopy", Vector3.up * (HalfHeight + Shell / 2), deckSize, layer, frame);
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * (HalfWidth + Shell / 2);
                Box(rotor, "Window guard collision", new Vector3(x, 0, 0),
                    new Vector3(Shell, 2 * HalfHeight, 2 * HalfLength), layer, frame).GetComponent<Renderer>().enabled = false;
                foreach (float y in new[] { -HalfHeight + .37f, -.58f, .60f, HalfHeight - .28f })
                    Box(rotor, "Guard rail", new Vector3(x, y, 0), new Vector3(Shell, .11f, 2 * HalfLength), layer, frame, false);
                foreach (float z in new[] { -HalfLength + .09f, 0f, HalfLength - .09f })
                    Box(rotor, "Cabin post", new Vector3(x, 0, z), new Vector3(Shell, 2 * HalfHeight, .18f), layer, frame, false);
            }
            cabin.Button = Box(rotor, "Transfer switch", new Vector3(HalfWidth - .08f, -.40f, 0),
                new Vector3(.20f, .45f, .52f), layer, indicator, false).transform;
            cabin.nearGate = Gate(anchor, "Near bulkhead", false, layer, gate, turnDegrees);
            cabin.farGate = Gate(anchor, "Far bulkhead", true, layer, gate, turnDegrees);
            cabin.SetGates();
            return cabin;
        }

        private static GameObject Gate(Transform anchor, string name, bool far, int layer, Material material, float degrees)
        {
            Pose pose = Doorway(Vector3.zero, Quaternion.identity, far, degrees);
            var mount = RaidAssembly.Child(anchor, name);
            mount.SetLocalPositionAndRotation(pose.position, pose.rotation);
            // A fixed shutter must cover the full sweep of the rectangular rotor aperture.
            float radius = Mathf.Sqrt(HalfWidth * HalfWidth + HalfHeight * HalfHeight) + .08f;
            return Box(mount, "Interlocked shutter", new Vector3(0, HalfHeight, far ? Shell / 2 : -Shell / 2),
                new Vector3(2 * radius, 2 * radius, Shell), layer, material);
        }

        private static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, int layer,
            Material material, bool solid = true)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.layer = layer;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = center;
            box.transform.localScale = size;
            if (material) box.GetComponent<Renderer>().sharedMaterial = material;
            box.GetComponent<BoxCollider>().enabled = solid;
            return box;
        }

        internal bool CanBoard(Vector3 feet)
        {
            if (turning) return false;
            Vector3 point = rotor.InverseTransformPoint(feet);
            return Mathf.Abs(point.x) <= HalfWidth - .35f && Mathf.Abs(point.z) <= HalfLength - .95f &&
                point.y >= -HalfHeight - .10f && point.y <= .25f;
        }

        internal bool TryBegin(Rigidbody player, Rigidbody held, Func<Rigidbody, bool> eligible)
        {
            if (!player || !CanBoard(player.position)) return false;
            return Depart(player, held, eligible, recall: false);
        }

        internal bool CanCall(bool far, Vector3 feet)
        {
            if (turning || atFar == far) return false;
            Vector3 local = DockFrame(far).InverseTransformPoint(feet);
            return Mathf.Abs(local.x) <= 4.75f && local.y >= -.12f && local.y <= 1.0f &&
                (far ? local.z >= 2.35f && local.z <= 5.5f : local.z >= -4f && local.z <= -.75f);
        }

        internal bool TryCall(Rigidbody player, bool far, Func<Rigidbody, bool> eligible)
        {
            if (!player || !CanCall(far, player.position) || !GateClear(far, player, eligible)) return false;
            return Depart(player, null, eligible, recall: true);
        }

        private bool Depart(Rigidbody player, Rigidbody held, Func<Rigidbody, bool> eligible, bool recall)
        {
            passengers.Clear();
            // The shutters close only on clear thresholds. A hand-held object must be aboard.
            if (!GateClear(atFar, player, eligible)) return false;
            foreach (Collider hit in Physics.OverlapBox(rotor.position, new Vector3(HalfWidth, HalfHeight, HalfLength),
                rotor.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                var body = hit.attachedRigidbody;
                if (!body || body == player || !eligible(body)) continue;
                if (!CanClearGate(hit.bounds)) return false;
                passengers.Add(body);
            }
            if (held && !passengers.Contains(held)) return false;
            rider = recall ? null : player;
            recalling = recall;
            travel = 0f;
            turning = true;
            SetGates();
            return true;
        }

        private bool GateClear(bool far, Rigidbody player, Func<Rigidbody, bool> eligible)
        {
            Transform gate = (far ? farGate : nearGate).transform;
            foreach (Collider hit in Physics.OverlapBox(gate.position, gate.lossyScale * .5f,
                gate.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                var body = hit.attachedRigidbody;
                if (body && (body == player || eligible(body))) return false;
            }
            return true;
        }

        private bool CanClearGate(Bounds worldBounds)
        {
            // The bounding-box projection is conservative for loose objects and loaded carts.
            float z = rotor.InverseTransformPoint(worldBounds.center).z;
            Vector3 axis = rotor.forward;
            float reach = Vector3.Dot(worldBounds.extents, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)));
            return Mathf.Abs(z) + reach <= HalfLength - GateClearance;
        }

        internal bool Step(float deltaTime, Rigidbody player, Action<Vector3, Quaternion, ISet<Rigidbody>> rebase,
            Action<Vector3, Quaternion, ISet<Rigidbody>> moveCartHistory = null)
        {
            if (!turning || deltaTime <= 0f) return false;
            bool observerAboard = !recalling && player && ContainsCabin(player.position);
            if (!recalling && !returning && (!observerAboard || player != rider))
            {
                // Keep the last accessible dock. A rider who lost control inside can be
                // transported with the cargo, while a replacement outside stays put.
                if (rider && rider != player && ContainsCabin(rider.position)) passengers.Add(rider);
                returning = true;
                interruptedTurn = Mathf.SmoothStep(0, 1, travel);
                travel = 0f;
            }
            if (observerAboard && Vector3.Dot(rotor.up, Vector3.up) < .999999f)
            {
                // A controller can return during an unattended roll. Relevel the whole
                // deck and its load once before using the upright rider frame again.
                Quaternion level = Quaternion.FromToRotation(rotor.up, Vector3.up);
                Vector3 velocity = player.linearVelocity;
                Vector3 spin = player.angularVelocity;
                rebase(player.position, level, null);
                if (!player.isKinematic)
                {
                    player.linearVelocity = level * velocity;
                    player.angularVelocity = level * spin;
                }
            }

            float duration = returning ? Duration * Mathf.Max(.15f, interruptedTurn) : Duration;
            travel = Mathf.Min(travel + deltaTime / duration, 1f);
            float smooth = Mathf.SmoothStep(0, 1, travel);
            float turn = returning ? interruptedTurn * (1f - smooth) : smooth;
            Quaternion localTarget = Quaternion.AngleAxis(turnDegrees * (atFar ? 1f - turn : turn), Vector3.forward);
            Quaternion worldTurn = transform.rotation * localTarget * Quaternion.Inverse(rotor.rotation);
            Vector3 pivot = transform.position;

            // With an observer aboard, the inverse world rebase keeps both the stock
            // controller and the load upright. Otherwise only the cabin and load move.
            Quaternion passengerTurn = observerAboard ? Quaternion.identity : worldTurn;
            foreach (var body in passengers)
                if (body && body != player) poses.Add(new PassengerPose(body, pivot, passengerTurn));
            if (observerAboard)
                rebase(pivot + worldTurn * (player.position - pivot), Quaternion.Inverse(worldTurn), passengers);
            else moveCartHistory?.Invoke(pivot, worldTurn, passengers);
            rotor.localRotation = localTarget;
            foreach (var pose in poses) pose.Restore();
            poses.Clear();

            if (travel >= 1f)
            {
                if (!returning) atFar = !atFar;
                turning = false;
                returning = false;
                recalling = false;
                rider = null;
                passengers.Clear();
                SetGates();
            }
            Physics.SyncTransforms();
            return observerAboard;
        }

        private void SetGates()
        {
            nearGate.SetActive(turning || atFar);
            farGate.SetActive(turning || !atFar);
        }

        private bool ContainsCabin(Vector3 point)
        {
            Vector3 local = rotor.InverseTransformPoint(point);
            return Mathf.Abs(local.x) <= HalfWidth && Mathf.Abs(local.z) <= HalfLength &&
                local.y >= -HalfHeight - .10f && local.y <= HalfHeight;
        }

        internal bool TryUp(Vector3 point, out Vector3 up)
        {
            if (ContainsCabin(point))
            {
                up = rotor.up;
                return true;
            }
            if (farField.Contains(farDock.InverseTransformPoint(point)))
            {
                up = farDock.up;
                return true;
            }
            up = default;
            return false;
        }

        private readonly struct PassengerPose
        {
            private readonly Rigidbody body;
            private readonly Vector3 position, velocity, spin;
            private readonly Quaternion rotation;
            private readonly bool asleep;

            internal PassengerPose(Rigidbody body, Vector3 pivot, Quaternion turn)
            {
                this.body = body;
                position = pivot + turn * (body.position - pivot);
                rotation = turn * body.rotation;
                velocity = turn * body.linearVelocity;
                spin = turn * body.angularVelocity;
                asleep = body.IsSleeping();
            }

            internal void Restore()
            {
                if (!body) return;
                body.position = position;
                body.rotation = rotation;
                body.transform.SetPositionAndRotation(position, rotation);
                if (body.isKinematic) return;
                body.linearVelocity = velocity;
                body.angularVelocity = spin;
                if (asleep) body.Sleep();
            }
        }
    }
}

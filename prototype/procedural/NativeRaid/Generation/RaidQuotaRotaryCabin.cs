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
        private const float LossDepth = 30f;
        private const float MaxContinuousStep = 6f;
        private static readonly Vector3 CageHalfSize = new Vector3(HalfWidth, HalfHeight, HalfLength);
        private readonly HashSet<Rigidbody> passengers = new();
        private readonly List<PassengerPose> poses = new();
        private readonly Dictionary<Rigidbody, (Vector3 Point, bool Detached)> traversals = new();
        private readonly List<Rigidbody> inactiveTraversals = new();
        private Collider[] cageContacts = new Collider[32];
        private Transform rotor, farDock;
        private GameObject nearGate, farGate;
        private Material shutterGlass;
        private Bounds farField, roomField;
        private Rigidbody rider;
        private float travel, turnDegrees, interruptedTurn;
        private bool atFar, turning, returning, recalling;

        internal Transform Rotor => rotor;
        internal bool IsTurning => turning;
        internal bool NearOpen => !nearGate.activeSelf;
        internal bool FarOpen => !farGate.activeSelf;
        internal IEnumerable<Rigidbody> Passengers => passengers;
        internal Transform DockFrame(bool far) => far ? farDock : nearGate.transform.parent;

        // Floor centers are coplanar with the cabin deck at the two docking planes.
        // The supplied far gravity bounds are measured from its floor center in the dock frame.
        internal static Pose Doorway(Vector3 center, Quaternion nearRotation, bool far, float turnDegrees)
        {
            Quaternion rotation = nearRotation * Quaternion.AngleAxis(far ? turnDegrees : 0f, Vector3.forward);
            Vector3 position = center + rotation * new Vector3(0, -HalfHeight, far ? HalfLength : -HalfLength);
            return new Pose(position, rotation);
        }

        internal static RaidQuotaRotaryCabin Create(Transform room, Vector3 center, Quaternion nearRotation,
            Bounds farField, int layer, Material frame, Material gate, float turnDegrees, Bounds roomField)
        {
            var anchor = RaidAssembly.Child(room, "Quota rotary transfer chamber");
            anchor.SetLocalPositionAndRotation(center, nearRotation);
            var cabin = anchor.gameObject.AddComponent<RaidQuotaRotaryCabin>();
            cabin.turnDegrees = turnDegrees;
            cabin.farField = farField;
            cabin.roomField = roomField;
            cabin.rotor = RaidAssembly.Child(anchor, "Rotating cabin");
            cabin.farDock = RaidAssembly.Child(anchor, "Far landing frame");
            Pose end = Doorway(Vector3.zero, Quaternion.identity, true, turnDegrees);
            cabin.farDock.SetLocalPositionAndRotation(end.position, end.rotation);

            Transform rotor = cabin.rotor;
            Vector3 deckSize = new Vector3(2 * (HalfWidth + Shell), Shell, 2 * HalfLength);
            Box(rotor, "Cabin deck", Vector3.down * (HalfHeight + Shell / 2), deckSize, layer, frame);
            // Actual open frames: perimeter and one centered cross on each exposed face.
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * (HalfWidth + Shell / 2);
                foreach (float y in new[] { -HalfHeight - Shell / 2, 0f, HalfHeight })
                    Box(rotor, "Side horizontal frame", new Vector3(x, y, 0),
                        new Vector3(Shell, Shell, 2 * HalfLength), layer, frame);
                foreach (float z in new[] { -HalfLength + Shell / 2, 0f, HalfLength - Shell / 2 })
                    Box(rotor, "Side vertical frame", new Vector3(x, 0, z),
                        new Vector3(Shell, 2 * HalfHeight, Shell), layer, frame);
            }
            foreach (float x in new[] { -HalfWidth, 0f, HalfWidth })
                Box(rotor, "Roof longitudinal frame", new Vector3(x, HalfHeight + Shell / 2, 0),
                    new Vector3(Shell, Shell, 2 * HalfLength), layer, frame);
            foreach (float z in new[] { -HalfLength + Shell / 2, 0f, HalfLength - Shell / 2 })
                Box(rotor, "Roof transverse frame", new Vector3(0, HalfHeight + Shell / 2, z),
                    new Vector3(2 * HalfWidth, Shell, Shell), layer, frame);
            Shader standard = Shader.Find("Standard");
            if (standard) cabin.shutterGlass = RaidSightStrips.Glazing(standard, "Contour-4 rotary shutter glazing",
                new Color(.32f, .48f, .53f, .085f));
            cabin.nearGate = cabin.Gate("Near bulkhead", false, layer, gate);
            cabin.farGate = cabin.Gate("Far bulkhead", true, layer, gate);
            cabin.SetGates();
            return cabin;
        }

        private GameObject Gate(string name, bool far, int layer, Material material)
        {
            Pose pose = Doorway(Vector3.zero, Quaternion.identity, far, turnDegrees);
            var mount = RaidAssembly.Child(transform, name);
            mount.SetLocalPositionAndRotation(pose.position, pose.rotation);
            // Keep a solid interlock across the swept aperture and a clear view between docks.
            float radius = Mathf.Sqrt(HalfWidth * HalfWidth + HalfHeight * HalfHeight) + .08f;
            var shutter = Box(mount, "Interlocked shutter", new Vector3(0, HalfHeight, far ? Shell / 2 : -Shell / 2),
                new Vector3(2 * radius, 2 * radius, Shell), layer, material);
            shutter.GetComponent<Renderer>().enabled = false;
            const float view = HalfHeight - .06f;
            foreach (int sign in new[] { -1, 1 })
            {
                Box(mount, "Shutter jamb", new Vector3(sign * (radius + view) / 2, HalfHeight, shutter.transform.localPosition.z),
                    new Vector3(radius - view, 2 * radius, Shell), layer, material, false).transform.SetParent(shutter.transform, true);
                Box(mount, "Shutter crosspiece", new Vector3(0, HalfHeight + sign * (radius + view) / 2, shutter.transform.localPosition.z),
                    new Vector3(2 * view, radius - view, Shell), layer, material, false).transform.SetParent(shutter.transform, true);
            }
            if (shutterGlass)
            {
                var pane = Box(mount, "Shutter glass", shutter.transform.localPosition,
                    new Vector3(2 * view, 2 * view, .018f), layer, shutterGlass, false);
                pane.transform.SetParent(shutter.transform, true);
                pane.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return shutter;
        }

        private void OnDestroy()
        {
            if (!shutterGlass) return;
            if (Application.isPlaying) Destroy(shutterGlass);
            else DestroyImmediate(shutterGlass);
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
            foreach (Collider hit in Physics.OverlapBox(rotor.position, CageHalfSize,
                rotor.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                var body = hit.attachedRigidbody;
                if (!body || body == player || !eligible(body)) continue;
                if (!CanClearGate(hit.bounds)) return false;
                if (ContainsCabin(body.worldCenterOfMass)) passengers.Add(body);
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

        internal void TrackEntrants(List<Rigidbody> actors, Func<Rigidbody, bool> eligible)
        {
            int count;
            while ((count = Physics.OverlapBoxNonAlloc(rotor.position, CageHalfSize, cageContacts, rotor.rotation,
                ~0, QueryTriggerInteraction.Ignore)) == cageContacts.Length)
                Array.Resize(ref cageContacts, cageContacts.Length * 2);
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = cageContacts[i].attachedRigidbody;
                if (body && !actors.Contains(body) && eligible(body)) actors.Add(body);
            }
            Array.Clear(cageContacts, 0, count);
        }

        internal bool Step(float deltaTime, Rigidbody player, Action<Vector3, Quaternion, ISet<Rigidbody>> rebase,
            Action<Vector3, Quaternion, ISet<Rigidbody>> moveCartHistory = null, IEnumerable<Rigidbody> nearbyActors = null)
        {
            if (!turning || deltaTime <= 0f) return false;
            bool observerAboard = player && ContainsCabin(player.position);
            if (!recalling && !returning && (!player || player != rider))
            {
                // Keep the last accessible dock. A rider who lost control inside can be
                // transported with the cargo, while a replacement outside stays put.
                if (rider && rider != player && ContainsCabin(rider.position)) passengers.Add(rider);
                returning = true;
                interruptedTurn = Mathf.SmoothStep(0, 1, travel);
                travel = 0f;
            }
            // Update the load before rotating any body so a crossing takes effect this tick.
            passengers.RemoveWhere(body => !IsPassenger(body, player));
            if (nearbyActors != null)
                foreach (var body in nearbyActors)
                    if (IsPassenger(body, player)) passengers.Add(body);
            if (observerAboard) LevelObserver(player, rotor.up, rebase);

            float duration = returning ? Duration * Mathf.Max(.15f, interruptedTurn) : Duration;
            travel = Mathf.Min(travel + deltaTime / duration, 1f);
            float smooth = Mathf.SmoothStep(0, 1, travel);
            float turn = returning ? interruptedTurn * (1f - smooth) : smooth;
            Quaternion localTarget = Quaternion.AngleAxis(turnDegrees * (atFar ? 1f - turn : turn), Vector3.forward);
            Quaternion worldTurn = transform.rotation * localTarget * Quaternion.Inverse(rotor.rotation);
            Vector3 pivot = transform.position;

            // With an observer aboard, the inverse rebase keeps the controller upright.
            // Detached bodies move only with that change of coordinates.
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

        private bool IsPassenger(Rigidbody body, Rigidbody observer) =>
            body && body != observer && ContainsCabin(body == rider ? body.position : body.worldCenterOfMass);

        internal bool ContainsCabin(Vector3 point)
        {
            Vector3 local = rotor.InverseTransformPoint(point);
            return Mathf.Abs(local.x) <= HalfWidth && Mathf.Abs(local.z) <= HalfLength &&
                local.y >= -HalfHeight - .10f && local.y <= HalfHeight;
        }

        internal bool TryUp(Vector3 point, out Vector3 up) =>
            TryCabinOrDockUp(point, out up) || TryRoomUp(point, out up);

        internal bool TryCabinOrDockUp(Vector3 point, out Vector3 up)
        {
            if (ContainsCabin(point))
            {
                up = rotor.up;
                return true;
            }
            if (InFarField(point))
            {
                up = farDock.up;
                return true;
            }
            up = default;
            return false;
        }

        internal void ObserveBody(Rigidbody body, Vector3 point)
        {
            if (!body) return;
            if (body.isKinematic || !body.useGravity)
            {
                ForgetBody(body);
                return;
            }
            Vector3 local = transform.parent.InverseTransformPoint(point);
            if (ContainsCabin(point)) traversals[body] = (local, false);
            else if (traversals.TryGetValue(body, out var previous))
            {
                if (InFarField(point) || !Continuous(previous.Point, local))
                    ForgetBody(body);
                else traversals[body] = (local, true);
            }
            else if (local.y < -HalfHeight - Shell && roomField.Contains(local) &&
                Mathf.Abs(transform.InverseTransformPoint(point).z) <= HalfLength &&
                Vector3.Dot(body.linearVelocity, transform.up) < 0f)
                traversals[body] = (local, true);
        }

        internal bool TracksBody(Rigidbody body) => body && traversals.ContainsKey(body);

        internal void TransferObserverBody(Rigidbody previous, Rigidbody current)
        {
            if (!previous || !current || !traversals.TryGetValue(previous, out var traversal)) return;
            ForgetBody(previous);
            Vector3 local = transform.parent.InverseTransformPoint(current.position);
            if (!Continuous(traversal.Point, local)) return;
            traversals[current] = (local, traversal.Detached);
            if (rider == previous) rider = current;
        }

        internal void ForgetBody(Rigidbody body)
        {
            if (!ReferenceEquals(body, null)) traversals.Remove(body);
        }

        internal void ClearTraversals() => traversals.Clear();

        internal void PruneTraversals()
        {
            foreach (var body in traversals.Keys)
                if (!body || !body.gameObject.activeInHierarchy) inactiveTraversals.Add(body);
            foreach (var body in inactiveTraversals) ForgetBody(body);
            inactiveTraversals.Clear();
        }

        private static bool Continuous(Vector3 previous, Vector3 current) =>
            (current - previous).sqrMagnitude <= MaxContinuousStep * MaxContinuousStep;

        private bool DetachedInRoom(Rigidbody body, Vector3 local) =>
            body && traversals.TryGetValue(body, out var traversal) && traversal.Detached &&
            Continuous(traversal.Point, local);

        internal bool TryRoomUp(Vector3 point, out Vector3 up, Rigidbody body = null)
        {
            Vector3 local = transform.parent.InverseTransformPoint(point);
            if (roomField.Contains(local) || DetachedInRoom(body, local))
            {
                up = transform.up;
                return true;
            }
            up = default;
            return false;
        }

        private bool InFarField(Vector3 point)
        {
            Vector3 dockPoint = farDock.InverseTransformPoint(point);
            return dockPoint.z >= 0f && farField.Contains(dockPoint);
        }

        internal bool FellInVoid(Vector3 point, Vector3 velocity, Rigidbody body = null)
        {
            Vector3 local = transform.parent.InverseTransformPoint(point);
            if (local.y > -LossDepth || Vector3.Dot(velocity, transform.up) >= 0f || InFarField(point)) return false;
            // A delayed stock Update can miss the band. Only a fall tracked here
            // can still be lost after it has passed below the room.
            return roomField.Contains(local) || DetachedInRoom(body, local);
        }

        internal Quaternion LevelFrom(Vector3 up) => Vector3.Dot(up, Vector3.up) < -.999999f
            ? Quaternion.AngleAxis(180f, transform.forward)
            : Quaternion.FromToRotation(up, Vector3.up);

        internal void LevelObserver(Rigidbody player, Vector3 up, Action<Vector3, Quaternion, ISet<Rigidbody>> rebase)
        {
            if (Vector3.Dot(up, Vector3.up) >= .999999f) return;
            Quaternion level = LevelFrom(up);
            Vector3 velocity = player.linearVelocity;
            Vector3 spin = player.angularVelocity;
            rebase(player.position, level, null);
            if (player.isKinematic) return;
            player.linearVelocity = level * velocity;
            player.angularVelocity = level * spin;
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

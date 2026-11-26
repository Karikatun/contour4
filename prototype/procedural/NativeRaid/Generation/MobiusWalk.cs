using Contour4.Procedural.Catalog;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class MobiusWalk
    {
        internal enum Change { None, Release, Landing }
        private enum Mode { Room, Ribbon, Jumped, JumpReleased, Separated }
        private readonly Transform room;
        private Mode mode;
        private bool observedSupport, newJump;
        internal Collider Ribbon { get; }
        internal bool Attached => mode == Mode.Ribbon;
        internal Vector3 RoomUp(Vector3 point) => AmbientUp(room, point);

        internal static Vector3 AmbientUp(Transform room, Vector3 point)
        {
            Vector3 local = room.InverseTransformPoint(point);
            var up = MobiusHall.RoomUpAt(new(local.x, local.y, local.z));
            return room.TransformDirection(new Vector3(up.X, up.Y, up.Z));
        }

        internal MobiusWalk(Transform room, Collider ribbon)
        {
            this.room = room;
            Ribbon = ribbon;
        }

        internal bool IsWalkSurface(Collider surface)
        {
            return surface == Ribbon;
        }

        internal bool Jump()
        {
            if (!Attached) return false;
            mode = Mode.Jumped;
            newJump = true;
            return true;
        }

        internal static Vector3 TakeoffGravityCorrection(Vector3 roomUp, Vector3 unityGravity) =>
            -roomUp * unityGravity.magnitude - unityGravity;

        internal void Observe(Vector3 feet, Vector3 point, Vector3 normal)
        {
            Vector3 offset = point - feet;
            float height = offset.y;
            offset.y = 0;
            observedSupport |= Mathf.Abs(height) < .39f && offset.sqrMagnitude < .25f && normal.y > .50f;
        }

        internal Vector3 Step(Vector3 feet, Vector3 velocity, Collider groundedSurface, out Change change)
        {
            bool contact = observedSupport;
            observedSupport = false;
            change = Change.None;
            if (mode == Mode.Jumped)
            {
                if (newJump) change = Change.Release;
                newJump = false;
                if (!contact) mode = Mode.JumpReleased;
                return RoomUp(feet);
            }

            bool supported = TrySupport(feet, out RaycastHit hit);
            if (supported && (Attached || contact && velocity.y <= .15f))
            {
                if (!Attached) change = Change.Landing;
                mode = Mode.Ribbon;
                Vector3 point = room.InverseTransformPoint(hit.point);
                Vector3 normal = room.InverseTransformDirection(hit.normal);
                var smooth = MobiusHall.NormalAt(new(point.x, point.y, point.z), new(normal.x, normal.y, normal.z));
                return room.TransformDirection(new Vector3(smooth.X, smooth.Y, smooth.Z));
            }
            if (Attached)
            {
                change = Change.Release;
                mode = Mode.Separated;
            }
            if (groundedSurface && !IsWalkSurface(groundedSurface)) mode = Mode.Room;
            return RoomUp(feet);
        }

        private bool TrySupport(Vector3 feet, out RaycastHit hit)
        {
            var ray = new Ray(feet + Vector3.up * .15f, Vector3.down);
            // The access ramps remain ordinary physical support inside their local field.
            // Only the true ribbon can attach and rotate the observer's world frame.
            return Ribbon.Raycast(ray, out hit, .34f) && hit.normal.y > .50f;
        }

        internal bool TryPassiveGravity(Vector3 feet, out Vector3 up)
        {
            up = default;
            if (Attached || mode is Mode.Jumped or Mode.JumpReleased) return false;
            return TryFieldUp(room, feet, out up);
        }

        internal static bool TryFieldUp(Transform room, Vector3 point, out Vector3 up)
        {
            up = default;
            Vector3 local = room.InverseTransformPoint(point);
            var pointLocal = new System.Numerics.Vector3(local.x, local.y, local.z);
            if (!MobiusHall.TryGravity(pointLocal, out var localUp)) return false;
            up = room.TransformDirection(new Vector3(localUp.X, localUp.Y, localUp.Z));
            return true;
        }

        // On release, turn around the stock capsule's center. In an inverted jump,
        // turning around the feet would put the player's head through their takeoff surface.
        internal static Vector3 ReleasePivot(Vector3 feet, Vector3 center, Quaternion turn) =>
            center - Quaternion.Inverse(turn) * (center - feet);
    }
}

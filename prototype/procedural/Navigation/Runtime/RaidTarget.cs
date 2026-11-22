using System;
using UnityEngine;

namespace Contour4.Procedural.Navigation
{
    [DisallowMultipleComponent]
    public sealed class RaidTarget : MonoBehaviour
    {
        public RaidNavigation Owner { get; private set; }
        public RaidFloor Floor { get; private set; }
        public Vector3 Feet => transform.position;
        public Vector3 SightPoint => eye.position;
        private Transform eye;

        // One component per player for the whole raid. Portal/crossing code changes the floor on this same component.
        public void Bind(RaidNavigation raid, RaidFloor floor, Transform sightPoint)
        {
            if (Owner || !isActiveAndEnabled || !raid || !raid.Includes(floor) ||
                !sightPoint || (sightPoint != transform && !sightPoint.IsChildOf(transform)))
                throw new InvalidOperationException("Bind an active player with its own sight point to a placed floor.");
            Owner = raid;
            Floor = floor;
            eye = sightPoint;
            raid.Register(this);
        }

        public void SetFloor(RaidFloor floor)
        {
            if (!Owner || !Owner.Includes(floor))
                throw new InvalidOperationException("The destination floor must belong to the current raid.");
            Floor = floor;
        }

        public void Unbind()
        {
            var raid = Owner;
            Owner = null;
            Floor = null;
            eye = null;
            if (raid)
                raid.Unregister(this);
        }

        internal bool HasSightPoint => eye;
        private void OnDisable() => Unbind();
        private void OnDestroy() => Unbind();
    }
}

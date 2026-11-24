using System.Collections.Generic;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class CabinetFront
    {
        internal Rigidbody Body { get; }
        internal IReadOnlyList<Collider> Parts { get; }

        internal static Rigidbody MountAnchor(Transform room)
        {
            var anchor = RaidAssembly.Child(room, "Cabinet joint anchors").gameObject.AddComponent<Rigidbody>();
            anchor.useGravity = false;
            anchor.isKinematic = true;
            return anchor;
        }

        internal CabinetFront(Transform pivot, Collider[] parts, Rigidbody mount, float angle, float travel)
        {
            Parts = parts;
            Body = pivot.gameObject.AddComponent<Rigidbody>();
            Body.mass = 5f;
            Body.useGravity = false;
            Body.linearDamping = 8f;
            Body.angularDamping = 6f;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Body.solverIterations = 12;
            Body.solverVelocityIterations = 4;

            if (travel == 0)
            {
                var hinge = pivot.gameObject.AddComponent<HingeJoint>();
                hinge.axis = Vector3.up;
                SetAnchors(hinge, mount, pivot.position);
                hinge.limits = new JointLimits { min = Mathf.Min(0, angle), max = Mathf.Max(0, angle) };
                hinge.useLimits = true;
                return;
            }

            // Unity's linear limit is symmetric around the connected anchor. The midpoint
            // gives the drawer its authored closed and fully extended stops.
            var rail = pivot.gameObject.AddComponent<ConfigurableJoint>();
            rail.axis = Vector3.forward;
            rail.secondaryAxis = Vector3.up;
            SetAnchors(rail, mount, pivot.TransformPoint(new Vector3(0, 0, travel * .5f)));
            rail.xMotion = ConfigurableJointMotion.Limited;
            rail.yMotion = rail.zMotion = rail.angularXMotion = rail.angularYMotion = rail.angularZMotion = ConfigurableJointMotion.Locked;
            rail.linearLimit = new SoftJointLimit { limit = Mathf.Abs(travel) * .5f };
        }

        private static void SetAnchors(Joint joint, Rigidbody mount, Vector3 connectedPoint)
        {
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedBody = mount;
            joint.anchor = Vector3.zero;
            joint.connectedAnchor = mount.transform.InverseTransformPoint(connectedPoint);
        }
    }
}

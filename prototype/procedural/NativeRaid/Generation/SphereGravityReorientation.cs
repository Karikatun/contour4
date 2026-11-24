using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class SphereGravityReorientation
    {
        internal const float DegreesPerSecond = 240f;
        private Vector3 lastAxis;

        internal void Reset() => lastAxis = Vector3.zero;

        internal Quaternion Step(Vector3 fieldUp, Vector3 roomForward, float seconds)
        {
            Vector3 cross = Vector3.Cross(fieldUp, Vector3.up);
            float sine = cross.magnitude;
            float cosine = Vector3.Dot(fieldUp, Vector3.up);
            if (sine < .00001f && cosine >= 0) return Quaternion.identity;

            Vector3 axis = lastAxis;
            if (sine >= .00001f)
                axis = cross / sine;
            else if (axis == Vector3.zero)
            {
                axis = Vector3.ProjectOnPlane(roomForward, Vector3.up).normalized;
                if (axis == Vector3.zero) axis = Vector3.right;
            }

            lastAxis = axis;
            float degrees = Mathf.Atan2(sine, cosine) * Mathf.Rad2Deg;
            return Quaternion.AngleAxis(Mathf.Min(degrees, seconds * DegreesPerSecond), axis);
        }

        internal static Vector3 ForceCorrection(Vector3 fieldUp, Vector3 unityGravity) =>
            -fieldUp * unityGravity.magnitude - unityGravity;

        internal static Vector3 FallImpulseCorrection(Vector3 fieldUp, float stockVerticalImpulse) =>
            (fieldUp - Vector3.up) * stockVerticalImpulse;
    }
}

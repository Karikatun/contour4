using System.Collections.Generic;
using UnityEngine;

namespace Contour4.NativeRaid.Runtime
{
    internal sealed class RaidWorldRebase
    {
        private readonly List<BodyPose> moves = new();

        // Move scene bodies with the stage. The observer stays in the stock controller frame.
        internal void Apply(Transform stage, Rigidbody observer, IEnumerable<Rigidbody> actors,
            Vector3 sourcePivot, Quaternion turn, ISet<Rigidbody> portalLoad = null)
        {
            Vector3 pivot = observer.position;
            var observerPose = new BodyPose(observer, pivot, pivot, Quaternion.identity);
            foreach (var body in actors)
            {
                if (body && body != observer && (portalLoad == null || !portalLoad.Contains(body)))
                    moves.Add(new BodyPose(body, sourcePivot, pivot, turn));
            }
            stage.SetPositionAndRotation(pivot + turn * (stage.position - sourcePivot), turn * stage.rotation);
            foreach (BodyPose move in moves) move.Apply();
            moves.Clear();
            if (observer.transform.IsChildOf(stage)) observerPose.Apply();
            Physics.SyncTransforms();
        }

        private readonly struct BodyPose
        {
            private readonly Rigidbody body;
            private readonly Vector3 position, velocity, angularVelocity;
            private readonly Quaternion rotation;
            private readonly bool asleep;

            internal BodyPose(Rigidbody body, Vector3 pivot, Vector3 destination, Quaternion turn)
            {
                this.body = body;
                position = destination + turn * (body.position - pivot);
                rotation = turn * body.rotation;
                velocity = turn * body.linearVelocity;
                angularVelocity = turn * body.angularVelocity;
                asleep = body.IsSleeping();
            }

            internal void Apply()
            {
                if (!body) return;
                body.position = position;
                body.rotation = rotation;
                body.transform.SetPositionAndRotation(position, rotation);
                if (body.isKinematic) return;
                body.linearVelocity = velocity;
                body.angularVelocity = angularVelocity;
                if (asleep) body.Sleep();
            }
        }
    }
}

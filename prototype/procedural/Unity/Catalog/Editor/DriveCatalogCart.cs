using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Contour4.Procedural.Catalog
{
    internal static class DriveCatalogCart
    {
        public static void Verify(GrayModule module, IReadOnlyDictionary<Collider, BlockRole> geometry)
        {
            var cart = new GameObject("Dynamic gray cart with secured cargo");
            var material = new PhysicsMaterial("Fixture wheels")
            {
                staticFriction = .08f,
                dynamicFriction = .08f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
            };
            var cargo = cart.AddComponent<BoxCollider>();
            cargo.center = new Vector3(0, 1.23f, .02f);
            cargo.size = new Vector3(1.5f, 1.5f, 1.9f);
            var chassis = cart.AddComponent<BoxCollider>();
            chassis.center = new Vector3(0, .48f, 0);
            chassis.size = new Vector3(1.72f, .43f, 2.52f);
            var wheels = new List<SphereCollider>();
            foreach (float x in new[] { -.65f, .65f })
            foreach (float z in new[] { -.85f, .85f })
            {
                var wheel = cart.AddComponent<SphereCollider>();
                wheel.center = new Vector3(x, .2f, z);
                wheel.radius = .20f;
                wheel.sharedMaterial = material;
                wheels.Add(wheel);
            }
            var body = cart.AddComponent<Rigidbody>();
            body.mass = 80;
            body.useGravity = false;
            body.angularDamping = 7;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            SimulationMode old = Physics.simulationMode;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                foreach (HaulRoute route in module.Routes)
                    Drive(route, module.Docks.Single(d => d.Id == route.To), body, cargo, wheels, geometry);
            }
            finally
            {
                Physics.simulationMode = old;
                UnityEngine.Object.DestroyImmediate(cart);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static void Drive(HaulRoute route, Dock outlet, Rigidbody body, BoxCollider cargo, IReadOnlyList<SphereCollider> wheels,
            IReadOnlyDictionary<Collider, BlockRole> geometry)
        {
            ModulePose[] frames = route.Knots.Distinct().ToArray();
            Vector3[] points = frames.Select(f => ToUnity(f.Position)).ToArray();
            float[] distance = new float[points.Length];
            for (int i = 1; i < points.Length; i++)
                distance[i] = distance[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            body.position = points[0] + ToUnity(frames[0].Up) * .04f;
            body.rotation = Quaternion.LookRotation(ToUnity(frames[0].Forward), ToUnity(frames[0].Up));
            body.transform.SetPositionAndRotation(body.position, body.rotation);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            int minimumWheels = wheels.Count;
            float worstLateral = 0;
            for (int step = 0; step < 1400; step++)
            {
                Vector3 position = body.position;
                var (index, t) = Closest(position, points);
                Vector3 track = Vector3.Lerp(points[index], points[index + 1], t);
                Vector3 forward = Vector3.Lerp(ToUnity(frames[index].Forward), ToUnity(frames[index + 1].Forward), t).normalized;
                Vector3 up = Vector3.Lerp(ToUnity(frames[index].Up), ToUnity(frames[index + 1].Up), t).normalized;
                Vector3 lateral = Vector3.Cross(forward, up).normalized;
                float remaining = distance[distance.Length - 1] - Mathf.Lerp(distance[index], distance[index + 1], t);
                float forwardSpeed = Mathf.Min(2.65f, remaining * 5);
                float lateralOffset = Vector3.Dot(track - position, lateral);
                float elevation = Vector3.Dot(position - track, up);
                worstLateral = Mathf.Max(worstLateral, Mathf.Abs(lateralOffset));
                if (Mathf.Abs(lateralOffset) > .30f || elevation < -.12f || elevation > .34f)
                    throw new InvalidOperationException($"{route.Id}: dynamic cart left deck near {position}, " +
                        $"lateral {lateralOffset:F3}m, height {elevation:F3}m");
                if (Physics.OverlapBox(body.position + body.rotation * cargo.center, cargo.size / 2, body.rotation,
                    ~0, QueryTriggerInteraction.Ignore).Any(geometry.ContainsKey))
                    throw new InvalidOperationException($"{route.Id}: secured cargo hit module geometry at {position}");
                if (step > 3)
                {
                    int supportedWheels = wheels.Count(wheel => Physics.RaycastAll(
                        body.position + body.rotation * wheel.center, -up, wheel.radius + .09f,
                        ~0, QueryTriggerInteraction.Ignore).Any(hit => geometry.TryGetValue(hit.collider, out BlockRole role)
                        && role == BlockRole.Floor && Vector3.Dot(up, hit.normal) > .65f));
                    minimumWheels = Mathf.Min(minimumWheels, supportedWheels);
                    if (supportedWheels != wheels.Count)
                        throw new InvalidOperationException($"{route.Id}: dynamic cart lost deck at {position}, wheels {supportedWheels}");
                }
                if (remaining <= .12f)
                {
                    Vector3 thresholdForward = ToUnity(outlet.Threshold.Forward);
                    float centerBeyondOutlet = Vector3.Dot(position - ToUnity(outlet.Threshold.Position), thresholdForward);
                    if (centerBeyondOutlet < route.Envelope.X / 2)
                        throw new InvalidOperationException($"{route.Id}: loaded cart has not cleared the outlet.");
                    Debug.Log($"CONTOUR4 CATALOG {route.Id}: same secured 80 kg cart reached mated outlet " +
                        $"after {distance[distance.Length - 1] - remaining:F2} m / {step * .02f:F2} s; " +
                        $"{minimumWheels} minimum wheel rays, {worstLateral:F3} m worst lateral offset");
                    return;
                }
                Vector3 velocity = body.linearVelocity;
                float drive = Mathf.Clamp((forwardSpeed - Vector3.Dot(velocity, forward)) * 8, -15, 15);
                float correction = Mathf.Clamp(lateralOffset * 14 - Vector3.Dot(velocity, lateral) * 9, -12, 12);
                body.MoveRotation(Quaternion.LookRotation(forward, up));
                body.AddForce(drive * forward + correction * lateral - 9.81f * up, ForceMode.Acceleration);
                Physics.Simulate(.02f);
            }
            throw new InvalidOperationException($"{route.Id}: dynamic cart did not reach the outlet in 28 s; last at {body.position}");
        }

        private static (int Index, float T) Closest(Vector3 position, IReadOnlyList<Vector3> points)
        {
            int best = 0;
            float bestT = 0;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 offset = points[i + 1] - points[i];
                float t = Mathf.Clamp01(Vector3.Dot(position - points[i], offset) / offset.sqrMagnitude);
                float distance = (points[i] + t * offset - position).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = i;
                bestT = t;
            }
            return (best, bestT);
        }

        private static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}

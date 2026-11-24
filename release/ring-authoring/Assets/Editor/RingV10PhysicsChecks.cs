using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

internal static class RingV10PhysicsChecks
{
    internal static void Verify(Transform root, TextWriter report)
    {
        VerifyBridgeSides(root, report);
        VerifyFurnitureMotion(root, report);
    }

    private static void VerifyBridgeSides(Transform root, TextWriter report)
    {
        var group = root.Find("V10 shaft side closures");
        int swept = 0, clear = 0;
        foreach (string name in new[] { "BridgeQuotaLoot1", "BridgeLoot1Loot2" })
        {
            var bridge = root.Find(name);
            float cy = name == "BridgeQuotaLoot1" ? 4.7875f : 19.7875f;
            var center = root.TransformPoint(new Vector3(7.5f, cy, 0));
            foreach (int index in new[] { 4, 8, 12 })
            {
                string sampleName = name + "__Sample_" + index.ToString("00", CultureInfo.InvariantCulture);
                var sample = bridge.Find("Markers/" + sampleName);
                Require(sample && Vector3.Dot((center - sample.position).normalized, sample.up) > .9f,
                    "Missing valid inward bridge frame: " + sampleName);
                var side = root.forward;
                foreach (float height in new[] { 1f, 1.4f, 1.46f })
                foreach (float sign in new[] { -1f, 1f })
                {
                    var foot = sample.position + sample.up * height;
                    var hits = Physics.CapsuleCastAll(foot + sample.up * .20f,
                        foot + sample.up * 1.50f, .20f, side * sign, 5f, ~0, QueryTriggerInteraction.Ignore);
                    Require(hits.Any(h => h.collider.transform.IsChildOf(group)), "Open bridge side at " + sampleName);
                    swept++;
                }
                var cart = Physics.OverlapBox(sample.position + sample.up * 1.105f, new Vector3(.90f, 1.025f, 1.30f),
                    Quaternion.LookRotation(Vector3.Cross(side, sample.up), sample.up), ~0, QueryTriggerInteraction.Ignore);
                Require(!cart.Any(c => c.transform.IsChildOf(group)), "Bridge wall obstructs cart at " + sampleName);
                clear++;
            }
        }
        report.WriteLine($"BRIDGE cavities {swept}/36 raised side casts hit V10 walls, {clear}/6 rigid cart samples clear of new side closures.");
    }

    private static void VerifyFurnitureMotion(Transform root, TextWriter report)
    {
        var old = Physics.simulationMode;
        var joints = root.GetComponentsInChildren<Joint>();
        var bodies = joints.Select(joint => joint.GetComponent<Rigidbody>()).ToArray();
        Physics.simulationMode = SimulationMode.Script;
        try
        {
            foreach (var body in bodies) body.isKinematic = true;
            for (int index = 0; index < joints.Length; index++)
            {
                var joint = joints[index];
                var body = bodies[index];
                var grip = body.GetComponentsInChildren<BoxCollider>()
                    .Single(c => c.name.Contains("grip", StringComparison.OrdinalIgnoreCase) ||
                        c.name.Contains("handle", StringComparison.OrdinalIgnoreCase));
                var home = new Pose(body.position, body.rotation);
                Vector3 forward = home.rotation * Vector3.forward;
                Vector3 gripOffset = Quaternion.Inverse(home.rotation) * (grip.bounds.center - home.position);
                Vector3 closed = home.position + home.rotation * gripOffset;
                body.isKinematic = false;
                Require(GripRay(body, grip), "Closed grip ray missed " + joint.name);
                float expected;
                Vector3 target;
                if (joint is HingeJoint hinge)
                {
                    expected = Mathf.Abs(hinge.limits.min);
                    target = home.position + home.rotation * Quaternion.AngleAxis(hinge.limits.min, Vector3.up) * gripOffset;
                }
                else
                {
                    expected = ((ConfigurableJoint)joint).linearLimit.limit * 2;
                    target = closed + forward * expected;
                }

                Pull(body, gripOffset, target);
                Require(GripRay(body, grip), "Open grip ray missed " + joint.name);
                float opened = joint is HingeJoint
                    ? Quaternion.Angle(home.rotation, body.rotation)
                    : Vector3.Dot(body.position - home.position, forward);
                Pull(body, gripOffset, closed - (joint is HingeJoint ? Vector3.zero : forward * .08f));
                float reclose = joint is HingeJoint
                    ? Quaternion.Angle(home.rotation, body.rotation)
                    : Vector3.Distance(home.position, body.position);
                Require(Mathf.Abs(opened - expected) <= (joint is HingeJoint ? 1f : .015f) &&
                    reclose <= (joint is HingeJoint ? 1.5f : .015f), "Opening/closing failed: " + joint.name);
                report.WriteLine($"FRONT {joint.name}: {opened:F3}/{expected:F3}, reclose {reclose:F3}, grip rays passed.");
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
                body.position = home.position;
                body.rotation = home.rotation;
                Physics.SyncTransforms();
            }
        }
        finally
        {
            foreach (var body in bodies) body.isKinematic = false;
            Physics.simulationMode = old;
        }
    }

    private static bool GripRay(Rigidbody body, Collider grip) =>
        Physics.Raycast(grip.bounds.center + body.transform.forward * .4f, -body.transform.forward,
            out var hit, 1f, ~0, QueryTriggerInteraction.Ignore) && hit.rigidbody == body;

    private static void Pull(Rigidbody body, Vector3 grip, Vector3 target)
    {
        for (int step = 0; step < 120; step++)
        {
            Vector3 point = body.position + body.rotation * grip;
            body.AddForceAtPosition(Vector3.ClampMagnitude((target - point) * 350f, 90f), point);
            Physics.Simulate(.02f);
        }
        Physics.SyncTransforms();
    }

    private static void Require(bool result, string detail)
    {
        if (!result) throw new InvalidOperationException(detail);
    }
}

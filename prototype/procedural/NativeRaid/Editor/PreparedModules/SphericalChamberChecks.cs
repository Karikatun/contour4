using System;
using UnityEngine;
using N = System.Numerics;
using Object = UnityEngine.Object;

namespace Contour4.Procedural.Catalog
{
    public static class SphericalChamberChecks
    {
        public static void Verify(GameObject instance)
        {
            const string clone = "(Clone)";
            string id = instance.name;
            if (id.EndsWith(clone, StringComparison.Ordinal)) id = id.Substring(0, id.Length - clone.Length);
            Verify(instance, SphericalChamber.GetMask(id));
        }

        public static void Verify(GameObject instance, int mask)
        {
            string id = SphericalChamber.VariantId(mask);
            Transform root = instance.transform;
            var shell = root.Find("Geometry/Shell/" + SphericalChamber.Shell).GetComponent<MeshCollider>();
            Check(Mathf.Abs(shell.bounds.size.z - SphericalChamber.Diameter) < .002f && shell.sharedMesh.vertices.Length > 0,
                $"The reloaded shell no longer measures {SphericalChamber.Diameter} m across the central section.");
            foreach (Vector3 vertex in shell.sharedMesh.vertices)
                Check(Mathf.Abs((vertex + shell.transform.localPosition).magnitude - SphericalChamber.Radius) < .002f,
                    $"A reloaded shell vertex left the expected {SphericalChamber.Radius} m radius.");

            VerifyThreshold(root, shell, "west", Vector3.left, Vector3.up, true);
            bool east = (mask & SphericalChamber.EastMask) != 0;
            VerifyThreshold(root, shell, "east", Vector3.right, Vector3.up, east);
            Rail(root, SphericalChamber.DiameterRailings, new N.Vector3(-3.6f, 0, 0), N.Vector3.UnitY, N.Vector3.UnitZ);
            VerifyDiameter(root, east, mask);
            int contacts = 0;
            int landingPhase = SphericalChamber.BranchPhaseCount - 1;
            foreach (int side in new[] { 1, -1 })
            {
                string branch = side > 0 ? SphericalChamber.Left : SphericalChamber.Right;
                string exit = side > 0 ? SphericalChamber.UpperExit : SphericalChamber.LowerExit;
                ModulePose start = SphericalChamber.BranchFrame(side, 0, 0, 0);
                ModulePose vertical = SphericalChamber.BranchFrame(side, 0, 1, 0);
                ModulePose arrival = SphericalChamber.BranchFrame(side, landingPhase, 1, 0);
                int bit = side > 0 ? SphericalChamber.UpperMask : SphericalChamber.LowerMask;
                bool open = (mask & bit) != 0;
                VerifyThreshold(root, shell, exit, Vector(arrival.Forward), Vector(arrival.Up), open);
                if (!open)
                {
                    VerifyTrimmedBranch(root, side, branch, mask);
                    continue;
                }
                Check(start.Up == N.Vector3.UnitY && start.Forward.Z == side && Math.Abs(start.Position.Z) == SphericalChamber.HalfWidth &&
                    N.Vector3.Dot(vertical.Forward, side * N.Vector3.UnitY) > .99999f &&
                    Math.Abs(vertical.Up.Y) < .0001f, $"Branch {side}: 90° direction or bridge joint changed.");
                var sill = root.Find("Docks/" + exit);
                Check(arrival.Position == side * SphericalChamber.HalfReservation * N.Vector3.UnitY &&
                    arrival.Forward == side * N.Vector3.UnitY && arrival.Up == -N.Vector3.UnitZ &&
                    Vector3.Distance(sill.position, root.TransformPoint(Vector(arrival.Position))) < .0001f &&
                    Vector3.Dot(sill.forward, root.TransformDirection(Vector(arrival.Forward))) > .9999f &&
                    Vector3.Dot(sill.up, root.TransformDirection(Vector(arrival.Up))) > .9999f,
                    $"Branch {side}: exit is not vertically aligned over the main bridge center.");
                for (int phase = 1; phase <= landingPhase; phase++)
                {
                    var a = SphericalChamber.BranchFrame(side, phase - 1, 1, 0);
                    var b = SphericalChamber.BranchFrame(side, phase, 0, 0);
                    Check(N.Vector3.Distance(a.Position, b.Position) < .0001f &&
                        N.Vector3.Dot(a.Forward, b.Forward) > .9999f && N.Vector3.Dot(a.Up, b.Up) > .9999f,
                        $"Branch {side}: discontinuous deck seam at phase {phase}.");
                }
                foreach (int phase in new[] { 0, 1, 2, landingPhase })
                {
                    var frame = SphericalChamber.BranchFrame(side, phase, .5f, 0);
                    Rail(root, branch + "-railings", frame.Position, frame.Up, N.Vector3.UnitX);
                }
                foreach (float x in new[] { -1.65f, 0, 1.65f })
                foreach (bool reverse in new[] { false, true })
                {
                    N.Vector3 oldUp = reverse ? arrival.Up : N.Vector3.UnitY;
                    const int subdivisions = 70;
                    int samples = subdivisions * SphericalChamber.BranchPhaseCount;
                    for (int index = 0; index <= samples; index++)
                    {
                        int sample = reverse ? samples - index : index;
                        int phase = Mathf.Min(landingPhase, sample / subdivisions);
                        float t = (sample - subdivisions * phase) / (float)subdivisions;
                        if (phase == landingPhase && t > .88f) continue; // Loaded fixture crosses the final sill separately.
                        ModulePose frame = SphericalChamber.BranchFrame(side, phase, t, x);
                        Vector3 world = root.TransformPoint(Vector(frame.Position));
                        Vector3 up = root.TransformDirection(Vector(frame.Up));
                        Check(Physics.Raycast(world + up * .18f, -up, out var hit, .29f) &&
                            Vector3.Dot(hit.normal, up) > .91f && (hit.collider.name == "diameter-deck" ||
                            hit.collider.name == branch || hit.collider.name == branch + "-sill-deck"),
                            $"Branch {side}: PhysX has no aligned support at lateral {x:F2}, stage {phase}/{t:F3}.");
                        Check(SphericalGravityField.TryUp(frame.Position + frame.Up * .12f, oldUp, out var actual, out bool onDeck, mask) && onDeck &&
                            N.Vector3.Dot(actual, frame.Up) > .991f && N.Vector3.Dot(actual, oldUp) > .98f,
                            $"Branch {side}: gravity changed abruptly at lateral {x:F2}, stage {phase}/{t:F3}.");
                        oldUp = actual;
                        const float radius = .30f, clearance = .055f, height = 1.72f;
                        Check(!Physics.CheckCapsule(world + up * (radius + clearance), world + up * (height - radius),
                            radius, ~0, QueryTriggerInteraction.Ignore),
                            $"Branch {side}: player envelope clips at lateral {x:F2}, stage {phase}/{t:F3}.");
                        contacts++;
                    }
                }
            }

            WalkShell(root, shell, mask);
            Debug.Log($"CONTOUR4 SPHERE PHYSICS PASSED {id}: {contacts} branch contacts and body envelopes, both directions; open and sealed thresholds; guarded deck edges; {SphericalChamber.Diameter} m shell; full grippy orbit under radial acceleration.");
        }

        private static void VerifyThreshold(Transform root, MeshCollider shell, string id, Vector3 direction,
            Vector3 up, bool open)
        {
            Vector3 origin = root.TransformPoint((SphericalChamber.Radius - .65f) * direction + 1.4f * up);
            Vector3 forward = root.TransformDirection(direction);
            Check(shell.Raycast(new Ray(origin, forward), out var hit, 1.3f) == !open &&
                (open || Vector3.Dot(hit.normal, forward) < -.96f), $"{id}: incorrect shell opening or closure.");
            var dock = root.Find("Docks/" + id);
            Check((bool)dock == open &&
                (bool)root.Find($"Geometry/Shell/{id}-collar") == open &&
                (bool)root.Find($"Geometry/Conditional plugs/{id}-plug") == open,
                $"{id}: dock markers or collar/plug state disagrees with its aperture.");
            if (open)
            {
                float width = Vector3.Distance(dock.Find("Contact start").position, dock.Find("Contact end").position);
                float height = Vector3.Distance(dock.position, dock.Find("Opening top").position);
                Check(Mathf.Abs(width - 4.5f) < .002f && Mathf.Abs(height - 4f) < .002f,
                    $"{id}: opening is not 4.5 m × 4 m.");
            }
        }

        private static void VerifyDiameter(Transform root, bool east, int mask)
        {
            const float cut = SphericalChamber.EastCut;
            var deck = root.Find("Geometry/Floors/diameter-deck").GetComponent<MeshCollider>();
            float end = east ? SphericalChamber.HalfReservation : cut;
            Check(Mathf.Abs(deck.bounds.max.x - root.position.x - end) < .002f, "East bridge ends at the wrong point.");
            Check((bool)root.Find("Geometry/Floors/right-service-bay") == east &&
                (bool)root.Find("Haul routes/diameter-through") == east, "East deck references disagree with the installed exit.");
            if (east) return;
            foreach (float z in new[] { -1.9f, 0, 1.9f })
                Guard(root, new Vector3(cut - .35f, 1.02f, z), Vector3.right);
            Check(SphericalGravityField.TryUp(new N.Vector3(cut + .1f, .9f, 0), N.Vector3.UnitY,
                out _, out bool onDeck, mask) && !onDeck, "The trimmed east bridge retained a deck field.");
        }

        private static void VerifyTrimmedBranch(Transform root, int side, string branch, int mask)
        {
            Check(!root.Find("Geometry/Floors/" + branch) && !root.Find($"Geometry/Floors/{branch}-sill-deck") &&
                !root.Find($"Geometry/Obstacles/{branch}-railings") && !root.Find($"Floor frames/{branch}-landing") &&
                !root.Find($"Landmarks/{branch}-entry") && !root.Find($"Haul routes/{branch}-through"),
                $"{branch}: trimmed branch has residual geometry or markers.");
            foreach (float x in new[] { -1.9f, 0, 1.9f })
                Guard(root, new Vector3(x, 1.02f, side * (SphericalChamber.HalfWidth - .35f)), side * Vector3.forward);
            var probe = SphericalChamber.BranchFrame(side, SphericalChamber.BranchPhaseCount - 1, .12f, 0);
            Check(SphericalGravityField.TryUp(probe.Position + probe.Up * .8f, N.Vector3.UnitY,
                out _, out bool onDeck, mask) && !onDeck, $"{branch}: trimmed branch retained a deck field.");
        }

        private static void Guard(Transform root, Vector3 position, Vector3 direction)
        {
            Check(Physics.Raycast(root.TransformPoint(position), root.TransformDirection(direction), out var hit, .35f) &&
                hit.collider.name == SphericalChamber.DiameterRailings, $"The bridge cut at {position} has no edge guard.");
        }

        private static void Rail(Transform root, string id, N.Vector3 floor, N.Vector3 up, N.Vector3 across)
        {
            foreach (int edge in new[] { -1, 1 })
            {
                Vector3 origin = root.TransformPoint(Vector(floor + up * 1.02f + across * (edge * 1.92f)));
                Vector3 direction = root.TransformDirection(Vector(across * edge));
                Check(Physics.Raycast(origin, direction, out var hit, .33f) && hit.collider.name == id,
                    $"Missing continuous rail {id} on edge {edge} at {floor}.");
            }
        }

        private static void WalkShell(Transform root, MeshCollider shell, int mask)
        {
            const float x = 4, sphereRadius = .21f, speed = 3.15f, step = .02f;
            float ring = Mathf.Sqrt(SphericalChamber.Radius * SphericalChamber.Radius - x * x);
            var probe = new GameObject("Orbital contact test");
            var sphere = probe.AddComponent<SphereCollider>();
            sphere.radius = sphereRadius;
            var body = probe.AddComponent<Rigidbody>();
            body.useGravity = false;
            Vector3 start = new Vector3(x, 0, ring - sphereRadius - .03f);
            body.position = root.TransformPoint(start);
            var previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                Physics.SyncTransforms();
                var scene = root.gameObject.scene.GetPhysicsScene();
                float orbit = 0, priorAngle = 0;
                int steps = 0;
                while (orbit < 2 * Mathf.PI && steps < Mathf.CeilToInt(2.4f * Mathf.PI * ring / (speed * step)))
                {
                    Vector3 local = root.InverseTransformPoint(body.position);
                    var inward = -local.normalized;
                    Check(SphericalGravityField.TryUp(Numeric(local), Numeric(inward), out var direction, out bool onDeck, mask) && !onDeck &&
                        Vector3.Dot(Vector(direction), inward) > .9999f, "Orbital test lost radial gravity.");
                    Vector3 worldUp = root.TransformDirection(Vector(direction));
                    Vector3 tangent = new Vector3(0, local.z, -local.y).normalized;
                    Vector3 velocity = root.InverseTransformDirection(body.linearVelocity);
                    Vector3 radial = Vector3.Project(velocity, inward);
                    body.linearVelocity = root.TransformDirection(speed * tangent + radial + (x - local.x) * Vector3.right * 6f);
                    body.AddForce(-worldUp * 13f, ForceMode.Acceleration);
                    scene.Simulate(step);
                    local = root.InverseTransformPoint(body.position);
                    float angle = Mathf.Atan2(local.y, local.z);
                    orbit += Mathf.DeltaAngle(priorAngle * Mathf.Rad2Deg, angle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    priorAngle = angle;
                    Check(Physics.Raycast(body.position, -worldUp, out var hit, .35f) && hit.collider == shell &&
                        hit.distance > sphereRadius - .055f && Mathf.Abs(local.x - x) < .65f,
                        $"Orbital test lost its real shell contact at {orbit * Mathf.Rad2Deg:F1}°.");
                    steps++;
                }
                Check(orbit >= 2 * Mathf.PI, "The body did not complete its orbit on the spherical collision mesh.");
            }
            finally
            {
                Physics.simulationMode = previousMode;
                Object.DestroyImmediate(probe);
            }
        }

        private static void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException("Spherical chamber: " + message);
        }

        private static Vector3 Vector(N.Vector3 value) => new(value.X, value.Y, value.Z);
        private static N.Vector3 Numeric(Vector3 value) => new(value.x, value.y, value.z);
    }
}

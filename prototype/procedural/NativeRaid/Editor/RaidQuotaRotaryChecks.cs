using System;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Contour4.NativeRaid.Editor
{
    public static class RaidQuotaRotaryChecks
    {
        private const string Label = "CONTOUR4 ROTARY QUOTA";
        private const float Tick = .02f;

        public static void Run()
        {
            var previousSimulation = Physics.simulationMode;
            var previousGravity = Physics.gravity;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                Physics.gravity = new Vector3(0, -9.81f, 0);
                foreach (float degrees in new[] { -90f, 90f, 180f }) CheckRoomAndTransfer(degrees);
                CheckSeed();
                Debug.Log($"{Label} PASSED");
            }
            finally
            {
                Physics.simulationMode = previousSimulation;
                Physics.gravity = previousGravity;
            }
        }

        private static void CheckRoomAndTransfer(float degrees)
        {
            Transform stage = new GameObject("Quota fixture").transform;
            var module = RaidQuotaRotaryRoom.Create(degrees);
            foreach (var block in module.Geometry.Where(block => block.Role is not BlockRole.Free and not BlockRole.Plug))
            {
                var (low, high) = block.Bounds;
                var part = RaidAssembly.Child(stage, block.Id);
                part.localPosition = RaidPlan.Vector((high + low) / 2);
                part.gameObject.AddComponent<BoxCollider>().size = RaidPlan.Vector(high - low);
            }
            var cabin = RaidQuotaRotaryCabin.Create(stage, RaidQuotaRotaryRoom.Center, RaidQuotaRotaryRoom.NearRotation,
                RaidQuotaRotaryRoom.FarField, 0, null, null, degrees);
            var farApproach = RaidExtraction.LoadingRoute(module).Knots.Last();
            Transform farDock = cabin.DockFrame(true);
            Require(Vector3.Angle(RaidPlan.Vector(farApproach.Up), farDock.up) < .06f &&
                Vector3.Angle(RaidPlan.Vector(farApproach.Forward), farDock.forward) < .06f &&
                Mathf.Abs(Vector3.Dot(RaidPlan.Vector(farApproach.Position) - farDock.position, farDock.up)) < .001f,
                $"The rotary far deck and native machine inherit the same docking axis and elevation ({degrees}°)");
            CheckSight(cabin, false);
            var rebase = new RaidWorldRebase();
            Vector3 Offset(float width, float height, float depth) => cabin.Rotor.TransformPoint(new Vector3(width, height, depth));
            var observer = PlayerEnvelope(Offset(-1.35f, -RaidQuotaRotaryCabin.HalfHeight + .03f, -4.2f));
            var cart = Body(stage, "Stock cart envelope", Offset(.50f, -RaidQuotaRotaryCabin.HalfHeight + .03f, -4.2f));
            cart.rotation = cabin.Rotor.rotation;
            cart.transform.rotation = cart.rotation;
            var cargoEnvelope = RaidExtraction.LoadingRoute(module).Envelope;
            var cartBox = cart.gameObject.AddComponent<BoxCollider>();
            const float chassisClearance = .24f;
            cartBox.size = new Vector3(cargoEnvelope.Z, cargoEnvelope.Y - chassisClearance, cargoEnvelope.X);
            cartBox.center = Vector3.up * ((cargoEnvelope.Y + chassisClearance) / 2);
            foreach (float wheelX in new[] { -.65f, .65f })
            foreach (float wheelZ in new[] { -1f, 1f })
            {
                var wheel = cart.gameObject.AddComponent<SphereCollider>();
                wheel.radius = .24f;
                wheel.center = new Vector3(wheelX, wheel.radius, wheelZ);
            }
            var cargo = Body(stage, "Loose cargo", Offset(1.76f, -RaidQuotaRotaryCabin.HalfHeight + .24f, -4.2f));
            cargo.gameObject.AddComponent<BoxCollider>().size = new Vector3(.42f, .42f, .42f);
            cargo.mass = .2f;
            var actors = new[] { cart, cargo };
            Physics.SyncTransforms();

            Require(cabin.NearOpen && !cabin.FarOpen, "Near gate available and far gate interlocked before departure");
            MoveTo(-2f);
            Require(cabin.CanBoard(observer.position), "Player can reach the button while the cart still straddles the threshold");
            Require(!cabin.TryBegin(observer, cart, _ => true), "No departure with cart across the near threshold");
            Require(cabin.NearOpen && !cabin.FarOpen, "Blocked departure leaves the approach open");
            MoveTo(0f);
            Require(cabin.CanBoard(observer.position), "Player reaches the transfer switch from the near deck");
            Require(cabin.Rotor.InverseTransformPoint(cart.position).z > -1.45f, "Cart reaches the full cabin floor");

            for (int trip = 0; trip < 4; trip++)
            {
                bool far = (trip & 1) == 0;
                Vector3 startingPoint = observer.position;
                Vector3 cargoRelative = cabin.Rotor.InverseTransformPoint(cargo.position);
                Quaternion upright = cabin.Rotor.rotation;
                Vector3 currentFarUp = stage.up;
                Require(cabin.TryBegin(observer, cart, _ => true),
                    $"Cabin starts on trip {trip + 1} with player={cabin.Rotor.InverseTransformPoint(observer.position):F3}, " +
                    $"cart={cabin.Rotor.InverseTransformPoint(cart.position):F3}, cargo={cargoRelative:F3}");
                Require(!cabin.NearOpen && !cabin.FarOpen, "Both apertures lock before rotation");
                int steps = 0;
                while (cabin.IsTurning && steps++ < 150)
                {
                    Require(!cabin.CanBoard(observer.position) &&
                        !cabin.CanCall(false, cabin.DockFrame(false).TransformPoint(new Vector3(0, .03f, -1.55f))) &&
                        !cabin.CanCall(true, cabin.DockFrame(true).TransformPoint(new Vector3(0, .03f, 3.25f))),
                        "None of the three controls can actuate during a transfer");
                    cabin.Step(Tick, observer, (feet, rotation, aboard) => rebase.Apply(stage, observer, actors, feet, rotation, aboard));
                    Physics.Simulate(Tick);
                    Require(Quaternion.Angle(cabin.Rotor.rotation, upright) < .06f, "Rider frame stays upright through the transfer");
                }
                Require(!cabin.IsTurning && steps is >= 139 and <= 142, "Both directions use the timed smooth rotation");
                Require(cabin.FarOpen == far && cabin.NearOpen != far, "Only the destination gate releases at the end of each turn");
                Require(Mathf.Abs(Vector3.Angle(currentFarUp, stage.up) - Mathf.Abs(degrees)) < .06f,
                    $"Cabin rolls through {degrees}° for this layout");
                Require(Vector3.Distance(observer.position, startingPoint) < .055f, "Player remains supported during the turn");
                Vector3 cargoAfter = cabin.Rotor.InverseTransformPoint(cargo.position);
                Require(Vector3.Distance(cargoRelative, cargoAfter) < .14f,
                    $"Loose freight rides in place on trip {trip + 1}, before={cargoRelative:F3}, after={cargoAfter:F3}");
                float side = far ? 1f : -1f;
                MoveTo(side * 4.65f);
                Vector3 playerLocal = cabin.Rotor.InverseTransformPoint(observer.position);
                Vector3 cartLocal = cabin.Rotor.InverseTransformPoint(cart.position);
                Require(side * playerLocal.z > 3.8f && side * cartLocal.z > 3.8f,
                    $"Player and cart physically cross the destination threshold on trip {trip + 1}, far={far}, player={playerLocal:F3}, cart={cartLocal:F3}");
                Require(playerLocal.y > -RaidQuotaRotaryCabin.HalfHeight - .08f &&
                    cartLocal.y > -RaidQuotaRotaryCabin.HalfHeight - .08f,
                    "Destination floor supports both crossing bodies");
                if (far)
                    Require(cabin.TryUp(cart.position, out var up) && Vector3.Dot(up, Vector3.up) > .9999f,
                        "Far floor gravity agrees with the cabin at the threshold");
                MoveTo(0f);
            }

            CheckInterruptedTrip(far: false, destroyRider: true);
            Require(cabin.TryBegin(observer, cart, _ => true), "Park the cabin at the far dock for the reverse regression");
            for (int step = 0; cabin.IsTurning && step < 150; step++) AdvanceObserved();
            CheckInterruptedTrip(far: true, destroyRider: false);
            Debug.Log($"{Label} {degrees}°: staged player, cart and freight crossed both directions. " +
                "The replacement recalled, boarded and rode from each opposite dock after an interruption.");
            CheckEmptyCalls();
            UnityEngine.Object.DestroyImmediate(stage.gameObject);

            void CheckInterruptedTrip(bool far, bool destroyRider)
            {
                Require(cabin.FarOpen == far && cabin.NearOpen != far, "Controller-loss fixture starts at its intended dock");
                Require(cabin.TryBegin(observer, cart, _ => true), "Board the cabin for the controller-loss regression");
                for (int step = 0; step < 50; step++) AdvanceObserved();
                Require(cabin.IsTurning && !cabin.NearOpen && !cabin.FarOpen, "Interruption occurs between sealed docks");
                Vector3 cartLocal = cabin.Rotor.InverseTransformPoint(cart.position);
                Vector3 cargoLocal = cabin.Rotor.InverseTransformPoint(cargo.position);
                Quaternion fixedStage = stage.rotation;
                Rigidbody replacement = null;
                Transform opposite = cabin.DockFrame(!far);
                Vector3 replacementPosition = opposite.TransformPoint(new Vector3(-1.35f, .025f, far ? -2.85f : 3.35f));
                if (destroyRider)
                    UnityEngine.Object.DestroyImmediate(observer.gameObject);
                else
                {
                    replacement = PlayerEnvelope(replacementPosition);
                    replacement.isKinematic = true;
                }

                for (int step = 0; cabin.IsTurning && step < 150; step++)
                {
                    Require(!cabin.NearOpen && !cabin.FarOpen, "Both doors stay sealed while the cabin returns");
                    Require(!cabin.Step(Tick, replacement, (_, __, ___) => throw new InvalidOperationException("Unattended cabin rebased the level")),
                        "A replacement outside does not become the cabin observer");
                    SimulateDetached(observer);
                }
                Require(!cabin.IsTurning && cabin.FarOpen == far && cabin.NearOpen != far,
                    "The cabin returns to the departure dock after losing its controller");
                Require(Quaternion.Angle(cabin.Rotor.localRotation, Quaternion.AngleAxis(far ? degrees : 0f, Vector3.forward)) < .06f,
                    "The departure gate only releases at its aligned rotor pose");
                Require(Quaternion.Angle(stage.rotation, fixedStage) < .06f &&
                    (!replacement || Vector3.Distance(replacement.position, replacementPosition) < .001f),
                    "An unattended return moves neither the stage nor an outside controller");
                Require(Vector3.Distance(cabin.Rotor.InverseTransformPoint(cart.position), cartLocal) < .14f &&
                    Vector3.Distance(cabin.Rotor.InverseTransformPoint(cargo.position), cargoLocal) < .14f,
                    "The cart and loose freight return to the departure dock and remain aboard");

                if (observer) UnityEngine.Object.DestroyImmediate(observer.gameObject);
                observer = replacement ? replacement : PlayerEnvelope(replacementPosition);
                rebase.Apply(stage, observer, actors, observer.position, cabin.LevelFrom(opposite.up));
                observer.isKinematic = false;
                Require(!cabin.CanBoard(observer.position), "The inside switch is out of reach on the opposite apron");
                Require(cabin.CanCall(!far, observer.position), "The stranded replacement has a call switch on its own apron");
                Require(!cabin.TryCall(observer, far, _ => true), "The opposite apron cannot activate the distant call switch");

                Transform dock = cabin.DockFrame(far);
                var obstruction = Body(stage, "Shutter obstruction", dock.TransformPoint(new Vector3(0, .3f, far ? .07f : -.07f)));
                obstruction.isKinematic = true;
                obstruction.gameObject.AddComponent<BoxCollider>().size = Vector3.one * .40f;
                Physics.SyncTransforms();
                Require(!cabin.TryCall(observer, !far, _ => true) && cabin.FarOpen == far && cabin.NearOpen != far,
                    "An occupied departure threshold blocks a call without closing its gate");
                obstruction.position = opposite.TransformPoint(new Vector3(0, .3f, far ? -.07f : .07f));
                Physics.SyncTransforms();
                Require(!cabin.TryCall(observer, !far, _ => true) && cabin.FarOpen == far && cabin.NearOpen != far,
                    "An occupied destination threshold also blocks a call");
                UnityEngine.Object.DestroyImmediate(obstruction.gameObject);
                Physics.SyncTransforms();
                Require(cabin.TryCall(observer, !far, _ => true), "The same stranded replacement can call the parked cabin");
                Require(!cabin.TryCall(observer, !far, _ => true), "A repeated call does not restart a trip");
                Quaternion callStage = stage.rotation;
                Vector3 callFeet = observer.position;
                for (int step = 0; cabin.IsTurning && step < 150; step++)
                {
                    Require(!cabin.NearOpen && !cabin.FarOpen, "Both doors seal during a recall");
                    Require(!cabin.Step(Tick, observer, (_, __, ___) => throw new InvalidOperationException("Recall rebased the level")),
                        "The cabin calls without an observer aboard");
                    SimulateDetached(null);
                }
                Require(!cabin.IsTurning && cabin.FarOpen != far && cabin.NearOpen == far,
                    "Recall opens the correct opposite dock after alignment");
                Require(Quaternion.Angle(stage.rotation, callStage) < .06f && Vector3.Distance(observer.position, callFeet) < .08f,
                    "Recall leaves the outside player's frame and physical position intact");
                Require(Vector3.Distance(cabin.Rotor.InverseTransformPoint(cart.position), cartLocal) < .20f &&
                    Vector3.Distance(cabin.Rotor.InverseTransformPoint(cargo.position), cargoLocal) < .20f,
                    "The loaded cabin can be recalled without losing cargo");
                Require(!cabin.TryCall(observer, !far, _ => true), "Calling a cabin already at the platform is a no-op");

                MoveTo(0f);
                Require(cabin.CanBoard(observer.position) && !cabin.TryCall(observer, far, _ => true),
                    "The same replacement boards across the physical threshold and cannot use either outside switch from within");
                Require(cabin.TryBegin(observer, cart, _ => true), "The replacement starts an occupied trip after recall");
                for (int step = 0; cabin.IsTurning && step < 150; step++) AdvanceObserved();
                Require(!cabin.IsTurning && cabin.FarOpen == far && cabin.NearOpen != far,
                    "A subsequent occupied trip returns to the original source");
            }

            void AdvanceObserved()
            {
                cabin.Step(Tick, observer, (feet, rotation, aboard) => rebase.Apply(stage, observer, actors, feet, rotation, aboard));
                Physics.Simulate(Tick);
            }

            void SimulateDetached(Rigidbody originalRider)
            {
                foreach (var body in actors.Append(originalRider))
                    if (body && !body.IsSleeping() && cabin.TryUp(body.worldCenterOfMass, out Vector3 up))
                        body.AddForce(SphereGravityReorientation.ForceCorrection(up, Physics.gravity), ForceMode.Acceleration);
                Physics.Simulate(Tick);
            }

            void CheckEmptyCalls()
            {
                UnityEngine.Object.DestroyImmediate(observer.gameObject);
                UnityEngine.Object.DestroyImmediate(cart.gameObject);
                UnityEngine.Object.DestroyImmediate(cargo.gameObject);
                foreach (bool far in new[] { false, true })
                {
                    Transform dock = cabin.DockFrame(far);
                    Vector3 feet = dock.TransformPoint(new Vector3(-1.35f, .025f, far ? 3.35f : -2.85f));
                    var caller = PlayerEnvelope(feet);
                    rebase.Apply(stage, caller, actors, caller.position, cabin.LevelFrom(dock.up));
                    Require(cabin.TryCall(caller, far, _ => true) && !cabin.Passengers.Any(),
                        "The cabin parked after an occupied trip can be called empty");
                    int steps = 0;
                    while (cabin.IsTurning && steps++ < 150)
                    {
                        cabin.Step(Tick, steps <= 35 ? null : caller, null);
                        Physics.Simulate(Tick);
                    }
                    Require(!cabin.IsTurning && steps is >= 139 and <= 142 && cabin.FarOpen == far && cabin.NearOpen != far,
                        "An empty call reaches the selected dock even when the controller briefly goes away");
                    UnityEngine.Object.DestroyImmediate(caller.gameObject);
                    CheckSight(cabin, far);
                }
                Debug.Log($"{Label} {degrees}°: both idle calls complete through a controller gap. Both dock views clear.");
            }

            void MoveTo(float target)
            {
                // The contact solver subtracts a different amount of speed for each body.
                // Drive to a position under collision instead of assuming the commanded distance.
                for (int step = 0; step < 235; step++)
                {
                    float walk = target - cabin.Rotor.InverseTransformPoint(observer.position).z;
                    float push = target - cabin.Rotor.InverseTransformPoint(cart.position).z;
                    float freight = target - cabin.Rotor.InverseTransformPoint(cargo.position).z;
                    if (Mathf.Abs(walk) < .04f && Mathf.Abs(push) < .04f && Mathf.Abs(freight) < .04f) break;
                    Drive(observer, walk);
                    Drive(cart, push);
                    // A hand-carried item clears the seam above the deck. Drop it for the leg of the test that moves the cabin.
                    float lift = -.98f - cabin.Rotor.InverseTransformPoint(cargo.position).y;
                    cargo.linearVelocity = cabin.Rotor.forward * Mathf.Clamp(8f * freight, -2f, 2f) +
                        Vector3.up * Mathf.Clamp(12f * lift, -2f, 2f);
                    Physics.Simulate(Tick);
                }
                observer.linearVelocity = cart.linearVelocity = cargo.linearVelocity = Vector3.zero;
                for (int settle = 0; settle < 30; settle++) Physics.Simulate(Tick);
                Require(Mathf.Abs(target - cabin.Rotor.InverseTransformPoint(observer.position).z) < .07f &&
                    Mathf.Abs(target - cabin.Rotor.InverseTransformPoint(cart.position).z) < .07f &&
                    Mathf.Abs(target - cabin.Rotor.InverseTransformPoint(cargo.position).z) < .07f,
                    $"Colliding bodies reach {target:F3}, player={cabin.Rotor.InverseTransformPoint(observer.position):F3}, " +
                    $"cart={cabin.Rotor.InverseTransformPoint(cart.position):F3}, cargo={cabin.Rotor.InverseTransformPoint(cargo.position):F3}");
            }

            void Drive(Rigidbody body, float error) => body.linearVelocity =
                cabin.Rotor.forward * Mathf.Clamp(8f * error, -2f, 2f) + Vector3.Project(body.linearVelocity, Vector3.up);
        }

        private static void CheckSight(RaidQuotaRotaryCabin cabin, bool far)
        {
            Transform nearDock = cabin.DockFrame(false);
            Transform farDock = cabin.DockFrame(true);
            Vector3 machine = farDock.TransformPoint(new Vector3(0, 1.5f, 10.8f));
            Vector3 nearEntry = nearDock.TransformPoint(new Vector3(0, 1.5f, -6.80f));
            Vector3 source = far ? machine : nearEntry;
            Vector3 delta = (far ? nearEntry : machine) - source;
            Physics.SyncTransforms();
            var ray = new Ray(source, delta.normalized);
            var hits = Physics.RaycastAll(ray, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
            Require(hits.Length == 1 && hits[0].collider.name == "Interlocked shutter" &&
                !hits[0].collider.GetComponent<Renderer>().enabled,
                $"Entry-to-quota eye line crosses only an interlocked window, far dock open={far}: " +
                string.Join(", ", hits.Select(hit => hit.collider.name)));
            var rendered = cabin.GetComponentsInChildren<Renderer>().Where(renderer => renderer.enabled &&
                renderer.bounds.IntersectRay(ray, out float distance) && distance < delta.magnitude).ToArray();
            Require(rendered.Length == 1 && rendered[0].name == "Shutter glass" &&
                rendered[0].sharedMaterial.renderQueue == (int)UnityEngine.Rendering.RenderQueue.Transparent,
                "The shutter's opaque trim and cabin guards leave the glazing visible through the center sight line: " +
                string.Join(", ", rendered.Select(renderer => renderer.name)));
        }

        private static Rigidbody Body(Transform parent, string name, Vector3 position)
        {
            var node = RaidAssembly.Child(parent, name);
            node.position = position;
            var body = node.gameObject.AddComponent<Rigidbody>();
            body.constraints = RigidbodyConstraints.FreezeRotation;
            return body;
        }

        private static Rigidbody PlayerEnvelope(Vector3 position)
        {
            var rider = Body(null, "Player envelope", position);
            var capsule = rider.gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = .35f;
            capsule.height = 1.70f;
            capsule.center = Vector3.up * .85f;
            return rider;
        }

        private static void CheckSeed()
        {
            const int seed = -31;
            var start = new Pose(Vector3.zero, Quaternion.identity);
            foreach (int level in new[] { 0, 1, 5, 9, 14 })
            {
                var normal = RaidPlan.Generate(seed, start, level);
                var optedIn = RaidPlan.Generate(seed, start, level, rotaryQuota: true);
                int count = optedIn.Rooms.Count(room => room.Module.Id == RaidQuotaRotaryRoom.Id);
                Require(normal.Rooms.All(room => room.Module.Id != RaidQuotaRotaryRoom.Id), "Default raid never uses the prototype");
                Require(count == (normal.QuotaCount == 0 ? 0 : 1) && normal.QuotaCount == optedIn.QuotaCount,
                    $"Opt-in retains native quota count and replaces one terminal after quotas unlock (completed {level})");
            }
            foreach ((int sample, Vector3 up) in new[] { (-31, Vector3.forward), (-30, Vector3.back), (-29, Vector3.down) })
            {
                var generated = RaidPlan.Generate(sample, start, completedLevels: 1, rotaryQuota: true);
                var room = generated.Rooms.Single(entry => entry.Module.Id == RaidQuotaRotaryRoom.Id);
                var approach = RaidExtraction.LoadingRoute(room.Module).Knots.Last();
                Require(RaidPlan.Vector(approach.Up) == up,
                    $"Seed {sample} naturally generates its own far floor plane");
                Debug.Log($"{Label} seed {sample} generated far local up {up}");
            }
            Debug.Log($"{Label} seed {seed} verified with opt-in at completed levels 1, 5, 9 and 14");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException($"{Label}: {message}");
        }
    }
}

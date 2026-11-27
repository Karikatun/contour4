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
                foreach (float degrees in new[] { -90f, 90f, 180f })
                {
                    CheckRoomAndTransfer(degrees);
                    CheckOpenFaces(degrees);
                    CheckDeepVoid(degrees);
                }
                CheckLateralFall();
                CheckSeed();
                Debug.Log($"{Label} PASSED");
            }
            finally
            {
                Physics.simulationMode = previousSimulation;
                Physics.gravity = previousGravity;
            }
        }

        private static RaidQuotaRotaryCabin CreateCabin(Transform stage, GrayModule module, float degrees)
        {
            foreach (var block in module.Geometry.Where(block => block.Role is not BlockRole.Free and not BlockRole.Plug))
            {
                var (low, high) = block.Bounds;
                var part = RaidAssembly.Child(stage, block.Id);
                part.localPosition = RaidPlan.Vector((high + low) / 2);
                part.gameObject.AddComponent<BoxCollider>().size = RaidPlan.Vector(high - low);
            }
            return RaidQuotaRotaryCabin.Create(stage, RaidQuotaRotaryRoom.Center, RaidQuotaRotaryRoom.NearRotation,
                RaidQuotaRotaryRoom.FarField, 0, null, null, degrees, RaidQuotaRotaryRoom.RoomField);
        }

        private static void CheckRoomAndTransfer(float degrees)
        {
            Transform stage = new GameObject("Quota fixture").transform;
            var module = RaidQuotaRotaryRoom.Create(degrees);
            var cabin = CreateCabin(stage, module, degrees);
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

        private static void CheckOpenFaces(float degrees)
        {
            CheckExit(Vector3.left, startFar: false);
            CheckExit(Vector3.right, startFar: true);
            CheckExit(Vector3.up, startFar: false);
            CheckEntry();

            void CheckExit(Vector3 face, bool startFar)
            {
                Transform room = new GameObject("Open-face fixture").transform;
                var cabin = CreateCabin(room, RaidQuotaRotaryRoom.Create(degrees), degrees);
                Transform rotor = cabin.Rotor;
                bool roof = face == Vector3.up;
                float x = roof ? 1.05f : .90f * face.x;
                var player = PlayerEnvelope(rotor.TransformPoint(new Vector3(x, -1.61f, 1.4f)), roof ? 1.70f : 1.15f);
                var cargo = TestCargo(room, rotor.TransformPoint(new Vector3(x, -1.41f, -1.4f)));
                var bodies = new[] { cargo };
                var rebase = new RaidWorldRebase();
                string label = $"{degrees}° {(roof ? "roof" : face == Vector3.left ? "left side" : "right side")}";
                Physics.SyncTransforms();
                if (startFar)
                {
                    Require(cabin.TryBegin(player, cargo, _ => true), "Stage the open-face fixture at the far platform");
                    for (int step = 0; cabin.IsTurning && step < 150; step++) Advance();
                    Require(cabin.FarOpen, "The return fixture starts at its far dock");
                    Transform dock = cabin.DockFrame(true);
                    VerifyUp(cabin, dock.TransformPoint(new Vector3(0, .40f, 6f)), dock.up, "far platform keeps its dock gravity");
                    VerifyUp(cabin, dock.TransformPoint(new Vector3(5.72f, .40f, 6f)), room.up, "air beyond the far platform uses room gravity");
                }

                Require(cabin.TryBegin(player, cargo, _ => true), $"{label} trip starts with stock-shaped load aboard");
                for (int step = 0; step < (roof ? 30 : 36); step++) Advance();
                Require(Vector3.Angle(room.up, rotor.up) > 5f, "Sample after a meaningful cabin/room roll");
                cargo.linearVelocity = rotor.TransformDirection(face) * (roof ? 14f : 7f);
                bool cargoFell = false, cargoReleasedWhileRiding = false, playerFell = false;
                for (int step = 0; step < 94 && cabin.IsTurning; step++)
                {
                    if (step == (roof ? 14 : 19))
                        player.linearVelocity = rotor.TransformDirection(face) * (roof ? 17f : 7f) +
                            (roof ? Vector3.zero : rotor.up * 1.7f);

                    bool cargoOutside = !cabin.ContainsCabin(cargo.worldCenterOfMass);
                    bool playerOutside = !cabin.ContainsCabin(player.position);
                    if (playerOutside && !playerFell)
                        VerifyUp(cabin, player.position, room.up, $"{label} player gets room gravity on the exit tick");
                    if (cargoOutside)
                    {
                        Vector3 before = room.InverseTransformPoint(cargo.position);
                        if (!cargoFell)
                            VerifyUp(cabin, cargo.worldCenterOfMass, room.up, $"{label} cargo gets room gravity on the exit tick");
                        cabin.Step(Tick, player, Rebase, nearbyActors: bodies);
                        cargoReleasedWhileRiding |= !playerOutside && cabin.IsTurning;
                        Require(Vector3.Distance(before, room.InverseTransformPoint(cargo.position)) < .001f &&
                            !cabin.Passengers.Contains(cargo), $"{label} loose cargo stays at its room pose across the turn after exiting");
                    }
                    else cabin.Step(Tick, player, Rebase, nearbyActors: bodies);

                    if (cabin.TryUp(player.position, out var up)) cabin.LevelObserver(player, up, Rebase);
                    bool clearCargo = cargoOutside && BeyondFace(cargo.worldCenterOfMass);
                    bool clearPlayer = playerOutside && BeyondFace(player.position);
                    if (clearPlayer) Require(Vector3.Angle(room.up, Vector3.up) < .06f, $"{label} the stock controller follows room up");
                    float cargoSpeed = Vector3.Dot(cargo.linearVelocity, room.up);
                    float playerSpeed = Vector3.Dot(player.linearVelocity, room.up);
                    Simulate();
                    if (clearCargo && cabin.IsTurning)
                        cargoFell |= ExpectedFall(cargoSpeed, cargo, room.up);
                    if (clearPlayer && cabin.IsTurning)
                        playerFell |= ExpectedFall(playerSpeed, player, room.up);
                    if (cargoFell && cargoReleasedWhileRiding && playerFell) break;
                }
                Require(cargoFell && cargoReleasedWhileRiding && playerFell,
                    $"{label}: both bodies physically exit under room gravity during the turn, " +
                    $"cargo {cargoFell}, cargo outside first {cargoReleasedWhileRiding}, player {playerFell}, " +
                    $"cargo pose {rotor.InverseTransformPoint(cargo.position):F3}, player pose {rotor.InverseTransformPoint(player.position):F3}");
                for (int step = 0; cabin.IsTurning && step < 150; step++) Advance();
                Require(!cabin.IsTurning && cabin.FarOpen != startFar && cabin.NearOpen == startFar,
                    $"{label} an active controller's exit leaves the requested direction intact");
                Debug.Log($"{Label} {label}: player and loose cargo crossed live colliders, used room gravity and detached while the requested trip continued");
                UnityEngine.Object.DestroyImmediate(player.gameObject);
                UnityEngine.Object.DestroyImmediate(room.gameObject);

                bool BeyondFace(Vector3 point) => Vector3.Dot(rotor.InverseTransformPoint(point), face) >
                    (roof ? RaidQuotaRotaryCabin.HalfHeight : RaidQuotaRotaryCabin.HalfWidth);

                void Rebase(Vector3 pivot, Quaternion rotation, System.Collections.Generic.ISet<Rigidbody> transported) =>
                    rebase.Apply(room, player, bodies, pivot, rotation, transported);

                void Advance()
                {
                    cabin.Step(Tick, player, Rebase, nearbyActors: bodies);
                    if (cabin.TryUp(player.position, out var up)) cabin.LevelObserver(player, up, Rebase);
                    Simulate();
                }

                void Simulate()
                {
                    if (cabin.TryUp(cargo.worldCenterOfMass, out var up))
                        cargo.AddForce(SphereGravityReorientation.ForceCorrection(up, Physics.gravity), ForceMode.Acceleration);
                    Physics.Simulate(Tick);
                }
            }

            void CheckEntry()
            {
                Transform room = new GameObject("Open-face reentry fixture").transform;
                var cabin = CreateCabin(room, RaidQuotaRotaryRoom.Create(degrees), degrees);
                Transform rotor = cabin.Rotor;
                var player = PlayerEnvelope(cabin.DockFrame(true).TransformPoint(new Vector3(0, .03f, 3.35f)), 1.12f);
                player.isKinematic = true;
                Physics.SyncTransforms();
                Require(cabin.TryCall(player, true, _ => true), "Set up an empty recall to observe a moving side opening");
                for (int i = 0; i < 14; i++) cabin.Step(Tick, player, null);
                player.position = rotor.TransformPoint(new Vector3(-2.75f, -1.32f, 1.4f));
                player.isKinematic = false;
                player.linearVelocity = rotor.right * 6f + Vector3.up * .95f;
                var cargo = TestCargo(room, rotor.TransformPoint(new Vector3(-2.72f, -.90f, -1.4f)));
                cargo.linearVelocity = rotor.right * 5f;
                var bodies = new System.Collections.Generic.List<Rigidbody>();
                var rebase = new RaidWorldRebase();
                Physics.SyncTransforms();
                Require(!cabin.ContainsCabin(player.position) && !cabin.ContainsCabin(cargo.worldCenterOfMass),
                    "Reentry begins clear of the left face");
                VerifyUp(cabin, player.position, room.up, "approach to the moving opening uses room gravity");
                bool playerEntered = false, cargoEntered = false;
                for (int i = 0; i < 30 && (!playerEntered || !cargoEntered); i++)
                {
                    cabin.TrackEntrants(bodies, body => body == cargo);
                    cabin.Step(Tick, player, Rebase, nearbyActors: bodies);
                    if (cabin.TryUp(player.position, out var up)) cabin.LevelObserver(player, up, Rebase);
                    if (cabin.ContainsCabin(player.position))
                    {
                        VerifyUp(cabin, player.position, rotor.up, "player crossing inward immediately uses cabin gravity");
                        Require(Vector3.Angle(rotor.up, Vector3.up) < .06f,
                            "The stock movement frame follows the entered cabin");
                        playerEntered = true;
                    }
                    if (cabin.TryUp(cargo.worldCenterOfMass, out var cargoUp))
                        cargo.AddForce(SphereGravityReorientation.ForceCorrection(cargoUp, Physics.gravity), ForceMode.Acceleration);
                    if (cabin.Passengers.Contains(cargo))
                    {
                        VerifyUp(cabin, cargo.worldCenterOfMass, rotor.up, "cargo entering from outside joins the moving cabin");
                        cargoEntered = true;
                    }
                    Physics.Simulate(Tick);
                }
                Require(playerEntered && cargoEntered && cabin.IsTurning && !cabin.NearOpen && !cabin.FarOpen,
                    $"{degrees}° both bodies enter via a moving side with the axial shutters still locked, " +
                    $"player {playerEntered} {rotor.InverseTransformPoint(player.position):F3}, " +
                    $"cargo {cargoEntered} {rotor.InverseTransformPoint(cargo.position):F3}");
                Debug.Log($"{Label} {degrees}°: passing inward through a side gap reapplies cabin gravity to player and cargo during recall");
                UnityEngine.Object.DestroyImmediate(player.gameObject);
                UnityEngine.Object.DestroyImmediate(room.gameObject);

                void Rebase(Vector3 pivot, Quaternion rotation, System.Collections.Generic.ISet<Rigidbody> transported) =>
                    rebase.Apply(room, player, bodies, pivot, rotation, transported);
            }
        }

        private static void VerifyUp(RaidQuotaRotaryCabin cabin, Vector3 point, Vector3 expected, string message)
        {
            bool found = cabin.TryUp(point, out var up);
            Require(found && Vector3.Angle(up, expected) < .06f,
                $"{message} at rotor {cabin.Rotor.InverseTransformPoint(point):F3}, got {up:F3}, expected {expected:F3}");
        }

        private static void CheckDeepVoid(float degrees)
        {
            Transform room = new GameObject("Rotary deep void fixture").transform;
            var cabin = CreateCabin(room, RaidQuotaRotaryRoom.Create(degrees), degrees);
            var player = PlayerEnvelope(cabin.Rotor.TransformPoint(new Vector3(-1.05f, -1.61f, 0)));
            var rebase = new RaidWorldRebase();
            Physics.SyncTransforms();
            Require(cabin.TryBegin(player, null, _ => true), "The deep-void fixture travels first to the far dock");
            while (cabin.IsTurning)
            {
                cabin.Step(Tick, player, (pivot, turn, load) => rebase.Apply(room, player, Array.Empty<Rigidbody>(), pivot, turn, load));
                Physics.Simulate(Tick);
            }

            const float launchDepth = -4.8f;
            Vector3 DeepPoint(float depth, float axial = 10f) => room.TransformPoint(new Vector3(axial, depth, 0));
            var cargo = TestCargo(room, DeepPoint(launchDepth));
            cargo.linearVelocity = -room.up * 10f;
            var bodies = new[] { cargo };
            Physics.SyncTransforms();
            Require(!cabin.FellInVoid(cargo.position, cargo.linearVelocity), "A short drop still permits a recovery");
            Require(cabin.FellInVoid(DeepPoint(-31.5f), -room.up), "A direct hit in the local loss band is detected");
            Require(!cabin.FellInVoid(DeepPoint(-30.8f, -.5f), -room.up), "The void cannot claim a neighboring hall");
            Require(!cabin.FellInVoid(DeepPoint(-31.5f), room.up), "The void cannot claim upward-moving bodies");
            Require(!cabin.FellInVoid(DeepPoint(-80f), -room.up), "The void cannot claim an unrelated falling body far below the room");
            var far = cabin.DockFrame(true);
            Require(!cabin.FellInVoid(far.TransformPoint(new Vector3(0, 33.8f, 6f)), -room.up),
                "The tilted stock extraction airspace is excluded from room-void loss");
            Require(cabin.TryBegin(player, null, _ => true), "The player begins an occupied return above the falling cargo");

            bool lost = false, delayedLoss = false;
            float lowestWorldHeight = cargo.position.y;
            for (int i = 0; i < 115 && cabin.IsTurning; i++)
            {
                cabin.Step(Tick, player, (pivot, turn, load) => rebase.Apply(room, player, bodies, pivot, turn, load), nearbyActors: bodies);
                Vector3 center = cargo.worldCenterOfMass;
                cabin.ObserveBody(cargo, center);
                Require(cabin.TryRoomUp(center, out var up, cargo) && Vector3.Angle(up, room.up) < .06f,
                    "The room guides the loose cargo down during the occupied return");
                float speed = Vector3.Dot(cargo.linearVelocity, room.up);
                cargo.AddForce(SphereGravityReorientation.ForceCorrection(room.up, Physics.gravity), ForceMode.Acceleration);
                Physics.Simulate(Tick);
                lowestWorldHeight = Mathf.Min(lowestWorldHeight, cargo.position.y);
                Require(Mathf.Abs(Vector3.Dot(cargo.linearVelocity, room.up) - speed + 9.81f * Tick) < .012f,
                    "The falling cargo accelerates along room down even as a rider rebases the level");
                if (!cabin.FellInVoid(cargo.position, cargo.linearVelocity, cargo)) continue;
                if (room.InverseTransformPoint(cargo.position).y >= -34.5f)
                {
                    lost = true;
                    continue;
                }
                delayedLoss = !cabin.FellInVoid(cargo.position, cargo.linearVelocity);
                break;
            }
            Require(lost && delayedLoss && cabin.IsTurning && lowestWorldHeight > -50f,
                $"{degrees}° a real shaft fall still qualifies for loss after passing the local band and before the stock world floor");
            var replacement = TestCargo(room, cargo.position + room.up * .2f);
            replacement.linearVelocity = cargo.linearVelocity;
            cabin.TransferObserverBody(cargo, replacement);
            Require(!cabin.TracksBody(cargo) && cabin.FellInVoid(replacement.position, replacement.linearVelocity, replacement),
                "A continuous same-avatar observer body swap retains the tracked fall");
            var distant = TestCargo(room, DeepPoint(-80f));
            cabin.TransferObserverBody(replacement, distant);
            Require(!cabin.FellInVoid(distant.position, -room.up, distant),
                "A discontinuous observer body swap cannot carry ownership down to an unrelated hall");
            Debug.Log($"{Label} {degrees}°: deep cargo reaches the local loss boundary during an occupied return " +
                $"at room y={room.InverseTransformPoint(cargo.position).y:F2} m, minimum world y={lowestWorldHeight:F2} m");
            UnityEngine.Object.DestroyImmediate(player.gameObject);
            UnityEngine.Object.DestroyImmediate(room.gameObject);
        }

        private static bool ExpectedFall(float before, Rigidbody body, Vector3 up) =>
            Mathf.Abs(Vector3.Dot(body.linearVelocity, up) - before - Vector3.Dot(Physics.gravity, up) * Tick) < .025f;

        private static void CheckLateralFall()
        {
            const float degrees = 180f;
            Transform room = new GameObject("Rotary lateral fall fixture").transform;
            var cabin = CreateCabin(room, RaidQuotaRotaryRoom.Create(degrees), degrees);
            Transform rotor = cabin.Rotor;
            var player = PlayerEnvelope(rotor.TransformPoint(new Vector3(1.22f, -1.61f, 1.25f)));
            var cargo = TestCargo(room, rotor.TransformPoint(new Vector3(-.85f, -1.41f, -1.4f)));
            var bodies = new[] { cargo };
            var rebase = new RaidWorldRebase();
            Physics.SyncTransforms();
            Require(cabin.TryBegin(player, null, _ => true), "Set up the inverted landing with cargo on the cabin deck");
            for (int i = 0; cabin.IsTurning && i < 150; i++) Advance();
            Require(cabin.FarOpen && Vector3.Dot(room.up, Vector3.up) < -.999f, "The room starts inverted relative to the observer");
            Require(cabin.TryBegin(player, null, _ => true), "An occupied reverse trip starts with cargo inside");
            for (int i = 0; i < 7; i++) Advance();
            Require(cabin.TracksBody(cargo) && cabin.ContainsCabin(cargo.worldCenterOfMass), "The loose cargo is tracked from inside");
            cargo.linearVelocity = rotor.TransformDirection(new Vector3(-6f, 5f, 0));
            bool escaped = false, beyondFootprint = false, lost = false;
            for (int i = 0; cabin.IsTurning && i < 140; i++)
            {
                Advance();
                Vector3 center = cargo.worldCenterOfMass;
                Vector3 local = room.InverseTransformPoint(center);
                escaped |= rotor.InverseTransformPoint(center).x < -RaidQuotaRotaryCabin.HalfWidth;
                if (!cabin.TryRoomUp(center, out _) && local.y > -30f)
                {
                    Require(cabin.TryRoomUp(center, out var up, cargo) && Vector3.Angle(up, room.up) < .06f,
                        "Only the body that actually left the cage retains room gravity past its ordinary footprint");
                    beyondFootprint = true;
                }
                if (!cabin.FellInVoid(cargo.position, cargo.linearVelocity, cargo)) continue;
                lost = true;
                Require(!cabin.FellInVoid(cargo.position, cargo.linearVelocity),
                    "At the same coordinates an unrelated body would not take the rotary room's loss path");
                Debug.Log($"{Label} 180°: tracked loose cargo exits and falls past room z={local.z:F2} m to local y={local.y:F2} m " +
                    $"with the observer still aboard and world y={cargo.position.y:F2} m");
                break;
            }
            Require(escaped && beyondFootprint && lost && cabin.IsTurning,
                $"A real side exit retains room gravity until loss even past the room footprint. " +
                $"escaped={escaped}, beyond={beyondFootprint}, lost={lost}, room={room.InverseTransformPoint(cargo.position):F3}");

            Vector3 farReset = cabin.DockFrame(true).TransformPoint(new Vector3(0, .2f, 4f));
            cargo.position = farReset;
            cabin.ObserveBody(cargo, farReset);
            Require(!cabin.TracksBody(cargo), "The stock reset or entry to the far landing releases the old room frame");
            VerifyUp(cabin, farReset, cabin.DockFrame(true).up, "The platform owns gravity when a departed body reaches it");
            UnityEngine.Object.DestroyImmediate(player.gameObject);
            UnityEngine.Object.DestroyImmediate(room.gameObject);

            void Advance()
            {
                cabin.Step(Tick, player, (pivot, turn, carried) => rebase.Apply(room, player, bodies, pivot, turn, carried),
                    nearbyActors: bodies);
                Vector3 center = cargo.worldCenterOfMass;
                cabin.ObserveBody(cargo, center);
                if (cabin.TryCabinOrDockUp(center, out var up) || cabin.TryRoomUp(center, out up, cargo))
                    cargo.AddForce(SphereGravityReorientation.ForceCorrection(up, Physics.gravity), ForceMode.Acceleration);
                Physics.Simulate(Tick);
            }
        }

        private static Rigidbody TestCargo(Transform room, Vector3 position)
        {
            var cargo = Body(room, "Open-face freight", position);
            cargo.gameObject.AddComponent<SphereCollider>().radius = .19f;
            cargo.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            return cargo;
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

        private static Rigidbody PlayerEnvelope(Vector3 position, float height = 1.70f)
        {
            var rider = Body(null, "Player envelope", position);
            var capsule = rider.gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = .35f;
            capsule.height = height;
            capsule.center = Vector3.up * (height * .5f);
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

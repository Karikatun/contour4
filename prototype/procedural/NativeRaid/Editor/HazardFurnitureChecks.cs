using System;
using System.Linq;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Catalog;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Contour4.NativeRaid.Editor
{
    public static class HazardFurnitureChecks
    {
        private const float Step = .02f;

        public static void Run()
        {
            var previousGravity = Physics.gravity;
            var previousSimulation = Physics.simulationMode;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Physics.simulationMode = SimulationMode.Script;
                var owner = new GameObject("Production raid mechanics check");
                try
                {
                    var raid = owner.AddComponent<RaidAssembly>();
                    raid.Place(42, new Pose(Vector3.zero, Quaternion.identity), 0, 0, catalogSites: true, completedLevels: 7);
                    var runtime = RaidRoomInteractions.Attach(raid);
                    Expect(ReferenceEquals(runtime, RaidRoomInteractions.Attach(raid)), "Duplicate installation");
                    var trench = raid.Rooms.Single(room => room.Plan.Module.Id == MaintenanceTrench.Id);
                    Expect(raid.Plan.Freight.Any(link => link.To.Room == trench.Plan), "No native freight connection to the selected trench");
                    var lab = raid.Rooms.FirstOrDefault(room => room.Plan.Module.Id == "observation-lab-broken-floor");
                    if (lab != null)
                        Expect(runtime.CatchesFall(lab.Root.TransformPoint(new Vector3(10.275f, -.5f, 0)),
                            lab.Root.TransformPoint(new Vector3(10.275f, -1.6f, 0))), "Selected broken lab shaft has no death volume");
                    Physics.SyncTransforms();
                    CheckCabinets(raid, runtime, trench);
                    CheckShutter(runtime, trench);
                    CheckTrench(runtime, trench);
                    Debug.Log($"CONTOUR4 HAZARD FURNITURE PASS seed={raid.Plan.Seed} level={raid.Plan.CompletedLevels + 1}");
                }
                finally { Object.DestroyImmediate(owner); }
            }
            finally
            {
                Physics.gravity = previousGravity;
                Physics.simulationMode = previousSimulation;
            }
        }

        private static void CheckCabinets(RaidAssembly raid, RaidRoomInteractions runtime, BakedRoom trench)
        {
            var root = trench.Root;
            var cabinet = runtime.Cabinets.Single(front => front.Body.name == "trench-isolation-cabinet-door pivot");
            var panel = cabinet.Parts[0];
            var hinge = cabinet.Body.GetComponent<HingeJoint>();
            Expect(hinge && hinge.connectedBody && hinge.connectedBody.isKinematic && hinge.useLimits,
                "Cabinet needs an anchored physical hinge");
            var storage = raid.Rooms.Single(room => room.Plan.Module.Id.StartsWith("sample-storage", StringComparison.Ordinal)).Root;
            var drawer = runtime.Cabinets.Single(front => front.Body.transform.parent == storage &&
                front.Body.name == "samples-specimen-drawers-drawer-0 pivot");
            var rail = drawer.Body.GetComponent<ConfigurableJoint>();
            Expect(rail && rail.connectedBody && rail.connectedBody.isKinematic && rail.xMotion == ConfigurableJointMotion.Limited &&
                rail.angularXMotion == ConfigurableJointMotion.Locked, "Drawer needs an anchored physical slide");

            var shelf = root.Find("trench-isolation-cabinet-shelf-1").GetComponent<BoxCollider>();
            float shelfTop = shelf.transform.localPosition.y + shelf.transform.localScale.y / 2;
            Vector3 shelfSeat = shelf.transform.localPosition + Vector3.up * (shelfTop - shelf.transform.localPosition.y);
            var cast = new Ray(root.TransformPoint(new Vector3(shelfSeat.x, shelfTop + .12f, 4.1f)), root.forward);
            Expect(FirstHit(cast).collider == panel && FirstHit(cast).transform == cabinet.Body.transform,
                "Closed cabinet must be the first physical grab target at shelf height");
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Quaternion closed = cabinet.Body.rotation;
            Vector3 handle = cabinet.Body.transform.InverseTransformPoint(cabinet.Parts[1].bounds.center);
            float openAngle = Mathf.Abs(hinge.limits.min) > 0 ? hinge.limits.min : hinge.limits.max;
            Quaternion open = closed * Quaternion.AngleAxis(openAngle, Vector3.up);
            Vector3 closedHandle = cabinet.Body.position + closed * handle;
            Vector3 openHandle = cabinet.Body.position + open * handle;
            try
            {
                blocker.transform.position = cast.GetPoint(.5f);
                blocker.transform.localScale = Vector3.one * .3f;
                Physics.SyncTransforms();
                Expect(FirstHit(cast).collider == blocker.GetComponent<Collider>(), "An obstruction must be hit before the door");

                blocker.transform.position = cabinet.Body.position + closed * Quaternion.AngleAxis(openAngle * .37f, Vector3.up) * handle;
                blocker.transform.localScale = Vector3.one * .24f;
                Physics.SyncTransforms();
                Pull(cabinet.Body, handle, openHandle);
                float stopped = Quaternion.Angle(closed, cabinet.Body.rotation);
                Expect(stopped > 5f && stopped < 65f, $"Door passed through an obstruction or failed to begin pulling ({stopped:F1}°)");
            }
            finally { Object.DestroyImmediate(blocker); }

            Pull(cabinet.Body, handle, openHandle);
            Expect(Quaternion.Angle(open, cabinet.Body.rotation) < 4f && panel.enabled && !panel.isTrigger,
                $"Open door lost its solid collider or stop ({Quaternion.Angle(closed, cabinet.Body.rotation):F1}°)");
            VerifyShelfAccess(root, cast, shelf, shelfSeat);
            Vector3 panelCenter = panel.bounds.center;
            var closeRay = new Ray(panelCenter + panel.transform.forward * .3f, -panel.transform.forward);
            Expect(FirstHit(closeRay).rigidbody == cabinet.Body, "Open panel must remain grabbable");

            var observer = new GameObject("Cabinet gravity check observer").AddComponent<Rigidbody>();
            observer.isKinematic = true;
            Vector3 originalPosition = root.position;
            Vector3 shiftedPosition = root.position + new Vector3(4f, 60f, 9f);
            Quaternion turn = Quaternion.AngleAxis(90, root.right);
            var bodies = root.GetComponentsInChildren<Rigidbody>();
            var stage = new RaidWorldRebase();
            try
            {
                observer.position = shiftedPosition;
                stage.Apply(root, observer, bodies, originalPosition, turn);
                Physics.Simulate(Step);
                Expect(Quaternion.Angle(cabinet.Body.rotation, turn * open) < 4f &&
                    Vector3.Distance(cabinet.Body.position, hinge.connectedBody.transform.TransformPoint(hinge.connectedAnchor)) < .005f,
                    "Rebase reset the open hinge or displaced its anchor");
                Pull(cabinet.Body, handle, shiftedPosition + turn * (closedHandle - originalPosition));
                Expect(Quaternion.Angle(cabinet.Body.rotation, turn * closed) < 4f,
                    "The rebased hinge cannot be pushed closed");
            }
            finally
            {
                observer.position = originalPosition;
                stage.Apply(root, observer, bodies, shiftedPosition, Quaternion.Inverse(turn));
                Object.DestroyImmediate(observer.gameObject);
            }
            Expect(FirstHit(cast).rigidbody == cabinet.Body, "Closed cabinet no longer covers the shelf opening");

            var drawerCast = new Ray(storage.TransformPoint(new Vector3(5.45f, .31f, -4.72f)), -storage.forward);
            Vector3 closedDrawer = drawer.Body.position;
            Quaternion drawerRotation = drawer.Body.rotation;
            var tray = drawer.Parts[2];
            Vector3 closedTray = tray.transform.position;
            Vector3 drawerPull = drawer.Body.transform.InverseTransformPoint(drawer.Parts[1].bounds.center);
            Vector3 drawerHandle = drawer.Body.position + drawerRotation * drawerPull;
            Vector3 direction = (rail.connectedBody.transform.TransformPoint(rail.connectedAnchor) - closedDrawer).normalized;
            Expect(FirstHit(drawerCast).rigidbody == drawer.Body, "Specimen drawer's front must be the first physical target");
            Pull(drawer.Body, drawerPull, drawerHandle + direction * .44f);
            float extension = Vector3.Dot(drawer.Body.position - closedDrawer, direction);
            Expect(Mathf.Abs(extension - .34f) < .015f && Vector3.Distance(tray.transform.position, closedTray + direction * .34f) < .015f &&
                Quaternion.Angle(drawer.Body.rotation, drawerRotation) < 1f && tray.enabled,
                $"Drawer did not slide its actual tray to the stop without spinning ({extension:F3} m)");
            for (int i = 0; i < 20; i++) Physics.Simulate(Step);
            Expect(Vector3.Distance(tray.transform.position, closedTray + direction * .34f) < .015f,
                "Drawer moved away from the open stop after release");
            Pull(drawer.Body, drawerPull, drawerHandle - direction * .08f);
            Expect(Vector3.Distance(tray.transform.position, closedTray) < .015f, "Drawer could not be pushed shut");
            Debug.Log("CONTOUR4 CABINET MOTION first hit and obstruction, hinged shelf access after pull, rebase/reclose, and 0.34 m drawer travel with release passed");

            CheckPhysicalFronts(raid);
            PutFrontsAtOpenStops(raid);
            Physics.SyncTransforms();
            int checkedPoses = 0, coverHits = 0;
            foreach (var room in raid.Rooms)
            {
                foreach (var route in room.Plan.Module.Routes.Where(route => route.Cargo == Cargo.Cart))
                foreach (var pose in route.Samples(.14f))
                {
                    Vector3 centerOfCart = room.Root.TransformPoint(RaidPlan.Vector(pose.Position + pose.Up * (.04f + route.Envelope.Y / 2)));
                    var rotation = room.Root.rotation * Quaternion.LookRotation(RaidPlan.Vector(pose.Right), RaidPlan.Vector(pose.Up));
                    var hits = Physics.OverlapBox(centerOfCart, RaidPlan.Vector(route.Envelope) * .495f, rotation, 1, QueryTriggerInteraction.Ignore);
                    Expect(!hits.Any(hit => hit.transform.parent && hit.transform.parent.name.EndsWith(" pivot", StringComparison.Ordinal)),
                        $"Open furniture obstructs {room.Plan.Module.Id}/{route.Id} at {pose.Position}: " +
                        string.Join(",", hits.Select(hit => hit.name)));
                    checkedPoses++;
                }
                foreach (var dressing in room.Plan.Module.Dressing)
                foreach (var cover in dressing.Covers.Where(cover => cover.Collider.EndsWith("-body", StringComparison.Ordinal)))
                {
                    Vector3 lift = RaidPlan.Vector(dressing.Frame.Up) * .78f;
                    Vector3 start = room.Root.TransformPoint(RaidPlan.Vector(dressing.Point(cover.From)) + lift);
                    Vector3 end = room.Root.TransformPoint(RaidPlan.Vector(dressing.Point(cover.Behind)) + lift);
                    Expect(Physics.Linecast(start, end, out var hit, 1, QueryTriggerInteraction.Ignore) &&
                        hit.collider.name == dressing.Name(cover.Collider), $"Cabinet case lost original cover {dressing.Name(cover.Collider)}");
                    coverHits++;
                }
            }
            Expect(runtime.Cabinets.Count > 10 && checkedPoses > 100 && coverHits > 3, "Missing intended production cabinet cases");
            Debug.Log($"CONTOUR4 CABINETS {runtime.Cabinets.Count} active doors/drawers, {checkedPoses} open cargo poses and {coverHits} cover rays passed");
        }

        private static RaycastHit FirstHit(Ray ray)
        {
            Physics.Raycast(ray, out var hit, 3f, 1, QueryTriggerInteraction.Ignore);
            return hit;
        }

        private static void Pull(Rigidbody body, Vector3 grip, Vector3 target)
        {
            for (int step = 0; step < 90; step++)
            {
                Vector3 point = body.position + body.rotation * grip;
                body.AddForceAtPosition(Vector3.ClampMagnitude((target - point) * 350f, 90f), point);
                Physics.Simulate(Step);
            }
            Physics.SyncTransforms();
        }

        private static void VerifyShelfAccess(Transform room, Ray cast, BoxCollider shelf, Vector3 shelfSeat)
        {
            var oldGravity = Physics.gravity;
            var cargo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                cargo.transform.SetPositionAndRotation(room.TransformPoint(shelfSeat + Vector3.up * .22f), room.rotation);
                cargo.transform.localScale = Vector3.one * .20f;
                cargo.AddComponent<Rigidbody>();
                Physics.gravity = -room.up * 9.81f;
                Physics.SyncTransforms();
                for (int i = 0; i < 45; i++) Physics.Simulate(Step);
                float height = room.InverseTransformPoint(cargo.GetComponent<Rigidbody>().position).y - shelfSeat.y;
                Physics.SyncTransforms();
                Expect(FirstHit(cast).collider == cargo.GetComponent<Collider>() && Mathf.Abs(height - .10f) < .025f && !shelf.attachedRigidbody,
                    "Open door must reveal a reachable item supported by the static shelf");
            }
            finally
            {
                Object.DestroyImmediate(cargo);
                Physics.gravity = oldGravity;
            }
        }

        private static void PutFrontsAtOpenStops(RaidAssembly raid)
        {
            foreach (var room in raid.Rooms)
            foreach (var dressing in room.Plan.Module.Dressing)
            foreach (var moving in dressing.MovingFronts)
            {
                var pivot = room.Root.Find(dressing.Name(moving.Panel) + " pivot");
                var body = pivot.GetComponent<Rigidbody>();
                Vector3 home = RaidPlan.Vector(dressing.Point(moving.Pivot));
                Quaternion local = Quaternion.LookRotation(RaidPlan.Vector(dressing.Direction(System.Numerics.Vector3.UnitZ)),
                    RaidPlan.Vector(dressing.Frame.Up));
                body.position = room.Root.TransformPoint(home + local * (Vector3.forward * moving.Travel));
                body.rotation = room.Root.rotation * local * Quaternion.AngleAxis(moving.Angle, Vector3.up);
                pivot.SetPositionAndRotation(body.position, body.rotation);
            }
        }

        private static void CheckPhysicalFronts(RaidAssembly raid)
        {
            int doors = 0, drawers = 0;
            foreach (var room in raid.Rooms)
            foreach (var dressing in room.Plan.Module.Dressing)
            foreach (var moving in dressing.MovingFronts)
            {
                var body = room.Root.Find(dressing.Name(moving.Panel) + " pivot").GetComponent<Rigidbody>();
                var handle = body.transform.Find(dressing.Name(moving.Handle));
                Vector3 grip = body.transform.InverseTransformPoint(handle.GetComponent<Collider>().bounds.center);
                Vector3 home = room.Root.TransformPoint(RaidPlan.Vector(dressing.Point(moving.Pivot)));
                Quaternion closed = room.Root.rotation * Quaternion.LookRotation(
                    RaidPlan.Vector(dressing.Direction(System.Numerics.Vector3.UnitZ)), RaidPlan.Vector(dressing.Frame.Up));
                Quaternion open = closed * Quaternion.AngleAxis(moving.Angle, Vector3.up);
                Vector3 slide = closed * (Vector3.forward * moving.Travel);
                Pull(body, grip, home + slide * 1.25f + open * grip);
                Expect(Vector3.Distance(body.position, home + slide) < .025f && Quaternion.Angle(body.rotation, open) < 8f,
                    $"{room.Plan.Module.Id}/{body.name} cannot reach its opening (angle {Quaternion.Angle(body.rotation, closed):F1}°, " +
                    $"slide {Vector3.Dot(body.position - home, slide.normalized):F3} m)");
                Pull(body, grip, home - slide * .25f + closed * grip);
                Expect(Vector3.Distance(body.position, home) < .025f && Quaternion.Angle(body.rotation, closed) < 8f,
                    $"{room.Plan.Module.Id}/{body.name} cannot close after opening");
                if (moving.Travel == 0) doors++; else drawers++;
            }
            Debug.Log($"CONTOUR4 CABINET RIGS {doors} hinges and {drawers} slides pulled open and shut in PhysX");
        }

        private static void CheckShutter(RaidRoomInteractions runtime, BakedRoom trench)
        {
            var root = trench.Root;
            var gate = root.Find(MaintenanceTrench.Gate).GetComponent<BoxCollider>();
            var indicators = root.GetComponentsInChildren<Renderer>().Where(renderer => renderer.name == "Shutter status").ToArray();
            var shutter = runtime.Shutters.Single();
            Vector3 approach = root.TransformPoint(new Vector3(12.07f, 0, MaintenanceTrench.BridgeZ));
            Vector3 clear = root.TransformPoint(new Vector3(5, 0, 0));
            Vector3 under = root.TransformPoint(new Vector3(MaintenanceTrench.GateX, 0, MaintenanceTrench.BridgeZ));
            var crossing = new Ray(root.TransformPoint(new Vector3(11.8f, 1, MaintenanceTrench.BridgeZ)), root.right);
            Expect(!gate.Raycast(crossing, out _, 3) && indicators.Length == 2, "Shutter must start open with two approach indicators");
            runtime.Advance(approach, 1.9f, Step, 0);
            Expect(shutter.State == InspectionShutter.Stage.Warning &&
                indicators.All(renderer => renderer.sharedMaterial.color.r > .9f && renderer.sharedMaterial.color.g > .55f),
                "Shutter failed to warn visibly on both approaches");
            for (int i = 0; i < 60; i++)
                Expect(!runtime.Advance(clear, 1.9f, Step, 0), "Shutter hurt player before warning ended");
            Expect(shutter.State == InspectionShutter.Stage.Warning, "Shutter shortened its warning below 1.2 seconds");
            for (int i = 0; i < 65; i++) runtime.Advance(clear, 1.9f, Step, 0);
            Physics.SyncTransforms();
            Expect(shutter.State == InspectionShutter.Stage.Closed && gate.Raycast(crossing, out _, 3) &&
                !gate.Raycast(new Ray(root.TransformPoint(new Vector3(11.8f, 1, 0)), root.right), out _, 3),
                "Closed shutter failed to block the short route or blocked freight bypass");
            for (int i = 0; i < 155; i++) runtime.Advance(clear, 1.9f, Step, 0);
            Physics.SyncTransforms();
            Expect(shutter.State == InspectionShutter.Stage.Armed && !gate.Raycast(crossing, out _, 3), "Shutter failed to reopen and re-arm");
            int strikes = 0;
            for (int i = 0; i < 185; i++) if (runtime.Advance(under, 1.9f, Step, 0)) strikes++;
            Expect(strikes == 1 && shutter.State == InspectionShutter.Stage.Rearming, "Shutter did not strike once and reopen for an occupied passage");
            Debug.Log("CONTOUR4 SHUTTER warned on both sides, blocked only shortcut, recovered automatically, single contact on occupied passage");
        }

        private static void CheckTrench(RaidRoomInteractions runtime, BakedRoom trench)
        {
            var root = trench.Root;
            foreach (bool sideways in new[] { false, true })
            {
                if (sideways) root.SetPositionAndRotation(root.position + new Vector3(0, 60, 0),
                    root.rotation * Quaternion.AngleAxis(90, Vector3.right));
                Physics.gravity = -root.up * 9.81f;
                Physics.SyncTransforms();
                VerifyCrossing(MaintenanceTrench.BridgeZ, new Vector3(.48f, .7f, .72f));
                VerifyCrossing(0, new Vector3(2.6f, 2.05f, 1.8f));
                VerifyFall(-6.1f);
                VerifyFall(-3.45f);
                Debug.Log($"CONTOUR4 TRENCH PhysX sideways={sideways}: flush plank with hand cargo, broad bypass with loaded cart envelope, both actual falls reached death volume");
            }

            void VerifyCrossing(float lane, Vector3 size)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                try
                {
                    marker.transform.SetPositionAndRotation(root.TransformPoint(new Vector3(3.7f, size.y / 2 + .02f, lane)), root.rotation);
                    marker.transform.localScale = size;
                    var body = marker.AddComponent<Rigidbody>();
                    body.constraints = RigidbodyConstraints.FreezeRotation;
                    float goal = MaintenanceTrench.PitEnd + size.x / 2 + .12f;
                    Vector3 local = default;
                    for (int i = 0; i < 177; i++)
                    {
                        body.linearVelocity = Vector3.Project(body.linearVelocity, root.up) + root.right * 3;
                        Physics.Simulate(Step);
                        local = root.InverseTransformPoint(body.position);
                        Expect(local.y >= size.y / 2 - .08f && !runtime.CatchesFall(body.position, body.position),
                            $"Cargo fell off its supported route z={lane} at {local}");
                        if (local.x >= goal) break;
                    }
                    Expect(local.x >= goal, $"Cargo failed to cross trench span z={lane}: {local.x:F2}m reached, {goal:F2}m needed");
                }
                finally { Object.DestroyImmediate(marker); }
            }

            void VerifyFall(float lane)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                try
                {
                    marker.transform.SetPositionAndRotation(root.TransformPoint(new Vector3(8, 1.02f, lane)), root.rotation);
                    marker.transform.localScale = new Vector3(.52f, .85f, .52f);
                    var body = marker.AddComponent<Rigidbody>();
                    bool caught = false;
                    for (int i = 0; i < 75 && !caught; i++)
                    {
                        Vector3 previous = body.position;
                        Physics.Simulate(Step);
                        caught = runtime.CatchesFall(previous, body.position);
                    }
                    float depth = root.InverseTransformPoint(body.position).y;
                    Expect(caught && depth < -4.1f && depth > -7, $"Actual unsupported fall missed the death zone z={lane} depth={depth:F2}");
                    Expect(runtime.TryShaftUp(body.position, out Vector3 shaftUp) && Vector3.Dot(shaftUp, root.up) > .999f,
                        "Fall direction lost the room's local gravity frame");
                }
                finally { Object.DestroyImmediate(marker); }
            }
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}

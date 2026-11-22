using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Contour4.Procedural.Navigation.Editor
{
    public static class NavigationChecks
    {
        private const string Log = "CONTOUR4 NAVIGATION";
        private readonly static Vector3 EnemyStart = new(-.7f, 0, 0);
        private readonly static Vector3 PlayerStart = new(-.7f, 0, 1.2f);
        private readonly static Vector3 PortalPad = new(0, 0, 4.22f);
        private readonly static Vector3 AcrossSeam = new(-2.5f, 0, -2);
        private const float EyeHeight = 1.27f;
        private const float Step = .02f;

        public static void Run()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Run the navigation fixture in its dedicated batch project.");
            Debug.Log($"{Log} Unity {Application.unityVersion}, real NavMeshBuilder and PhysX in a placed offline fixture.");
            CheckRoutesAndVision();
            CheckUnavailableLayouts();
            CheckPursuitAndTeardown();
            CheckBlockedExitAndAuthority();
            CheckPursuitRegressions();
            Debug.Log($"{Log} PASSED");
        }

        private static RaidEnemyProfile Profile(float range = 20, float radius = .34f, float height = 1.55f,
            bool canWalk = true, bool canTurn = true) =>
            new(radius, height, EyeHeight, 2.6f, range, 110, 4, canWalk, canTurn);

        private static void CheckRoutesAndVision()
        {
            using var fixture = new Fixture();
            var raid = fixture.Raid;
            var profile = Profile();
            Expect(raid.BakedFloorCount == 3, "Three separately placed navigation floors installed");
            var triangles = TriangleCount();
            Expect(triangles > 3, "Runtime created real NavMesh triangulation");
            float floorAngle = Vector3.Angle(fixture.A.Up, fixture.B.Up);
            Expect(Mathf.Abs(floorAngle - 90) < .01f, "Portal fixture has 90 degree floor orientation");
            Expect(fixture.Route(fixture.A, EnemyStart, fixture.B, PlayerStart, profile, RaidRouteKind.Portal),
                "Directed route uses portal");
            Expect(fixture.Route(fixture.A, EnemyStart, fixture.C, AcrossSeam, profile, RaidRouteKind.PhysicalPassage),
                "Physical same-gravity seam joins two complete local routes");
            Expect(fixture.Route(fixture.C, AcrossSeam, fixture.B, PlayerStart, profile, RaidRouteKind.PhysicalPassage),
                "Combined path uses a physical seam before a portal");
            Expect(!fixture.Route(fixture.B, PlayerStart, fixture.A, EnemyStart, profile),
                "Prohibited reverse has no path");
            Expect(!fixture.Route(fixture.A, EnemyStart, fixture.B, PlayerStart, Profile(canWalk: false)),
                "Incompatible enemy cannot walk through portal");
            Expect(!fixture.Route(fixture.A, EnemyStart, fixture.B, PlayerStart, Profile(canTurn: false)),
                "Enemy with fixed up cannot land on a perpendicular floor");
            Debug.Log($"{Log} AC-2111 nav floor count=3, triangles={triangles}, floor angle={floorAngle:F1}, " +
                $"supports A={fixture.A.Frame.position}/{fixture.A.Up}, B={fixture.B.Frame.position}/{fixture.B.Up}, " +
                $"C={fixture.C.Frame.position}/{fixture.C.Up}; physical seam, one-way portal, combined route.");

            var aEye = EnemyStart + Vector3.up * EyeHeight;
            var virtualTarget = new Vector3(-PlayerStart.x, EyeHeight, 10 - PlayerStart.z);
            var delta = virtualTarget - aEye;
            var toward = delta.normalized;
            Expect(Mathf.Abs(toward.x) > .1f, "Portal optics tested off the center axis");
            Expect(fixture.Sight(fixture.A, aEye, toward, profile) == RaidSightKind.Portal,
                "Angled visibility through rotated portal reaches actual player point");
            Expect(fixture.Sight(fixture.A, aEye, toward, Profile(range: delta.magnitude + .005f)) == RaidSightKind.Portal,
                "Just inside sight distance");
            Expect(fixture.Sight(fixture.A, aEye, toward, Profile(range: delta.magnitude - .005f)) == RaidSightKind.None,
                "Just beyond sight distance");
            Vector3 facingInside = Quaternion.AngleAxis(54.9f, Vector3.up) * toward;
            Vector3 facingOutside = Quaternion.AngleAxis(55.1f, Vector3.up) * toward;
            Expect(fixture.Sight(fixture.A, aEye, facingInside, profile) == RaidSightKind.Portal,
                "Just inside the field of view");
            Expect(fixture.Sight(fixture.A, aEye, facingOutside, profile) == RaidSightKind.None,
                "Beyond the field of view");
            Expect(fixture.Sight(fixture.A, new Vector3(0, EyeHeight, 5.25f), Vector3.forward, profile) ==
                RaidSightKind.None, "Back of entrance is not visible");
            fixture.PlacePlayer(fixture.B, new Vector3(0, 0, 5.3f));
            Expect(fixture.Sight(fixture.A, aEye, toward, profile) == RaidSightKind.None, "Back of exit is not visible");
            fixture.PlacePlayer(fixture.B, new Vector3(-3, 0, 2));
            var outsideEye = new Vector3(3, EyeHeight, 0);
            var outsideEntry = new Vector3(3, EyeHeight, 5);
            var outsideExit = fixture.B.World(new Vector3(-3, EyeHeight, 5));
            fixture.SetDoorFramesEnabled(false);
            Expect(fixture.IsClearRay(outsideEye, outsideEntry) && fixture.IsClearRay(outsideExit, fixture.Player.SightPoint) &&
                fixture.Sight(fixture.A, outsideEye, Vector3.forward, profile) == RaidSightKind.None,
                "Unobstructed direction outside aperture is independently rejected");
            fixture.SetDoorFramesEnabled(true);
            fixture.PlacePlayer(fixture.B, PlayerStart);

            var hit = aEye + delta * (5 / delta.z);
            var entryOccluder = fixture.Block(fixture.A, Vector3.Lerp(aEye, hit, .65f), new Vector3(.3f, .4f, .22f));
            Expect(fixture.Sight(fixture.A, aEye, toward, profile) == RaidSightKind.None, "Independent entry occlusion");
            fixture.Remove(entryOccluder);
            var bHit = new Vector3(-hit.x, hit.y, hit.z);
            var exitOccluder = fixture.Block(fixture.B, Vector3.Lerp(bHit, PlayerStart + Vector3.up * EyeHeight, .5f),
                new Vector3(.3f, .4f, .24f));
            Expect(fixture.Sight(fixture.A, aEye, toward, profile) == RaidSightKind.None, "Independent exit occlusion");
            fixture.Remove(exitOccluder);
            Expect(fixture.Sight(fixture.A, aEye, toward, profile) == RaidSightKind.Portal, "Removing blockers restores sight");

            fixture.PlacePlayer(fixture.A, new Vector3(-1, 0, 1.1f));
            Expect(fixture.Sight(fixture.B, PlayerStart + Vector3.up * EyeHeight, Vector3.forward, profile) ==
                RaidSightKind.None, "Reverse sight obeys its independently configured direction");
            Expect(fixture.Sight(fixture.A, aEye, Vector3.forward, profile) == RaidSightKind.Direct, "Ordinary direct sight");
            fixture.PlacePlayer(fixture.C, AcrossSeam);
            Expect(fixture.Sight(fixture.A, new Vector3(3, EyeHeight, -2), Vector3.right, profile) ==
                RaidSightKind.Direct, "Ordinary sight through a physical doorway");
            var walker = fixture.Spawn(fixture.A, new Vector3(2f, 0, -2), Vector3.right, profile);
            var movement = fixture.TickUntil(walker, () => walker.Floor == fixture.C &&
                Vector3.Distance(walker.transform.position, fixture.Player.Feet) < .25f, 200);
            Expect(movement >= 0 && walker.PortalCrossings == 0 && walker.Target == fixture.Player,
                "Chaser follows the physical seam continuously");
            Debug.Log($"{Log} AC-2121 angled sight {Vector3.Angle(Vector3.forward, toward):F2}°, " +
                $"virtual path {delta.magnitude:F3}m, half-FOV=55°, plane hit {hit}; separate blockers, wrong side, " +
                $"range, aperture and direct sight; physical chase finished after {movement * Step:F2}s.");
        }

        private static void CheckUnavailableLayouts()
        {
            var profile = Profile();
            using (var fixture = new Fixture(partition: true))
            {
                Expect(fixture.Route(fixture.A, new Vector3(0, 0, 3.6f), fixture.B, PlayerStart, profile,
                    RaidRouteKind.Portal), "The partitioned portal is reachable from its own side");
                Expect(!fixture.Route(fixture.A, EnemyStart, fixture.A, PortalPad, profile),
                    "Placed partition splits the local NavMesh into disconnected islands");
                Expect(!fixture.Route(fixture.A, EnemyStart, fixture.B, PlayerStart, profile),
                    "Physically unreachable portal approach is rejected");
            }
            using (var fixture = new Fixture(landing: new Vector3(0, 0, 6)))
                Expect(!fixture.Route(fixture.A, EnemyStart, fixture.B, PlayerStart, profile),
                    "Missing landing is rejected");
            var setbackLanding = new Vector3(0, 0, 3.85f);
            using (var fixture = new Fixture(landing: setbackLanding, exitFloorInset: .55f))
            {
                Expect(fixture.Route(fixture.B, PlayerStart, fixture.B, setbackLanding, profile, RaidRouteKind.Local),
                    "Landing pad still has a complete route behind the short floor");
                Expect(!fixture.Route(fixture.A, EnemyStart, fixture.B, PlayerStart, profile),
                    "A gap from the aperture to a good landing pad rejects this walking portal");
            }
            using (var fixture = new Fixture(size: new Vector2(.7f, 2.65f)))
                Expect(!fixture.Route(fixture.A, EnemyStart, fixture.B, PlayerStart, profile),
                    "Enemy wider than aperture with edge clearance is rejected");
            using (var fixture = new Fixture(size: new Vector2(2.2f, 1.50f)))
                Expect(!fixture.Route(fixture.A, EnemyStart, fixture.B, PlayerStart, profile),
                    "Enemy taller than aperture is rejected");
            using (var fixture = new Fixture(kind: RaidPortalKind.Upper))
            {
                Vector3 eye = Vector3.up * EyeHeight;
                fixture.PlacePlayer(fixture.B, Vector3.zero);
                Expect(fixture.Sight(fixture.A, eye, Vector3.up, profile) == RaidSightKind.Portal,
                    "Upper portal can be configured for sight");
                Expect(!fixture.Route(fixture.A, EnemyStart, fixture.B, Vector3.zero, profile),
                    "A vision-only upper portal offers no walking route");
                ExpectThrows<ArgumentException>(() => new RaidPortal(fixture.Entrance, fixture.Exit,
                    RaidPortalUse.Walking, RaidPortalUse.None), "Upper walking configuration is refused");
            }
            Debug.Log($"{Log} AC-2111 rejects disconnected approach, missing exit, gap under exit, oversized bodies and upper walking.");
        }

        private static void CheckPursuitAndTeardown()
        {
            int before = TriangleCount();
            using (var fixture = new Fixture(allowReturn: true))
            {
                var profile = Profile();
                Expect(fixture.Route(fixture.B, PlayerStart, fixture.A, EnemyStart, profile, RaidRouteKind.Portal),
                    "The return edge is viable in the two-way pursuit fixture");
                fixture.PlacePlayer(fixture.A, new Vector3(.3f, 0, 3.8f));
                var enemy = fixture.Spawn(fixture.A, EnemyStart, Vector3.forward, profile);
                enemy.Advance(Step);
                Expect(enemy.Target == fixture.Player && enemy.LastSight == RaidSightKind.Direct,
                    "Enemy originally acquires this same moving player");

                int playerId = fixture.Player.GetInstanceID();
                fixture.PlacePlayer(fixture.B, PlayerStart);
                int seen = fixture.TickUntil(enemy, () => enemy.LastSight == RaidSightKind.Portal, 25);
                Expect(seen >= 0 && enemy.Target == fixture.Player && enemy.Floor == fixture.A,
                    "Player crosses first and the enemy observes the same identity through the linked portal");

                var initialEnemy = enemy.transform.position;
                float detourX = 0;
                int crossed = fixture.TickUntil(enemy, () =>
                {
                    var position = enemy.transform.position;
                    if (enemy.Floor == fixture.A && position.z >= 1.8f && position.z <= 2.65f)
                        detourX = Mathf.Max(detourX, Mathf.Abs(position.x + .38f));
                    return enemy.PortalCrossings == 1;
                }, 240);
                Expect(crossed >= 0 && detourX >= .96f && enemy.Floor == fixture.B && enemy.Target.GetInstanceID() == playerId,
                    $"Enemy went around a real obstacle, reached the entry and crossed safely; pos={enemy.transform.position}");
                float upError = Vector3.Angle(enemy.transform.up, fixture.B.Up);
                Expect(upError <= .1f && Vector3.Dot(enemy.transform.forward, -(fixture.B.Frame.rotation * Vector3.forward)) > 0,
                    "Portal preserves tangential heading and reorients local up");
                Expect(Vector3.Distance(enemy.transform.position, fixture.B.World(PortalPad)) < .18f,
                    "Landing on exit-side NavMesh");

                var landing = enemy.transform.position;
                fixture.PlacePlayer(fixture.B, new Vector3(.75f, 0, -2.9f));
                float initialGap = Vector3.Distance(landing, fixture.Player.Feet);
                int caught = fixture.TickUntil(enemy, () => Vector3.Distance(enemy.transform.position, fixture.Player.Feet) < .22f, 250);
                float finalGap = Vector3.Distance(enemy.transform.position, fixture.Player.Feet);
                Expect(caught >= 0 && enemy.Target.GetInstanceID() == playerId && enemy.PortalCrossings == 1,
                    "After crossing the enemy recalculates and approaches the same moved player once");
                fixture.TickUntil(enemy, () => false, 70);
                Expect(enemy.PortalCrossings == 1, "Stable target does not cause return trips even with a viable reverse edge");

                UnityEngine.Object.DestroyImmediate(enemy.gameObject);
                Expect(fixture.Raid.Ready, "Destroying the enemy leaves the raid available for other enemies");
                var second = fixture.Spawn(fixture.B, PlayerStart, -Vector3.forward, profile);
                second.Advance(Step);
                Expect(second.Target == fixture.Player, "Replacement sees one live target");
                fixture.Raid.Close();
                Expect(!fixture.Raid.Ready && fixture.Raid.BakedFloorCount == 0 && !fixture.Player.Owner && !second.Target &&
                    second.Floor == null, "Closing the raid releases meshes, player and enemy ownership");
                Expect(TriangleCount() == before, "Closing removes its NavMesh registrations");
                fixture.Reload();
                Expect(fixture.Raid.Ready && TriangleCount() > before, "Same root supports fresh layout after Close");
                ExpectThrows<InvalidOperationException>(fixture.Reload, "Reinstall cannot duplicate registrations");
                Debug.Log($"{Log} AC-2131 enemy {initialEnemy} -> {landing}, player acquired through portal after " +
                    $"{seen * Step:F2}s; crossed after {crossed * Step:F2}s; obstacle detour={detourX:F3}m; up error={upError:F3}°, " +
                    $"same player {playerId}, chased after relocation {initialGap:F3}m -> {finalGap:F3}m in {caught * Step:F2}s.");
            }
            Expect(TriangleCount() == before, "Actual scene teardown removes the reinstalled NavMesh");
        }

        private static void CheckBlockedExitAndAuthority()
        {
            using var fixture = new Fixture();
            RaidEnemyProfile profile = Profile();
            bool host = true;
            bool acceptCrossing = false;
            int rejectedWrites = 0;
            var enemy = fixture.Spawn(fixture.A, EnemyStart, Vector3.forward, profile, () => host,
                (transform, pose) =>
                {
                    if (pose.position.x > 10 && !acceptCrossing)
                    {
                        rejectedWrites++;
                        return false;
                    }
                    transform.SetPositionAndRotation(pose.position, pose.rotation);
                    return true;
                });
            int nearExit = fixture.TickUntil(enemy, () => enemy.transform.position.z > 3.95f, 140);
            Expect(nearExit >= 0 && enemy.PortalCrossings == 0, "Enemy is committed to the saved route before obstruction");
            var blocked = fixture.Block(fixture.B, PortalPad + Vector3.up * .38f, new Vector3(.8f, .74f, .65f));
            fixture.TickUntil(enemy, () => false, 50);
            Expect(enemy.PortalCrossings == 0 && enemy.Floor == fixture.A && rejectedWrites == 0 &&
                enemy.Target == fixture.Player, "Late exit obstruction prevents even an attempted pose application");
            Expect(!fixture.Route(fixture.A, EnemyStart, fixture.B, PlayerStart, profile), "Blocked exit removed as viable edge");
            fixture.Remove(blocked);
            Expect(fixture.TickUntil(enemy, () => rejectedWrites > 0, 70) >= 0 && enemy.Floor == fixture.A &&
                enemy.PortalCrossings == 0, "A declined authoritative pose leaves the source state intact");

            host = false;
            var before = enemy.transform.position;
            fixture.TickUntil(enemy, () => false, 18);
            Expect(enemy.transform.position == before && !enemy.Target && enemy.PortalCrossings == 0,
                "Authority loss cancels chase and pose changes");
            host = true;
            acceptCrossing = true;
            Expect(fixture.TickUntil(enemy, () => enemy.PortalCrossings == 1, 150) >= 0 && enemy.Target == fixture.Player,
                "Cleared landing and restored authority let the same chaser recover");
            UnityEngine.Object.DestroyImmediate(fixture.Player.gameObject);
            var afterDestruction = enemy.transform.position;
            enemy.Advance(Step);
            Expect(ReferenceEquals(enemy.Target, null) && enemy.transform.position == afterDestruction,
                "Destroying the target clears the identity before any further step");
            Debug.Log($"{Log} AC-2131 exit obstructed at z>3.95, unsafe pose suppressed, failed writer retained " +
                $"floor (declined writes={rejectedWrites}), authority loss cancelled, recovery crossed once, cleanup verified.");
        }

        private static void CheckPursuitRegressions()
        {
            bool reachedWallGoal;
            using (var fixture = new Fixture(bakeRadius: .65f))
            {
                var profile = Profile();
                var start = new Vector3(0, 0, 3.65f);
                var wallFeet = new Vector3(4.75f, 0, -2);
                fixture.PlacePlayer(fixture.B, wallFeet);
                var enemy = fixture.Spawn(fixture.A, start, Vector3.forward, profile);
                enemy.Advance(Step);
                Expect(enemy.LastSight == RaidSightKind.Portal && enemy.Target == fixture.Player,
                    "Wall-hugging player is visible through portal");
                Expect(!fixture.Route(fixture.A, start, fixture.B, wallFeet, profile),
                    "Strict monster route still excludes the player's feet in the wall margin");
                float Gap() => Vector3.Distance(enemy.transform.position, fixture.Player.Feet);
                int reached = fixture.TickUntil(enemy, () => enemy.Floor == fixture.B && Gap() <= .9f, 240);
                fixture.TickUntil(enemy, () => false, 45);
                float gap = Gap();
                reachedWallGoal = reached >= 0 && gap > .16f && gap <= .9f && enemy.Target == fixture.Player &&
                    enemy.PortalCrossings == 1;
                Debug.Log($"{Log} AC-2131 wall-margin pursuit pass={reachedWallGoal}, crossed={enemy.PortalCrossings}, " +
                    $"remaining distance={gap:F3}m, reachable stopping position reached at {reached * Step:F2}s.");
            }

            bool followedMemory;
            using (var fixture = new Fixture())
            {
                var profile = Profile();
                var lastVisibleFeet = new Vector3(.3f, 0, 3.8f);
                fixture.PlacePlayer(fixture.A, lastVisibleFeet);
                var enemy = fixture.Spawn(fixture.A, EnemyStart, Vector3.forward, profile);
                enemy.Advance(Step);
                Expect(enemy.LastSight == RaidSightKind.Direct && enemy.Target == fixture.Player,
                    "Player is visible before leaving the floor");
                fixture.PlacePlayer(fixture.B, new Vector3(3, 0, 4));
                var initialGap = Vector3.Distance(enemy.transform.position, lastVisibleFeet);
                int reached = fixture.TickUntil(enemy,
                    () => Vector3.Distance(enemy.transform.position, lastVisibleFeet) < .24f, 160);
                var gap = Vector3.Distance(enemy.transform.position, lastVisibleFeet);
                bool remembered = reached >= 0 && enemy.Target == fixture.Player && enemy.LastSight == RaidSightKind.None &&
                    enemy.Floor == fixture.A && enemy.PortalCrossings == 0;
                fixture.TickUntil(enemy, () => false, 245);
                followedMemory = remembered && ReferenceEquals(enemy.Target, null) && enemy.PortalCrossings == 0;
                Debug.Log($"{Log} AC-2131 hidden floor change pass={followedMemory}, last observed position " +
                    $"{initialGap:F3}m -> {gap:F3}m in {reached * Step:F2}s, memory expired={ReferenceEquals(enemy.Target, null)}.");
            }
            Expect(reachedWallGoal && followedMemory, "Wall-margin destination and hidden floor memory pursuit regressions");
        }

        private static int TriangleCount() => NavMesh.CalculateTriangulation().indices.Length / 3;

        private static void Expect(bool passed, string assertion)
        {
            if (!passed)
                throw new InvalidOperationException($"{Log} FAIL {assertion}");
        }

        private static void ExpectThrows<T>(Action action, string assertion) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            Expect(false, assertion);
        }

        // Hand-placed rooms and authored cutouts isolated from the game. All surfaces and occluders are real colliders.
        private sealed class Fixture : IDisposable
        {
            private readonly Scene scene;
            private readonly GameObject root;
            private readonly RaidLayout layout;
            private readonly List<Collider> doorFrames = new();
            private const int SolidLayer = 8;

            internal RaidFloor A { get; }
            internal RaidFloor B { get; }
            internal RaidFloor C { get; }
            internal RaidNavigation Raid { get; }
            internal RaidTarget Player { get; }
            internal RaidAperture Entrance { get; }
            internal RaidAperture Exit { get; }

            internal Fixture(bool partition = false, Vector2? size = null, Vector3? landing = null,
                RaidPortalKind kind = RaidPortalKind.Door, float exitFloorInset = 0, bool allowReturn = false,
                float bakeRadius = .42f)
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                root = new GameObject("Offline Contour-4 raid");
                Raid = root.AddComponent<RaidNavigation>();
                A = Room("flat", new Pose(Vector3.zero, Quaternion.identity), size, partition, seam: 1, baffles: true);
                B = Room("wall", new Pose(new Vector3(14, 8, 12), Quaternion.Euler(0, 0, -90)), size,
                    floorInset: exitFloorInset);
                C = Room("side", new Pose(new Vector3(10, 0, 0), Quaternion.identity), size, seam: -1);
                Entrance = Aperture(A, kind, size, PortalPad);
                Exit = Aperture(B, kind, size, landing ?? PortalPad);
                var allowed = kind == RaidPortalKind.Door ? RaidPortalUse.Vision | RaidPortalUse.Walking : RaidPortalUse.Vision;
                var link = new RaidPortal(Entrance, Exit, allowed, allowReturn ? allowed : RaidPortalUse.None);
                var source = new Vector3(4.25f, 0, -2);
                var destination = C.World(new Vector3(-4.25f, 0, -2));
                layout = new RaidLayout(new[] { A, B, C }, new[] { link },
                    new[] { new RaidWalkPassage(A, C, source, destination), new RaidWalkPassage(C, A, destination, source) },
                    1 << SolidLayer, new Vector3(31, -120, 0), bakeRadius, 1.8f);
                Raid.Install(layout);
                var avatar = new GameObject("Player feet and tracked identity");
                avatar.transform.SetParent(root.transform);
                var eye = new GameObject("Player view point");
                eye.transform.SetParent(avatar.transform, false);
                eye.transform.localPosition = Vector3.up * EyeHeight;
                Player = avatar.AddComponent<RaidTarget>();
                Player.Bind(Raid, B, eye.transform);
                PlacePlayer(B, PlayerStart);
            }

            internal void Reload() => Raid.Install(layout);

            private RaidFloor Room(string id, Pose frame, Vector2? size, bool partition = false, int seam = 0,
                bool baffles = false, float floorInset = 0)
            {
                var door = size ?? new Vector2(2.2f, 2.65f);
                Collider floor = Box(frame, $"Support {id}", new Vector3(0, -.2f, -floorInset * .5f),
                    new Vector3(10, .4f, 10 - floorInset));
                float sideSize = (10 - door.x) * .5f;
                var frameColliders = new[]
                {
                    Box(frame, $"Left pilaster {id}", new Vector3(-(door.x + sideSize) * .5f, 1.6f, 5.08f),
                        new Vector3(sideSize, 3.2f, .16f)),
                    Box(frame, $"Right pilaster {id}", new Vector3((door.x + sideSize) * .5f, 1.6f, 5.08f),
                        new Vector3(sideSize, 3.2f, .16f)),
                    Box(frame, $"Head {id}", new Vector3(0, (3.2f + door.y) * .5f, 5.08f),
                        new Vector3(door.x, 3.2f - door.y, .16f))
                };
                var geometry = new List<Collider>(frameColliders)
                {
                    floor,
                    Box(frame, $"Rear {id}", new Vector3(0, 1.6f, -5.08f), new Vector3(10, 3.2f, .16f))
                };
                doorFrames.AddRange(frameColliders);
                foreach (int side in new[] { -1, 1 })
                {
                    if (side == seam)
                    {
                        geometry.Add(Box(frame, $"Seam front {id}", new Vector3(side * 5.08f, 1.6f, 2.25f),
                            new Vector3(.16f, 3.2f, 5.5f)));
                        geometry.Add(Box(frame, $"Seam rear {id}", new Vector3(side * 5.08f, 1.6f, -4.25f),
                            new Vector3(.16f, 3.2f, 1.5f)));
                    }
                    else
                        geometry.Add(Box(frame, $"Side {id}", new Vector3(side * 5.08f, 1.6f, 0),
                            new Vector3(.16f, 3.2f, 10)));
                }
                if (partition)
                    geometry.Add(Box(frame, "Disconnected partition in source", new Vector3(0, 1.6f, 1.75f),
                        new Vector3(10.34f, 3.2f, .35f)));
                if (baffles)
                    geometry.Add(Box(frame, "Low obstacle on the direct path", new Vector3(-.38f, .36f, 2.2f),
                        new Vector3(1.25f, .72f, 1.06f)));
                return new RaidFloor(id, "floor", "main", frame,
                    new Bounds(new Vector3(0, 1, 0), new Vector3(10.6f, 4.5f, 10.6f)), geometry, new[] { floor });
            }

            private static RaidAperture Aperture(RaidFloor floor, RaidPortalKind kind, Vector2? size, Vector3 pad)
            {
                var door = size ?? new Vector2(2.2f, 2.65f);
                var center = kind == RaidPortalKind.Door ? new Vector3(0, door.y * .5f, 5) : new Vector3(0, 3.2f, 0);
                var local = kind == RaidPortalKind.Door ? Quaternion.identity :
                    Quaternion.LookRotation(Vector3.up, Vector3.forward);
                return new RaidAperture(floor, kind, new Pose(floor.World(center), floor.Frame.rotation * local),
                    door, floor.World(pad));
            }

            private BoxCollider Box(Pose frame, string label, Vector3 center, Vector3 size)
            {
                var block = new GameObject(label);
                block.layer = SolidLayer;
                block.transform.SetParent(root.transform);
                block.transform.SetPositionAndRotation(frame.position + frame.rotation * center, frame.rotation);
                var collider = block.AddComponent<BoxCollider>();
                collider.size = size;
                return collider;
            }

            internal void PlacePlayer(RaidFloor floor, Vector3 feet)
            {
                Player.transform.SetPositionAndRotation(floor.World(feet), floor.Frame.rotation);
                Player.SetFloor(floor);
            }

            internal Collider Block(RaidFloor floor, Vector3 local, Vector3 size)
            {
                var collider = Box(floor.Frame, "Dynamic blocker", local, size);
                Physics.SyncTransforms();
                return collider;
            }

            internal void Remove(Collider collider)
            {
                UnityEngine.Object.DestroyImmediate(collider.gameObject);
                Physics.SyncTransforms();
            }

            internal void SetDoorFramesEnabled(bool enabled)
            {
                foreach (var frame in doorFrames)
                    frame.enabled = enabled;
                Physics.SyncTransforms();
            }

            internal bool IsClearRay(Vector3 from, Vector3 to)
            {
                var delta = to - from;
                return !Physics.Raycast(from, delta.normalized, delta.magnitude, 1 << SolidLayer,
                    QueryTriggerInteraction.Ignore);
            }

            internal RaidSightKind Sight(RaidFloor floor, Vector3 localEye, Vector3 localForward, RaidEnemyProfile profile) =>
                Raid.Sight(floor, floor.World(localEye), floor.Frame.rotation * localForward, profile, Player);

            internal bool Route(RaidFloor a, Vector3 start, RaidFloor b, Vector3 end, RaidEnemyProfile profile,
                RaidRouteKind? kind = null) => Raid.TryGetRouteKind(a, a.World(start), b, b.World(end), profile, out var first) &&
                    (!kind.HasValue || first == kind.Value);

            internal RaidEnemyChaser Spawn(RaidFloor floor, Vector3 localFeet, Vector3 facing, RaidEnemyProfile profile,
                Func<bool> authority = null, Func<Transform, Pose, bool> writer = null)
            {
                var actor = new GameObject("Enemy feet pose");
                actor.transform.SetParent(root.transform);
                actor.transform.SetPositionAndRotation(floor.World(localFeet),
                    Quaternion.LookRotation(floor.Frame.rotation * facing, floor.Up));
                var chaser = actor.AddComponent<RaidEnemyChaser>();
                chaser.Begin(Raid, floor, profile, authority ?? (() => true),
                    writer == null ? null : pose => writer(actor.transform, pose));
                return chaser;
            }

            internal int TickUntil(RaidEnemyChaser chaser, Func<bool> done, int max)
            {
                for (int index = 1; index <= max; index++)
                {
                    chaser.Advance(Step);
                    if (done())
                        return index;
                }
                return -1;
            }

            public void Dispose()
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (scene.isLoaded)
                    throw new InvalidOperationException("Could not close fixture scene.");
            }
        }
    }
}

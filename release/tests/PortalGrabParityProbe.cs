using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Navigation;
using UnityEditor.SceneManagement;
using UnityEngine;
using NavigationRuntime = Contour4.Procedural.Navigation.RaidNavigation;

// Editor-only binary fixture; the asmdef is named Contour4NativeRaid (the runtime's friend assembly).
// CONTOUR4_BIN and CONTOUR4_RESULT select the DLL pair and results file.
// Supply explicit dependency directories below, or use CONTOUR4_RUNTIME_TASK from Run-ProductionProbe.ps1.
public static class PortalGrabParityProbe
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static;
    private const int CargoLayer = 11;
    private const int CargoMask = 1 << CargoLayer;
    private static readonly List<string> Results = new List<string>();
    private static int failures;

    public static void Run()
    {
        string task = Environment.GetEnvironmentVariable("CONTOUR4_RUNTIME_TASK");
        string bin = Environment.GetEnvironmentVariable("CONTOUR4_BIN");
        string managed = Environment.GetEnvironmentVariable("CONTOUR4_GAME_MANAGED") ??
            Path.GetFullPath(Path.Combine(task, "../../contour4-mobius-rails/compose/game/REPO_Data/Managed"));
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Thunderstore Mod Manager/DataFolder/REPO/cache");
        string bep = Environment.GetEnvironmentVariable("CONTOUR4_BEPINEX_CORE") ??
            Path.Combine(cache, "BepInEx-BepInExPack/5.4.2305/BepInExPack/BepInEx/core");
        string repoLib = Environment.GetEnvironmentVariable("CONTOUR4_REPOLIB_DIR") ??
            Path.Combine(cache, "Zehs-REPOLib/4.5.1/plugins");
        string valuables = Environment.GetEnvironmentVariable("CONTOUR4_VALUABLES_DIR") ??
            Path.Combine(task, "dependencies");
        Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string file = new AssemblyName(args.Name).Name + ".dll";
            foreach (string directory in new[] { bin, managed, bep, repoLib, valuables })
            {
                string path = Path.Combine(directory, file);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        }

        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try
        {
            var game = Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll"));
            var plugin = Assembly.LoadFrom(Path.Combine(bin, "Contour4.dll"));
            var ringBundle = AssetBundle.LoadFromFile(Environment.GetEnvironmentVariable("CONTOUR4_RING_BUNDLE") ??
                Path.Combine(Path.GetDirectoryName(bin), "contour4-ring.bundle"));
            var shader = ringBundle ? ringBundle.LoadAsset<Shader>("Assets/Contour4Ring/Portal.shader") : null;
            if (!shader) throw new InvalidOperationException("Exact portal shader failed to load.");
            plugin.GetType("SpaceJourney.Contour4", true).GetProperty("PortalShader", Members).SetValue(null, shader);
            CheckRaid(game, plugin, "same-up", Quaternion.Euler(0f, -50f, 0f));
            CheckRaid(game, plugin, "quarter-turn", Quaternion.Euler(0f, -50f, 90f));
            ringBundle.Unload(false);
        }
        catch (Exception error)
        {
            Results.Add("HARNESS ERROR " + error);
            throw;
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
            Results.Add("violations=" + failures);
            File.WriteAllLines(Environment.GetEnvironmentVariable("CONTOUR4_RESULT"), Results);
            foreach (string result in Results) Debug.Log(result);
        }

        if (failures > 0) throw new Exception("Procedural door grab failed " + failures + " checks.");
    }

    private static void CheckRaid(Assembly game, Assembly plugin, string layout, Quaternion exitRotation)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Procedural raid level");
        var host = new GameObject("Placed raid module");
        host.transform.SetParent(root.transform);
        var first = Frame(host.transform, "First door", new Vector3(2f, 3f, 4f), Quaternion.Euler(0f, 35f, 0f));
        var second = Frame(host.transform, "Second door", new Vector3(-12f, 8f, 17f), exitRotation);
        var firstFloor = Floor("first", first);
        var secondFloor = Floor("second", second);
        var widthAndHeight = new Vector2(1.5f, 2.5f);
        var forward = new NativeRaidPortal("forward", first, second, widthAndHeight, firstFloor, secondFloor, false,
            RaidPortalKind.Door, RaidPortalKind.Door, first, second, .39f);
        var reverse = new NativeRaidPortal("reverse", second, first, widthAndHeight, secondFloor, firstFloor, false,
            RaidPortalKind.Door, RaidPortalKind.Door, second, first, .39f);
        var near = Frame(host.transform, "Near alternative", new Vector3(80f, 3f, 4f), first.rotation);
        var distant = Frame(host.transform, "Far alternative", new Vector3(90f, 3f, 4f), second.rotation);
        var alternative = new NativeRaidPortal("closer", near, distant, widthAndHeight, firstFloor, secondFloor, false,
            RaidPortalKind.Door, RaidPortalKind.Door, near, distant, .39f);
        var back = new NativeRaidPortal("back", distant, near, widthAndHeight, secondFloor, firstFloor, false,
            RaidPortalKind.Door, RaidPortalKind.Door, distant, near, .39f);
        var portals = new[] { forward, reverse, alternative, back };
        var raid = host.AddComponent<RaidAssembly>();
        var navigation = host.AddComponent<NavigationRuntime>();
        navigation.Install(new RaidLayout(new[] { firstFloor, secondFloor }, Array.Empty<RaidPortal>(),
            Array.Empty<RaidWalkPassage>(), 1, new Vector3(500f, 300f, 500f), .35f, 1.9f));
        Set(raid, "solidMask", 1);
        Set(raid, "<Context>k__BackingField", new RaidNavigationContext(1, navigation, Array.Empty<BakedRoom>(), portals, 1));

        Component Stock(string name) => new GameObject(name).AddComponent(game.GetType(name, true));
        var generator = Stock("LevelGenerator");
        Set(generator, "Instance", generator);
        Set(generator, "LevelParent", root);
        var level = ScriptableObject.CreateInstance(game.GetType("Level", true));
        level.name = "Level - SpaceJourneyContour4Procedural";
        Set(generator, "Level", level);
        var gameManager = Stock("GameManager");
        Set(gameManager, "instance", gameManager);
        var director = Stock("GameDirector");
        Set(director, "instance", director);
        var camera = new GameObject("Stock main camera").AddComponent<Camera>();
        camera.enabled = false;
        Set(director, "MainCamera", camera);
        var grabber = Stock("PhysGrabber");
        Set(grabber, "instance", grabber);
        Set(grabber, "isLocal", true);
        var module = host.AddComponent(plugin.GetType("Contour4.NativeRaid.NativeRaidModule", true));
        Set(module, "raid", raid);
        var presentation = plugin.GetType("SpaceJourney.ProceduralPortalPresentation", true);
        var attach = presentation.GetMethod("Attach", Members);
        attach.Invoke(null, new object[] { host.transform, portals });

        var player = Stock("PlayerController");
        Set(player, "instance", player);
        var playerBody = player.gameObject.AddComponent<Rigidbody>();
        playerBody.useGravity = false;
        Set(player, "rb", playerBody);
        var avatar = Stock("PlayerAvatar");
        Set(avatar, "instance", avatar);
        Set(grabber, "playerAvatar", avatar);
        Set(grabber, "grabReleaseDistance", 4.5f);
        var puller = new GameObject("Native puller").transform;
        Set(grabber, "physGrabPointPuller", puller);
        var gravity = host.AddComponent(plugin.GetType("Contour4.NativeRaid.NativeRaidGravity", true));
        Set(gravity, "raid", raid);
        Set(gravity, "levelRoot", root.transform);
        Set(gravity, "nextActorScan", float.PositiveInfinity);

        var grab = plugin.GetType("SpaceJourney.PortalGrab", true);
        var cast = grab.GetMethod("Cast", Members);
        var finalizer = grab.GetNestedType("NativeRay", Members).GetMethod("Finalizer", Members);
        foreach (var portal in new[] { forward, reverse })
        {
            CheckDirection(layout, portal, alternative, game, grabber, camera, cast, finalizer, level, grab,
                player, playerBody, avatar, puller, module, gravity, raid, host.transform);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Physics.SyncTransforms();
        }

        var registry = presentation.GetProperty("Grab", Members);
        var original = registry?.GetValue(null);
        Check(layout + " placed level owns the active grab context", original != null);
        var nextHost = new GameObject("Replacement placed raid");
        nextHost.transform.SetParent(root.transform);
        var nextModule = nextHost.AddComponent(module.GetType());
        Set(nextModule, "raid", raid);
        attach.Invoke(null, new object[] { nextHost.transform, portals });
        var next = registry?.GetValue(null);
        Check(layout + " replacement owns a fresh context", next != null && !ReferenceEquals(next, original));
        // Stock runtime components do not receive Unity play-mode messages in this edit-mode fixture.
        var teardown = module.GetType().GetMethod("OnDestroy", Members);
        teardown?.Invoke(module, null);
        UnityEngine.Object.DestroyImmediate(module);
        Check(layout + " previous module teardown preserves replacement", next != null && ReferenceEquals(registry?.GetValue(null), next));
        teardown?.Invoke(nextModule, null);
        UnityEngine.Object.DestroyImmediate(nextHost);
        Check(layout + " final module teardown drops its grab context", registry != null && registry.GetValue(null) == null);
        navigation.Close();
    }

    private static void CheckDirection(string layout, NativeRaidPortal portal, NativeRaidPortal alternative, Assembly game,
        Component grabber, Camera camera, MethodInfo cast, MethodInfo finalizer, ScriptableObject level, Type grab,
        Component player, Rigidbody playerBody, Component avatar, Transform puller, Component module,
        Component gravity, RaidAssembly raid, Transform host)
    {
        var origin = portal.Entrance.TransformPoint(new Vector3(0f, .35f, -.9f));
        var direction = portal.Entrance.forward;
        camera.transform.SetPositionAndRotation(origin, portal.Entrance.rotation);
        var remote = Box(portal.Exit, "Hand cargo", new Vector3(0f, .35f, -.8f), new Vector3(.24f, .24f, .24f), CargoLayer);
        // Game-spawned valuables may live outside LevelParent and miss the periodic actor scan.
        remote.transform.SetParent(null, true);
        var body = remote.gameObject.AddComponent<Rigidbody>();
        body.useGravity = false;
        var held = remote.gameObject.AddComponent(game.GetType("PhysGrabObject", true));
        Set(held, "rb", body);
        var local = Box(portal.Entrance, "Near obstruction", new Vector3(0f, .35f, -.4f),
            new Vector3(.2f, .2f, .15f), CargoLayer);
        var blocker = Box(portal.Exit, "Exit containment", new Vector3(0f, .35f, 0f),
            new Vector3(.30f, .30f, .30f), CargoLayer);
        blocker.enabled = false;

        RaycastHit Cast(float range, int mask = CargoMask, QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore,
            Vector3? start = null, bool nativeStart = false)
        {
            Physics.SyncTransforms();
            var args = new object[] { start ?? origin, direction, default(RaycastHit), range, mask, triggers, grabber };
            bool hit = (bool)cast.Invoke(null, args);
            if (!nativeStart) finalizer.Invoke(null, null);
            return hit ? (RaycastHit)args[2] : default;
        }

        void Expect(string check, bool passed)
        {
            Check(layout + " " + portal.Id + " " + check, passed);
        }

        Expect("prefers local obstruction", Cast(2f).collider == local);
        local.enabled = false;

        var rayHit = Cast(2f);
        Expect("selects actual far-side Rigidbody", rayHit.rigidbody == body);
        Expect("reports total path to the far-side contact", Mathf.Abs(rayHit.distance - 1.58f) < .005f);
        Expect("honors remaining reach", Cast(1.50f).collider == null);
        Expect("honors the native layer mask", Cast(2f, 1).collider == null);

        blocker.enabled = true;
        Expect("rejects a solid enclosing the exit ray origin", Cast(2f).collider == null);
        blocker.isTrigger = true;
        Expect("ignores exit trigger when native ray ignores it", Cast(2f).rigidbody == body);
        Expect("rejects exit trigger when native ray collides with it", Cast(2f, CargoMask, QueryTriggerInteraction.Collide).collider == null);
        blocker.enabled = false;

        Expect("does not pass through the jamb",
            Cast(2f, start: origin + portal.Entrance.right * (portal.HalfWidth + .03f)).collider == null);
        Set(grabber, "isLocal", false);
        Expect("preserves the nonlocal stock cast", Cast(2f).collider == null);
        Set(grabber, "isLocal", true);
        string originalName = level.name;
        level.name = "Unrelated inventory level";
        Expect("preserves the stock cast outside the procedural level", Cast(2f).collider == null);
        level.name = originalName;

        var originalAlternative = alternative.Entrance.position;
        alternative.Entrance.SetPositionAndRotation(
            portal.Entrance.position - portal.Entrance.forward * .2f, portal.Entrance.rotation);
        Expect("uses the nearest entry and spends only one portal segment", Cast(2f).collider == null);
        alternative.Entrance.position = originalAlternative;

        body.isKinematic = true;
        Expect("rejects immovable cargo through the portal", Cast(2f).collider == null);
        body.isKinematic = false;
        var joint = body.gameObject.AddComponent<FixedJoint>();
        Expect("rejects attached cargo through the portal", Cast(2f).collider == null);
        UnityEngine.Object.DestroyImmediate(joint);

        if (layout == "same-up")
        {
            var ringRoot = new GameObject("Embedded authored Ring");
            ringRoot.transform.SetParent(host);
            var runtime = ringRoot.AddComponent(grab.Assembly.GetType("SpaceJourney.RingRuntime", true));
            var ringView = ringRoot.AddComponent(grab.Assembly.GetType("SpaceJourney.PortalViews", true));
            Set(runtime, "ring", ringRoot.transform);
            Set(runtime, "portalViews", ringView);
            Set(runtime, "proceduralBranch", true);
            Set(runtime, "live", true);
            Set(runtime, "active", runtime);
            Transform firstRoom = RingMouth(ringRoot.transform, "From", "To",
                portal.Entrance.position - direction * .2f, portal.Entrance.rotation);
            Transform nextRoom = RingMouth(ringRoot.transform, "To", "From",
                portal.Exit.position + Vector3.up * 15f, portal.Exit.rotation);
            var pair = grab.Assembly.GetType("SpaceJourney.Portal", true).GetMethod("Pair", Members)
                .Invoke(null, new object[] { firstRoom, "To", nextRoom, "From", false });
            object endA = pair.GetType().GetProperty("A", Members).GetValue(pair);
            object endB = pair.GetType().GetProperty("B", Members).GetValue(pair);
            var crossing = Activator.CreateInstance(grab.Assembly.GetType("SpaceJourney.Crossing", true),
                Members, null, new[] { endA, endB }, null);
            ((IList)Field(runtime, "doors").GetValue(runtime)).Add(crossing);
            var ringCargo = Box(nextRoom, "Cargo visible through embedded Ring",
                new Vector3(0f, .35f, -.8f), new Vector3(.24f, .24f, .24f), CargoLayer);
            var ringBody = ringCargo.gameObject.AddComponent<Rigidbody>();
            ringBody.useGravity = false;
            Expect("a nearer embedded Ring door wins when both systems are active", Cast(2f).rigidbody == ringBody);
            firstRoom.position += Vector3.up * 15f;
            Expect("the ordinary door selects its cargo while the Ring stays active", Cast(2f).rigidbody == body);
            Set(runtime, "active", null);
            UnityEngine.Object.DestroyImmediate(ringRoot);
        }

        var castForGrab = Cast(2f, nativeStart: true);
        var pullDistance = grab.GetMethod("PullDistance", Members);
        float initial = (float)pullDistance.Invoke(null, new object[] { origin, body.position });
        Expect("native start distance uses the linked eye", Mathf.Abs(initial - 1.7f) < .005f);
        var start = grab.GetNestedType("NativeGrabStart", Members).GetMethod("Postfix", Members);
        var update = grab.Assembly.GetType("SpaceJourney.Contour4", true).GetNestedType("MapGamePullPoint", Members)
            .GetMethod("Postfix", Members);
        Vector3 NativePull() => (Vector3)Field(grabber, "physGrabPointPullerPosition").GetValue(grabber);
        bool Close(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .005f;
        void MovePlayer(Vector3 feet) => playerBody.position = feet;
        Vector3 Feet(float depth) => portal.Entrance.TransformPoint(new Vector3(0f, -1.25f, depth));
        void SetHeld(bool value)
        {
            Set(grabber, "grabbed", value);
            Set(grabber, "grabbedPhysGrabObject", value ? held : null);
        }
        void Acquire(bool reuseRay = false)
        {
            if (!reuseRay) Cast(2f, nativeStart: true);
            SetHeld(true);
            start.Invoke(null, new object[] { grabber, held });
            finalizer.Invoke(null, null);
        }
        void UpdatePull() => update.Invoke(null, new object[] { grabber });

        var moveAcross = module.GetType().GetMethod("TryMoveAcross", Members);
        bool Cross(NativeRaidPortal door) =>
            (bool)moveAcross.Invoke(module, new object[] { player, avatar, door, Feet(-.392f) });
        MovePlayer(Feet(-.8f));
        puller.position = origin + direction * 1.7f;
        Acquire(true);
        Expect("native start maps the pull cache using the selected Rigidbody",
            castForGrab.rigidbody == body && Close(NativePull(), portal.MapPoint(puller.position)));
        Vector3 nextPull = origin + direction * 1.5f;
        puller.position = nextPull;
        UpdatePull();
        UpdatePull();
        Expect("native Update maps once and keeps its puller Transform untouched",
            Close(NativePull(), portal.MapPoint(nextPull)) && Close(puller.position, nextPull));
        SetHeld(false);
        UpdatePull();
        Expect("release clears the mapped pull cache", Close(NativePull(), nextPull));

        Acquire();
        playerBody.position = Feet(-8f);
        UpdatePull();
        Expect("range loss clears the mapped pull cache", Close(NativePull(), nextPull));
        MovePlayer(Feet(-.388f));
        Expect("an expired mapped hold cannot enter the stock cargo-transfer branch", !Cross(portal));
        SetHeld(false);
        UpdatePull();

        MovePlayer(Feet(-.8f));
        Acquire();
        Vector3 before = Feet(-.392f);
        Vector3 after = Feet(-.388f);
        MovePlayer(after);
        bool support = raid.TryPassPlayer(portal, after, out _, before);
        Expect("real destination capsule and floor accept the crossing", support);
        Expect("a different pair cannot consume the mapped hold", !Cross(alternative) && Close(playerBody.position, after));
        var landingBlock = Box(portal.Exit, "Player landing obstruction", new Vector3(0f, -.2f, -.75f),
            new Vector3(.9f, 1.2f, .6f), 0);
        Physics.SyncTransforms();
        Expect("blocked matching crossing preserves grab and does not move the player",
            !Cross(portal) && (bool)Field(grabber, "grabbed").GetValue(grabber) && Close(playerBody.position, after));
        UnityEngine.Object.DestroyImmediate(landingBlock.gameObject);
        Physics.SyncTransforms();

        Vector3 cargoPose = portal.Exit.InverseTransformPoint(body.position);
        Vector3 localVelocity = portal.Exit.InverseTransformDirection(body.linearVelocity = portal.Exit.forward * .2f);
        Quaternion cargoTurn = Quaternion.Inverse(portal.Exit.rotation) * body.rotation;
        Expect("unparented far-side cargo starts outside the actor cache", !((IList)Field(gravity, "actors").GetValue(gravity)).Contains(body));
        Expect("matching native crossing succeeds while the far-side cargo is held", Cross(portal));
        Expect("crossing leaves cargo pose and velocity in its destination room",
            Close(portal.Exit.InverseTransformPoint(body.position), cargoPose) &&
            Close(portal.Exit.InverseTransformDirection(body.linearVelocity), localVelocity) &&
            Quaternion.Angle(Quaternion.Inverse(portal.Exit.rotation) * body.rotation, cargoTurn) < .1f);
        Expect("crossing keeps the original stock grab", (bool)Field(grabber, "grabbed").GetValue(grabber) &&
            ReferenceEquals(Field(grabber, "grabbedPhysGrabObject").GetValue(grabber), held));
        Vector3 stockUpdate = portal.Exit.TransformPoint(new Vector3(0f, .35f, -.7f));
        Set(grabber, "physGrabPointPullerPosition", stockUpdate);
        UpdatePull();
        Expect("after crossing, stock pull coordinates pass through once", Close(NativePull(), stockUpdate));
        SetHeld(false);
        UpdatePull();
        UnityEngine.Object.DestroyImmediate(remote.gameObject);
        UnityEngine.Object.DestroyImmediate(local.gameObject);
        UnityEngine.Object.DestroyImmediate(blocker.gameObject);
    }

    private static void Check(string check, bool passed)
    {
        Results.Add((passed ? "PASS " : "FAIL ") + check);
        if (!passed) failures++;
    }

    private static Transform RingMouth(Transform root, string name, string target, Vector3 center, Quaternion pose)
    {
        var room = Frame(root, name, center, pose);
        var markers = new GameObject("Markers").transform;
        markers.SetParent(room, false);
        Frame(markers, "Grab_" + target, center - room.up * 1.6f, pose);
        return room;
    }

    private static Transform Frame(Transform root, string name, Vector3 position, Quaternion rotation)
    {
        var frame = new GameObject(name).transform;
        frame.SetParent(root);
        frame.SetPositionAndRotation(position, rotation);
        return frame;
    }

    private static RaidFloor Floor(string id, Transform frame)
    {
        var deck = Box(frame, "Supporting floor", new Vector3(0f, -1.45f, -2f), new Vector3(5f, .40f, 4f), 0);
        var pose = new Pose(frame.TransformPoint(Vector3.down * 1.25f), frame.rotation);
        return new RaidFloor(id, "deck", id, pose, new Bounds(new Vector3(0f, .4f, -2f), new Vector3(6f, 5f, 6f)),
            new[] { deck }, new[] { deck });
    }

    private static BoxCollider Box(Transform frame, string name, Vector3 center, Vector3 size, int layer)
    {
        var box = new GameObject(name).AddComponent<BoxCollider>();
        box.gameObject.layer = layer;
        box.transform.SetParent(frame, false);
        box.transform.localPosition = center;
        box.size = size;
        return box;
    }

    private static void Set(object target, string name, object value)
    {
        var field = Field(target, name);
        field.SetValue(field.IsStatic ? null : target, value);
    }

    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, Members) ??
        throw new MissingFieldException(target.GetType().FullName, name);
}

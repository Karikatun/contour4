using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Contour4.NativeRaid.Runtime;
using Contour4.Procedural.Navigation;
using UnityEditor.SceneManagement;
using UnityEngine;
using NavigationRuntime = Contour4.Procedural.Navigation.RaidNavigation;

// Run in the binary-only Editor fixture with the production plugin and stock managed references.
public static class PortalCameraSideProbe
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static;
    private const float HalfHeight = 1.25f;
    private const float NearCorner = .12f;
    private static readonly List<string> Results = new();
    private static int failures;

    public static void Run()
    {
        string task = Environment.GetEnvironmentVariable("CONTOUR4_RUNTIME_TASK");
        string bin = Environment.GetEnvironmentVariable("CONTOUR4_BIN");
        string managed = Path.GetFullPath(Path.Combine(task, "../../contour4-mobius-rails/compose/game/REPO_Data/Managed"));
        string bep = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Thunderstore Mod Manager/DataFolder/REPO/cache/BepInEx-BepInExPack/5.4.2305/BepInExPack/BepInEx/core");
        Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string file = new AssemblyName(args.Name).Name + ".dll";
            foreach (string directory in new[] { bin, managed, bep })
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
            CheckPair("straight cart-capable doors", 0f, Quaternion.identity, game, plugin);
            CheckPair("right-angle internal doors", .39f, Quaternion.Euler(0f, 0f, 90f), game, plugin);
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
            Results.Add("violations=" + failures);
            File.WriteAllLines(Environment.GetEnvironmentVariable("CONTOUR4_RESULT"), Results);
            foreach (string result in Results) Debug.Log(result);
        }
        if (failures != 0) throw new Exception("Native door camera handoff failed " + failures + " checks.");
    }

    private static void CheckPair(string name, float inset, Quaternion exitRotation, Assembly game, Assembly plugin)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject(name);
        var first = DoorFrame(root.transform, "Entrance", new Vector3(2f, 3f, 4f), Quaternion.Euler(0f, 35f, 0f));
        var second = DoorFrame(root.transform, "Exit", new Vector3(-12f, 8f, 17f), exitRotation);
        var firstFloor = Floor("first", first);
        var secondFloor = Floor("second", second);
        var size = new Vector2(inset == 0f ? 4.5f : 1.5f, HalfHeight * 2);
        var forward = new NativeRaidPortal("forward", first, second, size, firstFloor, secondFloor, inset == 0f,
            RaidPortalKind.Door, RaidPortalKind.Door, first, second, inset);
        var reverse = new NativeRaidPortal("reverse", second, first, size, secondFloor, firstFloor, inset == 0f,
            RaidPortalKind.Door, RaidPortalKind.Door, second, first, inset);
        var raid = root.AddComponent<RaidAssembly>();
        var navigation = root.AddComponent<NavigationRuntime>();
        navigation.Install(new RaidLayout(new[] { firstFloor, secondFloor }, Array.Empty<RaidPortal>(),
            Array.Empty<RaidWalkPassage>(), 1, new Vector3(500f, 300f, 500f), .35f, 1.9f));
        Set(raid, "solidMask", 1);
        Set(raid, "<Context>k__BackingField", new RaidNavigationContext(1, navigation, Array.Empty<BakedRoom>(),
            new[] { forward, reverse }, 1));

        var avatarType = game.GetType("PlayerAvatar", true);
        var avatar = new GameObject("Stock avatar").AddComponent(avatarType);
        Set(avatar, "instance", avatar);
        var grabberType = game.GetType("PhysGrabber", true);
        var grabber = new GameObject("Stock grabber").AddComponent(grabberType);
        Set(grabber, "instance", grabber);
        var body = new GameObject("Tracked capsule").AddComponent<Rigidbody>();
        var module = root.AddComponent(plugin.GetType("Contour4.NativeRaid.NativeRaidModule", true));
        Set(module, "raid", raid);
        Set(module, "trackedBody", body);
        Set(module, "trackedAvatar", avatar);
        var cameraSide = (Func<Vector3, float, NativeRaidPortal>)module.GetType().GetMethod("CameraSide", Members)
            .CreateDelegate(typeof(Func<Vector3, float, NativeRaidPortal>), module);

        foreach (var portal in new[] { forward, reverse })
            CheckDirection(name + " " + portal.Id, inset, portal, body, cameraSide, raid, grabber);
        navigation.Close();
    }

    private static void CheckDirection(string name, float inset, NativeRaidPortal portal, Rigidbody body,
        Func<Vector3, float, NativeRaidPortal> cameraSide, RaidAssembly raid, Component grabber)
    {
        Transform entrance = portal.Entrance;
        Vector3 AtDoor(float x, float y, float depth) => entrance.TransformPoint(new Vector3(x, y, depth));
        Vector3 before = AtDoor(0f, -HalfHeight, -inset - .002f);
        Vector3 after = AtDoor(0f, -HalfHeight, -inset + .002f);
        body.position = AtDoor(0f, -HalfHeight, -inset - .04f);
        Vector3 originalFeet = body.position;

        Verify(name + " physics accepts entry and rejects reverse movement",
            portal.Crossed(before, after, .35f, 1.9f) && !portal.Crossed(after, before, .35f, 1.9f));
        Verify(name + " physics has a clear supported landing", raid.TryPassPlayer(portal, after, out _, before));
        Verify(name + " eye stays in its room before visual plane", cameraSide(AtDoor(0f, .35f, -.015f), NearCorner) == null);
        Verify(name + " eye beyond near-door guard stays unmapped", cameraSide(AtDoor(0f, .35f, .30f), NearCorner) == null);
        Verify(name + " eye outside opening stays unmapped",
            cameraSide(AtDoor(portal.HalfWidth + .025f, .35f, .03f), NearCorner) == null);
        var firstView = AtDoor(0f, .35f, .06f);
        var leaningView = AtDoor(.22f, .55f, .15f);
        Verify(name + " center view hands off to paired room", cameraSide(firstView, NearCorner) == portal);
        Verify(name + " leaning view hands off to paired room", cameraSide(leaningView, NearCorner) == portal);
        Verify(name + " camera handoff leaves tracked body and visual frame in place",
            Vector3.Distance(body.position, originalFeet) < .001f &&
            Vector3.Distance(portal.MapPoint(firstView), portal.Exit.TransformPoint(new Vector3(0f, .35f, -.06f))) < .001f);

        Set(grabber, "grabbed", true);
        Verify(name + " holding cargo suppresses camera handoff", cameraSide(firstView, NearCorner) == null);
        Set(grabber, "grabbed", false);
        body.position = AtDoor(portal.HalfWidth - .10f, -HalfHeight, -inset - .04f);
        Verify(name + " body outside doorway clearance prevents handoff", cameraSide(firstView, NearCorner) == null);
        body.position = originalFeet;
        var blocker = Box(portal.Exit, "Blocked landing", new Vector3(0f, -.2f, -.75f), new Vector3(.9f, 1.2f, .6f));
        Physics.SyncTransforms();
        Verify(name + " blocked landing rejects physics and camera",
            !raid.TryPassPlayer(portal, after, out _, before) && cameraSide(firstView, NearCorner) == null);
        blocker.enabled = false;
        Physics.SyncTransforms();
    }

    private static Transform DoorFrame(Transform root, string name, Vector3 position, Quaternion rotation)
    {
        var frame = new GameObject(name).transform;
        frame.SetParent(root);
        frame.SetPositionAndRotation(position, rotation);
        return frame;
    }

    private static RaidFloor Floor(string id, Transform frame)
    {
        var deck = Box(frame, "Supporting floor", new Vector3(0f, -HalfHeight - .20f, -2f),
            new Vector3(5f, .40f, 4f));
        var pose = new Pose(frame.TransformPoint(Vector3.down * HalfHeight), frame.rotation);
        return new RaidFloor(id, "deck", id, pose, new Bounds(new Vector3(0f, .4f, -2f), new Vector3(6f, 5f, 6f)),
            new[] { deck }, new[] { deck });
    }

    private static BoxCollider Box(Transform frame, string name, Vector3 center, Vector3 size)
    {
        var box = new GameObject(name).AddComponent<BoxCollider>();
        box.transform.SetParent(frame, false);
        box.center = center;
        box.size = size;
        return box;
    }

    private static void Set(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, Members) ?? throw new MissingFieldException(target.GetType().FullName, name);
        field.SetValue(field.IsStatic ? null : target, value);
    }

    private static void Verify(string check, bool passed)
    {
        Results.Add((passed ? "PASS " : "FAIL ") + check);
        if (!passed) failures++;
    }
}

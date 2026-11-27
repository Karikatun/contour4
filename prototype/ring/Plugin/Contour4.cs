using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using Contour4.NativeRaid;
using Contour4.NativeRaid.Runtime;
using HarmonyLib;
using Photon.Pun;
using REPOLib;
using REPOLib.Modules;
using REPOLib.Objects.Sdk;
using Unity.AI.Navigation;
using UnityEngine;
using System.Collections;
using Contour4Valuables;

namespace SpaceJourney;

[BepInPlugin(Id, "Contour-4", "0.3.9")]
[BepInDependency("REPOLib", "4.5.1")]
[BepInDependency("spacejourney.contour4.valuables", BepInDependency.DependencyFlags.HardDependency)]
public sealed class Contour4 : BaseUnityPlugin
{
    internal static Shader PortalShader { get; private set; }
    internal static Shader MapShader { get; private set; }
    internal const string Id = "spacejourney.contour4";
    internal const string LevelName = "Level - SpaceJourneyContour4";
    private GameObject templates;
    private Harmony patches;
    private NativeRaidFeature nativeRaid;
    private RaidPrefabBranch proceduralRing;
    private LoadingArt loadingArt;
    private RoomPrefabBundle roomBundle;

    private void Awake()
    {
        try
        {
            string directory = Path.GetDirectoryName(Info.Location);
            loadingArt = LoadingArt.Load(directory);
            roomBundle = RoomPrefabBundle.Load(Path.Combine(directory, RoomPrefabBundle.BundleName),
                Path.Combine(directory, RoomPrefabBundle.PreparedBundleName), requireWear: true,
                trenchPath: Path.Combine(directory, RoomPrefabBundle.TrenchBundleName));
            nativeRaid = NativeRaidFeature.Attach(Config, Logger, Id + ".procedural-feature", LevelName, loadingArt.Bind,
                deferredRegistration: true, prepareCatalog: PrepareProceduralRaid, spawnCatalog: ProceduralCatalogLoot.Spawn,
                present: PresentProceduralRaid, prefabBranch: () => proceduralRing, roomPrefabs: roomBundle.Prefabs);
            patches = new Harmony(Id);
            foreach (var type in typeof(Contour4).Assembly.GetTypes().Where(type =>
                type.Namespace == typeof(Contour4).Namespace && type.IsDefined(typeof(HarmonyPatch), false)))
                patches.CreateClassProcessor(type).Patch();
        }
        catch (Exception error)
        {
            RemovePatches();
            Logger.LogError($"Contour-4 could not start: {error}");
            return;
        }
        BundleLoader.OnAllBundlesLoaded += BeginRegistration;
    }

    private void OnDestroy()
    {
        BundleLoader.OnAllBundlesLoaded -= BeginRegistration;
        RemovePatches();
        if (templates)
            Destroy(templates);
    }

    private void RemovePatches()
    {
        try { patches?.UnpatchSelf(); }
        finally
        {
            try { nativeRaid?.Dispose(); }
            finally
            {
                try { roomBundle?.Dispose(); }
                finally { loadingArt?.Dispose(); }
            }
        }
    }

    internal static bool IsSoloRing(LevelGenerator generator) => generator && generator.Level &&
        generator.Level.name == LevelName && !GameManager.Multiplayer();

    internal static bool IsProceduralRaid(LevelGenerator generator) => generator && generator.Level &&
        generator.Level.name == NativeRaidFeature.LevelName && !GameManager.Multiplayer();

    private static RingRuntime FindCurrentRing(LevelGenerator generator) =>
        (IsSoloRing(generator) || IsProceduralRaid(generator)) && generator.LevelParent
        ? generator.LevelParent.GetComponentInChildren<RingRuntime>()
        : null;

    private static void PrepareProceduralRaid(RaidAssembly raid)
    {
        ProceduralCatalogLoot.Prepare(raid);
        ProceduralRaidMap.Attach(raid);
        if (raid.BranchRoot && raid.BranchEntrance is RaidPlan.End entrance)
            ProceduralRaidMap.AttachRing(raid, raid.BranchRoot.GetComponent<RingRuntime>(), entrance);
    }

    private static void PresentProceduralRaid(RaidAssembly raid) =>
        ProceduralPortalPresentation.Attach(raid.transform, raid.Context.Portals);

    private bool registrationStarted;

    private void BeginRegistration()
    {
        if (registrationStarted) return;
        registrationStarted = true;
        if (typeof(ValuableApi).Assembly.GetName().Version < new System.Version(0, 2, 4))
        {
            Logger.LogError("Contour-4 levels were not registered: Contour4Valuables 0.2.4 or newer is required.");
            return;
        }
        StartCoroutine(RegisterWhenReady());
    }

    private IEnumerator RegisterWhenReady()
    {
        float deadline = Time.realtimeSinceStartup + 30f;
        while (!ValuableApi.IsReady && Time.realtimeSinceStartup < deadline)
            yield return null;
        if (!ValuableApi.IsReady)
        {
            Logger.LogError("Contour-4 levels were not registered: required Contour4Valuables 0.2.4 did not become ready within 30 seconds. Check its bundle and registration log.");
            yield break;
        }
        try
        {
            foreach (string key in CatalogLoot.Keys.Concat(new[] { "field-core" }).Concat(RaidCatalogSites.LaboratoryKeys))
            {
                var prefab = ValuableApi.GetPrefab(key);
                if (!prefab || !prefab.GetComponent<ValuableObject>() || !prefab.GetComponent<Rigidbody>())
                    throw new InvalidOperationException("Required valuable is unusable: " + key);
            }
            Register();
            nativeRaid.RegisterReady();
        }
        catch (Exception error)
        {
            Logger.LogError("Contour-4 was not registered; dependency or map content is unusable: " + error);
            if (templates) Destroy(templates);
        }
    }

    private void Register()
    {
        if (Levels.RegisteredLevels.Any(level => level.name == LevelName))
            return;

        var native = Levels.AllLevels.FirstOrDefault(level => level && level.name == "Level - Manor");
        if (!native || native.StartRooms.Count == 0)
            throw new InvalidOperationException("The stock Manor starter is unavailable.");

        string directory = Path.GetDirectoryName(Info.Location);
        var bundle = AssetBundle.LoadFromFile(Path.Combine(directory, "contour4-ring.bundle"));
        if (!bundle)
            throw new InvalidOperationException("Contour-4 could not open its ring bundle.");
        var model = bundle.LoadAsset<GameObject>("Assets/Contour4Ring/Contour4 Ring.prefab");
        if (!model)
            throw new InvalidOperationException("Contour-4 bundle does not contain the ring.");
        proceduralRing = ProceduralRingBranch.Describe(model);

        templates = new GameObject("Contour-4 inactive templates");
        templates.SetActive(false);
        DontDestroyOnLoad(templates);
        var root = new GameObject("Contour4 Quota Module");
        root.transform.SetParent(templates.transform, false);
        var ring = Instantiate(model, root.transform);
        ring.name = model.name;
        PrepareModule(root, ring.transform);
        PortalShader = bundle.LoadAsset<Shader>("Assets/Contour4Ring/Portal.shader");
        if (!PortalShader || !PortalShader.isSupported)
            throw new InvalidOperationException("The Portal shader is not supported.");
        MapShader = bundle.LoadAsset<Shader>("Assets/Contour4Ring/Map.shader");
        if (!MapShader || !MapShader.isSupported)
            throw new InvalidOperationException("The handheld map shader is not supported.");
        bundle.Unload(false);

        var level = Instantiate(native);
        level.name = LevelName;
        level.NarrativeName = "Контур-4";
        level.NarrativeNameLocalized = null;
        // Doorways need to show the full depth of the ring's 15 m rooms.
        level.FogEndDistance = Mathf.Max(level.FogEndDistance, 24f);
        level.ModuleAmount = 1; // One fixed layout, connected to the native starter.
        level.PassageMaxAmount = 0;
        level.HasEnemies = false;
        level.ValuablePresets = native.ValuablePresets.ToList();
        level.LoadingGraphic01 = new PrefabRef<Sprite>();
        level.LoadingGraphic02 = new PrefabRef<Sprite>();
        level.LoadingGraphic03 = new PrefabRef<Sprite>();
        var content = ScriptableObject.CreateInstance<LevelContent>();
        AccessTools.Field(typeof(LevelContent), "_level").SetValue(content, level);
        content.ModulesNormal1.Add(root);
        Levels.RegisterLevel(content);

        level.StartRooms.Add(native.StartRooms[0]);
        level.ConnectObject = native.ConnectObject;
        level.BlockObject = native.BlockObject;
        loadingArt.Bind(level);
        Logger.LogInfo("Contour-4 registered: 4 rooms, 2 bridges, 16 wall lights.");
    }

    private static void PrepareModule(GameObject root, Transform ring)
    {
        root.AddComponent<PhotonView>();
        var module = root.AddComponent<Module>();
        module.type = Module.Type.Normal;
        module.status = Module.Status.Finalized;
        root.AddComponent<RingRuntime>();

        RoomVolume quota = null;
        foreach (string roomName in RingRuntime.RoomNames)
        {
            var room = ring.Find(roomName);
            var floor = room.Find("Model").GetComponentsInChildren<BoxCollider>()
                .Single(collider => collider.name == roomName + "__Floor");
            if (roomName == "Quota")
            {
                var walkable = floor.gameObject.AddComponent<NavMeshModifier>();
                walkable.overrideArea = true;
                walkable.area = 0;
            }

            var markers = room.Find("Markers");
            var volumeNode = markers.Find(roomName + "__RoomVolume");
            var trigger = volumeNode.gameObject.AddComponent<BoxCollider>();
            trigger.size = new Vector3(12f, 3.7f, 12f);
            trigger.isTrigger = true;
            var volume = volumeNode.gameObject.AddComponent<RoomVolume>();
            if (roomName == "Quota")
                quota = volume;

        }

        var center = AddPathPoint(root.transform, "Quota path center", Vector3.up * .15f, quota, false);
        var entrance = AddPathPoint(root.transform, "Quota grid entrance", new Vector3(0, .15f, -5f), quota, true);
        center.ConnectedPoints.Add(entrance);
        entrance.ConnectedPoints.Add(center);
    }

    private static LevelPoint AddPathPoint(Transform parent, string name, Vector3 position, RoomVolume volume, bool connects)
    {
        var marker = new GameObject(name);
        marker.transform.SetParent(parent, false);
        marker.transform.localPosition = position;
        var point = marker.AddComponent<LevelPoint>();
        point.Room = volume;
        point.ModuleConnect = connects;
        point.ConnectedPoints = new List<LevelPoint>();
        return point;
    }

    [HarmonyPatch(typeof(Module), "ConnectionSetup")]
    private static class AlignRing
    {
        private static void Prefix(Module __instance)
        {
            if (!IsSoloRing(LevelGenerator.Instance)) return;
            var ring = __instance.GetComponent<RingRuntime>();
            if (ring)
            {
                ring.Connect(__instance);
                ValuableSites.InstallOrdinary(ring.Ring);
            }
        }
    }

    [HarmonyPatch(typeof(LevelGenerator), "GenerateDoneApply")]
    private static class FinishRing
    {
        private static void Postfix(LevelGenerator __instance)
        {
            var ring = FindCurrentRing(__instance);
            if (ring)
            {
                if (IsSoloRing(__instance)) ring.BeginRaid(__instance);
                else ring.BeginProceduralRaid();
            }
        }
    }

    [HarmonyPatch(typeof(ValuableDirector), "SetupHost")]
    private static class PrepareRingLoot
    {
        private static void Postfix(ValuableDirector __instance, ref IEnumerator __result)
        {
            if (IsSoloRing(LevelGenerator.Instance))
                __result = NativeRaidLoot.UseStockPools(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(ValuableDirector), "SpawnValuable")]
    private static class TrackLoot
    {
        private static void Postfix(ValuableVolume __1, ValuableObject __result)
        {
            if (!__1 || !__result || (!IsSoloRing(LevelGenerator.Instance) && !IsProceduralRaid(LevelGenerator.Instance)))
                return;
            var ring = __1.GetComponentInParent<RingRuntime>();
            if (ring)
                ring.TrackLoot(__result, __1.transform);
        }
    }

    [HarmonyPatch(typeof(PlayerDeathHead), nameof(PlayerDeathHead.Trigger))]
    private static class ReturnFallenHead
    {
        private static void Postfix(PlayerDeathHead __instance)
        {
            var ring = FindCurrentRing(LevelGenerator.Instance);
            if (ring)
                ring.PlaceFallenHead(__instance);
        }
    }

    [HarmonyPatch(typeof(PhysGrabCart), "CartSteer")]
    private static class SteerAcrossDoor
    {
        private static bool Prefix(PhysGrabCart __instance)
        {
            var ring = FindCurrentRing(LevelGenerator.Instance);
            return !ring || !ring.SteerCartAcrossDoor(__instance);
        }
    }

    [HarmonyPatch(typeof(PhysGrabber), "Update")]
    private static class MapGamePullPoint
    {
        private static void Postfix(PhysGrabber __instance)
        {
            if (!__instance.isLocal || PortalGrab.RemapRaidPull(__instance))
                return;
            var ring = FindCurrentRing(LevelGenerator.Instance);
            if (ring)
                ring.RemapHeldPull(__instance);
        }
    }
}

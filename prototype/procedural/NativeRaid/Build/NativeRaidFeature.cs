using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx.Configuration;
using BepInEx.Logging;
using Contour4.NativeRaid.Enemies;
using Contour4.NativeRaid.Runtime;
using HarmonyLib;
using Photon.Pun;
using REPOLib;
using REPOLib.Modules;
using REPOLib.Objects.Sdk;
using UnityEngine;
using StockModule = global::Module;

namespace Contour4.NativeRaid;

internal sealed class NativeRaidFeature : IDisposable
{
    internal const string LevelName = "Level - SpaceJourneyContour4Procedural";
    private const string GameAssemblyHash = "98A0FEDCDF70FC47FA49257BF8AAB80955D9B947717D236BCEB9DA6FD44F4D40";
    private static readonly Guid GameModuleId = new Guid("bb352449-c1e7-4a41-9f75-0ed4e11521ea");
    private static NativeRaidFeature instance;
    private GameObject templates;
    private Level level;
    private Level stockManor;
    private readonly ConfigEntry<string> configuredSeed;
    private readonly ManualLogSource logger;
    private readonly Harmony harmony;
    private readonly string additionalSoloLevel;
    private readonly Action<Level> bindLoadingArt;
    private Action<RaidAssembly> prepareCatalog, spawnCatalog, present;
    private Func<RaidPrefabBranch> prefabBranch;
    private RaidRoomPrefabs roomPrefabs;

    private NativeRaidFeature(ConfigFile config, ManualLogSource logger, string patchOwner,
        string additionalSoloLevel, Action<Level> bindLoadingArt)
    {
        this.logger = logger;
        this.additionalSoloLevel = additionalSoloLevel;
        this.bindLoadingArt = bindLoadingArt;
        configuredSeed = config.Bind("Raid", "Seed", "", "A signed 32-bit seed. Blank generates a new seed for each solo raid.");
        harmony = new Harmony(patchOwner);
    }

    internal static NativeRaidFeature Attach(ConfigFile config, ManualLogSource logger, string patchOwner,
        string additionalSoloLevel = null, Action<Level> bindLoadingArt = null,
        bool deferredRegistration = false, Action<RaidAssembly> prepareCatalog = null, Action<RaidAssembly> spawnCatalog = null,
        Action<RaidAssembly> present = null, Func<RaidPrefabBranch> prefabBranch = null, RaidRoomPrefabs roomPrefabs = null)
    {
        if (instance != null) throw new InvalidOperationException("The Contour-4 procedural feature is already attached.");
        RequireSupportedGame();
        var feature = new NativeRaidFeature(config, logger, patchOwner, additionalSoloLevel, bindLoadingArt);
        feature.prepareCatalog = prepareCatalog;
        feature.spawnCatalog = spawnCatalog;
        feature.present = present;
        feature.prefabBranch = prefabBranch;
        feature.roomPrefabs = roomPrefabs;
        feature.InstallPatches();
        instance = feature;
        if (!deferredRegistration) BundleLoader.OnAllBundlesLoaded += feature.Register;
        return feature;
    }

    private static void RequireSupportedGame()
    {
        var assembly = typeof(LevelGenerator).Assembly;
        if (assembly.ManifestModule.ModuleVersionId != GameModuleId)
            throw new NotSupportedException("Contour-4 requires the audited R.E.P.O. 0.5.2 Assembly-CSharp module.");
        // BepInEx can preload the game assembly from bytes, leaving Assembly.Location empty.
        string stockPath = Path.Combine(Application.dataPath, "Managed", "Assembly-CSharp.dll");
        using var sha256 = SHA256.Create();
        using var file = File.OpenRead(stockPath);
        string hash = BitConverter.ToString(sha256.ComputeHash(file)).Replace("-", "");
        if (hash != GameAssemblyHash)
            throw new NotSupportedException($"Contour-4 does not support Assembly-CSharp SHA-256 {hash}.");
    }

    private void InstallPatches()
    {
        try
        {
            harmony.CreateClassProcessor(typeof(PlaceModulePatch)).Patch();
            harmony.CreateClassProcessor(typeof(InstallNavigationPatch)).Patch();
            harmony.CreateClassProcessor(typeof(SoloSelectionPatch)).Patch();
            harmony.CreateClassProcessor(typeof(SpawnCatalogPatch)).Patch();
            NativeRaidLoot.Patch(harmony);
            harmony.CreateClassProcessor(typeof(NativeRaidCabinetGrab.FallHeightPatch)).Patch();
            harmony.CreateClassProcessor(typeof(NativeRaidSphericalFallPatch)).Patch();
            EnemyHooks.Install(harmony);
        }
        catch
        {
            harmony.UnpatchSelf();
            throw;
        }
    }

    public void Dispose()
    {
        if (instance != this) return;
        BundleLoader.OnAllBundlesLoaded -= Register;
        instance = null;
        try { EnemyHooks.Clear(); }
        finally
        {
            harmony.UnpatchSelf();
            if (templates) UnityEngine.Object.Destroy(templates);
        }
    }

    private void Register()
    {
        if (level || Levels.RegisteredLevels.Any(candidate => IsThisLevel(candidate))) return;
        try
        {
            var manor = Levels.AllLevels.FirstOrDefault(candidate => candidate && candidate.name == "Level - Manor");
            if (!manor || manor.StartRooms.Count == 0)
                throw new InvalidOperationException("The stock Manor start room is unavailable.");
            stockManor = manor;

            templates = new GameObject("Contour-4 Procedural inactive templates");
            templates.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(templates);
            var entry = RaidAssembly.Child(templates.transform, "Contour4 Procedural Normal").gameObject;
            entry.AddComponent<PhotonView>();
            var module = entry.AddComponent<StockModule>();
            module.type = StockModule.Type.Normal;
            module.status = StockModule.Status.Finalized;
            entry.AddComponent<NativeRaidModule>();

            level = UnityEngine.Object.Instantiate(manor);
            level.name = LevelName;
            level.NarrativeName = "Контур-4 · Процедурный рейд";
            level.NarrativeNameLocalized = null;
            level.ModuleAmount = 1;
            level.PassageMaxAmount = 0;
            level.HasEnemies = true;
            level.FogEndDistance = Mathf.Max(manor.FogEndDistance, 26f);
            level.ValuablePresets = manor.ValuablePresets.ToList();
            level.LoadingGraphic01 = new PrefabRef<Sprite>();
            level.LoadingGraphic02 = new PrefabRef<Sprite>();
            level.LoadingGraphic03 = new PrefabRef<Sprite>();

            var content = ScriptableObject.CreateInstance<LevelContent>();
            AccessTools.Field(typeof(LevelContent), "_level").SetValue(content, level);
            content.ModulesNormal1.Add(entry);
            Levels.RegisterLevel(content);

            // REPOLib normalizes Level's prefab lists during registration. Reuse the native truck and quota.
            level.StartRooms.Add(manor.StartRooms[0]);
            level.ConnectObject = manor.ConnectObject;
            level.BlockObject = manor.BlockObject;
            if (bindLoadingArt != null)
                bindLoadingArt(level);
            else
            {
                level.LoadingGraphic01 = manor.LoadingGraphic01;
                level.LoadingGraphic02 = manor.LoadingGraphic02;
                level.LoadingGraphic03 = manor.LoadingGraphic03;
            }
            logger.LogInfo($"{LevelName} registered with stock starter and enemy generation.");
        }
        catch (Exception error)
        {
            if (templates) UnityEngine.Object.Destroy(templates);
            if (level) UnityEngine.Object.Destroy(level);
            logger.LogError($"Failed to register {LevelName}: {error}");
        }
    }

    // The production map calls this only after its shared mandatory companion gate.
    internal void RegisterReady() => Register();

    private static bool IsThisLevel(Level candidate) => candidate && candidate.name == LevelName;

    private static bool IsSoloLevel(Level candidate) =>
        IsThisLevel(candidate) || (candidate && instance?.additionalSoloLevel == candidate.name);

    internal int RaidSeed()
    {
        string value = configuredSeed.Value.Trim();
        if (value.Length == 0) return Guid.NewGuid().GetHashCode();
        if (int.TryParse(value, out int seed)) return seed;
        throw new InvalidOperationException("Contour-4 Procedural Seed must be a signed 32-bit integer or blank.");
    }

    [HarmonyPatch(typeof(StockModule), "ConnectionSetup")]
    private static class PlaceModulePatch
    {
        private static void Prefix(StockModule __instance)
        {
            var generator = LevelGenerator.Instance;
            if (!generator || !IsThisLevel(generator.Level) || GameManager.Multiplayer()) return;
            var entry = __instance.GetComponent<NativeRaidModule>();
            if (entry) entry.Place(__instance, instance.RaidSeed, instance.stockManor, instance.prepareCatalog,
                instance.prefabBranch?.Invoke(), instance.roomPrefabs);
        }
    }

    [HarmonyPatch(typeof(LevelGenerator), "GenerateDoneApply")]
    private static class SpawnCatalogPatch
    {
        private static void Postfix(LevelGenerator __instance)
        {
            if (!IsThisLevel(__instance.Level) || GameManager.Multiplayer() || !SemiFunc.IsMasterClientOrSingleplayer()) return;
            var entry = __instance.LevelParent.GetComponentInChildren<NativeRaidModule>();
            if (entry) entry.SpawnCatalog(instance.spawnCatalog);
        }
    }

    [HarmonyPatch(typeof(LevelGenerator), nameof(LevelGenerator.NavMeshSetup))]
    private static class InstallNavigationPatch
    {
        private static void Prefix(LevelGenerator __instance)
        {
            if (!IsThisLevel(__instance.Level) || GameManager.Multiplayer()) return;
            var entry = __instance.LevelParent.GetComponentInChildren<NativeRaidModule>();
            if (!entry) throw new InvalidOperationException("The stock generator did not place the Contour-4 module.");
            entry.InstallNavigation(__instance, instance.present);
        }
    }

    [HarmonyPatch(typeof(RunManager), "SetRunLevel")]
    private static class SoloSelectionPatch
    {
        private static readonly FieldInfo LevelsField = AccessTools.Field(typeof(RunManager), "levels");

        private static void Prefix(RunManager __instance, ref List<Level> __state)
        {
            if (!GameManager.Multiplayer()) return;
            var candidates = (List<Level>)LevelsField.GetValue(__instance);
            if (candidates == null || !candidates.Any(IsSoloLevel)) return;
            __state = candidates;
            LevelsField.SetValue(__instance, candidates.Where(candidate => !IsSoloLevel(candidate)).ToList());
        }

        private static void Finalizer(RunManager __instance, List<Level> __state)
        {
            if (__state != null) LevelsField.SetValue(__instance, __state);
        }
    }
}

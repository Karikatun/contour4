using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HarmonyLib;

internal static class StockEnemyBindings
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [DllImport("kernel32.dll")]
    private static extern void ExitProcess(uint exitCode);

    private static void Main(string[] args)
    {
        int result = Check(args);
        Console.Out.Flush();
        Console.Error.Flush();
        // Standalone Windows Unity Mono faults in shutdown after HarmonyX AccessTools initialization,
        // even with a test that references neither the game assembly nor the plugin.
        ExitProcess((uint) result);
    }

    private static int Check(string[] args)
    {
        try
        {
            string managed = args[0];
            string pluginPath = args[1];
            AppDomain.CurrentDomain.AssemblyResolve += (sender, message) =>
                Resolve(new AssemblyName(message.Name).Name, Path.GetDirectoryName(pluginPath), managed);
            var game = Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll"));
            var plugin = Assembly.LoadFrom(pluginPath);
            var hooks = plugin.GetType("Contour4.NativeRaid.Enemies.EnemyHooks", true);
            var supported = (Guid) hooks.GetField("SupportedGameModule", Declared).GetValue(null);
            Require(supported == game.ManifestModule.ModuleVersionId, "Assembly MVID");
            var visionRoutine = game.GetType("EnemyVision", true).GetMethod("Vision", Declared);
            var vision = AccessTools.EnumeratorMoveNext(visionRoutine);
            Require(vision.DeclaringType.Name == "<Vision>d__39" && ((MethodInfo) vision).ReturnType == typeof(bool),
                "EnemyVision iterator");
            Require(game.GetType("EnemyVision", true).GetMethod("OnDisable", Declared).ReturnType == typeof(void),
                "native EnemyVision lifecycle target");
            Require(PatchProcessor.GetOriginalInstructions(hooks.GetMethod("Install", Declared)).Count(instruction =>
            {
                var method = instruction.operand as MethodInfo;
                return method != null && method.DeclaringType == typeof(Harmony) && method.Name == "Patch";
            }) == 4, "native vision, chase, look and disable hook registrations");
            var enemy = game.GetType("Enemy", true);
            Require(PatchProcessor.GetOriginalInstructions(enemy.GetMethod("EnemyTeleported", Declared)).Any(instruction =>
            {
                var method = instruction.operand as MethodInfo;
                return method != null && method.DeclaringType.Name == "EnemyRigidbody" && method.Name == "Teleport";
            }), "native EnemyTeleported updates its independent body");
            Test(hooks, vision, "VisionCode", 761, 20, "TryPortalDirection", 1);
            Test(hooks, game.GetType("EnemyStateChase", true).GetMethod("Update", Declared),
                "ChaseCode", 345, 3, "DriveThroughDoor", 1);
            Test(hooks, game.GetType("EnemyHeadController", true).GetMethod("Update", Declared),
                "HeadCode", 298, 6, "LookThroughDoor", 3);
            TestLandingAndLiveEye(hooks);
            Console.WriteLine("PASS stock API and transpiler binding for " + supported);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void TestLandingAndLiveEye(Type hooks)
    {
        var drive = PatchProcessor.GetOriginalInstructions(hooks.GetMethod("DriveThroughDoor", Declared));
        int supports = CallIndex(drive, "RaidNavigation", "TryStand");
        int clearance = CallIndex(drive, "RaidNavigation", "BodyClear");
        int occupancy = CallIndex(drive, "StockPortalPursuit", "LandingOccupied");
        int warp = CallIndex(drive, "StockPortalPursuit", "Warp");
        Require(supports >= 0 && supports < clearance && clearance < occupancy && occupancy < warp,
            "landing support, body clearance and actor occupancy before native Warp");

        var targetScope = PatchProcessor.GetOriginalInstructions(hooks.GetMethod("TryScope", Declared));
        Require(CallIndex(targetScope, "Vector3", "op_Equality") < 0, "identity checked without proxy position equality");
        var sightMethod = hooks.GetMethods(Declared).Single(method =>
            method.Name == "TryPortalDirection" && method.GetParameters().Length == 6);
        var sight = PatchProcessor.GetOriginalInstructions(sightMethod);
        int refresh = CallIndex(sight, "NativeRaidModule", "RefreshTargetEye");
        int point = CallIndex(sight, "RaidTarget", "get_SightPoint");
        int observe = CallIndex(sight, "StockPortalPursuit", "SeesPortal");
        Require(refresh >= 0 && refresh < point && point < observe,
            "live stock player eye refreshed before portal optics");
        Require(sight.Take(refresh).Any(instruction =>
        {
            var field = instruction.operand as FieldInfo;
            return field != null && field.DeclaringType.Name == "PlayerAvatar" && field.Name == "PlayerVisionTarget";
        }), "the live eye comes from the observed native PlayerAvatar");
        var module = hooks.Assembly.GetType("Contour4.NativeRaid.NativeRaidModule", true);
        var refreshBody = PatchProcessor.GetOriginalInstructions(module.GetMethod("RefreshTargetEye", Declared));
        Require(CallIndex(refreshBody, "RaidAssembly", "SyncVisionEye") >= 0,
            "native eye refresh uses the same instantaneous sync as the Unity regression");
        Console.WriteLine("PASS compiled native adapter orders landing checks before Warp and refreshes the player eye before sight");
    }

    private static int CallIndex(List<CodeInstruction> code, string type, string name)
    {
        return code.FindIndex(instruction =>
        {
            var method = instruction.operand as MethodInfo;
            return method != null && method.DeclaringType.Name == type && method.Name == name;
        });
    }

    private static Assembly Resolve(string name, params string[] directories)
    {
        foreach (string directory in directories)
        {
            string file = Path.Combine(directory, name + ".dll");
            if (File.Exists(file))
                return Assembly.LoadFrom(file);
        }
        return null;
    }

    private static void Test(Type hooks, MethodBase original, string patch, int before, int growth, string call, int callCount)
    {
        ILGenerator generator;
        List<CodeInstruction> source = PatchProcessor.GetOriginalInstructions(original, out generator);
        Require(source.Count == before, patch + " original instruction count " + source.Count);
        var transform = hooks.GetMethod(patch, Declared);
        object[] parameters = transform.GetParameters().Length == 2
            ? new object[] { source, generator }
            : new object[] { source };
        var changed = ((IEnumerable<CodeInstruction>) transform.Invoke(null, parameters)).ToList();
        Require(changed.Count == before + growth, patch + " changed instruction count " + changed.Count);
        Require(changed.Count(instruction =>
        {
            var method = instruction.operand as MethodInfo;
            return method != null && method.DeclaringType == hooks && method.Name == call;
        }) == callCount, patch + " injected helper calls");

        var labels = new HashSet<Label>();
        foreach (var instruction in changed)
        foreach (Label label in instruction.labels)
            Require(labels.Add(label), patch + " duplicated branch label");
        foreach (var instruction in changed)
        {
            if (instruction.operand is Label)
                Require(labels.Contains((Label) instruction.operand), patch + " unresolved branch label");
            var destinations = instruction.operand as Label[];
            if (destinations != null)
                Require(destinations.All(labels.Contains), patch + " unresolved switch label");
            Require(instruction.opcode.OperandType != OperandType.ShortInlineBrTarget, patch + " short branch");
        }
        Console.WriteLine("PASS " + original.DeclaringType.FullName + "." + original.Name +
            " " + before + " -> " + changed.Count + " instructions");
    }

    private static void Require(bool found, string requirement)
    {
        if (!found)
            throw new InvalidOperationException("Failed " + requirement);
    }
}

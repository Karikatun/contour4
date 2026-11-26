param(
    [Parameter(Mandatory)] [string] $EditionSource,
    [Parameter(Mandatory)] [string] $Package,
    [Parameter(Mandatory)] [string] $Output,
    [Parameter(Mandatory)] [string] $WarmPackageCache,
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$out = [IO.Path]::GetFullPath($Output)
$source = (Resolve-Path -LiteralPath $EditionSource).Path
& "$source/release/tests/Verify-WorkroomClutter.ps1" -EditionSource $source -Package $Package -Output $out `
    -WarmPackageCache $WarmPackageCache -Rooms both -ExpectClutter -PrepareOnly
@'
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpaceJourney
{
    public static class PairRackPreviousChecks
    {
        public static void Run()
        {
            string output = Environment.GetEnvironmentVariable("CONTOUR4_FURNITURE_OUTPUT");
            var failures = new List<string>();
            foreach (string suite in new[] { "both", "storage", "dispatch" })
            {
                Environment.SetEnvironmentVariable("CONTOUR4_FURNITURE_ROOMS", suite);
                try { WorkroomClutterChecks.Run(); }
                catch (Exception exception) { failures.Add(suite + "\n" + exception); }
            }
            try { Contour4.NativeRaid.Editor.HazardFurnitureChecks.Run(); }
            catch (Exception exception) { failures.Add("hazards and closed cabinets\n" + exception); }
            File.WriteAllLines(Path.Combine(output, "failures.txt"), failures);
            Debug.Log("CONTOUR4 PREVIOUS RACK REGRESSIONS COMPLETED: 4 suites, " + failures.Count + " failures.");
            if (failures.Count != 0) throw new InvalidOperationException("Previous rack regression failures; see failures.txt.");
        }
    }
}
'@ | Set-Content -LiteralPath "$out/Unity/Assets/Contour4NativeRaid/Editor/PairRackPreviousChecks.cs" -Encoding utf8
$log = "$out/unity.log"
$project = "$out/Unity"
$arguments = @('-batchmode','-force-d3d11','-quit','-projectPath',('"' + $project + '"'),
    '-executeMethod','SpaceJourney.PairRackPreviousChecks.Run','-logFile',('"' + $log + '"'))
$environment = @{
    CONTOUR4_FURNITURE_OUTPUT = $out
    CONTOUR4_FURNITURE_EXPECT_OBSTRUCTIONS = '0'
    CONTOUR4_WORKROOM_EXPECT_CLUTTER = '1'
    CONTOUR4_WALL_OUTPUT = $out
    CONTOUR4_WALL_BUNDLES = $out
    CONTOUR4_WALL_ROOMS = 'both'
    CONTOUR4_WALL_EXPECT_FINISH = '1'
}
$editor = Start-Process $Unity -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -Environment $environment
if (!(Test-Path -LiteralPath $log)) { throw "Unity did not produce a log. Exit $($editor.ExitCode)." }
Select-String -LiteralPath $log -Pattern 'CONTOUR4 (PREVIOUS RACK|WORKROOM|REFERENCE WALLS|FURNITURE ACCESS|HAZARD FURNITURE|CABINETS|SHUTTER)|error CS\d+|Exception:|return code' |
    Select-Object -Last 22 -ExpandProperty Line
if ($editor.ExitCode -ne 0) { exit $editor.ExitCode }
if (!(Select-String -LiteralPath $log -SimpleMatch 'CONTOUR4 PREVIOUS RACK REGRESSIONS COMPLETED' -Quiet)) { throw 'Previous rack regressions did not complete.' }

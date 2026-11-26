param(
    [Parameter(Mandatory)] [string] $Baseline,
    [Parameter(Mandatory)] [string] $Candidate,
    [Parameter(Mandatory)] [string] $Output,
    [ValidateRange(1,36)] [int] $Layouts = 36,
    [switch] $InspectionLight,
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path "$PSScriptRoot/../..").Path
$Output = [IO.Path]::GetFullPath($Output)
$native = "$source/prototype/procedural/NativeRaid"
$ring = "$source/prototype/ring"
if (!(Test-Path "$Output/Unity")) {
    & "$native/Test-NativeRaid.ps1" -Unity $unity -Output $Output -PrepareOnly
}
$project = "$Output/Unity"
$assets = "$project/Assets/Contour4NativeRaid"
Copy-Item "$ring/Plugin/ProceduralRaidMap.cs", "$ring/Plugin/RoomPrefabBundle.cs", "$ring/Editor/ReceivingPrefabChecks.cs", `
    "$ring/Editor/PreparedModulePackageChecks.cs", "$native/Editor/PreparedModules/SphericalChamberChecks.cs", `
    "$native/Editor/PreparedModules/MobiusHallChecks.cs", "$source/release/tests/RoomSurfaceWearChecks.cs" -Destination "$assets/Editor"
Copy-Item "$native/Build/NativeRaidStartBay.cs" -Destination $assets
'-langversion:10.0' | Set-Content "$project/Assets/csc.rsp" -Encoding ascii
@'
using UnityEngine;
public sealed class StartRoom : MonoBehaviour {}
public sealed class LevelGenerator : MonoBehaviour
{
    public GameObject LevelParent;
    public WearTestLevel Level;
}
public sealed class WearTestLevel
{
    public WearTestConnectObject ConnectObject;
}
public sealed class WearTestConnectObject
{
    public GameObject Prefab;
}
'@ | Set-Content "$assets/WearStartGameStubs.cs" -Encoding utf8
$manifest = Get-Content "$project/Packages/manifest.json" -Raw | ConvertFrom-Json -AsHashtable
foreach ($module in 'assetbundle','imageconversion','jsonserialize') { $manifest.dependencies["com.unity.modules.$module"] = '1.0.0' }
$manifest | ConvertTo-Json -Depth 4 | Set-Content "$project/Packages/manifest.json" -Encoding utf8

function Expand-Bundle($Package, $Name, $Target) {
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $Package).Path)
    try {
        $entry = @($archive.Entries | Where-Object FullName -CEQ "plugins/Contour4/$Name")
        if ($entry.Count -ne 1) { throw "$Package/$Name has $($entry.Count) entries" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry[0], $Target, $true)
    } finally { $archive.Dispose() }
}
$prepared = "$Output/prepared.bundle"
$basePrepared = "$Output/prepared-base.bundle"
$rooms = "$Output/rooms.bundle"
$baseRooms = "$Output/rooms-base.bundle"
$ringBundle = "$Output/ring.bundle"
$baseRing = "$Output/ring-base.bundle"
Expand-Bundle $Candidate 'contour4-prepared-modules.bundle' $prepared
Expand-Bundle $baseline 'contour4-prepared-modules.bundle' $basePrepared
Expand-Bundle $Candidate 'contour4-rooms.bundle' $rooms
Expand-Bundle $baseline 'contour4-rooms.bundle' $baseRooms
Expand-Bundle $Candidate 'contour4-ring.bundle' $ringBundle
Expand-Bundle $baseline 'contour4-ring.bundle' $baseRing
if ((Get-FileHash $prepared).Hash -ne (Get-FileHash $basePrepared).Hash) { throw 'Prepared bundles differ' }
if ((Get-FileHash $ringBundle).Hash -ne (Get-FileHash $baseRing).Hash) { throw 'Ring bundles differ' }

$log = "$Output/unity-visual.log"
$inspect = if ($InspectionLight) { '1' } else { '0' }
$argsList = @('-batchmode', '-force-d3d11', '-quit', '-projectPath', ('"' + $project + '"'), `
    '-executeMethod', 'SpaceJourney.RoomSurfaceWearChecks.Run', '-logFile', ('"' + $log + '"'))
$editor = Start-Process $unity -ArgumentList $argsList -Wait -PassThru -WindowStyle Hidden `
    -Environment @{CONTOUR4_WEAR_EVIDENCE=$Output; CONTOUR4_PREPARED_BUNDLE=$prepared; CONTOUR4_RING_BUNDLE=$ringBundle; CONTOUR4_WEAR_LAYOUTS=$Layouts; `
        CONTOUR4_BASELINE_ROOMS_BUNDLE=$baseRooms; CONTOUR4_ROOMS_BUNDLE=$rooms; CONTOUR4_WEAR_INSPECTION_LIGHT=$inspect}
Select-String $log -Pattern 'CONTOUR4 WEAR|shader error|error CS\d+|Exception|return code' | Select-Object -Last 24
exit $editor.ExitCode

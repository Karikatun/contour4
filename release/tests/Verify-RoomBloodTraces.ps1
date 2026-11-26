param(
    [Parameter(Mandatory)] [string] $Baseline,
    [Parameter(Mandatory)] [string] $Candidate,
    [Parameter(Mandatory)] [string] $Output,
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path "$PSScriptRoot/../..").Path
$native = "$source/prototype/procedural/NativeRaid"
$ring = "$source/prototype/ring"
$Output = [IO.Path]::GetFullPath($Output)
& "$native/Test-NativeRaid.ps1" -Unity $Unity -Output $Output -PrepareOnly
$project = "$Output/Unity"
$assets = "$project/Assets/Contour4NativeRaid"
Copy-Item "$ring/Plugin/RoomPrefabBundle.cs", "$PSScriptRoot/RoomBloodTraceChecks.cs" -Destination "$assets/Editor"
$manifest = Get-Content "$project/Packages/manifest.json" -Raw | ConvertFrom-Json -AsHashtable
foreach ($module in 'assetbundle','imageconversion','jsonserialize') { $manifest.dependencies["com.unity.modules.$module"] = '1.0.0' }
$manifest | ConvertTo-Json -Depth 4 | Set-Content "$project/Packages/manifest.json" -Encoding utf8

function Expand-Bundle($Package, $Name, $Target) {
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $Package).Path)
    try {
        $entry = @($zip.Entries | Where-Object FullName -CEQ "plugins/Contour4/$Name")
        if ($entry.Count -ne 1) { throw "$Package/$Name has $($entry.Count) entries" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry[0], $Target, $true)
    } finally { $zip.Dispose() }
}
$prepared = "$Output/prepared.bundle"
$basePrepared = "$Output/prepared-base.bundle"
$rooms = "$Output/rooms.bundle"
$baseRooms = "$Output/rooms-base.bundle"
Expand-Bundle $Candidate 'contour4-prepared-modules.bundle' $prepared
Expand-Bundle $Baseline 'contour4-prepared-modules.bundle' $basePrepared
Expand-Bundle $Candidate 'contour4-rooms.bundle' $rooms
Expand-Bundle $Baseline 'contour4-rooms.bundle' $baseRooms
if ((Get-FileHash $prepared).Hash -ne (Get-FileHash $basePrepared).Hash) { throw 'Prepared bundles differ' }

$log = "$Output/unity-visual.log"
$arguments = @('-batchmode', '-force-d3d11', '-quit', '-projectPath', ('"' + $project + '"'), `
    '-executeMethod', 'SpaceJourney.RoomBloodTraceChecks.Run', '-logFile', ('"' + $log + '"'))
$editor = Start-Process $Unity -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden `
    -Environment @{CONTOUR4_TRACE_EVIDENCE=$Output; CONTOUR4_PREPARED_BUNDLE=$prepared; `
        CONTOUR4_BASELINE_ROOMS_BUNDLE=$baseRooms; CONTOUR4_ROOMS_BUNDLE=$rooms}
Select-String $log -Pattern 'CONTOUR4 TRACES|shader error|error CS\d+|Exception|return code' | Select-Object -Last 24
exit $editor.ExitCode

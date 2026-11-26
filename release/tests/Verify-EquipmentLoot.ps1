param(
    [Parameter(Mandatory)] [string] $Baseline,
    [Parameter(Mandatory)] [string] $BaselineSource,
    [Parameter(Mandatory)] [string] $Candidate,
    [Parameter(Mandatory)] [string] $Output,
    [string] $WarmPackageCache,
    [ValidateSet('Both','Before','After')] [string] $Stage = 'Both',
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path "$PSScriptRoot/../..").Path
$BaselineSource = (Resolve-Path $BaselineSource).Path
$Output = [IO.Path]::GetFullPath($Output)

function Expand-Bundle($Package, $Name, $Target) {
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $Package).Path)
    try {
        $entry = @($archive.Entries | Where-Object FullName -CEQ "plugins/Contour4/$Name")
        if ($entry.Count -ne 1) { throw "Expected one $Name in $Package" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry[0], $Target, $true)
    } finally { $archive.Dispose() }
}

function Measure-Stage([string] $Label, [string] $StageSource, [string] $Package) {
    $target = "$Output/$Label"
    $native = "$StageSource/prototype/procedural/NativeRaid"
    & "$native/Test-NativeRaid.ps1" -Unity $Unity -Output $target -PrepareOnly
    $project = "$target/Unity"
    $assets = "$project/Assets/Contour4NativeRaid"
    Copy-Item "$StageSource/prototype/ring/Plugin/RoomPrefabBundle.cs" -Destination "$assets/Editor"
    $checks = Get-Content -LiteralPath "$PSScriptRoot/RoomEquipmentLootChecks.cs" -Raw
    if ($Label -eq 'after') {
        $checks = "#define CONTOUR4_EQUIPMENT_TEST`n$checks"
        Copy-Item "$native/Build/RaidLootPrefab.cs" -Destination $assets
        '-langversion:10.0' | Set-Content "$project/Assets/csc.rsp" -Encoding utf8
    }
    $checks | Set-Content "$assets/Editor/RoomEquipmentLootChecks.cs" -Encoding utf8
    $manifest = Get-Content "$project/Packages/manifest.json" -Raw | ConvertFrom-Json -AsHashtable
    foreach ($module in 'assetbundle','imageconversion','jsonserialize') { $manifest.dependencies["com.unity.modules.$module"] = '1.0.0' }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content "$project/Packages/manifest.json" -Encoding utf8
    if ($WarmPackageCache -and !(Test-Path "$project/Library/PackageCache")) {
        New-Item -ItemType Directory "$project/Library" -Force | Out-Null
        Copy-Item -LiteralPath (Resolve-Path $WarmPackageCache).Path -Destination "$project/Library/PackageCache" -Recurse
    }
    $prepared = "$target/prepared.bundle"
    $rooms = "$target/rooms.bundle"
    Expand-Bundle $Package 'contour4-prepared-modules.bundle' $prepared
    Expand-Bundle $Package 'contour4-rooms.bundle' $rooms
    $log = "$target/unity-equipment-loot.log"
    $args = @('-batchmode','-force-d3d11','-quit','-projectPath',('"' + $project + '"'),
        '-executeMethod','SpaceJourney.RoomEquipmentLootChecks.Run','-logFile',('"' + $log + '"'))
    $editor = Start-Process $Unity -ArgumentList $args -PassThru -WindowStyle Hidden `
        -Environment @{CONTOUR4_EQUIPMENT_LOOT_OUTPUT=$target; CONTOUR4_PREPARED_BUNDLE=$prepared; CONTOUR4_ROOMS_BUNDLE=$rooms}
    $editor.WaitForExit()
    Select-String -LiteralPath $log -Pattern 'CONTOUR4 EQUIPMENT LOOT|error CS\d+|Exception|return code' |
        Select-Object -Last 24 -ExpandProperty Line
    if ($editor.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'CONTOUR4 EQUIPMENT LOOT PASSED' -SimpleMatch -Quiet)) {
        throw "Unity failed the $Label witness (exit $($editor.ExitCode)). See $log"
    }
}

if ($Stage -in 'Before','Both') { Measure-Stage 'before' $BaselineSource $Baseline }
if ($Stage -in 'After','Both') { Measure-Stage 'after' $source $Candidate }
if ($Stage -eq 'Both') {
    foreach ($name in 'rooms.bundle','prepared.bundle','parity.txt') {
        if ((Get-FileHash "$Output/before/$name").Hash -ne (Get-FileHash "$Output/after/$name").Hash) { throw "$name differs" }
    }
    'Bundles, room placement, lighting, ordinary loot sites and sampled routes match.'
}

param(
    [Parameter(Mandatory)] [string] $Baseline,
    [Parameter(Mandatory)] [string] $BaselineSource,
    [Parameter(Mandatory)] [string] $Candidate,
    [Parameter(Mandatory)] [string] $Output,
    [string] $WarmPackageCache,
    [switch] $BothHaveMaterialFamilies,
    [ValidateSet('Both','Before','After')] [string] $Stage = 'Both',
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path "$PSScriptRoot/../..").Path
$BaselineSource = (Resolve-Path $BaselineSource).Path
$Output = [IO.Path]::GetFullPath($Output)
$project = "$Output/Unity"

function Expand-Bundle($Package, $Name, $Target) {
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $Package).Path)
    try {
        $entry = @($archive.Entries | Where-Object FullName -CEQ "plugins/Contour4/$Name")
        if ($entry.Count -ne 1) { throw "$Package/$Name has $($entry.Count) entries" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry[0], $Target, $true)
    } finally { $archive.Dispose() }
}

function Measure-Stage([string] $Label, [string] $StageSource, [string] $Package, [string] $IsCandidate) {
    $native = "$StageSource/prototype/procedural/NativeRaid"
    & "$native/Test-NativeRaid.ps1" -Unity $Unity -Output $Output -PrepareOnly
    $assets = "$project/Assets/Contour4NativeRaid"
    Copy-Item "$StageSource/prototype/ring/Plugin/RoomPrefabBundle.cs", "$PSScriptRoot/ReadableMaterialChecks.cs" -Destination "$assets/Editor"
    $manifest = Get-Content "$project/Packages/manifest.json" -Raw | ConvertFrom-Json -AsHashtable
    foreach ($module in 'assetbundle','imageconversion','jsonserialize') { $manifest.dependencies["com.unity.modules.$module"] = '1.0.0' }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content "$project/Packages/manifest.json" -Encoding utf8
    if ($WarmPackageCache -and !(Test-Path "$project/Library/PackageCache")) {
        New-Item -ItemType Directory "$project/Library" -Force | Out-Null
        Copy-Item -LiteralPath (Resolve-Path $WarmPackageCache).Path -Destination "$project/Library/PackageCache" -Recurse
    }
    $target = "$Output/$Label"
    New-Item -ItemType Directory $target -Force | Out-Null
    $prepared = "$target/prepared.bundle"
    $rooms = "$target/rooms.bundle"
    Expand-Bundle $Package 'contour4-prepared-modules.bundle' $prepared
    Expand-Bundle $Package 'contour4-rooms.bundle' $rooms
    $log = "$target/unity-visual.log"
    $argumentList = @('-batchmode','-force-d3d11','-quit','-projectPath',('"' + $project + '"'), `
        '-executeMethod','SpaceJourney.ReadableMaterialChecks.Run','-logFile',('"' + $log + '"'))
    $editor = Start-Process $Unity -ArgumentList $argumentList -PassThru -WindowStyle Hidden `
        -Environment @{CONTOUR4_SURFACE_EVIDENCE=$target; CONTOUR4_PREPARED_BUNDLE=$prepared; `
            CONTOUR4_SURFACE_CANDIDATE=$IsCandidate; CONTOUR4_ROOMS_BUNDLE=$rooms}
    $editor.WaitForExit()
    Select-String $log -Pattern 'CONTOUR4 READABLE MATERIALS|shader error|error CS\d+|Exception|return code' |
        Select-Object -Last 24
    if ($editor.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'CONTOUR4 READABLE MATERIALS PASSED' -SimpleMatch -Quiet)) {
        throw "Unity failed the $Label witness (exit $($editor.ExitCode)). See $log"
    }
}

if ($Stage -in 'Before','Both') { Measure-Stage 'before' $BaselineSource $Baseline $(if ($BothHaveMaterialFamilies) { '1' } else { '0' }) }
if ($Stage -in 'After','Both') { Measure-Stage 'after' $source $Candidate '1' }
if ($Stage -eq 'Both') {
    foreach ($name in 'prepared.bundle','scene-signatures.tsv') {
        if ((Get-FileHash "$Output/before/$name").Hash -ne (Get-FileHash "$Output/after/$name").Hash) { throw "$name differs" }
    }
    if ($BothHaveMaterialFamilies -and
        (Get-FileHash "$Output/before/material-inventory.tsv").Hash -ne (Get-FileHash "$Output/after/material-inventory.tsv").Hash) {
        throw 'The assigned material families differ.'
    }
    'Exact scene signatures and the prepared module bundle match.'
}

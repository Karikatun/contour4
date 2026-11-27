param(
    [Parameter(Mandatory)] [string] $MapPackage,
    [Parameter(Mandatory)] [string] $Output,
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe',
    [switch] $ExtractionBridges
)

$ErrorActionPreference = 'Stop'
$outPath = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $outPath) { throw 'Choose a new verification output directory.' }
$native = Join-Path $PSScriptRoot '../procedural/NativeRaid'
& "$native/Test-NativeRaid.ps1" -Unity $Unity -Output $outPath -PrepareOnly
$project = Join-Path $outPath 'Unity'
$assets = Join-Path $project 'Assets/Contour4NativeRaid'
$log = Join-Path $outPath 'unity-prepared-package.log'
Copy-Item -LiteralPath "$PSScriptRoot/Plugin/ProceduralRaidMap.cs","$PSScriptRoot/Plugin/RoomPrefabBundle.cs",`
    "$PSScriptRoot/Editor/ReceivingPrefabChecks.cs","$PSScriptRoot/Editor/PreparedModulePackageChecks.cs",`
    "$native/Editor/PreparedModules/SphericalChamberChecks.cs","$native/Editor/PreparedModules/MobiusHallChecks.cs" -Destination "$assets/Editor"
$manifestPath = Join-Path $project 'Packages/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
foreach ($module in 'assetbundle','imageconversion','jsonserialize') { $manifest.dependencies["com.unity.modules.$module"] = '1.0.0' }
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8

$package = (Resolve-Path -LiteralPath $MapPackage).Path
$receiving = Join-Path $outPath 'contour4-rooms.bundle'
$prepared = Join-Path $outPath 'contour4-prepared-modules.bundle'
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    foreach ($file in $receiving,$prepared) {
        $name = [IO.Path]::GetFileName($file)
        $entry = @($archive.Entries | Where-Object FullName -CEQ "plugins/Contour4/$name")
        if ($entry.Count -ne 1 -or $entry[0].Length -le 0) { throw "Expected exactly one nonempty archive entry for $name." }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry[0], $file)
    }
} finally { $archive.Dispose() }
Get-FileHash -LiteralPath $package,$receiving,$prepared -Algorithm SHA256

$method = if ($ExtractionBridges) { 'RunExtractionBridges' } else { 'Run' }
$success = if ($ExtractionBridges) { 'CONTOUR4 NATIVE RAID EXTRACTION BRIDGES PASSED' } else { 'CONTOUR4 PREPARED PACKAGE PASSED' }
$unityArgs = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'),
    '-executeMethod', "SpaceJourney.PreparedModulePackageChecks.$method", '-logFile', ('"' + $log + '"'))
$editor = Start-Process -FilePath $Unity -ArgumentList $unityArgs -Wait -PassThru -WindowStyle Hidden `
    -Environment @{ CONTOUR4_ROOMS_BUNDLE = $receiving; CONTOUR4_PREPARED_BUNDLE = $prepared;
        CONTOUR4_PREPARED_EVIDENCE = $outPath }
if (!(Test-Path -LiteralPath $log -PathType Leaf)) { throw "Unity produced no log. Exit $($editor.ExitCode)." }
Select-String -LiteralPath $log -Pattern 'CONTOUR4 (PREPARED PACKAGE (RAID|NAV|TAB|FIELD|PHYSICAL NETWORK|COMPOSITION DOCKS|SPHERE|PASSED)|SPHERE PHYSICS|PREPARED MOBIUS|NATIVE RAID extraction bridges)|error CS\d+|Exception:|return code|No valid Unity Editor license' |
    Select-Object -Last 65 -ExpandProperty Line
if ($editor.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -SimpleMatch $success -Quiet)) {
    throw "Prepared package verification failed. See $log"
}
"Offline Unity evidence: $log"

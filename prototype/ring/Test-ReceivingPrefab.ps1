[CmdletBinding(DefaultParameterSetName='Bundle')]
param(
    [Parameter(Mandatory, ParameterSetName='Archive')] [string] $MapPackage,
    [Parameter(Mandatory, ParameterSetName='Bundle')] [string] $RoomsBundle,
    [Parameter(ParameterSetName='Bundle')] [string] $PreparedModulesBundle,
    [Parameter(Mandatory)] [string] $Output,
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe'
)

$ErrorActionPreference = 'Stop'
$outPath = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $outPath) { throw 'Choose a new verification output directory.' }
$native = Join-Path $PSScriptRoot '../procedural/NativeRaid'
& "$native/Test-NativeRaid.ps1" -Unity $Unity -Output $outPath -PrepareOnly
$project = Join-Path $outPath 'Unity'
$assets = Join-Path $project 'Assets/Contour4NativeRaid'
$log = Join-Path $outPath 'unity-receiving.log'
$map = Join-Path $outPath 'tab-projection.png'
Copy-Item -LiteralPath "$PSScriptRoot/Plugin/ProceduralRaidMap.cs","$PSScriptRoot/Plugin/RoomPrefabBundle.cs",`
    "$PSScriptRoot/Editor/ReceivingPrefabChecks.cs" -Destination "$assets/Editor"
$manifestPath = Join-Path $project 'Packages/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
$manifest.dependencies['com.unity.modules.assetbundle'] = '1.0.0'
$manifest.dependencies['com.unity.modules.imageconversion'] = '1.0.0'
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8

if ($MapPackage) {
    $RoomsBundle = Join-Path $outPath 'contour4-rooms.bundle'
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $MapPackage).Path)
    try {
        $entry = $archive.GetEntry('plugins/Contour4/contour4-rooms.bundle')
        if (!$entry -or $entry.Length -le 0) { throw 'The package has no authored rooms bundle.' }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $RoomsBundle)
        $prepared = $archive.GetEntry('plugins/Contour4/contour4-prepared-modules.bundle')
        if ($prepared) {
            if ($prepared.Length -le 0) { throw 'The package has an empty prepared modules bundle.' }
            $PreparedModulesBundle = Join-Path $outPath $prepared.Name
            [IO.Compression.ZipFileExtensions]::ExtractToFile($prepared, $PreparedModulesBundle)
        }
    } finally { $archive.Dispose() }
    Get-FileHash -LiteralPath $MapPackage -Algorithm SHA256
}
$bundlePath = (Resolve-Path -LiteralPath $RoomsBundle).Path
Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256
$preparedPath = if ($PreparedModulesBundle) { (Resolve-Path -LiteralPath $PreparedModulesBundle).Path }
if ($preparedPath) { Get-FileHash -LiteralPath $preparedPath -Algorithm SHA256 }

$unityArgs = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'),
    '-executeMethod', 'SpaceJourney.ReceivingPrefabChecks.Run', '-logFile', ('"' + $log + '"'))
$editor = Start-Process -FilePath $Unity -ArgumentList $unityArgs -Wait -PassThru -WindowStyle Hidden `
    -Environment @{ CONTOUR4_ROOMS_BUNDLE = $bundlePath; CONTOUR4_PREPARED_BUNDLE = $preparedPath;
        CONTOUR4_MAP_PROJECTION = $map }
if (!(Test-Path -LiteralPath $log -PathType Leaf)) { throw "Unity produced no log. Exit $($editor.ExitCode)." }
Select-String -LiteralPath $log -Pattern 'CONTOUR4 RECEIVING PREFAB|error CS\d+|Exception:|return code|No valid Unity Editor license' |
    Select-Object -Last 35 -ExpandProperty Line
if ($editor.ExitCode -ne 0 -or !(Test-Path -LiteralPath $map -PathType Leaf) -or
    !(Select-String -LiteralPath $log -SimpleMatch 'CONTOUR4 RECEIVING PREFAB PASSED' -Quiet)) {
    throw "Authored receiving verification failed. See $log"
}
"Offline Unity evidence: $log"

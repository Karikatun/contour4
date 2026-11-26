param(
    [Parameter(Mandatory)] [string] $Package,
    [Parameter(Mandatory)] [string] $Output,
    [string] $EditionSource = "$PSScriptRoot/../..",
    [string] $WarmPackageCache,
    [switch] $ReuseRuntime
)
$ErrorActionPreference = 'Stop'
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe'
$source = (Resolve-Path $EditionSource).Path
$native = "$source/prototype/procedural/NativeRaid"
$out = [IO.Path]::GetFullPath($Output)
$project = "$out/Unity"
$assets = "$project/Assets/Contour4NativeRaid"
if (!$ReuseRuntime) { & "$native/Test-NativeRaid.ps1" -Unity $unity -Output $out -PrepareOnly }
Copy-Item "$source/prototype/ring/Plugin/RoomPrefabBundle.cs", "$PSScriptRoot/MaterialCoverageChecks.cs" -Destination "$assets/Editor"
$start = Get-Content "$native/Build/NativeRaidStartBay.cs" -Raw
$begin = $start.IndexOf('    internal static void Install(')
$end = $start.IndexOf('    private void Build(')
if ($begin -lt 0 -or $end -le $begin) { throw 'Could not find the stock attachment interval' }
# Unity fixture uses the actual authored factory. Omit only the game API adapter that
# finds the stock starter/truck hierarchy at runtime.
if (!$ReuseRuntime) { $start.Remove($begin,$end-$begin) | Set-Content "$assets/NativeRaidStartBay.cs" -Encoding utf8 }
'-langversion:10.0' | Set-Content "$project/Assets/csc.rsp" -Encoding utf8
$manifest = Get-Content "$project/Packages/manifest.json" -Raw | ConvertFrom-Json -AsHashtable
foreach ($module in 'assetbundle','imageconversion','jsonserialize') { $manifest.dependencies["com.unity.modules.$module"] = '1.0.0' }
$manifest | ConvertTo-Json -Depth 4 | Set-Content "$project/Packages/manifest.json" -Encoding utf8
if ($WarmPackageCache -and !(Test-Path "$project/Library/PackageCache")) {
    New-Item -ItemType Directory "$project/Library" -Force | Out-Null
    Copy-Item -LiteralPath (Resolve-Path $WarmPackageCache).Path -Destination "$project/Library/PackageCache" -Recurse
}
$loaded = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $Package).Path)
try {
    foreach ($bundle in 'contour4-rooms.bundle','contour4-prepared-modules.bundle','contour4-ring.bundle') {
        $entry = $loaded.GetEntry("plugins/Contour4/$bundle")
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,"$out/$bundle",$true)
    }
} finally { $loaded.Dispose() }
$log = "$out/unity.log"
$arguments = @('-batchmode','-force-d3d11','-quit','-projectPath',('"' + $project + '"'),
    '-executeMethod','SpaceJourney.MaterialCoverageChecks.Run','-logFile',('"' + $log + '"'))
$environment = @{
    CONTOUR4_COVERAGE_OUTPUT=$out
    CONTOUR4_ROOMS_BUNDLE="$out/contour4-rooms.bundle"
    CONTOUR4_PREPARED_BUNDLE="$out/contour4-prepared-modules.bundle"
    CONTOUR4_COVERAGE_RING="$out/contour4-ring.bundle"
}
$editor = Start-Process $unity -ArgumentList $arguments -PassThru -WindowStyle Hidden -Environment $environment
$editor.WaitForExit()
Select-String -LiteralPath $log -Pattern 'CONTOUR4 MATERIAL COVERAGE|error CS\d+|Exception|return code' | Select-Object -Last 20
if ($editor.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'CONTOUR4 MATERIAL COVERAGE PASSED' -SimpleMatch -Quiet)) {
    throw "Coverage failed for $out"
}


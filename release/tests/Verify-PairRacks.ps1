param(
    [Parameter(Mandatory)] [string] $SourceArchive,
    [Parameter(Mandatory)] [string] $Package,
    [Parameter(Mandatory)] [string] $Output,
    [string] $WarmPackageCache,
    [switch] $Baseline,
    [switch] $PrepareOnly,
    [switch] $SkipRenders,
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$sourceZip = (Resolve-Path -LiteralPath $SourceArchive).Path
$packageZip = (Resolve-Path -LiteralPath $Package).Path
$out = [IO.Path]::GetFullPath($Output)
$source = "$out/source"
$project = "$out/Unity"
$assets = "$project/Assets/Contour4NativeRaid"
if (!(Test-Path "$source/prototype/procedural/NativeRaid/Test-NativeRaid.ps1")) {
    Expand-Archive -LiteralPath $sourceZip -DestinationPath $source
}
& "$source/prototype/procedural/NativeRaid/Test-NativeRaid.ps1" -Unity $Unity -Output $out -PrepareOnly
Copy-Item "$source/prototype/ring/Plugin/RoomPrefabBundle.cs", "$PSScriptRoot/PairRackChecks.cs" -Destination "$assets/Editor"
Copy-Item "$source/prototype/procedural/NativeRaid/Build/RaidLootPrefab.cs" -Destination $assets
'-langversion:10.0' | Set-Content "$project/Assets/csc.rsp" -Encoding utf8
$manifest = Get-Content "$project/Packages/manifest.json" -Raw | ConvertFrom-Json -AsHashtable
foreach ($module in 'assetbundle','imageconversion','jsonserialize') { $manifest.dependencies["com.unity.modules.$module"] = '1.0.0' }
$manifest | ConvertTo-Json -Depth 4 | Set-Content "$project/Packages/manifest.json" -Encoding utf8
if ($WarmPackageCache -and !(Test-Path -LiteralPath "$project/Library/PackageCache")) {
    New-Item -ItemType Directory "$project/Library" -Force | Out-Null
    Copy-Item -LiteralPath (Resolve-Path -LiteralPath $WarmPackageCache).Path -Destination "$project/Library/PackageCache" -Recurse
}
$inputs = [ordered]@{ sourceArchive = (Get-FileHash -LiteralPath $sourceZip).Hash; package = (Get-FileHash -LiteralPath $packageZip).Hash;
    checks = (Get-FileHash -LiteralPath "$PSScriptRoot/PairRackChecks.cs").Hash;
    editor = $Unity; baseline = $Baseline.IsPresent; renders = !$SkipRenders.IsPresent }
$zip = [IO.Compression.ZipFile]::OpenRead($packageZip)
try {
    foreach ($name in 'contour4-rooms.bundle','contour4-prepared-modules.bundle') {
        $entry = $zip.GetEntry("plugins/Contour4/$name")
        if (!$entry) { throw "Missing $name in $packageZip" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "$out/$name", $true)
        $inputs[$name] = (Get-FileHash -LiteralPath "$out/$name").Hash
    }
} finally { $zip.Dispose() }
$inputs | ConvertTo-Json | Set-Content -LiteralPath "$out/inputs.json" -Encoding utf8
if ($PrepareOnly) { return }
$log = "$out/unity.log"
$arguments = @('-batchmode','-force-d3d11','-quit','-projectPath',('"' + $project + '"'),
    '-executeMethod','SpaceJourney.PairRackChecks.Run','-logFile',('"' + $log + '"'))
$environment = @{ CONTOUR4_PAIR_RACK_OUTPUT = $out; CONTOUR4_PAIR_RACK_BASELINE = [string]([int]$Baseline.IsPresent);
    CONTOUR4_PAIR_RACK_RENDERS = [string]([int](!$SkipRenders.IsPresent)) }
$editor = Start-Process $Unity -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -Environment $environment
$inputs.editorExit = $editor.ExitCode
if (Test-Path -LiteralPath $log) {
    Select-String -LiteralPath $log -Pattern 'CONTOUR4 PAIR RACK|error CS\d+|Exception:|return code|No valid Unity Editor license' |
        Select-Object -Last 25 -ExpandProperty Line
    $inputs.log = (Get-FileHash -LiteralPath $log).Hash
}
$inputs.outputs = @(Get-ChildItem -LiteralPath $out -File -Filter '*.tsv' | Get-FileHash | ForEach-Object {
    [ordered]@{ file = [IO.Path]::GetFileName($_.Path); sha256 = $_.Hash }
})
$inputs | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$out/receipt.json" -Encoding utf8
if ($editor.ExitCode -ne 0) { exit $editor.ExitCode }
if (!(Select-String -LiteralPath $log -SimpleMatch 'CONTOUR4 PAIR RACK CHECK COMPLETED' -Quiet)) { throw 'Pair rack check did not complete.' }

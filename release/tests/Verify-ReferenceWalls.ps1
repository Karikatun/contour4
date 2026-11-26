param(
    [Parameter(Mandatory)] [string] $EditionSource,
    [Parameter(Mandatory)] [string] $BundleRoot,
    [Parameter(Mandatory)] [string] $Output,
    [string] $WarmPackageCache,
    [ValidateSet('receiving','workshop','both')] [string] $Rooms = 'both',
    [switch] $ExpectFinish,
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $EditionSource).Path
$bundles = (Resolve-Path -LiteralPath $BundleRoot).Path
$out = [IO.Path]::GetFullPath($Output)
$project = "$out/Unity"
$assets = "$project/Assets/Contour4NativeRaid"
& "$source/prototype/procedural/NativeRaid/Test-NativeRaid.ps1" -Unity $Unity -Output $out -PrepareOnly
Copy-Item "$source/prototype/ring/Plugin/RoomPrefabBundle.cs", "$PSScriptRoot/ReferenceWallChecks.cs" -Destination "$assets/Editor"
'-langversion:10.0' | Set-Content "$project/Assets/csc.rsp" -Encoding utf8
$manifest = Get-Content "$project/Packages/manifest.json" -Raw | ConvertFrom-Json -AsHashtable
foreach ($module in 'assetbundle','imageconversion','jsonserialize') { $manifest.dependencies["com.unity.modules.$module"] = '1.0.0' }
$manifest | ConvertTo-Json -Depth 4 | Set-Content "$project/Packages/manifest.json" -Encoding utf8
if ($WarmPackageCache -and !(Test-Path -LiteralPath "$project/Library/PackageCache")) {
    New-Item -ItemType Directory "$project/Library" -Force | Out-Null
    Copy-Item -LiteralPath (Resolve-Path -LiteralPath $WarmPackageCache).Path -Destination "$project/Library/PackageCache" -Recurse
}
$log = "$out/unity.log"
$arguments = @('-batchmode','-force-d3d11','-quit','-projectPath',('"' + $project + '"'),
    '-executeMethod','SpaceJourney.ReferenceWallChecks.Run','-logFile',('"' + $log + '"'))
$environment = @{
    CONTOUR4_WALL_OUTPUT = $out
    CONTOUR4_WALL_BUNDLES = $bundles
    CONTOUR4_WALL_ROOMS = $Rooms
    CONTOUR4_WALL_EXPECT_FINISH = [string]([int]$ExpectFinish.IsPresent)
}
$editor = Start-Process $Unity -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -Environment $environment
if (!(Test-Path -LiteralPath $log)) { throw "Unity did not produce a log. Exit $($editor.ExitCode)." }
Select-String -LiteralPath $log -Pattern 'CONTOUR4 REFERENCE WALLS|error CS\d+|Exception:|return code' |
    Select-Object -Last 20 -ExpandProperty Line
if ($editor.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -SimpleMatch 'CONTOUR4 REFERENCE WALLS PASSED' -Quiet)) {
    throw "Wall check failed for $Rooms. See $log"
}
[ordered]@{
    roomsBundle = (Get-FileHash -LiteralPath "$bundles/contour4-rooms.bundle").Hash
    preparedBundle = (Get-FileHash -LiteralPath "$bundles/contour4-prepared-modules.bundle").Hash
    editionWear = (Get-FileHash -LiteralPath "$source/prototype/procedural/NativeRaid/Generation/RoomSurfaceWear.cs").Hash
    checkSource = (Get-FileHash -LiteralPath "$PSScriptRoot/ReferenceWallChecks.cs").Hash
    observations = (Get-FileHash -LiteralPath "$out/room-contract.tsv").Hash
    rooms = $Rooms
    expectedFinish = $ExpectFinish.IsPresent
    editorExit = $editor.ExitCode
} | ConvertTo-Json | Set-Content -LiteralPath "$out/receipt.json" -Encoding utf8

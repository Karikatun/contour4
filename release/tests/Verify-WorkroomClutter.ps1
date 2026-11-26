param(
    [Parameter(Mandatory)] [string] $EditionSource,
    [Parameter(Mandatory)] [string] $Package,
    [Parameter(Mandatory)] [string] $Output,
    [string] $WarmPackageCache,
    [ValidateSet('receiving','workshop','both','storage','dispatch')] [string] $Rooms = 'both',
    [switch] $ExpectClutter,
    [switch] $PrepareOnly,
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $EditionSource).Path
$packageFile = (Resolve-Path -LiteralPath $Package).Path
$out = [IO.Path]::GetFullPath($Output)
$project = "$out/Unity"
$assets = "$project/Assets/Contour4NativeRaid"
& "$source/prototype/procedural/NativeRaid/Test-NativeRaid.ps1" -Unity $Unity -Output $out -PrepareOnly
Copy-Item "$source/prototype/ring/Plugin/RoomPrefabBundle.cs", "$PSScriptRoot/WorkroomClutterChecks.cs",
    "$PSScriptRoot/StorageClutterChecks.cs", "$PSScriptRoot/DispatchClutterChecks.cs" -Destination "$assets/Editor"
Copy-Item "$source/prototype/procedural/NativeRaid/Build/RaidLootPrefab.cs" -Destination $assets
foreach ($check in @{
    FurnitureAccessChecks='CONTOUR4_WORKROOM_TEST'; ReferenceWallChecks='CONTOUR4_WORKROOM_TEST'
    RoomEquipmentLootChecks='CONTOUR4_EQUIPMENT_TEST'
}.GetEnumerator()) {
    $code = Get-Content -LiteralPath "$PSScriptRoot/$($check.Key).cs" -Raw
    "#define $($check.Value)`n$code" | Set-Content -LiteralPath "$assets/Editor/$($check.Key).cs" -Encoding utf8
}
'-langversion:10.0' | Set-Content "$project/Assets/csc.rsp" -Encoding utf8
$manifest = Get-Content "$project/Packages/manifest.json" -Raw | ConvertFrom-Json -AsHashtable
foreach ($module in 'assetbundle','imageconversion','jsonserialize') { $manifest.dependencies["com.unity.modules.$module"] = '1.0.0' }
$manifest | ConvertTo-Json -Depth 4 | Set-Content "$project/Packages/manifest.json" -Encoding utf8
if ($WarmPackageCache -and !(Test-Path -LiteralPath "$project/Library/PackageCache")) {
    New-Item -ItemType Directory "$project/Library" -Force | Out-Null
    Copy-Item -LiteralPath (Resolve-Path -LiteralPath $WarmPackageCache).Path -Destination "$project/Library/PackageCache" -Recurse
}
$zip = [IO.Compression.ZipFile]::OpenRead($packageFile)
try {
    foreach ($name in 'contour4-rooms.bundle','contour4-prepared-modules.bundle') {
        $entry = $zip.GetEntry("plugins/Contour4/$name")
        if (!$entry) { throw "Missing $name in $packageFile" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "$out/$name", $true)
    }
} finally { $zip.Dispose() }
$runtime = "$source/prototype/procedural/NativeRaid/Generation"
$sources = Get-ChildItem -LiteralPath $runtime -Filter 'Raid*Fitout*.cs' | Get-FileHash | Sort-Object Path
$receipt = [ordered]@{
    package = (Get-FileHash -LiteralPath $packageFile).Hash
    roomsBundle = (Get-FileHash -LiteralPath "$out/contour4-rooms.bundle").Hash
    preparedBundle = (Get-FileHash -LiteralPath "$out/contour4-prepared-modules.bundle").Hash
    editionFitout = @($sources | ForEach-Object { [ordered]@{ file = [IO.Path]::GetFileName($_.Path); sha256 = $_.Hash } })
    checks = @(('WorkroomClutterChecks','StorageClutterChecks','DispatchClutterChecks','FurnitureAccessChecks','ReferenceWallChecks','RoomEquipmentLootChecks') |
        ForEach-Object { [ordered]@{ file = "$_.cs"; sha256 = (Get-FileHash -LiteralPath "$PSScriptRoot/$_.cs").Hash } })
    rooms = $Rooms
    expectedClutter = $ExpectClutter.IsPresent
}
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$out/inputs.json" -Encoding utf8
if ($PrepareOnly) { return }

$log = "$out/unity.log"
$arguments = @('-batchmode','-force-d3d11','-quit','-projectPath',('"' + $project + '"'),
    '-executeMethod','SpaceJourney.WorkroomClutterChecks.Run','-logFile',('"' + $log + '"'))
$environment = @{
    CONTOUR4_FURNITURE_OUTPUT = $out
    CONTOUR4_FURNITURE_ROOMS = $Rooms
    CONTOUR4_FURNITURE_EXPECT_OBSTRUCTIONS = '0'
    CONTOUR4_WORKROOM_EXPECT_CLUTTER = [string]([int]$ExpectClutter.IsPresent)
    CONTOUR4_WALL_OUTPUT = $out
    CONTOUR4_WALL_BUNDLES = $out
    CONTOUR4_WALL_ROOMS = $Rooms
    CONTOUR4_WALL_EXPECT_FINISH = '1'
}
$editor = Start-Process $Unity -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -Environment $environment
if (!(Test-Path -LiteralPath $log)) { throw "Unity did not produce a log. Exit $($editor.ExitCode)." }
Select-String -LiteralPath $log -Pattern 'CONTOUR4 (WORKROOM CLUTTER|REFERENCE WALLS|FURNITURE ACCESS)|error CS\d+|Exception:|return code' |
    Select-Object -Last 20 -ExpandProperty Line
if ($editor.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -SimpleMatch 'CONTOUR4 WORKROOM CLUTTER PASSED' -Quiet)) {
    throw "Clutter check failed for $Rooms. See $log"
}
$observationFiles = switch ($Rooms) {
    storage { 'storage-contract.tsv' }
    dispatch { 'dispatch-contract.tsv' }
    default { 'furniture-access.tsv','room-contract.tsv' }
}
$receipt.observations = @($observationFiles | ForEach-Object {
    [ordered]@{ file = $_; sha256 = (Get-FileHash -LiteralPath "$out/$_").Hash }
})
$receipt.editorExit = $editor.ExitCode
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$out/receipt.json" -Encoding utf8

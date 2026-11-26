param(
    [Parameter(Mandatory)] [string] $Package,
    [Parameter(Mandatory)] [string] $Output,
    [string] $EditionSource = "$PSScriptRoot/../..",
    [string] $WarmPackageCache,
    [ValidateSet('receiving','workshop','both')] [string] $Rooms = 'both',
    [switch] $ExpectObstructions,
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path $EditionSource).Path
$out = [IO.Path]::GetFullPath($Output)
$project = "$out/Unity"
$assets = "$project/Assets/Contour4NativeRaid"
& "$source/prototype/procedural/NativeRaid/Test-NativeRaid.ps1" -Unity $Unity -Output $out -PrepareOnly
Copy-Item "$source/prototype/ring/Plugin/RoomPrefabBundle.cs", "$PSScriptRoot/FurnitureAccessChecks.cs" -Destination "$assets/Editor"
Copy-Item "$source/prototype/procedural/NativeRaid/Build/RaidLootPrefab.cs" -Destination $assets
$pocketChecks = Get-Content -LiteralPath "$PSScriptRoot/RoomEquipmentLootChecks.cs" -Raw
"#define CONTOUR4_EQUIPMENT_TEST`n$pocketChecks" | Set-Content -LiteralPath "$assets/Editor/RoomEquipmentLootChecks.cs" -Encoding utf8
'-langversion:10.0' | Set-Content "$project/Assets/csc.rsp" -Encoding utf8
$manifest = Get-Content "$project/Packages/manifest.json" -Raw | ConvertFrom-Json -AsHashtable
foreach ($module in 'assetbundle','imageconversion','jsonserialize') { $manifest.dependencies["com.unity.modules.$module"] = '1.0.0' }
$manifest | ConvertTo-Json -Depth 4 | Set-Content "$project/Packages/manifest.json" -Encoding utf8
if ($WarmPackageCache -and !(Test-Path "$project/Library/PackageCache")) {
    New-Item -ItemType Directory "$project/Library" -Force | Out-Null
    Copy-Item -LiteralPath (Resolve-Path $WarmPackageCache).Path -Destination "$project/Library/PackageCache" -Recurse
}
$packageFile = (Resolve-Path -LiteralPath $Package).Path
$zip = [IO.Compression.ZipFile]::OpenRead($packageFile)
try {
    foreach ($name in 'contour4-rooms.bundle','contour4-prepared-modules.bundle') {
        $entry = $zip.GetEntry("plugins/Contour4/$name")
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "$out/$name", $true)
    }
} finally { $zip.Dispose() }
$log = "$out/unity.log"
$arguments = @('-batchmode','-force-d3d11','-quit','-projectPath',('"' + $project + '"'),
    '-executeMethod','SpaceJourney.FurnitureAccessChecks.Run','-logFile',('"' + $log + '"'))
$environment = @{
    CONTOUR4_FURNITURE_OUTPUT = $out
    CONTOUR4_FURNITURE_ROOMS = $Rooms
    CONTOUR4_FURNITURE_EXPECT_OBSTRUCTIONS = [string]([int]$ExpectObstructions.IsPresent)
}
$editor = Start-Process $Unity -ArgumentList $arguments -PassThru -WindowStyle Hidden -Environment $environment
$editor.WaitForExit()
if (!(Test-Path -LiteralPath $log)) { throw "Unity did not produce a log. Exit $($editor.ExitCode)." }
Select-String -LiteralPath $log -Pattern 'CONTOUR4 FURNITURE|error CS\d+|Exception|return code' | Select-Object -Last 20 -ExpandProperty Line
if ($editor.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'CONTOUR4 FURNITURE ACCESS PASSED' -SimpleMatch -Quiet)) {
    throw "Furniture access failed for $Rooms. See $log"
}
[ordered]@{
    package = (Get-FileHash -LiteralPath $packageFile).Hash
    roomsBundle = (Get-FileHash -LiteralPath "$out/contour4-rooms.bundle").Hash
    preparedBundle = (Get-FileHash -LiteralPath "$out/contour4-prepared-modules.bundle").Hash
    editionFitout = (Get-FileHash -LiteralPath "$source/prototype/procedural/NativeRaid/Generation/RaidReferenceRoomFitout.cs").Hash
    editionServiceRoom = (Get-FileHash -LiteralPath "$source/prototype/procedural/NativeRaid/Generation/FreightServiceRoom.cs").Hash
    regressionSource = (Get-FileHash -LiteralPath "$PSScriptRoot/FurnitureAccessChecks.cs").Hash
    observations = (Get-FileHash -LiteralPath "$out/furniture-access.tsv").Hash
    rooms = $Rooms
    obstructionControl = $ExpectObstructions.IsPresent
    editorExit = $editor.ExitCode
} | ConvertTo-Json | Set-Content -LiteralPath "$out/receipt.json" -Encoding utf8

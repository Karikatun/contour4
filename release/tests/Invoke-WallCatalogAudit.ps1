param(
    [Parameter(Mandatory)] [string] $Package,
    [Parameter(Mandatory)] [string] $EditionSource,
    [Parameter(Mandatory)] [string] $Output,
    [string] $WarmPackageCache,
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $EditionSource).Path
$native = "$source/prototype/procedural/NativeRaid"
$out = [IO.Path]::GetFullPath($Output)
$project = "$out/Unity"
$assets = "$project/Assets/Contour4NativeRaid"
$fixture = "$PSScriptRoot/MaterialCoverageChecks.cs"
& "$native/Test-NativeRaid.ps1" -Unity $Unity -Output $out -PrepareOnly
Copy-Item "$source/prototype/ring/Plugin/RoomPrefabBundle.cs", "$PSScriptRoot/CatalogWallObservation.cs" -Destination "$assets/Editor"

# The stock installer discovers the external truck/starter through game APIs.
# Keep the exact local authored factory and leave that external adapter out of the Editor fixture.
$start = Get-Content -LiteralPath "$native/Build/NativeRaidStartBay.cs" -Raw
$begin = $start.IndexOf('    internal static void Install(')
$end = $start.IndexOf('    private void Build(')
if ($begin -lt 0 -or $end -le $begin) { throw 'Cannot isolate the stock installer' }
$start.Remove($begin, $end - $begin) | Set-Content -LiteralPath "$assets/NativeRaidStartBay.cs" -Encoding utf8

$coverage = Get-Content -LiteralPath $fixture -Raw
function Replace-Fixture([string] $Old, [string] $New) {
    if (!$script:coverage.Contains($Old)) { throw "Coverage fixture does not contain $Old" }
    $script:coverage = $script:coverage.Replace($Old, $New)
}
Replace-Fixture 'foreach (var module in ModuleCatalog.Rooms()) PlaceModule(module, bundles.Prefabs, "forced-fallback");' @'
foreach (var module in ModuleCatalog.Rooms()) PlaceModule(module, bundles.Prefabs, "forced-fallback");
            PlaceModule(MobiusHall.Create(), bundles.Prefabs, "prepared-rotated");
            PlaceModule(RightAngleChambers.Create(), bundles.Prefabs, "prepared-rotated");
            PlaceModule(FreightServiceRoom.Create(FreightServiceRoom.SampleReceiving, true), bundles.Prefabs,
                "receiving-annex", useReceiving: false);
            PlaceModule(FreightServiceRoom.Create(FreightServiceRoom.Dispatch, false, true), bundles.Prefabs,
                "dispatch-portal-annex");
'@
Replace-Fixture 'PlaceModule(receiving, bundles.Prefabs, "receiving-" + branch);' @'
PlaceModule(receiving, bundles.Prefabs, "receiving-" + branch);
                PlaceModule(FreightServiceRoom.Create(FreightServiceRoom.SampleReceiving, research), bundles.Prefabs,
                    "receiving-fallback-" + branch, useReceiving: false);
'@
Replace-Fixture 'RaidRoomPrefabs prefabs, string route)' 'RaidRoomPrefabs prefabs, string route, bool useReceiving = true)'
Replace-Fixture 'var plan = new RaidPlan.Room(module, Identity, Identity, 0);' @'
var pose = route.EndsWith("rotated", StringComparison.Ordinal)
                    ? new Pose(new Vector3(11, 21, -17), Quaternion.Euler(39, 63, 117)) : Identity;
                var plan = new RaidPlan.Room(module, pose, pose, 0);
'@
Replace-Fixture 'if (route == "prepared")' 'if (route.StartsWith("prepared", StringComparison.Ordinal))'
Replace-Fixture 'Snapshot("packed-prefab", module.Id, prefab.transform, module);' 'if (route == "prepared") Snapshot("packed-prefab", module.Id, prefab.transform, module);'
Replace-Fixture 'var receiving = prefabs.For(plan);' 'var receiving = useReceiving ? prefabs.For(plan) : null;'
Replace-Fixture 'Snapshot(stage, room.Plan.Module.Id, room.Root, room.Plan.Module);' 'Snapshot($"{stage}/room-{room.Plan.Index}", room.Plan.Module.Id, room.Root, room.Plan.Module);'
Replace-Fixture 'Snapshot(stage, fold.Link.Fold.Module.Id, fold.Root, fold.Link.Fold.Module);' 'Snapshot($"{stage}/fold-{fold.Link.To.Room.Index}", fold.Link.Fold.Module.Id, fold.Root, fold.Link.Fold.Module);'
Replace-Fixture 'if (route is "prepared" or "receiving-with-research" or "service-native") RenderModule(module.Id, baked.Root);' @'
if (route is "prepared" or "receiving-with-research" or "service-native" or "dispatch-with-research" or "extraction-native" or "prepared-rotated")
                    RenderModule(module.Id, baked.Root, route == "prepared-rotated" ? "-rotated" : "");
'@
Replace-Fixture 'private static void RenderModule(string module, Transform root)' 'private static void RenderModule(string module, Transform root, string suffix)'
Replace-Fixture 'switch (module)' @'
if (module.StartsWith("spherical-chamber", StringComparison.Ordinal))
            {
                Render(root, module, new(-7.4f,1.65f,.61f), new(2.4f,-7f,12.4f), 65);
                return;
            }
            switch (module)
'@
Replace-Fixture 'case SphericalChamber.Id: Render(root, module, new(-7.4f,1.65f,.61f), new(2.4f,-7f,12.4f), 65); break;' @'
case "sample-storage": Render(root, module, new(1.11f,1.60f,0f), new(7.1f,1.48f,4.85f), 63); break;
                case "freight-dispatch": Render(root, module, new(2.10f,1.63f,-.24f), new(8.4f,1.63f,4.0f), 62); break;
                case "junction-corridor": Render(root, module, new(2.40f,1.62f,.4f), new(4.90f,1.65f,6.8f), 69); break;
                case "mobius-hall": Render(root, module + suffix, new(-13.2f,1.62f,1.65f), new(1.4f,1.65f,12.5f), 68); break;
                case "right-angle-chambers": Render(root, module + suffix, new(-3.37f,-1.08f,-.60f), new(.24f,-5.39f,2.20f), 67, new Vector3(.613f,.790f,0)); break;
                case "extraction-chamber": Render(root, module, new(2.5f,1.64f,-.85f), new(10f,1.75f,5.85f), 61); break;
'@
Replace-Fixture 'Snapshot("assembled-ring", "ring", root.transform);' @'
Snapshot("assembled-ring", "ring", root.transform);
                foreach (string name in RingRooms)
                {
                    Transform room = root.transform.Find(name);
                    Snapshot("assembled-ring-room", name, room);
                    Render(room, "ring-" + name, new(-2.3f,1.63f,-2.2f), new(3.7f,1.7f,5.8f), 66);
                }
'@
Replace-Fixture 'if (service) Render(root.transform, name,' 'Render(root.transform, name,'
Replace-Fixture 'Vector3 eye, Vector3 aim, float fov)' 'Vector3 eye, Vector3 aim, float fov, Vector3? localUp = null)'
Replace-Fixture 'Quaternion.LookRotation(root.TransformPoint(aim)-root.TransformPoint(eye),root.up)' 'Quaternion.LookRotation(root.TransformPoint(aim)-root.TransformPoint(eye), root.TransformDirection(localUp ?? Vector3.up))'
Replace-Fixture 'int entries = 0;' @'
var parts = module?.Geometry.GroupBy(block => block.Id).ToDictionary(group => group.Key, group => group.First());
            int entries = 0;
'@
Replace-Fixture 'Slots.Add($"{stage}' @'
Block part = parts != null && parts.TryGetValue(renderer.name, out var piece) ? piece : null;
                    string expected = CatalogWallObservation.Classify(stage, id, renderer, part, module);
                    string appearance = CatalogWallObservation.Describe(root, renderer, material, part, module);
                    Slots.Add($"{stage}
'@
Replace-Fixture '{renderer.bounds.size:F3}");' (@('{renderer.bounds.size:F3}', '{expected}', '{appearance}");') -join "`t")
$coverage | Set-Content -LiteralPath "$assets/Editor/MaterialCoverageChecks.cs" -Encoding utf8

'-langversion:10.0' | Set-Content -LiteralPath "$project/Assets/csc.rsp" -Encoding utf8
$manifest = Get-Content -LiteralPath "$project/Packages/manifest.json" -Raw | ConvertFrom-Json -AsHashtable
foreach ($module in 'assetbundle', 'imageconversion', 'jsonserialize') { $manifest.dependencies["com.unity.modules.$module"] = '1.0.0' }
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$project/Packages/manifest.json" -Encoding utf8
if ($WarmPackageCache -and !(Test-Path -LiteralPath "$project/Library/PackageCache")) {
    New-Item -ItemType Directory "$project/Library" -Force | Out-Null
    Copy-Item -LiteralPath (Resolve-Path -LiteralPath $WarmPackageCache).Path -Destination "$project/Library/PackageCache" -Recurse
}

$zipPath = (Resolve-Path -LiteralPath $Package).Path
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
$bundles = 'contour4-rooms.bundle', 'contour4-prepared-modules.bundle', 'contour4-ring.bundle'
try {
    foreach ($bundle in $bundles) {
        $entry = $archive.GetEntry("plugins/Contour4/$bundle")
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "$out/$bundle", $true)
    }
} finally { $archive.Dispose() }
$log = "$out/unity.log"
$arguments = '-batchmode', '-force-d3d11', '-quit', '-projectPath', ('"' + $project + '"'),
    '-executeMethod', 'SpaceJourney.MaterialCoverageChecks.Run', '-logFile', ('"' + $log + '"')
$environment = @{
    CONTOUR4_COVERAGE_OUTPUT = $out
    CONTOUR4_ROOMS_BUNDLE = "$out/contour4-rooms.bundle"
    CONTOUR4_PREPARED_BUNDLE = "$out/contour4-prepared-modules.bundle"
    CONTOUR4_COVERAGE_RING = "$out/contour4-ring.bundle"
}
$editor = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden -Environment $environment
Select-String -LiteralPath $log -Pattern 'CONTOUR4 MATERIAL COVERAGE|error CS\d+|Exception|return code' | Select-Object -Last 20 -ExpandProperty Line
if ($editor.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'CONTOUR4 MATERIAL COVERAGE PASSED' -SimpleMatch -Quiet)) {
    throw "Wall catalog fixture failed in $out with exit $($editor.ExitCode)"
}
[ordered]@{
    editorExit = $editor.ExitCode
    package = (Get-FileHash -LiteralPath $zipPath).Hash
    bundles = $bundles | ForEach-Object { (Get-FileHash -LiteralPath "$out/$_").Hash }
    editionWear = (Get-FileHash -LiteralPath "$native/Generation/RoomSurfaceWear.cs").Hash
    observationSource = (Get-FileHash -LiteralPath "$PSScriptRoot/CatalogWallObservation.cs").Hash
    materialCoverageFixture = (Get-FileHash -LiteralPath "$assets/Editor/MaterialCoverageChecks.cs").Hash
    observations = (Get-FileHash -LiteralPath "$out/renderer-slots.tsv").Hash
} | ConvertTo-Json | Set-Content -LiteralPath "$out/receipt.json" -Encoding utf8

param(
    [string] $Module,
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe',
    [string] $Output = (Join-Path ([IO.Path]::GetTempPath()) 'Contour4-PreparedModules'),
    [switch] $StageOnly,
    [switch] $VerifyExisting,
    [switch] $Renders
)

$ErrorActionPreference = 'Stop'
$outPath = [IO.Path]::GetFullPath($Output)
$project = Join-Path $outPath 'Unity'
$marker = Join-Path $project '.contour4-prepared-project'
$assets = Join-Path $project 'Assets'
$source = Join-Path $assets 'Contour4Sources'
$editorAssets = Join-Path $source 'Editor'
$target = Join-Path $assets 'Contour4/PreparedModules'
$label = if ($Module) { $Module } else { 'all' }
if ($label -notmatch '^[a-z0-9-]+$') { throw 'Bad module identifier.' }
if (!(Test-Path -LiteralPath $Unity -PathType Leaf)) { throw "Unity editor unavailable: $Unity" }
if ((Test-Path -LiteralPath $project) -and !(Test-Path -LiteralPath $marker -PathType Leaf)) {
    throw "Choose an empty output directory. An unrelated Unity project is present at $project."
}

New-Item -ItemType Directory -Path $editorAssets,"$project/Packages","$project/ProjectSettings","$assets/Contour4" -Force | Out-Null
New-Item -ItemType File -Path $marker -Force | Out-Null
Copy-Item -Path "$PSScriptRoot/../Source/*.cs","$PSScriptRoot/../Catalog/*.cs" -Destination $source
Copy-Item -LiteralPath "$PSScriptRoot/../Unity/Catalog/CatalogPreview.cs" -Destination $source
Copy-Item -Path "$PSScriptRoot/../Unity/Catalog/Editor/*.cs","$PSScriptRoot/Editor/PreparedModules/*.cs" -Destination $editorAssets
Copy-Item -LiteralPath "$PSScriptRoot/Generation/MobiusWalk.cs" -Destination $editorAssets
Copy-Item -LiteralPath "$PSScriptRoot/../Catalog/Checks/CatalogChecks.cs", "$PSScriptRoot/../Unity/Editor/SceneDressing.cs" `
    -Destination $editorAssets
Copy-Item -LiteralPath "$PSScriptRoot/Authored/Assets/Contour4.meta" -Destination $assets

# Seed a clean project from the editable assets so regeneration keeps stable Unity references.
$published = "$PSScriptRoot/Authored/Assets/Contour4/PreparedModules"
if (!(Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $published)) {
    Copy-Item -LiteralPath $published -Destination "$assets/Contour4" -Recurse
    Copy-Item -LiteralPath "$published.meta" -Destination "$assets/Contour4"
}

@{ dependencies = @{ 'com.unity.modules.physics' = '1.0.0'; 'com.unity.modules.ai' = '1.0.0' } } |
    ConvertTo-Json | Set-Content -LiteralPath "$project/Packages/manifest.json" -Encoding utf8
@('m_EditorVersion: 6000.3.17f1', 'm_EditorVersionWithRevision: 6000.3.17f1 (cf0352b38e81)') |
    Set-Content -LiteralPath "$project/ProjectSettings/ProjectVersion.txt" -Encoding utf8

"Unity project: $project"
if ($StageOnly) { return }

$operation = if ($VerifyExisting) { 'VerifyExisting' } else { 'Run' }
$log = Join-Path $outPath "$label-$operation-unity.log"
$graphics = if ($Renders) { '-force-d3d11' } else { '-nographics' }
$env:CONTOUR4_PREPARED_MODULE = $Module
$env:CONTOUR4_PREPARED_RENDER = if ($Renders) { Join-Path $outPath 'renders' } else { $null }
$env:CONTOUR4_PREPARED_NAV_EVIDENCE = Join-Path $outPath 'navigation'
$unityArgs = @('-batchmode', $graphics, '-quit', '-projectPath', ('"' + $project + '"'),
    '-executeMethod', "Contour4.Procedural.Catalog.BuildPreparedModules.$operation", '-logFile', ('"' + $log + '"'))
$editor = Start-Process -FilePath $Unity -ArgumentList $unityArgs -PassThru -WindowStyle Hidden
if (!$editor.WaitForExit(300000)) {
    $editor.Kill()
    throw 'The isolated Unity project exceeded the five-minute build timeout.'
}
if (!(Test-Path -LiteralPath $log -PathType Leaf)) { throw "Unity produced no log and exited with $($editor.ExitCode)." }
Select-String -LiteralPath $log -Pattern 'CONTOUR4 PREPARED|error CS\d+|Exception:|No valid Unity Editor license' |
    Select-Object -ExpandProperty Line
if ($editor.ExitCode -ne 0) { exit $editor.ExitCode }
if (!(Select-String -LiteralPath $log -SimpleMatch 'CONTOUR4 PREPARED UNITY PASSED' -Quiet)) {
    throw 'Unity exited without completing prefab verification.'
}
"Editable prefabs: $target"

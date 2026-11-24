param([Parameter(Mandatory)] [string] $Project)

$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Project) { throw 'Choose a new Unity project path.' }
$native = Join-Path $PSScriptRoot '../procedural/NativeRaid'
$catalog = Join-Path $native '../Catalog'
$source = Join-Path $native '../Source'
$authored = Join-Path $native 'Authored/Assets/Contour4'
$assets = Join-Path $Project 'Assets'
$editor = Join-Path $assets 'Editor'
$runtime = Join-Path $assets 'RoomCatalog'
New-Item -ItemType Directory -Path $editor,$runtime,"$Project/Packages","$Project/ProjectSettings" -Force | Out-Null
Copy-Item -LiteralPath $authored -Destination $assets -Recurse
Copy-Item -LiteralPath "$authored.meta" -Destination $assets
Copy-Item -LiteralPath "$native/Editor/BuildReceivingPrefab.cs","$PSScriptRoot/Editor/BuildRoomPrefabs.cs" -Destination $editor
Copy-Item -Path "$catalog/*.cs","$source/*.cs" -Destination $runtime
Copy-Item -LiteralPath "$native/Generation/FreightServiceRoom.cs","$native/Generation/SampleReceivingDressing.cs" -Destination $runtime
'[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Assembly-CSharp-Editor")]' |
    Set-Content -LiteralPath "$runtime/EditorAccess.cs" -Encoding utf8
@{ dependencies = @{
    'com.unity.modules.assetbundle' = '1.0.0'
    'com.unity.modules.physics' = '1.0.0'
} } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$Project/Packages/manifest.json" -Encoding utf8
@('m_EditorVersion: 6000.3.17f1','m_EditorVersionWithRevision: 6000.3.17f1 (cf0352b38e81)') |
    Set-Content -LiteralPath "$Project/ProjectSettings/ProjectVersion.txt" -Encoding utf8

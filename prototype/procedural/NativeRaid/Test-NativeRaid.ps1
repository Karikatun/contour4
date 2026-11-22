param(
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe',
    [Parameter(Mandatory)] [string] $Output,
    [switch] $PrepareOnly
)

$ErrorActionPreference = 'Stop'
$outPath = [IO.Path]::GetFullPath($Output)
$project = Join-Path $outPath 'Unity'
$marker = Join-Path $project '.contour4-native-raid-project'
$assets = Join-Path $project 'Assets/Contour4NativeRaid'
$log = Join-Path $outPath 'unity-native-raid.log'

if ([IO.Path]::GetFileName($Unity) -ine 'Unity.exe' -or !(Test-Path -LiteralPath $Unity -PathType Leaf)) {
    throw "Unity Editor unavailable: $Unity"
}
if ((Test-Path -LiteralPath $project) -and !(Test-Path -LiteralPath $marker -PathType Leaf)) {
    throw "Choose a different output directory: $project"
}

New-Item -ItemType Directory -Path "$assets/Editor", "$project/Packages", "$project/ProjectSettings" -Force | Out-Null
New-Item -ItemType File -Path $marker -Force | Out-Null
Copy-Item -Path "$PSScriptRoot/../Navigation/Runtime/*.cs", "$PSScriptRoot/../Catalog/*.cs",
    "$PSScriptRoot/../Source/*.cs", "$PSScriptRoot/Generation/*.cs" -Destination $assets
Copy-Item -LiteralPath "$PSScriptRoot/Enemies/StockPortalPursuit.cs" -Destination $assets
Copy-Item -Path "$PSScriptRoot/Editor/*.cs" -Destination "$assets/Editor"
'[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Assembly-CSharp-Editor")]' |
    Set-Content -LiteralPath "$assets/OfflineEditorAccess.cs" -Encoding utf8

@{
    dependencies = @{
        'com.unity.modules.ai' = '1.0.0'
        'com.unity.modules.physics' = '1.0.0'
    }
} | ConvertTo-Json | Set-Content -LiteralPath "$project/Packages/manifest.json" -Encoding utf8
@('m_EditorVersion: 6000.3.17f1', 'm_EditorVersionWithRevision: 6000.3.17f1 (cf0352b38e81)') |
    Set-Content -LiteralPath "$project/ProjectSettings/ProjectVersion.txt" -Encoding utf8
# Authored settings for the two agent IDs observed in the installed R.E.P.O. 0.5.2 build.
$areas = "  - {name: '', cost: 1}`n" * 29
$settings = @"
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!126 &1
NavMeshProjectSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  areas:
  - {name: Walkable, cost: 1}
  - {name: Not Walkable, cost: 1}
  - {name: Jump, cost: 2}
${areas}  m_LastAgentTypeID: -334000983
  m_Settings:
  - serializedVersion: 3
    agentTypeID: 0
    agentRadius: 0.3
    agentHeight: 2
    agentSlope: 60
    agentClimb: 0.75
    minRegionArea: 2
    cellSize: 0.16666667
    tileSize: 256
  - serializedVersion: 3
    agentTypeID: -334000983
    agentRadius: 0.6
    agentHeight: 2
    agentSlope: 60
    agentClimb: 0.4
    minRegionArea: 2
    cellSize: 0.16666667
    tileSize: 256
  m_SettingNames:
  - Humanoid
  - Enemy
"@
$settings | Set-Content -LiteralPath "$project/ProjectSettings/NavMeshAreas.asset" -Encoding utf8

if ($PrepareOnly) {
    "Prepared offline Unity project: $project"
    return
}

$unityArgs = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'),
    '-executeMethod', 'Contour4.NativeRaid.Editor.SeededRaidChecks.Run', '-logFile', ('"' + $log + '"'))
$editor = Start-Process -FilePath $Unity -ArgumentList $unityArgs -Wait -PassThru -WindowStyle Hidden
if (!(Test-Path -LiteralPath $log -PathType Leaf)) { throw "Unity produced no log. Exit $($editor.ExitCode)." }
Select-String -LiteralPath $log -Pattern 'CONTOUR4 NATIVE RAID|error CS\d+|Exception:|return code|No valid Unity Editor license' |
    Select-Object -Last 35 -ExpandProperty Line
if ($editor.ExitCode -ne 0) { exit $editor.ExitCode }
if (!(Select-String -LiteralPath $log -SimpleMatch 'CONTOUR4 NATIVE RAID PASSED' -Quiet)) {
    throw "Unity did not complete the native raid checks. See $log"
}
"Unity native raid evidence: $log"

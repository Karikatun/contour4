param(
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe',
    [string] $Output = (Join-Path ([IO.Path]::GetTempPath()) 'Contour4-Procedural-Navigation')
)

$ErrorActionPreference = 'Stop'
$outPath = [IO.Path]::GetFullPath($Output)
$project = Join-Path $outPath 'Unity'
$marker = Join-Path $project '.contour4-navigation-project'
$assets = Join-Path $project 'Assets/Contour4Navigation'
$log = Join-Path $outPath 'unity-navigation.log'

if ([IO.Path]::GetFileName($Unity) -ine 'Unity.exe' -or !(Test-Path -LiteralPath $Unity -PathType Leaf)) {
    throw "Unity Editor unavailable: $Unity"
}
if ((Test-Path -LiteralPath $project) -and !(Test-Path -LiteralPath $marker -PathType Leaf)) {
    throw "Choose a different output directory. This Unity project already exists: $project"
}

New-Item -ItemType Directory -Path "$assets/Editor", "$project/Packages", "$project/ProjectSettings" -Force | Out-Null
New-Item -ItemType File -Path $marker -Force | Out-Null
Copy-Item -Path "$PSScriptRoot/Runtime/*.cs" -Destination $assets
Copy-Item -Path "$PSScriptRoot/Editor/*.cs" -Destination "$assets/Editor"

@{
    dependencies = @{
        'com.unity.modules.ai' = '1.0.0'
        'com.unity.modules.physics' = '1.0.0'
    }
} | ConvertTo-Json | Set-Content -LiteralPath "$project/Packages/manifest.json" -Encoding utf8
@('m_EditorVersion: 6000.3.17f1', 'm_EditorVersionWithRevision: 6000.3.17f1 (cf0352b38e81)') |
    Set-Content -LiteralPath "$project/ProjectSettings/ProjectVersion.txt" -Encoding utf8

$unityArgs = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'),
    '-executeMethod', 'Contour4.Procedural.Navigation.Editor.NavigationChecks.Run', '-logFile', ('"' + $log + '"'))
$editor = Start-Process -FilePath $Unity -ArgumentList $unityArgs -Wait -PassThru -WindowStyle Hidden
if (!(Test-Path -LiteralPath $log -PathType Leaf)) { throw "Unity produced no log. Exit $($editor.ExitCode)." }
Select-String -LiteralPath $log -Pattern 'CONTOUR4 NAVIGATION|error CSd+|Exception:|return code|No valid Unity Editor license|invalid dependencies' |
    Select-Object -Last 34 -ExpandProperty Line
if ($editor.ExitCode -ne 0) { exit $editor.ExitCode }
if (!(Select-String -LiteralPath $log -SimpleMatch 'CONTOUR4 NAVIGATION PASSED' -Quiet)) {
    throw "Unity did not complete the navigation checks. See $log"
}
"Unity navigation evidence: $log"

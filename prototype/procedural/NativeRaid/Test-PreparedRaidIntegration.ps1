param(
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe',
    [Parameter(Mandatory)] [string] $Output
)

$ErrorActionPreference = 'Stop'
$outPath = [IO.Path]::GetFullPath($Output)
& "$PSScriptRoot/Test-NativeRaid.ps1" -Unity $Unity -Output $outPath -PrepareOnly
$project = Join-Path $outPath 'Unity'
Copy-Item -LiteralPath "$PSScriptRoot/Authored/Assets/Contour4" -Destination "$project/Assets" -Recurse -Force

$log = Join-Path $outPath 'prepared-integration.log'
$args = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'),
    '-executeMethod', 'Contour4.NativeRaid.Editor.PreparedRaidIntegrationChecks.Run', '-logFile', ('"' + $log + '"'))
$editor = Start-Process -FilePath $Unity -ArgumentList $args -Wait -PassThru -WindowStyle Hidden
if (!(Test-Path -LiteralPath $log -PathType Leaf)) { throw "Unity produced no log. Exit $($editor.ExitCode)." }
Select-String -LiteralPath $log -Pattern 'CONTOUR4 PREPARED RUNTIME|error CS\d+|Exception:|return code|No valid Unity Editor license' |
    Select-Object -Last 30 -ExpandProperty Line
if ($editor.ExitCode -ne 0) { exit $editor.ExitCode }
if (!(Select-String -LiteralPath $log -SimpleMatch 'CONTOUR4 PREPARED RUNTIME PASSED' -Quiet)) {
    throw "Unity did not complete the integration checks. See $log"
}

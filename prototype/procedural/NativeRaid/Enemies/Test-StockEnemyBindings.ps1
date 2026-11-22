[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $GameManaged,
    [Parameter(Mandatory)] [string] $BepInExCore,
    [Parameter(Mandatory)] [string] $Plugin,
    [Parameter(Mandatory)] [string] $MonoRoot,
    [Parameter(Mandatory)] [string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
$GameManaged = (Resolve-Path -LiteralPath $GameManaged).Path
$BepInExCore = (Resolve-Path -LiteralPath $BepInExCore).Path
$Plugin = (Resolve-Path -LiteralPath $Plugin).Path
$mono = (Resolve-Path -LiteralPath "$MonoRoot/bin/mono.exe").Path
$compiler = (Resolve-Path -LiteralPath "$MonoRoot/lib/mono/4.5/mcs.exe").Path
$harmony = (Resolve-Path -LiteralPath "$BepInExCore/0Harmony.dll").Path
$gameDll = Join-Path $GameManaged 'Assembly-CSharp.dll'
$expectedGameHash = '98A0FEDCDF70FC47FA49257BF8AAB80955D9B947717D236BCEB9DA6FD44F4D40'
if ((Get-FileHash -LiteralPath $gameDll).Hash -ne $expectedGameHash) {
    throw 'The stock AI contract requires the audited 0.5.2 Assembly-CSharp.dll.'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$executable = Join-Path $OutputDirectory 'StockEnemyBindings.exe'
& $mono $compiler "-r:$harmony" "-out:$executable" "$PSScriptRoot/Checks/StockEnemyBindings.cs"
if ($LASTEXITCODE) { throw "Stock AI binding test compilation failed ($LASTEXITCODE)." }

# The game ships the Mono build of Harmony. Run its real IL reader in the already-installed Unity Mono host.
$previousMonoPath = $env:MONO_PATH
try {
    $env:MONO_PATH = $BepInExCore
    & $mono $executable $GameManaged $Plugin
    if ($LASTEXITCODE) { throw "Stock AI binding test failed ($LASTEXITCODE)." }
    "PASS Assembly-CSharp SHA-256 $expectedGameHash"
    "PASS $([IO.Path]::GetFileName($Plugin)) SHA-256 $((Get-FileHash -LiteralPath $Plugin).Hash)"
}
finally {
    $env:MONO_PATH = $previousMonoPath
}

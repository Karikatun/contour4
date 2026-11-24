param(
    [Parameter(Mandatory)] [string] $Bundle,
    [Parameter(Mandatory)] [string] $Plugin,
    [Parameter(Mandatory)] [string] $Output,
    [string] $Runtime,
    [string] $LoadingBundle,
    [string] $RoomsBundle,
    [Parameter(Mandatory)] [string] $PreparedModulesBundle
)
$ErrorActionPreference = 'Stop'
$bundlePath = (Resolve-Path -LiteralPath $Bundle).Path
$pluginPath = (Resolve-Path -LiteralPath $Plugin).Path
if (!$LoadingBundle) { $LoadingBundle = Join-Path ([IO.Path]::GetDirectoryName($bundlePath)) 'contour4-loading.bundle' }
$loadingPath = (Resolve-Path -LiteralPath $LoadingBundle).Path
if (!$RoomsBundle) { $RoomsBundle = Join-Path ([IO.Path]::GetDirectoryName($bundlePath)) 'contour4-rooms.bundle' }
$roomsPath = (Resolve-Path -LiteralPath $RoomsBundle).Path
$preparedPath = (Resolve-Path -LiteralPath $PreparedModulesBundle).Path
if (!$Runtime) { $Runtime = Join-Path ([IO.Path]::GetDirectoryName($pluginPath)) 'Contour4NativeRaidRuntime.dll' }
$runtimePath = (Resolve-Path -LiteralPath $Runtime).Path
$outPath = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $outPath) { throw 'Choose a new package path.' }
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($outPath)) -Force | Out-Null
$metadata = Get-Content -LiteralPath "$PSScriptRoot/manifest.json" -Raw | ConvertFrom-Json
if (([Reflection.AssemblyName]::GetAssemblyName($pluginPath)).Version.ToString(3) -ne $metadata.version_number) {
    throw 'Plugin and manifest versions differ.'
}
$runtimeAssembly = [Reflection.AssemblyName]::GetAssemblyName($runtimePath)
if ($runtimeAssembly.Name -cne 'Contour4NativeRaidRuntime' -or $runtimeAssembly.Version.ToString(3) -ne '0.1.0') {
    throw 'Expected the Contour4 native raid runtime library 0.1.0.'
}
$partial = Join-Path ([IO.Path]::GetDirectoryName($outPath)) ([Guid]::NewGuid().ToString() + '.zip')
try {
    $zip = [IO.Compression.ZipFile]::Open($partial, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entries = [ordered]@{
            'README.md' = "$PSScriptRoot/README.md"
            'manifest.json' = "$PSScriptRoot/manifest.json"
            'plugins/Contour4/Contour4.dll' = $pluginPath
            'plugins/Contour4/Contour4NativeRaidRuntime.dll' = $runtimePath
            'plugins/Contour4/contour4-ring.bundle' = $bundlePath
            'plugins/Contour4/contour4-loading.bundle' = $loadingPath
            'plugins/Contour4/contour4-rooms.bundle' = $roomsPath
            'plugins/Contour4/contour4-prepared-modules.bundle' = $preparedPath
            'icon.png' = "$PSScriptRoot/icon.png"
        }
        foreach ($entry in $entries.GetEnumerator()) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entry.Value, $entry.Key) | Out-Null
        }
    } finally { $zip.Dispose() }
    [IO.File]::Move($partial, $outPath)
} finally {
    if (Test-Path -LiteralPath $partial -PathType Leaf) { Remove-Item -LiteralPath $partial }
}
$check = [IO.Compression.ZipFile]::OpenRead($outPath)
try {
    $check.Entries | Select-Object FullName,Length
} finally {
    $check.Dispose()
}
Get-FileHash -LiteralPath $outPath -Algorithm SHA256

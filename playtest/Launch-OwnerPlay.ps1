[CmdletBinding(DefaultParameterSetName = 'ByVersion', PositionalBinding = $false)]
param(
    [Parameter(Mandatory, ParameterSetName = 'ByVersion', Position = 0)]
    [ValidatePattern('^\d+\.\d+\.\d+$')] [string] $Version,
    [Parameter(Mandatory, ParameterSetName = 'ByPackage')] [string] $MapPackage,
    [Parameter(Position = 1)] [string] $Clone = (Join-Path $env:TEMP 'contour4-ring-dev-052'),
    [Parameter(Position = 2)] [ValidatePattern('^[A-Fa-f0-9]{64}$')] [string] $ExpectedSha256,
    [Parameter(Position = 3)] [string] $ValuablesPackage,
    [switch] $PortalTrace,
    [switch] $NaturalRun
)

$ErrorActionPreference = 'Stop'
$mapPayloadNames = @('Contour4.dll', 'contour4-ring.bundle', 'Contour4NativeRaidRuntime.dll',
    'contour4-loading.bundle', 'contour4-rooms.bundle', 'contour4-prepared-modules.bundle')

function Get-OwnerPlayMap {
    param([string] $Path, [string] $RequestedVersion, [string] $ExpectedHash)
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Map package is missing: $Path" }
    $path = (Resolve-Path -LiteralPath $Path).Path
    $archiveStream = [IO.File]::OpenRead($path)
    try {
        $sha256 = (Get-FileHash -InputStream $archiveStream -Algorithm SHA256).Hash
        if ($ExpectedHash -and $sha256 -ne $ExpectedHash) { throw 'Package SHA-256 differs from the expected value.' }
        $archiveStream.Position = 0
        $mapZip = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Read, $true)
        try {
            $manifestEntry = $mapZip.GetEntry('manifest.json')
            if (!$manifestEntry) { throw 'The map package manifest is missing.' }
            $reader = [IO.StreamReader]::new($manifestEntry.Open())
            try { $metadata = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
            if ($metadata.name -ne 'Contour4' -or $metadata.version_number -notmatch '^\d+\.\d+\.\d+$' -or
                ($RequestedVersion -and $metadata.version_number -ne $RequestedVersion)) {
                throw 'Unexpected map package name or version.'
            }
            $assets = @{}
            # Earlier packages include smaller test slices. The full split payload starts at 0.2.20.
            $fullPayload = [version] $metadata.version_number -ge [version] '0.2.20'
            foreach ($name in $mapPayloadNames) {
                $entryPath = "plugins/Contour4/$name"
                $entry = $mapZip.GetEntry($entryPath)
                if (!$entry -and !$fullPayload -and $name -notin @('Contour4.dll', 'contour4-ring.bundle')) { continue }
                if (!$entry -or !$entry.Length) { throw "Required map asset is missing: $name" }
                if (@($mapZip.Entries | Where-Object FullName -eq $entryPath).Count -ne 1) {
                    throw "Duplicate map asset: $name"
                }
                $memory = [IO.MemoryStream]::new()
                $stream = $entry.Open()
                try { $stream.CopyTo($memory); $assets[$name] = $memory.ToArray() }
                finally { $stream.Dispose(); $memory.Dispose() }
            }
            [pscustomobject]@{ Path=$path; Version=$metadata.version_number; Sha256=$sha256; Manifest=$metadata; Assets=$assets }
        } finally { $mapZip.Dispose() }
    } finally { $archiveStream.Dispose() }
}

function Install-OwnerPlayMap {
    param([string] $CloneRoot, $Map, [string] $Scratch)
    $root = (Resolve-Path -LiteralPath $CloneRoot).Path
    $owned = Get-Content -LiteralPath (Join-Path $root '.obruchnik-dev-owned.json') -Raw | ConvertFrom-Json
    if ($owned.purpose -ne 'Contour-4 local isolated test') { throw 'The map requires a marked private Contour-4 test copy.' }
    $target = $root
    foreach ($part in @('', 'BepInEx', 'plugins', 'Contour4')) {
        if ($part) { $target = Join-Path $target $part }
        $item = Get-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
        if ($item -and (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint))) {
            throw "Map installation path must be a directory without links: $target"
        }
    }

    $previous = @{}
    foreach ($name in $mapPayloadNames) {
        $destination = Join-Path $target $name
        $item = Get-Item -LiteralPath $destination -Force -ErrorAction SilentlyContinue
        if ($item -and ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint))) {
            throw "Map asset must be a regular file: $destination"
        }
        if ($item) { $previous[$name] = [IO.File]::ReadAllBytes($destination) }
    }

    $assemblyProbe = Join-Path $Scratch 'Contour4.dll'
    [IO.File]::WriteAllBytes($assemblyProbe, $Map.Assets['Contour4.dll'])
    if (([Reflection.AssemblyName]::GetAssemblyName($assemblyProbe)).Version.ToString(3) -ne $Map.Version) {
        throw 'The package DLL does not match the map manifest version.'
    }

    New-Item -ItemType Directory -Path $target -Force | Out-Null
    $staged = @{}
    $updated = [Collections.Generic.List[string]]::new()
    $id = [guid]::NewGuid().ToString('N')
    try {
        foreach ($name in $Map.Assets.Keys) {
            $staged[$name] = Join-Path $target ".ownerplay-$id-$name.tmp"
            [IO.File]::WriteAllBytes($staged[$name], $Map.Assets[$name])
        }
        foreach ($name in $mapPayloadNames) {
            $destination = Join-Path $target $name
            if ($staged.ContainsKey($name)) {
                [IO.File]::Move($staged[$name], $destination, $true)
            } elseif ($previous.ContainsKey($name)) {
                [IO.File]::Delete($destination)
            } else { continue }
            $updated.Add($name)
        }
    } catch {
        $failure = $_
        $restoreErrors = foreach ($name in $updated) {
            $destination = Join-Path $target $name
            try {
                if ($previous.ContainsKey($name)) { [IO.File]::WriteAllBytes($destination, $previous[$name]) }
                else { [IO.File]::Delete($destination) }
            } catch { $_.Exception.Message }
        }
        if ($restoreErrors) { throw "Map installation failed: $failure. Restore failed: $($restoreErrors -join ', ')" }
        throw $failure
    } finally {
        foreach ($path in $staged.Values) { [IO.File]::Delete($path) }
    }
    $target
}

function Get-OwnerPlayDistPackagePath {
    param([string] $Directory, [string] $FileName)
    $current = Join-Path $Directory $FileName
    if (Test-Path -LiteralPath $current -PathType Leaf) { return $current }
    $legacy = Join-Path $Directory "SpaceJourney-$FileName"
    if (Test-Path -LiteralPath $legacy -PathType Leaf) { return $legacy }
    $current
}

function Get-OwnerPlayCompanion {
    param($MapManifest, [string] $ProjectRoot, [string] $OverridePath)
    $requirements = @($MapManifest.dependencies | Where-Object { $_ -like 'SpaceJourney-Contour4Valuables-*' })
    # Historical maps have no companion dependency and retain their existing installation.
    if ($requirements.Count -eq 0) { return $null }
    if ($requirements.Count -ne 1 -or $requirements[0] -notmatch '^SpaceJourney-Contour4Valuables-(\d+\.\d+\.\d+)$') {
        throw 'The map must declare exactly one valid Contour4Valuables dependency.'
    }
    $requiredVersion = $Matches[1]
    $path = if ($OverridePath) { $OverridePath } else {
        Get-OwnerPlayDistPackagePath (Join-Path $ProjectRoot 'contour4-valuables/dist') "Contour4Valuables-$requiredVersion.zip"
    }
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required companion package is missing: $path" }
    $path = (Resolve-Path -LiteralPath $path).Path
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $seen = @{}
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -match '(^/|\\|(^|/)\.\.(/|$)|:)' -or $seen.ContainsKey($entry.FullName)) {
                throw "Unsafe or duplicate companion ZIP path: $($entry.FullName)"
            }
            $seen[$entry.FullName] = $true
            $stream = $entry.Open()
            try { $stream.CopyTo([IO.Stream]::Null) } finally { $stream.Dispose() }
        }
        $manifestEntry = $zip.GetEntry('manifest.json')
        if (!$manifestEntry) { throw 'The companion package manifest is missing.' }
        $reader = [IO.StreamReader]::new($manifestEntry.Open())
        try { $metadata = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if ($metadata.name -ne 'Contour4Valuables' -or $metadata.version_number -ne $requiredVersion) {
            throw "Expected companion Contour4Valuables $requiredVersion."
        }
        $assets = @{}
        foreach ($name in @('Contour4Valuables.dll', 'contour4-valuables.bundle')) {
            $entry = $zip.GetEntry("plugins/Contour4Valuables/$name")
            if (!$entry -or !$entry.Length) { throw "Required companion asset is missing: $name" }
            $stream = $entry.Open()
            $memory = [IO.MemoryStream]::new()
            try { $stream.CopyTo($memory); $assets[$name] = $memory.ToArray() }
            finally { $memory.Dispose(); $stream.Dispose() }
        }
        # Keep the validated bytes, rather than reopening an archive during installation.
        [pscustomobject]@{ Path=$path; Version=$requiredVersion; Sha256=(Get-FileHash -LiteralPath $path).Hash; Assets=$assets }
    } finally { $zip.Dispose() }
}

function Install-OwnerPlayCompanion {
    param([string] $CloneRoot, $Companion)
    if (!$Companion) { return }
    $root = (Resolve-Path -LiteralPath $CloneRoot).Path
    $owned = Get-Content -LiteralPath (Join-Path $root '.obruchnik-dev-owned.json') -Raw | ConvertFrom-Json
    if ($owned.purpose -ne 'Contour-4 local isolated test') { throw 'The companion requires a marked private Contour-4 test copy.' }
    $target = $root
    foreach ($part in @('BepInEx', 'plugins', 'Contour4Valuables')) {
        $target = Join-Path $target $part
        if ((Test-Path -LiteralPath $target) -and ((Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Companion installation path cannot be a link: $target"
        }
    }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach ($name in @('Contour4Valuables.dll', 'contour4-valuables.bundle')) {
        $destination = Join-Path $target $name
        if ((Test-Path -LiteralPath $destination) -and ((Get-Item -LiteralPath $destination -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Companion asset cannot be a link: $destination"
        }
    }
    foreach ($name in @('Contour4Valuables.dll', 'contour4-valuables.bundle')) {
        [IO.File]::WriteAllBytes((Join-Path $target $name), $Companion.Assets[$name])
    }
}

$project = Split-Path -Parent $PSScriptRoot
$packagePath = if ($PSCmdlet.ParameterSetName -eq 'ByPackage') { $MapPackage } else {
    Get-OwnerPlayDistPackagePath (Join-Path $project 'prototype/ring/dist') "Contour4-$Version.zip"
}
$map = Get-OwnerPlayMap -Path $packagePath -RequestedVersion $Version -ExpectedHash $ExpectedSha256
$Version = $map.Version
$companion = Get-OwnerPlayCompanion -MapManifest $map.Manifest -ProjectRoot $project -OverridePath $ValuablesPackage
if (Get-Process REPO -ErrorAction SilentlyContinue) { throw 'Close the running R.E.P.O. session before launching this profile.' }
if (!(Get-Process steam -ErrorAction SilentlyContinue)) { throw 'Open Steam in the Windows session used for the playtest.' }

$cloneRoot = (Resolve-Path -LiteralPath $Clone).Path
$marker = Get-Content -LiteralPath (Join-Path $cloneRoot '.obruchnik-dev-owned.json') -Raw | ConvertFrom-Json
if ($marker.purpose -ne 'Contour-4 local isolated test') { throw 'The clone is not marked as a private Contour-4 test copy.' }
$gameExe = Join-Path $cloneRoot 'REPO.exe'
$gameManaged = Join-Path $cloneRoot 'REPO_Data/Managed'
$bepInExCore = Join-Path $cloneRoot 'BepInEx/core'
$repoLib = Join-Path $cloneRoot 'BepInEx/plugins/Zehs-REPOLib/REPOLib.dll'
$config = Join-Path $cloneRoot 'BepInEx/config/REPOLib.cfg'
$preloader = Join-Path $cloneRoot 'BepInEx/patchers/Obruchnik.TestIsolation.dll'
foreach ($path in @($gameExe, $repoLib, $preloader, "$bepInExCore/BepInEx.dll")) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing private profile file: $path" }
}
if (!(Test-Path -LiteralPath $gameManaged -PathType Container)) { throw "Missing game dependencies: $gameManaged" }

$stamp = [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss-fff')
$evidence = Join-Path $project "local-evidence/owner-play/$Version-$stamp"
$build = Join-Path $evidence 'helper-build'
if (Test-Path -LiteralPath $evidence) { throw "The evidence directory already exists: $evidence" }
New-Item -ItemType Directory -Path $evidence | Out-Null

$ownerHelper = Join-Path $PSScriptRoot 'OwnerPlay/Contour4OwnerPlay.csproj'
dotnet build $ownerHelper -c Release -o "$build/out" "-p:BaseIntermediateOutputPath=$build/obj/" `
    "-p:GameManaged=$gameManaged" "-p:BepInExCore=$bepInExCore" "-p:RepoLibPath=$repoLib" --nologo
if ($LASTEXITCODE -ne 0) { throw 'The playtest helper did not compile.' }

$mod = Install-OwnerPlayMap -CloneRoot $cloneRoot -Map $map -Scratch $evidence
Install-OwnerPlayCompanion -CloneRoot $cloneRoot -Companion $companion
$pluginFile = Join-Path $mod 'Contour4.dll'
Copy-Item -LiteralPath "$build/out/Contour4OwnerPlay.dll" -Destination (Join-Path $mod 'Contour4OwnerPlay.dll') -Force

if (Test-Path -LiteralPath $config) { $settings = [IO.File]::ReadAllText($config) } else { $settings = '' }
$developerMode = (!$NaturalRun).ToString().ToLowerInvariant()
foreach ($flag in @('DeveloperMode', 'VanillaDeveloperMode')) {
    $pattern = "(?m)^$flag[ \t]*=[^\r\n]*"
    if ($settings -match $pattern) {
        $settings = $settings -replace $pattern, "$flag = $developerMode"
    } else {
        $section = '(?m)^\[General\][ \t]*\r?$'
        if ($settings -match $section) {
            $settings = $settings -replace $section, "[General]`r`n$flag = $developerMode"
        } else {
            $settings = "$settings`r`n[General]`r`n$flag = $developerMode`r`n"
        }
    }
}
New-Item -ItemType Directory -Path (Split-Path -Parent $config) -Force | Out-Null
[IO.File]::WriteAllText($config, $settings)

$saveRoot = Join-Path $env:USERPROFILE 'AppData/LocalLow/semiwork/Repo'
function Get-SaveFingerprint {
    Get-ChildItem -LiteralPath $saveRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in '.es3','.json','.bak' -and $_.Name -ne 'RemoteConfigCache.json' -and
            !$_.FullName.StartsWith("$saveRoot\Unity\", [StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object FullName | ForEach-Object { "$($_.FullName) $((Get-FileHash -LiteralPath $_.FullName).Hash)" }
}
$before = @(Get-SaveFingerprint)
$logPath = Join-Path $evidence 'player.log'
$readyPath = Join-Path $evidence 'owner-ready.txt'
$failurePath = Join-Path $evidence 'failure.txt'
$arguments = @('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','800',
    '-obruchnik-game-root', ('"' + $cloneRoot + '"'), '-logFile', ('"' + $logPath + '"'),
    '-contour4-owner-play', ('"' + $evidence + '"'))
if ($PortalTrace) { $arguments += @('-contour4-portal-trace', $Version) }
if ($NaturalRun) { $arguments += '-contour4-owner-natural' }
$process = Start-Process -FilePath $gameExe -ArgumentList $arguments -WorkingDirectory $cloneRoot -PassThru -WindowStyle Normal
$process.Id | Set-Content -LiteralPath (Join-Path $evidence 'pid.txt')
"Opened R.E.P.O. (PID $($process.Id)). Logs: $evidence"

$deadline = [DateTimeOffset]::UtcNow.AddSeconds(100)
do {
    if ($process.HasExited) { throw "R.E.P.O. exited with code $($process.ExitCode). See $logPath" }
    if (Test-Path -LiteralPath $failurePath) { throw (Get-Content -LiteralPath $failurePath -Raw) }
    $process.Refresh()
    if ((Test-Path -LiteralPath $readyPath) -and $process.MainWindowHandle -ne 0) { break }
    Start-Sleep -Milliseconds 500
} while ([DateTimeOffset]::UtcNow -lt $deadline)
if (!(Test-Path -LiteralPath $readyPath) -or $process.MainWindowHandle -eq 0) { throw "The raid did not become ready. See $logPath" }

$ready = Get-Content -LiteralPath $readyPath -Raw
$upgrades = [regex]::Matches($ready, '(?m)^playerUpgrade[^=\r\n]+=5\r?$')
$consoleEnabled = $ready -match '(?m)^nativeConsoleEnabled=True\r?$'
$infiniteEnergy = $ready -match '(?m)^infiniteEnergy=True\r?$'
$settingsConfirmed = if ($NaturalRun) {
    $ready -match '(?m)^runMode=natural\r?$' -and $ready -match '(?m)^nativeConsoleEnabled=False\r?$' -and
        $ready -match '(?m)^infiniteEnergy=False\r?$' -and $ready -match '(?m)^upgradesApplied=False\r?$'
} else {
    $upgrades.Count -eq 13 -and $consoleEnabled -and $infiniteEnergy -and $ready -match '(?m)^cartCount=1\r?$'
}
if (!$settingsConfirmed) {
    throw "Playtest settings were not confirmed. See $readyPath"
}
$isolation = Get-Content -LiteralPath (Join-Path $cloneRoot 'isolation-preloader.txt') -Raw
if ($isolation -notmatch "(?m)^pid=$($process.Id)\r?$" -or $isolation -notmatch '(?m)^Assembly-CSharp=\d+') {
    throw 'Save isolation was not confirmed for this process.'
}
$after = @(Get-SaveFingerprint)
if (($before -join '|') -ne ($after -join '|')) { throw 'Original save files changed during startup.' }
$launch = [ordered]@{
    version = $Version; packagePath = $map.Path; packageSha256 = $map.Sha256; processId = $process.Id; windowTitle = $process.MainWindowTitle;
    protectedSaves = $before.Count; naturalRun = [bool] $NaturalRun; console = $consoleEnabled; infiniteEnergy = $infiniteEnergy;
    isolationReceipt = $isolation;
    pluginSha256 = (Get-FileHash -LiteralPath $pluginFile).Hash;
    bundleSha256 = (Get-FileHash -LiteralPath (Join-Path $mod 'contour4-ring.bundle')).Hash
}
if (!$NaturalRun) { $launch.upgrades = $upgrades.Count; $launch.upgradeLevel = 5 }
$launch.mapPayloadSha256 = [ordered]@{}
foreach ($name in $mapPayloadNames) {
    if ($map.Assets.ContainsKey($name)) {
        $launch.mapPayloadSha256[$name] = (Get-FileHash -LiteralPath (Join-Path $mod $name)).Hash
    }
}
if ($PortalTrace) { $launch.portalTraceReady = $ready -match '(?m)^portalTrace=armed\r?$' }
if ($companion) {
    $launch.valuablesVersion = $companion.Version
    $launch.valuablesPackageSha256 = $companion.Sha256
}
$launch | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'launch.json') -Encoding utf8
Get-Content -LiteralPath $readyPath
if ($launch.portalTraceReady) { "Press F8 in the raid to save a portal trace under $evidence." }
elseif ($PortalTrace) { Write-Warning "Portal trace was unavailable. See $logPath" }

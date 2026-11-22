param(
    [Parameter(Mandatory)] [string] $MapPackage,
    [Parameter(Mandatory)] [string] $ValuablesPackage,
    [string] $BepInExCore = "$env:APPDATA/Thunderstore Mod Manager/DataFolder/REPO/cache/BepInEx-BepInExPack/5.4.2305/BepInExPack/BepInEx/core"
)
$ErrorActionPreference = 'Stop'
# Read-only metadata/integrity check. Runtime behavior is checked in an isolated game.
function Read-Package([string] $Path, [string] $DllEntry) {
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $zip = [IO.Compression.ZipFile]::OpenRead($resolved)
    try {
        $entries = [ordered]@{}
        foreach ($entry in $zip.Entries) {
            $fileType = ($entry.ExternalAttributes -shr 28) -band 15
            if ($entry.FullName -match '(^/|\\|(^|/)\.\.?(/|$)|:|//|[\x00-\x1f])' -or
                $entries.Contains($entry.FullName) -or $fileType -notin 0,8 -or $entry.Length -eq 0) {
                throw "Unsafe or duplicate ZIP path in ${resolved}: $($entry.FullName)"
            }
            $stream = $entry.Open()
            try { $entries[$entry.FullName] = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
        }
        $manifest = $zip.GetEntry('manifest.json')
        $dll = $zip.GetEntry($DllEntry)
        if (!$manifest -or !$dll -or $dll.Length -eq 0) { throw "Required manifest or DLL missing in $resolved" }
        $reader = [IO.StreamReader]::new($manifest.Open())
        try { $metadata = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        [pscustomobject]@{ Metadata = $metadata; Path = $resolved; Sha256 = (Get-FileHash -LiteralPath $resolved).Hash; Entries = $entries }
    } finally { $zip.Dispose() }
}

function Require-ExactSet($Actual, $Expected, [string] $Label) {
    if (Compare-Object -CaseSensitive -ReferenceObject $Expected -DifferenceObject @($Actual)) {
        throw "Unexpected $Label. Actual: $($Actual -join ', ')"
    }
}

function Read-EmbeddedAssembly($Archive, [string] $Name, $Resolver) {
    $stream = $Archive.GetEntry($Name).Open()
    $memory = [IO.MemoryStream]::new()
    try {
        $stream.CopyTo($memory)
        $memory.Position = 0
        [Mono.Cecil.AssemblyDefinition]::ReadAssembly($memory, [Mono.Cecil.ReaderParameters]@{
            ReadingMode = 'Immediate'; InMemory = $true; AssemblyResolver = $Resolver
        })
    } finally { $stream.Dispose(); $memory.Dispose() }
}

function Test-MainPlugin([string] $Path, [string] $Version) {
    Add-Type -Path (Join-Path $BepInExCore 'Mono.Cecil.dll')
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    $resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
    $resolver.AddSearchDirectory((Resolve-Path -LiteralPath $BepInExCore).Path)
    $main = $runtime = $null
    try {
        $main = Read-EmbeddedAssembly $archive 'plugins/Contour4/Contour4.dll' $resolver
        $runtime = Read-EmbeddedAssembly $archive 'plugins/Contour4/Contour4NativeRaidRuntime.dll' $resolver
        $plugins = @($main.MainModule.GetTypes() + $runtime.MainModule.GetTypes() | Where-Object {
            $_.CustomAttributes | Where-Object { $_.AttributeType.FullName -ceq 'BepInEx.BepInPlugin' }
        })
        $attribute = $plugins.CustomAttributes | Where-Object { $_.AttributeType.FullName -ceq 'BepInEx.BepInPlugin' }
        if ($plugins.Count -ne 1 -or $plugins[0].FullName -cne 'SpaceJourney.Contour4' -or
            $main.Name.Name -cne 'Contour4' -or $main.Name.Version.ToString(3) -cne $Version -or
            $attribute.ConstructorArguments[0].Value -cne 'spacejourney.contour4' -or
            $attribute.ConstructorArguments[2].Value -cne $Version -or
            $runtime.Name.Name -cne 'Contour4NativeRaidRuntime' -or $runtime.Name.Version.ToString(3) -cne '0.1.0' -or
            $main.MainModule.GetType('Contour4.NativeRaid.NativeRaidPlugin')) {
            throw 'The ZIP must contain one Contour4 plugin with the matching version and the private runtime library.'
        }
        $bepDependencies = $plugins[0].CustomAttributes | Where-Object { $_.AttributeType.FullName -ceq 'BepInEx.BepInDependency' }
        $valuables = $bepDependencies | Where-Object { $_.ConstructorArguments[0].Value -ceq 'spacejourney.contour4.valuables' }
        $repoLib = $bepDependencies | Where-Object { $_.ConstructorArguments[0].Value -ceq 'REPOLib' }
        if ($valuables.ConstructorArguments[1].Value -ne 1 -or $repoLib.ConstructorArguments[1].Value -cne '4.5.1') {
            throw 'The compiled plugin must require Contour4Valuables and REPOLib 4.5.1.'
        }
        [pscustomobject]@{ Id = $attribute.ConstructorArguments[0].Value; Version = $Version; Runtime = $runtime.Name.FullName }
    } finally {
        if ($runtime) { $runtime.Dispose() }
        if ($main) { $main.Dispose() }
        $resolver.Dispose()
        $archive.Dispose()
    }
}

$map = Read-Package $MapPackage 'plugins/Contour4/Contour4.dll'
$valuables = Read-Package $ValuablesPackage 'plugins/Contour4Valuables/Contour4Valuables.dll'
if ($map.Metadata.name -cne 'Contour4' -or $map.Metadata.version_number -notin '0.2.11','0.2.12','0.2.13','0.2.16','0.2.17','0.2.18') {
    throw 'This verifier expects a supported Contour4 package through 0.2.18.'
}
$companionVersion = if ($map.Metadata.version_number -eq '0.2.18') { '0.2.3' } elseif ($map.Metadata.version_number -in '0.2.13','0.2.16','0.2.17','0.2.18') { '0.2.0' } else { '0.1.0' }
if ($valuables.Metadata.name -ne 'Contour4Valuables' -or $valuables.Metadata.version_number -ne $companionVersion) { throw "Expected companion Contour4Valuables $companionVersion." }
$baseDependencies = 'BepInEx-BepInExPack-5.4.2305', 'Zehs-REPOLib-4.5.1'
Require-ExactSet $map.Metadata.dependencies ($baseDependencies + "SpaceJourney-Contour4Valuables-$companionVersion") 'map dependencies'
Require-ExactSet $valuables.Metadata.dependencies $baseDependencies 'valuables dependencies'
$rootEntries = 'README.md', 'manifest.json', 'icon.png'
$mapEntries = $rootEntries + 'plugins/Contour4/Contour4.dll', 'plugins/Contour4/contour4-ring.bundle'
$mainPlugin = $null
if ($map.Metadata.version_number -in '0.2.12','0.2.13','0.2.16','0.2.17','0.2.18') {
    $mapEntries += 'plugins/Contour4/Contour4NativeRaidRuntime.dll', 'plugins/Contour4/contour4-loading.bundle'
}
Require-ExactSet $map.Entries.Keys $mapEntries 'map archive entries'
Require-ExactSet $valuables.Entries.Keys ($rootEntries + 'plugins/Contour4Valuables/Contour4Valuables.dll',
    'plugins/Contour4Valuables/contour4-valuables.bundle') 'valuables archive entries'
if ($map.Metadata.version_number -in '0.2.12','0.2.13','0.2.16','0.2.17','0.2.18') {
    $mainPlugin = Test-MainPlugin $map.Path $map.Metadata.version_number
}
[ordered]@{ check='package-pair-metadata-and-entry-hashes'; passed=$true; map=$map; valuables=$valuables; plugin=$mainPlugin } |
    ConvertTo-Json -Depth 5

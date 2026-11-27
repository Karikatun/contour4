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

function Read-EmbeddedAssembly($Archive, [string] $Name, $Resolver, [switch] $ReadBody) {
    $stream = $Archive.GetEntry($Name).Open()
    $memory = [IO.MemoryStream]::new()
    try {
        $stream.CopyTo($memory)
        $memory.Position = 0
        $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($memory, [Mono.Cecil.ReaderParameters]@{
            ReadingMode = 'Immediate'; InMemory = $true; AssemblyResolver = $Resolver
        })
        # Cecil resolves metadata immediately but reads method bodies from this stream on demand.
        if ($ReadBody) {
            foreach ($method in $assembly.MainModule.GetTypes().Methods | Where-Object HasBody) {
                [void]$method.Body.Instructions.Count
            }
        }
        $assembly
    } finally { $stream.Dispose(); $memory.Dispose() }
}

function Get-MethodOperands($Assembly, [string] $Type, [string] $Name) {
    $methods = @($Assembly.MainModule.GetType($Type).Methods | Where-Object Name -CEQ $Name)
    if ($methods.Count -ne 1 -or !$methods[0].HasBody) { throw "Missing expected compiled method ${Type}::$Name." }
    $methods[0].Body.Instructions | ForEach-Object { $_.Operand }
}

function Test-MainPlugin([string] $Path, [string] $Version) {
    Add-Type -Path (Join-Path $BepInExCore 'Mono.Cecil.dll')
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    $resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
    $resolver.AddSearchDirectory((Resolve-Path -LiteralPath $BepInExCore).Path)
    $main = $runtime = $null
    try {
        $hasPreparedModules = $Version -in '0.2.20','0.2.21','0.2.22','0.2.23','0.2.24','0.2.25','0.2.26','0.2.27','0.2.28','0.2.29','0.2.30','0.2.31','0.2.32','0.2.33','0.2.34','0.2.35','0.2.36','0.2.37','0.2.38','0.2.39','0.2.40','0.2.41','0.2.42','0.2.43','0.2.44','0.2.45','0.2.46','0.2.47','0.2.48','0.2.49','0.2.50','0.2.51','0.2.52','0.2.53','0.3.0','0.3.1','0.3.2','0.3.3','0.3.4'
        $main = Read-EmbeddedAssembly $archive 'plugins/Contour4/Contour4.dll' $resolver -ReadBody:$hasPreparedModules
        $runtime = Read-EmbeddedAssembly $archive 'plugins/Contour4/Contour4NativeRaidRuntime.dll' $resolver -ReadBody:$hasPreparedModules
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
        $preparedBindings = $null
        if ($hasPreparedModules) {
            $awake = @(Get-MethodOperands $main 'SpaceJourney.Contour4' 'Awake')
            $bundle = @(Get-MethodOperands $main 'SpaceJourney.RoomPrefabBundle' 'Load')
            $place = @(Get-MethodOperands $runtime 'Contour4.NativeRaid.Runtime.RaidAssembly' 'Place')
            $loot = @(Get-MethodOperands $runtime 'Contour4.NativeRaid.Runtime.RaidLootSites' 'Find')
            $install = @(Get-MethodOperands $main 'Contour4.NativeRaid.NativeRaidLoot' 'Install')
            $expectedCalls = [ordered]@{
                rooms = @($awake, 'SpaceJourney.RoomPrefabBundle', 'Load')
                feature = @($awake, 'Contour4.NativeRaid.NativeRaidFeature', 'Attach')
                sidecars = @($bundle, 'Contour4.NativeRaid.Runtime.RaidRoomPrefabs/PreparedModule', '.ctor')
                selection = @($place, 'Contour4.NativeRaid.Runtime.RaidRoomPrefabs', 'TryGetPrepared')
                placements = @($place, 'Contour4.NativeRaid.Runtime.RaidAssembly', 'PlacePrepared')
                slots = @($loot, 'Contour4.NativeRaid.Runtime.RaidRoomPrefabs', 'PreparedLoot')
                stockVolumes = @($install, 'Contour4.NativeRaid.Runtime.RaidLootSites', 'Find')
            }
            if ($Version -in '0.2.21','0.2.22','0.2.23','0.2.24','0.2.25','0.2.26','0.2.27','0.2.28','0.2.29','0.2.30','0.2.31','0.2.32','0.2.33','0.2.34','0.2.35','0.2.36','0.2.37','0.2.38','0.2.39','0.2.40','0.2.41','0.2.42','0.2.43','0.2.44','0.2.45','0.2.46','0.2.47','0.2.48','0.2.49','0.2.50','0.2.51','0.2.52','0.2.53','0.3.0','0.3.1','0.3.2','0.3.3','0.3.4') {
                $fixedUpdate = @(Get-MethodOperands $main 'Contour4.NativeRaid.NativeRaidGravity' 'FixedUpdate')
                $ribbon = @(Get-MethodOperands $main 'Contour4.NativeRaid.NativeRaidGravity' 'UpdateRibbon')
                $relayStep = @(Get-MethodOperands $main 'Contour4.NativeRaid.MobiusContactRelay' 'Step')
                $relayContact = @(Get-MethodOperands $main 'Contour4.NativeRaid.MobiusContactRelay' 'OnCollisionStay')
                $ribbonStep = @(Get-MethodOperands $runtime 'Contour4.NativeRaid.Runtime.MobiusWalk' 'Step')
                $jump = @(Get-MethodOperands $main 'SpaceJourney.RingRuntime/NativeJumpMotion' 'ApplyJump')
                $jumpBinding = @(Get-MethodOperands $main 'SpaceJourney.RingRuntime/NativeJumpMotion' 'Bind')
                $ribbonJump = @(Get-MethodOperands $main 'Contour4.NativeRaid.NativeRaidGravity' 'NotifyRibbonJump')
                if ($jumpBinding -cnotcontains 'ApplyJump') { throw 'Stock jump binding does not install the ribbon-aware jump.' }
                $expectedCalls.Add('playerRebase', @($fixedUpdate, 'Contour4.NativeRaid.NativeRaidGravity', 'Rebase'))
                if ($Version -in '0.2.26','0.2.27','0.2.28','0.2.29','0.2.30','0.2.31','0.2.32','0.2.33','0.2.34','0.2.35','0.2.36','0.2.37','0.2.38','0.2.39','0.2.40','0.2.41','0.2.42','0.2.43','0.2.44','0.2.45','0.2.46','0.2.47','0.2.48','0.2.49','0.2.50','0.2.51','0.2.52','0.2.53','0.3.0','0.3.1','0.3.2','0.3.3','0.3.4') {
                    $gravityType = 'Contour4.NativeRaid.NativeRaidGravity'
                    $assemblyType = 'Contour4.NativeRaid.Runtime.RaidAssembly'
                    $sphereOrientation = @(Get-MethodOperands $main $gravityType 'UpdateSphericalOrientation')
                    $sphereForces = @(Get-MethodOperands $main $gravityType 'ApplySphericalPlayerGravity')
                    $sphericalUp = @(Get-MethodOperands $runtime $assemblyType 'TrySphericalUp')
                    $actorGravity = @(Get-MethodOperands $main $gravityType 'ApplyActorGravity')
                    $expectedCalls.Add('playerGravity', @($fixedUpdate, $assemblyType, 'TryUp'))
                    if ($Version -in '0.2.36','0.2.37','0.2.38','0.2.39','0.2.40','0.2.41','0.2.42','0.2.43','0.2.44','0.2.45','0.2.46','0.2.47','0.2.48','0.2.49','0.2.50','0.2.51','0.2.52','0.2.53','0.3.0','0.3.1','0.3.2','0.3.3','0.3.4') {
                        $step = @(Get-MethodOperands $main 'Contour4.NativeRaid.NativeRaidActorGravityStep' 'FixedUpdate')
                        $deferredActors = @(Get-MethodOperands $main $gravityType 'ApplyDeferredActorGravity')
                        $expectedCalls.Add('actorGravityStep', @($step, $gravityType, 'ApplyDeferredActorGravity'))
                        $expectedCalls.Add('actorGravityDispatch', @($deferredActors, $gravityType, 'ApplyActorGravity'))
                    } else {
                        $expectedCalls.Add('actorGravityStep', @($fixedUpdate, $gravityType, 'ApplyActorGravity'))
                    }
                    if ($Version -in '0.2.36','0.2.37','0.2.38','0.2.39','0.2.40','0.2.41','0.2.42','0.2.43','0.2.44','0.2.45','0.2.46','0.2.47','0.2.48','0.2.49','0.2.50','0.2.51','0.2.52','0.2.53','0.3.0','0.3.1','0.3.2','0.3.3','0.3.4') {
                        $actorUp = @(Get-MethodOperands $main $gravityType 'TryActorUp')
                        $expectedCalls.Add('actorGravity', @($actorGravity, $gravityType, 'TryActorUp'))
                        $expectedCalls.Add('actorGravityField', @($actorUp, $assemblyType, 'TryUp'))
                    } else {
                        $expectedCalls.Add('actorGravity', @($actorGravity, $gravityType, 'TryUp'))
                    }
                    $expectedCalls.Add('sphericalOrientation', @($fixedUpdate, $gravityType, 'UpdateSphericalOrientation'))
                    $expectedCalls.Add('sphericalPlayerUp', @($sphereOrientation, $assemblyType, 'TrySphericalUp'))
                    $expectedCalls.Add('sphericalRebase', @($sphereOrientation, $gravityType, 'Rebase'))
                    $expectedCalls.Add('sphericalForceUp', @($sphereForces, $assemblyType, 'TrySphericalUp'))
                    $expectedCalls.Add('sphericalField', @($sphericalUp, 'Contour4.Procedural.Catalog.SphericalGravityField', 'TryUp'))
                } else {
                    $playerUp = @(Get-MethodOperands $runtime 'Contour4.NativeRaid.Runtime.RaidAssembly' 'TryPlayerUp')
                    $expectedCalls.Add('playerGravity', @($fixedUpdate, 'Contour4.NativeRaid.Runtime.RaidAssembly', 'TryPlayerUp'))
                    $expectedCalls.Add('actorGravity', @($fixedUpdate, 'Contour4.NativeRaid.NativeRaidGravity', 'TryUp'))
                    $expectedCalls.Add('sphericalField', @($playerUp, 'Contour4.Procedural.Catalog.SphericalChamber', 'TryPlayerUp'))
                }
                $expectedCalls.Add('mobiusUpdate', @($fixedUpdate, 'Contour4.NativeRaid.NativeRaidGravity', 'UpdateRibbon'))
                $expectedCalls.Add('mobiusSelection', @($ribbon, 'Contour4.NativeRaid.Runtime.RaidAssembly', 'MobiusRoomAt'))
                $expectedCalls.Add('mobiusRebase', @($ribbon, 'Contour4.NativeRaid.NativeRaidGravity', 'Rebase'))
                $expectedCalls.Add('mobiusRelay', @($ribbon, 'Contour4.NativeRaid.MobiusContactRelay', 'Step'))
                $expectedCalls.Add('mobiusContact', @($relayContact, 'Contour4.NativeRaid.Runtime.MobiusWalk', 'Observe'))
                $expectedCalls.Add('mobiusStep', @($relayStep, 'Contour4.NativeRaid.Runtime.MobiusWalk', 'Step'))
                $expectedCalls.Add('mobiusNormal', @($ribbonStep, 'Contour4.Procedural.Catalog.MobiusHall', 'NormalAt'))
                $expectedCalls.Add('mobiusJumpHook', @($jump, 'Contour4.NativeRaid.NativeRaidGravity', 'NotifyRibbonJump'))
                $expectedCalls.Add('mobiusJumpRelease', @($ribbonJump, 'Contour4.NativeRaid.Runtime.MobiusWalk', 'Jump'))
                $expectedCalls.Add('mobiusTakeoffGravity', @($ribbonJump, 'Contour4.NativeRaid.Runtime.MobiusWalk', 'TakeoffGravityCorrection'))
                $expectedCalls.Add('mobiusTakeoffForce', @($ribbonJump, 'UnityEngine.Rigidbody', 'AddForce'))
            }
            if ($awake -cnotcontains 'contour4-prepared-modules.bundle') { throw 'Plugin does not open the packed prepared bundle path.' }
            $preparedBindings = [ordered]@{}
            foreach ($binding in $expectedCalls.GetEnumerator()) {
                $operands, $type, $method = $binding.Value
                $match = @($operands | Where-Object {
                    $_ -is [Mono.Cecil.MethodReference] -and $_.DeclaringType.FullName -ceq $type -and $_.Name -ceq $method
                })
                if ($match.Count -eq 0) { throw "Missing compiled prepared module binding ${type}::$method." }
                $preparedBindings[$binding.Key] = $match[0].FullName
            }
        }
        [pscustomobject]@{ Id = $attribute.ConstructorArguments[0].Value; Version = $Version; Runtime = $runtime.Name.FullName;
            PreparedBindings = $preparedBindings }
    } finally {
        if ($runtime) { $runtime.Dispose() }
        if ($main) { $main.Dispose() }
        $resolver.Dispose()
        $archive.Dispose()
    }
}

$map = Read-Package $MapPackage 'plugins/Contour4/Contour4.dll'
$valuables = Read-Package $ValuablesPackage 'plugins/Contour4Valuables/Contour4Valuables.dll'
if ($map.Metadata.name -cne 'Contour4' -or $map.Metadata.version_number -notin '0.2.11','0.2.12','0.2.13','0.2.16','0.2.17','0.2.18','0.2.19','0.2.20','0.2.21','0.2.22','0.2.23','0.2.24','0.2.25','0.2.26','0.2.27','0.2.28','0.2.29','0.2.30','0.2.31','0.2.32','0.2.33','0.2.34','0.2.35','0.2.36','0.2.37','0.2.38','0.2.39','0.2.40','0.2.41','0.2.42','0.2.43','0.2.44','0.2.45','0.2.46','0.2.47','0.2.48','0.2.49','0.2.50','0.2.51','0.2.52','0.2.53','0.3.0','0.3.1','0.3.2','0.3.3','0.3.4') {
    throw 'This verifier expects a supported Contour4 package through 0.3.4.'
}
$companionVersion = if ($map.Metadata.version_number -in '0.2.32','0.2.33','0.2.34','0.2.35','0.2.36','0.2.37','0.2.38','0.2.39','0.2.40','0.2.41','0.2.42','0.2.43','0.2.44','0.2.45','0.2.46','0.2.47','0.2.48','0.2.49','0.2.50','0.2.51','0.2.52','0.2.53','0.3.0','0.3.1','0.3.2','0.3.3','0.3.4') { '0.2.4' } elseif ($map.Metadata.version_number -in '0.2.18','0.2.19','0.2.20','0.2.21','0.2.22','0.2.23','0.2.24','0.2.25','0.2.26','0.2.27','0.2.28','0.2.29','0.2.30','0.2.31','0.2.32','0.2.33','0.2.34','0.2.35') { '0.2.3' } `
    elseif ($map.Metadata.version_number -in '0.2.13','0.2.16','0.2.17') { '0.2.0' } else { '0.1.0' }
if ($valuables.Metadata.name -ne 'Contour4Valuables' -or $valuables.Metadata.version_number -ne $companionVersion) { throw "Expected companion Contour4Valuables $companionVersion." }
$baseDependencies = 'BepInEx-BepInExPack-5.4.2305', 'Zehs-REPOLib-4.5.1'
Require-ExactSet $map.Metadata.dependencies ($baseDependencies + "SpaceJourney-Contour4Valuables-$companionVersion") 'map dependencies'
Require-ExactSet $valuables.Metadata.dependencies $baseDependencies 'valuables dependencies'
$rootEntries = 'README.md', 'manifest.json', 'icon.png'
$mapEntries = $rootEntries + 'plugins/Contour4/Contour4.dll', 'plugins/Contour4/contour4-ring.bundle'
$mainPlugin = $null
if ($map.Metadata.version_number -ne '0.2.11') {
    $mapEntries += 'plugins/Contour4/Contour4NativeRaidRuntime.dll', 'plugins/Contour4/contour4-loading.bundle'
}
if ($map.Metadata.version_number -in '0.2.19','0.2.20','0.2.21','0.2.22','0.2.23','0.2.24','0.2.25','0.2.26','0.2.27','0.2.28','0.2.29','0.2.30','0.2.31','0.2.32','0.2.33','0.2.34','0.2.35','0.2.36','0.2.37','0.2.38','0.2.39','0.2.40','0.2.41','0.2.42','0.2.43','0.2.44','0.2.45','0.2.46','0.2.47','0.2.48','0.2.49','0.2.50','0.2.51','0.2.52','0.2.53','0.3.0','0.3.1','0.3.2','0.3.3','0.3.4') { $mapEntries += 'plugins/Contour4/contour4-rooms.bundle' }
if ($map.Metadata.version_number -in '0.2.20','0.2.21','0.2.22','0.2.23','0.2.24','0.2.25','0.2.26','0.2.27','0.2.28','0.2.29','0.2.30','0.2.31','0.2.32','0.2.33','0.2.34','0.2.35','0.2.36','0.2.37','0.2.38','0.2.39','0.2.40','0.2.41','0.2.42','0.2.43','0.2.44','0.2.45','0.2.46','0.2.47','0.2.48','0.2.49','0.2.50','0.2.51','0.2.52','0.2.53','0.3.0','0.3.1','0.3.2','0.3.3','0.3.4') { $mapEntries += 'plugins/Contour4/contour4-prepared-modules.bundle' }
Require-ExactSet $map.Entries.Keys $mapEntries 'map archive entries'
Require-ExactSet $valuables.Entries.Keys ($rootEntries + 'plugins/Contour4Valuables/Contour4Valuables.dll',
    'plugins/Contour4Valuables/contour4-valuables.bundle') 'valuables archive entries'
if ($map.Metadata.version_number -ne '0.2.11') {
    $mainPlugin = Test-MainPlugin $map.Path $map.Metadata.version_number
}
[ordered]@{ check='package-pair-metadata-and-entry-hashes'; passed=$true; map=$map; valuables=$valuables; plugin=$mainPlugin } |
    ConvertTo-Json -Depth 5

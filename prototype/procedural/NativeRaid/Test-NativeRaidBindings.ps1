param(
    [Parameter(Mandatory)] [string] $Plugin,
    [string] $Runtime,
    [string] $GameManaged = 'C:/Program Files (x86)/Steam/steamapps/common/REPO/REPO_Data/Managed',
    [string] $BepInExCore = "$env:APPDATA/Thunderstore Mod Manager/DataFolder/REPO/cache/BepInEx-BepInExPack/5.4.2305/BepInExPack/BepInEx/core"
)

$ErrorActionPreference = 'Stop'
$gamePath = Join-Path $GameManaged 'Assembly-CSharp.dll'
$nativeHash = (Get-FileHash -LiteralPath $gamePath -Algorithm SHA256).Hash
if ($nativeHash -ne '98A0FEDCDF70FC47FA49257BF8AAB80955D9B947717D236BCEB9DA6FD44F4D40') {
    throw "Unknown stock bindings: $nativeHash"
}

Add-Type -Path (Join-Path $BepInExCore 'Mono.Cecil.dll')
$native = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($gamePath)
$pluginPath = (Resolve-Path -LiteralPath $Plugin).Path
if (!$Runtime) { $Runtime = Join-Path ([IO.Path]::GetDirectoryName($pluginPath)) 'Contour4NativeRaidRuntime.dll' }
$candidate = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($pluginPath)
$runtimeAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Runtime).Path)
$failures = [Collections.Generic.List[string]]::new()

function Require([bool] $Passed, [string] $Message) {
    if (!$Passed) { $failures.Add($Message) }
}

function Find-Method($Type, [string] $Name) {
    $methods = @($Type.Methods | Where-Object Name -CEQ $Name)
    if ($methods.Length -ne 1) { throw "Expected one $($Type.FullName).$Name method" }
    $methods[0]
}

function Find-Calls($Method, [string] $DeclaringType, [string] $Name) {
    if (!$Method.HasBody) { throw "Expected an executable method: $Method" }
    $Method.Body.Instructions | Where-Object {
        $_.OpCode.Name -in 'call', 'callvirt' -and $_.Operand.DeclaringType.FullName -ceq $DeclaringType -and
        $_.Operand.Name -ceq $Name
    }
}

function Find-SurfaceLookup($Method) {
    Find-Calls $Method 'UnityEngine.Component' 'GetComponent' | Where-Object {
        $_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and
        $_.Operand.GenericArguments[0].FullName -ceq 'Unity.AI.Navigation.NavMeshSurface'
    }
}

try {
    $generator = $native.MainModule.GetType('LevelGenerator')
    $setup = Find-Method $generator 'NavMeshSetup'
    $rpc = Find-Method $generator 'NavMeshSetupRPC'
    $moveNext = Find-Method ($generator.NestedTypes | Where-Object Name -CLike '<Generate>d__*') 'MoveNext'
    $setupCall = Find-Calls $moveNext 'LevelGenerator' 'NavMeshSetup'
    $enemyCall = Find-Calls $moveNext 'LevelGenerator' 'EnemySetup'
    $rpcCall = Find-Calls $setup 'LevelGenerator' 'NavMeshSetupRPC'
    $stockLookup = Find-SurfaceLookup $rpc
    $field = $generator.Fields | Where-Object Name -CEQ 'NavMeshSurface'
    $writes = @($generator.Methods | Where-Object HasBody | ForEach-Object { $_.Body.Instructions } |
        Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.FullName -ceq $field.FullName })
    Require ($setupCall.Offset -lt $enemyCall.Offset -and $null -ne $rpcCall -and $writes.Count -eq 1 -and
        $stockLookup.Next -eq $writes[0]) 'The installed generator changed its NavMeshSetup/RPC/EnemySetup lifecycle.'

    $feature = $candidate.MainModule.GetType('Contour4.NativeRaid.NativeRaidFeature')
    $standalone = $candidate.MainModule.GetType('Contour4.NativeRaid.NativeRaidPlugin')
    $hostType = if ($standalone) { $standalone } else { $candidate.MainModule.GetType('SpaceJourney.Contour4') }
    $awake = Find-Method $hostType 'Awake'
    $patchHost = if ($feature) { $feature } else { $hostType }
    $patchInstaller = if ($feature) { Find-Method $feature 'InstallPatches' } else { $awake }
    if ($feature) {
        $attach = Find-Method $feature 'Attach'
        $hostAttach = Find-Calls $awake $feature.FullName 'Attach'
        $compatibilityCheck = Find-Calls $attach $feature.FullName 'RequireSupportedGame'
        $installPatches = Find-Calls $attach $feature.FullName $patchInstaller.Name
        Require ($null -ne $hostAttach -and $null -ne $compatibilityCheck -and
            $null -ne $installPatches -and $compatibilityCheck.Offset -lt $installPatches.Offset) `
            'The host must attach the shared procedural feature only after checking the game assembly.'
    }
    $patchType = $patchHost.NestedTypes | Where-Object Name -CEQ 'InstallNavigationPatch'
    $attribute = $patchType.CustomAttributes | Where-Object { $_.AttributeType.FullName -ceq 'HarmonyLib.HarmonyPatch' }
    $registeredPatch = $patchInstaller.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'ldtoken' -and $_.Operand.FullName -ceq $patchType.FullName
    }
    $prefix = Find-Method $patchType 'Prefix'
    $installCall = Find-Calls $prefix 'Contour4.NativeRaid.NativeRaidModule' 'InstallNavigation'
    Require ($null -ne $registeredPatch -and $attribute.ConstructorArguments[0].Value.FullName -ceq 'LevelGenerator' -and
        $attribute.ConstructorArguments[1].Value -ceq 'NavMeshSetup' -and $null -ne $installCall) `
        'The registered prefix does not reach the raid provider from the normal stock NavMeshSetup entry point.'

    $moduleType = $candidate.MainModule.GetType('Contour4.NativeRaid.NativeRaidModule')
    $placement = Find-Method $moduleType 'Place'
    $placementMask = Find-Calls $placement 'UnityEngine.LayerMask' 'GetMask'
    $raidPlacement = Find-Calls $placement 'Contour4.NativeRaid.Runtime.RaidAssembly' 'Place'
    $hingeLayer = @($placement.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'ldstr' -and $_.Operand -ceq 'PhysGrabObjectHinge'
    })
    Require ($null -ne $placementMask -and $null -ne $raidPlacement -and $hingeLayer.Count -eq 1 -and
        $placementMask.Previous.Previous -eq $hingeLayer[0] -and $placementMask.Offset -lt $raidPlacement.Offset) `
        'The native raid collision mask must include cabinet fronts after they move to the stock hinge layer.'

    $install = Find-Method $moduleType 'InstallNavigation'
    $candidateLookup = Find-SurfaceLookup $install
    $publish = Find-Calls $install 'Contour4.NativeRaid.Runtime.RaidAssembly' 'InstallNavigation'
    $privateRead = @($moduleType.Methods | Where-Object HasBody | ForEach-Object { $_.Body.Instructions } |
        Where-Object { $_.OpCode.Name -eq 'ldstr' -and $_.Operand -ceq 'NavMeshSurface' })
    Require ($null -ne $candidateLookup -and $privateRead.Count -eq 0 -and $null -ne $publish -and
        $candidateLookup.Offset -lt $publish.Offset) `
        'The prefix must locate the same serialized NavMeshSurface component as stock before the private field is written.'

    $vision = $native.MainModule.GetType('PlayerVisionTarget')
    $eyeField = $vision.Fields | Where-Object Name -CEQ 'VisionTransform'
    $stockEyeUpdate = Find-Method $vision 'Update'
    Require ($eyeField.IsPublic -and $null -ne (Find-Calls $stockEyeUpdate 'UnityEngine.Transform' 'set_localPosition')) `
        'The installed stock eye no longer updates its posture during PlayerVisionTarget.Update.'
    $hooks = $candidate.MainModule.GetType('Contour4.NativeRaid.Enemies.EnemyHooks')
    $optics = $hooks.Methods | Where-Object { $_.Name -ceq 'TryPortalDirection' -and $_.Parameters.Count -gt 3 }
    $refreshCall = Find-Calls $optics $moduleType.FullName 'RefreshTargetEye'
    $sightPoint = Find-Calls $optics 'Contour4.Procedural.Navigation.RaidTarget' 'get_SightPoint'
    $readsPlayerComponent = $optics.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -ceq 'PlayerVisionTarget' -and
        $_.Operand.DeclaringType.FullName -ceq 'PlayerAvatar'
    }
    $refresh = $moduleType.Methods | Where-Object Name -CEQ 'RefreshTargetEye'
    $readsStockEye = $refresh.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'ldfld' -and $_.Operand.FullName -ceq $eyeField.FullName
    }
    $helperType = $runtimeAssembly.MainModule.GetType('Contour4.NativeRaid.Runtime.RaidAssembly')
    $sync = $helperType.Methods | Where-Object Name -CEQ 'SyncVisionEye'
    Require ($null -ne $refreshCall -and $null -ne $readsStockEye -and $null -ne $sightPoint -and
        $null -ne $readsPlayerComponent -and $readsPlayerComponent.Offset -lt $refreshCall.Offset -and
        $refreshCall.Offset -lt $sightPoint.Offset -and $null -ne $sync -and
        $null -ne (Find-Calls $refresh $helperType.FullName 'SyncVisionEye') -and
        $null -ne (Find-Calls $sync 'UnityEngine.Transform' 'get_position') -and
        $null -ne (Find-Calls $sync 'UnityEngine.Transform' 'set_position')) `
        'Native vision must synchronously refresh the bound player eye from the live posture transform before portal evaluation.'

    "Stock Assembly-CSharp SHA-256 $nativeHash / MVID $($native.MainModule.Mvid)"
    "Candidate SHA-256 $((Get-FileHash -LiteralPath $Plugin -Algorithm SHA256).Hash)"
    "Runtime SHA-256 $((Get-FileHash -LiteralPath $Runtime -Algorithm SHA256).Hash)"
    "Stock LevelGenerator.Generate calls NavMeshSetup at IL_$('{0:x4}' -f $setupCall.Offset) and EnemySetup at IL_$('{0:x4}' -f $enemyCall.Offset)."
    "Stock RPC gets the serialized component at IL_$('{0:x4}' -f $stockLookup.Offset) and writes its only private field assignment at IL_$('{0:x4}' -f $writes[0].Offset)."
    if ($failures.Count -ne 0) { throw ($failures -join [Environment]::NewLine) }
    'CONTOUR4 NATIVE RAID BINDINGS PASSED: prefix finds the stock component before the RPC caches it, and the sight hook reads the prepared posture on the same target.'
} finally {
    $runtimeAssembly.Dispose()
    $candidate.Dispose()
    $native.Dispose()
}

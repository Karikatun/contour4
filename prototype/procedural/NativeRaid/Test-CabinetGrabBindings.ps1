param(
    [Parameter(Mandatory)] [string] $Plugin,
    [string] $GameManaged = 'C:/Program Files (x86)/Steam/steamapps/common/REPO/REPO_Data/Managed',
    [string] $BepInExCore = "$env:APPDATA/Thunderstore Mod Manager/DataFolder/REPO/cache/BepInEx-BepInExPack/5.4.2305/BepInExPack/BepInEx/core"
)

$ErrorActionPreference = 'Stop'
$gamePath = Join-Path $GameManaged 'Assembly-CSharp.dll'
$gameHash = (Get-FileHash -LiteralPath $gamePath -Algorithm SHA256).Hash
if ($gameHash -ne '98A0FEDCDF70FC47FA49257BF8AAB80955D9B947717D236BCEB9DA6FD44F4D40') {
    throw "Unknown stock cabinet bindings: $gameHash"
}

Add-Type -Path (Join-Path $BepInExCore 'Mono.Cecil.dll')
$native = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($gamePath)
$pluginPath = (Resolve-Path -LiteralPath $Plugin).Path
$candidate = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($pluginPath)

function Require([bool] $Passed, [string] $Message) {
    if (!$Passed) { throw $Message }
}

function Find-Method($Type, [string] $Name) {
    $method = @($Type.Methods | Where-Object Name -CEQ $Name)
    Require ($method.Length -eq 1 -and $method[0].HasBody) "Expected one executable $($Type.FullName).$Name method."
    $method[0]
}

function Find-Call($Method, [string] $Owner, [string] $Name) {
    $Method.Body.Instructions | Where-Object {
        $_.OpCode.Name -in 'call', 'callvirt' -and $_.Operand.DeclaringType.FullName -ceq $Owner -and $_.Operand.Name -ceq $Name
    } | Select-Object -First 1
}

function Find-GenericCall($Method, [string] $Name, [string] $Argument, [int] $AfterOffset = -1) {
    $Method.Body.Instructions | Where-Object {
        $_.Offset -gt $AfterOffset -and $_.OpCode.Name -in 'call', 'callvirt' -and $_.Operand.Name -ceq $Name -and
        $_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.GenericArguments[0].FullName -ceq $Argument
    } | Select-Object -First 1
}

function Find-String($Method, [string] $Value) {
    $Method.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'ldstr' -and $_.Operand -ceq $Value
    } | Select-Object -First 1
}

try {
    $grabber = $native.MainModule.GetType('PhysGrabber')
    $grab = $native.MainModule.GetType('PhysGrabObject')
    $ray = Find-Method $grabber 'RayCheck'
    $start = Find-Method $grabber 'StartGrabbingPhysObject'
    $update = Find-Method $grabber 'Update'
    $mask = Find-Method ($native.MainModule.GetType('SemiFunc')) 'LayerMaskGetVisionObstruct'
    $nearest = Find-Call $ray 'UnityEngine.Physics' 'Raycast'
    $tag = Find-String $ray 'Phys Grab Object'
    $root = Find-GenericCall $ray 'GetComponent' 'PhysGrabObject' $nearest.Offset
    $startGrab = Find-Call $ray 'PhysGrabber' 'StartGrabbingPhysObject'
    Require ((Find-String $mask 'PhysGrabObjectHinge') -and (Find-String $mask 'Default') -and
        $nearest -and $tag -and $root -and $startGrab -and
        $nearest.Offset -lt $tag.Offset -and $tag.Offset -lt $root.Offset -and $root.Offset -lt $startGrab.Offset -and
        (Find-Call $start 'UnityEngine.RaycastHit' 'get_rigidbody') -and
        (Find-GenericCall $start 'GetComponent' 'PhysGrabObjectCollider')) `
        'Stock grab no longer selects the first eligible tagged rigidbody and its hit collider.'

    $stockInput = $native.MainModule.GetType('InputKey').Fields | Where-Object Name -CEQ 'Grab'
    $hold = $update.Body.Instructions | Where-Object {
        $_.OpCode.Name -in 'call', 'callvirt' -and $_.Operand.DeclaringType.FullName -ceq 'SemiFunc' -and
        $_.Operand.Name -ceq 'InputHold' -and $_.Previous.Operand -eq $stockInput.Constant
    } | Select-Object -First 1
    Require ($hold -and (Find-Call $update 'PhysGrabber' 'RayCheck') -and (Find-Call $update 'PhysGrabber' 'ReleaseObject') -and
        (Find-Call (Find-Method $grabber 'PhysGrabStarted') 'PhysGrabObject' 'GrabStarted') -and
        (Find-Call (Find-Method $grabber 'PhysGrabEnded') 'PhysGrabObject' 'GrabEnded') -and
        (Find-Call (Find-Method $grab 'PhysicsGrabbingManipulation') 'UnityEngine.Rigidbody' 'AddForceAtPosition')) `
        'Stock held input, release or physical pulling contract changed.'

    $attach = Find-Method ($candidate.MainModule.GetType('Contour4.NativeRaid.NativeRaidModule')) 'Place'
    $binding = $candidate.MainModule.GetType('Contour4.NativeRaid.NativeRaidInteractions')
    $installer = $candidate.MainModule.GetType('Contour4.NativeRaid.NativeRaidCabinetGrab')
    Require ($null -ne $installer) 'The candidate has no stock PhysGrabObject cabinet installer (the E-toggle baseline fails here).'
    $bind = Find-Method $binding 'Attach'
    $installers = @($installer.Methods | Where-Object Name -CEQ 'Install')
    $cabinetEntry = @($installers | Where-Object { $_.Parameters.Count -eq 1 })
    $bodyEntry = @($installers | Where-Object { $_.Parameters.Count -eq 2 })
    Require ($cabinetEntry.Count -eq 1 -and $cabinetEntry[0].HasBody -and
        $installers.Count -eq 1 + $bodyEntry.Count -and $bodyEntry.Count -le 1) `
        'Expected the cabinet entry and at most one shared body installer.'
    $install = $cabinetEntry[0]
    if ($bodyEntry.Count -eq 1) {
        $sharedCall = Find-Call $install $installer.FullName 'Install'
        Require ($bodyEntry[0].HasBody -and $sharedCall.Operand.FullName -ceq $bodyEntry[0].FullName) `
            'The procedural cabinet path must delegate to the same stock body installer as the ring.'
        $install = $bodyEntry[0]
    }
    $cabinetCall = Find-Call $bind $installer.FullName 'Install'
    Require ((Find-Call $attach 'GameManager' 'Multiplayer') -and (Find-Call $attach $binding.FullName 'Attach') -and
        $cabinetCall.Operand.FullName -ceq $cabinetEntry[0].FullName -and
        ($binding.Methods.Name -cnotcontains 'Update') -and
        (Find-Call (Find-Method $binding 'OnDisable') 'PhysGrabber' 'ReleaseObject')) `
        'Cabinets must enter the stock path from solo generation and release on teardown.'

    $view = Find-GenericCall $install 'AddComponent' 'Photon.Pun.PhotonView'
    $transformView = Find-GenericCall $install 'AddComponent' 'Photon.Pun.PhotonTransformView'
    $shape = Find-GenericCall $install 'AddComponent' 'PhysGrabObjectCollider'
    $actor = Find-GenericCall $install 'AddComponent' 'PhysGrabObject'
    Require ((Find-String $install 'PhysGrabObjectHinge') -and (Find-String $install 'Phys Grab Object') -and
        $view -and $transformView -and $shape -and $actor -and
        $view.Offset -lt $actor.Offset -and $transformView.Offset -lt $actor.Offset -and $shape.Offset -lt $actor.Offset) `
        'The cabinet body and hit colliders must be initialized before stock grab lifecycle runs.'

    $detectorType = 'PhysGrabObjectImpactDetector'
    $pointActivate = Find-Method $grabber 'PhysGrabPointActivate'
    $grid = Find-Method $grabber 'GridObjectsInstantiate'
    $gridDetector = Find-GenericCall $grid 'GetComponent' $detectorType
    $torque = Find-Method $grab 'GrabTorqueGet'
    $torqueDetector = $torque.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'ldfld' -and $_.Operand.FullName -ceq 'PhysGrabObjectImpactDetector PhysGrabObject::impactDetector' -and
        $_.Next.OpCode.Name -eq 'ldfld' -and $_.Next.Operand.FullName -ceq 'System.Boolean PhysGrabObjectImpactDetector::isEnemy'
    } | Select-Object -First 1
    Require ((Find-Call $start 'PhysGrabber' 'PhysGrabPointActivate') -and
        (Find-Call $pointActivate 'PhysGrabber' 'GridObjectsInstantiate') -and $gridDetector -and
        $gridDetector.Next.OpCode.Name -eq 'ldfld' -and $gridDetector.Next.Operand.FullName -ceq 'System.Boolean PhysGrabObjectImpactDetector::isCart' -and
        (Find-GenericCall (Find-Method $grab 'Awake') 'GetComponent' $detectorType) -and $torqueDetector) `
        'The held-beam startup and torque both require a stock impact detector on the actual target.'
    $cabinetDetector = Find-GenericCall $install 'AddComponent' $detectorType
    $disableImpacts = Find-Call $install 'UnityEngine.Behaviour' 'set_enabled'
    Require ($cabinetDetector -and $disableImpacts -and $transformView.Offset -lt $cabinetDetector.Offset -and
        $cabinetDetector.Offset -lt $disableImpacts.Offset -and $disableImpacts.Previous.OpCode.Name -eq 'ldc.i4.0' -and
        $disableImpacts.Offset -lt $actor.Offset) `
        'Before actor Awake, the held beam needs a passive native impact detector on the cabinet root.'
    $breakRpc = Find-Method ($native.MainModule.GetType($detectorType)) 'BreakRPC'
    $breakEvents = @($breakRpc.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'ldfld' -and $_.Operand.DeclaringType.Name -ceq $detectorType -and
        $_.Next.Operand.FullName -ceq 'System.Void UnityEngine.Events.UnityEvent::Invoke()'
    } | ForEach-Object { $_.Operand.Name })
    $wiredEvents = @($install.Body.Instructions | Where-Object {
        $_.Offset -gt $cabinetDetector.Offset -and $_.Offset -lt $actor.Offset -and $_.OpCode.Name -eq 'stfld' -and
        $_.Operand.DeclaringType.Name -ceq $detectorType -and $_.Operand.Name -cin $breakEvents
    } | ForEach-Object { $_.Operand.Name })
    Require ($breakEvents.Count -eq 4 -and @($breakEvents | Where-Object { $_ -cnotin $wiredEvents }).Count -eq 0) `
        'Hazard impulses can call the passive detector. Initialize the native break callbacks before activation.'

    $objectUpdate = Find-Method $grab 'Update'
    $levels = @($objectUpdate.Body.Instructions | Where-Object {
        $_.OpCode.Name -ceq 'ldc.r4' -and $_.Operand -eq -50 -and $_.Previous.OpCode.Name -ceq 'ldfld' -and
        $_.Previous.Operand.FullName -ceq 'System.Single UnityEngine.Vector3::y' -and
        $_.Next.OpCode.Name -ceq 'bge.un.s' -and $_.Next.Next.Next.Operand.FullName -ceq 'PhysGrabObjectImpactDetector PhysGrabObject::impactDetector' -and
        $_.Next.Next.Next.Next.Operand.FullName -ceq 'System.Boolean PhysGrabObjectImpactDetector::destroyDisable'
    })
    Require ($levels.Count -eq 1 -and (Find-Call $objectUpdate 'PhysGrabObjectImpactDetector' 'DestroyObject') -and
        (Find-Call $objectUpdate 'PhysGrabObject' 'Teleport')) 'Expected a single signed stock world-Y fall path.'

    $marker = $candidate.MainModule.GetType('Contour4.NativeRaid.NativeRaidCabinetMount')
    Require ($null -ne $marker) 'The candidate has no local anchored cabinet fall guard.'
    $lookup = Find-GenericCall $install 'TryGetComponent' $marker.FullName
    $addMarker = Find-GenericCall $install 'AddComponent' $marker.FullName
    Require ($lookup -and $addMarker -and $lookup.Offset -lt $view.Offset -and $lookup.Next.OpCode.Name -eq 'brfalse.s' -and
        $lookup.Next.Next.OpCode.Name -eq 'ret' -and $addMarker.Offset -gt $actor.Offset) 'A cabinet already marked must not register another stock actor.'

    $threshold = Find-Method $installer 'FallHeight'
    $localMarker = Find-GenericCall $threshold 'TryGetComponent' $marker.FullName
    $eligible = Find-Call $threshold $marker.FullName 'get_IsAnchored'
    $stockHeight = @($threshold.Body.Instructions | Where-Object { $_.OpCode.Name -ceq 'ldc.r4' -and $_.Operand -eq -50 })
    $exempt = @($threshold.Body.Instructions | Where-Object { $_.OpCode.Name -ceq 'ldc.r4' -and [float]::IsNegativeInfinity($_.Operand) })
    $mount = Find-Method $marker 'get_IsAnchored'
    $mountGuards = @($mount.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'brfalse.s' })
    Require ($localMarker -and $eligible -and $stockHeight.Count -eq 1 -and $exempt.Count -eq 1 -and
        $localMarker.Next.OpCode.Name -eq 'brfalse.s' -and $localMarker.Next.Operand -eq $stockHeight[0] -and
        $eligible.Next.OpCode.Name -eq 'brtrue.s' -and $eligible.Next.Operand -eq $exempt[0] -and $eligible.Next.Next -eq $stockHeight[0] -and
        $mountGuards.Count -eq 4 -and @($mountGuards | Where-Object { $_.Operand.OpCode.Name -ne 'ldc.i4.0' }).Count -eq 0 -and
        (Find-Call $mount 'UnityEngine.Behaviour' 'get_isActiveAndEnabled') -and
        (Find-Call $mount 'UnityEngine.Rigidbody' 'get_isKinematic') -and (Find-Call $mount 'UnityEngine.Joint' 'get_connectedBody') -and
        (Find-Call $mount 'UnityEngine.Object' 'op_Equality')) `
        'Only the marked local front still connected to its original kinematic anchor may skip the stock fall path.'

    $feature = $candidate.MainModule.GetType('Contour4.NativeRaid.NativeRaidFeature')
    $patchTarget = $installer.NestedTypes | Where-Object Name -CEQ 'FallHeightPatch'
    $patchAttribute = $patchTarget.CustomAttributes | Where-Object { $_.AttributeType.FullName -ceq 'HarmonyLib.HarmonyPatch' }
    $patches = Find-Method $feature 'InstallPatches'
    $registration = @($patches.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldtoken' -and $_.Operand.FullName -ceq $patchTarget.FullName })
    $attachFeature = Find-Method $feature 'Attach'
    $attachOnce = Find-Call $attachFeature $feature.FullName 'InstallPatches'
    $instanceGuard = $attachFeature.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'ldsfld' -and $_.Operand.Name -ceq 'instance' -and $_.Next.OpCode.Name -eq 'brfalse.s' -and $_.Next.Next
    } | Select-Object -First 1
    Require ($patchAttribute -and $patchAttribute.ConstructorArguments[0].Value.FullName -ceq 'PhysGrabObject' -and
        $patchAttribute.ConstructorArguments[1].Value -ceq 'Update' -and $registration.Count -eq 1 -and
        $instanceGuard -and $attachOnce -and $instanceGuard.Offset -lt $attachOnce.Offset -and
        (Find-Call $patches 'HarmonyLib.Harmony' 'UnpatchSelf') -and
        (Find-Call (Find-Method $feature 'Dispose') 'HarmonyLib.Harmony' 'UnpatchSelf')) `
        'The cabinet comparison patch must share the unique feature owner and its rollback and disposal paths.'

    "CONTOUR4 CABINET BINDINGS PASS native first hit, grab-grid startup, held torque/release, and anchored fall scope ($gameHash)"
}
finally {
    $candidate.Dispose()
    $native.Dispose()
}

param(
    [Parameter(Mandatory)] [string] $Baseline,
    [Parameter(Mandatory)] [string] $Candidate,
    [Parameter(Mandatory)] [string] $Output
)
$ErrorActionPreference = 'Stop'
function Read-Contract([string] $Folder) {
    $rows = [Collections.Generic.Dictionary[string, string[]]]::new([StringComparer]::Ordinal)
    $occurrences = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($line in Get-Content -LiteralPath "$Folder/room-contract.tsv") {
        $fields = $line -split "`t"
        if ($fields[0] -ceq 'dispatch-control') { $fields[0] = 'dispatch' }
        $width = if ($fields[1] -ceq 'WALLS') { 1 } else { 2 }
        $key = $fields[0..$width] -join '|'
        if ($occurrences.ContainsKey($key)) {
            $occurrences[$key]++
            $key = "$key|$($occurrences[$key])"
        } else {
            $occurrences.Add($key, 1)
        }
        $rows.Add($key, $fields)
    }
    return $rows
}

$before = Read-Contract $Baseline
$after = Read-Contract $Candidate
if ($before.Count -ne $after.Count) { throw 'Different numbers of scene observations.' }
$target = '(?:(?:west|east|near|far|spur|research)-wall(?:-[^\[]+)?|(?:west|east|spur|supply)-plug)\[\d+\]'
$wallMat = 'Contour-4 painted plaster dado'
$painted = [ordered]@{}
$counts = [ordered]@{ wallMaterials = 0; hiddenCladding = 0; otherRenderers = 0; colliders = 0; lights = 0; rays = 0 }
foreach ($entry in $before.GetEnumerator()) {
    if (!$after.ContainsKey($entry.Key)) { throw "Missing observation $($entry.Key)." }
    $left = $entry.Value
    $right = $after[$entry.Key]
    $identical = ($left -join "`t") -ceq ($right -join "`t")
    switch -CaseSensitive ($left[1]) {
        'WALLS' {
            if ($left[2] -cne $right[2]) {
                throw "Wall membership changed: $($entry.Key)."
            }
            if ([int]$right[3] -ne @($right[2] -split ',').Count -or $right[4] -cne '1') {
                throw "Incomplete wall coverage: $($entry.Key)."
            }
            $painted[$right[0]] = [int]$right[3]
        }
        'RENDER' {
            if ($identical) {
                $counts.otherRenderers++
                continue
            }
            if (($left[4..6] -join '|') -cne ($right[4..6] -join '|')) {
                throw "Render geometry changed: $($entry.Key)."
            }
            if ($right[2] -cmatch "^$target$" -and $right[7] -ceq $wallMat -and $right[3] -ceq $left[3]) {
                $counts.wallMaterials++
            } elseif ($right[2] -cmatch "^$target/(?:Lower bulkhead rail|Inset cladding)\[\d+\]$" -and
                $left[3] -ceq 'True' -and $right[3] -ceq 'False') {
                $counts.hiddenCladding++
            } else {
                throw "Unexpected renderer change: $($entry.Key)."
            }
        }
        'COLLIDER' {
            if (!$identical) { throw "Collider changed: $($entry.Key)." }
            $counts.colliders++
        }
        'LIGHT' {
            if (!$identical) { throw "Light changed: $($entry.Key)." }
            $counts.lights++
        }
        'RAY' {
            if ($left[3] -cne $right[3]) { throw "The inspection ray hit different geometry: $($entry.Key)." }
            $counts.rays++
        }
        default { throw "Unexpected observation $($entry.Key)." }
    }
}
$rayWitnesses = @(
    'receiving-research-prefab-room|RAY|0.24,0.58',
    'receiving-research-prefab-jamb|RAY|0.70,0.55'
)
$witness = foreach ($key in $rayWitnesses) {
    if ($before[$key][4] -cnotmatch '^spur-plug\[\d+\](?:/Inset cladding\[\d+\])?$' -or
        $after[$key][4] -cnotmatch '^spur-plug\[\d+\]$' -or $after[$key][5] -cne $wallMat) {
        throw "Original cladding obstruction remains under the camera: $key."
    }
    [ordered]@{ view = $after[$key][0]; viewport = $after[$key][2]; baseline = $before[$key][4]; candidate = $after[$key][4] }
}
[ordered]@{
    counts = $counts
    surfacesPerCase = $painted
    claddingWitnesses = @($witness)
} | ConvertTo-Json -Depth 5 | Tee-Object -FilePath $Output

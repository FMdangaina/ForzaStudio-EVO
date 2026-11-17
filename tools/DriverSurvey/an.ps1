param([string]$tsv)
$inv = [Globalization.CultureInfo]::InvariantCulture
function V($row, [int]$i) { return @([double]::Parse($row."v${i}x", $inv), [double]::Parse($row."v${i}y", $inv), [double]::Parse($row."v${i}z", $inv)) }
function Sec($row, [string]$name) {
    foreach ($p in $row.sections.Split(';')) {
        if ($p -match ('^' + $name + '=(.*)$')) { return @($Matches[1].Split(',') | ForEach-Object { [double]::Parse($_, $inv) }) }
    }
    return $null
}
function Attr([string]$xml, [string]$name) {
    $o = @()
    foreach ($a in 'x', 'y', 'z') {
        if ($xml -match ($name + '\.' + $a + '="([^"]*)"')) { $o += [double]::Parse($Matches[1], $inv) } else { return $null }
    }
    return $o
}
function Sub($a, $b) { return @(($a[0] - $b[0]), ($a[1] - $b[1]), ($a[2] - $b[2])) }
function Len($a) { return [Math]::Sqrt($a[0] * $a[0] + $a[1] * $a[1] + $a[2] * $a[2]) }
function Stat([string]$label, $list) {
    $l = @($list | Sort-Object)
    if ($l.Count -eq 0) { "{0,-46} n=0" -f $label; return }
    "{0,-46} n={1,3} min {2,8:F4} p10 {3,8:F4} med {4,8:F4} p90 {5,8:F4} max {6,8:F4}" -f $label, $l.Count, $l[0], $l[[int]($l.Count * 0.1)], $l[[int]($l.Count * 0.5)], $l[[int][Math]::Min($l.Count - 1, $l.Count * 0.9)], $l[$l.Count - 1]
}
$rows = Import-Csv $tsv -Delimiter "`t" | Where-Object { $_.version -eq '3' }
$cars = $rows | Group-Object car
$out = @()
foreach ($g in $cars) {
    $m = $g.Group | Where-Object { $_.file -eq 'main' }
    $c = $g.Group | Where-Object { $_.file -eq 'cockpit' }
    if (-not $m -or -not $c) { continue }
    $o = [ordered]@{ car = $g.Name }
    $o.wheelMain = V $m 0; $o.axisMain = V $m 1; $o.lhMain = V $m 2; $o.rhMain = V $m 3; $o.buttMain = V $m 4; $o.shiftMain = V $m 5
    $o.wheelCk = V $c 12; $o.axisCk = V $c 13; $o.buttCk = V $c 14; $o.shiftCk = V $c 15
    $o.buttMainInCk = V $c 4; $o.buttCkInMain = V $m 14
    $o.swMainSec = Sec $m 'steering_wheel'; $o.swCkSec = Sec $c 'steering_wheel'
    $side = if ($o.wheelCk[0] -lt 0) { 'L' } else { 'R' }
    $o.side = $side
    $o.seatMainSec = Sec $m ('seat' + $side); $o.seatCkSec = Sec $c ('seat' + $side)
    $o.buttOff = Attr $m.driver 'ButtOffset'; $o.buttOff0 = Attr $m.driver 'ButtOffsetLOD0'
    $o.lhOff = Attr $m.driver 'LeftHandOffset'; $o.lhOff0 = Attr $m.driver 'LeftHandOffsetLOD0'
    $o.rhOff = Attr $m.driver 'RightHandOffset'; $o.rhOff0 = Attr $m.driver 'RightHandOffsetLOD0'
    $out += [pscustomobject]$o
}
"cars with main + cockpit, CDriverControls v3: $($out.Count)"
""
"== 1. butt anchor: main v4 against cockpit v14 =="
$same = @($out | Where-Object { (Len (Sub $_.buttMain $_.buttCk)) -lt 0.0002 }).Count
"identical (within 0.2 mm): $same of $($out.Count)"
Stat 'butt main - butt cockpit, distance' ($out | ForEach-Object { Len (Sub $_.buttMain $_.buttCk) })
"main file's own v14 non zero: $(@($out | Where-Object { (Len $_.buttCkInMain) -gt 0 }).Count); cockpit's own v4 non zero: $(@($out | Where-Object { (Len $_.buttMainInCk) -gt 0 }).Count)"
""
"== 2. steering wheel: main v0 against cockpit v12 =="
Stat 'wheel main - wheel cockpit, |dx|' ($out | ForEach-Object { [Math]::Abs($_.wheelMain[0] - $_.wheelCk[0]) })
Stat 'wheel main - wheel cockpit, dx toward outside' ($out | ForEach-Object { [Math]::Abs($_.wheelMain[0]) - [Math]::Abs($_.wheelCk[0]) })
Stat 'wheel main - wheel cockpit, dy' ($out | ForEach-Object { $_.wheelMain[1] - $_.wheelCk[1] })
Stat 'wheel main - wheel cockpit, dz' ($out | ForEach-Object { $_.wheelMain[2] - $_.wheelCk[2] })
Stat 'wheel main - wheel cockpit, distance' ($out | ForEach-Object { Len (Sub $_.wheelMain $_.wheelCk) })
""
"== 3. anchors against the geometry of the same file =="
$a = $out | Where-Object { $_.swMainSec }
"main files with a steering_wheel section: $(@($a).Count)"
Stat 'main v0 - main steering_wheel origin, dist' ($a | ForEach-Object { Len (Sub $_.wheelMain $_.swMainSec) })
Stat 'main v0 - main steering_wheel origin, |dx|' ($a | ForEach-Object { [Math]::Abs($_.wheelMain[0] - $_.swMainSec[0]) })
$b = $out | Where-Object { $_.swCkSec }
Stat 'cockpit v12 - cockpit steering_wheel, dist' ($b | ForEach-Object { Len (Sub $_.wheelCk $_.swCkSec) })
$ab = $out | Where-Object { $_.swMainSec -and $_.swCkSec }
Stat 'steering_wheel origin main - cockpit, dist' ($ab | ForEach-Object { Len (Sub $_.swMainSec $_.swCkSec) })
Stat 'steering_wheel origin main - cockpit, |x| out' ($ab | ForEach-Object { [Math]::Abs($_.swMainSec[0]) - [Math]::Abs($_.swCkSec[0]) })
$s = $out | Where-Object { $_.seatMainSec -and $_.seatCkSec }
"cars with the driver's seat in both files: $(@($s).Count)"
Stat 'seat origin main - cockpit, dist' ($s | ForEach-Object { Len (Sub $_.seatMainSec $_.seatCkSec) })
Stat 'seat origin main - cockpit, |x| out' ($s | ForEach-Object { [Math]::Abs($_.seatMainSec[0]) - [Math]::Abs($_.seatCkSec[0]) })
Stat 'butt anchor x - main seat x' ($s | ForEach-Object { [Math]::Abs($_.buttMain[0]) - [Math]::Abs($_.seatMainSec[0]) })
Stat 'butt anchor x - cockpit seat x' ($s | ForEach-Object { [Math]::Abs($_.buttCk[0]) - [Math]::Abs($_.seatCkSec[0]) })
Stat 'butt anchor y - main seat y' ($s | ForEach-Object { $_.buttMain[1] - $_.seatMainSec[1] })
Stat 'butt anchor y - cockpit seat y' ($s | ForEach-Object { $_.buttCk[1] - $_.seatCkSec[1] })
Stat 'butt anchor z - main seat z' ($s | ForEach-Object { $_.buttMain[2] - $_.seatMainSec[2] })
Stat 'butt anchor z - cockpit seat z' ($s | ForEach-Object { $_.buttCk[2] - $_.seatCkSec[2] })
""
"== 4. carattribs: ButtOffset against ButtOffsetLOD0 =="
$d = $out | Where-Object { $_.buttOff -and $_.buttOff0 }
"cars with both: $(@($d).Count); ButtOffset = 0: $(@($d | Where-Object { (Len $_.buttOff) -eq 0 }).Count); ButtOffsetLOD0 = 0: $(@($d | Where-Object { (Len $_.buttOff0) -eq 0 }).Count); equal: $(@($d | Where-Object { (Len (Sub $_.buttOff $_.buttOff0)) -lt 0.0005 }).Count)"
Stat 'ButtOffset.x (toward outside)' ($d | ForEach-Object { if ($_.side -eq 'L') { -$_.buttOff[0] } else { $_.buttOff[0] } })
Stat 'ButtOffset.y' ($d | ForEach-Object { $_.buttOff[1] })
Stat 'ButtOffset.z' ($d | ForEach-Object { $_.buttOff[2] })
Stat 'ButtOffsetLOD0.x (toward outside)' ($d | ForEach-Object { if ($_.side -eq 'L') { -$_.buttOff0[0] } else { $_.buttOff0[0] } })
Stat 'ButtOffsetLOD0.y' ($d | ForEach-Object { $_.buttOff0[1] })
Stat 'ButtOffsetLOD0.z' ($d | ForEach-Object { $_.buttOff0[2] })
Stat 'LOD0 - LOD1..5 offset, distance' ($d | ForEach-Object { Len (Sub $_.buttOff0 $_.buttOff) })
"hand offsets non zero: L $(@($out | Where-Object { $_.lhOff -and (Len $_.lhOff) -gt 0 }).Count), R $(@($out | Where-Object { $_.rhOff -and (Len $_.rhOff) -gt 0 }).Count), L LOD0 $(@($out | Where-Object { $_.lhOff0 -and (Len $_.lhOff0) -gt 0 }).Count), R LOD0 $(@($out | Where-Object { $_.rhOff0 -and (Len $_.rhOff0) -gt 0 }).Count)"
""
"== 5. final seat point (anchor + offset) against the seat of each model =="
$e = $out | Where-Object { $_.buttOff -and $_.buttOff0 -and $_.seatMainSec -and $_.seatCkSec }
Stat '(butt+ButtOffset) x - main seat x, signed out' ($e | ForEach-Object { $sg = [Math]::Sign($_.wheelCk[0]); $sg * (($_.buttMain[0] + $_.buttOff[0]) - $_.seatMainSec[0]) })
Stat '(butt+ButtOffsetLOD0) x - cockpit seat x' ($e | ForEach-Object { $sg = [Math]::Sign($_.wheelCk[0]); $sg * (($_.buttCk[0] + $_.buttOff0[0]) - $_.seatCkSec[0]) })
Stat '(butt+ButtOffset) z - main seat z' ($e | ForEach-Object { ($_.buttMain[2] + $_.buttOff[2]) - $_.seatMainSec[2] })
Stat '(butt+ButtOffsetLOD0) z - cockpit seat z' ($e | ForEach-Object { ($_.buttCk[2] + $_.buttOff0[2]) - $_.seatCkSec[2] })
""
"== 6. main hand anchors and shifter =="
Stat 'hand anchors: half distance / none' ($out | ForEach-Object { [Math]::Abs($_.rhMain[0] - $_.lhMain[0]) / 2 })
Stat 'hand anchors mid x - wheel main x' ($out | ForEach-Object { (($_.rhMain[0] + $_.lhMain[0]) / 2) - $_.wheelMain[0] })
Stat 'hand anchors y - wheel main y' ($out | ForEach-Object { $_.lhMain[1] - $_.wheelMain[1] })
Stat 'hand anchors z - wheel main z' ($out | ForEach-Object { $_.lhMain[2] - $_.wheelMain[2] })
Stat 'shifter main - shifter cockpit, distance' ($out | ForEach-Object { Len (Sub $_.shiftMain $_.shiftCk) })
"main anchors all zero: wheel $(@($out | Where-Object { (Len $_.wheelMain) -eq 0 }).Count), butt $(@($out | Where-Object { (Len $_.buttMain) -eq 0 }).Count); cockpit LOD0: pivot $(@($out | Where-Object { (Len $_.wheelCk) -eq 0 }).Count), butt $(@($out | Where-Object { (Len $_.buttCk) -eq 0 }).Count)"

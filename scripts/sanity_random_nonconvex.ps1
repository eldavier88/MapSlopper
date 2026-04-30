# Random non-convex map sanity:
#   * Generates simple non-convex (star-shaped) polygons with random radii
#   * Random heightmap painted with axis-aligned stripes / blobs
#   * Builds the .map with the CLI, runs q3map2 -meta -leaktest
#   * Asserts BSP > 1KB and no leak
#   * Loops until -Streak (default 10) consecutive successes
#
# Usage:  pwsh -NoProfile -File scripts/sanity_random_nonconvex.ps1
#         pwsh -NoProfile -File scripts/sanity_random_nonconvex.ps1 -Streak 20
[CmdletBinding()]
param(
    [string]$Q3Map2 = "$env:TEMP\netradiant-custom\q3map2.exe",
    [string]$Cli   = "src/MapSlopper.Cli/bin/Debug/net5.0/MapSlopper.Cli.exe",
    [int]$Streak   = 10,
    [int]$Seed     = 0,
    [int]$MaxAttempts = 60,
    [switch]$Uniform
)
$ErrorActionPreference = 'Stop'

$work = Join-Path $env:TEMP "mapslopper-rand"
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $work,"$work\baseq3","$work\baseq3\scripts","$work\baseq3\maps" | Out-Null
Copy-Item assets/baseq3/scripts/mapslopper.shader "$work\baseq3\scripts\mapslopper.shader" -Force

if ($Seed -eq 0) { $Seed = (Get-Random -Minimum 1 -Maximum 999999) }
Write-Host "Seed: $Seed" -ForegroundColor Cyan
$rand = New-Object System.Random($Seed)

function R-Range([double]$lo, [double]$hi) { return $lo + $rand.NextDouble() * ($hi - $lo) }
function R-Int([int]$lo, [int]$hi)         { return $rand.Next($lo, $hi + 1) }

function New-StarPolygon {
    # Star-shaped polygon: pick N angles spread around centre, jitter each,
    # then assign random radii in [rMin, rMax]. CCW order = ascending angle.
    param([int]$N, [double]$Cx, [double]$Cy, [double]$RMin, [double]$RMax)
    $angles = @()
    for ($i = 0; $i -lt $N; $i++) {
        $base = ($i / $N) * [Math]::PI * 2.0
        $jitter = (R-Range -0.4 0.4) * ([Math]::PI * 2.0 / $N)
        $angles += ($base + $jitter)
    }
    $verts = @()
    foreach ($a in $angles) {
        $r = R-Range $RMin $RMax
        $verts += ,@([Math]::Round($Cx + [Math]::Cos($a) * $r), [Math]::Round($Cy + [Math]::Sin($a) * $r))
    }
    return ,$verts
}

function Test-PolygonSimple {
    # Reject self-intersecting polygons. O(N^2) segment intersection check.
    param([object[]]$Verts)
    $n = $Verts.Count
    function CrossSeg($p, $q, $r) {
        return ($q[0] - $p[0]) * ($r[1] - $p[1]) - ($q[1] - $p[1]) * ($r[0] - $p[0])
    }
    function SegIntersect($a, $b, $c, $d) {
        $d1 = CrossSeg $c $d $a; $d2 = CrossSeg $c $d $b
        $d3 = CrossSeg $a $b $c; $d4 = CrossSeg $a $b $d
        if ((($d1 -gt 0 -and $d2 -lt 0) -or ($d1 -lt 0 -and $d2 -gt 0)) -and
            (($d3 -gt 0 -and $d4 -lt 0) -or ($d3 -lt 0 -and $d4 -gt 0))) { return $true }
        return $false
    }
    for ($i = 0; $i -lt $n; $i++) {
        $a = $Verts[$i]; $b = $Verts[($i + 1) % $n]
        for ($j = $i + 2; $j -lt $n; $j++) {
            if ($i -eq 0 -and $j -eq ($n - 1)) { continue }
            $c = $Verts[$j]; $d = $Verts[($j + 1) % $n]
            if (SegIntersect $a $b $c $d) { return $false }
        }
    }
    return $true
}

function Test-IsCcw {
    param([object[]]$Verts)
    $area = 0.0
    for ($i = 0; $i -lt $Verts.Count; $i++) {
        $a = $Verts[$i]; $b = $Verts[($i + 1) % $Verts.Count]
        $area += $a[0] * $b[1] - $b[0] * $a[1]
    }
    return ($area -gt 0)
}

function Test-IsConvex {
    param([object[]]$Verts)
    $sign = 0
    for ($i = 0; $i -lt $Verts.Count; $i++) {
        $a = $Verts[$i]; $b = $Verts[($i + 1) % $Verts.Count]; $c = $Verts[($i + 2) % $Verts.Count]
        $cr = ($b[0] - $a[0]) * ($c[1] - $b[1]) - ($b[1] - $a[1]) * ($c[0] - $b[0])
        if ([Math]::Abs($cr) -lt 1e-6) { continue }
        $s = if ($cr -gt 0) { 1 } else { -1 }
        if ($sign -eq 0) { $sign = $s }
        elseif ($sign -ne $s) { return $false }
    }
    return $true
}

function Make-Proj {
    param([object[]]$Verts, [scriptblock]$HeightFn)
    $points = @(); $ids = @()
    foreach ($v in $Verts) {
        $id = [guid]::NewGuid().ToString()
        $ids += $id
        $points += [pscustomobject]@{ id = $id; x = [double]$v[0]; y = [double]$v[1] }
    }
    $n = $Verts.Count
    $edges = @()
    for ($i = 0; $i -lt $n; $i++) {
        $edges += [pscustomobject]@{ a = $ids[$i]; b = $ids[($i + 1) % $n] }
    }
    $cellSize = 32; $hmW = 128; $hmH = 128
    $cx = 4096; $cy = 4096
    $bytes = New-Object byte[] ($hmW * $hmH * 2)
    for ($yy = 0; $yy -lt $hmH; $yy++) {
        for ($xx = 0; $xx -lt $hmW; $xx++) {
            $wx = $cx - ($hmW * $cellSize / 2) + ($xx + 0.5) * $cellSize
            $wy = $cy - ($hmH * $cellSize / 2) + ($yy + 0.5) * $cellSize
            $h = & $HeightFn $wx $wy
            $h = [int]$h
            if ($h -lt 0) { $h = 0 }
            if ($h -gt 200) { $h = 200 }
            $idx = ($yy*$hmW + $xx) * 2
            $bytes[$idx]     = [byte]($h -band 0xFF)
            $bytes[$idx + 1] = [byte](($h -shr 8) -band 0xFF)
        }
    }
    return [pscustomobject]@{
        formatVersion = 1
        outline = [pscustomobject]@{ points = $points; edges = $edges }
        heightmap = [pscustomobject]@{
            width = $hmW; height = $hmH; cellSize = $cellSize
            originX = $cx - ($hmW * $cellSize / 2); originY = $cy - ($hmH * $cellSize / 2)
            dataBase64 = [Convert]::ToBase64String($bytes)
        }
        ceilingHeight = 256; wallThickness = 16
        floorTexture = "random/floor"; wallTexture = "random/wall"; ceilingTexture = "random/ceiling"
        playerStartOverride = $null
        lightSpacing = 800; lightIntensity = 300; lightInsetFromCeiling = 16
        ceilingThickness = 16; floorBaseThickness = 16
    }
}

function New-RandomHeightFn {
    param([switch]$Uniform)
    if ($Uniform) {
        $u = R-Int 32 180
        return { param($wx, $wy) return $u }.GetNewClosure()
    }
    # Composes a few rectangular paint stripes into a height function.
    $stripes = @()
    $count = R-Int 1 5
    for ($i = 0; $i -lt $count; $i++) {
        $stripes += [pscustomobject]@{
            x0 = (R-Range 3500 4500); y0 = (R-Range 3500 4500)
            x1 = (R-Range 3500 4500); y1 = (R-Range 3500 4500)
            h  = (R-Int 8 180)
        }
    }
    $sb = {
        param($wx, $wy)
        $h = 32
        foreach ($s in $stripes) {
            $xa = [Math]::Min($s.x0, $s.x1); $xb = [Math]::Max($s.x0, $s.x1)
            $ya = [Math]::Min($s.y0, $s.y1); $yb = [Math]::Max($s.y0, $s.y1)
            if ($wx -ge $xa -and $wx -le $xb -and $wy -ge $ya -and $wy -le $yb) {
                if ($s.h -gt $h) { $h = $s.h }
            }
        }
        return $h
    }.GetNewClosure()
    return $sb
}

function Run-Case {
    param([int]$Index, [object[]]$Verts, [scriptblock]$HeightFn)
    $name = "rand{0:D3}" -f $Index
    $project = Make-Proj -Verts $Verts -HeightFn $HeightFn
    $projPath = "$work\$name.json"
    $mapPath  = "$work\baseq3\maps\$name.map"
    $project | ConvertTo-Json -Depth 10 | Set-Content $projPath -Encoding UTF8

    $cliOut = & $Cli build $projPath -o $mapPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host ("[{0}] CLI FAIL: {1}" -f $name, ($cliOut -join ' ')) -ForegroundColor Red
        return $false
    }
    $brushCount = (Select-String -Path $mapPath -Pattern '^// brush ' -SimpleMatch:$false).Count

    $q3Out = & $Q3Map2 -fs_basepath $work -fs_game baseq3 -meta -leaktest $mapPath 2>&1
    $bspPath = [System.IO.Path]::ChangeExtension($mapPath, '.bsp')
    $bspSize = if (Test-Path $bspPath) { (Get-Item $bspPath).Length } else { 0 }
    $leaked  = ($q3Out -match 'LEAKED')
    $badPlane = ($q3Out -match 'FloatPlane')
    $maxBuild = ($q3Out -match 'MAX_BUILD_SIDES|MAX_MAP_DRAW_SURFS')
    $okBsp = ($bspSize -gt 1024 -and -not $leaked -and -not $badPlane -and -not $maxBuild)

    if ($okBsp) {
        Write-Host ("[{0}] OK   verts={1} brushes={2} bsp={3}" -f $name, $Verts.Count, $brushCount, $bspSize) -ForegroundColor Green
        return $true
    }
    $reason = @()
    if ($bspSize -le 1024) { $reason += "bsp=$bspSize" }
    if ($leaked)  { $reason += "LEAKED" }
    if ($badPlane){ $reason += "FloatPlane" }
    if ($maxBuild){ $reason += "MAX_BUILD" }
    Write-Host ("[{0}] FAIL verts={1} brushes={2} bsp={3} reason={4}" -f $name, $Verts.Count, $brushCount, $bspSize, ($reason -join ',')) -ForegroundColor Red
    Write-Host "  saved: $projPath  $mapPath" -ForegroundColor Yellow
    return $false
}

# Main loop: keep generating until $Streak in a row succeed.
$consecutive = 0
$total       = 0
$attempt     = 0
while ($consecutive -lt $Streak -and $attempt -lt $MaxAttempts) {
    $attempt++

    # Generate a non-convex star polygon.
    $verts = $null
    for ($try = 0; $try -lt 20; $try++) {
        $n = R-Int 6 12
        $candidate = New-StarPolygon -N $n -Cx 4000 -Cy 4000 -RMin 200 -RMax 700
        if (-not (Test-IsCcw -Verts $candidate)) { continue }
        if (-not (Test-PolygonSimple -Verts $candidate)) { continue }
        if (Test-IsConvex -Verts $candidate) { continue }   # require non-convex
        $verts = $candidate
        break
    }
    if ($null -eq $verts) {
        Write-Host "Could not generate valid non-convex polygon after 20 tries" -ForegroundColor Yellow
        continue
    }

    $hfn = New-RandomHeightFn -Uniform:$Uniform
    $ok  = Run-Case -Index $attempt -Verts $verts -HeightFn $hfn
    $total++
    if ($ok) { $consecutive++ } else { $consecutive = 0 }
}

Write-Host ""
if ($consecutive -ge $Streak) {
    Write-Host ("PASS: {0} consecutive successes (out of {1} attempts)" -f $Streak, $attempt) -ForegroundColor Green
    exit 0
} else {
    Write-Host ("FAIL: only reached {0} consecutive (target {1}) in {2} attempts" -f $consecutive, $Streak, $attempt) -ForegroundColor Red
    exit 1
}

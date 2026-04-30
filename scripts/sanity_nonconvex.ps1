# Non-convex sanity tests:
#   * L-shape   : 6 vertices, uniform floor   -> 1 ceiling/piece * 2 + walls
#   * U-shape   : 8 vertices, uniform floor
#   * spiral    : 12 vertices, NON-uniform floor (different paint per arm)
#
# Asserts:
#   1. CLI build succeeds.
#   2. q3map2 -meta accepts the .map (>= 1 KB BSP).
#   3. Brush count is "small" (heuristic upper bound) - non-convex polygons
#      can't always hit the convex-N+2 ideal but should not explode.
#   4. info_player_start origin Z corresponds to the highest paint cell.
[CmdletBinding()]
param(
    [string]$Q3Map2 = "$env:TEMP\netradiant-custom\q3map2.exe",
    [string]$Cli = "src/MapSlopper.Cli/bin/Debug/net5.0/MapSlopper.Cli.exe"
)
$ErrorActionPreference = 'Stop'

$work = Join-Path $env:TEMP "mapslopper-nonconvex"
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $work,"$work\baseq3","$work\baseq3\scripts","$work\maps" | Out-Null
Copy-Item assets/baseq3/scripts/mapslopper.shader "$work\baseq3\scripts\mapslopper.shader" -Force

function Make-Proj {
    param(
        [Parameter(Mandatory)] $Verts,        # array of [double[]]@(x,y)
        [Parameter(Mandatory)] $HeightCells   # 2D array of ushort or scalar
    )
    $points = @()
    $ids    = @()
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
    if ($HeightCells -is [int]) {
        for ($i = 0; $i -lt $hmW*$hmH; $i++) {
            $bytes[$i*2]   = [byte]($HeightCells -band 0xFF)
            $bytes[$i*2+1] = [byte](($HeightCells -shr 8) -band 0xFF)
        }
    } else {
        # ScriptBlock taking (worldX, worldY) -> ushort
        for ($yy = 0; $yy -lt $hmH; $yy++) {
            for ($xx = 0; $xx -lt $hmW; $xx++) {
                $wx = $cx - ($hmW * $cellSize / 2) + ($xx + 0.5) * $cellSize
                $wy = $cy - ($hmH * $cellSize / 2) + ($yy + 0.5) * $cellSize
                $h  = & $HeightCells $wx $wy
                $idx = ($yy*$hmW + $xx) * 2
                $bytes[$idx]     = [byte]($h -band 0xFF)
                $bytes[$idx + 1] = [byte](($h -shr 8) -band 0xFF)
            }
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

function Run-Case {
    param([string]$Name, [object]$Project, [int]$MaxBrushes, [int]$MinBrushes = 1, [Nullable[int]]$ExpectMaxStartZ = $null)
    $projPath = "$work\$Name.json"
    $mapPath  = "$work\maps\$Name.map"
    $Project | ConvertTo-Json -Depth 10 | Set-Content $projPath -Encoding UTF8

    $cliOut = & $Cli build $projPath -o $mapPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[$Name] CLI FAIL: $cliOut" -ForegroundColor Red
        return $false
    }
    $brushCount = (Select-String -Path $mapPath -Pattern '^// brush ' -SimpleMatch:$false).Count
    $playerOriginLine = Select-String -Path $mapPath -Pattern '"origin"\s+"[^"]+"' | ForEach-Object {
        # Find first one after a "info_player_start" line
        $_
    } | Select-Object -First 1

    # Find player_start origin:
    $mapText = Get-Content -Raw $mapPath
    $pStart = [regex]::Match($mapText, '"classname"\s+"info_player_start"\s*\r?\n\s*"origin"\s+"([^"]+)"').Groups[1].Value
    $startZ = $null
    if ($pStart) {
        $parts = $pStart.Split(' ')
        if ($parts.Length -eq 3) { $startZ = [double]$parts[2] }
    }

    $q3Out = & $Q3Map2 -fs_basepath $work -fs_game baseq3 -meta $mapPath 2>&1
    $bspPath = [System.IO.Path]::ChangeExtension($mapPath, '.bsp')
    $bspSize = if (Test-Path $bspPath) { (Get-Item $bspPath).Length } else { 0 }

    $okCount = ($brushCount -le $MaxBrushes -and $brushCount -ge $MinBrushes)
    $okBsp   = ($bspSize -gt 1024)
    $okZ     = if ($ExpectMaxStartZ -ne $null) { ($startZ -ne $null -and [Math]::Abs($startZ - $ExpectMaxStartZ) -lt 32) } else { $true }

    $tag = if ($okCount -and $okBsp -and $okZ) { "OK" } else { "FAIL" }
    $color = if ($okCount -and $okBsp -and $okZ) { "Green" } else { "Red" }
    $expectStr = if ($ExpectMaxStartZ -ne $null) { " expectedZ~$ExpectMaxStartZ gotZ=$startZ" } else { "" }
    Write-Host ("[$Name] {0}  brushes={1} (<= {2})  bsp={3}{4}" -f $tag, $brushCount, $MaxBrushes, $bspSize, $expectStr) -ForegroundColor $color
    return ($okCount -and $okBsp -and $okZ)
}

# L-shape (6 verts, CCW):
#   __________
#  |          |
#  |          |
#  |   ___    |
#  |  |   |   |
#  |  |   |___|
#  |  |
#  |__|
#
# Actually simpler: outer rectangle with one notch.
$L = @(
    @(3500,3500), @(4400,3500), @(4400,4000), @(4000,4000),
    @(4000,4500), @(3500,4500)
)
Write-Host "--- L-shape uniform floor (6 verts) ---" -ForegroundColor Cyan
$ok1 = Run-Case "Lshape" (Make-Proj -Verts $L -HeightCells 64) -MaxBrushes 12 -ExpectMaxStartZ 72

# U-shape (8 verts CCW):
#  ___________
# |           |
# |           |
# |   _____   |
# |  |     |  |
# |  |     |  |
# |__|     |__|
$U = @(
    @(3500,3500), @(3800,3500), @(3800,4200), @(4200,4200),
    @(4200,3500), @(4500,3500), @(4500,4500), @(3500,4500)
)
Write-Host "--- U-shape uniform floor (8 verts) ---" -ForegroundColor Cyan
$ok2 = Run-Case "Ushape" (Make-Proj -Verts $U -HeightCells 64) -MaxBrushes 16 -ExpectMaxStartZ 72

# Spiral with varying heights: 12-vert spiral; height = paint based on how
# far along the spiral arm you are.
# Plus-shape (cross), 12 verts CCW with three radial height bands.
$S = @(
    @(3800,3500), @(4200,3500),
    @(4200,3800), @(4500,3800),
    @(4500,4200), @(4200,4200),
    @(4200,4500), @(3800,4500),
    @(3800,4200), @(3500,4200),
    @(3500,3800), @(3800,3800)
)
$spiralHeights = {
    param($wx, $wy)
    $r = [Math]::Max([Math]::Abs($wx - 4000), [Math]::Abs($wy - 4000))
    if ($r -gt 400) { return 32 }
    if ($r -gt 200) { return 96 }
    return 200
}
Write-Host "--- Plus-shape non-uniform floor (12 verts, 3 height bands) ---" -ForegroundColor Cyan
$ok3 = Run-Case "spiral" (Make-Proj -Verts $S -HeightCells $spiralHeights) -MaxBrushes 90 -ExpectMaxStartZ 208

if ($ok1 -and $ok2 -and $ok3) { exit 0 } else { exit 1 }

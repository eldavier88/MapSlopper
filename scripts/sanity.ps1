# Sanity tests for brush merging. Ensures:
#   * triangle (3 verts, flat floor) -> 5 brushes (1 floor + 1 ceiling + 3 walls)
#   * quad     (4 verts, flat floor) -> 6 brushes
#   * N-gon    (N verts, flat floor) -> N+2 brushes
# Also runs each through q3map2 -meta to verify q3map2 still accepts them.

[CmdletBinding()]
param(
    [string]$Q3Map2 = "$env:TEMP\netradiant-custom\q3map2.exe",
    [string]$Cli = "src/MapSlopper.Cli/bin/Debug/net5.0/MapSlopper.Cli.exe"
)
$ErrorActionPreference = 'Stop'

$work = Join-Path $env:TEMP "mapslopper-sanity"
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $work,"$work\baseq3","$work\baseq3\scripts","$work\maps" | Out-Null
Copy-Item assets/baseq3/scripts/mapslopper.shader "$work\baseq3\scripts\mapslopper.shader" -Force

function Make-Proj {
    param([int]$N, [double]$Cx = 4096, [double]$Cy = 4096, [double]$R = 512, [int]$FloorH = 64)
    $points = @()
    $ids = @()
    for ($i = 0; $i -lt $N; $i++) {
        $a = $i * 2 * [Math]::PI / $N
        $x = [Math]::Round($Cx + [Math]::Cos($a) * $R, 0)
        $y = [Math]::Round($Cy + [Math]::Sin($a) * $R, 0)
        $id = [guid]::NewGuid().ToString()
        $ids += $id
        $points += [pscustomobject]@{ id = $id; x = $x; y = $y }
    }
    $edges = @()
    for ($i = 0; $i -lt $N; $i++) {
        $edges += [pscustomobject]@{ a = $ids[$i]; b = $ids[($i + 1) % $N] }
    }
    # Heightmap: covers whole polygon AABB with FloorH everywhere.
    $cellSize = 32; $hmW = 96; $hmH = 96
    $bytes = New-Object byte[] ($hmW * $hmH * 2)
    for ($i = 0; $i -lt $hmW * $hmH; $i++) {
        $bytes[$i*2]   = [byte]($FloorH -band 0xFF)
        $bytes[$i*2+1] = [byte](($FloorH -shr 8) -band 0xFF)
    }
    [pscustomobject]@{
        formatVersion = 1
        outline = [pscustomobject]@{ points = $points; edges = $edges }
        heightmap = [pscustomobject]@{
            width = $hmW; height = $hmH; cellSize = $cellSize
            originX = $Cx - ($hmW * $cellSize / 2); originY = $Cy - ($hmH * $cellSize / 2)
            dataBase64 = [Convert]::ToBase64String($bytes)
        }
        ceilingHeight = 256; wallThickness = 16
        floorTexture = "random/floor"; wallTexture = "random/wall"; ceilingTexture = "random/ceiling"
        playerStartOverride = $null
        lightSpacing = 800; lightIntensity = 300; lightInsetFromCeiling = 16
        ceilingThickness = 16; floorBaseThickness = 16
    }
}

$cases = @(
    @{ N = 3;  Expected = 5  },
    @{ N = 4;  Expected = 6  },
    @{ N = 6;  Expected = 8  },
    @{ N = 10; Expected = 12 }
)

$allOk = $true
foreach ($c in $cases) {
    $proj = Make-Proj -N $c.N
    $projPath = "$work\p$($c.N).json"
    $mapPath  = "$work\maps\p$($c.N).map"
    $proj | ConvertTo-Json -Depth 10 | Set-Content $projPath -Encoding UTF8

    $cliOut = & $Cli build $projPath -o $mapPath 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Host "[$($c.N)] CLI fail: $cliOut" -ForegroundColor Red; $allOk = $false; continue }

    # Count brush tokens "// brush " in the .map.
    $brushCount = (Select-String -Path $mapPath -Pattern '^// brush ' -SimpleMatch:$false).Count

    # q3map2 sanity.
    $q3Out = & $Q3Map2 -fs_basepath $work -fs_game baseq3 -meta $mapPath 2>&1
    $q3Combined = $q3Out -join "`n"
    $metaSurf = ([regex]::Match($q3Combined, '(\d+)\s+total meta surfaces')).Groups[1].Value
    $bspSize = if (Test-Path ([System.IO.Path]::ChangeExtension($mapPath, '.bsp'))) {
        (Get-Item ([System.IO.Path]::ChangeExtension($mapPath, '.bsp'))).Length
    } else { 0 }

    $brushOk = ($brushCount -eq $c.Expected)
    # q3map2 emits one meta surface per brush face that survives merging. For our
    # convex prism brushes the count is N+2 faces per brush; checking that it's
    # >= expected brush count and bsp > 1KB is enough to confirm acceptance.
    $q3Ok = ([int]$metaSurf -ge $c.Expected) -and ($bspSize -gt 1024)
    $tag = if ($brushOk -and $q3Ok) { "OK" } else { "FAIL" }
    $color = if ($brushOk -and $q3Ok) { "Green" } else { "Red" }
    Write-Host ("[N={0}] {1}  brushes={2} (expected {3})  metaSurfaces={4}  bsp={5}" -f `
        $c.N, $tag, $brushCount, $c.Expected, $metaSurf, $bspSize) -ForegroundColor $color
    if (-not ($brushOk -and $q3Ok)) { $allOk = $false }
}

if (-not $allOk) { exit 1 }
exit 0

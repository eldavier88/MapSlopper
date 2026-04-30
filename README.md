# MapSlopper

A 2.5D level editor that exports leak-free Quake 3 `.map` files from a vector outline plus a heightmap.

## What it is

MapSlopper is a focused, opinionated editor for building Quake 3 levels in a 2.5D workflow:
you draw the floor plan as a 2D outline, paint per-cell heights with a heightmap brush, and the
generator emits a closed, convex, brush-based `.map` file ready to compile with `q3map2`.

The output is intentionally boring: standard Quake 3 brush format, classname-first entity
emission, invariant-culture numbers, integer coordinates emitted bare. This makes the result
diff-friendly and compatible with every tool in the q3map2 ecosystem.

## Player reference

All defaults are tuned to the Quake 3 player:

- Player bounding box: **32 × 32 × 64** units
- Default heightmap `cellSize = 32` — exactly **one player width** per cell
- Default `ceilingHeight = 256` — comfortable jumping height

## Pipeline

```
   2D outline graph        Heightmap (uint16 grid)
   (points + edges)        (per-cell floor height)
          |                          |
          +------------+-------------+
                       |
                       v
            +----------------------+
            |  GeometryGenerator   |
            +----------------------+
              |        |        |
              v        v        v
           walls   floor +   info_player_start
        (mitered  ceiling   + light entities
        prism ring) cells   (every lightSpacing units)
              \      |      /
               \     |     /
                v    v    v
              +-------------+
              |  MapWriter  |  -->  .map  -->  q3map2  -->  .bsp
              +-------------+
```

Every brush produced is a closed convex prism. Floor and ceiling cells exactly tile the outline
interior; walls form a continuous outward-mitered ring. The generator is leak-free by
construction.

## Quick start

Prerequisites: **.NET 5.0 SDK**.

```sh
# Build everything
dotnet build

# Run the GUI editor
dotnet run --project src/MapSlopper.Gui

# Build the included sample to a .map
dotnet run --project src/MapSlopper.Cli -- build samples/square-room/square-room.mapsproj.json -o square-room.map

# Validate a project (geometry sanity, no output written)
dotnet run --project src/MapSlopper.Cli -- validate samples/square-room/square-room.mapsproj.json

# Compile the .map to a .bsp with q3map2 (paths depend on your install)
q3map2 -fs_basepath /path/to/q3 -fs_game baseq3 square-room.map
q3map2 -fs_basepath /path/to/q3 -fs_game baseq3 -vis square-room.map
q3map2 -fs_basepath /path/to/q3 -fs_game baseq3 -light -fast square-room.map
```

## Repo layout

```
src/
  MapSlopper.Core/   geometry, brush model, .map writer, project JSON I/O
  MapSlopper.Gui/    Avalonia 0.10.21 editor (2D + 3D preview tabs)
  MapSlopper.Cli/    `build` and `validate` commands
tests/
  MapSlopper.Core.Tests/         xunit 2.4.2, 44 unit tests
  MapSlopper.Integration.Tests/  q3map2 round-trip (gated on MAPSLOPPER_Q3MAP2)
samples/
  square-room/      minimal 256x256 room
  l-shaped/         L-shaped outline showing miter handling
  stepped-floor/    8x8 grid demonstrating per-cell heights
docs/               format specs, algorithm notes, manual test checklist
```

## CLI commands

```
mapslopper build    <project.mapsproj.json> [-o <out.map>]
mapslopper validate <project.mapsproj.json>
mapslopper help
```

- `build` — generates geometry and writes `.map`. Defaults output to the input path with the
  extension swapped to `.map`. Emits a count of brushes and entities on success; prints issues
  and exits non-zero on failure.
- `validate` — runs the generator, reports issues, prints `OK` / `INVALID`. Exit code reflects
  validity (useful for CI smoke tests).

## Default texture choice: `common/caulk`

Every default texture (floor, wall, ceiling) is `common/caulk`. Caulk ships with q3map2 itself,
so the included samples have **zero asset dependencies** — you can compile and load them on a
clean Quake 3 install with no PK3 setup. Replace these with shader names of your choice once
you wire up real textures.

## Default `cellSize = 32`

A heightmap cell is one player wide. Picking 32 makes the cell grid line up naturally with the
default outline grid and keeps generated brush counts tractable while still allowing fine
height variation.

## Status

Phase 10 — documentation and sample expansion. Earlier phases delivered:

- Phase 1 — Core geometry primitives (`Vec2`, `Vec3`, `Polygon2D`, triangulation, clipping)
- Phase 2 — Brush model, planes, `MapWriter`
- Phase 3 — Outline graph and project document
- Phase 4 — Heightmap (`Heightmap16`) and levels mapping
- Phase 5 — Wall generator (mitered prism ring)
- Phase 6 — Floor / ceiling generator (per-cell clipped brushes)
- Phase 7 — Entity placement (`info_player_start`, lights)
- Phase 8 — CLI (`build`, `validate`)
- Phase 9 — GUI (Avalonia 2D editor + 3D preview, 7 tools, undo/redo, save/open)
- Phase 10 — Docs + samples (this commit)

See [docs/algorithms.md](docs/algorithms.md), [docs/project-format.md](docs/project-format.md),
and [docs/map-export-spec.md](docs/map-export-spec.md) for details.

## License

MIT (placeholder) — see [LICENSE](LICENSE).

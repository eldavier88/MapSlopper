# MapSlopper

A 2.5D level editor that exports leak-free, brush-perfect Quake 3 `.map` files
ready for `q3map2` (NetRadiant-custom).

> Draw the floorplan as a closed vector outline, paint a 16-bit step heightmap,
> and out comes a fully convex, non-overlapping brush set with walls, floor,
> ceiling, lights, and a player start.

## Projects

| Project | Description |
| --- | --- |
| `MapSlopper.Core` | Pure library: data model, JSON I/O, geometry, brush generator, `.map` writer. (netstandard2.1) |
| `MapSlopper.Cli`  | Console tool: `build`/`validate` JSON projects → `.map`. |
| `MapSlopper.Gui`  | Avalonia 11 desktop app: 2D vector + heightmap editor and live 3D preview. |
| `MapSlopper.Core.Tests` | xUnit unit tests for Core. |
| `MapSlopper.Integration.Tests` | End-to-end tests that invoke `q3map2` to verify exports. |

## Quick start

```powershell
dotnet build
dotnet test
dotnet run --project src/MapSlopper.Gui
dotnet run --project src/MapSlopper.Cli -- build samples/square-room.mapsproj.json -o out.map
```

## Project file format

Plain JSON (see [docs/project-format.md](docs/project-format.md)).
The heightmap is encoded as base64 little-endian `ushort` values; everything
else is human-readable.

## Export format

A canonical Quake 3 `.map` file (entity 0 = `worldspawn`). See
[docs/map-export-spec.md](docs/map-export-spec.md).

## Status

Active development. See [docs/algorithms.md](docs/algorithms.md) for the
geometry generation strategy.

## License

MIT — see [LICENSE](LICENSE).

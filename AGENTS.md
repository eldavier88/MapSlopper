# AGENTS.md

## Cursor Cloud specific instructions

### Overview

MapSlopper is a 2.5D Quake 3 level editor targeting .NET 5.0 (legacy) and .NET 10.0 (modern). See `README.md` for full details.

### Build & run

- `dotnet build` builds the entire solution (Core, Cli, Gui, Gui.Legacy, tests).
- `dotnet run --project src/MapSlopper.Gui` runs the modern Avalonia 11 GUI (requires .NET 10).
- `dotnet run --project src/MapSlopper.Gui.Legacy` runs the legacy Avalonia 0.10 GUI (targets net5.0, builds with .NET 10 SDK).
- `dotnet run --project src/MapSlopper.Cli -f net10.0 -- build <project.json> -o <output.map>` builds a .map file.
- `dotnet run --project src/MapSlopper.Cli -f net10.0 -- validate <project.json>` validates a project.

### Tests

- Core unit tests: `dotnet test tests/MapSlopper.Core.Tests/MapSlopper.Core.Tests.csproj -f net10.0`
- Integration tests require `MAPSLOPPER_Q3MAP2` environment variable pointing to a q3map2 binary.
- **Gotcha**: The xunit v3 test runner sometimes hangs after all tests pass. All tests do complete — the process just doesn't exit cleanly. Use `timeout 30 dotnet test ...` or check output for "Passed"/"Failed" counts if the command seems stuck.
- The net5.0 TFM test runs require .NET 5 runtime which is not installed in Cloud Agent VMs (only .NET 10 SDK). Always use `-f net10.0` for test runs.

### Modern GUI SkiaSharp workaround

Avalonia.Skia transitively pulls `SkiaSharp.NativeAssets.Linux 2.88.9` (native version 88.1), but SkiaSharp 3.119.0 requires native version [119.0, 120.0). To run the modern GUI on Linux:
```bash
curl -Lo /tmp/skiasharp-linux.nupkg \
  "https://api.nuget.org/v3-flatcontainer/skiasharp.nativeassets.linux/3.119.0/skiasharp.nativeassets.linux.3.119.0.nupkg"
mkdir -p /tmp/skiasharp-native && unzip -o /tmp/skiasharp-linux.nupkg -d /tmp/skiasharp-native
cp /tmp/skiasharp-native/runtimes/linux-x64/native/libSkiaSharp.so \
  /root/.nuget/packages/skiasharp.nativeassets.linux/2.88.9/runtimes/linux-x64/native/libSkiaSharp.so
```
Then launch with: `LD_LIBRARY_PATH=src/MapSlopper.Gui/bin/Debug/net10.0/runtimes/linux-x64/native DISPLAY=:1 dotnet run --project src/MapSlopper.Gui`

### Legacy GUI (Gui.Legacy) notes

- Targets net5.0 with Avalonia 0.10.21. `Avalonia.Themes.Fluent` does NOT exist as a separate NuGet package in 0.10.x — it's bundled in the core Avalonia package.
- `FormattedText` API differs from Avalonia 11: brush goes to `DrawingContext.DrawText(brush, point, text)` not to the constructor.
- File dialogs use `OpenFileDialog`/`SaveFileDialog` (no `StorageProvider` API).
- `NumericUpDown.Value` is `double`, not `decimal?`.

# AGENTS.md

## Cursor Cloud specific instructions

### Overview

MapSlopper is a .NET 10 Avalonia desktop application (2.5D Quake 3 level editor). The repo has three main projects:
- `src/MapSlopper.Core` — geometry/brush library (no NuGet deps beyond BCL)
- `src/MapSlopper.Gui` — Avalonia 11.3 desktop editor
- `src/MapSlopper.Cli` — CLI with `build` and `validate` commands

### Build & Test

Standard commands (see README for details):
```
dotnet build
dotnet test tests/MapSlopper.Core.Tests --blame-hang-timeout 30s
dotnet run --project src/MapSlopper.Cli -- build samples/square-room/square-room.mapsproj.json -o /tmp/out.map
dotnet run --project src/MapSlopper.Cli -- validate samples/l-shaped/l-shaped.mapsproj.json
```

### Non-obvious caveats

1. **Test runner hangs after completion**: The xUnit test host process hangs indefinitely after all tests pass. Use `--blame-hang-timeout 30s` to force termination after the hang timeout. All 45 tests pass; the hang is a known VSTest/xUnit issue.

2. **SkiaSharp native library mismatch on Linux**: Avalonia.Skia transitively pulls `SkiaSharp.NativeAssets.Linux 2.88.9` (native version 88.1), but the project uses `SkiaSharp 3.119.0` which requires native version [119.0, 120.0). To run the GUI on Linux, you must replace the native library:
   ```bash
   # Download correct native assets
   curl -Lo /tmp/skiasharp-linux.nupkg \
     "https://api.nuget.org/v3-flatcontainer/skiasharp.nativeassets.linux/3.119.0/skiasharp.nativeassets.linux.3.119.0.nupkg"
   mkdir -p /tmp/skiasharp-native && unzip -o /tmp/skiasharp-linux.nupkg -d /tmp/skiasharp-native
   # Replace in NuGet cache (the runtime resolves from here)
   cp /tmp/skiasharp-native/runtimes/linux-x64/native/libSkiaSharp.so \
     /root/.nuget/packages/skiasharp.nativeassets.linux/2.88.9/runtimes/linux-x64/native/libSkiaSharp.so
   ```
   Then launch the GUI with:
   ```bash
   LD_LIBRARY_PATH=/workspace/src/MapSlopper.Gui/bin/Debug/net10.0/runtimes/linux-x64/native \
     DISPLAY=:1 dotnet run --project src/MapSlopper.Gui
   ```

3. **GUI requires a display server**: Use `DISPLAY=:1` (Xvfb is typically running on :1 in Cloud Agent VMs).

4. **Integration tests are optional**: `MapSlopper.Integration.Tests` requires `MAPSLOPPER_Q3MAP2` and `MAPSLOPPER_Q3_BASEPATH` environment variables pointing to a q3map2 binary. These are skipped when unset.

5. **.NET SDK version**: All projects target `net10.0`. The README says .NET 5 (outdated); the actual requirement is .NET 10.0 SDK.

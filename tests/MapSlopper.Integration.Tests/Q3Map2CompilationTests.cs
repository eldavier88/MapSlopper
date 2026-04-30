using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MapSlopper.Core.Export;
using MapSlopper.Core.Generation;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Project;
using Xunit;

namespace MapSlopper.Integration.Tests;

/// <summary>
/// q3map2 compilation + leak harness. These tests are gated on the
/// <c>MAPSLOPPER_Q3MAP2</c> environment variable, which must point at a
/// q3map2 executable. They are skipped when the variable is unset so the
/// suite is green on machines without a Quake 3 toolchain installed.
/// </summary>
public class Q3Map2CompilationTests
{
    private const string Q3Map2EnvVar = "MAPSLOPPER_Q3MAP2";
    private const string BasePathEnvVar = "MAPSLOPPER_Q3_BASEPATH";

    [SkippableFact]
    public void SquareRoom_Compiles_Without_Leaks()
    {
        var q3map2 = Environment.GetEnvironmentVariable(Q3Map2EnvVar);
        Skip.If(string.IsNullOrWhiteSpace(q3map2),
            $"{Q3Map2EnvVar} not set; skipping q3map2 integration test.");

        var project = MakeSquareRoomProject();
        var result = GeometryGenerator.Generate(project);
        Assert.True(result.Ok, string.Join("; ", result.Issues.Select(i => i.Message)));

        var workDir = Path.Combine(Path.GetTempPath(), "mapslopper-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            var mapPath = Path.Combine(workDir, "test.map");
            MapWriter.WriteToFile(result.Document!, mapPath);

            var basePath = Environment.GetEnvironmentVariable(BasePathEnvVar);
            var args = "-bsp -leaktest";
            if (!string.IsNullOrWhiteSpace(basePath))
                args = $"-fs_basepath \"{basePath}\" {args}";
            args += $" \"{mapPath}\"";

            var (exit, stdout, stderr) = Run(q3map2!, args, workDir);
            var combined = stdout + "\n" + stderr;
            Assert.True(exit == 0,
                $"q3map2 exit code = {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
            Assert.DoesNotContain("leaked", combined, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("MAX_MAP", combined, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("degenerate", combined, StringComparison.OrdinalIgnoreCase);
            // q3map2 emits a .bsp file alongside the .map on success.
            Assert.True(File.Exists(Path.ChangeExtension(mapPath, ".bsp")),
                "Expected .bsp output not found.");
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static (int Exit, string Stdout, string Stderr) Run(string exe, string args, string cwd)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = cwd,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {exe}.");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout, stderr);
    }

    private static MapSlopperProject MakeSquareRoomProject()
    {
        var p = new MapSlopperProject
        {
            CeilingHeight = 256,
            WallThickness = 16,
            CeilingThickness = 16,
            FloorBaseThickness = 16,
            FloorTexture = "common/caulk",
            WallTexture = "common/caulk",
            CeilingTexture = "common/caulk",
            LightSpacing = 800,
        };
        p.Heightmap = new MapSlopper.Core.Heightmap.Heightmap16(8, 8, 32, Vec2.Zero);
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
                p.Heightmap.Set(x, y, 64);
        var ids = new Guid[4];
        for (var i = 0; i < 4; i++) ids[i] = Guid.NewGuid();
        p.Outline.AddPoint(ids[0], new Vec2(0, 0));
        p.Outline.AddPoint(ids[1], new Vec2(256, 0));
        p.Outline.AddPoint(ids[2], new Vec2(256, 256));
        p.Outline.AddPoint(ids[3], new Vec2(0, 256));
        p.Outline.AddEdge(ids[0], ids[1]);
        p.Outline.AddEdge(ids[1], ids[2]);
        p.Outline.AddEdge(ids[2], ids[3]);
        p.Outline.AddEdge(ids[3], ids[0]);
        return p;
    }
}

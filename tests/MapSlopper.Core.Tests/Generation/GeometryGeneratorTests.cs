using System.IO;
using System.Linq;
using MapSlopper.Core.Export;
using MapSlopper.Core.Generation;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Outline;
using MapSlopper.Core.Project;
using Xunit;

namespace MapSlopper.Core.Tests.Generation;

public class GeometryGeneratorTests
{
    private static MapSlopperProject MakeSquareRoomProject(int hmSize = 8, double cellSize = 32, ushort floorH = 64)
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
        p.Heightmap = new MapSlopper.Core.Heightmap.Heightmap16(hmSize, hmSize, cellSize, Vec2.Zero);
        for (var y = 0; y < hmSize; y++)
            for (var x = 0; x < hmSize; x++)
                p.Heightmap.Set(x, y, floorH);

        // Square outline matching heightmap world bounds.
        var size = hmSize * cellSize;
        var ids = new System.Guid[4];
        for (var i = 0; i < 4; i++) ids[i] = System.Guid.NewGuid();
        p.Outline.AddPoint(ids[0], new Vec2(0, 0));
        p.Outline.AddPoint(ids[1], new Vec2(size, 0));
        p.Outline.AddPoint(ids[2], new Vec2(size, size));
        p.Outline.AddPoint(ids[3], new Vec2(0, size));
        p.Outline.AddEdge(ids[0], ids[1]);
        p.Outline.AddEdge(ids[1], ids[2]);
        p.Outline.AddEdge(ids[2], ids[3]);
        p.Outline.AddEdge(ids[3], ids[0]);
        return p;
    }

    [Fact]
    public void SquareRoom_Generates_Walls_Floor_Ceiling_AndPlayerStart()
    {
        var project = MakeSquareRoomProject();
        var result = GeometryGenerator.Generate(project);
        Assert.True(result.Ok, string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.Document);
        var doc = result.Document!;

        var startCount = doc.Entities.Count(e =>
            e.Properties.TryGetValue("classname", out var c) && c == "info_player_start");
        Assert.Equal(1, startCount);

        // Worldspawn must contain the 4 walls + a number of floor & ceiling pieces.
        var ws = doc.Worldspawn;
        Assert.True(ws.Brushes.Count >= 4 + 2, "expected at least walls + floor + ceiling brushes");
    }

    [Fact]
    public void SquareRoom_Empty_Outline_Reports_Fatal_Issue()
    {
        var p = new MapSlopperProject();
        var r = GeometryGenerator.Generate(p);
        Assert.False(r.Ok);
        Assert.Contains(r.Issues, i => i.IsFatal);
    }

    [Fact]
    public void SquareRoom_Generated_Map_Round_Trips_Through_Writer_Without_Throwing()
    {
        var project = MakeSquareRoomProject();
        var result = GeometryGenerator.Generate(project);
        Assert.NotNull(result.Document);
        var text = MapWriter.Write(result.Document!);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("info_player_start", text);
        Assert.Contains("worldspawn", text);
    }

    /// <summary>
    /// When cells are painted at a high raw value the per-piece floor base-slab
    /// must NOT extend from the absolute map bottom all the way up (the
    /// "2000u thick brush" bug). Verify that generation succeeds and that no
    /// floor brush has a Z bottom at the absolute floor base while its top is
    /// near the painted height (which would indicate the old 2000-unit-thick slab).
    /// </summary>
    [Fact]
    public void HighPaintedRoom_FloorBaseSlab_IsNotThick()
    {
        const ushort HighRaw = 2000;
        var project = MakeSquareRoomProject(hmSize: 4, cellSize: 32, floorH: HighRaw);
        double zFloorBase = -project.FloorBaseThickness; // -16

        var result = GeometryGenerator.Generate(project);
        Assert.True(result.Ok, string.Join("; ", result.Issues.Select(i => i.Message)));

        var doc = result.Document!;

        // The old bug: a floor brush with bottom at zFloorBase=-16 and top
        // at zFloorBase+HighRaw=1984, giving ~2000 units thickness.
        // With the fix the base-slab bottom is clamped to
        // max(zFloorBase, baseTop - StackOverlapRaw).
        // StackOverlapRaw = HeightQuantStep = 16.
        // So the maximum floor-slab span for HighRaw=2000 should be 16 units.
        // We check no floor brush's Z range spans from near zFloorBase to near HighRaw.

        foreach (var brush in doc.Worldspawn.Brushes)
        {
            double minZ = double.MaxValue;
            double maxZ = double.MinValue;
            foreach (var plane in brush.Planes)
            {
                foreach (var z in new[] { plane.P1.Z, plane.P2.Z, plane.P3.Z })
                {
                    if (z < minZ) minZ = z;
                    if (z > maxZ) maxZ = z;
                }
            }
            if (minZ >= maxZ - 0.001) continue;

            // Flag brushes whose bottom is near the absolute map floor
            // AND whose top is near the painted height — that's the bug pattern.
            var isNearAbsoluteBottom = minZ < zFloorBase + 1.0;
            var isNearPaintedHeight = maxZ > zFloorBase + HighRaw - 1.0;
            Assert.False(isNearAbsoluteBottom && isNearPaintedHeight,
                $"Brush spans from near absolute floor bottom ({minZ:F1}) to near painted height ({maxZ:F1}) " +
                $"— this is the 2000u thick brush bug. Z range: [{minZ:F1}, {maxZ:F1}]");
        }
    }
}

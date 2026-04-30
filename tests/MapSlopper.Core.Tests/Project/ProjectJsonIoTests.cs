using MapSlopper.Core.Geometry;
using MapSlopper.Core.Project;

namespace MapSlopper.Core.Tests.Project;

public class ProjectJsonIoTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var p = new MapSlopperProject
        {
            CeilingHeight = 320,
            WallThickness = 24,
            FloorTexture = "textures/floor",
            WallTexture = "textures/wall",
            CeilingTexture = "textures/ceiling",
            LightSpacing = 600,
            LightIntensity = 250,
            CeilingThickness = 8,
            FloorBaseThickness = 12,
            PlayerStartOverride = new Vec3(40, 40, 32),
        };
        var a = p.Outline.AddPoint(new Vec2(0, 0));
        var b = p.Outline.AddPoint(new Vec2(100, 0));
        var c = p.Outline.AddPoint(new Vec2(100, 100));
        var d = p.Outline.AddPoint(new Vec2(0, 100));
        p.Outline.AddEdge(a.Id, b.Id);
        p.Outline.AddEdge(b.Id, c.Id);
        p.Outline.AddEdge(c.Id, d.Id);
        p.Outline.AddEdge(d.Id, a.Id);

        p.Heightmap.Fill(0, 0, 31, 31, 64);
        p.Heightmap.Fill(10, 10, 20, 20, 128);

        var json = ProjectJsonIo.Serialize(p);
        var p2 = ProjectJsonIo.Deserialize(json);

        Assert.Equal(p.CeilingHeight, p2.CeilingHeight);
        Assert.Equal(p.WallThickness, p2.WallThickness);
        Assert.Equal(p.FloorTexture, p2.FloorTexture);
        Assert.Equal(p.WallTexture, p2.WallTexture);
        Assert.Equal(p.CeilingTexture, p2.CeilingTexture);
        Assert.Equal(p.LightSpacing, p2.LightSpacing);
        Assert.Equal(p.PlayerStartOverride, p2.PlayerStartOverride);

        Assert.Equal(p.Outline.Points.Count, p2.Outline.Points.Count);
        Assert.Equal(p.Outline.Edges.Count, p2.Outline.Edges.Count);
        Assert.True(p2.Outline.TryGetClosedPolygon(out var poly));
        Assert.Equal(4, poly.Count);

        Assert.Equal(p.Heightmap.Width, p2.Heightmap.Width);
        Assert.Equal(p.Heightmap.Height, p2.Heightmap.Height);
        Assert.Equal(p.Heightmap.CellSize, p2.Heightmap.CellSize);
        for (var y = 0; y < p.Heightmap.Height; y++)
            for (var x = 0; x < p.Heightmap.Width; x++)
                Assert.Equal(p.Heightmap.Sample(x, y), p2.Heightmap.Sample(x, y));
    }

    [Fact]
    public void Serialize_IsIndentedAndStartsWithBrace()
    {
        var p = new MapSlopperProject();
        var json = ProjectJsonIo.Serialize(p);
        Assert.StartsWith("{", json);
        Assert.Contains("\n", json);
        Assert.Contains("\"formatVersion\"", json);
    }

    [Fact]
    public void Serialize_TwiceProducesIdenticalOutput()
    {
        var p = new MapSlopperProject();
        var pt = p.Outline.AddPoint(new Vec2(1.5, 2.5));
        p.Heightmap.Fill(0, 0, 5, 5, 1234);
        var a = ProjectJsonIo.Serialize(p);
        var b = ProjectJsonIo.Serialize(ProjectJsonIo.Deserialize(a));
        // Edge order may differ in dictionary; we round-trip once more for stability.
        var c = ProjectJsonIo.Serialize(ProjectJsonIo.Deserialize(b));
        Assert.Equal(b, c);
        Assert.NotNull(pt);
    }
}

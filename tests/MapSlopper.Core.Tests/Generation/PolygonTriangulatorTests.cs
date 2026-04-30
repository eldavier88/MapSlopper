using MapSlopper.Core.Geometry;
using Xunit;

namespace MapSlopper.Core.Tests.Generation;

public class PolygonTriangulatorTests
{
    [Fact]
    public void Square_Triangulates_Into_TwoTriangles()
    {
        var poly = new[]
        {
            new Vec2(0, 0),
            new Vec2(64, 0),
            new Vec2(64, 64),
            new Vec2(0, 64),
        };
        var tris = PolygonTriangulator.Triangulate(poly);
        Assert.Equal(2, tris.Count);
        var totalArea = 0.0;
        foreach (var t in tris) totalArea += t.Area;
        Assert.Equal(64.0 * 64.0, totalArea, 5);
    }

    [Fact]
    public void LShape_Triangulates_Into_FourTriangles()
    {
        // L-shape: 6 vertices, expect 6-2 = 4 triangles.
        var poly = new[]
        {
            new Vec2(0, 0),
            new Vec2(128, 0),
            new Vec2(128, 64),
            new Vec2(64, 64),
            new Vec2(64, 128),
            new Vec2(0, 128),
        };
        var tris = PolygonTriangulator.Triangulate(poly);
        Assert.Equal(4, tris.Count);
        // L-shape area = 128*64 + 64*64 = 8192 + 4096 = 12288.
        var totalArea = 0.0;
        foreach (var t in tris) totalArea += t.Area;
        Assert.Equal(12288.0, totalArea, 5);
    }

    [Fact]
    public void Triangulate_Triangle_Returns_OneTriangle()
    {
        var poly = new[] { new Vec2(0, 0), new Vec2(10, 0), new Vec2(0, 10) };
        var tris = PolygonTriangulator.Triangulate(poly);
        Assert.Single(tris);
    }
}

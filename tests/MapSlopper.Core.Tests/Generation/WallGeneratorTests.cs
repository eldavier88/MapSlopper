using MapSlopper.Core.Generation;
using MapSlopper.Core.Geometry;
using Xunit;

namespace MapSlopper.Core.Tests.Generation;

public class WallGeneratorTests
{
    [Fact]
    public void Square_Produces_Four_Wall_Brushes()
    {
        var poly = new Polygon2D(new[]
        {
            new Vec2(0, 0), new Vec2(256, 0),
            new Vec2(256, 256), new Vec2(0, 256),
        });
        var brushes = WallGenerator.Generate(poly, thickness: 16, zBottom: 0, zTop: 256,
            sideTexture: "common/caulk", capTexture: "common/caulk");
        Assert.Equal(4, brushes.Count);
        // Every brush must have exactly 6 planes (top, bottom, 4 sides for a quad prism).
        foreach (var b in brushes) Assert.Equal(6, b.Planes.Count);
    }

    [Fact]
    public void LShape_Produces_Six_Wall_Brushes()
    {
        var poly = new Polygon2D(new[]
        {
            new Vec2(0, 0), new Vec2(256, 0),
            new Vec2(256, 128), new Vec2(128, 128),
            new Vec2(128, 256), new Vec2(0, 256),
        });
        var brushes = WallGenerator.Generate(poly, thickness: 16, zBottom: 0, zTop: 256,
            sideTexture: "x", capTexture: "x");
        Assert.Equal(6, brushes.Count);
    }

    [Fact]
    public void Throws_On_Cw_Polygon()
    {
        var cw = new Polygon2D(new[]
        {
            new Vec2(0, 0), new Vec2(0, 64),
            new Vec2(64, 64), new Vec2(64, 0),
        });
        Assert.Throws<System.ArgumentException>(() =>
            WallGenerator.Generate(cw, 16, 0, 256, "x", "x"));
    }
}

using MapSlopper.Core.Geometry;
using Xunit;

namespace MapSlopper.Core.Tests.Generation;

public class RectangleClipperTests
{
    [Fact]
    public void Triangle_Fully_Inside_Rectangle_Is_Unchanged()
    {
        var tri = new[] { new Vec2(2, 2), new Vec2(8, 2), new Vec2(5, 8) };
        var clipped = RectangleClipper.Clip(tri, 0, 0, 10, 10);
        Assert.Equal(3, clipped.Count);
    }

    [Fact]
    public void Triangle_Fully_Outside_Returns_Empty()
    {
        var tri = new[] { new Vec2(20, 20), new Vec2(30, 20), new Vec2(25, 30) };
        var clipped = RectangleClipper.Clip(tri, 0, 0, 10, 10);
        Assert.Empty(clipped);
    }

    [Fact]
    public void Triangle_Partially_Crossing_Edge_Yields_Convex_Polygon()
    {
        var tri = new[] { new Vec2(-5, 5), new Vec2(5, 5), new Vec2(0, 15) };
        var clipped = RectangleClipper.Clip(tri, 0, 0, 10, 10);
        clipped = RectangleClipper.RemoveDegenerate(clipped);
        Assert.True(clipped.Count >= 3);
        // All vertices must lie within (or on) the rectangle bounds.
        foreach (var p in clipped)
        {
            Assert.InRange(p.X, 0 - 1e-9, 10 + 1e-9);
            Assert.InRange(p.Y, 0 - 1e-9, 10 + 1e-9);
        }
    }
}

using MapSlopper.Core.Geometry;
using Xunit;

namespace MapSlopper.Core.Tests.Geometry;

public class Polygon2DTests
{
    [Fact]
    public void SignedArea_CcwSquare_IsPositive()
    {
        var p = new Polygon2D(new[]
        {
            new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10),
        });
        Assert.Equal(100, p.SignedArea(), 6);
        Assert.True(p.IsCcw());
    }

    [Fact]
    public void SignedArea_CwSquare_IsNegative()
    {
        var p = new Polygon2D(new[]
        {
            new Vec2(0, 0), new Vec2(0, 10), new Vec2(10, 10), new Vec2(10, 0),
        });
        Assert.Equal(-100, p.SignedArea(), 6);
        Assert.False(p.IsCcw());
        Assert.True(p.ToCcw().IsCcw());
    }

    [Fact]
    public void ContainsPoint_Square()
    {
        var p = new Polygon2D(new[]
        {
            new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10),
        });
        Assert.True(p.ContainsPoint(new Vec2(5, 5)));
        Assert.False(p.ContainsPoint(new Vec2(15, 5)));
        Assert.False(p.ContainsPoint(new Vec2(-1, 5)));
        Assert.False(p.ContainsPoint(new Vec2(5, 15)));
    }

    [Fact]
    public void ContainsPoint_Concave_LShape()
    {
        // L-shape:
        //  +---+
        //  |   |
        //  |   +---+
        //  |       |
        //  +-------+
        var p = new Polygon2D(new[]
        {
            new Vec2(0, 0), new Vec2(20, 0), new Vec2(20, 10),
            new Vec2(10, 10), new Vec2(10, 20), new Vec2(0, 20),
        });
        Assert.True(p.IsCcw());
        Assert.True(p.ContainsPoint(new Vec2(5, 5)));    // inside lower-left
        Assert.True(p.ContainsPoint(new Vec2(15, 5)));   // inside lower-right
        Assert.True(p.ContainsPoint(new Vec2(5, 15)));   // inside upper part of L
        Assert.False(p.ContainsPoint(new Vec2(15, 15))); // notch (outside)
    }

    [Fact]
    public void IsSimple_Square_True()
    {
        var p = new Polygon2D(new[]
        {
            new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10),
        });
        Assert.True(p.IsSimple());
    }

    [Fact]
    public void IsSimple_Bowtie_False()
    {
        var p = new Polygon2D(new[]
        {
            new Vec2(0, 0), new Vec2(10, 10), new Vec2(10, 0), new Vec2(0, 10),
        });
        Assert.False(p.IsSimple());
    }

    [Fact]
    public void Centroid_Square_IsCenter()
    {
        var p = new Polygon2D(new[]
        {
            new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10),
        });
        var c = p.Centroid();
        Assert.Equal(5, c.X, 6);
        Assert.Equal(5, c.Y, 6);
    }
}

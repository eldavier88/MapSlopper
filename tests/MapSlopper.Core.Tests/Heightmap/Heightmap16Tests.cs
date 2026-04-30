using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;

namespace MapSlopper.Core.Tests.Heightmap;

public class Heightmap16Tests
{
    [Fact]
    public void FillSampleRoundTrip()
    {
        var hm = new Heightmap16(8, 8, 32, Vec2.Zero);
        hm.Fill(2, 2, 5, 5, 256);
        Assert.Equal(256, hm.Sample(3, 3));
        Assert.Equal(256, hm.Sample(2, 2));
        Assert.Equal(256, hm.Sample(5, 5));
        Assert.Equal(0, hm.Sample(1, 1));
        Assert.Equal(0, hm.Sample(6, 6));
    }

    [Fact]
    public void OutOfBoundsSample_ReturnsZero()
    {
        var hm = new Heightmap16(4, 4, 32, Vec2.Zero);
        hm.Fill(0, 0, 3, 3, 1000);
        Assert.Equal(0, hm.Sample(-1, 0));
        Assert.Equal(0, hm.Sample(0, -1));
        Assert.Equal(0, hm.Sample(4, 0));
        Assert.Equal(0, hm.Sample(0, 4));
    }

    [Fact]
    public void Fill_ClipsToBounds()
    {
        var hm = new Heightmap16(4, 4, 32, Vec2.Zero);
        hm.Fill(-5, -5, 100, 100, 7);
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
                Assert.Equal(7, hm.Sample(x, y));
    }

    [Fact]
    public void CellWorldPositions_AreCorrect()
    {
        var hm = new Heightmap16(4, 4, 64, new Vec2(100, 200));
        var min = hm.CellWorldMin(2, 1);
        var max = hm.CellWorldMax(2, 1);
        Assert.Equal(new Vec2(100 + 64 * 2, 200 + 64 * 1), min);
        Assert.Equal(new Vec2(100 + 64 * 3, 200 + 64 * 2), max);
    }

    [Fact]
    public void WorldToCell_RoundsDown()
    {
        var hm = new Heightmap16(4, 4, 32, Vec2.Zero);
        Assert.Equal((0, 0), hm.WorldToCell(new Vec2(1, 1)));
        Assert.Equal((1, 0), hm.WorldToCell(new Vec2(32, 0)));
        Assert.Equal((0, 0), hm.WorldToCell(new Vec2(31.99, 31.99)));
    }
}

public class HeightmapLevelsTests
{
    [Fact]
    public void NormalizeForDisplay_ClampsToRange()
    {
        var l = new HeightmapLevels { DisplayMin = 100, DisplayMax = 200 };
        Assert.Equal(0.0, l.NormalizeForDisplay(50));
        Assert.Equal(0.0, l.NormalizeForDisplay(100));
        Assert.Equal(0.5, l.NormalizeForDisplay(150));
        Assert.Equal(1.0, l.NormalizeForDisplay(200));
        Assert.Equal(1.0, l.NormalizeForDisplay(300));
    }
}

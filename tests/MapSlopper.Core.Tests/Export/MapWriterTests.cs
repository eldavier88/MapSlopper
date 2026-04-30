using System.IO;
using MapSlopper.Core.Brushes;
using MapSlopper.Core.Export;
using MapSlopper.Core.Geometry;

namespace MapSlopper.Core.Tests.Export;

public class MapWriterTests
{
    [Fact]
    public void SingleBox_MatchesFixtureExactly()
    {
        var doc = new MapDocument();
        var ws = doc.Worldspawn;
        ws.Brushes.Add(BrushFactory.MakeAabb(Vec3.Zero, new Vec3(64, 64, 64), "common/caulk"));

        var actual = MapWriter.Write(doc);
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "single_box.map");
        var expected = File.ReadAllText(fixturePath).Replace("\r\n", "\n");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Aabb_AllPlanesNonDegenerate_AndNormalsPointOutward()
    {
        var brush = BrushFactory.MakeAabb(Vec3.Zero, new Vec3(10, 10, 10), "tex");
        Assert.Equal(6, brush.Planes.Count);
        // Outward normals along ±X, ±Y, ±Z (any order).
        var found = new System.Collections.Generic.HashSet<(int, int, int)>();
        foreach (var p in brush.Planes)
        {
            Assert.False(p.IsDegenerate());
            var n = p.Normal.Normalized;
            var sx = (int)System.Math.Round(n.X);
            var sy = (int)System.Math.Round(n.Y);
            var sz = (int)System.Math.Round(n.Z);
            found.Add((sx, sy, sz));
        }
        Assert.Contains(( 1, 0, 0), found);
        Assert.Contains((-1, 0, 0), found);
        Assert.Contains(( 0, 1, 0), found);
        Assert.Contains(( 0,-1, 0), found);
        Assert.Contains(( 0, 0, 1), found);
        Assert.Contains(( 0, 0,-1), found);
    }

    [Fact]
    public void DegenerateAabb_Throws()
    {
        Assert.Throws<System.ArgumentException>(() =>
            BrushFactory.MakeAabb(Vec3.Zero, new Vec3(0, 10, 10), "tex"));
    }

    [Fact]
    public void VerticalPrism_TriangleFootprint_HasFiveValidPlanes()
    {
        var prism = BrushFactory.MakeVerticalPrism(
            new[] { new Vec2(0, 0), new Vec2(10, 0), new Vec2(0, 10) },
            zBottom: 0, zTop: 64,
            sideTexture: "wall", topTexture: "ceil", bottomTexture: "floor");
        Assert.Equal(5, prism.Planes.Count); // top + bottom + 3 sides
        foreach (var p in prism.Planes) Assert.False(p.IsDegenerate());
        // Top plane normal is +Z, bottom is -Z.
        Assert.True(prism.Planes[0].Normal.Normalized.Z > 0.999);
        Assert.True(prism.Planes[1].Normal.Normalized.Z < -0.999);
    }

    [Fact]
    public void Writer_UsesInvariantCulture_ForNonIntegralValues()
    {
        var doc = new MapDocument();
        var ws = doc.Worldspawn;
        var b = new Brush();
        b.Planes.Add(new Plane(
            new Vec3(0.5, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0),
            "tex"));
        ws.Brushes.Add(b);
        var s = MapWriter.Write(doc);
        Assert.Contains("0.500000", s);
        Assert.DoesNotContain(",", s.Replace(",", "")); // no comma decimals
    }
}

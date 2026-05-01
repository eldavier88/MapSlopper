using MapSlopper.Core.Generation;
using MapSlopper.Core.Project;
using Xunit;

namespace MapSlopper.Core.Tests.Generation;

public class AutoMapGeneratorTests
{
    [Fact]
    public void Generate_Default_Produces_Valid_Exportable_Project()
    {
        var template = new MapSlopperProject();
        var generated = AutoMapGenerator.Generate(template, new AutoMapGenerator.Options
        {
            WidthCells = 96,
            HeightCells = 96,
            Complexity = 3,
            Relief = 192,
            Seed = 123456,
        });

        Assert.NotNull(generated.Project);
        Assert.True(generated.Project.Outline.TryGetClosedPolygon(out var poly));
        Assert.True(poly.IsSimple());

        var validation = GeometryGenerator.Generate(generated.Project);
        Assert.True(validation.Ok);
    }

    [Fact]
    public void Generate_Is_Deterministic_For_Same_Seed()
    {
        var template = new MapSlopperProject();
        var a = AutoMapGenerator.Generate(template, new AutoMapGenerator.Options { Seed = 42, Complexity = 4 });
        var b = AutoMapGenerator.Generate(template, new AutoMapGenerator.Options { Seed = 42, Complexity = 4 });

        Assert.True(a.Project.Outline.TryGetClosedPolygon(out var pa));
        Assert.True(b.Project.Outline.TryGetClosedPolygon(out var pb));
        Assert.Equal(pa.Count, pb.Count);
        for (var i = 0; i < pa.Count; i++)
        {
            Assert.Equal(pa[i].X, pb[i].X, 6);
            Assert.Equal(pa[i].Y, pb[i].Y, 6);
        }

        Assert.Equal(a.Project.Heightmap.Width, b.Project.Heightmap.Width);
        Assert.Equal(a.Project.Heightmap.Height, b.Project.Heightmap.Height);
        Assert.Equal(a.Project.Heightmap.Data.Length, b.Project.Heightmap.Data.Length);
        for (var i = 0; i < a.Project.Heightmap.Data.Length; i++)
            Assert.Equal(a.Project.Heightmap.Data[i], b.Project.Heightmap.Data[i]);
    }

    [Fact]
    public void Generate_Multiple_Seeds_Stay_Valid()
    {
        var template = new MapSlopperProject();
        for (var seed = 1; seed <= 3; seed++)
        {
            var result = AutoMapGenerator.Generate(template, new AutoMapGenerator.Options
            {
                Seed = seed,
                WidthCells = 48,
                HeightCells = 48,
                Complexity = 2,
                Relief = 128,
            });
            var validation = GeometryGenerator.Generate(result.Project);
            Assert.True(validation.Ok, $"Seed {seed} should produce a valid project.");
        }
    }
}

using MapSlopper.Core.Geometry;
using MapSlopper.Core.Outline;

namespace MapSlopper.Core.Tests.Outline;

public class OutlineGraphTests
{
    [Fact]
    public void EmptyGraph_DoesNotClose()
    {
        var g = new OutlineGraph();
        Assert.False(g.TryGetClosedPolygon(out _));
    }

    [Fact]
    public void Triangle_ClosesAsCcw()
    {
        var g = new OutlineGraph();
        var a = g.AddPoint(new Vec2(0, 0));
        var b = g.AddPoint(new Vec2(10, 0));
        var c = g.AddPoint(new Vec2(0, 10));
        g.AddEdge(a.Id, b.Id);
        g.AddEdge(b.Id, c.Id);
        g.AddEdge(c.Id, a.Id);

        Assert.True(g.TryGetClosedPolygon(out var poly));
        Assert.Equal(3, poly.Count);
        Assert.True(poly.IsCcw());
        Assert.True(poly.IsSimple());
    }

    [Fact]
    public void TriangleMissingEdge_DoesNotClose()
    {
        var g = new OutlineGraph();
        var a = g.AddPoint(new Vec2(0, 0));
        var b = g.AddPoint(new Vec2(10, 0));
        var c = g.AddPoint(new Vec2(0, 10));
        g.AddEdge(a.Id, b.Id);
        g.AddEdge(b.Id, c.Id);
        Assert.False(g.TryGetClosedPolygon(out _));
    }

    [Fact]
    public void FigureEightSharedVertex_DoesNotClose()
    {
        // Two triangles sharing a single vertex (degree-4 there).
        var g = new OutlineGraph();
        var s = g.AddPoint(new Vec2(0, 0));
        var a1 = g.AddPoint(new Vec2(-10, -10));
        var a2 = g.AddPoint(new Vec2(-10, 10));
        var b1 = g.AddPoint(new Vec2(10, -10));
        var b2 = g.AddPoint(new Vec2(10, 10));
        g.AddEdge(s.Id, a1.Id); g.AddEdge(a1.Id, a2.Id); g.AddEdge(a2.Id, s.Id);
        g.AddEdge(s.Id, b1.Id); g.AddEdge(b1.Id, b2.Id); g.AddEdge(b2.Id, s.Id);
        Assert.False(g.TryGetClosedPolygon(out _));
    }

    [Fact]
    public void Square_ClosesAndIsCcw_RegardlessOfInputOrder()
    {
        var g = new OutlineGraph();
        // Add points clockwise; expect TryGetClosedPolygon to flip to CCW.
        var p1 = g.AddPoint(new Vec2(0, 0));
        var p2 = g.AddPoint(new Vec2(0, 10));
        var p3 = g.AddPoint(new Vec2(10, 10));
        var p4 = g.AddPoint(new Vec2(10, 0));
        g.AddEdge(p1.Id, p2.Id);
        g.AddEdge(p2.Id, p3.Id);
        g.AddEdge(p3.Id, p4.Id);
        g.AddEdge(p4.Id, p1.Id);

        Assert.True(g.TryGetClosedPolygon(out var poly));
        Assert.True(poly.IsCcw());
        Assert.Equal(100, poly.Area, 6);
    }

    [Fact]
    public void RemovePoint_RemovesIncidentEdges()
    {
        var g = new OutlineGraph();
        var a = g.AddPoint(new Vec2(0, 0));
        var b = g.AddPoint(new Vec2(10, 0));
        var c = g.AddPoint(new Vec2(0, 10));
        g.AddEdge(a.Id, b.Id);
        g.AddEdge(b.Id, c.Id);
        g.RemovePoint(b.Id);
        Assert.Empty(g.Edges);
        Assert.Equal(2, g.Points.Count);
    }

    [Fact]
    public void InsertPointOnEdge_SplitsEdge()
    {
        var g = new OutlineGraph();
        var a = g.AddPoint(new Vec2(0, 0));
        var b = g.AddPoint(new Vec2(10, 0));
        g.AddEdge(a.Id, b.Id);
        var mid = g.InsertPointOnEdge(a.Id, b.Id, new Vec2(5, 0));
        Assert.Equal(3, g.Points.Count);
        Assert.Equal(2, g.Edges.Count);
        Assert.True(g.Degree(mid.Id) == 2);
    }

    [Fact]
    public void PickPoint_FindsClosestWithinRadius()
    {
        var g = new OutlineGraph();
        var a = g.AddPoint(new Vec2(0, 0));
        g.AddPoint(new Vec2(100, 100));
        var picked = g.PickPoint(new Vec2(2, 2), 5);
        Assert.NotNull(picked);
        Assert.Equal(a.Id, picked!.Id);
    }
}

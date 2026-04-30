using System;
using System.Collections.Generic;
using System.Linq;

namespace MapSlopper.Core.Geometry;

/// <summary>
/// Closed simple polygon in 2D. Vertices are stored in their listed order;
/// canonical forms returned by the generator are oriented CCW so that
/// <see cref="SignedArea"/> is positive and the right-perpendicular of each
/// edge direction points outward.
/// </summary>
public sealed class Polygon2D
{
    public List<Vec2> Vertices { get; }

    public Polygon2D() => Vertices = new List<Vec2>();
    public Polygon2D(IEnumerable<Vec2> verts) => Vertices = verts.ToList();

    public int Count => Vertices.Count;
    public Vec2 this[int i] => Vertices[i];

    /// <summary>Shoelace signed area. Positive = CCW, negative = CW.</summary>
    public double SignedArea()
    {
        var n = Vertices.Count;
        if (n < 3) return 0;
        double s = 0;
        for (var i = 0; i < n; i++)
        {
            var a = Vertices[i];
            var b = Vertices[(i + 1) % n];
            s += (a.X * b.Y) - (b.X * a.Y);
        }
        return s * 0.5;
    }

    public double Area => Math.Abs(SignedArea());
    public bool IsCcw() => SignedArea() > 0;

    /// <summary>Returns a CCW-oriented copy of this polygon.</summary>
    public Polygon2D ToCcw()
    {
        if (IsCcw()) return new Polygon2D(Vertices);
        var rev = new List<Vec2>(Vertices);
        rev.Reverse();
        return new Polygon2D(rev);
    }

    /// <summary>
    /// Ray-casting point-in-polygon. Uses half-open edges (inclusive on lower y,
    /// exclusive on upper y) to avoid double-counting at shared vertices.
    /// </summary>
    public bool ContainsPoint(Vec2 p)
    {
        var n = Vertices.Count;
        if (n < 3) return false;
        var inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var a = Vertices[i];
            var b = Vertices[j];
            // Half-open: edge spans [min(y), max(y)) — inclusive low, exclusive high.
            var yMin = Math.Min(a.Y, b.Y);
            var yMax = Math.Max(a.Y, b.Y);
            if (p.Y < yMin || p.Y >= yMax) continue;
            // x of intersection of edge with horizontal line y = p.Y
            var t = (p.Y - a.Y) / (b.Y - a.Y);
            var xCross = a.X + t * (b.X - a.X);
            if (p.X < xCross) inside = !inside;
        }
        return inside;
    }

    /// <summary>O(n²) check that no two non-adjacent edges intersect.</summary>
    public bool IsSimple()
    {
        var n = Vertices.Count;
        if (n < 3) return false;
        for (var i = 0; i < n; i++)
        {
            var a1 = Vertices[i];
            var a2 = Vertices[(i + 1) % n];
            for (var j = i + 1; j < n; j++)
            {
                // Skip same edge and adjacent edges (shared endpoint).
                if (j == i) continue;
                if ((j + 1) % n == i || j == (i + 1) % n) continue;
                var b1 = Vertices[j];
                var b2 = Vertices[(j + 1) % n];
                if (SegmentsIntersect(a1, a2, b1, b2)) return false;
            }
        }
        return true;
    }

    /// <summary>Axis-aligned bounding box.</summary>
    public (Vec2 Min, Vec2 Max) Bounds()
    {
        if (Vertices.Count == 0) return (Vec2.Zero, Vec2.Zero);
        var minX = Vertices[0].X; var minY = Vertices[0].Y;
        var maxX = minX; var maxY = minY;
        foreach (var v in Vertices)
        {
            if (v.X < minX) minX = v.X;
            if (v.Y < minY) minY = v.Y;
            if (v.X > maxX) maxX = v.X;
            if (v.Y > maxY) maxY = v.Y;
        }
        return (new Vec2(minX, minY), new Vec2(maxX, maxY));
    }

    public Vec2 Centroid()
    {
        var n = Vertices.Count;
        if (n == 0) return Vec2.Zero;
        var s = SignedArea();
        if (Math.Abs(s) < 1e-12)
        {
            // Degenerate: return mean.
            var sum = Vec2.Zero;
            foreach (var v in Vertices) sum += v;
            return sum / n;
        }
        double cx = 0, cy = 0;
        for (var i = 0; i < n; i++)
        {
            var a = Vertices[i];
            var b = Vertices[(i + 1) % n];
            var cross = a.X * b.Y - b.X * a.Y;
            cx += (a.X + b.X) * cross;
            cy += (a.Y + b.Y) * cross;
        }
        var k = 1.0 / (6.0 * s);
        return new Vec2(cx * k, cy * k);
    }

    /// <summary>Open-segment intersection test (excludes touching at endpoints).</summary>
    private static bool SegmentsIntersect(Vec2 p1, Vec2 p2, Vec2 p3, Vec2 p4)
    {
        var d1 = Direction(p3, p4, p1);
        var d2 = Direction(p3, p4, p2);
        var d3 = Direction(p1, p2, p3);
        var d4 = Direction(p1, p2, p4);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
            ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;

        // Collinear-overlap cases — count as intersection only if they overlap on a segment of nonzero length.
        if (d1 == 0 && OnSegment(p3, p4, p1) && !ApproxEq(p1, p3) && !ApproxEq(p1, p4)) return true;
        if (d2 == 0 && OnSegment(p3, p4, p2) && !ApproxEq(p2, p3) && !ApproxEq(p2, p4)) return true;
        if (d3 == 0 && OnSegment(p1, p2, p3) && !ApproxEq(p3, p1) && !ApproxEq(p3, p2)) return true;
        if (d4 == 0 && OnSegment(p1, p2, p4) && !ApproxEq(p4, p1) && !ApproxEq(p4, p2)) return true;
        return false;
    }

    private static double Direction(Vec2 a, Vec2 b, Vec2 c) => Vec2.Cross(b - a, c - a);
    private static bool OnSegment(Vec2 a, Vec2 b, Vec2 c) =>
        Math.Min(a.X, b.X) <= c.X && c.X <= Math.Max(a.X, b.X) &&
        Math.Min(a.Y, b.Y) <= c.Y && c.Y <= Math.Max(a.Y, b.Y);
    private static bool ApproxEq(Vec2 a, Vec2 b) =>
        Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9;
}

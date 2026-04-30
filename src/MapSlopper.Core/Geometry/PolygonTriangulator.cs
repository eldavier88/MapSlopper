using System;
using System.Collections.Generic;

namespace MapSlopper.Core.Geometry;

/// <summary>
/// Ear-clipping triangulation of a simple (possibly concave) polygon. The
/// input must be a CCW-oriented simple polygon. Output triangles are
/// CCW-oriented and reference the original vertex coordinates. Produces
/// exactly N-2 triangles for an N-vertex polygon.
/// </summary>
public static class PolygonTriangulator
{
    public readonly record struct Triangle(Vec2 A, Vec2 B, Vec2 C)
    {
        public double Area => Math.Abs(Vec2.Cross(B - A, C - A)) * 0.5;
    }

    public static List<Triangle> Triangulate(IReadOnlyList<Vec2> ccwPolygon)
    {
        if (ccwPolygon.Count < 3) return new List<Triangle>();
        // Working list of vertex indices into the original polygon.
        var indices = new List<int>(ccwPolygon.Count);
        for (var i = 0; i < ccwPolygon.Count; i++) indices.Add(i);

        var output = new List<Triangle>(ccwPolygon.Count - 2);
        var guard = ccwPolygon.Count * ccwPolygon.Count + 1; // termination safeguard
        while (indices.Count > 3 && guard-- > 0)
        {
            var earFound = false;
            for (var i = 0; i < indices.Count; i++)
            {
                var prev = indices[(i - 1 + indices.Count) % indices.Count];
                var curr = indices[i];
                var next = indices[(i + 1) % indices.Count];
                var a = ccwPolygon[prev];
                var b = ccwPolygon[curr];
                var c = ccwPolygon[next];
                if (!IsConvex(a, b, c)) continue;
                if (AnyVertexInside(ccwPolygon, indices, prev, curr, next, a, b, c)) continue;
                output.Add(new Triangle(a, b, c));
                indices.RemoveAt(i);
                earFound = true;
                break;
            }
            if (!earFound)
            {
                // Defensive fallback: emit a fan from index 0 to avoid infinite loops on
                // numerically degenerate input. This should not happen for well-formed
                // simple CCW polygons.
                for (var k = 1; k < indices.Count - 1; k++)
                    output.Add(new Triangle(
                        ccwPolygon[indices[0]],
                        ccwPolygon[indices[k]],
                        ccwPolygon[indices[k + 1]]));
                return output;
            }
        }
        if (indices.Count == 3)
        {
            output.Add(new Triangle(
                ccwPolygon[indices[0]],
                ccwPolygon[indices[1]],
                ccwPolygon[indices[2]]));
        }
        return output;
    }

    private static bool IsConvex(Vec2 a, Vec2 b, Vec2 c) =>
        Vec2.Cross(b - a, c - b) > 0;

    private static bool AnyVertexInside(
        IReadOnlyList<Vec2> poly, List<int> indices,
        int prev, int curr, int next,
        Vec2 a, Vec2 b, Vec2 c)
    {
        for (var i = 0; i < indices.Count; i++)
        {
            var idx = indices[i];
            if (idx == prev || idx == curr || idx == next) continue;
            if (PointInTriangle(poly[idx], a, b, c)) return true;
        }
        return false;
    }

    private static bool PointInTriangle(Vec2 p, Vec2 a, Vec2 b, Vec2 c)
    {
        var d1 = Vec2.Cross(b - a, p - a);
        var d2 = Vec2.Cross(c - b, p - b);
        var d3 = Vec2.Cross(a - c, p - c);
        var hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }
}

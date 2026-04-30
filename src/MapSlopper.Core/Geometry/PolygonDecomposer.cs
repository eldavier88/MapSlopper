using System;
using System.Collections.Generic;

namespace MapSlopper.Core.Geometry;

/// <summary>
/// Decomposes a (possibly non-convex but simple) CCW polygon into a small
/// number of convex polygons:
///   1. Ear-clip triangulation (handles arbitrary simple polygons).
///   2. Hertel-Mehlhorn merge: greedily removes interior diagonals whose
///      removal does not introduce a reflex vertex at either endpoint. This
///      collapses adjacent triangles into the largest convex pieces possible.
///
/// Result is the minimal-or-near-minimal set of convex polygons that
/// partitions the input. Used by FloorCeilingGenerator to emit one brush
/// per convex piece (Q3 brushes must be convex), and to clip floor
/// rectangles per piece without Sutherland-Hodgman breaking on concavities.
/// </summary>
public static class PolygonDecomposer
{
    /// <summary>
    /// Triangulate a simple CCW polygon by ear clipping. Returns a list of
    /// triangles (each three vertices, CCW). Robust enough for hand-drawn
    /// polygons; not optimised for huge ones.
    /// </summary>
    public static List<List<Vec2>> Triangulate(IReadOnlyList<Vec2> ccwVerts)
    {
        var n = ccwVerts.Count;
        var triangles = new List<List<Vec2>>();
        if (n < 3) return triangles;
        if (n == 3)
        {
            triangles.Add(new List<Vec2> { ccwVerts[0], ccwVerts[1], ccwVerts[2] });
            return triangles;
        }
        // Doubly-linked working list of indices into ccwVerts.
        var prev = new int[n];
        var next = new int[n];
        for (var i = 0; i < n; i++) { prev[i] = (i - 1 + n) % n; next[i] = (i + 1) % n; }
        var remaining = n;
        var guard = 0;
        var current = 0;
        while (remaining > 3 && guard++ < n * n)
        {
            var i0 = prev[current];
            var i1 = current;
            var i2 = next[current];
            if (IsEar(ccwVerts, i0, i1, i2, next))
            {
                triangles.Add(new List<Vec2> { ccwVerts[i0], ccwVerts[i1], ccwVerts[i2] });
                next[i0] = i2;
                prev[i2] = i0;
                remaining--;
                current = i0;
            }
            else
            {
                current = next[current];
            }
        }
        if (remaining == 3)
        {
            var a = current;
            var b = next[a];
            var c = next[b];
            triangles.Add(new List<Vec2> { ccwVerts[a], ccwVerts[b], ccwVerts[c] });
        }
        return triangles;
    }

    /// <summary>
    /// Triangulate, then run Hertel-Mehlhorn merge. Returns convex pieces
    /// (each CCW). Always at least one piece; fewer pieces than triangles.
    /// </summary>
    public static List<List<Vec2>> ConvexDecompose(IReadOnlyList<Vec2> ccwVerts)
    {
        var tris = Triangulate(ccwVerts);
        if (tris.Count <= 1) return tris;

        // Convert triangles into polygons stored as List<Vec2>.
        var polys = new List<List<Vec2>>(tris.Count);
        foreach (var t in tris) polys.Add(new List<Vec2>(t));

        // Iteratively try to merge any pair of polygons sharing an edge if
        // the merged polygon is still convex. Single-pass with restart-on-merge.
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var a = 0; a < polys.Count && !changed; a++)
            {
                for (var b = a + 1; b < polys.Count && !changed; b++)
                {
                    if (TryMergeConvex(polys[a], polys[b], out var merged))
                    {
                        polys[a] = merged;
                        polys.RemoveAt(b);
                        changed = true;
                    }
                }
            }
        }
        return polys;
    }

    private static bool IsEar(
        IReadOnlyList<Vec2> verts, int i0, int i1, int i2, int[] next)
    {
        var a = verts[i0];
        var b = verts[i1];
        var c = verts[i2];
        // Convex (left turn) test.
        if (Cross(b - a, c - b) <= 0) return false;
        // No other (still-active) vertex inside triangle abc.
        for (var k = next[i2]; k != i0; k = next[k])
        {
            if (k == i0 || k == i1 || k == i2) continue;
            if (PointInTriangle(verts[k], a, b, c)) return false;
        }
        return true;
    }

    private static bool TryMergeConvex(List<Vec2> A, List<Vec2> B, out List<Vec2> merged)
    {
        merged = null!;
        // Find a shared edge (B has edge bj→bj+1 that equals ai+1→ai in A
        // because both polygons are CCW so the shared edge runs opposite).
        var na = A.Count;
        var nb = B.Count;
        for (var i = 0; i < na; i++)
        {
            var a0 = A[i];
            var a1 = A[(i + 1) % na];
            for (var j = 0; j < nb; j++)
            {
                var b0 = B[j];
                var b1 = B[(j + 1) % nb];
                if (!ApproxEq(a0, b1) || !ApproxEq(a1, b0)) continue;
                // Build merged polygon: walk A from a1 around back to a0,
                // then walk B from b1 around back to b0 (skipping shared edge).
                var poly = new List<Vec2>(na + nb - 2);
                // From A: i+1, i+2, ..., back to i (exclusive of i+1, but include i)
                // We want the polygon that EXCLUDES the shared edge a0->a1.
                // Walking A: start at index (i+1)%na = a1. Then go around to i = a0.
                // We INCLUDE a1 and a0 (since the shared edge is removed but its endpoints remain).
                var k = (i + 1) % na;
                while (true)
                {
                    poly.Add(A[k]);
                    if (k == i) break;
                    k = (k + 1) % na;
                }
                // Then from B: starting at (j+1)%nb = b1 (== a0 already added → skip), go to j = b0 (== a1 → skip).
                k = (j + 2) % nb;
                while (k != j)
                {
                    poly.Add(B[k]);
                    k = (k + 1) % nb;
                }
                // Check convexity of merged poly.
                if (IsConvexCcw(poly))
                {
                    merged = poly;
                    return true;
                }
                return false; // shared edge found but merge non-convex; skip
            }
        }
        return false;
    }

    private static bool IsConvexCcw(IReadOnlyList<Vec2> poly)
    {
        var n = poly.Count;
        if (n < 3) return false;
        for (var i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            var c = poly[(i + 2) % n];
            // Allow exactly-collinear points (cross == 0) but not concave.
            if (Cross(b - a, c - b) < -1e-9) return false;
        }
        return true;
    }

    private static bool PointInTriangle(Vec2 p, Vec2 a, Vec2 b, Vec2 c)
    {
        var d1 = Cross(b - a, p - a);
        var d2 = Cross(c - b, p - b);
        var d3 = Cross(a - c, p - c);
        var hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    private static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
    private static bool ApproxEq(Vec2 a, Vec2 b) =>
        Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;
}

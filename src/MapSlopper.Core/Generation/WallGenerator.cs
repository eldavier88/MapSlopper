using System;
using System.Collections.Generic;
using MapSlopper.Core.Brushes;
using MapSlopper.Core.Geometry;

namespace MapSlopper.Core.Generation;

/// <summary>
/// Generates the exterior wall ring as a sequence of mitered convex prisms,
/// one per polygon edge. Adjacent brushes share end-cap planes, so the union
/// has zero overlap and zero gaps. For acute corners (where the miter would
/// extend more than <see cref="MaxMiterRatio"/>×<c>thickness</c>), the miter
/// is capped — the brushes still meet without overlap because the cap plane
/// is shared.
/// </summary>
public static class WallGenerator
{
    public const double MaxMiterRatio = 4.0;

    /// <summary>
    /// Default overload: one shared <paramref name="zBottom"/> for every wall.
    /// </summary>
    public static IReadOnlyList<Brush> Generate(
        Polygon2D ccwPolygon, double thickness, double zBottom, double zTop,
        string sideTexture, string capTexture)
        => GenerateInternal(ccwPolygon, thickness, _ => zBottom, zTop, sideTexture, capTexture, null, null);

    /// <summary>
    /// Per-edge overload: <paramref name="zBottomForEdge"/> is invoked once
    /// per polygon edge index (0..N-1) and returns the z value to use as
    /// that wall brush's bottom. Lets the caller drop wall bottoms only as
    /// far as the adjacent floor requires (slim airtight overlap), avoiding
    /// the huge wasted brush volume from a single common floor-base bottom.
    /// </summary>
    public static IReadOnlyList<Brush> Generate(
        Polygon2D ccwPolygon, double thickness, Func<int, double> zBottomForEdge, double zTop,
        string sideTexture, string capTexture)
        => GenerateInternal(ccwPolygon, thickness, zBottomForEdge, zTop, sideTexture, capTexture, null, null);

    /// <summary>
    /// Per-edge overload with optional horizontal split. When
    /// <paramref name="splitZForEdge"/> is supplied AND the returned
    /// absolute Z lies strictly between the wall's bottom and top, the
    /// wall is emitted as two stacked airtight prisms sharing that Z
    /// plane. The lower brush is textured with
    /// <paramref name="sideTexture"/>; the upper brush with
    /// <paramref name="upperSideTexture"/> (the "window" texture). Caps
    /// (top/bottom) and the shared seam plane use
    /// <paramref name="capTexture"/>. Walls whose split Z falls outside
    /// (bottom, top) stay as a single brush.
    /// </summary>
    public static IReadOnlyList<Brush> Generate(
        Polygon2D ccwPolygon, double thickness, Func<int, double> zBottomForEdge, double zTop,
        string sideTexture, string capTexture,
        Func<int, double>? splitZForEdge, string? upperSideTexture)
        => GenerateInternal(ccwPolygon, thickness, zBottomForEdge, zTop, sideTexture, capTexture, splitZForEdge, upperSideTexture);

    private static IReadOnlyList<Brush> GenerateInternal(
        Polygon2D ccwPolygon, double thickness, Func<int, double> zBottomForEdge, double zTop,
        string sideTexture, string capTexture,
        Func<int, double>? splitZForEdge, string? upperSideTexture)
    {
        if (!ccwPolygon.IsCcw())
            throw new ArgumentException("Polygon must be CCW.", nameof(ccwPolygon));
        if (thickness <= 0)
            throw new ArgumentException("thickness must be positive.", nameof(thickness));
        if (zBottomForEdge is null)
            throw new ArgumentNullException(nameof(zBottomForEdge));
        if (zTop <= 0 && zTop <= 0) { /* zTop validated below per-edge */ }

        var n = ccwPolygon.Count;
        var verts = new Vec2[n];
        for (var i = 0; i < n; i++) verts[i] = ccwPolygon[i];

        // Outward edge normals (CCW poly: right-perp of edge direction is OUTWARD).
        var edgeNormals = new Vec2[n];
        for (var i = 0; i < n; i++)
        {
            var dir = (verts[(i + 1) % n] - verts[i]).Normalized;
            edgeNormals[i] = dir.PerpRight; // already normalized
        }

        // Miter offsets at each vertex i: bisects edges (i-1,i) and (i,i+1).
        // m = (n_prev + n_next) * (thickness / (1 + n_prev·n_next))   (standard miter formula)
        var miters = new Vec2[n];
        for (var i = 0; i < n; i++)
        {
            var nPrev = edgeNormals[(i - 1 + n) % n];
            var nNext = edgeNormals[i];
            var bis = nPrev + nNext;
            var lenSq = bis.LengthSquared;
            if (lenSq < 1e-12)
            {
                // 180° turn: degenerate; just use one normal × thickness.
                miters[i] = nNext * thickness;
                continue;
            }
            // Project onto bisector: distance from vertex to outer wall =
            // thickness / cos(half-angle). Using identity 1+cos(angle) = 2 cos²(half):
            // |bis| = 2 cos(half-angle), and miter scalar = thickness / cos(half) = 2t / |bis|.
            var bisLen = Math.Sqrt(lenSq);
            var miterScale = 2.0 * thickness / bisLen;
            var maxAllowed = MaxMiterRatio * thickness;
            if (miterScale > maxAllowed) miterScale = maxAllowed;
            miters[i] = (bis / bisLen) * miterScale;
        }

        var brushes = new List<Brush>(n);
        for (var i = 0; i < n; i++)
        {
            var a = verts[i];
            var b = verts[(i + 1) % n];
            var aOut = a + miters[i];
            var bOut = b + miters[(i + 1) % n];

            // Inner face is the polygon edge a->b. Outward direction is +edgeNormal[i].
            // For a CCW polygon edge a->b, the wall sits on the OUTSIDE side (right-perp).
            // Walking CCW around the OUTSIDE quad: b -> a -> aOut -> bOut.
            var footprint = new List<Vec2> { b, a, aOut, bOut };
            // Defensive: if degenerate / inverted, skip this brush rather than emit garbage.
            if (PolygonSignedArea(footprint) <= 0) continue;

            var zBottom = zBottomForEdge(i);
            if (zTop <= zBottom) continue; // skip degenerate

            // Compute split Z (absolute). Skip splitting when:
            //  - no callback supplied
            //  - upper texture not provided
            //  - split z would clamp the upper or lower brush below a
            //    sensible minimum thickness (avoid sliver brushes that
            //    q3map2 may discard)
            const double MinHalfThickness = 1.0;
            var doSplit = false;
            double zSplit = 0;
            if (splitZForEdge is not null && upperSideTexture is not null)
            {
                zSplit = splitZForEdge(i);
                doSplit = zSplit > zBottom + MinHalfThickness
                          && zSplit < zTop - MinHalfThickness;
            }

            if (!doSplit)
            {
                var prism = BrushFactory.MakeVerticalPrism(
                    footprint, zBottom, zTop,
                    sideTexture: sideTexture,
                    topTexture: capTexture,
                    bottomTexture: capTexture);
                brushes.Add(prism);
                continue;
            }

            // Split at a shared Z plane. Both brushes use the same XY
            // footprint so the seam is a perfect plane match (q3map2 won't
            // create T-junctions and there are no leaks).
            var lower = BrushFactory.MakeVerticalPrism(
                footprint, zBottom, zSplit,
                sideTexture: sideTexture,
                topTexture: capTexture,
                bottomTexture: capTexture);
            var upper = BrushFactory.MakeVerticalPrism(
                footprint, zSplit, zTop,
                sideTexture: upperSideTexture!,
                topTexture: capTexture,
                bottomTexture: capTexture);
            brushes.Add(lower);
            brushes.Add(upper);
        }
        return brushes;
    }

    private static double PolygonSignedArea(IReadOnlyList<Vec2> poly)
    {
        double s = 0;
        var n = poly.Count;
        for (var i = 0; i < n; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % n];
            s += a.X * b.Y - b.X * a.Y;
        }
        return s * 0.5;
    }
}

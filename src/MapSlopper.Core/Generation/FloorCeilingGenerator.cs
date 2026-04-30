using System;
using System.Collections.Generic;
using MapSlopper.Core.Brushes;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;

namespace MapSlopper.Core.Generation;

/// <summary>
/// Floor + ceiling brush generation for an arbitrary simple CCW polygon
/// (convex OR non-convex) and an underlying heightmap. Q3 brushes must be
/// convex, so we first decompose the polygon into convex pieces using
/// Hertel-Mehlhorn (PolygonDecomposer.ConvexDecompose).
///
/// Per convex piece:
///   * Ceiling: ONE prism shaped like the piece.
///   * Floor: scan the cells whose centre lies inside the piece. If every
///     such cell shares the SAME non-zero height, emit ONE prism shaped
///     like the piece (the user's "big slab" preference). Otherwise
///     iteratively extract the LARGEST axis-aligned rectangle of uniform
///     height (histogram-stack algorithm), emit one prism per rectangle
///     clipped against the piece. "Largest first" keeps wide rectangular
///     interiors intact while pushing thin variations to leftover cells
///     along edges.
///
/// Wall ring is generated separately; this module only emits floor/ceiling.
/// </summary>
public static class FloorCeilingGenerator
{
    public sealed class Result
    {
        public List<Brush> FloorBrushes { get; } = new();
        public List<Brush> CeilingBrushes { get; } = new();
        public List<string> Warnings { get; } = new();
    }

    private const ushort SentinelOutside = ushort.MaxValue;

    public static Result Generate(
        Polygon2D ccwPolygon,
        Heightmap16 heightmap,
        double zFloorBase,
        double zCeilingBottom,
        double ceilingThickness,
        string floorTexture,
        string wallTexture,
        string ceilingTexture)
    {
        if (!ccwPolygon.IsCcw())
            throw new ArgumentException("Polygon must be CCW.", nameof(ccwPolygon));
        if (ceilingThickness <= 0)
            throw new ArgumentException("ceilingThickness must be positive.");
        if (zCeilingBottom <= zFloorBase)
            throw new ArgumentException("Ceiling must be above floor.");

        var result = new Result();
        const double MinFloorCeilingGap = 8.0;
        var maxFloorTopAllowed = zCeilingBottom - MinFloorCeilingGap;

        var pieces = PolygonDecomposer.ConvexDecompose(ccwPolygon.Vertices);
        if (pieces.Count == 0)
        {
            result.Warnings.Add("Polygon decomposition returned no pieces; outline may be degenerate.");
            return result;
        }

        // Ceiling: one prism per convex piece.
        foreach (var piece in pieces)
        {
            result.CeilingBrushes.Add(BrushFactory.MakeVerticalPrism(
                piece, zCeilingBottom, zCeilingBottom + ceilingThickness,
                sideTexture: wallTexture,
                topTexture: ceilingTexture,
                bottomTexture: ceilingTexture));
        }

        // Floor: per piece.
        var clampedCells = 0;
        ushort clampedMaxRaw = 0;
        var (oMin, _) = heightmap.WorldBounds();
        var cs = heightmap.CellSize;

        foreach (var piece in pieces)
        {
            EmitFloorForPiece(
                piece, heightmap, oMin, cs,
                zFloorBase, maxFloorTopAllowed,
                floorTexture, wallTexture,
                result.FloorBrushes,
                ref clampedCells, ref clampedMaxRaw);
        }

        if (clampedCells > 0)
        {
            result.Warnings.Add(
                $"{clampedCells} heightmap cell(s) painted above ceiling (max raw={clampedMaxRaw}); "
                + $"floor tops clamped to z={maxFloorTopAllowed:F0}. Lower paint values or raise ceilingHeight.");
        }
        return result;
    }

    private static void EmitFloorForPiece(
        List<Vec2> piece, Heightmap16 hm, Vec2 oMin, double cs,
        double zFloorBase, double maxFloorTopAllowed,
        string floorTexture, string wallTexture,
        List<Brush> floorBrushes,
        ref int clampedCells, ref ushort clampedMaxRaw)
    {
        // Bounding box of this convex piece in cell coords.
        var (pMin, pMax) = ComputeBounds(piece);
        var cx0 = Math.Max(0, (int)Math.Floor((pMin.X - oMin.X) / cs));
        var cy0 = Math.Max(0, (int)Math.Floor((pMin.Y - oMin.Y) / cs));
        var cx1 = Math.Min(hm.Width - 1, (int)Math.Floor((pMax.X - oMin.X) / cs));
        var cy1 = Math.Min(hm.Height - 1, (int)Math.Floor((pMax.Y - oMin.Y) / cs));
        if (cx1 < cx0 || cy1 < cy0) return;

        var w = cx1 - cx0 + 1;
        var h = cy1 - cy0 + 1;
        var heights = new ushort[w * h];
        var anyNonZero = false;
        ushort firstNonZero = 0;
        var allSame = true;

        for (var dy = 0; dy < h; dy++)
        for (var dx = 0; dx < w; dx++)
        {
            var center = new Vec2(
                oMin.X + (cx0 + dx + 0.5) * cs,
                oMin.Y + (cy0 + dy + 0.5) * cs);
            if (!ConvexContains(piece, center))
            {
                heights[dy * w + dx] = SentinelOutside;
                continue;
            }
            var raw = hm.Sample(cx0 + dx, cy0 + dy);
            var floorTop = zFloorBase + raw;
            if (floorTop > maxFloorTopAllowed)
            {
                clampedCells++;
                if (raw > clampedMaxRaw) clampedMaxRaw = raw;
                raw = (ushort)Math.Max(0, maxFloorTopAllowed - zFloorBase);
                if (raw == SentinelOutside) raw = (ushort)(SentinelOutside - 1);
            }
            heights[dy * w + dx] = raw;
            if (raw != 0)
            {
                if (!anyNonZero) { firstNonZero = raw; anyNonZero = true; }
                else if (raw != firstNonZero) allSame = false;
            }
            else
            {
                // Zero-cells inside polygon mean "no floor here". Treat as a
                // non-uniform piece so we don't emit a slab over them.
                allSame = false;
            }
        }

        if (!anyNonZero) return; // entire piece has zero floor; nothing to emit

        // FAST PATH: every interior cell has the same non-zero height ->
        // emit one prism shaped exactly like the piece. Keeps convex
        // uniform-floor maps at the optimal N+2 brush count.
        if (allSame)
        {
            floorBrushes.Add(BrushFactory.MakeVerticalPrism(
                piece, zFloorBase, zFloorBase + firstNonZero,
                sideTexture: wallTexture,
                topTexture: floorTexture,
                bottomTexture: floorTexture));
            return;
        }

        // GENERAL PATH: greedy "largest uniform rectangle first".
        var consumed = new bool[w * h];
        while (true)
        {
            var best = LargestUniformRectangle(heights, consumed, w, h);
            if (best is null) break;
            var r = best.Value;
            for (var yy = r.y0; yy <= r.y1; yy++)
                for (var xx = r.x0; xx <= r.x1; xx++)
                    consumed[yy * w + xx] = true;

            var rxMin = oMin.X + (cx0 + r.x0) * cs;
            var ryMin = oMin.Y + (cy0 + r.y0) * cs;
            var rxMax = oMin.X + (cx0 + r.x1 + 1) * cs;
            var ryMax = oMin.Y + (cy0 + r.y1 + 1) * cs;
            var clipped = RectangleClipper.Clip(piece, rxMin, ryMin, rxMax, ryMax);
            clipped = RectangleClipper.RemoveDegenerate(clipped);
            if (clipped.Count < 3) continue;
            var floorTop = zFloorBase + r.height;
            floorBrushes.Add(BrushFactory.MakeVerticalPrism(
                clipped, zFloorBase, floorTop,
                sideTexture: wallTexture,
                topTexture: floorTexture,
                bottomTexture: floorTexture));
        }
    }

    private static (Vec2 Min, Vec2 Max) ComputeBounds(List<Vec2> poly)
    {
        var minX = poly[0].X; var minY = poly[0].Y;
        var maxX = minX; var maxY = minY;
        foreach (var v in poly)
        {
            if (v.X < minX) minX = v.X;
            if (v.Y < minY) minY = v.Y;
            if (v.X > maxX) maxX = v.X;
            if (v.Y > maxY) maxY = v.Y;
        }
        return (new Vec2(minX, minY), new Vec2(maxX, maxY));
    }

    /// <summary>
    /// Convex CCW polygon containment: point is inside iff it sits to the
    /// LEFT of every edge (or on it). Faster + simpler than ray-casting.
    /// </summary>
    private static bool ConvexContains(List<Vec2> ccw, Vec2 p)
    {
        var n = ccw.Count;
        for (var i = 0; i < n; i++)
        {
            var a = ccw[i];
            var b = ccw[(i + 1) % n];
            var cr = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            if (cr < -1e-9) return false;
        }
        return true;
    }

    private static (int x0, int y0, int x1, int y1, ushort height)? LargestUniformRectangle(
        ushort[] heights, bool[] consumed, int w, int h)
    {
        var distinct = new HashSet<ushort>();
        for (var i = 0; i < heights.Length; i++)
        {
            if (consumed[i]) continue;
            var v = heights[i];
            if (v == 0 || v == SentinelOutside) continue;
            distinct.Add(v);
        }
        if (distinct.Count == 0) return null;

        (int x0, int y0, int x1, int y1, ushort h) bestRect = (0, 0, -1, -1, 0);
        var bestArea = 0;
        var heightsCol = new int[w];
        foreach (var v in distinct)
        {
            for (var x = 0; x < w; x++) heightsCol[x] = 0;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var idx = y * w + x;
                    if (!consumed[idx] && heights[idx] == v) heightsCol[x]++;
                    else heightsCol[x] = 0;
                }
                LargestInHistogram(heightsCol, w, y, v, ref bestRect, ref bestArea);
            }
        }
        if (bestArea == 0) return null;
        return bestRect;
    }

    private static void LargestInHistogram(
        int[] bars, int w, int rowBaseline, ushort heightValue,
        ref (int x0, int y0, int x1, int y1, ushort h) bestRect,
        ref int bestArea)
    {
        var stackIdx = new int[w + 1];
        var top = -1;
        for (var i = 0; i <= w; i++)
        {
            var bar = i == w ? 0 : bars[i];
            while (top >= 0 && bars[stackIdx[top]] > bar)
            {
                var hVal = bars[stackIdx[top]];
                top--;
                var left = top < 0 ? 0 : stackIdx[top] + 1;
                var right = i - 1;
                var width = right - left + 1;
                var area = hVal * width;
                if (area > bestArea)
                {
                    bestArea = area;
                    bestRect = (left, rowBaseline - hVal + 1, right, rowBaseline, heightValue);
                }
            }
            stackIdx[++top] = i;
        }
    }
}

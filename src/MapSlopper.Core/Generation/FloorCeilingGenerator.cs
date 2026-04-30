using System;
using System.Collections.Generic;
using MapSlopper.Core.Brushes;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;

namespace MapSlopper.Core.Generation;

/// <summary>
/// Generates floor and ceiling brushes by decomposing the heightmap into the
/// MINIMAL number of axis-aligned rectangles per distinct height value, then
/// clipping each rectangle by the polygon outline. The previous implementation
/// emitted one prism per (cell × triangle) intersection which produced
/// thousands of tiny brushes and an additional diagonal cut inside every
/// polygon. This version produces:
///   * exactly ONE ceiling brush (the whole polygon, since the ceiling is flat),
///   * exactly ONE floor brush when the heightmap is uniform inside the polygon,
///   * a small number of large floor brushes when heights vary, with edges
///     placed only where the height actually changes.
/// </summary>
public static class FloorCeilingGenerator
{
    public sealed class Result
    {
        public List<Brush> FloorBrushes { get; } = new();
        public List<Brush> CeilingBrushes { get; } = new();
        public List<string> Warnings { get; } = new();
    }

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
        var maxFloorTop = zCeilingBottom - MinFloorCeilingGap;
        var clampedCells = 0;
        ushort clampedMaxRaw = 0;

        // Polygon as a List<Vec2> for clipping.
        var polyVerts = new List<Vec2>(ccwPolygon.Vertices);

        // -------- Ceiling: ONE brush spanning the whole polygon. --------
        result.CeilingBrushes.Add(BrushFactory.MakeVerticalPrism(
            polyVerts, zCeilingBottom, zCeilingBottom + ceilingThickness,
            sideTexture: wallTexture,
            topTexture: ceilingTexture,
            bottomTexture: ceilingTexture));

        // -------- Floor: greedy rectangle decomposition by height. --------
        // Restrict scan to the heightmap window covering the polygon AABB.
        var (pMin, pMax) = ccwPolygon.Bounds();
        var (oMin, _) = heightmap.WorldBounds();
        var cs = heightmap.CellSize;
        var cx0 = Math.Max(0, (int)Math.Floor((pMin.X - oMin.X) / cs));
        var cy0 = Math.Max(0, (int)Math.Floor((pMin.Y - oMin.Y) / cs));
        var cx1 = Math.Min(heightmap.Width - 1, (int)Math.Floor((pMax.X - oMin.X) / cs));
        var cy1 = Math.Min(heightmap.Height - 1, (int)Math.Floor((pMax.Y - oMin.Y) / cs));
        if (cx1 < cx0 || cy1 < cy0) return result;

        var w = cx1 - cx0 + 1;
        var h = cy1 - cy0 + 1;
        // Local height grid; clamp to the floor-ceiling gap.
        var heights = new ushort[w * h];
        for (var dy = 0; dy < h; dy++)
        for (var dx = 0; dx < w; dx++)
        {
            var raw = heightmap.Sample(cx0 + dx, cy0 + dy);
            var floorTop = zFloorBase + raw;
            if (floorTop > maxFloorTop)
            {
                clampedCells++;
                if (raw > clampedMaxRaw) clampedMaxRaw = raw;
                // Map raw to the clamped value (cells with same effective height
                // still merge, even if their raw values differ above the cap).
                raw = (ushort)Math.Max(0, maxFloorTop - zFloorBase);
            }
            heights[dy * w + dx] = raw;
        }

        var consumed = new bool[w * h];
        var rects = new List<(int x0, int y0, int x1, int y1, ushort height)>();
        for (var dy = 0; dy < h; dy++)
        for (var dx = 0; dx < w; dx++)
        {
            if (consumed[dy * w + dx]) continue;
            var height = heights[dy * w + dx];
            if (height == 0) { consumed[dy * w + dx] = true; continue; }

            // Greedy: extend right as far as identical height & not consumed.
            var x1 = dx;
            while (x1 + 1 < w
                && !consumed[dy * w + (x1 + 1)]
                && heights[dy * w + (x1 + 1)] == height)
                x1++;
            // Extend down: each next row must have identical height across [dx..x1].
            var y1 = dy;
            while (y1 + 1 < h)
            {
                var ok = true;
                for (var xx = dx; xx <= x1; xx++)
                {
                    if (consumed[(y1 + 1) * w + xx] || heights[(y1 + 1) * w + xx] != height)
                    { ok = false; break; }
                }
                if (!ok) break;
                y1++;
            }
            for (var yy = dy; yy <= y1; yy++)
                for (var xx = dx; xx <= x1; xx++)
                    consumed[yy * w + xx] = true;
            rects.Add((dx, dy, x1, y1, height));
        }

        // Convert each rect to world-space and clip by the polygon.
        foreach (var r in rects)
        {
            var rxMin = oMin.X + (cx0 + r.x0) * cs;
            var ryMin = oMin.Y + (cy0 + r.y0) * cs;
            var rxMax = oMin.X + (cx0 + r.x1 + 1) * cs;
            var ryMax = oMin.Y + (cy0 + r.y1 + 1) * cs;

            var clipped = RectangleClipper.Clip(polyVerts, rxMin, ryMin, rxMax, ryMax);
            clipped = RectangleClipper.RemoveDegenerate(clipped);
            if (clipped.Count < 3) continue;

            var floorTop = zFloorBase + r.height;
            if (floorTop <= zFloorBase) continue;

            var floor = BrushFactory.MakeVerticalPrism(
                clipped, zFloorBase, floorTop,
                sideTexture: wallTexture,
                topTexture: floorTexture,
                bottomTexture: floorTexture);
            result.FloorBrushes.Add(floor);
        }

        if (clampedCells > 0)
        {
            result.Warnings.Add(
                $"{clampedCells} heightmap cell(s) painted above ceiling (max raw={clampedMaxRaw}); "
                + $"floor tops clamped to z={maxFloorTop:F0}. Lower paint values or raise ceilingHeight.");
        }
        return result;
    }
}

using System;
using System.Collections.Generic;
using MapSlopper.Core.Brushes;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;

namespace MapSlopper.Core.Generation;

/// <summary>
/// Generates floor and ceiling brushes by intersecting the heightmap grid with
/// the polygon outline. Each cell becomes one or more convex prisms whose
/// XY footprint is exactly the polygon ∩ cell. The ceiling reuses the same
/// horizontal subdivision but has constant Z, allowing manual edits later.
/// </summary>
public static class FloorCeilingGenerator
{
    public sealed class Result
    {
        public List<Brush> FloorBrushes { get; } = new();
        public List<Brush> CeilingBrushes { get; } = new();
        public List<string> Warnings { get; } = new();
    }

    /// <summary>
    /// <paramref name="zFloorBase"/> is the bottom Z of the floor slab (typically 0).
    /// Floor cell tops sit at the cell's heightmap value (interpreted as Quake units).
    /// Ceiling brushes span <paramref name="zCeilingBottom"/> to <c>zCeilingBottom + ceilingThickness</c>.
    /// </summary>
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
        // Q3 world hull is +/-32768; q3map2 silently discards brushes that escape it.
        // Reserve a minimum gap so the floor never punches the ceiling either.
        const double MinFloorCeilingGap = 8.0;
        var maxFloorTop = zCeilingBottom - MinFloorCeilingGap;
        var clampedCells = 0;
        ushort clampedMaxRaw = 0;

        // 1. Triangulate the polygon ONCE into convex CCW triangles.
        var verts = new List<Vec2>(ccwPolygon.Vertices);
        var tris = PolygonTriangulator.Triangulate(verts);

        // 2. For each cell that is at all touched by the polygon AABB, clip each
        //    triangle to the cell rectangle and emit prisms.
        var (pMin, pMax) = ccwPolygon.Bounds();
        var (oMin, _) = heightmap.WorldBounds();

        var cx0 = Math.Max(0, (int)Math.Floor((pMin.X - oMin.X) / heightmap.CellSize));
        var cy0 = Math.Max(0, (int)Math.Floor((pMin.Y - oMin.Y) / heightmap.CellSize));
        var cx1 = Math.Min(heightmap.Width - 1, (int)Math.Floor((pMax.X - oMin.X) / heightmap.CellSize));
        var cy1 = Math.Min(heightmap.Height - 1, (int)Math.Floor((pMax.Y - oMin.Y) / heightmap.CellSize));

        for (var cy = cy0; cy <= cy1; cy++)
        for (var cx = cx0; cx <= cx1; cx++)
        {
            var cellMin = heightmap.CellWorldMin(cx, cy);
            var cellMax = heightmap.CellWorldMax(cx, cy);
            var raw = heightmap.Sample(cx, cy);
            var floorTop = zFloorBase + raw;
            if (floorTop > maxFloorTop)
            {
                floorTop = maxFloorTop;
                clampedCells++;
                if (raw > clampedMaxRaw) clampedMaxRaw = raw;
            }
            if (floorTop <= zFloorBase) continue; // zero-height cell → no floor brush

            // Clip each triangle to this cell's rectangle.
            foreach (var tri in tris)
            {
                var subject = new List<Vec2> { tri.A, tri.B, tri.C };
                var clipped = RectangleClipper.Clip(
                    subject, cellMin.X, cellMin.Y, cellMax.X, cellMax.Y);
                clipped = RectangleClipper.RemoveDegenerate(clipped);
                if (clipped.Count < 3) continue;

                // Each triangle's clip against a rectangle is a convex polygon (3..7 verts).
                // Emit a vertical prism for the floor.
                var floor = BrushFactory.MakeVerticalPrism(
                    clipped, zFloorBase, floorTop,
                    sideTexture: wallTexture,
                    topTexture: floorTexture,
                    bottomTexture: floorTexture);
                result.FloorBrushes.Add(floor);

                // Ceiling: same XY footprint, between zCeilingBottom and zCeilingBottom+thickness.
                var ceiling = BrushFactory.MakeVerticalPrism(
                    clipped, zCeilingBottom, zCeilingBottom + ceilingThickness,
                    sideTexture: wallTexture,
                    topTexture: ceilingTexture,
                    bottomTexture: ceilingTexture);
                result.CeilingBrushes.Add(ceiling);
            }
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

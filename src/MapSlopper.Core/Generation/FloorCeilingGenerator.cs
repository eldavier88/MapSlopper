using System;
using System.Collections.Generic;
using MapSlopper.Core.Brushes;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;

namespace MapSlopper.Core.Generation;

/// <summary>
/// Generates floor + ceiling brushes for a (possibly non-convex) simple
/// CCW polygon using the cell-clip-then-merge algorithm:
///
///   1. Decompose the polygon into convex pieces (Hertel-Mehlhorn). Q3
///      requires convex brushes, so this step is mandatory whenever the
///      outline has reflex vertices.
///   2. For each convex piece P:
///        a. Quantize every heightmap cell's raw value to <see cref="HeightQuantStep"/>
///           and clamp to the floor-base / ceiling envelope. Quantization
///           collapses paint-brush falloff gradients (e.g. raw = 1, 13, 64,
///           125 from a single brush stroke) into a small number of
///           intentional levels. Without it the per-value rectangle
///           extraction explodes into hundreds of slivers.
///        b. For every cell whose AABB intersects P, clip P against the
///           cell-AABB to produce a small convex polygon, tag with that
///           cell's quantized value.
///        c. Iteratively merge adjacent same-value pieces while the union
///           remains convex (PolygonDecomposer.TryMergeConvexPolygons).
///           Same-value cells inside the piece collapse back into one big
///           polygon that follows the piece boundary cleanly. Cells of
///           different values stay separated by their cell-edge boundary.
///        d. Emit one floor brush + one ceiling brush per remaining (poly,
///           value) — both share the same xy footprint so floor and ceiling
///           subdivide identically (per the user's spec).
///
/// The "cells whose AABB intersects P" rule (rather than "centre inside P")
/// guarantees the union of cell-clipped polygons exactly tiles P with no
/// gaps -- essential for q3map2 leak-free compilation.
/// </summary>
public static class FloorCeilingGenerator
{
    /// <summary>
    /// Quantization step for raw heightmap values, in Q3 units. 16 keeps
    /// the natural Q3 grid resolution while collapsing brush-falloff
    /// gradients into discrete levels.
    /// </summary>
    public const ushort HeightQuantStep = 16;

    /// <summary>
    /// Minimum value (in raw units) the quantizer outputs. Equal to the
    /// project's floor base thickness so unpainted cells still get a
    /// "ground level" floor at z = zFloorBase + thickness = 0. Without
    /// this, unpainted cells inside the polygon would be holes -> guaranteed
    /// q3map2 leak.
    /// </summary>
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
        var maxFloorTopAllowed = zCeilingBottom - MinFloorCeilingGap;
        // Minimum quantized value -> guarantees every cell gets a floor
        // brush of non-zero thickness so the polygon interior is always
        // hermetically covered. Using zFloorBase as the bottom of every
        // floor brush, the minimum top must be > zFloorBase. Use the next
        // quantization step.
        var minQuant = (ushort)Math.Max((int)HeightQuantStep, 1);
        var maxRawAllowed = (ushort)Math.Max(0, Math.Min(ushort.MaxValue, (int)Math.Floor(maxFloorTopAllowed - zFloorBase)));

        var pieces = PolygonDecomposer.ConvexDecompose(ccwPolygon.Vertices);
        if (pieces.Count == 0)
        {
            result.Warnings.Add("Polygon decomposition returned no pieces; outline may be degenerate.");
            return result;
        }

        var (oMin, _) = heightmap.WorldBounds();
        var cs = heightmap.CellSize;
        var clampedCells = 0;
        ushort clampedMaxRaw = 0;

        foreach (var piece in pieces)
        {
            var regions = ComputeRegionsForPiece(
                piece, heightmap, oMin, cs,
                minQuant, maxRawAllowed,
                ref clampedCells, ref clampedMaxRaw);

            foreach (var (footprint, raw) in regions)
            {
                var floorTop = zFloorBase + raw;
                if (floorTop <= zFloorBase) continue;
                result.FloorBrushes.Add(BrushFactory.MakeVerticalPrism(
                    footprint, zFloorBase, floorTop,
                    sideTexture: wallTexture,
                    topTexture: floorTexture,
                    bottomTexture: floorTexture));
                result.CeilingBrushes.Add(BrushFactory.MakeVerticalPrism(
                    footprint, zCeilingBottom, zCeilingBottom + ceilingThickness,
                    sideTexture: wallTexture,
                    topTexture: ceilingTexture,
                    bottomTexture: ceilingTexture));
            }
        }

        if (clampedCells > 0)
        {
            result.Warnings.Add(
                $"{clampedCells} heightmap cell(s) painted above ceiling (max raw={clampedMaxRaw}); "
                + $"floor tops clamped to z={maxFloorTopAllowed:F0}. Lower paint values or raise ceilingHeight.");
        }
        return result;
    }

    /// <summary>
    /// Cell-clip + merge: tile the convex piece with cell-clipped polygons
    /// tagged by quantized height, then greedily merge same-value adjacent
    /// pieces while convex. Returns the final list of (footprint, rawHeight)
    /// pairs whose footprints exactly tile the input piece.
    /// </summary>
    private static List<(List<Vec2> Footprint, ushort RawHeight)> ComputeRegionsForPiece(
        List<Vec2> piece,
        Heightmap16 hm,
        Vec2 oMin,
        double cs,
        ushort minQuant,
        ushort maxRawAllowed,
        ref int clampedCells,
        ref ushort clampedMaxRaw)
    {
        var output = new List<(List<Vec2>, ushort)>();
        var (pMin, pMax) = ComputeBounds(piece);

        // Cell index range covering the piece's AABB. Use floor/ceil so we
        // pick up cells whose AABB merely TOUCHES the piece on an edge.
        var cx0 = (int)Math.Floor((pMin.X - oMin.X) / cs);
        var cy0 = (int)Math.Floor((pMin.Y - oMin.Y) / cs);
        var cx1 = (int)Math.Floor((pMax.X - oMin.X) / cs);
        var cy1 = (int)Math.Floor((pMax.Y - oMin.Y) / cs);
        cx0 = Math.Max(0, cx0); cy0 = Math.Max(0, cy0);
        cx1 = Math.Min(hm.Width - 1, cx1); cy1 = Math.Min(hm.Height - 1, cy1);
        if (cx1 < cx0 || cy1 < cy0) return output;

        var pieces = new List<(List<Vec2> Poly, ushort RawHeight)>();
        for (var cy = cy0; cy <= cy1; cy++)
        for (var cx = cx0; cx <= cx1; cx++)
        {
            var cellMinX = oMin.X + cx * cs;
            var cellMinY = oMin.Y + cy * cs;
            var cellMaxX = cellMinX + cs;
            var cellMaxY = cellMinY + cs;
            var clipped = RectangleClipper.Clip(piece, cellMinX, cellMinY, cellMaxX, cellMaxY);
            clipped = RectangleClipper.RemoveDegenerate(clipped);
            if (clipped.Count < 3) continue;
            // Skip near-zero-area slivers from numerical noise.
            if (PolygonArea(clipped) < cs * cs * 1e-4) continue;

            var raw = hm.Sample(cx, cy);
            // Clamp above ceiling.
            if (raw > maxRawAllowed)
            {
                clampedCells++;
                if (raw > clampedMaxRaw) clampedMaxRaw = raw;
                raw = maxRawAllowed;
            }
            // Quantize.
            var q = (ushort)((raw + HeightQuantStep / 2) / HeightQuantStep * HeightQuantStep);
            // Clamp lower bound.
            if (q < minQuant) q = minQuant;
            if (q > maxRawAllowed) q = maxRawAllowed;
            pieces.Add((clipped, q));
        }

        // Greedy merge: iterate until nothing more can be combined.
        // Repeatedly pick any pair of polygons sharing an edge with the
        // same height value, merge if their union remains convex.
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var a = 0; a < pieces.Count && !changed; a++)
            {
                for (var b = a + 1; b < pieces.Count && !changed; b++)
                {
                    if (pieces[a].RawHeight != pieces[b].RawHeight) continue;
                    if (!PolygonDecomposer.TryMergeConvexPolygons(pieces[a].Poly, pieces[b].Poly, out var merged))
                        continue;
                    // Prune collinear vertices on the merged result. Two
                    // adjacent cell-clipped polygons may share a boundary
                    // composed of multiple collinear cell-edge segments;
                    // TryMergeConvexPolygons only removes ONE shared edge,
                    // so without this prune the polygon retains duplicate
                    // vertices at every cell-corner along the shared side
                    // and a subsequent merge attempt with a third polygon
                    // would fail to find a unique shared edge -> floor brush
                    // ends up with hundreds of redundant side faces and a
                    // bad-normal/MAX_BUILD_SIDES q3map2 error.
                    var simplified = RectangleClipper.RemoveDegenerate(merged);
                    if (simplified.Count < 3) continue;
                    pieces[a] = (simplified, pieces[a].RawHeight);
                    pieces.RemoveAt(b);
                    changed = true;
                }
            }
        }

        foreach (var p in pieces)
        {
            // Strip collinear vertices that accumulated from merging
            // cell-clipped polygons -- otherwise the piece's straight edge
            // gets one vertex per cell crossing and the resulting brush
            // exceeds q3map2's MAX_BUILD_SIDES.
            var pruned = RectangleClipper.RemoveDegenerate(p.Poly);
            if (pruned.Count < 3) continue;
            output.Add((pruned, p.RawHeight));
        }
        return output;
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

    private static double PolygonArea(IReadOnlyList<Vec2> poly)
    {
        double a = 0;
        for (var i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return Math.Abs(a) * 0.5;
    }
}

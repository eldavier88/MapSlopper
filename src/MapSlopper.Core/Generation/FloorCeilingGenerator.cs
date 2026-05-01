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
        // Overlap between adjacent cell stacks (raw units). Brushes share
        // exact Z planes already, but a small overlap is forgiving against
        // floating point noise on the seam plane. Same constant as walls.
        const ushort StackOverlapRaw = HeightQuantStep;

        foreach (var piece in pieces)
        {
            // Compute per-cell quantized heights inside this piece.
            var (regions, baseRaw) = ComputeRegionsForPiece(
                piece, heightmap, oMin, cs,
                minQuant, maxRawAllowed,
                ref clampedCells, ref clampedMaxRaw);

            // Per-piece BASE slab: top at the piece's lowest quantized cell
            // top (baseTop), bottom at zFloorBase.  We extend the base slab
            // all the way down to the global floor base so that adjacent
            // convex pieces (which share decomposition diagonals with no
            // walls) do not leave a vertical gap when their floor heights
            // differ.
            var pieceCcw = RectangleClipper.RemoveDegenerate(new System.Collections.Generic.List<Vec2>(piece));
            if (pieceCcw.Count < 3) continue;
            var baseTop = zFloorBase + baseRaw;
            var baseBottom = zFloorBase;
            if (baseTop > zFloorBase)
            {
                result.FloorBrushes.Add(BrushFactory.MakeVerticalPrism(
                    pieceCcw, baseBottom, baseTop,
                    sideTexture: wallTexture,
                    topTexture: floorTexture,
                    bottomTexture: floorTexture));
                result.CeilingBrushes.Add(BrushFactory.MakeVerticalPrism(
                    pieceCcw, zCeilingBottom, zCeilingBottom + ceilingThickness,
                    sideTexture: wallTexture,
                    topTexture: ceilingTexture,
                    bottomTexture: ceilingTexture));
            }

            // Stacked elevations: one brush per merged same-(top,bottom)
            // region whose top is strictly above the piece's base. Each
            // region's BOTTOM Z floats up to its lowest neighbour's top so
            // plateau interiors get a thin (~HeightQuantStep) cap brush
            // and only cells bordering a step keep a tall cladding brush
            // down to the lower neighbour. The volume above the base slab
            // and below a plateau cap is enclosed (sealed by perimeter
            // cladding brushes) so the level remains leak-free.
            foreach (var (footprint, topRaw, bottomRaw) in regions)
            {
                if (topRaw <= baseRaw) continue;
                var floorTop = zFloorBase + topRaw;
                if (floorTop <= baseTop) continue;
                var bottom = Math.Max(baseTop, zFloorBase + bottomRaw - StackOverlapRaw);
                if (bottom >= floorTop) bottom = baseTop;
                result.FloorBrushes.Add(BrushFactory.MakeVerticalPrism(
                    footprint, bottom, floorTop,
                    sideTexture: wallTexture,
                    topTexture: floorTexture,
                    bottomTexture: floorTexture));
                // Mirror stack with a matching ceiling region so floor and
                // ceiling subdivide identically (per project spec).
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
    /// tagged by (quantized topRaw, bottomRaw) where bottomRaw is the min
    /// neighbour height (so plateau interiors collapse to thin slabs and
    /// only step-edge cells get a tall cladding brush). Returns the final
    /// list of (footprint, topRaw, bottomRaw) triples whose footprints
    /// exactly tile the input piece, plus the minimum topRaw seen across
    /// the piece (informational; the caller now uses a fixed base level).
    /// </summary>
    private static (List<(List<Vec2> Footprint, ushort TopRaw, ushort BottomRaw)> Regions, ushort BaseRaw) ComputeRegionsForPiece(
        List<Vec2> piece,
        Heightmap16 hm,
        Vec2 oMin,
        double cs,
        ushort minQuant,
        ushort maxRawAllowed,
        ref int clampedCells,
        ref ushort clampedMaxRaw)
    {
        var output = new List<(List<Vec2> Footprint, ushort TopRaw, ushort BottomRaw)>();
        var (pMin, pMax) = ComputeBounds(piece);
        ushort baseRaw = ushort.MaxValue;

        // Cell index range covering the piece's AABB. Use floor/ceil so we
        // pick up cells whose AABB merely TOUCHES the piece on an edge.
        var cx0 = (int)Math.Floor((pMin.X - oMin.X) / cs);
        var cy0 = (int)Math.Floor((pMin.Y - oMin.Y) / cs);
        var cx1 = (int)Math.Floor((pMax.X - oMin.X) / cs);
        var cy1 = (int)Math.Floor((pMax.Y - oMin.Y) / cs);
        cx0 = Math.Max(0, cx0); cy0 = Math.Max(0, cy0);
        cx1 = Math.Min(hm.Width - 1, cx1); cy1 = Math.Min(hm.Height - 1, cy1);
        if (cx1 < cx0 || cy1 < cy0) return (output, minQuant);

        // Helper: quantize+clamp a raw value (without recording clamp
        // warnings -- those are only for cells *inside* this piece).
        ushort QuantClamp(ushort raw)
        {
            if (raw > maxRawAllowed) raw = maxRawAllowed;
            var q = (ushort)((raw + HeightQuantStep / 2) / HeightQuantStep * HeightQuantStep);
            if (q < minQuant) q = minQuant;
            if (q > maxRawAllowed) q = maxRawAllowed;
            return q;
        }
        // Sample neighbour's quantized "exposed top" raw. Off-grid =
        // minQuant (= the piece-base level), so cells at the polygon edge
        // get bottomRaw = minQuant -> stack drops to baseTop, matching
        // the wall behaviour.
        ushort NeighbourTopRaw(int nx, int ny)
        {
            if (nx < 0 || ny < 0 || nx >= hm.Width || ny >= hm.Height) return minQuant;
            return QuantClamp(hm.Sample(nx, ny));
        }

        var pieces = new List<(List<Vec2> Poly, ushort TopRaw, ushort BottomRaw)>();
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
            // Don't drop tiny corner fragments by area: at sharp polygon
            // corners (e.g. star tips, narrow notches) the cell containing
            // the vertex can be arbitrarily small. Dropping it leaves a
            // pinhole between the floor edge and the wall's inner face,
            // which q3map2 reports as a leak. The only valid skip is "not
            // a polygon" (< 3 vertices after dedupe).

            var raw = hm.Sample(cx, cy);
            // Track clamp warnings for cells inside the piece.
            if (raw > maxRawAllowed)
            {
                clampedCells++;
                if (raw > clampedMaxRaw) clampedMaxRaw = raw;
            }
            var topRaw = QuantClamp(raw);
            if (topRaw < baseRaw) baseRaw = topRaw;

            // Bottom raw: lowest neighbour's exposed top, clamped to
            // [minQuant, topRaw]. Plateau interior cells (all 4 neighbours
            // share this cell's height) collapse to bottomRaw == topRaw,
            // which the caller turns into a thin stack right under the
            // top. Step-edge cells get a low neighbour -> tall cladding
            // brush (necessary geometry).
            var nMin = (ushort)Math.Min(
                Math.Min(NeighbourTopRaw(cx + 1, cy), NeighbourTopRaw(cx - 1, cy)),
                Math.Min(NeighbourTopRaw(cx, cy + 1), NeighbourTopRaw(cx, cy - 1)));
            if (nMin > topRaw) nMin = topRaw;
            if (nMin < minQuant) nMin = minQuant;
            pieces.Add((clipped, topRaw, nMin));
        }

        // Greedy merge: same (TopRaw, BottomRaw) pairs while the union
        // remains convex.
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var a = 0; a < pieces.Count && !changed; a++)
            {
                for (var b = a + 1; b < pieces.Count && !changed; b++)
                {
                    if (pieces[a].TopRaw != pieces[b].TopRaw) continue;
                    if (pieces[a].BottomRaw != pieces[b].BottomRaw) continue;
                    if (!PolygonDecomposer.TryMergeConvexPolygons(pieces[a].Poly, pieces[b].Poly, out var merged))
                        continue;
                    // Prune collinear vertices on the merged result -- see
                    // the long comment in the original code: without this
                    // we hit q3map2 MAX_BUILD_SIDES on long shared edges.
                    var simplified = RectangleClipper.RemoveDegenerate(merged);
                    if (simplified.Count < 3) continue;
                    pieces[a] = (simplified, pieces[a].TopRaw, pieces[a].BottomRaw);
                    pieces.RemoveAt(b);
                    changed = true;
                }
            }
        }

        foreach (var p in pieces)
        {
            var pruned = RectangleClipper.RemoveDegenerate(p.Poly);
            if (pruned.Count < 3) continue;
            output.Add((pruned, p.TopRaw, p.BottomRaw));
        }
        if (baseRaw == ushort.MaxValue) baseRaw = minQuant;
        return (output, baseRaw);
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

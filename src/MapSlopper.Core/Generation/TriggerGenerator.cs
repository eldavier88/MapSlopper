using System;
using System.Collections.Generic;
using System.Globalization;
using MapSlopper.Core.Brushes;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;
using MapSlopper.Core.Triggers;

namespace MapSlopper.Core.Generation;

/// <summary>
/// Generates brush-model trigger entities from the project's trigger paint
/// layer.
///
/// Algorithm (mirrors <see cref="FloorCeilingGenerator"/>'s per-piece
/// cell-clip + greedy-merge approach so triggers have the same brush
/// quality as floors):
///
///   1. Connected-component label every painted cell using 4-connected
///      flood fill. Cells are part of the labelling pool whenever
///      <c>raw != 0</c> AND that raw value is a known type id — we do
///      NOT require the cell centre to lie inside the polygon, otherwise
///      cells whose centre lies just outside the outline (very common at
///      the polygon edge) leave gaps between the trigger brush and the
///      walls.
///
///   2. Convex-decompose the polygon. For every convex piece P and every
///      cell labelled with component C: clip P against the cell AABB.
///      Cells whose AABB merely TOUCHES the piece on an edge are kept —
///      this guarantees the union of cell-clipped polygons exactly tiles
///      the painted footprint inside P, with no gaps to the walls.
///
///   3. Per polygon-piece, greedily merge same-component pieces while
///      their union remains convex (Hertel-Mehlhorn merge), then strip
///      collinear vertices. Per-piece merging is essential: merging
///      ACROSS polygon-pieces produces the multi-rectangle artefact the
///      user reported because pieces from different decomp slabs share
///      cell-edge boundaries that aren't collinear globally.
///
///   4. Per component, gather every merged piece from every polygon-piece
///      into ONE brush-model entity. The entity's brushes span vertically
///      from <c>floorBase + minRaw</c> (the lowest heightmap value the
///      component touches — never below the local floor) to the room
///      ceiling top. Companion <see cref="TriggerTargetSpec"/> point
///      entities sit at the area-weighted XY centroid, with Z above the
///      local floor at that point so target_startTimer etc. spawn on top
///      of any reachable surface.
/// </summary>
public static class TriggerGenerator
{
    public sealed class Result
    {
        /// <summary>Entities to append to the document (brush models and targets, in order).</summary>
        public List<MapEntity> Entities { get; } = new();
        public List<string> Warnings { get; } = new();
    }

    public static Result Generate(
        Polygon2D ccwPolygon,
        Heightmap16 triggerLayer,
        Heightmap16 floorHeightmap,
        TriggerTypeConfig types,
        double zFloorBase,
        double zCeilingTop)
    {
        var result = new Result();
        if (zCeilingTop <= zFloorBase)
        {
            result.Warnings.Add("TriggerGenerator: ceiling top must be above floor base.");
            return result;
        }
        if (types.Types.Count == 0) return result;
        if (!ccwPolygon.IsCcw())
            throw new ArgumentException("Polygon must be CCW.", nameof(ccwPolygon));

        var typeById = new Dictionary<byte, TriggerType>();
        foreach (var t in types.Types) typeById[t.Id] = t;

        var w = triggerLayer.Width;
        var h = triggerLayer.Height;
        if (w == 0 || h == 0) return result;

        // ---------- Pass 1: 4-conn connected components on painted cells -----
        // No polygon-centre filter here: edge cells whose centres fall just
        // outside the outline still contribute paint via the polygon-clip in
        // pass 2, so including them in the flood fill makes the painted
        // region correctly snug against the walls.
        var componentId = new int[w * h];
        var componentToType = new List<byte>(); // index 0 reserved for "no component"
        componentToType.Add(0);
        // Track min/max heightmap raw value seen per component so we can
        // place the brush bottom at the lowest local floor and the
        // target's Z above the highest local floor. Indexed by componentId.
        var componentMinRaw = new List<ushort>(); componentMinRaw.Add(0);
        var componentMaxRaw = new List<ushort>(); componentMaxRaw.Add(0);

        var stack = new Stack<(int X, int Y)>();
        for (var yy = 0; yy < h; yy++)
        for (var xx = 0; xx < w; xx++)
        {
            var idx = yy * w + xx;
            if (componentId[idx] != 0) continue;
            var raw = triggerLayer.Sample(xx, yy);
            if (raw == 0) continue;
            var typeIdNum = (byte)(raw & 0xFF);
            if (!typeById.ContainsKey(typeIdNum)) continue;

            var compId = componentToType.Count;
            componentToType.Add(typeIdNum);
            ushort cMin = ushort.MaxValue, cMax = 0;
            stack.Clear();
            stack.Push((xx, yy));
            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Pop();
                if (cx < 0 || cy < 0 || cx >= w || cy >= h) continue;
                var i = cy * w + cx;
                if (componentId[i] != 0) continue;
                var v = (byte)(triggerLayer.Sample(cx, cy) & 0xFF);
                if (v != typeIdNum) continue;
                componentId[i] = compId;
                // Sample floor heightmap (must share dims/origin with
                // trigger layer; the GUI keeps them in sync).
                var floorRaw = SampleFloorRaw(floorHeightmap, cx, cy);
                if (floorRaw < cMin) cMin = floorRaw;
                if (floorRaw > cMax) cMax = floorRaw;
                stack.Push((cx + 1, cy));
                stack.Push((cx - 1, cy));
                stack.Push((cx, cy + 1));
                stack.Push((cx, cy - 1));
            }
            if (cMin == ushort.MaxValue) cMin = 0;
            componentMinRaw.Add(cMin);
            componentMaxRaw.Add(cMax);
        }

        if (componentToType.Count <= 1) return result;

        // ---------- Pass 2 + 3: per polygon-piece, cell-clip + merge ---------
        // Final merged footprints, indexed by componentId.
        var perComponent = new Dictionary<int, List<List<Vec2>>>();
        // Centroid accumulator per component (area-weighted across pieces).
        var centroidAcc = new Dictionary<int, (double cx, double cy, double area)>();

        var pieces = PolygonDecomposer.ConvexDecompose(ccwPolygon.Vertices);
        if (pieces.Count == 0)
        {
            result.Warnings.Add("TriggerGenerator: polygon decomposition returned no pieces.");
            return result;
        }

        var (oMin, _) = triggerLayer.WorldBounds();
        var cs = triggerLayer.CellSize;

        foreach (var piece in pieces)
        {
            // Tag = componentId. Same-component cells inside this piece
            // collapse back to a clean polygon following the piece
            // boundary; different components remain separated.
            var tagged = new List<(List<Vec2> Poly, int Tag)>();
            var (pMin, pMax) = ComputeBounds(piece);
            // Use floor/ceil so cells whose AABB merely TOUCHES the piece
            // on an edge are visited.
            var cx0 = Math.Max(0, (int)Math.Floor((pMin.X - oMin.X) / cs));
            var cy0 = Math.Max(0, (int)Math.Floor((pMin.Y - oMin.Y) / cs));
            var cx1 = Math.Min(w - 1, (int)Math.Floor((pMax.X - oMin.X) / cs));
            var cy1 = Math.Min(h - 1, (int)Math.Floor((pMax.Y - oMin.Y) / cs));
            for (var yy = cy0; yy <= cy1; yy++)
            for (var xx = cx0; xx <= cx1; xx++)
            {
                var compId = componentId[yy * w + xx];
                if (compId == 0) continue;
                var cellMinX = oMin.X + xx * cs;
                var cellMinY = oMin.Y + yy * cs;
                var clipped = RectangleClipper.Clip(piece, cellMinX, cellMinY, cellMinX + cs, cellMinY + cs);
                clipped = RectangleClipper.RemoveDegenerate(clipped);
                if (clipped.Count < 3) continue;
                tagged.Add((clipped, compId));
            }

            // Greedy merge same-tag pieces within this polygon-piece.
            var changed = true;
            while (changed)
            {
                changed = false;
                for (var a = 0; a < tagged.Count && !changed; a++)
                for (var b = a + 1; b < tagged.Count && !changed; b++)
                {
                    if (tagged[a].Tag != tagged[b].Tag) continue;
                    if (!PolygonDecomposer.TryMergeConvexPolygons(tagged[a].Poly, tagged[b].Poly, out var merged))
                        continue;
                    var simplified = RectangleClipper.RemoveDegenerate(merged);
                    if (simplified.Count < 3) continue;
                    tagged[a] = (simplified, tagged[a].Tag);
                    tagged.RemoveAt(b);
                    changed = true;
                }
            }

            foreach (var (poly, compId) in tagged)
            {
                var pruned = RectangleClipper.RemoveDegenerate(poly);
                if (pruned.Count < 3) continue;
                if (!perComponent.TryGetValue(compId, out var list))
                {
                    list = new List<List<Vec2>>();
                    perComponent[compId] = list;
                }
                list.Add(pruned);

                // Accumulate area-weighted centroid.
                var area = Math.Abs(SignedArea(pruned));
                var c = PolygonCentroid(pruned);
                if (centroidAcc.TryGetValue(compId, out var acc))
                    centroidAcc[compId] = (acc.cx + c.X * area, acc.cy + c.Y * area, acc.area + area);
                else
                    centroidAcc[compId] = (c.X * area, c.Y * area, area);
            }
        }

        // ---------- Pass 4: emit one brush-model entity per component -------
        var nextTargetId = 1;
        var sortedComps = new List<int>(perComponent.Keys);
        sortedComps.Sort();
        foreach (var compId in sortedComps)
        {
            var typeIdNum = componentToType[compId];
            if (!typeById.TryGetValue(typeIdNum, out var type)) continue;
            var pieces2 = perComponent[compId];
            if (pieces2.Count == 0) continue;
            if (!centroidAcc.TryGetValue(compId, out var acc) || acc.area < 1e-9) continue;

            // Brush bottom = local floor low-water-mark. Don't extend below
            // the floor at any cell the component touches: triggers only
            // need to span the player-accessible volume above the floor.
            var minRaw = componentMinRaw[compId];
            var maxRaw = componentMaxRaw[compId];
            var zBottom = zFloorBase + minRaw;
            if (zBottom >= zCeilingTop) continue; // degenerate

            var brushes = new List<Brush>(pieces2.Count);
            foreach (var poly in pieces2)
            {
                brushes.Add(BrushFactory.MakeVerticalPrism(
                    poly, zBottom, zCeilingTop,
                    sideTexture: type.Texture,
                    topTexture: type.Texture,
                    bottomTexture: type.Texture));
            }
            var centerXy = new Vec2(acc.cx / acc.area, acc.cy / acc.area);

            var brushEntity = new MapEntity();
            foreach (var kv in type.EntityProperties)
                brushEntity.Properties[kv.Key] = kv.Value;
            if (!brushEntity.Properties.ContainsKey("classname"))
                brushEntity.Properties["classname"] = "trigger_multiple";
            brushEntity.Brushes.AddRange(brushes);

            // Target origin Z: above the LOCAL floor under the centroid.
            // Sample the heightmap at the centroid cell; fall back to
            // component max raw (highest reachable surface in the
            // component) so it never sits below a step.
            var (centerCx, centerCy) = floorHeightmap.WorldToCell(centerXy);
            ushort floorRawAtCenter;
            if (centerCx >= 0 && centerCy >= 0
                && centerCx < floorHeightmap.Width && centerCy < floorHeightmap.Height)
            {
                floorRawAtCenter = floorHeightmap.Sample(centerCx, centerCy);
            }
            else
            {
                floorRawAtCenter = maxRaw;
            }
            // If the centroid happens to land on an unpainted dip below
            // the rest of the component, prefer the component max so the
            // target spawns above any reachable terrain.
            var floorTopAtCenter = zFloorBase + Math.Max(floorRawAtCenter, maxRaw);
            var targetZ = floorTopAtCenter + 32;

            var targetEntities = new List<MapEntity>();
            foreach (var spec in type.Targets)
            {
                var tname = $"mapslopper_trig_{nextTargetId++}";
                brushEntity.Properties[spec.LinkKey] = tname;
                var pe = new MapEntity();
                foreach (var kv in spec.Properties)
                    pe.Properties[kv.Key] = kv.Value;
                if (!pe.Properties.ContainsKey("classname"))
                    pe.Properties["classname"] = "target_null";
                pe.Properties["targetname"] = tname;
                pe.Properties["origin"] = FormatVec(centerXy.X, centerXy.Y, targetZ);
                targetEntities.Add(pe);
            }

            result.Entities.Add(brushEntity);
            result.Entities.AddRange(targetEntities);
        }

        return result;
    }

    // ---- helpers ------------------------------------------------------------

    private static ushort SampleFloorRaw(Heightmap16 hm, int cx, int cy)
    {
        if (cx < 0 || cy < 0 || cx >= hm.Width || cy >= hm.Height) return 0;
        return hm.Sample(cx, cy);
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

    private static double SignedArea(IReadOnlyList<Vec2> poly)
    {
        double a = 0;
        for (var i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a * 0.5;
    }

    private static Vec2 PolygonCentroid(IReadOnlyList<Vec2> poly)
    {
        var s = SignedArea(poly);
        if (Math.Abs(s) < 1e-12)
        {
            double sx = 0, sy = 0;
            foreach (var v in poly) { sx += v.X; sy += v.Y; }
            return new Vec2(sx / poly.Count, sy / poly.Count);
        }
        double cx = 0, cy = 0;
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            var cross = a.X * b.Y - b.X * a.Y;
            cx += (a.X + b.X) * cross;
            cy += (a.Y + b.Y) * cross;
        }
        var f = 1.0 / (6.0 * s);
        return new Vec2(cx * f, cy * f);
    }

    private static string FormatVec(double x, double y, double z) =>
        string.Format(CultureInfo.InvariantCulture, "{0:0.######} {1:0.######} {2:0.######}", x, y, z);
}

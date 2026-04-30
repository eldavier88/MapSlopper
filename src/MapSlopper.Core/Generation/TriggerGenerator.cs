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
/// Algorithm (mirrors <see cref="FloorCeilingGenerator"/> but works on the
/// trigger ID grid instead of heights):
///
///   1. For every non-zero cell of <see cref="MapSlopper.Core.Project.MapSlopperProject.TriggerLayer"/>
///      whose centre lies inside the polygon, label it with a
///      "(typeId, componentId)" pair using 4-connected flood fill —
///      adjacent same-id cells form one component, diagonally separated
///      same-id paint creates two components.
///
///   2. Convex-decompose the polygon (Hertel-Mehlhorn). For every convex
///      piece P and every cell tagged with a component, clip P against
///      that cell's AABB to produce a small convex polygon tagged with the
///      component label.
///
///   3. Greedily merge adjacent same-component pieces while their union
///      stays convex (<see cref="PolygonDecomposer.TryMergeConvexPolygons"/>).
///      Then strip collinear vertices that accumulated along merged
///      cell-edge boundaries (otherwise q3map2 hits MAX_BUILD_SIDES).
///
///   4. For each component: emit ONE brush-model entity holding every
///      merged convex prism as a brush, full-height (zFloorBase ..
///      zCeilingTop), all sides textured with the trigger texture. Spawn
///      the configured companion point entities at the component's 2D
///      centroid (snapped above the floor). Each target gets an auto
///      <c>targetname</c> like <c>mapslopper_trig_3</c> and the brush
///      entity stores it under the configured <see cref="TriggerTargetSpec.LinkKey"/>.
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
        if (types.Types.Count == 0) return result; // nothing to emit
        if (!ccwPolygon.IsCcw())
            throw new ArgumentException("Polygon must be CCW.", nameof(ccwPolygon));

        // Build a quick lookup id -> type so unknown ids in the layer are skipped.
        var typeById = new Dictionary<byte, TriggerType>();
        foreach (var t in types.Types) typeById[t.Id] = t;

        var w = triggerLayer.Width;
        var h = triggerLayer.Height;
        if (w == 0 || h == 0) return result;

        // ---------- Pass 1: connected-component labelling -------------------
        // Component IDs start at 1; 0 = unlabeled / outside polygon / id=0.
        var componentId = new int[w * h];
        var componentIdToType = new List<byte>(); // index 0 reserved for "no component"
        componentIdToType.Add(0);

        var (oMin, _) = triggerLayer.WorldBounds();
        var cs = triggerLayer.CellSize;

        // Pre-compute "inside polygon at cell centre" so flood fill respects
        // the outline. Cells outside the polygon never become part of a
        // brush so they're treated as boundary.
        var inside = new bool[w * h];
        for (var yy = 0; yy < h; yy++)
        for (var xx = 0; xx < w; xx++)
        {
            var c = new Vec2(oMin.X + (xx + 0.5) * cs, oMin.Y + (yy + 0.5) * cs);
            inside[yy * w + xx] = ccwPolygon.ContainsPoint(c);
        }

        var stack = new Stack<(int X, int Y)>();
        for (var yy = 0; yy < h; yy++)
        for (var xx = 0; xx < w; xx++)
        {
            var idx = yy * w + xx;
            if (componentId[idx] != 0) continue;
            if (!inside[idx]) continue;
            var raw = triggerLayer.Sample(xx, yy);
            if (raw == 0) continue;
            var typeIdNum = (byte)(raw & 0xFF);
            if (!typeById.ContainsKey(typeIdNum)) continue;

            var compId = componentIdToType.Count;
            componentIdToType.Add(typeIdNum);
            stack.Clear();
            stack.Push((xx, yy));
            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Pop();
                if (cx < 0 || cy < 0 || cx >= w || cy >= h) continue;
                var i = cy * w + cx;
                if (componentId[i] != 0) continue;
                if (!inside[i]) continue;
                var v = (byte)(triggerLayer.Sample(cx, cy) & 0xFF);
                if (v != typeIdNum) continue;
                componentId[i] = compId;
                stack.Push((cx + 1, cy));
                stack.Push((cx - 1, cy));
                stack.Push((cx, cy + 1));
                stack.Push((cx, cy - 1));
            }
        }

        if (componentIdToType.Count <= 1) return result; // no painted components

        // ---------- Pass 2: cell-clip polygon, tag with component ----------
        var pieces = PolygonDecomposer.ConvexDecompose(ccwPolygon.Vertices);
        if (pieces.Count == 0)
        {
            result.Warnings.Add("TriggerGenerator: polygon decomposition returned no pieces.");
            return result;
        }

        // For each component, list of (footprint, mergedYet?) gathered across
        // all polygon pieces. Components are independent — they never merge
        // across the type boundary nor across disconnected paint.
        var perComponent = new Dictionary<int, List<List<Vec2>>>();
        foreach (var piece in pieces)
        {
            var (pMin, pMax) = ComputeBounds(piece);
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
                if (!perComponent.TryGetValue(compId, out var list))
                {
                    list = new List<List<Vec2>>();
                    perComponent[compId] = list;
                }
                list.Add(clipped);
            }
        }

        // ---------- Pass 3: greedy merge per component ---------------------
        foreach (var kv in perComponent)
        {
            var list = kv.Value;
            var changed = true;
            while (changed)
            {
                changed = false;
                for (var a = 0; a < list.Count && !changed; a++)
                for (var b = a + 1; b < list.Count && !changed; b++)
                {
                    if (!PolygonDecomposer.TryMergeConvexPolygons(list[a], list[b], out var merged))
                        continue;
                    var simplified = RectangleClipper.RemoveDegenerate(merged);
                    if (simplified.Count < 3) continue;
                    list[a] = simplified;
                    list.RemoveAt(b);
                    changed = true;
                }
            }
        }

        // ---------- Pass 4: emit entities ---------------------------------
        var nextTargetId = 1;
        var sortedComps = new List<int>(perComponent.Keys);
        sortedComps.Sort();
        foreach (var compId in sortedComps)
        {
            var typeIdNum = componentIdToType[compId];
            if (!typeById.TryGetValue(typeIdNum, out var type)) continue;
            var pieces2 = perComponent[compId];
            if (pieces2.Count == 0) continue;

            // Collect brushes and compute centroid (area-weighted) for target placement.
            var brushes = new List<Brush>(pieces2.Count);
            double cxAcc = 0, cyAcc = 0, areaAcc = 0;
            foreach (var poly in pieces2)
            {
                var prism = BrushFactory.MakeVerticalPrism(
                    poly, zFloorBase, zCeilingTop,
                    sideTexture: type.Texture,
                    topTexture: type.Texture,
                    bottomTexture: type.Texture);
                brushes.Add(prism);

                var area = Math.Abs(SignedArea(poly));
                var c = PolygonCentroid(poly);
                cxAcc += c.X * area;
                cyAcc += c.Y * area;
                areaAcc += area;
            }
            if (areaAcc < 1e-9) continue;
            var centerXy = new Vec2(cxAcc / areaAcc, cyAcc / areaAcc);

            // Build the brush entity with configured properties + auto target keys.
            var brushEntity = new MapEntity();
            foreach (var kv in type.EntityProperties)
                brushEntity.Properties[kv.Key] = kv.Value;
            // Ensure classname is set even if config forgot it.
            if (!brushEntity.Properties.ContainsKey("classname"))
                brushEntity.Properties["classname"] = "trigger_multiple";
            brushEntity.Brushes.AddRange(brushes);

            // Spawn each target as a sibling point entity. Origin: a bit
            // above the floor at the bmodel centroid, so it sits at a
            // sensible height for in-game effects.
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
                pe.Properties["origin"] = FormatVec(centerXy.X, centerXy.Y, zFloorBase + 32);
                targetEntities.Add(pe);
            }

            result.Entities.Add(brushEntity);
            result.Entities.AddRange(targetEntities);
        }

        return result;
    }

    // ---- helpers ------------------------------------------------------------

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

using System;
using System.Collections.Generic;
using System.Globalization;
using MapSlopper.Core.Brushes;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;

namespace MapSlopper.Core.Generation;

/// <summary>
/// Places point entities (info_player_start and lights) inside the polygon.
///
/// LIGHTING — three layered passes so non-convex maps stay well-lit:
///
///   1. <b>Grid pass</b>: starting lights on a regular grid spaced by
///      <see cref="MapSlopper.Core.Project.MapSlopperProject.LightSpacing"/>,
///      inset just below the ceiling. Only kept when XY lies inside the
///      polygon and at least <c>WallMargin</c> away from any wall.
///
///   2. <b>Line-of-sight fill</b>: light doesn't bend around corners. After
///      pass 1, sample the polygon interior on a finer grid (lightSpacing/2)
///      and, for every sample that has NO line-of-sight to any existing
///      light within reach, place a new ceiling-height light there. This
///      guarantees every interior cell can "see" at least one light, so no
///      tight corner of an L/U/maze layout is unlit.
///
///   3. <b>Low-floor staggered fill</b>: the ceiling is flat across the
///      whole map but floor heights vary; cells whose local floor sits well
///      below the highest painted floor end up far from the ceiling lights
///      and read as dark pits. For each such region we add an extra
///      mid-height light, on a staggered coarse grid, so the floor stays
///      roughly evenly lit throughout the level.
/// </summary>
public static class EntityPlacement
{
    public static IReadOnlyList<MapEntity> Generate(
        Polygon2D ccwPolygon,
        Heightmap16 heightmap,
        double zFloorBase,
        double zCeilingBottom,
        Vec2? playerStartOverride,
        Vec3? playerStartOverride3,
        double lightSpacing,
        double lightIntensity,
        double lightInsetFromCeiling)
    {
        var entities = new List<MapEntity>();

        // ---- info_player_start ----
        Vec2 startXy;
        ushort startRaw = 0;
        if (playerStartOverride is not null)
        {
            startXy = playerStartOverride.Value;
            var (ocx, ocy) = heightmap.WorldToCell(startXy);
            startRaw = heightmap.Sample(ocx, ocy);
        }
        else
        {
            startXy = ccwPolygon.Centroid();
            if (!ccwPolygon.ContainsPoint(startXy)) startXy = ccwPolygon.Vertices[0];
            var (pMin, pMax) = ccwPolygon.Bounds();
            var (oMin, _) = heightmap.WorldBounds();
            var cs = heightmap.CellSize;
            var cx0 = Math.Max(0, (int)Math.Floor((pMin.X - oMin.X) / cs));
            var cy0 = Math.Max(0, (int)Math.Floor((pMin.Y - oMin.Y) / cs));
            var cx1 = Math.Min(heightmap.Width - 1, (int)Math.Floor((pMax.X - oMin.X) / cs));
            var cy1 = Math.Min(heightmap.Height - 1, (int)Math.Floor((pMax.Y - oMin.Y) / cs));
            for (var yy = cy0; yy <= cy1; yy++)
            for (var xx = cx0; xx <= cx1; xx++)
            {
                var raw = heightmap.Sample(xx, yy);
                if (raw <= startRaw) continue;
                var c = new Vec2(oMin.X + (xx + 0.5) * cs, oMin.Y + (yy + 0.5) * cs);
                if (!ccwPolygon.ContainsPoint(c)) continue;
                startRaw = raw;
                startXy = c;
            }
        }
        var startZ = playerStartOverride3?.Z ?? (zFloorBase + startRaw + 24);
        var player = new MapEntity();
        player.Properties["classname"] = "info_player_start";
        player.Properties["origin"] = FormatVec(startXy.X, startXy.Y, startZ);
        player.Properties["angle"] = "0";
        entities.Add(player);

        // ---- LIGHTS ----
        if (lightIntensity <= 0) return entities;

        var ceilingZ = zCeilingBottom - lightInsetFromCeiling;
        var lightPositions = new List<Vec3>();

        var edges = BuildEdges(ccwPolygon);
        const double WallMargin = 24.0;

        // ===== PASS 1: REGULAR GRID =====
        if (lightSpacing > 0)
        {
            var (pMin, pMax) = ccwPolygon.Bounds();
            var x0 = Math.Floor(pMin.X / lightSpacing) * lightSpacing;
            var y0 = Math.Floor(pMin.Y / lightSpacing) * lightSpacing;
            for (var y = y0; y <= pMax.Y; y += lightSpacing)
            for (var x = x0; x <= pMax.X; x += lightSpacing)
            {
                var p = new Vec2(x, y);
                if (!ccwPolygon.ContainsPoint(p)) continue;
                if (DistanceToBoundary(p, edges) < WallMargin) continue;
                lightPositions.Add(new Vec3(x, y, ceilingZ));
            }
        }

        // ===== PASS 2: LINE-OF-SIGHT FILL =====
        if (lightSpacing > 0)
        {
            var sampleStep = Math.Max(64.0, lightSpacing * 0.5);
            var reach = lightSpacing;
            var (pMin, pMax) = ccwPolygon.Bounds();
            var sx0 = Math.Floor(pMin.X / sampleStep) * sampleStep;
            var sy0 = Math.Floor(pMin.Y / sampleStep) * sampleStep;
            for (var y = sy0; y <= pMax.Y; y += sampleStep)
            for (var x = sx0; x <= pMax.X; x += sampleStep)
            {
                var s = new Vec2(x, y);
                if (!ccwPolygon.ContainsPoint(s)) continue;
                if (HasLitNeighbor(s, lightPositions, edges, reach)) continue;
                var place = PullInsideFromWalls(s, edges, ccwPolygon, WallMargin);
                lightPositions.Add(new Vec3(place.X, place.Y, ceilingZ));
            }
        }

        // Fallback: at least one light so a tiny polygon < lightSpacing^2
        // isn't pitch-black.
        if (lightPositions.Count == 0)
        {
            var c = ccwPolygon.Centroid();
            if (!ccwPolygon.ContainsPoint(c)) c = ccwPolygon.Vertices[0];
            lightPositions.Add(new Vec3(c.X, c.Y, ceilingZ));
        }

        // ===== PASS 3: LOW-FLOOR STAGGERED FILL =====
        AddLowFloorStaggeredLights(
            ccwPolygon, heightmap, edges,
            zFloorBase, zCeilingBottom, lightSpacing,
            lightPositions);

        foreach (var lp in lightPositions)
        {
            var light = new MapEntity();
            light.Properties["classname"] = "light";
            light.Properties["origin"] = FormatVec(lp.X, lp.Y, lp.Z);
            light.Properties["light"] = lightIntensity.ToString("0.######", CultureInfo.InvariantCulture);
            entities.Add(light);
        }

        return entities;
    }

    // ---- Helpers ----

    private static List<(Vec2 A, Vec2 B)> BuildEdges(Polygon2D poly)
    {
        var n = poly.Vertices.Count;
        var list = new List<(Vec2, Vec2)>(n);
        for (var i = 0; i < n; i++)
            list.Add((poly.Vertices[i], poly.Vertices[(i + 1) % n]));
        return list;
    }

    private static bool HasLitNeighbor(
        Vec2 sample,
        List<Vec3> lights,
        List<(Vec2 A, Vec2 B)> edges,
        double reach)
    {
        var reachSq = reach * reach;
        foreach (var l in lights)
        {
            var dx = l.X - sample.X; var dy = l.Y - sample.Y;
            if (dx * dx + dy * dy > reachSq) continue;
            if (HasClearXyPath(sample, new Vec2(l.X, l.Y), edges)) return true;
        }
        return false;
    }

    /// <summary>
    /// True iff the open segment (a, b) does not properly cross any polygon
    /// edge. Endpoint touches don't count as crossings — a light grazing a
    /// corner is still considered visible. Models 2D "light doesn't bend
    /// around corners".
    /// </summary>
    private static bool HasClearXyPath(Vec2 a, Vec2 b, List<(Vec2 A, Vec2 B)> edges)
    {
        foreach (var (e1, e2) in edges)
            if (SegmentsProperlyIntersect(a, b, e1, e2)) return false;
        return true;
    }

    private static bool SegmentsProperlyIntersect(Vec2 p1, Vec2 p2, Vec2 p3, Vec2 p4)
    {
        var d1 = Cross(p4 - p3, p1 - p3);
        var d2 = Cross(p4 - p3, p2 - p3);
        var d3 = Cross(p2 - p1, p3 - p1);
        var d4 = Cross(p2 - p1, p4 - p1);
        const double Eps = 1e-9;
        return ((d1 > Eps && d2 < -Eps) || (d1 < -Eps && d2 > Eps)) &&
               ((d3 > Eps && d4 < -Eps) || (d3 < -Eps && d4 > Eps));
    }

    private static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

    private static double DistanceToBoundary(Vec2 p, List<(Vec2 A, Vec2 B)> edges)
    {
        var best = double.MaxValue;
        foreach (var (a, b) in edges)
        {
            var d = PointSegmentDistance(p, a, b);
            if (d < best) best = d;
        }
        return best;
    }

    private static double PointSegmentDistance(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        var lenSq = ab.X * ab.X + ab.Y * ab.Y;
        if (lenSq < 1e-12)
        {
            var ddx = p.X - a.X; var ddy = p.Y - a.Y;
            return Math.Sqrt(ddx * ddx + ddy * ddy);
        }
        var t = ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lenSq;
        if (t < 0) t = 0; else if (t > 1) t = 1;
        var cx = a.X + t * ab.X; var cy = a.Y + t * ab.Y;
        var dx = p.X - cx; var dy = p.Y - cy;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// If <paramref name="p"/> is closer than <paramref name="margin"/> to any
    /// polygon edge, push it inward along the inward normal of the nearest
    /// edge so it sits at least <paramref name="margin"/> away. Falls back
    /// to the original point if the push lands outside (e.g. concave corner).
    /// </summary>
    private static Vec2 PullInsideFromWalls(Vec2 p, List<(Vec2 A, Vec2 B)> edges, Polygon2D poly, double margin)
    {
        var d = DistanceToBoundary(p, edges);
        if (d >= margin) return p;
        var bestDist = double.MaxValue;
        var bestNx = 0.0; var bestNy = 0.0;
        foreach (var (a, b) in edges)
        {
            var dist = PointSegmentDistance(p, a, b);
            if (dist >= bestDist) continue;
            var ex = b.X - a.X; var ey = b.Y - a.Y;
            var len = Math.Sqrt(ex * ex + ey * ey);
            if (len < 1e-9) continue;
            bestDist = dist;
            bestNx = -ey / len; // CCW polygon -> inward normal is left perp.
            bestNy =  ex / len;
        }
        if (bestDist == double.MaxValue) return p;
        var pushed = new Vec2(p.X + bestNx * (margin - bestDist + 1.0),
                              p.Y + bestNy * (margin - bestDist + 1.0));
        return poly.ContainsPoint(pushed) ? pushed : p;
    }

    private static void AddLowFloorStaggeredLights(
        Polygon2D poly,
        Heightmap16 hm,
        List<(Vec2 A, Vec2 B)> edges,
        double zFloorBase,
        double zCeilingBottom,
        double lightSpacing,
        List<Vec3> lightPositions)
    {
        if (lightSpacing <= 0) return;

        // Reference floor = highest painted cell inside polygon.
        ushort maxRaw = 0;
        var (pMin, pMax) = poly.Bounds();
        var (oMin, _) = hm.WorldBounds();
        var cs = hm.CellSize;
        var cx0 = Math.Max(0, (int)Math.Floor((pMin.X - oMin.X) / cs));
        var cy0 = Math.Max(0, (int)Math.Floor((pMin.Y - oMin.Y) / cs));
        var cx1 = Math.Min(hm.Width - 1, (int)Math.Floor((pMax.X - oMin.X) / cs));
        var cy1 = Math.Min(hm.Height - 1, (int)Math.Floor((pMax.Y - oMin.Y) / cs));
        for (var y = cy0; y <= cy1; y++)
        for (var x = cx0; x <= cx1; x++)
        {
            var center = new Vec2(oMin.X + (x + 0.5) * cs, oMin.Y + (y + 0.5) * cs);
            if (!poly.ContainsPoint(center)) continue;
            var raw = hm.Sample(x, y);
            if (raw > maxRaw) maxRaw = raw;
        }
        if (maxRaw == 0) return; // flat / unpainted: pass 1+2 already cover it.

        var depthThreshold = Math.Max(96.0, lightSpacing * 0.25);
        var step = Math.Max(96.0, lightSpacing * 0.5);
        var rowIndex = 0;
        for (var sy = pMin.Y; sy <= pMax.Y; sy += step)
        {
            var offset = (rowIndex++ & 1) == 0 ? 0.0 : step * 0.5;
            for (var sx = pMin.X + offset; sx <= pMax.X; sx += step)
            {
                var p = new Vec2(sx, sy);
                if (!poly.ContainsPoint(p)) continue;
                var (ccx, ccy) = hm.WorldToCell(p);
                var raw = hm.Sample(ccx, ccy);
                var localTop = zFloorBase + raw;
                var refTop = zFloorBase + maxRaw;
                if (refTop - localTop < depthThreshold) continue;

                p = PullInsideFromWalls(p, edges, poly, 24.0);
                var midZ = (localTop + zCeilingBottom) * 0.5;
                var p3 = new Vec3(p.X, p.Y, midZ);
                if (HasNearbyLight3(p3, lightPositions, step * 0.75)) continue;
                lightPositions.Add(p3);
            }
        }
    }

    private static bool HasNearbyLight3(Vec3 p, List<Vec3> existing, double radius)
    {
        var rSq = radius * radius;
        foreach (var e in existing)
        {
            var dx = e.X - p.X; var dy = e.Y - p.Y; var dz = e.Z - p.Z;
            if (dx * dx + dy * dy + dz * dz <= rSq) return true;
        }
        return false;
    }

    private static string FormatVec(double x, double y, double z) =>
        string.Format(CultureInfo.InvariantCulture, "{0:0.######} {1:0.######} {2:0.######}", x, y, z);
}

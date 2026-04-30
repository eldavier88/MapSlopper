using System.Collections.Generic;
using System.Globalization;
using MapSlopper.Core.Brushes;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;

namespace MapSlopper.Core.Generation;

/// <summary>
/// Places point entities (info_player_start and lights) inside the polygon.
/// Lights are placed on a regular grid, snapped to <see cref="GeneratorOptions.LightSpacing"/>,
/// inset just below the ceiling, and only kept when the XY point lies inside
/// the polygon. The player start is placed at the polygon centroid (or the
/// override point if specified) at floor height + 1 unit.
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

        // info_player_start: prefer the cell with the HIGHEST paint value
        // whose center lies inside the polygon -- that guarantees the
        // player spawns standing on actual generated floor at the room's
        // peak (rather than the polygon centroid which may sit on the
        // lowest part or even outside a non-convex shape). Falls back to
        // centroid -> first vertex if no interior cell has a paint value.
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
            var cx0 = System.Math.Max(0, (int)System.Math.Floor((pMin.X - oMin.X) / cs));
            var cy0 = System.Math.Max(0, (int)System.Math.Floor((pMin.Y - oMin.Y) / cs));
            var cx1 = System.Math.Min(heightmap.Width - 1, (int)System.Math.Floor((pMax.X - oMin.X) / cs));
            var cy1 = System.Math.Min(heightmap.Height - 1, (int)System.Math.Floor((pMax.Y - oMin.Y) / cs));
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
        // Eye height: stand 24 units above the floor top at the chosen cell.
        var startZ = playerStartOverride3?.Z ?? (zFloorBase + startRaw + 24);
        var player = new MapEntity();
        player.Properties["classname"] = "info_player_start";
        player.Properties["origin"] = FormatVec(startXy.X, startXy.Y, startZ);
        player.Properties["angle"] = "0";
        entities.Add(player);

        // light grid
        var lightZ = zCeilingBottom - lightInsetFromCeiling;
        var lightCount = 0;
        if (lightSpacing > 0)
        {
            var (pMin, pMax) = ccwPolygon.Bounds();
            // Snap grid origin to nearest spacing multiple so different polygons share grids.
            var x0 = System.Math.Floor(pMin.X / lightSpacing) * lightSpacing;
            var y0 = System.Math.Floor(pMin.Y / lightSpacing) * lightSpacing;
            for (var y = y0; y <= pMax.Y; y += lightSpacing)
            for (var x = x0; x <= pMax.X; x += lightSpacing)
            {
                var p = new Vec2(x, y);
                if (!ccwPolygon.ContainsPoint(p)) continue;
                var light = new MapEntity();
                light.Properties["classname"] = "light";
                light.Properties["origin"] = FormatVec(x, y, lightZ);
                light.Properties["light"] = lightIntensity.ToString("0.######", CultureInfo.InvariantCulture);
                entities.Add(light);
                lightCount++;
            }
        }

        // Fallback: if the grid produced zero lights (e.g. polygon is smaller
        // than the spacing) emit a single centroid light so the room is never
        // pitch-black after q3map2 -light.
        if (lightCount == 0 && lightIntensity > 0)
        {
            var c = ccwPolygon.Centroid();
            if (!ccwPolygon.ContainsPoint(c)) c = ccwPolygon.Vertices[0];
            var fallback = new MapEntity();
            fallback.Properties["classname"] = "light";
            fallback.Properties["origin"] = FormatVec(c.X, c.Y, lightZ);
            fallback.Properties["light"] = lightIntensity.ToString("0.######", CultureInfo.InvariantCulture);
            entities.Add(fallback);
        }

        return entities;
    }

    private static string FormatVec(double x, double y, double z) =>
        string.Format(CultureInfo.InvariantCulture, "{0:0.######} {1:0.######} {2:0.######}", x, y, z);
}

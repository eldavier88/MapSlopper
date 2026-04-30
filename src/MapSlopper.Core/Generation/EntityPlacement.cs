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

        // info_player_start
        var startXy = playerStartOverride ?? ccwPolygon.Centroid();
        var (cx, cy) = heightmap.WorldToCell(startXy);
        var startZ = playerStartOverride3?.Z ?? (zFloorBase + heightmap.Sample(cx, cy) + 1);
        var player = new MapEntity();
        player.Properties["classname"] = "info_player_start";
        player.Properties["origin"] = FormatVec(startXy.X, startXy.Y, startZ);
        player.Properties["angle"] = "0";
        entities.Add(player);

        // light grid
        if (lightSpacing > 0)
        {
            var (pMin, pMax) = ccwPolygon.Bounds();
            var lightZ = zCeilingBottom - lightInsetFromCeiling;
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
            }
        }

        return entities;
    }

    private static string FormatVec(double x, double y, double z) =>
        string.Format(CultureInfo.InvariantCulture, "{0:0.######} {1:0.######} {2:0.######}", x, y, z);
}

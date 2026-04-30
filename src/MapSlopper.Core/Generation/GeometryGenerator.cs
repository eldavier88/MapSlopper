using System;
using System.Collections.Generic;
using MapSlopper.Core.Brushes;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Project;

namespace MapSlopper.Core.Generation;

/// <summary>
/// Composes the full <see cref="MapDocument"/> from a <see cref="MapSlopperProject"/>:
/// closes the outline polygon, generates floor/ceiling/wall brushes, and emits
/// info_player_start + a light grid into worldspawn-adjacent entities.
/// </summary>
public static class GeometryGenerator
{
    public sealed class Issue
    {
        public string Message { get; }
        public bool IsFatal { get; }
        public Issue(string message, bool isFatal) { Message = message; IsFatal = isFatal; }
        public override string ToString() => (IsFatal ? "ERROR: " : "warning: ") + Message;
    }

    public sealed class Result
    {
        public MapDocument? Document { get; set; }
        public List<Issue> Issues { get; } = new();
        public bool Ok => Document != null && !Issues.Exists(i => i.IsFatal);
    }

    public static Result Generate(MapSlopperProject project)
    {
        var result = new Result();
        if (project is null) throw new ArgumentNullException(nameof(project));

        if (!project.Outline.TryGetClosedPolygon(out var poly) || poly is null)
        {
            result.Issues.Add(new Issue(
                "Outline is not a closed simple polygon (every point must have degree 2 and edges must not cross).",
                true));
            return result;
        }

        var doc = new MapDocument();
        var ws = doc.Worldspawn;

        // Floor & ceiling.
        // CeilingHeight is interpreted as the floor-to-ceiling CLEARANCE: the
        // absolute ceiling Z is automatically placed `CeilingHeight` units above
        // the highest painted floor cell that the polygon covers. This keeps
        // the room interior usable regardless of the paint values used and
        // matches the user's mental model of "the ceiling sits N units above
        // the highest floor".
        var floorBase = -project.FloorBaseThickness;
        var maxFloorTop = ScanMaxFloorTopInsidePolygon(poly, project.Heightmap, floorBase);
        // Fall back to floorBase when nothing was painted so we still emit walls/ceiling.
        if (maxFloorTop < floorBase) maxFloorTop = floorBase;
        var ceilingBottom = maxFloorTop + project.CeilingHeight;
        var floorCeil = FloorCeilingGenerator.Generate(
            poly, project.Heightmap,
            zFloorBase: floorBase,
            zCeilingBottom: ceilingBottom,
            ceilingThickness: project.CeilingThickness,
            floorTexture: project.FloorTexture,
            wallTexture: project.WallTexture,
            ceilingTexture: project.CeilingTexture);
        ws.Brushes.AddRange(floorCeil.FloorBrushes);
        ws.Brushes.AddRange(floorCeil.CeilingBrushes);
        foreach (var w in floorCeil.Warnings)
            result.Issues.Add(new Issue(w, false));

        // Wall ring spans from floor base to ceiling top so floor & ceiling tuck snugly between walls.
        var wallTop = ceilingBottom + project.CeilingThickness;
        var walls = WallGenerator.Generate(
            poly, project.WallThickness, floorBase, wallTop,
            sideTexture: project.WallTexture,
            capTexture: project.WallTexture);
        ws.Brushes.AddRange(walls);

        if (ws.Brushes.Count == 0)
            result.Issues.Add(new Issue("No brushes were generated; the heightmap may be entirely zero or outside the polygon.", false));

        // Entities.
        Vec2? startXy = project.PlayerStartOverride is { } v
            ? new Vec2(v.X, v.Y)
            : null;
        var entities = EntityPlacement.Generate(
            poly, project.Heightmap,
            zFloorBase: floorBase,
            zCeilingBottom: ceilingBottom,
            playerStartOverride: startXy,
            playerStartOverride3: project.PlayerStartOverride,
            lightSpacing: project.LightSpacing,
            lightIntensity: project.LightIntensity,
            lightInsetFromCeiling: project.LightInsetFromCeiling);
        doc.Entities.AddRange(entities);

        result.Document = doc;
        return result;
    }

    /// <summary>
    /// Returns the highest absolute floor-top Z over heightmap cells whose
    /// center lies inside the polygon. Returns <paramref name="zFloorBase"/>
    /// when no such cell has a non-zero paint value.
    /// </summary>
    private static double ScanMaxFloorTopInsidePolygon(
        Polygon2D poly,
        MapSlopper.Core.Heightmap.Heightmap16 hm,
        double zFloorBase)
    {
        var (pMin, pMax) = poly.Bounds();
        var (oMin, _) = hm.WorldBounds();
        var cs = hm.CellSize;
        var cx0 = Math.Max(0, (int)Math.Floor((pMin.X - oMin.X) / cs));
        var cy0 = Math.Max(0, (int)Math.Floor((pMin.Y - oMin.Y) / cs));
        var cx1 = Math.Min(hm.Width - 1, (int)Math.Floor((pMax.X - oMin.X) / cs));
        var cy1 = Math.Min(hm.Height - 1, (int)Math.Floor((pMax.Y - oMin.Y) / cs));
        ushort maxRaw = 0;
        for (var cy = cy0; cy <= cy1; cy++)
        for (var cx = cx0; cx <= cx1; cx++)
        {
            var raw = hm.Sample(cx, cy);
            if (raw <= maxRaw) continue;
            // Only count cells whose center actually lies inside the polygon.
            var center = new Vec2(
                oMin.X + (cx + 0.5) * cs,
                oMin.Y + (cy + 0.5) * cs);
            if (poly.ContainsPoint(center)) maxRaw = raw;
        }
        return zFloorBase + maxRaw;
    }
}

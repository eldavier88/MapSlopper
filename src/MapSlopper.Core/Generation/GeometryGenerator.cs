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
        var floorBase = -project.FloorBaseThickness;
        var ceilingBottom = project.CeilingHeight;
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
}

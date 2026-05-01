using System;
using System.Collections.Generic;
using MapSlopper.Core.Brushes;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Project;
using MapSlopper.Core.Triggers;

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

        // Wall ring bottoms: drop each wall down only as far as the
        // adjacent floor requires (with a small overlap so the floor brush
        // and wall brush share volume -> q3map2 stays leak-free). Scan a
        // few cells just inside the polygon edge; take the MIN height of
        // those cells (lowest neighbouring floor). Wall bottom z =
        // zFloorBase + minHeight - overlap (clamped to >= zFloorBase).
        //
        // Wall SPLIT z (window strip) uses the MAX neighbouring height
        // along the same inner-edge samples. The split must err HIGH —
        // straddling cells of e.g. raw 100 and raw 200 should split above
        // 200 — so the upper window strip is always above any visible
        // floor surface that the wall borders. Split z =
        // zFloorBase + maxNeighbourRaw + splitHeight.
        var wallTop = ceilingBottom + project.CeilingThickness;
        var splitHeight = project.WallSplitHeight ?? project.CeilingHeight;
        (double Bottom, double SplitZ) WallVerticalsFor(int edgeIndex)
        {
            var n = poly.Count;
            var a = poly[edgeIndex];
            var b = poly[(edgeIndex + 1) % n];
            var dir = (b - a).Normalized;
            var inward = new Vec2(-dir.Y, dir.X); // CCW inward = left-perp
            var samples = 8;
            ushort minRaw = ushort.MaxValue;
            ushort maxRaw = 0;
            var found = false;
            for (var s = 0; s <= samples; s++)
            {
                var t = (s + 0.5) / (samples + 1);
                var px = a.X + (b.X - a.X) * t + inward.X * (project.Heightmap.CellSize * 0.5);
                var py = a.Y + (b.Y - a.Y) * t + inward.Y * (project.Heightmap.CellSize * 0.5);
                var (cx, cy) = project.Heightmap.WorldToCell(new Vec2(px, py));
                if (cx < 0 || cy < 0 || cx >= project.Heightmap.Width || cy >= project.Heightmap.Height) continue;
                if (!poly.ContainsPoint(new Vec2(px, py))) continue;
                var raw = project.Heightmap.Sample(cx, cy);
                if (raw < minRaw) { minRaw = raw; found = true; }
                if (raw > maxRaw) maxRaw = raw;
            }
            if (!found)
                return (floorBase, floorBase + splitHeight);
            var bottom = floorBase;
            var splitZ = floorBase + maxRaw + splitHeight;
            return (bottom, splitZ);
        }
        // Cache per-edge to avoid scanning twice per edge.
        var wallVerticals = new (double Bottom, double SplitZ)[poly.Count];
        for (var i = 0; i < poly.Count; i++) wallVerticals[i] = WallVerticalsFor(i);
        var walls = WallGenerator.Generate(
            poly, project.WallThickness,
            zBottomForEdge: i => wallVerticals[i].Bottom,
            zTop: wallTop,
            sideTexture: project.WallTexture,
            capTexture: project.WallTexture,
            splitZForEdge: i => wallVerticals[i].SplitZ,
            upperSideTexture: project.WindowTexture);
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

        // Trigger brush-model entities (one per painted same-id connected
        // component) plus their auto-spawned point-entity targets. Triggers
        // span the FULL room height (from floor base to top of ceiling
        // slab) so the player triggers them regardless of vertical
        // position.
        var triggerTypes = ResolveTriggerTypes(project);
        if (triggerTypes.Types.Count > 0 && project.TriggerLayer.Width > 0 && project.TriggerLayer.Height > 0)
        {
            var triggers = TriggerGenerator.Generate(
                poly, project.TriggerLayer, project.Heightmap, triggerTypes,
                zFloorBase: floorBase,
                zCeilingTop: wallTop);
            doc.Entities.AddRange(triggers.Entities);
            foreach (var w in triggers.Warnings)
                result.Issues.Add(new Issue(w, false));
        }

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

    /// <summary>
    /// Resolve the effective trigger types config for a project: program-wide
    /// defaults (loaded from <c>assets/triggers.json</c> next to the
    /// executable, falling back to the built-in three-color preset)
    /// merged with the project's own <see cref="MapSlopperProject.TriggerOverrides"/>
    /// (per-Id replacement; missing program-wide entries remain available).
    /// </summary>
    public static TriggerTypeConfig ResolveTriggerTypes(MapSlopperProject project)
    {
        var baseConfig = LoadProgramWideTriggerConfig();
        return TriggerTypeConfig.MergeOverrides(baseConfig, project.TriggerOverrides);
    }

    private static TriggerTypeConfig? s_programWideTriggerConfig;
    private static TriggerTypeConfig LoadProgramWideTriggerConfig()
    {
        if (s_programWideTriggerConfig is not null) return s_programWideTriggerConfig;
        var candidates = new[]
        {
            System.IO.Path.Combine(System.AppContext.BaseDirectory, "assets", "triggers.json"),
            System.IO.Path.Combine(System.AppContext.BaseDirectory, "triggers.json"),
        };
        foreach (var path in candidates)
        {
            if (System.IO.File.Exists(path))
            {
                try
                {
                    s_programWideTriggerConfig = TriggerTypeConfigJson.Load(path);
                    return s_programWideTriggerConfig;
                }
                catch { /* ignore, fall back to built-in */ }
            }
        }
        s_programWideTriggerConfig = TriggerTypeConfig.BuiltInDefault();
        return s_programWideTriggerConfig;
    }
}

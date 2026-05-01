using System;
using System.Collections.Generic;
using System.Linq;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;
using MapSlopper.Core.Project;

namespace MapSlopper.Core.Generation;

/// <summary>
/// A high-end procedural map generator that creates complex, branching maps
/// (like dungeons, caves, or arenas) while adhering to MapSlopper's constraint
/// of a single, non-intersecting closed polygon.
/// </summary>
public static class AdvancedAutoMapGenerator
{
    public enum MapStyle { Cave, TechCorridors, Arena }

    public sealed class Options
    {
        public int GridWidth { get; set; } = 48;
        public int GridHeight { get; set; } = 48;
        public double CellSize { get; set; } = 64.0;
        public MapStyle Style { get; set; } = MapStyle.Cave;
        public ushort BaseRelief { get; set; } = 256;
        public int? Seed { get; set; }
    }

    public static AutoMapGenerator.Result Generate(MapSlopperProject template, Options? options = null)
    {
        if (template is null) throw new ArgumentNullException(nameof(template));
        options ??= new Options();

        var width = Math.Clamp(options.GridWidth, 16, 128);
        var height = Math.Clamp(options.GridHeight, 16, 128);
        var cellSize = Math.Max(16.0, options.CellSize);
        var baseSeed = options.Seed ?? Environment.TickCount;

        for (var attempt = 0; attempt < 24; attempt++)
        {
            var seed = unchecked(baseSeed + attempt * 1013);
            var rng = new Random(seed);
            
            // 1. Generate the grid-based layout
            var grid = new bool[width, height];
            CarveLayout(grid, width, height, options.Style, rng);

            // 2. Trace the active cells to form an orthogonal exterior polygon
            var poly = TraceHull(grid, width, height, cellSize);
            if (poly.Count < 4 || !poly.IsSimple()) continue;

            // 3. Jitter/Smooth the polygon based on the style
            if (options.Style == MapStyle.Cave)
            {
                poly = SmoothPolygon(poly, cellSize * 0.4, rng);
            }
            if (!poly.IsSimple()) continue; // Throw out if smoothing caused intersections

            var p = CloneTemplateScalars(template);
            p.Heightmap = new Heightmap16(width, height, cellSize, Vec2.Zero);
            p.TriggerLayer = new Heightmap16(width, height, cellSize, Vec2.Zero);
            p.Outline.Clear();

            var ids = new Guid[poly.Count];
            for (var i = 0; i < ids.Length; i++) ids[i] = Guid.NewGuid();
            for (var i = 0; i < poly.Count; i++) p.Outline.AddPoint(ids[i], poly[i]);
            for (var i = 0; i < poly.Count; i++) p.Outline.AddEdge(ids[i], ids[(i + 1) % ids.Length]);

            // 4. Paint the heightmap and triggers
            PaintHeightmap(p.Heightmap, grid, poly, options.BaseRelief, options.Style, rng);
            PaintTriggerPatches(p, grid, rng);

            var validate = GeometryGenerator.Generate(p);
            if (!validate.Ok) continue;

            return new AutoMapGenerator.Result { Project = p, SeedUsed = seed, Attempts = attempt + 1 };
        }

        throw new InvalidOperationException("Advanced automatic map generation failed after multiple valid-shape attempts.");
    }

    private static MapSlopperProject CloneTemplateScalars(MapSlopperProject t)
    {
        return new MapSlopperProject
        {
            FormatVersion = t.FormatVersion,
            CeilingHeight = t.CeilingHeight,
            WallThickness = t.WallThickness,
            FloorTexture = t.FloorTexture,
            WallTexture = t.WallTexture,
            CeilingTexture = t.CeilingTexture,
            WindowTexture = t.WindowTexture,
            WallSplitHeight = t.WallSplitHeight,
            PlayerStartOverride = null,
            LightSpacing = t.LightSpacing,
            LightIntensity = t.LightIntensity,
            LightInsetFromCeiling = t.LightInsetFromCeiling,
            CeilingThickness = t.CeilingThickness,
            FloorBaseThickness = t.FloorBaseThickness,
            TriggerOverrides = t.TriggerOverrides,
            AssetRoots = new List<string>(t.AssetRoots),
        };
    }

    private static void CarveLayout(bool[,] grid, int w, int h, MapStyle style, Random rng)
    {
        int cx = w / 2, cy = h / 2;
        grid[cx, cy] = true;

        int targetCells = (w * h) / (style == MapStyle.Arena ? 3 : 5);
        int cells = 1;

        // Drunkard's walk with room bursting
        int x = cx, y = cy;
        while (cells < targetCells)
        {
            var dir = rng.Next(4);
            if (dir == 0 && x < w - 2) x++;
            else if (dir == 1 && x > 1) x--;
            else if (dir == 2 && y < h - 2) y++;
            else if (dir == 3 && y > 1) y--;

            if (!grid[x, y])
            {
                grid[x, y] = true;
                cells++;
            }

            // Occasionally carve a room
            if (rng.NextDouble() < 0.05)
            {
                int rw = rng.Next(2, 5), rh = rng.Next(2, 5);
                for (int ry = Math.Max(1, y - rh); ry <= Math.Min(h - 2, y + rh); ry++)
                for (int rx = Math.Max(1, x - rw); rx <= Math.Min(w - 2, x + rw); rx++)
                {
                    if (style == MapStyle.Arena || rng.NextDouble() < 0.8)
                    {
                        if (!grid[rx, ry])
                        {
                            grid[rx, ry] = true;
                            cells++;
                        }
                    }
                }
            }
        }
        
        // Ensure no unreachable isolated blocks or holes on the immediate boundary to simplify tracing
        for (int i = 0; i < w; i++) { grid[i, 0] = false; grid[i, h - 1] = false; }
        for (int i = 0; i < h; i++) { grid[0, i] = false; grid[w - 1, i] = false; }
    }

    private static Polygon2D TraceHull(bool[,] grid, int w, int h, double cellSize)
    {
        // Find starting edge (topmost, leftmost active cell)
        int sx = -1, sy = -1;
        for (int y = 0; y < h && sx == -1; y++)
        for (int x = 0; x < w && sx == -1; x++)
        {
            if (grid[x, y]) { sx = x; sy = y; }
        }
        if (sx == -1) return new Polygon2D(new List<Vec2>()); // Empty

        // Trace orthogonal outline using right-hand wall follower
        var pts = new List<Vec2>();
        int curX = sx, curY = sy;
        int dx = 1, dy = 0; // Start moving right along the top edge
        
        int startX = curX, startY = curY, startDx = dx, startDy = dy;
        do
        {
            // Add vertex for current corner.
            // When facing Right (dx=1, dy=0), we are at top-left corner of the cell moving to top-right.
            // We use cell coordinates where (X,Y) is the top-left corner of the cell.
            double px = curX, py = curY;
            if (dx == 1 && dy == 0) { px = curX; py = curY; }
            else if (dx == 0 && dy == 1) { px = curX + 1; py = curY; }
            else if (dx == -1 && dy == 0) { px = curX + 1; py = curY + 1; }
            else if (dx == 0 && dy == -1) { px = curX; py = curY + 1; }
            
            pts.Add(new Vec2(px * cellSize, py * cellSize));

            // Determine next direction
            // Left turn
            int lx = curX + dx + dy - 1, ly = curY + dy - dx; // Note: simplified logic for grid tracing
            
            // To properly trace, we check the cell relative to our heading
            int leftCellX = curX + (dx == 0 ? dy - 1 : (dx == 1 ? 0 : -1));
            int leftCellY = curY + (dy == 0 ? -dx - 1 : (dy == 1 ? 0 : -1));
            int frontCellX = curX + (dx == 0 ? dy : (dx == 1 ? 1 : -1));
            int frontCellY = curY + (dy == 0 ? -dx : (dy == 1 ? 1 : -1));

            // Standard Moore neighborhood tracing logic
            int nextDx = dx, nextDy = dy;
            bool turnLeft = IsActive(grid, w, h, curX + (dx == 0 ? dy : 0) - (dx == -1 ? 1 : 0), 
                                                 curY + (dy == 0 ? -dx : 0) - (dy == -1 ? 1 : 0));
            // Actual right-hand wall following implementation requires edge-based stepping.
            // For simplicity in this generator, we'll use a robust marching squares approach instead.
            
            // Since pure edge tracing is tricky to write perfectly in a short snippet without edge states, 
            // we'll rely on a simplified approach:
        } while (false); // Placeholder for actual complex tracing 

        // Let's implement a clean Marching Squares contour tracer
        return TraceMarchingSquares(grid, w, h, cellSize);
    }

    private static bool IsActive(bool[,] grid, int w, int h, int x, int y) =>
        x >= 0 && y >= 0 && x < w && y < h && grid[x, y];

    private static Polygon2D TraceMarchingSquares(bool[,] grid, int w, int h, double cellSize)
    {
        var edges = new HashSet<(int x1, int y1, int x2, int y2)>();
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (!grid[x, y]) continue;
            // Top edge
            if (!IsActive(grid, w, h, x, y - 1)) edges.Add((x, y, x + 1, y));
            // Bottom edge
            if (!IsActive(grid, w, h, x, y + 1)) edges.Add((x + 1, y + 1, x, y + 1));
            // Left edge
            if (!IsActive(grid, w, h, x - 1, y)) edges.Add((x, y + 1, x, y));
            // Right edge
            if (!IsActive(grid, w, h, x + 1, y)) edges.Add((x + 1, y, x + 1, y + 1));
        }

        if (edges.Count == 0) return new Polygon2D(new List<Vec2>());

        var adj = new Dictionary<(int x, int y), (int x, int y)>();
        foreach (var e in edges)
        {
            adj[(e.x1, e.y1)] = (e.x2, e.y2);
        }

        var start = adj.Keys.First();
        var pts = new List<Vec2>();
        var curr = start;
        do
        {
            pts.Add(new Vec2(curr.x * cellSize, curr.y * cellSize));
            curr = adj[curr];
        } while (curr != start && pts.Count < edges.Count + 1);

        // Simplify collinear points
        var simplified = new List<Vec2>();
        for (int i = 0; i < pts.Count; i++)
        {
            var prev = pts[(i - 1 + pts.Count) % pts.Count];
            var currPt = pts[i];
            var next = pts[(i + 1) % pts.Count];

            var dir1 = (currPt - prev).Normalized;
            var dir2 = (next - currPt).Normalized;
            
            if (Vec2.Dot(dir1, dir2) < 0.999)
                simplified.Add(currPt);
        }

        return new Polygon2D(simplified).ToCcw();
    }

    private static Polygon2D SmoothPolygon(Polygon2D poly, double jitterAmt, Random rng)
    {
        var pts = new List<Vec2>();
        for (int i = 0; i < poly.Count; i++)
        {
            var curr = poly[i];
            var next = poly[(i + 1) % poly.Count];
            pts.Add(curr);
            
            // Subdivide long edges
            int divisions = (int)(Vec2.Distance(curr, next) / (jitterAmt * 3));
            for (int j = 1; j < divisions; j++)
            {
                double t = (double)j / divisions;
                var mid = curr + (next - curr) * t;
                var offsetX = (rng.NextDouble() - 0.5) * jitterAmt;
                var offsetY = (rng.NextDouble() - 0.5) * jitterAmt;
                pts.Add(new Vec2(mid.X + offsetX, mid.Y + offsetY));
            }
        }
        return new Polygon2D(pts).ToCcw();
    }

    private static void PaintHeightmap(Heightmap16 hm, bool[,] grid, Polygon2D poly, ushort baseRelief, MapStyle style, Random rng)
    {
        double freq = style == MapStyle.Cave ? 0.05 : 0.02;
        double phaseA = rng.NextDouble() * 1000.0;
        
        for (var y = 0; y < hm.Height; y++)
        for (var x = 0; x < hm.Width; x++)
        {
            var wp = new Vec2(
                hm.Origin.X + (x + 0.5) * hm.CellSize,
                hm.Origin.Y + (y + 0.5) * hm.CellSize);
                
            if (!poly.ContainsPoint(wp))
            {
                hm.Data[y * hm.Width + x] = 0;
                continue;
            }

            // Base noise
            double n = Math.Sin(wp.X * freq + phaseA) * Math.Cos(wp.Y * freq) * 0.5 + 0.5;
            
            int targetHeight = baseRelief;
            if (style == MapStyle.Cave)
            {
                targetHeight = (int)(baseRelief + (n - 0.5) * 128);
            }
            else if (style == MapStyle.Arena)
            {
                targetHeight = baseRelief + (grid[x, y] && n > 0.7 ? 64 : 0);
            }
            else // TechCorridors
            {
                targetHeight = baseRelief + (rng.NextDouble() > 0.8 ? 16 : 0);
            }

            int q = (int)Math.Round(targetHeight / 8.0) * 8;
            hm.Data[y * hm.Width + x] = (ushort)Math.Clamp(q, 0, ushort.MaxValue);
        }
    }

    private static void PaintTriggerPatches(MapSlopperProject p, bool[,] grid, Random rng)
    {
        var types = GeometryGenerator.ResolveTriggerTypes(p).Types;
        if (types.Count == 0) return;

        var hm = p.TriggerLayer;
        int patches = rng.Next(3, 8);
        for (int i = 0; i < patches; i++)
        {
            int rx = rng.Next(2, hm.Width - 2);
            int ry = rng.Next(2, hm.Height - 2);
            if (!grid[rx, ry]) continue;

            var type = types[rng.Next(types.Count)];
            for (int y = ry; y < Math.Min(hm.Height, ry + 2); y++)
            for (int x = rx; x < Math.Min(hm.Width, rx + 2); x++)
            {
                hm.Data[y * hm.Width + x] = type.Id;
            }
        }
    }
}

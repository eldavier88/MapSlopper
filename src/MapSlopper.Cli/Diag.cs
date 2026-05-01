using System;
using System.Collections.Generic;
using System.IO;
using MapSlopper.Core.Generation;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Project;

namespace MapSlopper.Cli;

internal static class Diag
{
    /// <summary>
    /// Dumps the closed CCW polygon, its convex decomposition pieces, and
    /// (optionally) for a target world (X,Y) reports which decomposition
    /// piece contains it and what the per-cell heightmap clip looks like
    /// in that piece. Used to debug "missing floor brush" leaks.
    /// </summary>
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: diag <project.json> [x y]"); return 64; }
        var project = ProjectJsonIo.Load(args[1]);
        if (!project.Outline.TryGetClosedPolygon(out var poly) || poly is null)
        {
            Console.Error.WriteLine("Polygon not closed/simple.");
            return 1;
        }
        Console.WriteLine($"Polygon: {poly.Count} verts, signedArea={poly.SignedArea():F1}, ccw={poly.IsCcw()}");
        // Compute max raw inside polygon to see what the floor/ceiling generator sees.
        {
            var (oMin2, _) = project.Heightmap.WorldBounds();
            var cs2 = project.Heightmap.CellSize;
            var (pMin, pMax) = poly.Bounds();
            var cx0 = Math.Max(0, (int)Math.Floor((pMin.X - oMin2.X) / cs2));
            var cy0 = Math.Max(0, (int)Math.Floor((pMin.Y - oMin2.Y) / cs2));
            var cx1 = Math.Min(project.Heightmap.Width - 1, (int)Math.Floor((pMax.X - oMin2.X) / cs2));
            var cy1 = Math.Min(project.Heightmap.Height - 1, (int)Math.Floor((pMax.Y - oMin2.Y) / cs2));
            ushort mr = 0;
            for (var cy = cy0; cy <= cy1; cy++)
            for (var cx = cx0; cx <= cx1; cx++)
            {
                var raw = project.Heightmap.Sample(cx, cy);
                if (raw <= mr) continue;
                var c = new Vec2(oMin2.X + (cx + 0.5) * cs2, oMin2.Y + (cy + 0.5) * cs2);
                if (poly.ContainsPoint(c)) mr = raw;
            }
            Console.WriteLine($"Max raw inside polygon: {mr}");
            var fb = -project.FloorBaseThickness;
            var mft = fb + mr;
            var cb = mft + project.CeilingHeight;
            Console.WriteLine($"floorBase={fb} maxFloorTop={mft} ceilingBottom={cb} wallTop={cb + project.CeilingThickness}");
        }
        for (var i = 0; i < poly.Count; i++)
        {
            var v = poly[i];
            Console.WriteLine($"  v{i}: ({v.X}, {v.Y})");
        }
        var pieces = PolygonDecomposer.ConvexDecompose(poly.Vertices);
        Console.WriteLine($"Decomposition: {pieces.Count} pieces");
        for (var i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            Console.Write($"  piece {i}: {p.Count} verts ");
            foreach (var v in p) Console.Write($"({v.X:F0},{v.Y:F0}) ");
            Console.WriteLine();
        }
        if (args.Length >= 4)
        {
            var tx = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
            var ty = double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
            var target = new Vec2(tx, ty);
            Console.WriteLine($"\nProbe: ({tx}, {ty})");
            Console.WriteLine($"  ContainsPoint (full polygon): {poly.ContainsPoint(target)}");
            for (var i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                var pp = new Polygon2D(p);
                var inside = pp.ContainsPoint(target);
                if (inside) Console.WriteLine($"  Inside piece {i}");
            }
            // Heightmap cell at probe
            var (cx, cy) = project.Heightmap.WorldToCell(target);
            Console.WriteLine($"  Cell index: cx={cx}, cy={cy}");
            if (cx >= 0 && cy >= 0 && cx < project.Heightmap.Width && cy < project.Heightmap.Height)
            {
                Console.WriteLine($"  Heightmap raw: {project.Heightmap.Sample(cx, cy)}");
            }
            // Clip the cell AABB against each piece
            var (oMin, _) = project.Heightmap.WorldBounds();
            var cs = project.Heightmap.CellSize;
            var cellMinX = oMin.X + cx * cs;
            var cellMinY = oMin.Y + cy * cs;
            var cellMaxX = cellMinX + cs;
            var cellMaxY = cellMinY + cs;
            Console.WriteLine($"  Cell AABB: [{cellMinX},{cellMaxX}] x [{cellMinY},{cellMaxY}]");
            for (var i = 0; i < pieces.Count; i++)
            {
                var clipped = RectangleClipper.Clip(pieces[i], cellMinX, cellMinY, cellMaxX, cellMaxY);
                clipped = RectangleClipper.RemoveDegenerate(clipped);
                if (clipped.Count >= 3)
                {
                    Console.Write($"  piece {i} clip-with-cell: {clipped.Count} verts ");
                    foreach (var v in clipped) Console.Write($"({v.X:F0},{v.Y:F0}) ");
                    Console.WriteLine();
                }
            }
            // Run the actual floor/ceiling generator and report any floor brush
            // footprint that contains (tx, ty).
            Console.WriteLine("\nFloor brushes covering probe:");
            var fcResult = GeometryGenerator.Generate(project);
            if (fcResult.Document is null)
            {
                Console.WriteLine("  (geometry generation failed)");
            }
            else
            {
                var ws = fcResult.Document.Worldspawn;
                var hits = 0;
                for (var bi = 0; bi < ws.Brushes.Count; bi++)
                {
                    var b = ws.Brushes[bi];
                    // Find the bottom plane of the brush (it has a horizontal face at the lowest Z).
                    // Quick heuristic: gather XY footprint by intersecting top-plane vertices.
                    var fp = TryExtractFootprint(b);
                    if (fp is null) continue;
                    if (PointInPolygon(target, fp))
                    {
                        Console.Write($"  brush {bi}: footprint ");
                        foreach (var v in fp) Console.Write($"({v.X:F0},{v.Y:F0}) ");
                        Console.WriteLine();
                        hits++;
                    }
                }
                Console.WriteLine($"  Total worldspawn brushes covering probe XY: {hits}");
            }
        }
        return 0;
    }

    private static List<Vec2>? TryExtractFootprint(MapSlopper.Core.Brushes.Brush b)
    {
        // For a vertical prism, three planes encode the XY footprint indirectly,
        // but simpler: the top face is the plane with the largest Z and a normal
        // close to +Z. Its three reference points give us 3 of the footprint
        // vertices in the same XY order as the side planes.
        // Easier approach: collect all unique XY pairs across plane reference
        // points. For a vertical prism this gives exactly the footprint.
        var xys = new List<Vec2>();
        foreach (var pl in b.Planes)
        {
            void Add(Vec3 v) {
                foreach (var e in xys) if (Math.Abs(e.X - v.X) < 1e-6 && Math.Abs(e.Y - v.Y) < 1e-6) return;
                xys.Add(new Vec2(v.X, v.Y));
            }
            Add(pl.P1); Add(pl.P2); Add(pl.P3);
        }
        if (xys.Count < 3) return null;
        // The XY points may be unordered; find a CCW hull-like ordering by
        // sorting by angle from centroid (works because our brushes are convex).
        var cx = 0.0; var cy = 0.0;
        foreach (var v in xys) { cx += v.X; cy += v.Y; }
        cx /= xys.Count; cy /= xys.Count;
        xys.Sort((a, c) => Math.Atan2(a.Y - cy, a.X - cx).CompareTo(Math.Atan2(c.Y - cy, c.X - cx)));
        return xys;
    }

    private static bool PointInPolygon(Vec2 p, List<Vec2> poly)
    {
        var inside = false;
        var n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var a = poly[i]; var b = poly[j];
            var yMin = Math.Min(a.Y, b.Y);
            var yMax = Math.Max(a.Y, b.Y);
            if (p.Y < yMin || p.Y >= yMax) continue;
            var t = (p.Y - a.Y) / (b.Y - a.Y);
            var xCross = a.X + t * (b.X - a.X);
            if (p.X < xCross) inside = !inside;
        }
        return inside;
    }
}

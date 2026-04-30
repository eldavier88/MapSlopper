using System;
using System.Collections.Generic;
using Avalonia.Media;
using MapSlopper.Core.Brushes;
using MapSlopper.Core.Geometry;
using CoreBrush = MapSlopper.Core.Brushes.Brush;

namespace MapSlopper.Gui;

/// <summary>
/// Reconstructs the polygon for each plane of a brush by clipping a huge
/// rectangle on the plane against every other plane's half-space. This is the
/// classical brush→face conversion used by every Quake-derived BSP compiler;
/// we only need it here for previewing, so robustness over speed is fine.
/// Colors faces by orientation: ~+Z = floor, ~-Z = ceiling, sides = walls.
/// </summary>
internal static class BrushFaceBuilder
{
    private const double LargeSize = 1.0e5;
    private const double Epsilon = 1.0e-4;

    public static List<(List<Vec3> Verts, Color Color)> BuildFace(CoreBrush brush)
    {
        var result = new List<(List<Vec3>, Color)>();
        if (brush.Planes.Count < 4) return result;
        for (var i = 0; i < brush.Planes.Count; i++)
        {
            var poly = BuildPlanePolygon(brush.Planes[i]);
            for (var j = 0; j < brush.Planes.Count; j++)
            {
                if (i == j) continue;
                poly = ClipByPlane(poly, brush.Planes[j]);
                if (poly.Count == 0) break;
            }
            if (poly.Count >= 3)
            {
                var n = brush.Planes[i].Normal.Normalized;
                Color color;
                if (n.Z > 0.7) color = Color.FromRgb(120, 160, 180);       // floor
                else if (n.Z < -0.7) color = Color.FromRgb(180, 160, 120); // ceiling
                else color = Color.FromRgb(170, 170, 175);                 // wall
                result.Add((poly, color));
            }
        }
        return result;
    }

    private static List<Vec3> BuildPlanePolygon(Plane plane)
    {
        var n = plane.Normal.Normalized;
        // Choose any vector not parallel to n.
        var helper = Math.Abs(n.Z) < 0.9 ? Vec3.UnitZ : Vec3.UnitX;
        var u = Vec3.Cross(n, helper).Normalized;
        var v = Vec3.Cross(n, u).Normalized;
        var center = plane.P1;
        return new List<Vec3>
        {
            center + (u * LargeSize) + (v * LargeSize),
            center - (u * LargeSize) + (v * LargeSize),
            center - (u * LargeSize) - (v * LargeSize),
            center + (u * LargeSize) - (v * LargeSize),
        };
    }

    private static List<Vec3> ClipByPlane(List<Vec3> input, Plane plane)
    {
        // Keep points whose signed distance is <= 0 (interior side, since
        // plane normal points OUTWARD). Sutherland–Hodgman in 3D.
        var output = new List<Vec3>(input.Count);
        if (input.Count == 0) return output;
        var n = plane.Normal;
        var p0 = plane.P1;
        double Dist(Vec3 p) => Vec3.Dot(n, p - p0);

        var prev = input[input.Count - 1];
        var prevDist = Dist(prev);
        foreach (var curr in input)
        {
            var currDist = Dist(curr);
            if (currDist <= Epsilon)
            {
                if (prevDist > Epsilon)
                {
                    var t = prevDist / (prevDist - currDist);
                    output.Add(prev + (curr - prev) * t);
                }
                output.Add(curr);
            }
            else if (prevDist <= Epsilon)
            {
                var t = prevDist / (prevDist - currDist);
                output.Add(prev + (curr - prev) * t);
            }
            prev = curr;
            prevDist = currDist;
        }
        return output;
    }
}

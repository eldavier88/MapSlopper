using System;
using System.Collections.Generic;
using MapSlopper.Core.Brushes;
using MapSlopper.Core.Geometry;
using CoreBrush = MapSlopper.Core.Brushes.Brush;

namespace MapSlopper.Gui;

/// <summary>
/// Reconstructs the polygon for each plane of a brush by clipping a huge
/// rectangle on the plane against every other plane's half-space. This is
/// the classical brush→face conversion used by every Quake-derived BSP
/// compiler; we only need it here for previewing, so robustness over speed
/// is fine.
///
/// Each face carries:
///   * The outline polygon (CCW from outside).
///   * The plane's texture name (used by the asset library for sampling).
///   * Per-vertex Q3 axial UV coordinates (matching what the .map exporter
///     writes by default — see Plane.ScaleS/ScaleT). The standard Q3
///     trick: pick the dominant world axis of the plane normal, project
///     onto the OTHER two axes, divide by texture pixel size × scale.
///   * A fallback Lambertian tint that the rasterizer modulates the
///     texture by (so untextured/missing-asset faces still look correct
///     and lit faces still get directional shading).
/// </summary>
internal static class BrushFaceBuilder
{
    private const double LargeSize = 1.0e5;
    private const double Epsilon = 1.0e-4;
    /// <summary>
    /// Texture-pixel size assumed for axial UV projection. Q3's default
    /// scale 0.5 means "1 texel per 0.5 world units" if the texture is
    /// 64x64, i.e. the texture spans 32 world units. With our typical
    /// 32-unit cell grid and 256-unit ceilings that gives ~1 repetition
    /// per cell on floors and ~8 repetitions on a wall — looks correct.
    /// </summary>
    private const double DefaultTextureWorldSize = 64.0;

    public readonly struct Face
    {
        public readonly List<Vec3> Verts;
        public readonly List<(double U, double V)> Uvs;
        public readonly string TextureName;
        public readonly byte FallbackR, FallbackG, FallbackB;
        public Face(List<Vec3> verts, List<(double U, double V)> uvs, string tex, byte r, byte g, byte b)
        {
            Verts = verts; Uvs = uvs; TextureName = tex;
            FallbackR = r; FallbackG = g; FallbackB = b;
        }
    }

    public static List<Face> BuildFaces(CoreBrush brush)
    {
        var result = new List<Face>();
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
            if (poly.Count < 3) continue;

            var plane = brush.Planes[i];
            var n = plane.Normal.Normalized;
            // Q3 axial projection: pick the dominant axis of |n|.
            var ax = Math.Abs(n.X);
            var ay = Math.Abs(n.Y);
            var az = Math.Abs(n.Z);
            int axis = az >= ax && az >= ay ? 2 : (ay >= ax ? 1 : 0);
            var uvs = new List<(double U, double V)>(poly.Count);
            var sScale = plane.ScaleS == 0 ? 0.5 : plane.ScaleS;
            var tScale = plane.ScaleT == 0 ? 0.5 : plane.ScaleT;
            var periodS = DefaultTextureWorldSize * sScale;
            var periodT = DefaultTextureWorldSize * tScale;
            if (periodS == 0) periodS = DefaultTextureWorldSize;
            if (periodT == 0) periodT = DefaultTextureWorldSize;
            foreach (var v in poly)
            {
                double u, vv;
                switch (axis)
                {
                    case 0: u = v.Y; vv = -v.Z; break;
                    case 1: u = v.X; vv = -v.Z; break;
                    default: u = v.X; vv = -v.Y; break;
                }
                uvs.Add((u / periodS + plane.ShiftS / DefaultTextureWorldSize,
                        vv / periodT + plane.ShiftT / DefaultTextureWorldSize));
            }

            byte r, g, b;
            if (n.Z > 0.7) { r = 120; g = 160; b = 180; }
            else if (n.Z < -0.7) { r = 180; g = 160; b = 120; }
            else { r = 170; g = 170; b = 175; }

            result.Add(new Face(poly, uvs, plane.Texture ?? string.Empty, r, g, b));
        }
        return result;
    }

    private static List<Vec3> BuildPlanePolygon(Plane plane)
    {
        var n = plane.Normal.Normalized;
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

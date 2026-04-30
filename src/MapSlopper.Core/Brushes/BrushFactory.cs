using System;
using MapSlopper.Core.Geometry;

namespace MapSlopper.Core.Brushes;

/// <summary>
/// Helpers for building common convex brush shapes whose plane orientations
/// have been verified to match the Quake 3 .map convention (outward normals).
/// </summary>
public static class BrushFactory
{
    /// <summary>
    /// Build an axis-aligned box brush spanning <paramref name="min"/> to
    /// <paramref name="max"/>. All faces use a single texture unless overridden.
    /// </summary>
    public static Brush MakeAabb(
        Vec3 min, Vec3 max,
        string texture,
        string? topTexture = null,
        string? bottomTexture = null)
    {
        if (min.X >= max.X || min.Y >= max.Y || min.Z >= max.Z)
            throw new ArgumentException(
                $"Degenerate AABB: min={min} max={max}.");

        var tTop = topTexture ?? texture;
        var tBot = bottomTexture ?? texture;

        var b = new Brush();
        // +Z (top)
        b.Planes.Add(new Plane(
            new Vec3(min.X, min.Y, max.Z),
            new Vec3(max.X, min.Y, max.Z),
            new Vec3(max.X, max.Y, max.Z),
            tTop));
        // -Z (bottom)
        b.Planes.Add(new Plane(
            new Vec3(min.X, min.Y, min.Z),
            new Vec3(min.X, max.Y, min.Z),
            new Vec3(max.X, max.Y, min.Z),
            tBot));
        // +X (east)
        b.Planes.Add(new Plane(
            new Vec3(max.X, min.Y, min.Z),
            new Vec3(max.X, max.Y, min.Z),
            new Vec3(max.X, max.Y, max.Z),
            texture));
        // -X (west)
        b.Planes.Add(new Plane(
            new Vec3(min.X, min.Y, min.Z),
            new Vec3(min.X, min.Y, max.Z),
            new Vec3(min.X, max.Y, max.Z),
            texture));
        // +Y (north)
        b.Planes.Add(new Plane(
            new Vec3(min.X, max.Y, min.Z),
            new Vec3(min.X, max.Y, max.Z),
            new Vec3(max.X, max.Y, max.Z),
            texture));
        // -Y (south)
        b.Planes.Add(new Plane(
            new Vec3(min.X, min.Y, min.Z),
            new Vec3(max.X, min.Y, min.Z),
            new Vec3(max.X, min.Y, max.Z),
            texture));
        return b;
    }

    /// <summary>
    /// Build a vertical convex prism extruded between <paramref name="zBottom"/>
    /// and <paramref name="zTop"/> from a CCW-ordered convex 2D footprint.
    /// </summary>
    /// <remarks>
    /// The footprint must be CCW and convex; vertical-side planes are emitted
    /// with outward normals using the right-perpendicular rule. The +Z and -Z
    /// faces use the top/bottom textures; sides use the side texture.
    /// </remarks>
    public static Brush MakeVerticalPrism(
        System.Collections.Generic.IReadOnlyList<Vec2> ccwFootprint,
        double zBottom, double zTop,
        string sideTexture,
        string topTexture,
        string bottomTexture)
    {
        if (ccwFootprint.Count < 3)
            throw new ArgumentException("Footprint must have at least 3 vertices.");
        if (zTop <= zBottom)
            throw new ArgumentException($"zTop {zTop} must be > zBottom {zBottom}.");

        var b = new Brush();
        // Top face (+Z): pick three CCW points; (P2-P1)×(P3-P1) → +Z when CCW viewed from above.
        // From +Z looking down at a CCW polygon (CCW in XY), the points appear CW... so we
        // emit the first three CCW XY points which gives outward = +Z (verified below).
        var v0 = ccwFootprint[0]; var v1 = ccwFootprint[1]; var v2 = ccwFootprint[2];
        b.Planes.Add(new Plane(
            new Vec3(v0.X, v0.Y, zTop),
            new Vec3(v1.X, v1.Y, zTop),
            new Vec3(v2.X, v2.Y, zTop),
            topTexture));
        // Bottom face (-Z): reverse winding.
        b.Planes.Add(new Plane(
            new Vec3(v0.X, v0.Y, zBottom),
            new Vec3(v2.X, v2.Y, zBottom),
            new Vec3(v1.X, v1.Y, zBottom),
            bottomTexture));

        // Side faces. For each CCW edge V_i → V_{i+1}, outward normal is right-perp.
        // Pick three points in CW order viewed from outside:
        //   P1 = (V_i, zBottom), P2 = (V_i, zTop), P3 = (V_{i+1}, zTop).
        // (P2-P1) = (0,0, zTop-zBottom); (P3-P1) = (dx, dy, zTop-zBottom);
        // cross = ((zTop-zBottom)*dy - 0, 0 - (zTop-zBottom)*dx, 0) = (Δz*dy, -Δz*dx, 0).
        // For CCW polygon, right-perp of (dx,dy) is (dy,-dx) which IS outward. ✓
        var n = ccwFootprint.Count;
        for (var i = 0; i < n; i++)
        {
            var a = ccwFootprint[i];
            var c = ccwFootprint[(i + 1) % n];
            b.Planes.Add(new Plane(
                new Vec3(a.X, a.Y, zBottom),
                new Vec3(a.X, a.Y, zTop),
                new Vec3(c.X, c.Y, zTop),
                sideTexture));
        }
        return b;
    }
}

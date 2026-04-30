using System;
using MapSlopper.Core.Geometry;

namespace MapSlopper.Core.Brushes;

/// <summary>
/// Helpers for building common convex brush shapes whose plane orientations
/// match the Quake 3 .map convention.
/// </summary>
/// <remarks>
/// Q3 plane convention: the three points (P1, P2, P3) are listed in CLOCKWISE
/// order when viewed from OUTSIDE the brush (i.e. from the visible side). The
/// outward-pointing normal is therefore (P3-P1) x (P2-P1). This was verified
/// against q3map2 (NetRadiantCustom) using the gamepacks/Q3.game/install/maps/
/// grabtex.map sample. An earlier version of this file used the opposite
/// (CCW-from-outside) convention; that caused q3map2 to silently discard
/// every brush ("0 total world brushes" while all planes were counted) and
/// produce empty BSPs.
/// </remarks>
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
        // +Z (top): viewed from above (+Z, looking -Z), CW corners.
        b.Planes.Add(new Plane(
            new Vec3(min.X, min.Y, max.Z),
            new Vec3(min.X, max.Y, max.Z),
            new Vec3(max.X, max.Y, max.Z),
            tTop));
        // -Z (bottom): viewed from below (-Z, looking +Z), CW corners.
        b.Planes.Add(new Plane(
            new Vec3(min.X, min.Y, min.Z),
            new Vec3(max.X, min.Y, min.Z),
            new Vec3(max.X, max.Y, min.Z),
            tBot));
        // +X (east face): viewed from +X looking -X, CW corners.
        b.Planes.Add(new Plane(
            new Vec3(max.X, min.Y, min.Z),
            new Vec3(max.X, min.Y, max.Z),
            new Vec3(max.X, max.Y, max.Z),
            texture));
        // -X (west face): viewed from -X looking +X, CW corners.
        b.Planes.Add(new Plane(
            new Vec3(min.X, min.Y, min.Z),
            new Vec3(min.X, max.Y, min.Z),
            new Vec3(min.X, max.Y, max.Z),
            texture));
        // +Y (north face): viewed from +Y looking -Y, CW corners.
        b.Planes.Add(new Plane(
            new Vec3(min.X, max.Y, min.Z),
            new Vec3(max.X, max.Y, min.Z),
            new Vec3(max.X, max.Y, max.Z),
            texture));
        // -Y (south face): viewed from -Y looking +Y, CW corners.
        b.Planes.Add(new Plane(
            new Vec3(min.X, min.Y, min.Z),
            new Vec3(min.X, min.Y, max.Z),
            new Vec3(max.X, min.Y, max.Z),
            texture));
        return b;
    }

    /// <summary>
    /// Build a vertical convex prism extruded between <paramref name="zBottom"/>
    /// and <paramref name="zTop"/> from a CCW-ordered convex 2D footprint.
    /// </summary>
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
        var v0 = ccwFootprint[0]; var v1 = ccwFootprint[1]; var v2 = ccwFootprint[2];
        // Top face (+Z): need CW viewed from above. CCW footprint reversed = CW from above.
        b.Planes.Add(new Plane(
            new Vec3(v0.X, v0.Y, zTop),
            new Vec3(v2.X, v2.Y, zTop),
            new Vec3(v1.X, v1.Y, zTop),
            topTexture));
        // Bottom face (-Z): need CW viewed from below = CCW viewed from above
        // = the original CCW footprint order.
        b.Planes.Add(new Plane(
            new Vec3(v0.X, v0.Y, zBottom),
            new Vec3(v1.X, v1.Y, zBottom),
            new Vec3(v2.X, v2.Y, zBottom),
            bottomTexture));

        // Side faces. For each CCW XY edge a -> c, the four wall corners
        // viewed from OUTSIDE (right-perp of edge direction) trace CW as:
        //   (a, zBot) -> (a, zTop) -> (c, zTop) -> (c, zBot).
        // Three such CW points give the correct outward normal under the Q3
        // (P3-P1)x(P2-P1) convention.
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

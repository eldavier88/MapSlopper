using System.Collections.Generic;

namespace MapSlopper.Core.Geometry;

/// <summary>
/// Sutherland-Hodgman clipping of a convex subject polygon by an axis-aligned
/// rectangle. Both polygons are 2D in the same coordinate space; the rectangle
/// is given as <c>[minX..maxX] × [minY..maxY]</c>.
/// </summary>
public static class RectangleClipper
{
    /// <summary>
    /// Returns the (possibly empty) convex polygon resulting from clipping
    /// <paramref name="subject"/> by the rectangle. Result vertices have
    /// the same winding (CCW) as the input.
    /// </summary>
    public static List<Vec2> Clip(
        IReadOnlyList<Vec2> subject,
        double minX, double minY, double maxX, double maxY)
    {
        var output = new List<Vec2>(subject);
        // Clip against each rectangle edge in turn (left, right, bottom, top).
        output = ClipAgainstHalfPlane(output, p => p.X >= minX, (a, b) => InterpX(a, b, minX));
        if (output.Count == 0) return output;
        output = ClipAgainstHalfPlane(output, p => p.X <= maxX, (a, b) => InterpX(a, b, maxX));
        if (output.Count == 0) return output;
        output = ClipAgainstHalfPlane(output, p => p.Y >= minY, (a, b) => InterpY(a, b, minY));
        if (output.Count == 0) return output;
        output = ClipAgainstHalfPlane(output, p => p.Y <= maxY, (a, b) => InterpY(a, b, maxY));
        return output;
    }

    private static List<Vec2> ClipAgainstHalfPlane(
        List<Vec2> input,
        System.Func<Vec2, bool> inside,
        System.Func<Vec2, Vec2, Vec2> intersect)
    {
        var result = new List<Vec2>();
        if (input.Count == 0) return result;
        var prev = input[input.Count - 1];
        var prevInside = inside(prev);
        foreach (var curr in input)
        {
            var currInside = inside(curr);
            if (currInside)
            {
                if (!prevInside) result.Add(intersect(prev, curr));
                result.Add(curr);
            }
            else if (prevInside)
            {
                result.Add(intersect(prev, curr));
            }
            prev = curr;
            prevInside = currInside;
        }
        return result;
    }

    private static Vec2 InterpX(Vec2 a, Vec2 b, double x)
    {
        var dx = b.X - a.X;
        if (System.Math.Abs(dx) < 1e-15) return new Vec2(x, a.Y);
        var t = (x - a.X) / dx;
        return new Vec2(x, a.Y + t * (b.Y - a.Y));
    }

    private static Vec2 InterpY(Vec2 a, Vec2 b, double y)
    {
        var dy = b.Y - a.Y;
        if (System.Math.Abs(dy) < 1e-15) return new Vec2(a.X, y);
        var t = (y - a.Y) / dy;
        return new Vec2(a.X + t * (b.X - a.X), y);
    }

    /// <summary>
    /// Removes degenerate vertices: consecutive points within <paramref name="epsilon"/>
    /// are merged, and if the result has fewer than 3 unique points it is cleared.
    /// </summary>
    public static List<Vec2> RemoveDegenerate(List<Vec2> poly, double epsilon = 1e-6)
    {
        if (poly.Count == 0) return poly;
        var result = new List<Vec2>(poly.Count);
        for (var i = 0; i < poly.Count; i++)
        {
            var curr = poly[i];
            var prev = i == 0 ? poly[poly.Count - 1] : poly[i - 1];
            if (Vec2.DistanceSquared(prev, curr) > epsilon * epsilon)
                result.Add(curr);
        }
        // Also remove collinear vertices.
        var pruned = new List<Vec2>(result.Count);
        for (var i = 0; i < result.Count; i++)
        {
            var prev = result[(i - 1 + result.Count) % result.Count];
            var curr = result[i];
            var next = result[(i + 1) % result.Count];
            if (System.Math.Abs(Vec2.Cross(curr - prev, next - curr)) > epsilon)
                pruned.Add(curr);
        }
        if (pruned.Count < 3) pruned.Clear();
        return pruned;
    }
}

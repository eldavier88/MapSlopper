using System;

namespace MapSlopper.Core.Geometry;

/// <summary>2D point/vector with double-precision components.</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 Zero { get; } = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public Vec2 Normalized
    {
        get
        {
            var len = Length;
            return len > 1e-12 ? new Vec2(X / len, Y / len) : Zero;
        }
    }

    /// <summary>Right-perpendicular: rotate -90° (clockwise). For a CCW polygon edge, this points OUTSIDE.</summary>
    public Vec2 PerpRight => new(Y, -X);

    /// <summary>Left-perpendicular: rotate +90° (counter-clockwise).</summary>
    public Vec2 PerpLeft => new(-Y, X);

    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    /// <summary>2D cross (scalar z of 3D cross).</summary>
    public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
    public static double DistanceSquared(Vec2 a, Vec2 b) => (a - b).LengthSquared;

    public override string ToString() =>
        $"({X.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
        $"{Y.ToString(System.Globalization.CultureInfo.InvariantCulture)})";

    private bool PrintMembers(System.Text.StringBuilder sb)
    {
        sb.Append("X = ").Append(X.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(", Y = ").Append(Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }
}

using System;

namespace MapSlopper.Core.Geometry;

/// <summary>3D point/vector. Quake convention: Z is up.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 Zero { get; } = new(0, 0, 0);
    public static Vec3 UnitX { get; } = new(1, 0, 0);
    public static Vec3 UnitY { get; } = new(0, 1, 0);
    public static Vec3 UnitZ { get; } = new(0, 0, 1);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double LengthSquared => X * X + Y * Y + Z * Z;

    public Vec3 Normalized
    {
        get
        {
            var len = Length;
            return len > 1e-12 ? new Vec3(X / len, Y / len, Z / len) : Zero;
        }
    }

    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(Vec3 a, Vec3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public Vec2 Xy => new(X, Y);
}

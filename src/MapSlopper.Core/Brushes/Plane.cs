using MapSlopper.Core.Geometry;

namespace MapSlopper.Core.Brushes;

/// <summary>
/// One face plane of a brush. Three non-collinear points define the plane;
/// the Quake .map convention is to list the three points CLOCKWISE as seen
/// from OUTSIDE the brush, which makes the outward-pointing normal equal to
/// <c>(P3-P1) x (P2-P1)</c>.
/// </summary>
public readonly record struct Plane(
    Vec3 P1, Vec3 P2, Vec3 P3,
    string Texture,
    double ShiftS = 0,
    double ShiftT = 0,
    double Rotate = 0,
    double ScaleS = 0.5,
    double ScaleT = 0.5,
    int ContentFlags = 0,
    int SurfaceFlags = 0,
    int Value = 0)
{
    /// <summary>Outward-facing plane normal (not normalized) under the Q3 convention.</summary>
    public Vec3 Normal => Vec3.Cross(P3 - P1, P2 - P1);

    public bool IsDegenerate(double epsilon = 1e-6) =>
        Normal.LengthSquared < epsilon * epsilon;
}

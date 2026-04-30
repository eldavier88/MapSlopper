using MapSlopper.Core.Geometry;

namespace MapSlopper.Core.Brushes;

/// <summary>
/// One face plane of a brush. Three non-collinear points define the plane;
/// the plane normal is <c>(P2-P1) × (P3-P1)</c> and points OUTWARD from the
/// brush interior. The Quake .map convention is to list the three points
/// clockwise as seen from outside the brush.
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
    /// <summary>Outward-facing plane normal (not normalized).</summary>
    public Vec3 Normal => Vec3.Cross(P2 - P1, P3 - P1);

    public bool IsDegenerate(double epsilon = 1e-6) =>
        Normal.LengthSquared < epsilon * epsilon;
}

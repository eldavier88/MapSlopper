using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;
using MapSlopper.Core.Outline;

namespace MapSlopper.Core.Project;

/// <summary>
/// Top-level user-editable project document. Holds outline graph + heightmap
/// + global parameters that drive geometry generation and texture assignment.
/// </summary>
public sealed class MapSlopperProject
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public OutlineGraph Outline { get; set; } = new();
    public Heightmap16 Heightmap { get; set; } = new(64, 64, 32, Vec2.Zero);

    /// <summary>Z height of the ceiling (game units, top of interior space).</summary>
    public double CeilingHeight { get; set; } = 256.0;

    /// <summary>Outward thickness of the exterior wall band (game units).</summary>
    public double WallThickness { get; set; } = 16.0;

    // Default to standard baseq3 textures so q3map2 emits visible surfaces
    // and the map opens with renderable geometry in NetRadiant. caulk is a
    // nodraw tool shader and would be stripped by q3map2 -- leaving an empty
    // BSP with zero surfaces.
    public string FloorTexture { get; set; } = "base_floor/clang_floor";
    public string WallTexture { get; set; } = "base_wall/atech1_4";
    public string CeilingTexture { get; set; } = "base_ceiling/ceil1_3";

    /// <summary>If set, used as info_player_start origin instead of polygon centroid.</summary>
    public Vec3? PlayerStartOverride { get; set; }

    /// <summary>Approximate spacing (game units) between automatically-placed lights.</summary>
    public double LightSpacing { get; set; } = 800.0;
    public double LightIntensity { get; set; } = 300.0;
    public double LightInsetFromCeiling { get; set; } = 16.0;

    /// <summary>Vertical thickness of generated ceiling brushes (game units).</summary>
    public double CeilingThickness { get; set; } = 16.0;

    /// <summary>Vertical thickness of the floor "slab" (always at and below Z=0).</summary>
    public double FloorBaseThickness { get; set; } = 16.0;
}

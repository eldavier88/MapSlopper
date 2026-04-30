using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;
using MapSlopper.Core.Outline;
using MapSlopper.Core.Triggers;

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

    /// <summary>
    /// Trigger paint layer. Same dimensions / origin / cell size as the
    /// heightmap (kept in sync by the editor and the loader). Each cell
    /// stores a <see cref="TriggerType.Id"/> in its low byte; 0 means
    /// "no trigger". Painted same-id connected components become one
    /// brush-model entity at export time.
    /// </summary>
    public Heightmap16 TriggerLayer { get; set; } = new(64, 64, 32, Vec2.Zero);

    /// <summary>
    /// Optional per-project overrides for the program-wide trigger type
    /// config. Entries with the same <see cref="TriggerType.Id"/> as a
    /// program-wide entry replace it; entries that exist only program-wide
    /// remain available. Null means "use the program-wide config as-is".
    /// </summary>
    public TriggerTypeConfig? TriggerOverrides { get; set; }

    /// <summary>Z height of the ceiling (game units, top of interior space).</summary>
    public double CeilingHeight { get; set; } = 256.0;

    /// <summary>Outward thickness of the exterior wall band (game units).</summary>
    public double WallThickness { get; set; } = 16.0;

    // Default to MapSlopper's bundled "random/*" $whiteimage shaders
    // (assets/baseq3/scripts/mapslopper.shader). q3map2 needs a .shader
    // file at <fs_basepath>/baseq3/scripts/ defining these names; the file
    // is copied automatically by the fuzz / sanity / integration scripts
    // and shipped in the release archive. Avoids depending on baseq3 .pk3
    // art and avoids the silent surface-strip behaviour of common/caulk.
    public string FloorTexture { get; set; } = "random/floor";
    public string WallTexture { get; set; } = "random/wall";
    public string CeilingTexture { get; set; } = "random/ceiling";

    /// <summary>
    /// Texture used for the upper half of walls that exceed
    /// <see cref="WallSplitHeight"/>. When a wall would be taller than that
    /// limit (measured upward from its bottom Z), it is split horizontally
    /// at <c>bottom + WallSplitHeight</c>: the lower brush keeps
    /// <see cref="WallTexture"/>, the upper brush uses this texture.
    /// Defaults to MapSlopper's bundled <c>random/window</c> shader.
    /// </summary>
    public string WindowTexture { get; set; } = "random/window";

    /// <summary>
    /// Maximum wall height (game units, measured from the wall's bottom Z
    /// to its top) before the wall brush is horizontally split for
    /// window-texturing the upper half. <c>null</c> (the default) means
    /// "use <see cref="CeilingHeight"/>" — i.e. matches the default
    /// floor-to-ceiling clearance, so any wall whose visible height exceeds
    /// the standard ceiling offset gets a window strip on top.
    /// </summary>
    public double? WallSplitHeight { get; set; }

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

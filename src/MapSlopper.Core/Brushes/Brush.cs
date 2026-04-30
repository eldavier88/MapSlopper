using System.Collections.Generic;

namespace MapSlopper.Core.Brushes;

/// <summary>A convex solid defined by the intersection of half-spaces (planes).</summary>
public sealed class Brush
{
    public List<Plane> Planes { get; } = new();
}

/// <summary>An entity in a Quake 3 map: key/value properties plus optional brushes.</summary>
public sealed class MapEntity
{
    public Dictionary<string, string> Properties { get; } = new();
    public List<Brush> Brushes { get; } = new();
}

/// <summary>
/// A complete Quake 3 map document. Entity index 0 is the worldspawn;
/// subsequent entities follow.
/// </summary>
public sealed class MapDocument
{
    public List<MapEntity> Entities { get; } = new();

    /// <summary>Returns or creates the worldspawn entity at index 0.</summary>
    public MapEntity Worldspawn
    {
        get
        {
            if (Entities.Count == 0)
            {
                var e = new MapEntity();
                e.Properties["classname"] = "worldspawn";
                Entities.Add(e);
            }
            return Entities[0];
        }
    }
}

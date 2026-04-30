using System.Collections.Generic;

namespace MapSlopper.Core.Triggers;

/// <summary>
/// Single trigger type definition. Painted cells with the same numeric
/// <see cref="Id"/> are grouped (per connected component) into one
/// brush-model entity using <see cref="EntityProperties"/> as its
/// classname/keys, plus optional companion point entities defined by
/// <see cref="Targets"/>.
/// </summary>
public sealed class TriggerType
{
    /// <summary>Cell value used in the trigger paint layer (1..255). 0 = empty.</summary>
    public byte Id { get; set; }

    /// <summary>Human-readable label shown in the GUI palette.</summary>
    public string Name { get; set; } = "";

    /// <summary>Hex color for the GUI palette swatch and 2D overlay (e.g. "#00FF00").</summary>
    public string ColorHex { get; set; } = "#FFFFFF";

    /// <summary>Texture applied to every face of the generated brush.</summary>
    public string Texture { get; set; } = "system/trigger";

    /// <summary>
    /// Properties written onto the brush-model entity (must include
    /// "classname"). All values are emitted verbatim into the .map file.
    /// </summary>
    public Dictionary<string, string> EntityProperties { get; set; } = new();

    /// <summary>
    /// Companion point entities spawned alongside each component. Each one
    /// receives an auto-generated <c>targetname</c>; the matching
    /// <see cref="TriggerTargetSpec.LinkKey"/> on the brush entity is set
    /// to that targetname so the trigger fires the target.
    /// </summary>
    public List<TriggerTargetSpec> Targets { get; set; } = new();
}

/// <summary>
/// Spec for a companion point entity placed once per component (e.g. a
/// <c>target_startTimer</c> the trigger fires).
/// </summary>
public sealed class TriggerTargetSpec
{
    /// <summary>
    /// Key on the brush entity that should be set to the target's auto
    /// <c>targetname</c>. Defaults to "target". Use "target2", "killtarget",
    /// etc. for additional links.
    /// </summary>
    public string LinkKey { get; set; } = "target";

    /// <summary>
    /// Properties of the spawned point entity. <c>classname</c> is required;
    /// <c>targetname</c> and <c>origin</c> are auto-generated (any value
    /// supplied here is overwritten).
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}

/// <summary>
/// Collection of <see cref="TriggerType"/> definitions. The program-wide
/// config (<c>assets/triggers.json</c>) is loaded at startup and may be
/// overridden per-project: per-id entries in the project override the
/// program-wide entries; entries that exist only program-wide remain.
/// </summary>
public sealed class TriggerTypeConfig
{
    public List<TriggerType> Types { get; set; } = new();

    /// <summary>
    /// Returns a new config that contains every type from <paramref name="baseConfig"/>,
    /// with same-Id entries from <paramref name="overrides"/> replacing the
    /// base entry and any new Ids appended. Neither input is mutated.
    /// </summary>
    public static TriggerTypeConfig MergeOverrides(TriggerTypeConfig baseConfig, TriggerTypeConfig? overrides)
    {
        var merged = new TriggerTypeConfig();
        var byId = new Dictionary<byte, TriggerType>();
        foreach (var t in baseConfig.Types) byId[t.Id] = t;
        if (overrides is not null)
            foreach (var t in overrides.Types) byId[t.Id] = t;
        foreach (var t in byId.Values) merged.Types.Add(t);
        merged.Types.Sort((a, b) => a.Id.CompareTo(b.Id));
        return merged;
    }

    public TriggerType? FindById(byte id)
    {
        foreach (var t in Types) if (t.Id == id) return t;
        return null;
    }

    /// <summary>
    /// Built-in default — used when no <c>assets/triggers.json</c> file is
    /// found alongside the executable. Provides the three colors the spec
    /// asks for: green=startTimer, red=stopTimer, blue=checkpoint.
    /// </summary>
    public static TriggerTypeConfig BuiltInDefault() => new()
    {
        Types =
        {
            new TriggerType
            {
                Id = 1, Name = "Start Timer", ColorHex = "#33CC33",
                Texture = "system/trigger",
                EntityProperties = new Dictionary<string, string> { { "classname", "trigger_multiple" } },
                Targets =
                {
                    new TriggerTargetSpec
                    {
                        LinkKey = "target",
                        Properties = new Dictionary<string, string> { { "classname", "target_startTimer" } },
                    },
                },
            },
            new TriggerType
            {
                Id = 2, Name = "Stop Timer", ColorHex = "#CC3333",
                Texture = "system/trigger",
                EntityProperties = new Dictionary<string, string> { { "classname", "trigger_multiple" } },
                Targets =
                {
                    new TriggerTargetSpec
                    {
                        LinkKey = "target",
                        Properties = new Dictionary<string, string> { { "classname", "target_stopTimer" } },
                    },
                },
            },
            new TriggerType
            {
                Id = 3, Name = "Checkpoint", ColorHex = "#3366CC",
                Texture = "system/trigger",
                EntityProperties = new Dictionary<string, string> { { "classname", "trigger_multiple" } },
                Targets =
                {
                    new TriggerTargetSpec
                    {
                        LinkKey = "target",
                        Properties = new Dictionary<string, string> { { "classname", "target_checkpoint" } },
                    },
                },
            },
        },
    };
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MapSlopper.Gui;

/// <summary>
/// Most-recently-used (MRU) list of project file paths. Persisted to
/// <c>%APPDATA%/MapSlopper/recent-files.json</c> alongside <see cref="UiSettings"/>.
/// Used by:
/// <list type="bullet">
///   <item>The <c>File &gt; Open Recent</c> submenu on the main menu.</item>
///   <item>The Welcome dialog shown when the editor is launched without
///         a project loaded (top 5 entries).</item>
/// </list>
///
/// Entries are deduplicated case-insensitively (Windows-friendly) and
/// capped at <see cref="MaxEntries"/>; entries that no longer exist on
/// disk are pruned at load time so the menu doesn't accumulate stale
/// paths from deleted scratch files.
/// </summary>
public sealed class RecentFiles
{
    public const int MaxEntries = 10;

    /// <summary>Most-recent first.</summary>
    public List<string> Paths { get; set; } = new();

    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MapSlopper",
            "recent-files.json");

    public static RecentFiles Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path)) return new RecentFiles();
            var json = File.ReadAllText(path);
            var s = JsonSerializer.Deserialize<RecentFiles>(json, s_options) ?? new RecentFiles();
            // Drop entries whose backing file no longer exists. Done at
            // load time (rather than render time) so the persisted file
            // self-cleans across sessions.
            s.Paths = s.Paths.Where(File.Exists).Take(MaxEntries).ToList();
            return s;
        }
        catch
        {
            return new RecentFiles();
        }
    }

    public void Save()
    {
        try
        {
            var path = SettingsPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, s_options));
        }
        catch
        {
            // Best-effort persistence; swallow IO errors.
        }
    }

    /// <summary>
    /// Add (or promote) <paramref name="path"/> to the top of the list.
    /// Case-insensitive deduplication and capacity cap applied.
    /// </summary>
    public void Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        // Case-insensitive equality on Windows; consistent on Linux/macOS
        // by treating identical strings as duplicates without normalising
        // case (so a user who consciously uses two different cases gets
        // two entries — uncommon, harmless).
        Paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Paths.Insert(0, path);
        if (Paths.Count > MaxEntries)
            Paths.RemoveRange(MaxEntries, Paths.Count - MaxEntries);
    }

    public void Remove(string path)
    {
        Paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
    }

    public void Clear() => Paths.Clear();
}

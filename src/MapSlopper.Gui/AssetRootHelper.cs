using System;
using System.Collections.Generic;
using System.IO;

namespace MapSlopper.Gui;

/// <summary>
/// Helpers that turn user-picked file-system paths into "useful" asset
/// roots for the 3D preview, and locate the assets directory shipped
/// next to the GUI executable so MapSlopper's bundled
/// <c>mapslopper.shader</c> is always available even when the user
/// hasn't configured any roots.
///
/// Path conventions encountered in the wild:
/// <list type="bullet">
///   <item>Quake 3 install:   <c>&lt;fs_basepath&gt;/baseq3/{scripts,textures}</c></item>
///   <item>Jedi Outcast (JK2): <c>&lt;install&gt;/GameData/base/{shaders,textures}</c></item>
///   <item>Jedi Academy (JKA): <c>&lt;install&gt;/GameData/base/{shaders,textures}</c></item>
///   <item>RTCW / EF / etc.:   variations of the above</item>
/// </list>
/// The user typically picks the install directory, the GameData/baseq3
/// directory, or one of the inner content directories. We detect each
/// case and pick the one that gives <see cref="MapSlopper.Core.Assets.AssetLibrary"/>
/// the right set of children to walk.
/// </summary>
internal static class AssetRootHelper
{
    /// <summary>
    /// Folder names that act as "content roots" — they sit at the level
    /// where <c>scripts/</c> and <c>textures/</c> live as siblings.
    /// </summary>
    private static readonly string[] s_contentRootChildren =
    {
        "baseq3",       // Quake 3 / RTCW
        "base",         // Jedi Outcast (JK2), Jedi Academy (JKA), Elite Force
        "missionpack",  // Q3 Team Arena
        "main",         // Star Trek EF, RTCW
    };

    /// <summary>
    /// Names that already are content roots themselves (we should accept
    /// them as-is rather than walk further down).
    /// </summary>
    private static readonly string[] s_contentRootSiblings =
    {
        "scripts", "shaders", "textures", "models", "sounds",
    };

    /// <summary>
    /// Given a path the user just picked, choose the most useful asset
    /// root. Returns the original path if nothing better is found.
    ///
    /// Strategy:
    ///   1. If path contains scripts/ or shaders/ or textures/ as a
    ///      direct child, use it as-is.
    ///   2. Otherwise, if it has a known game-folder subdir (baseq3/
    ///      base/ etc.), use that subdir.
    ///   3. Otherwise, walk one level deeper looking for either of the
    ///      above (handles "user picked the parent of GameData").
    ///   4. Otherwise return the original path.
    /// </summary>
    public static string ResolvePickedDirectory(string picked)
    {
        if (string.IsNullOrWhiteSpace(picked) || !Directory.Exists(picked))
            return picked;

        if (HasContentSibling(picked)) return picked;

        var direct = TryFindContentRootChild(picked);
        if (direct is not null) return direct;

        // One level deeper: handles "user picked /Games/Jedi Academy"
        // when the actual root is /Games/Jedi Academy/GameData/base.
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(picked))
            {
                var candidate = TryFindContentRootChild(sub);
                if (candidate is not null) return candidate;
                if (HasContentSibling(sub)) return sub;
            }
        }
        catch { /* permission denied, broken symlink, etc. */ }

        return picked;
    }

    private static string? TryFindContentRootChild(string path)
    {
        foreach (var name in s_contentRootChildren)
        {
            var candidate = Path.Combine(path, name);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static bool HasContentSibling(string path)
    {
        try
        {
            foreach (var sib in s_contentRootSiblings)
            {
                if (Directory.Exists(Path.Combine(path, sib))) return true;
            }
            // Also accept directories that contain at least one .pk3 — they
            // may be a "mods" folder that the user wants mounted.
            foreach (var _ in Directory.EnumerateFiles(path, "*.pk3", SearchOption.TopDirectoryOnly))
                return true;
        }
        catch { /* permission denied */ }
        return false;
    }

    /// <summary>
    /// Path to the bundled <c>assets/baseq3</c> shipped next to the GUI
    /// exe (copied by the csproj Content item). Returns null when the
    /// directory is missing — e.g. running from a stripped publish that
    /// lost the assets, or unit tests that don't include them.
    ///
    /// Uses <see cref="AppContext.BaseDirectory"/> rather than
    /// <c>Assembly.GetExecutingAssembly().Location</c> because the
    /// latter returns an empty string when running from a single-file
    /// publish (the assemblies are extracted to a temp folder, but the
    /// exe lives elsewhere, and Location reflects neither correctly).
    /// </summary>
    public static string? GetBundledBaseq3Path()
    {
        var exeDir = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(exeDir)) return null;
        var bundled = Path.Combine(exeDir, "assets", "baseq3");
        return Directory.Exists(bundled) ? bundled : null;
    }

    /// <summary>
    /// Combine the project's user-configured roots with the bundled
    /// MapSlopper baseq3 (appended last so user roots override on
    /// shader-name conflicts, matching Q3 fs first-wins semantics in
    /// <see cref="MapSlopper.Core.Assets.AssetLibrary.Load"/>). The bundled
    /// path is *not* persisted in the project — it's machine-local.
    /// </summary>
    public static List<string> WithBundledFallback(IEnumerable<string> userRoots)
    {
        var combined = new List<string>(userRoots);
        var bundled = GetBundledBaseq3Path();
        if (bundled is not null
            && !combined.Exists(p => string.Equals(p, bundled, StringComparison.OrdinalIgnoreCase)))
        {
            combined.Add(bundled);
        }
        return combined;
    }
}

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;

namespace MapSlopper.Gui;

/// <summary>
/// Persistent user-interface preferences (window geometry, side-panel
/// widths). Stored in JSON under the user's roaming AppData directory so
/// the layout survives across launches. Validation on load discards
/// obviously bogus values (offscreen window, impossibly small sizes).
///
/// Three layers of protection keep the window from getting "stuck"
/// offscreen if the user yanks a monitor between sessions:
/// <list type="number">
///   <item>Partial overlap: <see cref="TryClampToVisibleScreen"/> shifts
///         the window so it fully fits inside the connected screen with
///         the most overlap.</item>
///   <item>Zero overlap (e.g. external monitor removed): falls back to
///         <see cref="WindowStartupLocation.CenterScreen"/>.</item>
///   <item>Corrupt JSON / unreadable file: silently uses defaults.</item>
/// </list>
/// </summary>
public sealed class UiSettings
{
    // --- Window ---
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    // --- Side panels (pixel widths of columns 0 and 4 in MainGrid) ---
    public double? LeftPanelWidth { get; set; }
    public double? RightPanelWidth { get; set; }

    /// <summary>
    /// Whether to show the Welcome dialog on launch when no project has
    /// been loaded yet. Defaults to true; flipped off by the user via
    /// the "Don't show again" checkbox on the dialog itself.
    /// </summary>
    public bool ShowWelcomeOnStartup { get; set; } = true;

    // Guard-rails. These match the XAML MinWidth values (40) and a sane
    // upper bound relative to typical screens.
    private const double MinPanelWidth = 40.0;
    private const double MaxPanelWidth = 4000.0;
    private const double MinWindowSide = 320.0;

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
            "ui-settings.json");

    public static UiSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path)) return new UiSettings();
            var json = File.ReadAllText(path);
            var s = JsonSerializer.Deserialize<UiSettings>(json, s_options);
            return s ?? new UiSettings();
        }
        catch
        {
            // Corrupt or unreadable settings should never block startup.
            return new UiSettings();
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
            // Best-effort persistence; swallow IO errors on shutdown.
        }
    }

    /// <summary>
    /// Apply the saved geometry + panel widths to the window. Safely ignores
    /// values that would place the window offscreen or shrink it below
    /// usable limits.
    /// </summary>
    public void ApplyTo(Window window, ColumnDefinition leftCol, ColumnDefinition rightCol)
    {
        if (window is null) throw new ArgumentNullException(nameof(window));

        if (LeftPanelWidth is double lw && lw >= MinPanelWidth && lw <= MaxPanelWidth)
            leftCol.Width = new GridLength(lw, GridUnitType.Pixel);
        if (RightPanelWidth is double rw && rw >= MinPanelWidth && rw <= MaxPanelWidth)
            rightCol.Width = new GridLength(rw, GridUnitType.Pixel);

        if (WindowWidth is double w && w >= MinWindowSide && w <= 100000)
            window.Width = w;
        if (WindowHeight is double h && h >= MinWindowSide && h <= 100000)
            window.Height = h;

        if (WindowX is int x && WindowY is int y)
        {
            var wPx = (int)Math.Round(window.Width > 0 ? window.Width : 1200);
            var hPx = (int)Math.Round(window.Height > 0 ? window.Height : 800);
            if (TryClampToVisibleScreen(window, x, y, wPx, hPx, out var cx, out var cy))
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = new PixelPoint(cx, cy);
            }
            else
            {
                // Saved position has zero overlap with any connected screen
                // (e.g. external monitor removed). Fall back to a visible
                // default instead of the previous phantom coordinates.
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        if (WindowMaximized) window.WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// Snapshot the window + column widths into this settings object. Does
    /// not write to disk -- call <see cref="Save"/> afterwards.
    /// </summary>
    public void CaptureFrom(Window window, ColumnDefinition leftCol, ColumnDefinition rightCol)
    {
        if (window is null) throw new ArgumentNullException(nameof(window));

        WindowMaximized = window.WindowState == WindowState.Maximized;
        // When maximized, Width/Height/Position describe the maximized rect
        // which is useless for restoration. Keep the previous "normal"
        // values untouched in that case.
        if (!WindowMaximized)
        {
            WindowWidth = window.Width;
            WindowHeight = window.Height;
            var p = window.Position;
            WindowX = p.X;
            WindowY = p.Y;
        }

        // Columns are declared as absolute pixels in XAML and the Grid
        // splitter keeps them absolute, so Width.Value is the current
        // resolved width. If a column ends up non-absolute for any reason,
        // keep the previously-saved width rather than overwriting with 0.
        if (leftCol.Width.IsAbsolute && leftCol.Width.Value > 0)
            LeftPanelWidth = leftCol.Width.Value;
        if (rightCol.Width.IsAbsolute && rightCol.Width.Value > 0)
            RightPanelWidth = rightCol.Width.Value;
    }

    /// <summary>
    /// Pick the connected screen that overlaps the saved rect the most and
    /// shift the window position so it fits entirely within that screen's
    /// bounds. Returns false when no connected screen overlaps the saved
    /// rect (so the caller can fall back to a visible default) or when
    /// there are no screens at all.
    /// </summary>
    private static bool TryClampToVisibleScreen(
        Window window, int x, int y, int w, int h,
        out int clampedX, out int clampedY)
    {
        clampedX = x; clampedY = y;
        var screens = window.Screens;
        if (screens is null || screens.All.Count == 0) return false;

        var saved = new PixelRect(x, y, Math.Max(1, w), Math.Max(1, h));
        Avalonia.Platform.Screen? best = null;
        var bestArea = 0L;
        foreach (var s in screens.All)
        {
            var b = s.Bounds;
            var ix = Math.Max(b.X, saved.X);
            var iy = Math.Max(b.Y, saved.Y);
            var ax = Math.Min(b.X + b.Width, saved.X + saved.Width);
            var ay = Math.Min(b.Y + b.Height, saved.Y + saved.Height);
            var iw = Math.Max(0, ax - ix);
            var ih = Math.Max(0, ay - iy);
            var area = (long)iw * ih;
            if (area > bestArea)
            {
                bestArea = area;
                best = s;
            }
        }

        // Nothing overlaps -> the saved monitor is gone, or the coords are
        // garbage. Refuse to restore the position.
        if (best is null || bestArea == 0) return false;

        var area2 = best.Bounds;
        // Clamp x/y so the window fits within area2. If the window is
        // larger than the screen on some axis, align to that edge so the
        // title bar stays reachable.
        var maxX = Math.Max(area2.X, area2.X + area2.Width - w);
        var maxY = Math.Max(area2.Y, area2.Y + area2.Height - h);
        clampedX = Math.Clamp(x, area2.X, maxX);
        clampedY = Math.Clamp(y, area2.Y, maxY);
        return true;
    }
}

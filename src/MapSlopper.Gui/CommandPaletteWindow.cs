using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace MapSlopper.Gui;

/// <summary>
/// VSCode-style command palette: a small modal dialog with a fuzzy
/// search box and a scrollable list of every action available in the
/// editor. Picking an entry (Enter or click) closes the dialog and
/// fires the command's handler. Esc or losing focus cancels.
///
/// Commands are registered as plain delegates so we don't introduce a
/// command framework just for this — the menu handlers in
/// <see cref="MainWindow"/> are reused verbatim.
/// </summary>
internal sealed class CommandPaletteWindow : Window
{
    public sealed record Command(string Name, string? Hint, Func<Task> Run);

    private readonly TaskCompletionSource<Command?> _tcs = new();
    private readonly IReadOnlyList<Command> _all;
    private readonly TextBox _query = new()
    {
        Watermark = "Type a command...",
        Margin = new Thickness(0, 0, 0, 8),
        FontSize = 14,
    };
    private readonly ListBox _results = new()
    {
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
    };

    private CommandPaletteWindow(IReadOnlyList<Command> commands)
    {
        _all = commands;
        Title = "Command Palette";
        Width = 520;
        Height = 360;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.BorderOnly;
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1E));

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(_query);
        panel.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x32)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x18)),
            Child = _results,
            Height = 280,
        });
        Content = panel;

        _query.TextChanged += (_, _) => Refresh();
        _query.KeyDown += OnQueryKey;
        _results.DoubleTapped += (_, _) => Pick();

        // ESC anywhere closes; Enter picks the highlighted result.
        AddHandler(KeyDownEvent, OnGlobalKey, RoutingStrategies.Tunnel);

        Refresh();
        Closed += (_, _) => _tcs.TrySetResult(null);
        Opened += (_, _) => _query.Focus();
    }

    /// <summary>Show the palette modally and return the picked command, or null on cancel.</summary>
    public static Task<Command?> ShowAsync(Window owner, IReadOnlyList<Command> commands)
    {
        var dlg = new CommandPaletteWindow(commands);
        _ = dlg.ShowDialog(owner);
        return dlg._tcs.Task;
    }

    private void OnGlobalKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _tcs.TrySetResult(null); Close(); e.Handled = true; }
    }

    private void OnQueryKey(object? sender, KeyEventArgs e)
    {
        // Up/Down move the highlight, Enter picks. Up/Down inside the
        // text box don't navigate text by default (single-line), so we
        // can safely bind them to list movement.
        if (e.Key == Key.Down)
        {
            if (_results.ItemCount > 0)
                _results.SelectedIndex = Math.Min(_results.ItemCount - 1, _results.SelectedIndex + 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (_results.ItemCount > 0)
                _results.SelectedIndex = Math.Max(0, _results.SelectedIndex - 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Pick();
            e.Handled = true;
        }
    }

    private void Pick()
    {
        var idx = _results.SelectedIndex;
        if (idx < 0 || idx >= _results.ItemCount) return;
        if (_results.ItemsSource is not IList<Command> list) return;
        var cmd = list[idx];
        _tcs.TrySetResult(cmd);
        Close();
    }

    /// <summary>
    /// Re-filter the visible list. Uses a substring + initials match
    /// (e.g. "gam" matches "Generate Automatic Map") with simple
    /// scoring: substring matches rank above initial-letter matches.
    /// Good enough for the dozen-or-so commands we expose.
    /// </summary>
    private void Refresh()
    {
        var q = _query.Text?.Trim() ?? string.Empty;
        IList<Command> filtered;
        if (q.Length == 0)
        {
            filtered = _all.ToList();
        }
        else
        {
            var qLower = q.ToLowerInvariant();
            filtered = _all
                .Select(c => (cmd: c, score: Score(c.Name, qLower)))
                .Where(t => t.score > 0)
                .OrderByDescending(t => t.score)
                .ThenBy(t => t.cmd.Name)
                .Select(t => t.cmd)
                .ToList();
        }

        var rows = new List<Control>();
        foreach (var cmd in filtered)
        {
            var name = new TextBlock
            {
                Text = cmd.Name,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEE)),
                FontSize = 13,
            };
            var hint = new TextBlock
            {
                Text = cmd.Hint ?? string.Empty,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x84)),
                FontSize = 11,
            };
            rows.Add(new StackPanel
            {
                Spacing = 1,
                Margin = new Thickness(8, 4),
                Children = { name, hint },
            });
        }

        _results.ItemsSource = filtered;
        _results.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Command>(
            (cmd, _) =>
            {
                if (cmd is null) return new TextBlock();
                return new StackPanel
                {
                    Spacing = 1,
                    Margin = new Thickness(8, 4),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = cmd.Name,
                            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEE)),
                            FontSize = 13,
                        },
                        new TextBlock
                        {
                            Text = cmd.Hint ?? string.Empty,
                            Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x84)),
                            FontSize = 11,
                        },
                    },
                };
            },
            supportsRecycling: true);
        if (filtered.Count > 0) _results.SelectedIndex = 0;
    }

    /// <summary>
    /// Tiny relevance scorer. 100 for exact match, 60 for prefix, 40
    /// for substring, 20 for initials. Anything else = 0 (filtered out).
    /// </summary>
    private static int Score(string name, string queryLower)
    {
        var n = name.ToLowerInvariant();
        if (n == queryLower) return 100;
        if (n.StartsWith(queryLower, StringComparison.Ordinal)) return 60;
        if (n.Contains(queryLower, StringComparison.Ordinal)) return 40;

        // Initials: first letters of each word in the command name.
        var initials = string.Concat(name.Split(new[] { ' ', '/', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w[0]))
            .ToLowerInvariant();
        if (initials.Contains(queryLower, StringComparison.Ordinal)) return 20;
        return 0;
    }
}

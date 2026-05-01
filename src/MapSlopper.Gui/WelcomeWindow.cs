using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MapSlopper.Gui;

/// <summary>
/// First-launch landing dialog. Replaces the silent "blank canvas with
/// no project loaded" state new users used to face with a curated set
/// of clear next-step buttons:
/// <list type="bullet">
///   <item><b>New project</b> — start from scratch on the blank canvas.</item>
///   <item><b>Open...</b> — file picker for an existing project.</item>
///   <item><b>Generate Automatic Map...</b> — opens the auto-map wizard.</item>
///   <item><b>Recent</b> — up to five MRU project files, one click each.</item>
/// </list>
/// The dialog is modal over the main window. Closing without picking
/// anything (X button or Esc) leaves the editor in its default state,
/// which is the same blank-canvas behaviour as if the dialog hadn't
/// been shown — i.e. no harm done.
/// </summary>
internal sealed class WelcomeWindow : Window
{
    public enum Action { Cancel, New, Open, AutoMap, OpenRecent }

    public sealed class Result
    {
        public Action Action { get; init; }
        /// <summary>Set when <see cref="Action"/> is <see cref="Action.OpenRecent"/>.</summary>
        public string? RecentPath { get; init; }
        /// <summary>True if the user ticked "Don't show on startup".</summary>
        public bool DontShowAgain { get; init; }
    }

    private readonly TaskCompletionSource<Result> _tcs = new();
    private readonly RecentFiles _recent;
    private readonly CheckBox _dontShowAgain = new()
    {
        Content = "Don't show on startup",
        Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA4)),
        FontSize = 11,
    };

    private WelcomeWindow(RecentFiles recent)
    {
        _recent = recent;
        Title = "Welcome to MapSlopper";
        Width = 560;
        Height = 420;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1A));
        Content = BuildLayout();
        Closed += (_, _) => _tcs.TrySetResult(new Result { Action = Action.Cancel });
    }

    /// <summary>
    /// Show the welcome dialog modally. Returns the user's choice (or
    /// <see cref="Action.Cancel"/> if they closed without picking).
    /// </summary>
    public static Task<Result> ShowAsync(Window owner, RecentFiles recent)
    {
        var dlg = new WelcomeWindow(recent);
        _ = dlg.ShowDialog(owner);
        return dlg._tcs.Task;
    }

    private Control BuildLayout()
    {
        var panel = new StackPanel { Margin = new Thickness(28), Spacing = 18 };

        panel.Children.Add(new TextBlock
        {
            Text = "MapSlopper",
            FontWeight = FontWeight.Bold,
            FontSize = 26,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEE)),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "2D map editor for Quake 3 / JK2 / JKA. Pick where to start:",
            Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xB3)),
            FontSize = 13,
        });

        // Big primary action buttons.
        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 8, 0, 0),
        };
        actionRow.Children.Add(MakePrimaryButton(
            "New project",
            "Start with an empty canvas.",
            () => Pick(Action.New)));
        actionRow.Children.Add(MakePrimaryButton(
            "Open...",
            "Load a saved .json project.",
            () => Pick(Action.Open)));
        actionRow.Children.Add(MakePrimaryButton(
            "Auto-Map...",
            "Generate a randomised map.",
            () => Pick(Action.AutoMap)));
        panel.Children.Add(actionRow);

        // Recent list (only when there's something to show).
        if (_recent.Paths.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Recent",
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA4)),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 8, 0, 0),
            });
            var recentList = new StackPanel { Spacing = 2 };
            // Cap at 5 here — the full ten still live in File > Open Recent.
            var max = Math.Min(5, _recent.Paths.Count);
            for (var i = 0; i < max; i++)
            {
                var path = _recent.Paths[i];
                recentList.Children.Add(MakeRecentRow(path));
            }
            panel.Children.Add(recentList);
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Tip: drop a .json project file onto the editor window to open it.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x6F, 0x7A)),
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0),
            });
        }

        // Footer: "Don't show on startup" checkbox.
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x32)),
            Margin = new Thickness(0, 12, 0, 0),
        });
        panel.Children.Add(_dontShowAgain);

        return panel;
    }

    private void Pick(Action a)
    {
        _tcs.TrySetResult(new Result
        {
            Action = a,
            DontShowAgain = _dontShowAgain.IsChecked == true,
        });
        Close();
    }

    private void PickRecent(string path)
    {
        _tcs.TrySetResult(new Result
        {
            Action = Action.OpenRecent,
            RecentPath = path,
            DontShowAgain = _dontShowAgain.IsChecked == true,
        });
        Close();
    }

    private static Button MakePrimaryButton(string title, string subtitle, System.Action onClick)
    {
        var titleBlock = new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEE)),
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
        };
        var subBlock = new TextBlock
        {
            Text = subtitle,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA4)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        };
        var btn = new Button
        {
            Width = 160,
            Height = 90,
            Padding = new Thickness(12),
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x2C)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)),
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Children = { titleBlock, subBlock },
            },
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private Button MakeRecentRow(string path)
    {
        var name = new TextBlock
        {
            Text = System.IO.Path.GetFileName(path),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEE)),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
        };
        var dir = new TextBlock
        {
            Text = System.IO.Path.GetDirectoryName(path) ?? string.Empty,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x6F, 0x7A)),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var btn = new Button
        {
            Padding = new Thickness(10, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x32)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children = { name, dir },
            },
        };
        ToolTip.SetTip(btn, path);
        btn.Click += (_, _) => PickRecent(path);
        return btn;
    }
}

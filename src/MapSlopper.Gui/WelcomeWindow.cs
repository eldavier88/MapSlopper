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

    private static readonly Color CAccent      = Color.FromRgb(0x00, 0xB4, 0xD8);
    private static readonly Color CAccentHover = Color.FromRgb(0x00, 0xD4, 0xFF);
    private static readonly Color CTextPrimary = Color.FromRgb(0xF0, 0xF0, 0xF8);
    private static readonly Color CTextSub     = Color.FromRgb(0xB8, 0xB8, 0xC8);
    private static readonly Color CTextMuted   = Color.FromRgb(0x88, 0x88, 0xA0);
    private static readonly Color CTextDim     = Color.FromRgb(0x50, 0x50, 0x68);
    private static readonly Color CSurface     = Color.FromRgb(0x0F, 0x10, 0x14);
    private static readonly Color CSurfaceCard = Color.FromRgb(0x1A, 0x1A, 0x22);
    private static readonly Color CSurfaceRaised = Color.FromRgb(0x20, 0x20, 0x2A);
    private static readonly Color CBorderSub   = Color.FromRgb(0x1E, 0x1E, 0x28);
    private static readonly Color CBorderDef   = Color.FromRgb(0x2C, 0x2C, 0x38);

    private readonly TaskCompletionSource<Result> _tcs = new();
    private readonly RecentFiles _recent;
    private readonly CheckBox _dontShowAgain = new()
    {
        Content = "Don't show on startup",
        Foreground = new SolidColorBrush(CTextMuted),
        FontSize = 11,
        Margin = new Thickness(0),
    };

    private WelcomeWindow(RecentFiles recent)
    {
        _recent = recent;
        Title = "Welcome to MapSlopper";
        Width = 600;
        Height = 500;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(CSurface);
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
        var root = new DockPanel();

        // ─── Hero header area ───
        var heroPanel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x13, 0x13, 0x18)),
            Padding = new Thickness(36, 32, 36, 24),
        };
        var heroStack = new StackPanel { Spacing = 8 };

        // App icon + title row
        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
        };
        titleRow.Children.Add(new TextBlock
        {
            Text = "◆",
            Foreground = new SolidColorBrush(CAccent),
            FontSize = 32,
            VerticalAlignment = VerticalAlignment.Center,
        });
        titleRow.Children.Add(new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "MapSlopper",
                    FontWeight = FontWeight.Bold,
                    FontSize = 28,
                    Foreground = new SolidColorBrush(CTextPrimary),
                },
                new TextBlock
                {
                    Text = "2D Map Editor for Quake 3 / JK2 / JKA",
                    Foreground = new SolidColorBrush(CTextMuted),
                    FontSize = 12,
                },
            },
        });
        heroStack.Children.Add(titleRow);

        // Tagline
        heroStack.Children.Add(new TextBlock
        {
            Text = "Design interconnected level layouts with real-time 3D preview.",
            Foreground = new SolidColorBrush(CTextSub),
            FontSize = 13,
            Margin = new Thickness(0, 6, 0, 0),
        });

        heroPanel.Child = heroStack;
        DockPanel.SetDock(heroPanel, Dock.Top);
        root.Children.Add(heroPanel);

        // ─── Content area ───
        var contentScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var content = new StackPanel
        {
            Margin = new Thickness(36, 20, 36, 20),
            Spacing = 16,
        };

        // Section: Get Started
        content.Children.Add(MakeSectionLabel("GET STARTED"));

        var actionRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,12,*,12,*"),
        };
        var btn1 = MakeActionCard("⊕", "New Project", "Start from scratch.", () => Pick(Action.New));
        var btn2 = MakeActionCard("📂", "Open...", "Load a saved project.", () => Pick(Action.Open));
        var btn3 = MakeActionCard("⚡", "Auto-Map", "Generate a random map.", () => Pick(Action.AutoMap));
        Grid.SetColumn(btn1, 0);
        Grid.SetColumn(btn2, 2);
        Grid.SetColumn(btn3, 4);
        actionRow.Children.Add(btn1);
        actionRow.Children.Add(btn2);
        actionRow.Children.Add(btn3);
        content.Children.Add(actionRow);

        // Section: Recent Projects
        if (_recent.Paths.Count > 0)
        {
            content.Children.Add(MakeSectionLabel("RECENT PROJECTS"));
            var recentStack = new StackPanel { Spacing = 4 };
            var max = Math.Min(5, _recent.Paths.Count);
            for (var i = 0; i < max; i++)
            {
                var path = _recent.Paths[i];
                recentStack.Children.Add(MakeRecentRow(path, i + 1));
            }
            content.Children.Add(recentStack);
        }
        else
        {
            content.Children.Add(new Border
            {
                Background = new SolidColorBrush(CAccent) { Opacity = 0.06 },
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 10),
                Margin = new Thickness(0, 4, 0, 0),
                Child = new TextBlock
                {
                    Text = "💡 Tip: Drop a .json project file onto the editor window to open it.",
                    Foreground = new SolidColorBrush(CTextMuted),
                    FontSize = 11.5,
                },
            });
        }

        // ─── Footer ───
        content.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(CBorderSub),
            Margin = new Thickness(0, 8, 0, 0),
        });

        var footerRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            Margin = new Thickness(0, 4, 0, 0),
        };
        Grid.SetColumn(_dontShowAgain, 0);
        footerRow.Children.Add(_dontShowAgain);

        var versionBadge = new Border
        {
            Background = new SolidColorBrush(CAccent) { Opacity = 0.08 },
            CornerRadius = new CornerRadius(100),
            Padding = new Thickness(10, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = $"Avalonia {typeof(Application).Assembly.GetName().Version?.ToString(3) ?? "11.x"} · .NET {Environment.Version.Major}",
                Foreground = new SolidColorBrush(CAccent),
                FontSize = 10,
            },
        };
        Grid.SetColumn(versionBadge, 1);
        footerRow.Children.Add(versionBadge);

        content.Children.Add(footerRow);

        contentScroll.Content = content;
        root.Children.Add(contentScroll);

        return root;
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

    private static TextBlock MakeSectionLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(CTextMuted),
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.2,
            Margin = new Thickness(0, 4, 0, 2),
        };
    }

    private static Button MakeActionCard(string icon, string title, string subtitle, System.Action onClick)
    {
        var cardContent = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = icon,
                    FontSize = 22,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(CAccent),
                },
                new TextBlock
                {
                    Text = title,
                    Foreground = new SolidColorBrush(CTextPrimary),
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                new TextBlock
                {
                    Text = subtitle,
                    Foreground = new SolidColorBrush(CTextMuted),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            },
        };

        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Height = 110,
            Padding = new Thickness(12),
            Background = new SolidColorBrush(CSurfaceCard),
            BorderBrush = new SolidColorBrush(CBorderDef),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Content = cardContent,
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private Button MakeRecentRow(string path, int index)
    {
        var fileName = System.IO.Path.GetFileName(path);
        var dirName = System.IO.Path.GetDirectoryName(path) ?? string.Empty;

        var row = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,8,*"),
        };

        // Index badge
        var badge = new Border
        {
            Background = new SolidColorBrush(CSurfaceRaised),
            CornerRadius = new CornerRadius(4),
            Width = 22,
            Height = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = index.ToString(),
                Foreground = new SolidColorBrush(CTextMuted),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(badge, 0);
        row.Children.Add(badge);

        var textStack = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        textStack.Children.Add(new TextBlock
        {
            Text = fileName,
            Foreground = new SolidColorBrush(CTextPrimary),
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
        });
        textStack.Children.Add(new TextBlock
        {
            Text = dirName,
            Foreground = new SolidColorBrush(CTextDim),
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(textStack, 2);
        row.Children.Add(textStack);

        var btn = new Button
        {
            Padding = new Thickness(10, 8),
            Background = new SolidColorBrush(CSurfaceCard),
            BorderBrush = new SolidColorBrush(CBorderSub),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = row,
        };
        ToolTip.SetTip(btn, path);
        btn.Click += (_, _) => PickRecent(path);
        return btn;
    }
}

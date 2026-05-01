using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MapSlopper.Core.Generation;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;
using MapSlopper.Core.Outline;
using MapSlopper.Core.Project;

namespace MapSlopper.Gui;

/// <summary>
/// Beginner-friendly auto-map wizard. Replaces the previous five-raw-knob
/// dialog with a guided experience: pick a style preset, pick a size, get a
/// live 256x256 heightmap thumbnail of what would be generated, hit Re-roll
/// to try a different seed, then Generate to actually apply it.
///
/// Hidden behind an Advanced expander, the original four knobs (Width /
/// Height in cells, Complexity, Relief) are still editable for power users.
/// Touching them switches the preset selection to "Custom" so the chip
/// reflects the current state truthfully.
///
/// All wiring is internal to this window — the result is the same
/// <see cref="EditorViewModel.AutoMapOptions"/> shape <see cref="MainWindow"/>
/// has always passed to <see cref="EditorViewModel.GenerateAutoMap"/>, so the
/// public API contract is unchanged.
/// </summary>
internal sealed class AutoMapWizardWindow : Window
{
    /// <summary>
    /// One named look the user can pick. Drives the underlying
    /// AutoMapGenerator knobs with parameters tuned to make the resulting
    /// map feel like the named playstyle. SizeMultiplier scales the
    /// segmented size selection so e.g. "Tight CQB" naturally produces
    /// smaller layouts than "Sprawling Outpost" for the same Size button.
    /// </summary>
    private sealed record StylePreset(
        string Name,
        string Blurb,
        int Complexity,
        ushort Relief,
        double SizeMultiplier);

    private static readonly StylePreset[] Presets =
    {
        new("Open Arena",        "Wide rooms, gentle slopes — good for free-for-all duels.", 1, 64,  1.0),
        new("Tight CQB",         "Close corridors and lots of corners. Frantic close combat.", 4, 80,  0.7),
        new("Sprawling Outpost", "Mixed indoor/outdoor footprint with a larger exploration radius.", 3, 128, 1.4),
        new("Vertical Citadel",  "Tall multi-tier layout with strong elevation play.", 3, 320, 0.9),
    };

    private const string SurpriseName = "Surprise me!";
    private const string CustomName   = "Custom (Advanced)";

    private enum SizeBucket { Small, Medium, Large }
    private static readonly Dictionary<SizeBucket, (int W, int H)> SizeBase = new()
    {
        { SizeBucket.Small,  (64,  64)  },
        { SizeBucket.Medium, (96,  96)  },
        { SizeBucket.Large,  (144, 144) },
    };

    private readonly TaskCompletionSource<EditorViewModel.AutoMapOptions?> _tcs = new();
    private readonly Random _rng = new();

    // Selection state ---------------------------------------------------------
    private StylePreset? _selectedPreset = Presets[1]; // Tight CQB by default
    private SizeBucket _sizeBucket = SizeBucket.Medium;
    private bool _isCustom;
    private int? _userSeed;
    /// <summary>
    /// Seed that produced the currently-visible thumbnail. Generate uses
    /// this so the user gets exactly the layout they previewed.
    /// </summary>
    private int _previewSeed;
    private MapSlopperProject? _previewProject;

    // Controls ---------------------------------------------------------------
    private readonly StackPanel _presetList = new() { Spacing = 4 };
    private readonly Button _btnSizeSmall  = MakeSegmentedButton("Small");
    private readonly Button _btnSizeMedium = MakeSegmentedButton("Medium");
    private readonly Button _btnSizeLarge  = MakeSegmentedButton("Large");
    private readonly TextBox _seedBox = new()
    {
        Watermark = "empty = random",
        Width = 140,
    };
    private readonly NumericUpDown _widthUpDown    = new() { Minimum = 24, Maximum = 512, Increment = 8, Value = 96 };
    private readonly NumericUpDown _heightUpDown   = new() { Minimum = 24, Maximum = 512, Increment = 8, Value = 96 };
    private readonly NumericUpDown _complexityUpDown = new() { Minimum = 1, Maximum = 5, Increment = 1, Value = 4 };
    private readonly NumericUpDown _reliefUpDown   = new() { Minimum = 32, Maximum = 4096, Increment = 16, Value = 80 };
    private readonly TextBlock _blurbText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xB3)),
        FontSize = 12,
        MinHeight = 36,
    };
    private readonly TextBlock _previewMeta = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x6F, 0x7A)),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly MapThumbnailControl _thumbnail = new();
    private readonly DispatcherTimer _refreshTimer;

    /// <summary>True while we're programmatically writing into the Advanced
    /// fields (preset → fields write-through). Suppresses the user-edit
    /// detector that would otherwise flip the selection to Custom.</summary>
    private bool _suppressAdvancedEcho;

    private AutoMapWizardWindow()
    {
        Title = "Generate Automatic Map";
        Width = 740;
        Height = 540;
        MinWidth = 680;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1A));

        _refreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150),
            DispatcherPriority.Background, OnRefreshTick);
        _refreshTimer.Stop();

        Content = BuildLayout();

        BuildPresetList();
        UpdateSizeButtons();
        UpdateBlurb();
        WriteThroughPresetToAdvanced();
        WireAdvancedHandlers();
        ScheduleRefresh();
    }

    /// <summary>
    /// Show the wizard modally and return the user's selected
    /// <see cref="EditorViewModel.AutoMapOptions"/>, or null if they
    /// cancelled. The seed encoded in the result is the same one that
    /// produced the thumbnail the user was looking at when they clicked
    /// Generate (so what they see is what they get).
    /// </summary>
    public static Task<EditorViewModel.AutoMapOptions?> ShowAsync(Window owner)
    {
        var dlg = new AutoMapWizardWindow();
        _ = dlg.ShowDialog(owner);
        return dlg._tcs.Task;
    }

    // ---------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------

    private Control BuildLayout()
    {
        var grid = new Grid
        {
            Margin = new Thickness(16),
            ColumnDefinitions = new ColumnDefinitions("360,*"),
            RowDefinitions = new RowDefinitions("*,Auto"),
        };

        var leftScroll = new ScrollViewer
        {
            Content = BuildLeftColumn(),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        Grid.SetRow(leftScroll, 0); Grid.SetColumn(leftScroll, 0);
        grid.Children.Add(leftScroll);

        var right = BuildRightColumn();
        Grid.SetRow(right, 0); Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        var buttons = BuildButtonRow();
        Grid.SetRow(buttons, 1); Grid.SetColumn(buttons, 0); Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);

        return grid;
    }

    private Control BuildLeftColumn()
    {
        var panel = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(0, 0, 16, 0),
        };

        panel.Children.Add(SectionHeader("Pick a style"));
        panel.Children.Add(_presetList);

        panel.Children.Add(SectionHeader("Size"));
        var sizeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
        };
        sizeRow.Children.Add(_btnSizeSmall);
        sizeRow.Children.Add(_btnSizeMedium);
        sizeRow.Children.Add(_btnSizeLarge);
        _btnSizeSmall.Click  += (_, _) => OnSizeBucketChanged(SizeBucket.Small);
        _btnSizeMedium.Click += (_, _) => OnSizeBucketChanged(SizeBucket.Medium);
        _btnSizeLarge.Click  += (_, _) => OnSizeBucketChanged(SizeBucket.Large);
        ToolTip.SetTip(sizeRow, "Approximate playable footprint. The selected style further scales this.");
        panel.Children.Add(sizeRow);

        panel.Children.Add(SectionHeader("Seed"));
        var seedRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        seedRow.Children.Add(_seedBox);
        var rerollSeedBtn = new Button { Content = "Random" };
        rerollSeedBtn.Click += (_, _) =>
        {
            _seedBox.Text = string.Empty;
            _userSeed = null;
            ScheduleRefresh();
        };
        seedRow.Children.Add(rerollSeedBtn);
        ToolTip.SetTip(seedRow, "Same seed + same options = same map. Leave empty to roll a new layout each time.");
        panel.Children.Add(seedRow);
        _seedBox.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            if (string.IsNullOrWhiteSpace(_seedBox.Text)) { _userSeed = null; ScheduleRefresh(); return; }
            if (int.TryParse(_seedBox.Text, out var s)) { _userSeed = s; ScheduleRefresh(); }
        };

        // Advanced expander — original four raw knobs preserved verbatim
        // so power users keep everything they had.
        var advanced = new Expander
        {
            Header = "Advanced",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = Brushes.LightGray,
        };
        var advGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            Margin = new Thickness(0, 8, 0, 0),
        };
        AddAdvancedRow(advGrid, 0, "Width (cells)", _widthUpDown,
            "Horizontal extent of the heightmap, in cells. 96 ≈ a medium duel arena.");
        AddAdvancedRow(advGrid, 1, "Height (cells)", _heightUpDown,
            "Vertical extent of the heightmap, in cells.");
        AddAdvancedRow(advGrid, 2, "Complexity (1-5)", _complexityUpDown,
            "Higher = more polygon vertices, more terraces, busier trigger paint.");
        AddAdvancedRow(advGrid, 3, "Relief (height)", _reliefUpDown,
            "Maximum elevation difference in map units. 192 ≈ two stories tall.");
        advanced.Content = advGrid;
        panel.Children.Add(advanced);

        return panel;
    }

    private Control BuildRightColumn()
    {
        var panel = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(0, 0, 0, 0),
        };
        panel.Children.Add(SectionHeader("Preview"));
        // Card with a 1px border around the thumbnail.
        var card = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x3A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x10)),
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = _thumbnail,
        };
        _thumbnail.Width = 256;
        _thumbnail.Height = 256;
        _thumbnail.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(card);
        panel.Children.Add(_blurbText);
        panel.Children.Add(_previewMeta);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var rerollBtn = new Button { Content = "Re-roll preview" };
        ToolTip.SetTip(rerollBtn, "Roll a different seed using the current style and size.");
        rerollBtn.Click += (_, _) =>
        {
            // Re-roll: clear any user seed so the next refresh picks a new
            // random one. The seed text box is cleared too so the user
            // can see it switched modes.
            _userSeed = null;
            _seedBox.Text = string.Empty;
            ScheduleRefresh();
        };
        btnRow.Children.Add(rerollBtn);
        panel.Children.Add(btnRow);
        return panel;
    }

    private Control BuildButtonRow()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(20, 6) };
        cancel.Click += (_, _) => { _tcs.TrySetResult(null); Close(); };
        var generate = new Button
        {
            Content = "Generate",
            Padding = new Thickness(20, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x6A, 0xE0)),
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
        };
        generate.Click += (_, _) => OnGenerateClicked();
        panel.Children.Add(cancel);
        panel.Children.Add(generate);
        Closed += (_, _) => _tcs.TrySetResult(null);
        return panel;
    }

    // ---------------------------------------------------------------------
    // Preset list / size / advanced wiring
    // ---------------------------------------------------------------------

    private void BuildPresetList()
    {
        _presetList.Children.Clear();
        foreach (var p in Presets)
            _presetList.Children.Add(MakePresetRadio(p));
        _presetList.Children.Add(MakeSpecialPresetRadio(SurpriseName,
            "Roll a random style for me each time I re-roll the preview.",
            isSurprise: true));
    }

    private RadioButton MakePresetRadio(StylePreset p)
    {
        var rb = new RadioButton
        {
            GroupName = "preset",
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    new TextBlock
                    {
                        Text = p.Name,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEE)),
                        FontWeight = FontWeight.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = p.Blurb,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA4)),
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
            IsChecked = ReferenceEquals(p, _selectedPreset),
            Padding = new Thickness(6, 4),
            Margin = new Thickness(0, 2, 0, 2),
        };
        rb.Checked += (_, _) =>
        {
            _selectedPreset = p;
            _isCustom = false;
            UpdateBlurb();
            WriteThroughPresetToAdvanced();
            ScheduleRefresh();
        };
        ToolTip.SetTip(rb, p.Blurb);
        return rb;
    }

    private RadioButton MakeSpecialPresetRadio(string name, string blurb, bool isSurprise)
    {
        var rb = new RadioButton
        {
            GroupName = "preset",
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    new TextBlock { Text = name, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEE)), FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = blurb, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA4)), FontSize = 11, TextWrapping = TextWrapping.Wrap },
                },
            },
            Padding = new Thickness(6, 4),
            Margin = new Thickness(0, 2, 0, 2),
        };
        rb.Checked += (_, _) =>
        {
            if (isSurprise)
            {
                _selectedPreset = Presets[_rng.Next(Presets.Length)];
                _isCustom = false;
                UpdateBlurb();
                WriteThroughPresetToAdvanced();
                ScheduleRefresh();
            }
        };
        ToolTip.SetTip(rb, blurb);
        return rb;
    }

    private void OnSizeBucketChanged(SizeBucket b)
    {
        if (_sizeBucket == b && !_isCustom) return;
        _sizeBucket = b;
        if (!_isCustom) WriteThroughPresetToAdvanced();
        UpdateSizeButtons();
        ScheduleRefresh();
    }

    private void UpdateSizeButtons()
    {
        SetSegmentedActive(_btnSizeSmall,  _sizeBucket == SizeBucket.Small);
        SetSegmentedActive(_btnSizeMedium, _sizeBucket == SizeBucket.Medium);
        SetSegmentedActive(_btnSizeLarge,  _sizeBucket == SizeBucket.Large);
    }

    private void UpdateBlurb()
    {
        if (_isCustom)
        {
            _blurbText.Text = $"{CustomName} — using the values you set in Advanced.";
        }
        else
        {
            var p = _selectedPreset;
            _blurbText.Text = p is null ? string.Empty : $"{p.Name} — {p.Blurb}";
        }
    }

    /// <summary>
    /// Push the selected preset's parameters into the Advanced numeric
    /// controls so the dialog reflects the actual values that will be sent
    /// to the generator. Suppresses the user-edit detector so this
    /// programmatic write doesn't flip us back into Custom mode.
    /// </summary>
    private void WriteThroughPresetToAdvanced()
    {
        if (_selectedPreset is null) return;
        _suppressAdvancedEcho = true;
        try
        {
            var (baseW, baseH) = SizeBase[_sizeBucket];
            var w = SnapTo8(baseW * _selectedPreset.SizeMultiplier);
            var h = SnapTo8(baseH * _selectedPreset.SizeMultiplier);
            _widthUpDown.Value      = w;
            _heightUpDown.Value     = h;
            _complexityUpDown.Value = _selectedPreset.Complexity;
            _reliefUpDown.Value     = _selectedPreset.Relief;
        }
        finally { _suppressAdvancedEcho = false; }
    }

    private void WireAdvancedHandlers()
    {
        ToolTip.SetTip(_widthUpDown,      "Horizontal extent of the heightmap, in cells. 96 ≈ a medium duel arena.");
        ToolTip.SetTip(_heightUpDown,     "Vertical extent of the heightmap, in cells.");
        ToolTip.SetTip(_complexityUpDown, "Higher = more polygon vertices, more terraces, busier trigger paint.");
        ToolTip.SetTip(_reliefUpDown,     "Maximum elevation difference in map units. 192 ≈ two stories tall.");
        foreach (var nud in new[] { _widthUpDown, _heightUpDown, _complexityUpDown, _reliefUpDown })
        {
            nud.ValueChanged += (_, _) =>
            {
                if (_suppressAdvancedEcho) return;
                _isCustom = true;
                _selectedPreset = null;
                // Visually deselect all preset radio buttons.
                foreach (var c in _presetList.Children)
                    if (c is RadioButton rb) rb.IsChecked = false;
                UpdateBlurb();
                ScheduleRefresh();
            };
        }
    }

    // ---------------------------------------------------------------------
    // Live preview generation
    // ---------------------------------------------------------------------

    private void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        try
        {
            var opts = ResolveOptions(forPreview: true);
            // Resolve seed: if user typed one, use exactly that; otherwise
            // pick a fresh random seed so re-roll has visible effect.
            var seed = opts.Seed ?? _rng.Next();
            _previewSeed = seed;

            var result = AutoMapGenerator.Generate(new MapSlopperProject(),
                new AutoMapGenerator.Options
                {
                    WidthCells = opts.WidthCells,
                    HeightCells = opts.HeightCells,
                    CellSize = 32.0,
                    Complexity = opts.Complexity,
                    Relief = opts.Relief,
                    Seed = seed,
                });
            _previewProject = result.Project;
            _thumbnail.Bind(result.Project);
            _previewMeta.Text = $"{opts.WidthCells}×{opts.HeightCells} cells • complexity {opts.Complexity} • relief {opts.Relief} • seed {result.SeedUsed}";
        }
        catch (Exception ex)
        {
            _previewProject = null;
            _thumbnail.Bind(null);
            _previewMeta.Text = "Preview failed: " + ex.Message;
        }
    }

    private void OnGenerateClicked()
    {
        var opts = ResolveOptions(forPreview: false);
        // Use the seed that produced the visible preview so what the user
        // sees is what they get. If the user explicitly typed a seed,
        // ResolveOptions will already have written it back; the previewSeed
        // captured in OnRefreshTick is the source of truth otherwise.
        opts.Seed = _userSeed ?? _previewSeed;
        _tcs.TrySetResult(opts);
        Close();
    }

    /// <summary>
    /// Build an <see cref="EditorViewModel.AutoMapOptions"/> from the current
    /// dialog state. When in Custom mode (the user touched the Advanced
    /// fields) we read the NumericUpDowns directly; otherwise we re-derive
    /// from preset+size so size-bucket changes are honoured even when the
    /// preset hasn't been re-checked.
    /// </summary>
    private EditorViewModel.AutoMapOptions ResolveOptions(bool forPreview)
    {
        if (_isCustom || _selectedPreset is null)
        {
            return new EditorViewModel.AutoMapOptions
            {
                WidthCells  = (int)Math.Round(_widthUpDown.Value),
                HeightCells = (int)Math.Round(_heightUpDown.Value),
                Complexity  = (int)Math.Round(_complexityUpDown.Value),
                Relief      = (ushort)Math.Clamp((int)Math.Round(_reliefUpDown.Value), 32, ushort.MaxValue),
                Seed        = _userSeed,
            };
        }

        var (baseW, baseH) = SizeBase[_sizeBucket];
        return new EditorViewModel.AutoMapOptions
        {
            WidthCells  = SnapTo8(baseW * _selectedPreset.SizeMultiplier),
            HeightCells = SnapTo8(baseH * _selectedPreset.SizeMultiplier),
            Complexity  = _selectedPreset.Complexity,
            Relief      = _selectedPreset.Relief,
            Seed        = _userSeed,
        };
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static int SnapTo8(double v)
    {
        var snapped = (int)Math.Round(v / 8.0) * 8;
        return Math.Clamp(snapped, 24, 512);
    }

    private static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xD0)),
        FontWeight = FontWeight.SemiBold,
        FontSize = 13,
    };

    private static Button MakeSegmentedButton(string label) => new()
    {
        Content = label,
        Padding = new Thickness(16, 6),
        Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x2C)),
        Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xDE)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(0),
    };

    private static void SetSegmentedActive(Button b, bool active)
    {
        b.Background = new SolidColorBrush(active
            ? Color.FromRgb(0x1F, 0x6A, 0xE0)
            : Color.FromRgb(0x24, 0x24, 0x2C));
        b.Foreground = new SolidColorBrush(active
            ? Colors.White
            : Color.FromRgb(0xD8, 0xD8, 0xDE));
        b.BorderBrush = new SolidColorBrush(active
            ? Color.FromRgb(0x4A, 0x90, 0xFF)
            : Color.FromRgb(0x3A, 0x3A, 0x42));
    }

    private static void AddAdvancedRow(Grid grid, int row, string label, NumericUpDown editor, string tooltip)
    {
        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC8)),
            Margin = new Thickness(0, 0, 8, 4),
        };
        Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
        Grid.SetRow(editor, row); Grid.SetColumn(editor, 1);
        editor.Margin = new Thickness(0, 0, 0, 4);
        ToolTip.SetTip(lbl, tooltip);
        ToolTip.SetTip(editor, tooltip);
        grid.Children.Add(lbl);
        grid.Children.Add(editor);
    }
}

/// <summary>
/// Tiny custom Avalonia control that paints a heightmap thumbnail plus the
/// outline polygon. Used by the auto-map wizard to give a live preview of
/// the layout that would be generated. Renders on the UI thread because the
/// per-cell loop is cheap (≤ 144×144 = ~20k cells in the worst case the
/// wizard ever asks for).
/// </summary>
internal sealed class MapThumbnailControl : Control
{
    private MapSlopperProject? _project;

    public void Bind(MapSlopperProject? project)
    {
        _project = project;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        var bg = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x10));
        context.FillRectangle(bg, new Rect(bounds.Size));

        if (_project is null) { DrawHint(context, bounds, "no preview"); return; }
        var hm = _project.Heightmap;
        if (hm.Width <= 0 || hm.Height <= 0) { DrawHint(context, bounds, "empty heightmap"); return; }

        // Compute scale so the heightmap fits centred inside our bounds.
        var sx = bounds.Width  / hm.Width;
        var sy = bounds.Height / hm.Height;
        var s  = Math.Min(sx, sy);
        var rw = hm.Width  * s;
        var rh = hm.Height * s;
        var ox = (bounds.Width  - rw) * 0.5;
        var oy = (bounds.Height - rh) * 0.5;

        // Find min/max non-zero height for normalisation. Cells at 0 are
        // unpainted (outside any floor) so we render them flat-dark
        // separately from the in-bounds ramp.
        ushort lo = ushort.MaxValue, hi = 0;
        for (var i = 0; i < hm.Data.Length; i++)
        {
            var v = hm.Data[i];
            if (v == 0) continue;
            if (v < lo) lo = v;
            if (v > hi) hi = v;
        }
        var span = hi > lo ? (double)(hi - lo) : 1.0;

        // Cache 256 grayscale brushes so we don't allocate one per cell.
        var brushes = new IBrush?[256];
        var emptyBrush = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x1B));

        for (var y = 0; y < hm.Height; y++)
        {
            for (var x = 0; x < hm.Width; x++)
            {
                var v = hm[x, y];
                IBrush brush;
                if (v == 0)
                {
                    brush = emptyBrush;
                }
                else
                {
                    var t = lo == hi ? 0.5 : (v - lo) / span;
                    var b = (byte)Math.Clamp(40 + (int)(t * 215), 0, 255);
                    var slot = brushes[b];
                    if (slot is null) brushes[b] = slot = new SolidColorBrush(Color.FromRgb(b, b, b));
                    brush = slot;
                }
                // Y is flipped so 0 is at the bottom (matches the editor).
                var yScreen = oy + (hm.Height - 1 - y) * s;
                var rect = new Rect(ox + x * s, yScreen, s + 0.5, s + 0.5);
                context.FillRectangle(brush, rect);
            }
        }

        // Outline polygon on top.
        var outline = _project.Outline;
        if (outline.Points.Count >= 2)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xFF)), 1.5);

            foreach (var edge in outline.Edges)
            {
                if (!outline.Points.TryGetValue(edge.A, out var pa)) continue;
                if (!outline.Points.TryGetValue(edge.B, out var pb)) continue;
                var (cax, cay) = hm.WorldToCell(pa.Position);
                var (cbx, cby) = hm.WorldToCell(pb.Position);
                var sax = ox + cax * s;
                var say = oy + (hm.Height - 1 - cay) * s;
                var sbx = ox + cbx * s;
                var sby = oy + (hm.Height - 1 - cby) * s;
                context.DrawLine(pen, new Point(sax, say), new Point(sbx, sby));
            }
        }

        // Subtle border around the rendered area.
        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x3A)), 1);
        context.DrawRectangle(borderPen, new Rect(ox, oy, rw, rh));
    }

    private static void DrawHint(DrawingContext context, Rect bounds, string text)
    {
        var ft = new FormattedText(text, new Typeface("Segoe UI"),
            12, TextAlignment.Center, TextWrapping.NoWrap, bounds.Size);
        var textColor = new SolidColorBrush(Color.FromRgb(0x6F, 0x6F, 0x7A));
        context.DrawText(textColor,
            new Point(bounds.Width / 2 - ft.Bounds.Width / 2,
                     bounds.Height / 2 - ft.Bounds.Height / 2),
            ft);
    }
}

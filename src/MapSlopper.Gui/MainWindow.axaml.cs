using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using MapSlopper.Core.Triggers;
using MapSlopper.Gui.Tools;

namespace MapSlopper.Gui;

public class MainWindow : Window
{
    private EditorViewModel _vm = null!;
    private Editor2DControl _canvas = null!;
    private Preview3DControl _preview = null!;
    private readonly Button[] _toolButtons = new Button[8];

    private TextBlock _statusText = null!;
    private TextBlock _undoState = null!;
    private TextBlock _closedState = null!;
    private TextBlock _activeToolText = null!;
    private NumericUpDown _brushSize = null!;
    private NumericUpDown _paintValue = null!;
    private HeightHistogramControl _histogram = null!;
    private Slider _levelsMin = null!;
    private Slider _levelsMax = null!;
    private TextBlock _levelsMinText = null!;
    private TextBlock _levelsMaxText = null!;
    private CheckBox _snapCheck = null!;
    private TextBlock _toolHint = null!;
    private TextBlock _triggerPaletteHeader = null!;
    private StackPanel _triggerPalette = null!;
    private bool _suppressClosePrompt;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        BuildViewModel();
        WireControls();
        WireMenus();
        WireKeyboard();
        Closing += OnClosingAsync;
    }

    private void BuildViewModel()
    {
        _vm = new EditorViewModel();
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.Undo.Changed += OnUndoChanged;
        _vm.RepaintRequested += UpdateBottomBar;
    }

    private void WireControls()
    {
        _canvas = this.FindControl<Editor2DControl>("Canvas")!;
        _preview = this.FindControl<Preview3DControl>("Preview3D")!;
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _undoState = this.FindControl<TextBlock>("UndoState")!;
        _closedState = this.FindControl<TextBlock>("ClosedState")!;
        _activeToolText = this.FindControl<TextBlock>("ActiveToolText")!;
        _brushSize = this.FindControl<NumericUpDown>("BrushSize")!;
        _paintValue = this.FindControl<NumericUpDown>("PaintValue")!;
        _histogram = this.FindControl<HeightHistogramControl>("HeightHistogram")!;
        _levelsMin = this.FindControl<Slider>("LevelsMinSlider")!;
        _levelsMax = this.FindControl<Slider>("LevelsMaxSlider")!;
        _levelsMinText = this.FindControl<TextBlock>("LevelsMinText")!;
        _levelsMaxText = this.FindControl<TextBlock>("LevelsMaxText")!;
        _snapCheck = this.FindControl<CheckBox>("SnapCheck")!;
        _toolHint = this.FindControl<TextBlock>("ToolHintText")!;
        _triggerPaletteHeader = this.FindControl<TextBlock>("TriggerPaletteHeader")!;
        _triggerPalette = this.FindControl<StackPanel>("TriggerPalette")!;

        _canvas.SetViewModel(_vm);
        _preview.Bind(_vm);
        _histogram.Bind(_vm);

        // Levels sliders: control the heightmap display gamma. Two-way wired
        // to vm.Levels and the histogram control so the user can quickly
        // dial in contrast against the live histogram.
        _levelsMin.Value = _vm.Levels.DisplayMin;
        _levelsMax.Value = _vm.Levels.DisplayMax;
        UpdateLevelsText();
        _levelsMin.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            var lo = (ushort)Math.Clamp((int)Math.Round(_levelsMin.Value), 0, 65535);
            if (lo >= _vm.Levels.DisplayMax) lo = (ushort)Math.Max(0, _vm.Levels.DisplayMax - 1);
            _vm.Levels.DisplayMin = lo;
            UpdateLevelsText();
            _histogram.InvalidateVisual();
            _canvas.InvalidateVisual();
        };
        _levelsMax.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            var hi = (ushort)Math.Clamp((int)Math.Round(_levelsMax.Value), 0, 65535);
            if (hi <= _vm.Levels.DisplayMin) hi = (ushort)Math.Min(65535, _vm.Levels.DisplayMin + 1);
            _vm.Levels.DisplayMax = hi;
            UpdateLevelsText();
            _histogram.InvalidateVisual();
            _canvas.InvalidateVisual();
        };

        _snapCheck.IsChecked = _vm.SnapToGrid;
        _snapCheck.Checked += (_, _) => _vm.SnapToGrid = true;
        _snapCheck.Unchecked += (_, _) => _vm.SnapToGrid = false;

        _brushSize.ValueChanged += (_, e) => _vm.BrushSizeCells = (int)Math.Max(1, (int)e.NewValue);
        _paintValue.ValueChanged += (_, e) =>
        {
            var v = e.NewValue;
            if (v < 0) v = 0;
            if (v > ushort.MaxValue) v = ushort.MaxValue;
            _vm.PaintValue = (ushort)v;
        };

        WireToolButton("ToolBtn1", 0);
        WireToolButton("ToolBtn2", 1);
        WireToolButton("ToolBtn3", 2);
        WireToolButton("ToolBtn4", 3);
        WireToolButton("ToolBtn5", 4);
        WireToolButton("ToolBtn6", 5);
        WireToolButton("ToolBtn7", 6);
        WireToolButton("ToolBtn8", 7);
        BuildTriggerPalette();

        // Force the 3D preview to refresh whenever its tab becomes
        // selected. Without this, an asset-root add or heightmap edit
        // performed while the 2D tab was active wouldn't pick up until
        // the user did something else inside the 3D pane.
        var tabs = this.FindControl<TabControl>("ViewTabs");
        var tab3D = this.FindControl<TabItem>("Tab3D");
        if (tabs is not null && tab3D is not null)
        {
            tabs.SelectionChanged += (_, _) =>
            {
                if (ReferenceEquals(tabs.SelectedItem, tab3D))
                    _preview.ForceRefresh();
            };
        }

        UpdateBottomBar();
        UpdateActiveToolText();
        UpdateToolButtons(0); // highlight first tool on startup

        Title = _vm.WindowTitle;
        // FrameProject is now triggered by Editor2DControl.OnAttachedToVisualTree
        // so Bounds are guaranteed to be valid.
    }

    private void UpdateLevelsText()
    {
        if (_levelsMinText is not null) _levelsMinText.Text = _vm.Levels.DisplayMin.ToString();
        if (_levelsMaxText is not null) _levelsMaxText.Text = _vm.Levels.DisplayMax.ToString();
    }

    private void WireToolButton(string name, int index)
    {
        var btn = this.FindControl<Button>(name);
        if (btn is null) return;
        _toolButtons[index] = btn;
        btn.Click += (_, _) => SelectTool(index);
    }

    private void SelectTool(int index)
    {
        if (index < 0 || index >= _vm.Tools.Length) return;
        _vm.ActiveTool = _vm.Tools[index];
        UpdateActiveToolText();
        UpdateToolButtons(index);
    }

    private void UpdateToolButtons(int activeIndex)
    {
        for (var i = 0; i < _toolButtons.Length; i++)
        {
            var b = _toolButtons[i];
            if (b is null) continue;
            if (i == activeIndex)
            {
                if (!b.Classes.Contains("active")) b.Classes.Add("active");
            }
            else
            {
                b.Classes.Remove("active");
            }
        }
    }

    private void UpdateActiveToolText()
    {
        _activeToolText.Text = _vm.ActiveTool.Name;
        if (_toolHint is not null) _toolHint.Text = _vm.ActiveTool.StatusHint(_vm) ?? string.Empty;
        UpdateTriggerPaletteVisibility();
    }

    private void UpdateTriggerPaletteVisibility()
    {
        var visible = _vm.ActiveTool is TriggerBrushTool;
        if (_triggerPalette is not null) _triggerPalette.IsVisible = visible;
        if (_triggerPaletteHeader is not null) _triggerPaletteHeader.IsVisible = visible;
    }

    private void BuildTriggerPalette()
    {
        if (_triggerPalette is null) return;
        _triggerPalette.Children.Clear();
        foreach (var t in _vm.TriggerTypes.Types)
        {
            var typeId = t.Id; // closure capture
            var color = TriggerBrushTool.ParseColor(t.ColorHex) ?? Colors.Gray;
            var swatch = new Border
            {
                Width = 20,
                Height = 20,
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)),
                BorderThickness = new Avalonia.Thickness(1),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            var label = new TextBlock
            {
                Text = $"{t.Id}: {t.Name}",
                Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(8, 0, 0, 0),
            };
            var row = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Children = { swatch, label },
            };
            var btn = new Button
            {
                Content = row,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)),
                BorderThickness = new Avalonia.Thickness(1),
                Padding = new Avalonia.Thickness(4, 3),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            };
            btn.Click += (_, _) =>
            {
                _vm.ActiveTriggerTypeId = typeId;
                // Auto-switch to the trigger brush tool so clicking a swatch
                // is a one-click "paint this color" action.
                var idx = Array.IndexOf(_vm.Tools, FindTool<TriggerBrushTool>());
                if (idx >= 0) SelectTool(idx);
                HighlightTriggerPaletteSelection();
            };
            _triggerPalette.Children.Add(btn);
        }
        HighlightTriggerPaletteSelection();
    }

    private IEditorTool? FindTool<T>() where T : IEditorTool
    {
        foreach (var t in _vm.Tools) if (t is T) return t;
        return null;
    }

    private void HighlightTriggerPaletteSelection()
    {
        if (_triggerPalette is null) return;
        for (var i = 0; i < _triggerPalette.Children.Count && i < _vm.TriggerTypes.Types.Count; i++)
        {
            if (_triggerPalette.Children[i] is not Button b) continue;
            var isActive = _vm.TriggerTypes.Types[i].Id == _vm.ActiveTriggerTypeId;
            b.BorderBrush = new SolidColorBrush(isActive
                ? Color.FromRgb(0x1F, 0x8F, 0xE6)
                : Color.FromRgb(0x3A, 0x3A, 0x42));
            b.BorderThickness = new Avalonia.Thickness(isActive ? 2 : 1);
        }
    }

    private void WireMenus()
    {
        Hook("MenuNew", _ => OnNew());
        Hook("MenuAutoMap", async _ => await OnAutoMapAsync().ConfigureAwait(true));
        Hook("MenuOpen", async _ => await _vm.OpenAsync(this).ConfigureAwait(true));
        Hook("MenuSave", async _ => await _vm.SaveAsync(this).ConfigureAwait(true));
        Hook("MenuSaveAs", async _ => await _vm.SaveAsAsync(this).ConfigureAwait(true));
        Hook("MenuExport", async _ => await _vm.ExportMapAsync(this).ConfigureAwait(true));
        Hook("MenuExit", _ => { _suppressClosePrompt = false; Close(); });
        Hook("MenuUndo", _ => _vm.UndoAction());
        Hook("MenuRedo", _ => _vm.RedoAction());
        Hook("MenuLevels", _ => OpenLevelsWindow());
        Hook("MenuFrame", _ => _canvas.FrameProject());
        Hook("MenuAddAssetRoot", async _ => await OnAddAssetRootAsync().ConfigureAwait(true));
        Hook("MenuAddAssetPk3", async _ => await OnAddAssetPk3Async().ConfigureAwait(true));
        Hook("MenuClearAssetRoots", _ => OnClearAssetRoots());
        Hook("MenuAbout", _ => ShowAbout());
    }

    private void Hook(string name, Action<RoutedEventArgs> handler)
    {
        var item = this.FindControl<MenuItem>(name);
        if (item is null) return;
        item.Click += (_, e) => handler(e);
    }

    private void WireKeyboard()
    {
        AddHandler(KeyDownEvent, OnKeyDownHandler, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private async void OnKeyDownHandler(object? sender, KeyEventArgs e)
    {
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (ctrl && e.Key == Key.Z && !shift) { _vm.UndoAction(); e.Handled = true; return; }
        if ((ctrl && e.Key == Key.Y) || (ctrl && shift && e.Key == Key.Z)) { _vm.RedoAction(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.S && !shift) { await _vm.SaveAsync(this).ConfigureAwait(true); e.Handled = true; return; }
        if (ctrl && shift && e.Key == Key.S) { await _vm.SaveAsAsync(this).ConfigureAwait(true); e.Handled = true; return; }
        if (ctrl && e.Key == Key.O) { await _vm.OpenAsync(this).ConfigureAwait(true); e.Handled = true; return; }
        if (ctrl && e.Key == Key.N) { OnNew(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.E) { await _vm.ExportMapAsync(this).ConfigureAwait(true); e.Handled = true; return; }

        // Skip single-key tool shortcuts and Frame shortcut when a text input has focus
        // (otherwise typing in NumericUpDown / TextBox steals our key).
        if (IsTextInputFocused()) return;

        if (!ctrl && !shift)
        {
            switch (e.Key)
            {
                case Key.D1: case Key.NumPad1: SelectTool(0); e.Handled = true; break;
                case Key.D2: case Key.NumPad2: SelectTool(1); e.Handled = true; break;
                case Key.D3: case Key.NumPad3: SelectTool(2); e.Handled = true; break;
                case Key.D4: case Key.NumPad4: SelectTool(3); e.Handled = true; break;
                case Key.D5: case Key.NumPad5: SelectTool(4); e.Handled = true; break;
                case Key.D6: case Key.NumPad6: SelectTool(5); e.Handled = true; break;
                case Key.D7: case Key.NumPad7: SelectTool(6); e.Handled = true; break;
                case Key.D8: case Key.NumPad8: SelectTool(7); e.Handled = true; break;
                case Key.Escape:
                    _vm.ActiveTool.Reset();
                    _vm.SelectedPointId = null;
                    _vm.StatusMessage = $"{_vm.ActiveTool.Name}: reset.";
                    e.Handled = true;
                    break;
                case Key.G:
                    _vm.SnapToGrid = !_vm.SnapToGrid;
                    _vm.StatusMessage = "Snap to grid: " + (_vm.SnapToGrid ? "ON" : "OFF");
                    e.Handled = true;
                    break;
                case Key.F:
                    if (IsPreview3DFocused()) _preview.FrameNow();
                    else _canvas.FrameProject();
                    e.Handled = true;
                    break;
            }
        }
    }

    private bool IsTextInputFocused()
    {
        var f = FocusManager.Instance?.Current;
        if (f is not IControl c) return false;
        IControl? cur = c;
        while (cur is not null)
        {
            switch (cur)
            {
                case TextBox _:
                case NumericUpDown _:
                    return true;
            }
            cur = cur.Parent as IControl;
        }
        return false;
    }

    private bool IsPreview3DFocused()
    {
        var f = FocusManager.Instance?.Current;
        IControl? cur = f as IControl;
        while (cur is not null)
        {
            if (cur is Preview3DControl) return true;
            cur = cur.Parent as IControl;
        }
        return false;
    }

    private void OnNew()
    {
        // For "New" we discard current state (mirrors discard semantics; user
        // already gets a Save? prompt on Quit which is the requirement spec).
        _vm.NewProject();
        _canvas.FrameProject();
    }

    private async Task OnAutoMapAsync()
    {
        var opts = await AutoMapWizardWindow.ShowAsync(this).ConfigureAwait(true);
        if (opts is null) return;
        try
        {
            _vm.GenerateAutoMap(opts);
            _canvas.FrameProject();
            _preview.ReloadAssets();
            _preview.FrameNow();
        }
        catch (Exception ex)
        {
            _vm.StatusMessage = "Automatic generation failed: " + ex.Message;
        }
    }

    private void OpenLevelsWindow()
    {
        var w = new LevelsWindow(_vm);
        w.Show(this);
    }

    private async void ShowAbout()
    {
        var w = new Window
        {
            Title = "About MapSlopper",
            Width = 360,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock
            {
                Text = "MapSlopper — 2D map editor for Quake 3.\nPhase 8 GUI build (Avalonia 0.10 / .NET 5).",
                Margin = new Avalonia.Thickness(20),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            },
        };
        await w.ShowDialog(this).ConfigureAwait(true);
    }

    private async Task OnAddAssetRootAsync()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Pick a baseq3, GameData/base, or other Q3-style content folder",
        };
        var picked = await dlg.ShowAsync(this).ConfigureAwait(true);
        if (string.IsNullOrEmpty(picked)) return;

        // Auto-correct common picks: parent of baseq3, parent of GameData,
        // etc. so the user doesn't have to know exactly which level the
        // shader/texture trees live at.
        var resolved = AssetRootHelper.ResolvePickedDirectory(picked);
        if (_vm.Project.AssetRoots.Contains(resolved))
        {
            _vm.StatusMessage = "Asset root already added.";
            return;
        }

        _vm.Project.AssetRoots.Add(resolved);
        _preview.ReloadAssets();
        await ShowAssetRootResultAsync(resolved, picked).ConfigureAwait(true);
    }

    private async Task OnAddAssetPk3Async()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose a .pk3 asset archive",
            AllowMultiple = false,
            Filters = new System.Collections.Generic.List<FileDialogFilter>
            {
                new() { Name = "Quake 3 PK3", Extensions = new System.Collections.Generic.List<string> { "pk3" } },
            },
        };
        var picked = await dlg.ShowAsync(this).ConfigureAwait(true);
        if (picked is null || picked.Length == 0) return;
        var path = picked[0];
        if (_vm.Project.AssetRoots.Contains(path)) { _vm.StatusMessage = "PK3 already added."; return; }
        _vm.Project.AssetRoots.Add(path);
        _preview.ReloadAssets();
        await ShowAssetRootResultAsync(path, path).ConfigureAwait(true);
    }

    private void OnClearAssetRoots()
    {
        if (_vm.Project.AssetRoots.Count == 0) { _vm.StatusMessage = "No asset roots."; return; }
        _vm.Project.AssetRoots.Clear();
        _preview.ReloadAssets();
        _vm.StatusMessage = "Asset roots cleared.";
    }

    /// <summary>
    /// Shows a structured feedback dialog after an asset-root add so the
    /// user knows whether the new root actually contributed shaders and
    /// whether the *current project's* textures (floor / wall / ceiling /
    /// window) resolve against it. Replaces the silent
    /// <c>StatusMessage = "Asset root added"</c> behaviour that left users
    /// guessing whether anything happened.
    /// </summary>
    private Task ShowAssetRootResultAsync(string resolvedPath, string originallyPicked)
    {
        var assets = _preview.Assets;
        var lib = MapSlopper.Core.Assets.AssetLibrary.Load(new[] { resolvedPath });
        var addedShaders = lib.ShaderCount;

        // Probe each project texture to tell the user "your project uses
        // these four shaders; here are the ones this root resolves".
        var probes = new (string Label, string Name)[]
        {
            ("Floor",   _vm.Project.FloorTexture),
            ("Wall",    _vm.Project.WallTexture),
            ("Ceiling", _vm.Project.CeilingTexture),
            ("Window",  _vm.Project.WindowTexture),
        };

        var w = new Window
        {
            Title = "Asset root added",
            Width = 460,
            Height = 380,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1F)),
        };

        var stack = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 10 };

        stack.Children.Add(new TextBlock
        {
            Text = addedShaders > 0 ? "Asset root mounted" : "Asset root mounted (no shaders found)",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEE)),
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            FontSize = 16,
        });

        var pathText = string.Equals(resolvedPath, originallyPicked, StringComparison.OrdinalIgnoreCase)
            ? resolvedPath
            : $"{resolvedPath}\n(auto-detected from {originallyPicked})";
        stack.Children.Add(new TextBlock
        {
            Text = pathText,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xB3)),
            FontSize = 12,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        stack.Children.Add(new TextBlock
        {
            Text = $"Indexed {addedShaders} shader definition{(addedShaders == 1 ? "" : "s")} from this root.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            Margin = new Avalonia.Thickness(0, 6, 0, 0),
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Project textures resolved (combined with bundled fallback):",
            Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xB3)),
            FontSize = 12,
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
        });

        foreach (var (label, name) in probes)
        {
            var resolved = assets.Resolve(name);
            var row = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 10,
                Margin = new Avalonia.Thickness(0, 2, 0, 0),
            };
            // Color swatch: 1x1 RGBA decode for the shader's intended color.
            var swatchColor = ColorFromResolved(resolved);
            row.Children.Add(new Border
            {
                Width = 22, Height = 22,
                Background = new SolidColorBrush(swatchColor),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(3),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });
            var status = resolved is null ? "unresolved (will use fallback tint)" :
                         resolved.Width == 1 && resolved.Height == 1 ? $"flat color ({resolved.ResolvedPath})" :
                         $"texture {resolved.Width}×{resolved.Height} ({resolved.ResolvedPath})";
            row.Children.Add(new TextBlock
            {
                Text = $"{label}: {name}",
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEE)),
                Width = 170,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock
            {
                Text = status,
                Foreground = new SolidColorBrush(resolved is null
                    ? Color.FromRgb(0x6F, 0x6F, 0x7A)
                    : Color.FromRgb(0x3F, 0xCB, 0x8E)),
                FontSize = 11,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });
            stack.Children.Add(row);
        }

        var btnRow = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 14, 0, 0),
        };
        var ok = new Button { Content = "OK", Padding = new Avalonia.Thickness(20, 4) };
        ok.Click += (_, _) => w.Close();
        btnRow.Children.Add(ok);
        stack.Children.Add(btnRow);

        w.Content = stack;
        _vm.StatusMessage = $"Asset root added: {resolvedPath} ({addedShaders} shaders)";
        return w.ShowDialog(this);
    }

    /// <summary>
    /// Decode a <see cref="MapSlopper.Core.Assets.AssetLibrary.ResolvedTexture"/> into
    /// an Avalonia color for swatch display. Falls back to a neutral gray
    /// when the texture is encoded (PNG/JPG bytes, expensive to decode
    /// for a single swatch — Skia could decode but the swatch isn't
    /// load-bearing visually).
    /// </summary>
    private static Color ColorFromResolved(MapSlopper.Core.Assets.AssetLibrary.ResolvedTexture? r)
    {
        if (r is null) return Color.FromRgb(0x33, 0x33, 0x3A);
        if (r.Rgba is { Length: >= 4 })
        {
            // For small textures (1x1 synth), the average is the same as
            // the single pixel; for larger we sample the centre.
            var pix = (r.Width * (r.Height / 2) + r.Width / 2) * 4;
            if (pix + 3 >= r.Rgba.Length) pix = 0;
            return Color.FromRgb(r.Rgba[pix], r.Rgba[pix + 1], r.Rgba[pix + 2]);
        }
        return Color.FromRgb(0x55, 0x55, 0x60);
    }

    private async void OnClosingAsync(object? sender, CancelEventArgs e)
    {
        if (_suppressClosePrompt) return;
        if (!_vm.IsDirty) return;
        e.Cancel = true;
        var choice = await PromptUnsavedChangesAsync().ConfigureAwait(true);
        if (choice == UnsavedChoice.Cancel) return;
        if (choice == UnsavedChoice.Save)
        {
            var saved = await _vm.SaveAsync(this).ConfigureAwait(true);
            if (!saved) return;
        }
        _suppressClosePrompt = true;
        Close();
    }

    private enum UnsavedChoice { Save, Discard, Cancel }

    private Task<UnsavedChoice> PromptUnsavedChangesAsync()
    {
        var tcs = new TaskCompletionSource<UnsavedChoice>();
        var dlg = new Window
        {
            Title = "Unsaved changes",
            Width = 360,
            Height = 140,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Save changes to the current project before closing?", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var saveBtn = new Button { Content = "Save" };
        var discardBtn = new Button { Content = "Discard" };
        var cancelBtn = new Button { Content = "Cancel" };
        saveBtn.Click += (_, _) => { tcs.TrySetResult(UnsavedChoice.Save); dlg.Close(); };
        discardBtn.Click += (_, _) => { tcs.TrySetResult(UnsavedChoice.Discard); dlg.Close(); };
        cancelBtn.Click += (_, _) => { tcs.TrySetResult(UnsavedChoice.Cancel); dlg.Close(); };
        row.Children.Add(saveBtn);
        row.Children.Add(discardBtn);
        row.Children.Add(cancelBtn);
        panel.Children.Add(row);
        dlg.Content = panel;
        dlg.Closed += (_, _) => tcs.TrySetResult(UnsavedChoice.Cancel);
        _ = dlg.ShowDialog(this);
        return tcs.Task;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.WindowTitle))
            Title = _vm.WindowTitle;
        if (e.PropertyName == nameof(EditorViewModel.ActiveTool))
            UpdateActiveToolText();
        if (e.PropertyName == nameof(EditorViewModel.StatusMessage))
            _statusText.Text = _vm.StatusMessage;
        if (e.PropertyName == nameof(EditorViewModel.TriggerTypes))
            BuildTriggerPalette();
        if (e.PropertyName == nameof(EditorViewModel.ActiveTriggerTypeId))
            HighlightTriggerPaletteSelection();
    }

    private void OnUndoChanged() => UpdateBottomBar();

    private void UpdateBottomBar()
    {
        _undoState.Text = $"Undo:{(_vm.Undo.CanUndo ? _vm.Undo.UndoCount : 0)} Redo:{(_vm.Undo.CanRedo ? _vm.Undo.RedoCount : 0)}";
        _closedState.Text = "Closed: " + (_vm.IsClosedPolygon ? "yes (leak-free)" : "no");
        _statusText.Text = _vm.StatusMessage;
    }
}

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
        var dlg = new OpenFolderDialog { Title = "Choose an asset root folder (e.g. baseq3)" };
        var picked = await dlg.ShowAsync(this).ConfigureAwait(true);
        if (string.IsNullOrEmpty(picked)) return;
        if (_vm.Project.AssetRoots.Contains(picked)) { _vm.StatusMessage = "Asset root already added."; return; }
        _vm.Project.AssetRoots.Add(picked);
        _preview.ReloadAssets();
        _vm.StatusMessage = $"Asset root added: {picked}";
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
        _vm.StatusMessage = $".pk3 added: {path}";
    }

    private void OnClearAssetRoots()
    {
        if (_vm.Project.AssetRoots.Count == 0) { _vm.StatusMessage = "No asset roots."; return; }
        _vm.Project.AssetRoots.Clear();
        _preview.ReloadAssets();
        _vm.StatusMessage = "Asset roots cleared.";
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

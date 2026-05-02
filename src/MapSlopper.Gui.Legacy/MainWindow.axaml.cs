using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using MapSlopper.Core.Export;
using MapSlopper.Core.Generation;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;
using MapSlopper.Core.Project;
using MapSlopper.Core.Undo;

namespace MapSlopper.Gui.Legacy;

public partial class MainWindow : Window
{
    private EditorViewModel _vm = null!;
    private Editor2DControl _canvas = null!;
    private readonly Button[] _toolButtons = new Button[8];

    private TextBlock _statusText = null!;
    private TextBlock _undoState = null!;
    private TextBlock _activeToolText = null!;
    private NumericUpDown _brushSize = null!;
    private NumericUpDown _paintValue = null!;
    private CheckBox _snapCheck = null!;
    private TextBlock _toolHint = null!;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        BuildViewModel();
        WireControls();
        WireMenus();
        WireKeyboard();
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
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _undoState = this.FindControl<TextBlock>("UndoState")!;
        _activeToolText = this.FindControl<TextBlock>("ActiveToolText")!;
        _brushSize = this.FindControl<NumericUpDown>("BrushSize")!;
        _paintValue = this.FindControl<NumericUpDown>("PaintValue")!;
        _snapCheck = this.FindControl<CheckBox>("SnapCheck")!;
        _toolHint = this.FindControl<TextBlock>("ToolHintText")!;

        _canvas.SetViewModel(_vm);

        _snapCheck.IsChecked = _vm.SnapToGrid;
        _snapCheck.Checked += (_, _) => _vm.SnapToGrid = true;
        _snapCheck.Unchecked += (_, _) => _vm.SnapToGrid = false;

        _brushSize.ValueChanged += (_, e) =>
        {
            _vm.BrushSizeCells = (int)Math.Max(1, _brushSize.Value);
        };
        _paintValue.ValueChanged += (_, e) =>
        {
            var v = _paintValue.Value;
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

        UpdateBottomBar();
        UpdateActiveToolText();
        UpdateToolButtons(0);
        Title = _vm.WindowTitle;
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
            b.Background = i == activeIndex
                ? new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC))
                : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x37));
            b.Foreground = Brushes.White;
        }
    }

    private void UpdateActiveToolText()
    {
        _activeToolText.Text = _vm.ActiveTool.Name;
        if (_toolHint is not null) _toolHint.Text = _vm.ActiveTool.StatusHint(_vm) ?? string.Empty;
    }

    private void WireMenus()
    {
        Hook("MenuNew", _ => OnNew());
        Hook("MenuOpen", async _ => await OnOpenAsync());
        Hook("MenuSave", async _ => await OnSaveAsync());
        Hook("MenuSaveAs", async _ => await OnSaveAsAsync());
        Hook("MenuExport", async _ => await OnExportAsync());
        Hook("MenuExit", _ => Close());
        Hook("MenuUndo", _ => _vm.UndoAction());
        Hook("MenuRedo", _ => _vm.RedoAction());
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
        AddHandler(KeyDownEvent, OnKeyDownHandler, RoutingStrategies.Tunnel);
    }

    private async void OnKeyDownHandler(object? sender, KeyEventArgs e)
    {
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        if (ctrl && e.Key == Key.Z) { _vm.UndoAction(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Y) { _vm.RedoAction(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.S) { await OnSaveAsync(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.O) { await OnOpenAsync(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.N) { OnNew(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.E) { await OnExportAsync(); e.Handled = true; return; }

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
                _snapCheck.IsChecked = _vm.SnapToGrid;
                _vm.StatusMessage = "Snap to grid: " + (_vm.SnapToGrid ? "ON" : "OFF");
                e.Handled = true;
                break;
            case Key.F:
                _canvas.FrameProject();
                e.Handled = true;
                break;
        }
    }

    private void OnNew()
    {
        _vm.NewProject();
        _canvas.FrameProject();
    }

    private async Task OnOpenAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open project",
            Filters = new System.Collections.Generic.List<FileDialogFilter>
            {
                new FileDialogFilter { Name = "MapSlopper project", Extensions = { "json" } },
                new FileDialogFilter { Name = "All files", Extensions = { "*" } },
            },
        };
        var result = await dlg.ShowAsync(this);
        if (result is null || result.Length == 0) return;
        try
        {
            var loaded = ProjectJsonIo.Load(result[0]);
            _vm.SetProject(loaded, result[0]);
            _canvas.FrameProject();
        }
        catch (Exception ex) { _vm.StatusMessage = "Open failed: " + ex.Message; }
    }

    private async Task OnSaveAsync()
    {
        if (string.IsNullOrEmpty(_vm.CurrentFilePath))
        {
            await OnSaveAsAsync();
            return;
        }
        try
        {
            ProjectJsonIo.Save(_vm.Project, _vm.CurrentFilePath!);
            _vm.IsDirty = false;
            _vm.StatusMessage = $"Saved {System.IO.Path.GetFileName(_vm.CurrentFilePath)}.";
        }
        catch (Exception ex) { _vm.StatusMessage = "Save failed: " + ex.Message; }
    }

    private async Task OnSaveAsAsync()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save project as",
            DefaultExtension = "json",
            InitialFileName = System.IO.Path.GetFileName(_vm.CurrentFilePath) ?? "untitled.mapsproj.json",
            Filters = new System.Collections.Generic.List<FileDialogFilter>
            {
                new FileDialogFilter { Name = "MapSlopper project", Extensions = { "json" } },
            },
        };
        var path = await dlg.ShowAsync(this);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            ProjectJsonIo.Save(_vm.Project, path);
            _vm.CurrentFilePath = path;
            _vm.IsDirty = false;
            _vm.StatusMessage = $"Saved {System.IO.Path.GetFileName(path)}.";
        }
        catch (Exception ex) { _vm.StatusMessage = "Save failed: " + ex.Message; }
    }

    private async Task OnExportAsync()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export Quake .map",
            DefaultExtension = "map",
            InitialFileName = System.IO.Path.GetFileNameWithoutExtension(_vm.CurrentFilePath ?? "untitled") + ".map",
            Filters = new System.Collections.Generic.List<FileDialogFilter>
            {
                new FileDialogFilter { Name = "Quake .map", Extensions = { "map" } },
            },
        };
        var path = await dlg.ShowAsync(this);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var result = GeometryGenerator.Generate(_vm.Project);
            if (!result.Ok || result.Document is null)
            {
                _vm.StatusMessage = "Export failed: " + (result.Issues.Count > 0 ? result.Issues[0].Message : "unknown");
                return;
            }
            MapWriter.WriteToFile(result.Document, path);
            _vm.StatusMessage = $"Exported {System.IO.Path.GetFileName(path)} ({result.Document.Worldspawn.Brushes.Count} brushes).";
        }
        catch (Exception ex) { _vm.StatusMessage = "Export failed: " + ex.Message; }
    }

    private async void ShowAbout()
    {
        var w = new Window
        {
            Title = "About MapSlopper Legacy",
            Width = 360, Height = 160, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock
            {
                Text = "MapSlopper Legacy \u2014 2D map editor for Quake 3.\n.NET 5 / Avalonia 0.10 build.\nFor Win7 / VS2017 compatibility.",
                Margin = new Thickness(20),
                TextWrapping = TextWrapping.Wrap,
            },
        };
        await w.ShowDialog(this);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.WindowTitle))
            Title = _vm.WindowTitle;
        if (e.PropertyName == nameof(EditorViewModel.StatusMessage))
            _statusText.Text = _vm.StatusMessage;
    }

    private void OnUndoChanged() => UpdateBottomBar();

    private void UpdateBottomBar()
    {
        _undoState.Text = $"Undo:{(_vm.Undo.CanUndo ? _vm.Undo.UndoCount : 0)} Redo:{(_vm.Undo.CanRedo ? _vm.Undo.RedoCount : 0)}";
        _statusText.Text = _vm.StatusMessage;
    }
}

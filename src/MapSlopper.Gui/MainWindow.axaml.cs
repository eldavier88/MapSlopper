using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MapSlopper.Gui.Tools;

namespace MapSlopper.Gui;

public class MainWindow : Window
{
    private EditorViewModel _vm = null!;
    private Editor2DControl _canvas = null!;

    private TextBlock _statusText = null!;
    private TextBlock _undoState = null!;
    private TextBlock _closedState = null!;
    private TextBlock _activeToolText = null!;
    private NumericUpDown _brushSize = null!;
    private NumericUpDown _paintValue = null!;
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
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _undoState = this.FindControl<TextBlock>("UndoState")!;
        _closedState = this.FindControl<TextBlock>("ClosedState")!;
        _activeToolText = this.FindControl<TextBlock>("ActiveToolText")!;
        _brushSize = this.FindControl<NumericUpDown>("BrushSize")!;
        _paintValue = this.FindControl<NumericUpDown>("PaintValue")!;

        _canvas.SetViewModel(_vm);

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

        UpdateBottomBar();
        UpdateActiveToolText();

        Title = _vm.WindowTitle;
        Opened += (_, _) => _canvas.FrameProject();
    }

    private void WireToolButton(string name, int index)
    {
        var btn = this.FindControl<Button>(name);
        if (btn is null) return;
        btn.Click += (_, _) => SelectTool(index);
    }

    private void SelectTool(int index)
    {
        if (index < 0 || index >= _vm.Tools.Length) return;
        _vm.ActiveTool = _vm.Tools[index];
        UpdateActiveToolText();
    }

    private void UpdateActiveToolText()
    {
        _activeToolText.Text = _vm.ActiveTool.Name;
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
                case Key.F: _canvas.FrameProject(); e.Handled = true; break;
            }
        }
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
    }

    private void OnUndoChanged() => UpdateBottomBar();

    private void UpdateBottomBar()
    {
        _undoState.Text = $"Undo:{(_vm.Undo.CanUndo ? _vm.Undo.UndoCount : 0)} Redo:{(_vm.Undo.CanRedo ? _vm.Undo.RedoCount : 0)}";
        _closedState.Text = "Closed: " + (_vm.IsClosedPolygon ? "yes (leak-free)" : "no");
        _statusText.Text = _vm.StatusMessage;
    }
}

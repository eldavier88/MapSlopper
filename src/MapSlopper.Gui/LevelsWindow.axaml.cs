using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MapSlopper.Gui;

/// <summary>
/// Modeless dialog for adjusting the heightmap display "levels" (min/max
/// brightness range). Changes are applied live by writing back into the
/// shared <see cref="Core.Heightmap.HeightmapLevels"/> instance.
/// </summary>
public class LevelsWindow : Window
{
    private readonly EditorViewModel? _vm;
    private NumericUpDown? _minUpDown;
    private NumericUpDown? _maxUpDown;

    // Parameterless ctor required by Avalonia XAML loader / designer.
    public LevelsWindow() { AvaloniaXamlLoader.Load(this); WireUp(); }

    public LevelsWindow(EditorViewModel vm) : this()
    {
        _vm = vm;
        if (_minUpDown is not null) _minUpDown.Value = vm.Levels.DisplayMin;
        if (_maxUpDown is not null) _maxUpDown.Value = vm.Levels.DisplayMax;
    }

    private void WireUp()
    {
        _minUpDown = this.FindControl<NumericUpDown>("MinUpDown");
        _maxUpDown = this.FindControl<NumericUpDown>("MaxUpDown");
        var resetBtn = this.FindControl<Button>("ResetButton");
        var closeBtn = this.FindControl<Button>("CloseButton");

        if (_minUpDown is not null) _minUpDown.ValueChanged += OnMinChanged;
        if (_maxUpDown is not null) _maxUpDown.ValueChanged += OnMaxChanged;
        if (resetBtn is not null) resetBtn.Click += OnResetClicked;
        if (closeBtn is not null) closeBtn.Click += OnCloseClicked;
    }

    private void OnMinChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_vm is null) return;
        _vm.Levels.DisplayMin = ClampToUshort(e.NewValue);
        // Trigger repaint by toggling a heightmap cell to its current value.
        var hm = _vm.Project.Heightmap;
        if (hm.Width > 0 && hm.Height > 0) hm.Set(0, 0, hm.Sample(0, 0));
    }

    private void OnMaxChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_vm is null) return;
        _vm.Levels.DisplayMax = ClampToUshort(e.NewValue);
        var hm = _vm.Project.Heightmap;
        if (hm.Width > 0 && hm.Height > 0) hm.Set(0, 0, hm.Sample(0, 0));
    }

    private void OnResetClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        ushort max = 0;
        foreach (var v in _vm.Project.Heightmap.Data)
            if (v > max) max = v;
        if (max == 0) max = ushort.MaxValue;
        _vm.Levels.DisplayMin = 0;
        _vm.Levels.DisplayMax = max;
        if (_minUpDown is not null) _minUpDown.Value = 0;
        if (_maxUpDown is not null) _maxUpDown.Value = max;
        // Trigger repaint.
        var hm = _vm.Project.Heightmap;
        if (hm.Width > 0 && hm.Height > 0) hm.Set(0, 0, hm.Sample(0, 0));
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private static ushort ClampToUshort(double v)
    {
        if (double.IsNaN(v) || v < 0) return 0;
        if (v > ushort.MaxValue) return ushort.MaxValue;
        return (ushort)v;
    }
}

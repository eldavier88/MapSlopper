using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MapSlopper.Core.Heightmap;

namespace MapSlopper.Gui;

/// <summary>
/// Live histogram of the current heightmap's raw 16-bit values.
/// Click or drag inside the control to set <see cref="EditorViewModel.PaintValue"/>
/// without leaving the editor surface. The X axis spans 0..65535; the Y axis
/// is the count of cells at each (binned) raw value, log-scaled so a few
/// painted cells remain visible against a sea of zeros. A vertical marker
/// shows the current PaintValue.
/// </summary>
public sealed class HeightHistogramControl : Control
{
    private const int Bins = 256;
    private EditorViewModel? _vm;
    private readonly int[] _hist = new int[Bins];
    private int _maxBin = 1;

    public HeightHistogramControl()
    {
        Focusable = true;
        DataContextChanged += (_, _) => OnVmAttached();
        AttachedToVisualTree += (_, _) => OnVmAttached();
        DetachedFromVisualTree += (_, _) => OnVmDetached();
    }

    public void Bind(EditorViewModel vm)
    {
        OnVmDetached();
        _vm = vm;
        if (_vm is not null)
        {
            _vm.RepaintRequested += OnRepaint;
            _vm.ProjectReplaced += OnRepaint;
            _vm.PropertyChanged += OnVmProp;
        }
        Recompute();
        InvalidateVisual();
    }

    private void OnVmAttached()
    {
        // Pull from logical tree if attached after Bind() was called.
    }

    private void OnVmDetached()
    {
        if (_vm is null) return;
        _vm.RepaintRequested -= OnRepaint;
        _vm.ProjectReplaced -= OnRepaint;
        _vm.PropertyChanged -= OnVmProp;
    }

    private void OnVmProp(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.PaintValue)) InvalidateVisual();
    }

    private void OnRepaint() { Recompute(); InvalidateVisual(); }

    private void Recompute()
    {
        Array.Clear(_hist, 0, Bins);
        _maxBin = 1;
        if (_vm is null) return;
        var hm = _vm.Project.Heightmap;
        var data = hm.Data;
        var binShift = 8; // 65536 / 256 = 256 -> shift right 8.
        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i] >> binShift;
            if (b >= Bins) b = Bins - 1;
            _hist[b]++;
        }
        for (var b = 0; b < Bins; b++) if (_hist[b] > _maxBin) _maxBin = _hist[b];
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));

        var binW = w / Bins;
        var logMax = Math.Log(_maxBin + 1);
        var bar = new SolidColorBrush(Color.FromRgb(96, 144, 200));
        for (var b = 0; b < Bins; b++)
        {
            if (_hist[b] == 0) continue;
            var v = Math.Log(_hist[b] + 1) / logMax;
            var bh = v * (h - 2);
            ctx.DrawRectangle(bar, null,
                new Rect(b * binW, h - bh, Math.Max(1, binW), bh));
        }

        if (_vm is not null)
        {
            // Translucent band showing the active Display Levels range.
            // Anything outside this band gets clamped to black/white when
            // the heightmap is rendered, so visualising it inline tells the
            // user how their slider tweaks affect contrast.
            var loPx = (_vm.Levels.DisplayMin / 65535.0) * w;
            var hiPx = (_vm.Levels.DisplayMax / 65535.0) * w;
            if (hiPx > loPx)
            {
                var bandBrush = new SolidColorBrush(Color.FromArgb(56, 96, 220, 96));
                ctx.DrawRectangle(bandBrush, null, new Rect(loPx, 0, hiPx - loPx, h));
                var edgePen = new Pen(new SolidColorBrush(Color.FromArgb(180, 96, 220, 96)), 1);
                ctx.DrawLine(edgePen, new Point(loPx, 0), new Point(loPx, h));
                ctx.DrawLine(edgePen, new Point(hiPx, 0), new Point(hiPx, h));
            }

            var px = (_vm.PaintValue / 65535.0) * w;
            var pen = new Pen(Brushes.OrangeRed, 1.5);
            ctx.DrawLine(pen, new Point(px, 0), new Point(px, h));
            var ft = new FormattedText(
                _vm.PaintValue.ToString(),
                Typeface.Default, 11, TextAlignment.Left,
                TextWrapping.NoWrap, new Size(60, 14));
            var tx = Math.Min(w - 32, px + 3);
            ctx.DrawText(Brushes.OrangeRed, new Point(tx, 1), ft);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        SetFromPointer(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            SetFromPointer(e.GetPosition(this).X);
            e.Handled = true;
        }
    }

    private void SetFromPointer(double x)
    {
        if (_vm is null || Bounds.Width <= 0) return;
        var t = Math.Clamp(x / Bounds.Width, 0, 1);
        _vm.PaintValue = (ushort)Math.Round(t * 65535.0);
        InvalidateVisual();
    }
}

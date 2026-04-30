using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;

namespace MapSlopper.Gui;

/// <summary>
/// 2D editing canvas. Owns the viewport (camera centre + zoom), translates
/// pointer events into world space, dispatches them to the active tool, and
/// paints the heightmap, outline graph, grid, and per-tool overlay.
///
/// World convention: +Y points up (Quake). The render method flips Y when
/// converting world->screen.
/// </summary>
public class Editor2DControl : UserControl
{
    public Editor2DControl()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private EditorViewModel? _vm;

    /// <summary>World-space point currently under the cursor (last seen).</summary>
    public Vec2 CursorWorld { get; private set; }

    /// <summary>True once the cursor has been moved over the canvas at least once.</summary>
    public bool HasCursor { get; private set; }

    public Vec2 CameraCenterWorld { get; private set; } = Vec2.Zero;
    public double PixelsPerWorldUnit { get; private set; } = 1.0;

    private bool _isLeftDown;
    private bool _isMiddleDown;
    private bool _isSpaceDown;
    private bool _isPanning;
    private Point _lastPanScreen;

    public void SetViewModel(EditorViewModel vm)
    {
        if (_vm is not null)
        {
            _vm.RepaintRequested -= OnVmRepaintRequested;
            _vm.ProjectReplaced -= OnVmProjectReplaced;
        }
        _vm = vm;
        _vm.RepaintRequested += OnVmRepaintRequested;
        _vm.ProjectReplaced += OnVmProjectReplaced;
        _vm.PixelsPerWorldUnit = PixelsPerWorldUnit;
        InvalidateVisual();
    }

    private void OnVmRepaintRequested() => InvalidateVisual();
    private void OnVmProjectReplaced() => InvalidateVisual();

    /// <summary>Centre the camera on the heightmap so the user sees something on first launch.</summary>
    public void FrameProject()
    {
        if (_vm is null) return;
        var (mn, mx) = _vm.Project.Heightmap.WorldBounds();
        CameraCenterWorld = new Vec2((mn.X + mx.X) * 0.5, (mn.Y + mx.Y) * 0.5);
        var w = Math.Max(1.0, Bounds.Width);
        var h = Math.Max(1.0, Bounds.Height);
        var zoomX = w / Math.Max(1.0, mx.X - mn.X);
        var zoomY = h / Math.Max(1.0, mx.Y - mn.Y);
        PixelsPerWorldUnit = Math.Max(0.05, Math.Min(zoomX, zoomY) * 0.9);
        _vm.PixelsPerWorldUnit = PixelsPerWorldUnit;
        InvalidateVisual();
    }

    public Point WorldToScreen(Vec2 w)
    {
        var cx = Bounds.Width * 0.5;
        var cy = Bounds.Height * 0.5;
        var sx = cx + (w.X - CameraCenterWorld.X) * PixelsPerWorldUnit;
        var sy = cy - (w.Y - CameraCenterWorld.Y) * PixelsPerWorldUnit;
        return new Point(sx, sy);
    }

    public Vec2 ScreenToWorld(Point s)
    {
        var cx = Bounds.Width * 0.5;
        var cy = Bounds.Height * 0.5;
        var wx = (s.X - cx) / PixelsPerWorldUnit + CameraCenterWorld.X;
        var wy = -((s.Y - cy) / PixelsPerWorldUnit) + CameraCenterWorld.Y;
        return new Vec2(wx, wy);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.RepaintRequested -= OnVmRepaintRequested;
            _vm.ProjectReplaced -= OnVmProjectReplaced;
        }
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm is null) return;
        Focus();
        var pt = e.GetCurrentPoint(this);
        var screen = pt.Position;
        var world = ScreenToWorld(screen);
        if (pt.Properties.IsMiddleButtonPressed || (pt.Properties.IsLeftButtonPressed && _isSpaceDown))
        {
            _isPanning = true;
            _lastPanScreen = screen;
            _isMiddleDown = pt.Properties.IsMiddleButtonPressed;
            e.Handled = true;
            return;
        }
        if (pt.Properties.IsLeftButtonPressed)
        {
            _isLeftDown = true;
            _vm.ActiveTool.OnPointerPressed(_vm, world, isRightClick: false);
            e.Handled = true;
        }
        else if (pt.Properties.IsRightButtonPressed)
        {
            _vm.ActiveTool.OnPointerPressed(_vm, world, isRightClick: true);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_vm is null) return;
        var pt = e.GetCurrentPoint(this);
        var screen = pt.Position;
        CursorWorld = ScreenToWorld(screen);
        HasCursor = true;
        _vm.StatusMessage =
            $"World ({CursorWorld.X:0.0}, {CursorWorld.Y:0.0}) | Tool: {_vm.ActiveTool.Name} | Brush: {_vm.BrushSizeCells} | Paint: {_vm.PaintValue}";
        if (_isPanning)
        {
            var dx = screen.X - _lastPanScreen.X;
            var dy = screen.Y - _lastPanScreen.Y;
            _lastPanScreen = screen;
            CameraCenterWorld = new Vec2(
                CameraCenterWorld.X - dx / PixelsPerWorldUnit,
                CameraCenterWorld.Y + dy / PixelsPerWorldUnit);
            InvalidateVisual();
            return;
        }
        _vm.ActiveTool.OnPointerMoved(_vm, CursorWorld, isPressed: _isLeftDown);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_vm is null) return;
        var world = ScreenToWorld(e.GetPosition(this));
        if (_isPanning)
        {
            _isPanning = false;
            _isMiddleDown = false;
            return;
        }
        if (_isLeftDown)
        {
            _isLeftDown = false;
            _vm.ActiveTool.OnPointerReleased(_vm, world);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_vm is null) return;
        var screen = e.GetPosition(this);
        var beforeWorld = ScreenToWorld(screen);
        var factor = e.Delta.Y > 0 ? 1.1 : (e.Delta.Y < 0 ? 1.0 / 1.1 : 1.0);
        PixelsPerWorldUnit = Math.Max(0.01, Math.Min(1000.0, PixelsPerWorldUnit * factor));
        var afterWorld = ScreenToWorld(screen);
        // Re-anchor so the cursor stays over the same world point.
        CameraCenterWorld = new Vec2(
            CameraCenterWorld.X + (beforeWorld.X - afterWorld.X),
            CameraCenterWorld.Y + (beforeWorld.Y - afterWorld.Y));
        _vm.PixelsPerWorldUnit = PixelsPerWorldUnit;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Space) _isSpaceDown = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space) _isSpaceDown = false;
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        if (_vm is null) return;
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), bounds);

        DrawHeightmap(ctx);
        DrawGrid(ctx);
        DrawHeightmapBounds(ctx);
        DrawOutline(ctx);
        DrawPlayerReference(ctx);

        // Tool overlay last.
        _vm.ActiveTool.RenderOverlay(_vm, ctx, this);
    }

    private void DrawHeightmap(DrawingContext ctx)
    {
        if (_vm is null) return;
        var hm = _vm.Project.Heightmap;
        var levels = _vm.Levels;

        // Compute visible cell range (with one-cell padding) for perf.
        var viewMinW = ScreenToWorld(new Point(0, Bounds.Height));
        var viewMaxW = ScreenToWorld(new Point(Bounds.Width, 0));
        var (cx0, cy0) = hm.WorldToCell(viewMinW);
        var (cx1, cy1) = hm.WorldToCell(viewMaxW);
        cx0 = Math.Max(0, cx0 - 1);
        cy0 = Math.Max(0, cy0 - 1);
        cx1 = Math.Min(hm.Width - 1, cx1 + 1);
        cy1 = Math.Min(hm.Height - 1, cy1 + 1);
        if (cx0 > cx1 || cy0 > cy1) return;

        var emptyBrush = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1C));
        var brushCache = new IBrush?[256];

        for (var y = cy0; y <= cy1; y++)
        {
            for (var x = cx0; x <= cx1; x++)
            {
                var v = hm.Sample(x, y);
                IBrush brush;
                if (v == 0)
                {
                    brush = emptyBrush;
                }
                else
                {
                    var b = levels.ToByte(v);
                    var slot = brushCache[b];
                    if (slot is null)
                    {
                        slot = new SolidColorBrush(Color.FromRgb(b, b, b));
                        brushCache[b] = slot;
                    }
                    brush = slot;
                }
                var min = hm.CellWorldMin(x, y);
                var max = hm.CellWorldMax(x, y);
                var a = WorldToScreen(new Vec2(min.X, max.Y));
                var c = WorldToScreen(new Vec2(max.X, min.Y));
                ctx.FillRectangle(brush, new Rect(a, c));
            }
        }
    }

    private void DrawGrid(DrawingContext ctx)
    {
        if (_vm is null) return;
        var hm = _vm.Project.Heightmap;
        var (mn, mx) = hm.WorldBounds();
        var thinPen = new Pen(new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)), 1);
        var thickPen = new Pen(new SolidColorBrush(Color.FromArgb(128, 255, 255, 255)), 1);
        var step = hm.CellSize;
        // Vertical lines.
        var nx = (int)Math.Round((mx.X - mn.X) / step);
        for (var i = 0; i <= nx; i++)
        {
            var x = mn.X + i * step;
            var a = WorldToScreen(new Vec2(x, mn.Y));
            var b = WorldToScreen(new Vec2(x, mx.Y));
            var pen = (i % 8 == 0) ? thickPen : thinPen;
            ctx.DrawLine(pen, a, b);
        }
        var ny = (int)Math.Round((mx.Y - mn.Y) / step);
        for (var j = 0; j <= ny; j++)
        {
            var y = mn.Y + j * step;
            var a = WorldToScreen(new Vec2(mn.X, y));
            var b = WorldToScreen(new Vec2(mx.X, y));
            var pen = (j % 8 == 0) ? thickPen : thinPen;
            ctx.DrawLine(pen, a, b);
        }
        // Labels at every 8-cell line.
        var labelBrush = new SolidColorBrush(Color.FromArgb(160, 200, 200, 200));
        for (var i = 0; i <= nx; i += 8)
        {
            var x = mn.X + i * step;
            var s = WorldToScreen(new Vec2(x, mn.Y));
            var ft = MakeLabel(((int)Math.Round(x)).ToString(System.Globalization.CultureInfo.InvariantCulture));
            ctx.DrawText(labelBrush, new Point(s.X + 2, s.Y - 14), ft);
        }
        for (var j = 0; j <= ny; j += 8)
        {
            var y = mn.Y + j * step;
            var s = WorldToScreen(new Vec2(mn.X, y));
            var ft = MakeLabel(((int)Math.Round(y)).ToString(System.Globalization.CultureInfo.InvariantCulture));
            ctx.DrawText(labelBrush, new Point(s.X + 2, s.Y), ft);
        }
    }

    private void DrawHeightmapBounds(DrawingContext ctx)
    {
        if (_vm is null) return;
        var hm = _vm.Project.Heightmap;
        var (mn, mx) = hm.WorldBounds();
        var a = WorldToScreen(new Vec2(mn.X, mx.Y));
        var b = WorldToScreen(new Vec2(mx.X, mn.Y));
        ctx.DrawRectangle(null, new Pen(Brushes.LightSkyBlue, 2), new Rect(a, b));
    }

    private void DrawOutline(DrawingContext ctx)
    {
        if (_vm is null) return;
        var graph = _vm.Project.Outline;
        var edgePen = new Pen(Brushes.LimeGreen, 2);
        foreach (var e in graph.Edges)
        {
            if (!graph.Points.TryGetValue(e.A, out var pa)) continue;
            if (!graph.Points.TryGetValue(e.B, out var pb)) continue;
            ctx.DrawLine(edgePen, WorldToScreen(pa.Position), WorldToScreen(pb.Position));
        }
        var ptBrush = Brushes.OrangeRed;
        var selBrush = Brushes.Yellow;
        foreach (var p in graph.Points.Values)
        {
            var s = WorldToScreen(p.Position);
            var isSelected = _vm.SelectedPointId == p.Id;
            var radius = isSelected ? 8.0 : 6.0;
            ctx.DrawEllipse(isSelected ? selBrush : ptBrush, null, s, radius, radius);
        }
    }

    private void DrawPlayerReference(DrawingContext ctx)
    {
        // Player bbox is 32×32 at origin (Quake "info_player_start" footprint).
        var a = WorldToScreen(new Vec2(-16, 16));
        var b = WorldToScreen(new Vec2(16, -16));
        var fill = new SolidColorBrush(Color.FromArgb(80, 80, 160, 255));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, 80, 160, 255)), 1);
        ctx.DrawRectangle(fill, pen, new Rect(a, b));
        var label = MakeLabel("32×32×64 player");
        var origin = WorldToScreen(new Vec2(-16, -16));
        ctx.DrawText(Brushes.LightSkyBlue, new Point(origin.X, origin.Y + 2), label);
    }

    private static FormattedText MakeLabel(string text) => new()
    {
        Text = text,
        Typeface = Typeface.Default,
        FontSize = 11,
        TextAlignment = TextAlignment.Left,
        Constraint = new Size(double.PositiveInfinity, double.PositiveInfinity),
    };
}

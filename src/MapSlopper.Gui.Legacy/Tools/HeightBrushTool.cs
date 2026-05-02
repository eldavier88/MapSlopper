using System;
using Avalonia;
using Avalonia.Media;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;
using MapSlopper.Gui.Legacy.Commands;

namespace MapSlopper.Gui.Legacy;

public sealed class HeightBrushTool : IEditorTool
{
    public string Name => "Height Brush";
    public string Hotkey => "7";

    private bool _stroking;
    private ushort[]? _snapshot;

    public void OnPointerPressed(EditorViewModel vm, Vec2 worldPos, bool isRightClick)
    {
        if (isRightClick) { _stroking = false; _snapshot = null; return; }
        var hm = vm.Project.Heightmap;
        _snapshot = (ushort[])hm.Data.Clone();
        _stroking = true;
        Paint(vm, worldPos);
    }

    public void OnPointerMoved(EditorViewModel vm, Vec2 worldPos, bool isPressed)
    {
        if (!_stroking || !isPressed) return;
        Paint(vm, worldPos);
    }

    public void OnPointerReleased(EditorViewModel vm, Vec2 worldPos)
    {
        if (!_stroking) return;
        _stroking = false;
        var snap = _snapshot;
        _snapshot = null;
        if (snap is null) return;

        var hm = vm.Project.Heightmap;
        var current = (ushort[])hm.Data.Clone();
        if (HeightStrokeCmd.ArraysEqual(snap, current)) return;

        Array.Copy(snap, hm.Data, snap.Length);
        vm.Undo.Execute(new HeightStrokeCmd(hm, snap, current));
    }

    private static void Paint(EditorViewModel vm, Vec2 worldPos)
    {
        var hm = vm.Project.Heightmap;
        var (cx, cy) = hm.WorldToCell(worldPos);
        var n = Math.Max(1, vm.BrushSizeCells);
        var half = n / 2;
        var x0 = cx - half;
        var y0 = cy - half;
        var x1 = x0 + n - 1;
        var y1 = y0 + n - 1;
        hm.Fill(x0, y0, x1, y1, vm.PaintValue);
    }

    public void RenderOverlay(EditorViewModel vm, DrawingContext ctx, Editor2DControl canvas)
    {
        if (!canvas.HasCursor) return;
        var hm = vm.Project.Heightmap;
        var (cx, cy) = hm.WorldToCell(canvas.CursorWorld);
        var n = Math.Max(1, vm.BrushSizeCells);
        var half = n / 2;
        var x0 = cx - half;
        var y0 = cy - half;
        var minW = hm.CellWorldMin(x0, y0);
        var maxW = hm.CellWorldMax(x0 + n - 1, y0 + n - 1);
        var a = canvas.WorldToScreen(new Vec2(minW.X, maxW.Y));
        var b = canvas.WorldToScreen(new Vec2(maxW.X, minW.Y));
        var rect = new Rect(a, b);
        var pen = new Pen(Brushes.Yellow, 2);
        ctx.DrawRectangle(null, pen, rect);
    }
}

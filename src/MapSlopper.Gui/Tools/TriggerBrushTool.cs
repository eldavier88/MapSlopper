using System;
using Avalonia;
using Avalonia.Media;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Triggers;
using MapSlopper.Gui.Commands;

namespace MapSlopper.Gui.Tools;

/// <summary>
/// Square N×N cell brush that paints the active trigger type id into the
/// project's trigger layer. Right-click erases (paints id 0). Press starts
/// a stroke (snapshots the layer), drag paints, release commits a single
/// <see cref="TriggerStrokeCmd"/> covering the whole stroke. No-op strokes
/// are dropped.
/// </summary>
public sealed class TriggerBrushTool : IEditorTool
{
    public string Name => "Trigger Brush";
    public string Hotkey => "8";

    private bool _stroking;
    private ushort[]? _snapshot;
    private bool _eraseStroke;

    public void OnPointerPressed(EditorViewModel vm, Vec2 worldPos, bool isRightClick)
    {
        var tl = vm.Project.TriggerLayer;
        _snapshot = (ushort[])tl.Data.Clone();
        _stroking = true;
        _eraseStroke = isRightClick;
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

        var tl = vm.Project.TriggerLayer;
        var current = (ushort[])tl.Data.Clone();
        if (HeightStrokeCmd.ArraysEqual(snap, current)) return; // no-op

        Array.Copy(snap, tl.Data, snap.Length);
        vm.Undo.Execute(new TriggerStrokeCmd(tl, snap, current));
    }

    public void Reset()
    {
        _stroking = false;
        _snapshot = null;
        _eraseStroke = false;
    }

    public string? StatusHint(EditorViewModel vm)
    {
        var t = vm.TriggerTypes.FindById(vm.ActiveTriggerTypeId);
        return t is null
            ? "Right-click erases. Choose a trigger type from the palette."
            : $"Painting '{t.Name}' (id {t.Id}). Right-click erases.";
    }

    private void Paint(EditorViewModel vm, Vec2 worldPos)
    {
        var tl = vm.Project.TriggerLayer;
        var (cx, cy) = tl.WorldToCell(worldPos);
        var n = Math.Max(1, vm.BrushSizeCells);
        var half = n / 2;
        var x0 = cx - half;
        var y0 = cy - half;
        var x1 = x0 + n - 1;
        var y1 = y0 + n - 1;
        ushort v = _eraseStroke ? (ushort)0 : vm.ActiveTriggerTypeId;
        tl.Fill(x0, y0, x1, y1, v);
    }

    public void RenderOverlay(EditorViewModel vm, DrawingContext ctx, Editor2DControl canvas)
    {
        if (!canvas.HasCursor) return;
        var tl = vm.Project.TriggerLayer;
        var (cx, cy) = tl.WorldToCell(canvas.CursorWorld);
        var n = Math.Max(1, vm.BrushSizeCells);
        var half = n / 2;
        var x0 = cx - half;
        var y0 = cy - half;
        var minW = tl.CellWorldMin(x0, y0);
        var maxW = tl.CellWorldMax(x0 + n - 1, y0 + n - 1);
        var a = canvas.WorldToScreen(new Vec2(minW.X, maxW.Y));
        var b = canvas.WorldToScreen(new Vec2(maxW.X, minW.Y));
        var rect = new Rect(a, b);

        // Cursor outline tinted with the active trigger color so the user
        // can read the active selection at a glance.
        var color = ParseColor(vm.TriggerTypes.FindById(vm.ActiveTriggerTypeId)?.ColorHex)
                    ?? Colors.Yellow;
        var pen = new Pen(new SolidColorBrush(color), 2);
        ctx.DrawRectangle(null, pen, rect);
    }

    /// <summary>Parse a "#RRGGBB" hex string. Returns null on failure.</summary>
    public static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.Trim();
        if (s.StartsWith("#")) s = s.Substring(1);
        if (s.Length == 6 &&
            byte.TryParse(s.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(s.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(s.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var bb))
        {
            return Color.FromRgb(r, g, bb);
        }
        return null;
    }
}

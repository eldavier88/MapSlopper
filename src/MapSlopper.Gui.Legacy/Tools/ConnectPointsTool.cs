using System;
using Avalonia.Media;
using MapSlopper.Core.Geometry;
using MapSlopper.Gui.Legacy.Commands;

namespace MapSlopper.Gui.Legacy;

public sealed class ConnectPointsTool : IEditorTool
{
    public string Name => "Connect Points";
    public string Hotkey => "5";

    private Guid? _pending;

    public void OnPointerPressed(EditorViewModel vm, Vec2 worldPos, bool isRightClick)
    {
        if (isRightClick) { Reset(); vm.SelectedPointId = null; vm.StatusMessage = "Connect: chain reset."; return; }
        var radius = 8.0 / Math.Max(vm.PixelsPerWorldUnit, 1e-6);
        var pick = vm.Project.Outline.PickPoint(worldPos, radius);

        if (pick is null) { Reset(); vm.SelectedPointId = null; return; }

        if (_pending is null)
        {
            _pending = pick.Id;
            vm.SelectedPointId = pick.Id;
            return;
        }

        if (_pending == pick.Id)
        {
            Reset();
            vm.SelectedPointId = null;
            return;
        }

        var a = _pending.Value;
        var b = pick.Id;
        if (!vm.Project.Outline.HasEdge(a, b))
            vm.Undo.Execute(new AddEdgeCmd(vm.Project.Outline, a, b));
        _pending = b;
        vm.SelectedPointId = b;
    }

    public void OnPointerMoved(EditorViewModel vm, Vec2 worldPos, bool isPressed) { }
    public void OnPointerReleased(EditorViewModel vm, Vec2 worldPos) { }

    public void RenderOverlay(EditorViewModel vm, DrawingContext ctx, Editor2DControl canvas)
    {
        if (_pending is null) return;
        if (!vm.Project.Outline.Points.TryGetValue(_pending.Value, out var p)) return;
        var s = canvas.WorldToScreen(p.Position);
        var pen = new Pen(Brushes.OrangeRed, 2);
        ctx.DrawEllipse(null, pen, s, 10, 10);
    }

    public void Reset() => _pending = null;

    public string? StatusHint(EditorViewModel vm) =>
        _pending is null
            ? "Click a vertex to start a connection chain."
            : "Click another vertex to connect (chains). Esc / right-click / empty-space to reset.";
}

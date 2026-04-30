using System;
using Avalonia.Media;
using MapSlopper.Core.Geometry;
using MapSlopper.Gui.Commands;

namespace MapSlopper.Gui.Tools;

/// <summary>
/// First click selects an existing vertex; second click on another vertex adds
/// the connecting edge (one undo step); third click resets the pending state.
/// </summary>
public sealed class ConnectPointsTool : IEditorTool
{
    public string Name => "Connect Points";
    public string Hotkey => "5";

    private Guid? _pending;

    public void OnPointerPressed(EditorViewModel vm, Vec2 worldPos, bool isRightClick)
    {
        if (isRightClick) { _pending = null; vm.SelectedPointId = null; return; }
        var radius = 8.0 / Math.Max(vm.PixelsPerWorldUnit, 1e-6);
        var pick = vm.Project.Outline.PickPoint(worldPos, radius);

        // Third-click reset (current state has _pending and click is far from any point).
        if (_pending is not null && pick is null)
        {
            _pending = null;
            vm.SelectedPointId = null;
            return;
        }
        if (pick is null) return;

        if (_pending is null)
        {
            _pending = pick.Id;
            vm.SelectedPointId = pick.Id;
            return;
        }

        if (_pending == pick.Id)
        {
            _pending = null;
            vm.SelectedPointId = null;
            return;
        }

        var a = _pending.Value;
        var b = pick.Id;
        _pending = null;
        vm.SelectedPointId = b;
        vm.Undo.Execute(new AddEdgeCmd(vm.Project.Outline, a, b));
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
}

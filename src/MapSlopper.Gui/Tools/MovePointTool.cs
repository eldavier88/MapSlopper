using System;
using Avalonia.Media;
using MapSlopper.Core.Geometry;
using MapSlopper.Gui.Commands;

namespace MapSlopper.Gui.Tools;

/// <summary>
/// Press to grab the nearest point, drag to move it (live preview), release to
/// commit the move as a single undo step. If the point did not actually move
/// (drag distance below 1e-6) no command is pushed.
/// </summary>
public sealed class MovePointTool : IEditorTool
{
    public string Name => "Move Point";
    public string Hotkey => "3";

    private Guid? _grabbed;
    private Vec2 _startPos;

    public void OnPointerPressed(EditorViewModel vm, Vec2 worldPos, bool isRightClick)
    {
        if (isRightClick) { _grabbed = null; return; }
        var radius = 8.0 / Math.Max(vm.PixelsPerWorldUnit, 1e-6);
        var pick = vm.Project.Outline.PickPoint(worldPos, radius);
        if (pick is null) { _grabbed = null; return; }
        _grabbed = pick.Id;
        _startPos = pick.Position;
        vm.SelectedPointId = pick.Id;
    }

    public void OnPointerMoved(EditorViewModel vm, Vec2 worldPos, bool isPressed)
    {
        if (!isPressed || _grabbed is null) return;
        // Live drag: mutate the graph directly (fires Changed -> repaint).
        // The single MovePointCmd captured at release covers the whole drag.
        vm.Project.Outline.MovePoint(_grabbed.Value, vm.SnapWorld(worldPos));
    }

    public void OnPointerReleased(EditorViewModel vm, Vec2 worldPos)
    {
        if (_grabbed is null) return;
        var id = _grabbed.Value;
        _grabbed = null;
        if (!vm.Project.Outline.Points.TryGetValue(id, out var p)) return;
        var endPos = p.Position;
        if (Vec2.DistanceSquared(_startPos, endPos) < 1e-12)
            return; // no-op drag, no undo entry
        // We've already moved the point during drag; revert + Execute commits cleanly.
        vm.Project.Outline.MovePoint(id, _startPos);
        vm.Undo.Execute(new MovePointCmd(vm.Project.Outline, id, _startPos, endPos));
    }

    public void RenderOverlay(EditorViewModel vm, DrawingContext ctx, Editor2DControl canvas) { }
}

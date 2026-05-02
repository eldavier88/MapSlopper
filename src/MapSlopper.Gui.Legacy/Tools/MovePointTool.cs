using System;
using Avalonia.Media;
using MapSlopper.Core.Geometry;
using MapSlopper.Gui.Legacy.Commands;

namespace MapSlopper.Gui.Legacy;

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
        var target = vm.ShouldSnapToGrid ? vm.SnapWorldToCell(worldPos) : worldPos;
        vm.EnsureHeightmapCovers(target);
        vm.Project.Outline.MovePoint(_grabbed.Value, target);
    }

    public void OnPointerReleased(EditorViewModel vm, Vec2 worldPos)
    {
        if (_grabbed is null) return;
        var id = _grabbed.Value;
        _grabbed = null;
        if (!vm.Project.Outline.Points.TryGetValue(id, out var p)) return;
        var endPos = p.Position;
        if (Vec2.DistanceSquared(_startPos, endPos) < 1e-12)
            return;
        vm.Project.Outline.MovePoint(id, _startPos);
        vm.Undo.Execute(new MovePointCmd(vm.Project.Outline, id, _startPos, endPos));
    }

    public void RenderOverlay(EditorViewModel vm, DrawingContext ctx, Editor2DControl canvas) { }

    public string? StatusHint(EditorViewModel vm) =>
        vm.ShouldSnapToGrid
            ? "Move tool: SNAP ON (cell-aligned). Drag a vertex."
            : "Move tool: drag a vertex. Hold Shift or toggle Snap to align to grid.";
}

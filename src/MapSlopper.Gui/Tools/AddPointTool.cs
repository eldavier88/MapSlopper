using System;
using Avalonia.Media;
using MapSlopper.Core.Geometry;
using MapSlopper.Gui.Commands;

namespace MapSlopper.Gui.Tools;

/// <summary>
/// Left click to add a new outline point at the snapped world location.
/// Snaps to the nearest existing point (within 8 px in screen space) or
/// to a fraction of the heightmap cell (cellSize/4) if no nearby point.
/// </summary>
public sealed class AddPointTool : IEditorTool
{
    public string Name => "Add Point";
    public string Hotkey => "1";

    public void OnPointerPressed(EditorViewModel vm, Vec2 worldPos, bool isRightClick)
    {
        if (isRightClick) return;
        var snapped = vm.SnapWorld(worldPos);
        // If a point already exists within snap radius, do not create a duplicate.
        var radius = 8.0 / Math.Max(vm.PixelsPerWorldUnit, 1e-6);
        if (vm.Project.Outline.PickPoint(snapped, radius) != null) return;
        var id = Guid.NewGuid();
        vm.Undo.Execute(new AddPointCmd(vm.Project.Outline, id, snapped));
        vm.SelectedPointId = id;
    }

    public void OnPointerMoved(EditorViewModel vm, Vec2 worldPos, bool isPressed) { }
    public void OnPointerReleased(EditorViewModel vm, Vec2 worldPos) { }
    public void RenderOverlay(EditorViewModel vm, DrawingContext ctx, Editor2DControl canvas) { }
}

using System;
using Avalonia.Media;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Outline;
using MapSlopper.Core.Undo;
using MapSlopper.Gui.Commands;

namespace MapSlopper.Gui.Tools;

/// <summary>
/// Left click on a vertex removes it and any incident edges as a single
/// undoable operation. Implemented as a composite command so undo restores
/// both the point and its prior edges.
/// </summary>
public sealed class RemovePointTool : IEditorTool
{
    public string Name => "Remove Point";
    public string Hotkey => "6";

    public void OnPointerPressed(EditorViewModel vm, Vec2 worldPos, bool isRightClick)
    {
        if (isRightClick) return;
        var radius = 8.0 / Math.Max(vm.PixelsPerWorldUnit, 1e-6);
        var pick = vm.Project.Outline.PickPoint(worldPos, radius);
        if (pick is null) return;

        // RemovePointCmd already snapshots the incident neighbors and restores them on revert.
        vm.Undo.Execute(new RemovePointCmd(vm.Project.Outline, pick.Id));
        if (vm.SelectedPointId == pick.Id) vm.SelectedPointId = null;
    }

    public void OnPointerMoved(EditorViewModel vm, Vec2 worldPos, bool isPressed) { }
    public void OnPointerReleased(EditorViewModel vm, Vec2 worldPos) { }
    public void RenderOverlay(EditorViewModel vm, DrawingContext ctx, Editor2DControl canvas) { }
}

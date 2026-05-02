using System;
using Avalonia.Media;
using MapSlopper.Core.Geometry;
using MapSlopper.Gui.Legacy.Commands;

namespace MapSlopper.Gui.Legacy;

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

        vm.Undo.Execute(new RemovePointCmd(vm.Project.Outline, pick.Id));
        if (vm.SelectedPointId == pick.Id) vm.SelectedPointId = null;
    }

    public void OnPointerMoved(EditorViewModel vm, Vec2 worldPos, bool isPressed) { }
    public void OnPointerReleased(EditorViewModel vm, Vec2 worldPos) { }
    public void RenderOverlay(EditorViewModel vm, DrawingContext ctx, Editor2DControl canvas) { }
}

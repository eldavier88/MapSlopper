using System;
using Avalonia.Media;
using MapSlopper.Core.Geometry;
using MapSlopper.Gui.Commands;

namespace MapSlopper.Gui.Tools;

/// <summary>Left click on an edge to insert a new vertex splitting it.</summary>
public sealed class InsertOnEdgeTool : IEditorTool
{
    public string Name => "Insert On Edge";
    public string Hotkey => "2";

    public void OnPointerPressed(EditorViewModel vm, Vec2 worldPos, bool isRightClick)
    {
        if (isRightClick) return;
        var radius = 8.0 / Math.Max(vm.PixelsPerWorldUnit, 1e-6);
        var edge = vm.Project.Outline.PickEdge(worldPos, radius);
        if (edge is null) return;
        var snapped = vm.SnapWorld(worldPos);
        var midId = Guid.NewGuid();
        vm.Undo.Execute(new InsertOnEdgeCmd(vm.Project.Outline, edge.Value.A, edge.Value.B, midId, snapped));
        vm.SelectedPointId = midId;
    }

    public void OnPointerMoved(EditorViewModel vm, Vec2 worldPos, bool isPressed) { }
    public void OnPointerReleased(EditorViewModel vm, Vec2 worldPos) { }
    public void RenderOverlay(EditorViewModel vm, DrawingContext ctx, Editor2DControl canvas) { }
}

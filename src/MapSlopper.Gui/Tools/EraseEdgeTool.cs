using System;
using Avalonia.Media;
using MapSlopper.Core.Geometry;
using MapSlopper.Gui.Commands;

namespace MapSlopper.Gui.Tools;

/// <summary>Left click on an edge removes it (vertices are kept).</summary>
public sealed class EraseEdgeTool : IEditorTool
{
    public string Name => "Erase Edge";
    public string Hotkey => "4";

    public void OnPointerPressed(EditorViewModel vm, Vec2 worldPos, bool isRightClick)
    {
        if (isRightClick) return;
        var radius = 8.0 / Math.Max(vm.PixelsPerWorldUnit, 1e-6);
        var edge = vm.Project.Outline.PickEdge(worldPos, radius);
        if (edge is null) return;
        vm.Undo.Execute(new RemoveEdgeCmd(vm.Project.Outline, edge.Value.A, edge.Value.B));
    }

    public void OnPointerMoved(EditorViewModel vm, Vec2 worldPos, bool isPressed) { }
    public void OnPointerReleased(EditorViewModel vm, Vec2 worldPos) { }
    public void RenderOverlay(EditorViewModel vm, DrawingContext ctx, Editor2DControl canvas) { }
}

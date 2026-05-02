using System;
using Avalonia.Media;
using MapSlopper.Core.Geometry;
using MapSlopper.Gui.Legacy.Commands;

namespace MapSlopper.Gui.Legacy;

public sealed class AddPointTool : IEditorTool
{
    public string Name => "Add Point";
    public string Hotkey => "1";

    private Guid? _previous;

    public void OnPointerPressed(EditorViewModel vm, Vec2 worldPos, bool isRightClick)
    {
        if (isRightClick) { Reset(); vm.StatusMessage = "Add Point: chain reset."; return; }
        var snapped = vm.SnapWorld(worldPos);
        var radius = 8.0 / Math.Max(vm.PixelsPerWorldUnit, 1e-6);

        var existing = vm.Project.Outline.PickPoint(snapped, radius);
        Guid newId;
        if (existing is not null)
        {
            newId = existing.Id;
        }
        else
        {
            vm.EnsureHeightmapCovers(snapped);
            newId = Guid.NewGuid();
            vm.Undo.Execute(new AddPointCmd(vm.Project.Outline, newId, snapped));
        }

        if (_previous is { } prev && prev != newId
            && !vm.Project.Outline.HasEdge(prev, newId))
        {
            vm.Undo.Execute(new AddEdgeCmd(vm.Project.Outline, prev, newId));
        }
        _previous = newId;
        vm.SelectedPointId = newId;
    }

    public void OnPointerMoved(EditorViewModel vm, Vec2 worldPos, bool isPressed) { }
    public void OnPointerReleased(EditorViewModel vm, Vec2 worldPos) { }

    public void RenderOverlay(EditorViewModel vm, DrawingContext ctx, Editor2DControl canvas)
    {
        if (_previous is null) return;
        if (!vm.Project.Outline.Points.TryGetValue(_previous.Value, out var p)) return;
        var s = canvas.WorldToScreen(p.Position);
        var pen = new Pen(Brushes.LimeGreen, 2);
        ctx.DrawEllipse(null, pen, s, 8, 8);
    }

    public void Reset() => _previous = null;

    public string? StatusHint(EditorViewModel vm) =>
        _previous is null
            ? "Click to add point. Esc / right-click to reset chain."
            : "Click to add chained point (auto-connected). Esc / right-click to reset.";
}

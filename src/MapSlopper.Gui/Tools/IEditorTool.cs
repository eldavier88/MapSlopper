using MapSlopper.Core.Geometry;

namespace MapSlopper.Gui.Tools;

/// <summary>
/// A 2D editor tool. Tools translate raw pointer events on the editor canvas
/// (already converted to world coordinates) into mutations of the project,
/// always going through the view-model's <c>UndoStack</c>.
/// </summary>
public interface IEditorTool
{
    string Name { get; }
    string Hotkey { get; }

    void OnPointerPressed(EditorViewModel vm, Vec2 worldPos, bool isRightClick);
    void OnPointerMoved(EditorViewModel vm, Vec2 worldPos, bool isPressed);
    void OnPointerReleased(EditorViewModel vm, Vec2 worldPos);

    /// <summary>Optional preview overlay for the tool (drawn after geometry).</summary>
    void RenderOverlay(EditorViewModel vm, Avalonia.Media.DrawingContext ctx, Editor2DControl canvas);

    /// <summary>
    /// Reset any tool-internal multi-click state (e.g. chained-add-point
    /// "previous vertex", connect "first vertex"). Called when switching
    /// tools, opening a project, or pressing Escape.
    /// </summary>
    void Reset() { }

    /// <summary>Optional one-line hint shown in the status bar while active.</summary>
    string? StatusHint(EditorViewModel vm) => null;
}

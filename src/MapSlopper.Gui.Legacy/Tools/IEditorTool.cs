using MapSlopper.Core.Geometry;

namespace MapSlopper.Gui.Legacy;

public interface IEditorTool
{
    string Name { get; }
    string Hotkey { get; }
    void OnPointerPressed(EditorViewModel vm, Vec2 worldPos, bool isRightClick);
    void OnPointerMoved(EditorViewModel vm, Vec2 worldPos, bool isPressed);
    void OnPointerReleased(EditorViewModel vm, Vec2 worldPos);
    void RenderOverlay(EditorViewModel vm, Avalonia.Media.DrawingContext ctx, Editor2DControl canvas);
    void Reset() { }
    string? StatusHint(EditorViewModel vm) => null;
}

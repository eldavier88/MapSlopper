using System.Globalization;
using Avalonia.Media;

namespace MapSlopper.Gui;

/// <summary>
/// Tiny helper that compresses the Avalonia 11 <see cref="FormattedText"/>
/// constructor's six required arguments into a single call site that
/// matches what we used to write under Avalonia 0.10. Keeps the rendering
/// code in <see cref="Editor2DControl"/>, <see cref="Preview3DControl"/>,
/// <see cref="HeightHistogramControl"/>, and the auto-map wizard's
/// thumbnail control readable without hard-coding culture / flow direction
/// at every call.
/// </summary>
internal static class FormattedTextCompat
{
    /// <summary>
    /// Build a <see cref="FormattedText"/> with the foreground brush
    /// baked in (Avalonia 11 moved the brush off <c>DrawingContext.DrawText</c>
    /// onto the <see cref="FormattedText"/> itself). Sets
    /// <see cref="FormattedText.TextAlignment"/> and, when finite,
    /// <see cref="FormattedText.MaxTextWidth"/> so word wrap behaves the
    /// way the legacy <c>TextWrapping</c> + <c>Size constraint</c> args
    /// used to control it.
    /// </summary>
    public static FormattedText Make(
        string text,
        Typeface typeface,
        double size,
        IBrush brush,
        TextAlignment alignment = TextAlignment.Left,
        double maxWidth = double.PositiveInfinity)
    {
        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush);
        ft.TextAlignment = alignment;
        if (!double.IsPositiveInfinity(maxWidth))
            ft.MaxTextWidth = maxWidth;
        return ft;
    }
}

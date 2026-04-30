using System;

namespace MapSlopper.Core.Heightmap;

/// <summary>
/// "Levels" window: maps a 16-bit height value into a 0..1 display intensity
/// by linearly stretching the range <c>[DisplayMin, DisplayMax]</c>.
/// Does not modify any underlying heightmap data.
/// </summary>
public sealed class HeightmapLevels
{
    public ushort DisplayMin { get; set; }
    public ushort DisplayMax { get; set; } = ushort.MaxValue;

    public double NormalizeForDisplay(ushort value)
    {
        if (DisplayMax <= DisplayMin) return 0.0;
        if (value <= DisplayMin) return 0.0;
        if (value >= DisplayMax) return 1.0;
        return (value - DisplayMin) / (double)(DisplayMax - DisplayMin);
    }

    public byte ToByte(ushort value) =>
        (byte)Math.Round(NormalizeForDisplay(value) * 255.0);
}

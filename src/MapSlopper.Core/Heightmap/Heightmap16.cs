using System;
using MapSlopper.Core.Geometry;

namespace MapSlopper.Core.Heightmap;

/// <summary>
/// 16-bit grid heightmap. One unit of height equals one Quake unit. The grid
/// is laid out so that cell <c>(0,0)</c> occupies the world rectangle starting
/// at <see cref="Origin"/> with size <see cref="CellSize"/>.
/// </summary>
public sealed class Heightmap16
{
    public int Width { get; }
    public int Height { get; }
    public double CellSize { get; }
    public Vec2 Origin { get; }
    public ushort[] Data { get; }

    public Heightmap16(int width, int height, double cellSize, Vec2 origin)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
        Width = width;
        Height = height;
        CellSize = cellSize;
        Origin = origin;
        Data = new ushort[width * height];
    }

    public Heightmap16(int width, int height, double cellSize, Vec2 origin, ushort[] data)
        : this(width, height, cellSize, origin)
    {
        if (data.Length != width * height)
            throw new ArgumentException(
                $"Data length {data.Length} does not match {width}*{height}.", nameof(data));
        Array.Copy(data, Data, data.Length);
    }

    /// <summary>Raised whenever the heightmap is mutated.</summary>
    public event Action? Changed;

    public ushort this[int x, int y]
    {
        get => Sample(x, y);
        set => Set(x, y, value);
    }

    public ushort Sample(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return 0;
        return Data[y * Width + x];
    }

    public void Set(int x, int y, ushort value)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        Data[y * Width + x] = value;
        Changed?.Invoke();
    }

    /// <summary>
    /// Fills the inclusive rectangle <c>[x0..x1] × [y0..y1]</c> (clipped to
    /// the heightmap bounds) with <paramref name="value"/>.
    /// </summary>
    public void Fill(int x0, int y0, int x1, int y1, ushort value)
    {
        if (x0 > x1) (x0, x1) = (x1, x0);
        if (y0 > y1) (y0, y1) = (y1, y0);
        x0 = Math.Max(0, x0);
        y0 = Math.Max(0, y0);
        x1 = Math.Min(Width - 1, x1);
        y1 = Math.Min(Height - 1, y1);
        if (x0 > x1 || y0 > y1) return;
        for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
                Data[y * Width + x] = value;
        Changed?.Invoke();
    }

    /// <summary>Returns a clone of this heightmap (deep copy of data).</summary>
    public Heightmap16 Clone() => new(Width, Height, CellSize, Origin, Data);

    public Vec2 CellWorldMin(int x, int y) =>
        new(Origin.X + x * CellSize, Origin.Y + y * CellSize);

    public Vec2 CellWorldMax(int x, int y) =>
        new(Origin.X + (x + 1) * CellSize, Origin.Y + (y + 1) * CellSize);

    /// <summary>Convert a world-space XY point to integer cell coordinates.</summary>
    public (int X, int Y) WorldToCell(Vec2 worldXy)
    {
        var cx = (int)Math.Floor((worldXy.X - Origin.X) / CellSize);
        var cy = (int)Math.Floor((worldXy.Y - Origin.Y) / CellSize);
        return (cx, cy);
    }

    /// <summary>World-space bounds of the heightmap rectangle.</summary>
    public (Vec2 Min, Vec2 Max) WorldBounds() => (
        Origin,
        new Vec2(Origin.X + Width * CellSize, Origin.Y + Height * CellSize));
}

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
    public int Width { get; private set; }
    public int Height { get; private set; }
    public double CellSize { get; }
    public Vec2 Origin { get; private set; }
    public ushort[] Data { get; private set; }

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

    /// <summary>
    /// Grows the heightmap (in-place) so that the world rectangle
    /// <c>[worldMin..worldMax]</c> is fully covered by cells. Existing data is
    /// preserved -- the heightmap NEVER shrinks. Returns true if the bounds
    /// or data were actually expanded. Adds a small <paramref name="paddingCells"/>
    /// margin so the polygon outline never sits flush against the edge.
    /// </summary>
    public bool GrowToInclude(Vec2 worldMin, Vec2 worldMax, int paddingCells = 2)
    {
        var (mn, mx) = WorldBounds();
        // Required new origin / extent -- only ever grows outward.
        var newOriginX = Math.Min(mn.X, worldMin.X - paddingCells * CellSize);
        var newOriginY = Math.Min(mn.Y, worldMin.Y - paddingCells * CellSize);
        var newMaxX    = Math.Max(mx.X, worldMax.X + paddingCells * CellSize);
        var newMaxY    = Math.Max(mx.Y, worldMax.Y + paddingCells * CellSize);
        // Snap origin to a multiple of CellSize relative to the old origin so
        // existing cells line up at integer offsets and we can copy verbatim.
        var dx = (int)Math.Ceiling((mn.X - newOriginX) / CellSize);
        var dy = (int)Math.Ceiling((mn.Y - newOriginY) / CellSize);
        newOriginX = mn.X - dx * CellSize;
        newOriginY = mn.Y - dy * CellSize;
        var newW = Math.Max(Width + dx, (int)Math.Ceiling((newMaxX - newOriginX) / CellSize));
        var newH = Math.Max(Height + dy, (int)Math.Ceiling((newMaxY - newOriginY) / CellSize));
        if (newW == Width && newH == Height && dx == 0 && dy == 0) return false;

        var newData = new ushort[newW * newH];
        for (var y = 0; y < Height; y++)
        {
            Array.Copy(Data, y * Width, newData, (y + dy) * newW + dx, Width);
        }
        Width = newW;
        Height = newH;
        Origin = new Vec2(newOriginX, newOriginY);
        Data = newData;
        Changed?.Invoke();
        return true;
    }
}

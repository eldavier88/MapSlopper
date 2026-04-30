using System;
using System.Globalization;
using System.IO;
using System.Text;
using MapSlopper.Core.Brushes;
using MapSlopper.Core.Geometry;

namespace MapSlopper.Core.Export;

/// <summary>
/// Writes a <see cref="MapDocument"/> as Quake 3 .map text. The format
/// matches the canonical NetRadiant output observed in real shipping maps:
/// integers are emitted bare, non-integral numbers are emitted with six
/// decimal places using <see cref="CultureInfo.InvariantCulture"/>.
/// </summary>
public static class MapWriter
{
    public static string Write(MapDocument doc)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < doc.Entities.Count; i++)
        {
            sb.Append("// entity ").Append(i).Append('\n');
            sb.Append('{').Append('\n');
            WriteEntity(sb, doc.Entities[i]);
            sb.Append('}').Append('\n');
        }
        return sb.ToString();
    }

    public static void WriteToFile(MapDocument doc, string path) =>
        File.WriteAllText(path, Write(doc));

    private static void WriteEntity(StringBuilder sb, MapEntity entity)
    {
        // Emit "classname" first (Q3 convention), then remaining properties in insertion order.
        if (entity.Properties.TryGetValue("classname", out var cn))
            WriteKv(sb, "classname", cn);
        foreach (var kv in entity.Properties)
        {
            if (kv.Key == "classname") continue;
            WriteKv(sb, kv.Key, kv.Value);
        }

        for (var i = 0; i < entity.Brushes.Count; i++)
        {
            sb.Append("// brush ").Append(i).Append('\n');
            sb.Append('{').Append('\n');
            WriteBrush(sb, entity.Brushes[i]);
            sb.Append('}').Append('\n');
        }
    }

    private static void WriteKv(StringBuilder sb, string key, string value)
    {
        sb.Append('"').Append(key).Append("\" \"").Append(value).Append('"').Append('\n');
    }

    private static void WriteBrush(StringBuilder sb, Brush brush)
    {
        foreach (var p in brush.Planes)
            WritePlane(sb, p);
    }

    private static void WritePlane(StringBuilder sb, Plane p)
    {
        WritePoint(sb, p.P1); sb.Append(' ');
        WritePoint(sb, p.P2); sb.Append(' ');
        WritePoint(sb, p.P3); sb.Append(' ');
        sb.Append(p.Texture).Append(' ');
        WriteNumber(sb, p.ShiftS); sb.Append(' ');
        WriteNumber(sb, p.ShiftT); sb.Append(' ');
        WriteNumber(sb, p.Rotate); sb.Append(' ');
        WriteNumber(sb, p.ScaleS); sb.Append(' ');
        WriteNumber(sb, p.ScaleT); sb.Append(' ');
        sb.Append(p.ContentFlags).Append(' ');
        sb.Append(p.SurfaceFlags).Append(' ');
        sb.Append(p.Value);
        sb.Append('\n');
    }

    private static void WritePoint(StringBuilder sb, Vec3 v)
    {
        sb.Append('(').Append(' ');
        WriteNumber(sb, v.X); sb.Append(' ');
        WriteNumber(sb, v.Y); sb.Append(' ');
        WriteNumber(sb, v.Z); sb.Append(' ');
        sb.Append(')');
    }

    /// <summary>
    /// Emit an integer if the value is exactly integral and small enough,
    /// otherwise a 6-digit fixed decimal. Always invariant culture.
    /// </summary>
    private static void WriteNumber(StringBuilder sb, double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
            throw new InvalidOperationException("Map writer received non-finite value.");

        if (Math.Abs(v) < 1e9 && Math.Truncate(v) == v)
        {
            sb.Append(((long)v).ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            sb.Append(v.ToString("0.000000", CultureInfo.InvariantCulture));
        }
    }
}

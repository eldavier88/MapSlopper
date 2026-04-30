using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;
using MapSlopper.Core.Outline;
using MapSlopper.Core.Triggers;

namespace MapSlopper.Core.Project;

/// <summary>
/// JSON serializer for <see cref="MapSlopperProject"/>. Writes a stable,
/// human-readable shape; the heightmap data is stored as a base64-encoded
/// little-endian ushort buffer.
/// </summary>
public static class ProjectJsonIo
{
    private static readonly JsonSerializerOptions s_options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            IncludeFields = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        o.Converters.Add(new MapSlopperProjectConverter());
        o.Converters.Add(new OutlineGraphConverter());
        o.Converters.Add(new Heightmap16Converter());
        o.Converters.Add(new Vec2Converter());
        o.Converters.Add(new Vec3Converter());
        o.Converters.Add(new TriggerTypeConfigConverter());
        return o;
    }

    public static string Serialize(MapSlopperProject project) =>
        JsonSerializer.Serialize(project, s_options);

    public static MapSlopperProject Deserialize(string json) =>
        JsonSerializer.Deserialize<MapSlopperProject>(json, s_options)
            ?? throw new InvalidDataException("Project JSON deserialized to null.");

    public static void Save(MapSlopperProject project, string path) =>
        File.WriteAllText(path, Serialize(project));

    public static MapSlopperProject Load(string path) =>
        Deserialize(File.ReadAllText(path));
}

internal sealed class Vec2Converter : JsonConverter<Vec2>
{
    public override Vec2 Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        double x = 0, y = 0;
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return new Vec2(x, y);
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "x": x = reader.GetDouble(); break;
                case "y": y = reader.GetDouble(); break;
            }
        }
        throw new JsonException();
    }
    public override void Write(Utf8JsonWriter w, Vec2 v, JsonSerializerOptions o)
    {
        w.WriteStartObject();
        w.WriteNumber("x", v.X);
        w.WriteNumber("y", v.Y);
        w.WriteEndObject();
    }
}

internal sealed class Vec3Converter : JsonConverter<Vec3>
{
    public override Vec3 Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        double x = 0, y = 0, z = 0;
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return new Vec3(x, y, z);
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "x": x = reader.GetDouble(); break;
                case "y": y = reader.GetDouble(); break;
                case "z": z = reader.GetDouble(); break;
            }
        }
        throw new JsonException();
    }
    public override void Write(Utf8JsonWriter w, Vec3 v, JsonSerializerOptions o)
    {
        w.WriteStartObject();
        w.WriteNumber("x", v.X);
        w.WriteNumber("y", v.Y);
        w.WriteNumber("z", v.Z);
        w.WriteEndObject();
    }
}

internal sealed class OutlineGraphConverter : JsonConverter<OutlineGraph>
{
    public override OutlineGraph Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        var g = new OutlineGraph();
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        var pendingEdges = new List<(Guid, Guid)>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            var prop = reader.GetString();
            reader.Read();
            if (prop == "points")
            {
                if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    Guid id = Guid.Empty;
                    double x = 0, y = 0;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName) continue;
                        var n = reader.GetString();
                        reader.Read();
                        switch (n)
                        {
                            case "id": id = Guid.Parse(reader.GetString()!); break;
                            case "x": x = reader.GetDouble(); break;
                            case "y": y = reader.GetDouble(); break;
                        }
                    }
                    g.AddPoint(id, new Vec2(x, y));
                }
            }
            else if (prop == "edges")
            {
                if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    Guid a = Guid.Empty, b = Guid.Empty;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName) continue;
                        var n = reader.GetString();
                        reader.Read();
                        switch (n)
                        {
                            case "a": a = Guid.Parse(reader.GetString()!); break;
                            case "b": b = Guid.Parse(reader.GetString()!); break;
                        }
                    }
                    pendingEdges.Add((a, b));
                }
            }
        }
        foreach (var (a, b) in pendingEdges) g.AddEdge(a, b);
        return g;
    }

    public override void Write(Utf8JsonWriter w, OutlineGraph g, JsonSerializerOptions o)
    {
        w.WriteStartObject();
        w.WriteStartArray("points");
        foreach (var p in g.Points.Values)
        {
            w.WriteStartObject();
            w.WriteString("id", p.Id.ToString());
            w.WriteNumber("x", p.Position.X);
            w.WriteNumber("y", p.Position.Y);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("edges");
        foreach (var e in g.Edges)
        {
            w.WriteStartObject();
            w.WriteString("a", e.A.ToString());
            w.WriteString("b", e.B.ToString());
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }
}

internal sealed class Heightmap16Converter : JsonConverter<Heightmap16>
{
    public override Heightmap16 Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        int width = 0, height = 0;
        double cellSize = 32, ox = 0, oy = 0;
        string? base64 = null;
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "width": width = reader.GetInt32(); break;
                case "height": height = reader.GetInt32(); break;
                case "cellSize": cellSize = reader.GetDouble(); break;
                case "originX": ox = reader.GetDouble(); break;
                case "originY": oy = reader.GetDouble(); break;
                case "dataBase64": base64 = reader.GetString(); break;
            }
        }
        var data = new ushort[width * height];
        if (!string.IsNullOrEmpty(base64))
        {
            var bytes = Convert.FromBase64String(base64);
            if (bytes.Length != data.Length * 2)
                throw new JsonException("Heightmap base64 length mismatch.");
            for (var i = 0; i < data.Length; i++)
                data[i] = (ushort)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
        }
        return new Heightmap16(width, height, cellSize, new Vec2(ox, oy), data);
    }

    public override void Write(Utf8JsonWriter w, Heightmap16 hm, JsonSerializerOptions o)
    {
        var bytes = new byte[hm.Data.Length * 2];
        for (var i = 0; i < hm.Data.Length; i++)
        {
            bytes[i * 2] = (byte)(hm.Data[i] & 0xFF);
            bytes[i * 2 + 1] = (byte)((hm.Data[i] >> 8) & 0xFF);
        }
        w.WriteStartObject();
        w.WriteNumber("width", hm.Width);
        w.WriteNumber("height", hm.Height);
        w.WriteNumber("cellSize", hm.CellSize);
        w.WriteNumber("originX", hm.Origin.X);
        w.WriteNumber("originY", hm.Origin.Y);
        w.WriteString("dataBase64", Convert.ToBase64String(bytes));
        w.WriteEndObject();
    }
}

internal sealed class MapSlopperProjectConverter : JsonConverter<MapSlopperProject>
{
    public override MapSlopperProject Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        var p = new MapSlopperProject();
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return p;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "formatVersion": p.FormatVersion = reader.GetInt32(); break;
                case "outline": p.Outline = JsonSerializer.Deserialize<OutlineGraph>(ref reader, o)!; break;
                case "heightmap": p.Heightmap = JsonSerializer.Deserialize<Heightmap16>(ref reader, o)!; break;
                case "triggerLayer": p.TriggerLayer = JsonSerializer.Deserialize<Heightmap16>(ref reader, o)!; break;
                case "triggerOverrides":
                    p.TriggerOverrides = reader.TokenType == JsonTokenType.Null
                        ? null
                        : JsonSerializer.Deserialize<TriggerTypeConfig>(ref reader, o);
                    break;
                case "ceilingHeight": p.CeilingHeight = reader.GetDouble(); break;
                case "wallThickness": p.WallThickness = reader.GetDouble(); break;
                case "floorTexture": p.FloorTexture = reader.GetString() ?? p.FloorTexture; break;
                case "wallTexture": p.WallTexture = reader.GetString() ?? p.WallTexture; break;
                case "ceilingTexture": p.CeilingTexture = reader.GetString() ?? p.CeilingTexture; break;
                case "windowTexture": p.WindowTexture = reader.GetString() ?? p.WindowTexture; break;
                case "wallSplitHeight":
                    p.WallSplitHeight = reader.TokenType == JsonTokenType.Null
                        ? null
                        : reader.GetDouble();
                    break;
                case "playerStartOverride":
                    p.PlayerStartOverride = reader.TokenType == JsonTokenType.Null
                        ? null
                        : JsonSerializer.Deserialize<Vec3>(ref reader, o);
                    break;
                case "lightSpacing": p.LightSpacing = reader.GetDouble(); break;
                case "lightIntensity": p.LightIntensity = reader.GetDouble(); break;
                case "lightInsetFromCeiling": p.LightInsetFromCeiling = reader.GetDouble(); break;
                case "ceilingThickness": p.CeilingThickness = reader.GetDouble(); break;
                case "floorBaseThickness": p.FloorBaseThickness = reader.GetDouble(); break;
                default: reader.Skip(); break;
            }
        }
        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter w, MapSlopperProject p, JsonSerializerOptions o)
    {
        w.WriteStartObject();
        w.WriteNumber("formatVersion", p.FormatVersion);
        w.WritePropertyName("outline");
        JsonSerializer.Serialize(w, p.Outline, o);
        w.WritePropertyName("heightmap");
        JsonSerializer.Serialize(w, p.Heightmap, o);
        w.WritePropertyName("triggerLayer");
        JsonSerializer.Serialize(w, p.TriggerLayer, o);
        w.WritePropertyName("triggerOverrides");
        if (p.TriggerOverrides is null) w.WriteNullValue();
        else JsonSerializer.Serialize(w, p.TriggerOverrides, o);
        w.WriteNumber("ceilingHeight", p.CeilingHeight);
        w.WriteNumber("wallThickness", p.WallThickness);
        w.WriteString("floorTexture", p.FloorTexture);
        w.WriteString("wallTexture", p.WallTexture);
        w.WriteString("ceilingTexture", p.CeilingTexture);
        w.WriteString("windowTexture", p.WindowTexture);
        w.WritePropertyName("wallSplitHeight");
        if (p.WallSplitHeight is null) w.WriteNullValue();
        else w.WriteNumberValue(p.WallSplitHeight.Value);
        w.WritePropertyName("playerStartOverride");
        if (p.PlayerStartOverride is null) w.WriteNullValue();
        else JsonSerializer.Serialize(w, p.PlayerStartOverride.Value, o);
        w.WriteNumber("lightSpacing", p.LightSpacing);
        w.WriteNumber("lightIntensity", p.LightIntensity);
        w.WriteNumber("lightInsetFromCeiling", p.LightInsetFromCeiling);
        w.WriteNumber("ceilingThickness", p.CeilingThickness);
        w.WriteNumber("floorBaseThickness", p.FloorBaseThickness);
        w.WriteEndObject();
    }
}

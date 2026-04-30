using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MapSlopper.Core.Triggers;

/// <summary>
/// JSON I/O for <see cref="TriggerTypeConfig"/>. Stable shape:
/// <code>
/// {
///   "types": [
///     {
///       "id": 1, "name": "Start Timer", "colorHex": "#33CC33",
///       "texture": "system/trigger",
///       "entityProperties": { "classname": "trigger_multiple" },
///       "targets": [
///         { "linkKey": "target",
///           "properties": { "classname": "target_startTimer" } }
///       ]
///     }
///   ]
/// }
/// </code>
/// </summary>
public static class TriggerTypeConfigJson
{
    private static readonly JsonSerializerOptions s_options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        o.Converters.Add(new TriggerTypeConfigConverter());
        return o;
    }

    public static string Serialize(TriggerTypeConfig cfg) =>
        JsonSerializer.Serialize(cfg, s_options);

    public static TriggerTypeConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<TriggerTypeConfig>(json, s_options)
            ?? throw new InvalidDataException("Trigger config JSON deserialized to null.");

    public static TriggerTypeConfig Load(string path) => Deserialize(File.ReadAllText(path));

    /// <summary>
    /// Loads program-wide config from <paramref name="path"/> if it exists,
    /// otherwise returns <see cref="TriggerTypeConfig.BuiltInDefault"/>.
    /// </summary>
    public static TriggerTypeConfig LoadOrDefault(string path)
    {
        if (File.Exists(path))
        {
            try { return Load(path); }
            catch { /* fall through to default */ }
        }
        return TriggerTypeConfig.BuiltInDefault();
    }

    public static void Save(TriggerTypeConfig cfg, string path) =>
        File.WriteAllText(path, Serialize(cfg));
}

internal sealed class TriggerTypeConfigConverter : JsonConverter<TriggerTypeConfig>
{
    public override TriggerTypeConfig Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        var cfg = new TriggerTypeConfig();
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return cfg;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            var name = reader.GetString();
            reader.Read();
            if (name == "types")
            {
                if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    cfg.Types.Add(ReadType(ref reader));
            }
            else
            {
                reader.Skip();
            }
        }
        throw new JsonException();
    }

    private static TriggerType ReadType(ref Utf8JsonReader reader)
    {
        var t = new TriggerType();
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return t;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            var n = reader.GetString();
            reader.Read();
            switch (n)
            {
                case "id": t.Id = (byte)reader.GetInt32(); break;
                case "name": t.Name = reader.GetString() ?? ""; break;
                case "colorHex": t.ColorHex = reader.GetString() ?? "#FFFFFF"; break;
                case "texture": t.Texture = reader.GetString() ?? "system/trigger"; break;
                case "entityProperties": t.EntityProperties = ReadStringMap(ref reader); break;
                case "targets":
                    if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        t.Targets.Add(ReadTarget(ref reader));
                    break;
                default: reader.Skip(); break;
            }
        }
        throw new JsonException();
    }

    private static TriggerTargetSpec ReadTarget(ref Utf8JsonReader reader)
    {
        var s = new TriggerTargetSpec();
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return s;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            var n = reader.GetString();
            reader.Read();
            switch (n)
            {
                case "linkKey": s.LinkKey = reader.GetString() ?? "target"; break;
                case "properties": s.Properties = ReadStringMap(ref reader); break;
                default: reader.Skip(); break;
            }
        }
        throw new JsonException();
    }

    private static Dictionary<string, string> ReadStringMap(ref Utf8JsonReader reader)
    {
        var d = new Dictionary<string, string>();
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return d;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            var k = reader.GetString() ?? "";
            reader.Read();
            d[k] = reader.GetString() ?? "";
        }
        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter w, TriggerTypeConfig cfg, JsonSerializerOptions o)
    {
        w.WriteStartObject();
        w.WriteStartArray("types");
        foreach (var t in cfg.Types) WriteType(w, t);
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WriteType(Utf8JsonWriter w, TriggerType t)
    {
        w.WriteStartObject();
        w.WriteNumber("id", t.Id);
        w.WriteString("name", t.Name);
        w.WriteString("colorHex", t.ColorHex);
        w.WriteString("texture", t.Texture);
        w.WriteStartObject("entityProperties");
        foreach (var kv in t.EntityProperties) w.WriteString(kv.Key, kv.Value);
        w.WriteEndObject();
        w.WriteStartArray("targets");
        foreach (var s in t.Targets)
        {
            w.WriteStartObject();
            w.WriteString("linkKey", s.LinkKey);
            w.WriteStartObject("properties");
            foreach (var kv in s.Properties) w.WriteString(kv.Key, kv.Value);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }
}

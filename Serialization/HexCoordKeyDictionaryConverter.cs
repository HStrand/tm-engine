using System;
using System.Collections.Immutable;
using System.Reflection;
using Newtonsoft.Json;
using TmEngine.Domain.Models;

namespace tm_engine.Serialization;

/// <summary>
/// Serializes ImmutableDictionary&lt;HexCoord, T&gt; with "col,row" string keys
/// instead of the default complex object key serialization.
/// </summary>
public class HexCoordKeyDictionaryConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        if (!objectType.IsGenericType)
            return false;
        var def = objectType.GetGenericTypeDefinition();
        return def == typeof(ImmutableDictionary<,>) &&
               objectType.GetGenericArguments()[0] == typeof(HexCoord);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value == null) { writer.WriteNull(); return; }

        writer.WriteStartObject();

        // Use the non-generic IDictionary<HexCoord, T> via reflection
        var type = value.GetType();
        var valueType = type.GetGenericArguments()[1];
        var enumerator = ((System.Collections.IEnumerable)value).GetEnumerator();

        while (enumerator.MoveNext())
        {
            var kvp = enumerator.Current!;
            var kvpType = kvp.GetType();
            var key = (HexCoord)kvpType.GetProperty("Key")!.GetValue(kvp)!;
            var val = kvpType.GetProperty("Value")!.GetValue(kvp);

            writer.WritePropertyName($"{key.Col},{key.Row}");
            serializer.Serialize(writer, val);
        }

        writer.WriteEndObject();
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        var valueType = objectType.GetGenericArguments()[1];
        var method = typeof(HexCoordKeyDictionaryConverter)
            .GetMethod(nameof(ReadTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(valueType);

        return method.Invoke(null, [reader, serializer]);
    }

    private static ImmutableDictionary<HexCoord, T> ReadTyped<T>(JsonReader reader, JsonSerializer serializer)
    {
        var builder = ImmutableDictionary.CreateBuilder<HexCoord, T>();

        reader.Read(); // move past StartObject
        while (reader.TokenType == JsonToken.PropertyName)
        {
            var keyStr = (string)reader.Value!;
            var parts = keyStr.Split(',');
            var coord = new HexCoord(int.Parse(parts[0]), int.Parse(parts[1]));

            reader.Read(); // move to value
            var val = serializer.Deserialize<T>(reader)!;
            builder.Add(coord, val);

            reader.Read(); // move to next property or EndObject
        }

        return builder.ToImmutable();
    }
}

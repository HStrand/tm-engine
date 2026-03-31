using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TmEngine.Domain.Cards;

namespace tm_engine.Serialization;

/// <summary>
/// Polymorphic JSON converter for the VictoryPoints hierarchy.
/// Adds a "type" discriminator on serialization.
/// </summary>
public class VictoryPointsJsonConverter : JsonConverter<VictoryPoints>
{
    public override VictoryPoints? ReadJson(JsonReader reader, Type objectType, VictoryPoints? existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        var obj = JObject.Load(reader);
        var type = obj["type"]?.Value<string>();
        if (type == null) return null;

        var inner = CreateInnerSerializer(serializer);
        return type switch
        {
            "Fixed" => obj.ToObject<FixedVictoryPoints>(inner),
            "PerResource" => obj.ToObject<PerResourceVictoryPoints>(inner),
            "PerTag" => obj.ToObject<PerTagVictoryPoints>(inner),
            _ => null,
        };
    }

    public override void WriteJson(JsonWriter writer, VictoryPoints? value, JsonSerializer serializer)
    {
        if (value == null) { writer.WriteNull(); return; }

        var inner = CreateInnerSerializer(serializer);
        var obj = JObject.FromObject(value, inner);

        var typeName = value switch
        {
            FixedVictoryPoints => "Fixed",
            PerResourceVictoryPoints => "PerResource",
            PerTagVictoryPoints => "PerTag",
            _ => value.GetType().Name,
        };

        obj.AddFirst(new JProperty("type", typeName));
        obj.WriteTo(writer);
    }

    private static JsonSerializer CreateInnerSerializer(JsonSerializer outer)
    {
        var settings = new JsonSerializerSettings
        {
            ContractResolver = outer.ContractResolver,
            NullValueHandling = outer.NullValueHandling,
            Formatting = outer.Formatting,
        };
        foreach (var conv in outer.Converters)
        {
            if (conv is not VictoryPointsJsonConverter)
                settings.Converters.Add(conv);
        }
        return JsonSerializer.Create(settings);
    }
}

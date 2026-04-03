using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TmEngine.Domain.Models;

namespace tm_engine.Serialization;

/// <summary>
/// Polymorphic JSON converter for the PendingAction hierarchy.
/// Adds a "type" discriminator on serialization so clients know which sub-move to submit.
/// </summary>
public class PendingActionJsonConverter : JsonConverter<PendingAction>
{
    public override PendingAction? ReadJson(JsonReader reader, Type objectType, PendingAction? existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        var obj = JObject.Load(reader);
        var type = obj["type"]?.Value<string>();
        if (type == null) return null;

        return type switch
        {
            "PlaceTile" => obj.ToObject<PlaceTilePending>(CreateInnerSerializer(serializer)),
            "RemoveResource" => obj.ToObject<RemoveResourcePending>(CreateInnerSerializer(serializer)),
            "AddCardResource" => obj.ToObject<AddCardResourcePending>(CreateInnerSerializer(serializer)),
            "ChooseOption" => obj.ToObject<ChooseOptionPending>(CreateInnerSerializer(serializer)),
            "ReduceProduction" => obj.ToObject<ReduceProductionPending>(CreateInnerSerializer(serializer)),
            "DiscardCards" => obj.ToObject<DiscardCardsPending>(CreateInnerSerializer(serializer)),
            "BuyCards" => obj.ToObject<BuyCardsPending>(CreateInnerSerializer(serializer)),
            "ClaimLand" => obj.ToObject<ClaimLandPending>(CreateInnerSerializer(serializer)),
            "PlayCardFromHand" => obj.ToObject<PlayCardFromHandPending>(CreateInnerSerializer(serializer)),
            "ChooseCardToPlay" => obj.ToObject<ChooseCardToPlayPending>(CreateInnerSerializer(serializer)),
            "Setup" => obj.ToObject<SetupPending>(CreateInnerSerializer(serializer)),
            "ChooseEffectOrder" => obj.ToObject<ChooseEffectOrderPending>(CreateInnerSerializer(serializer)),
            _ => null,
        };
    }

    public override void WriteJson(JsonWriter writer, PendingAction? value, JsonSerializer serializer)
    {
        if (value == null) { writer.WriteNull(); return; }

        // Use an inner serializer that skips this converter to avoid infinite recursion
        var inner = CreateInnerSerializer(serializer);
        var obj = JObject.FromObject(value, inner);

        var typeName = value switch
        {
            PlaceTilePending => "PlaceTile",
            RemoveResourcePending => "RemoveResource",
            AddCardResourcePending => "AddCardResource",
            ChooseOptionPending => "ChooseOption",
            ReduceProductionPending => "ReduceProduction",
            DiscardCardsPending => "DiscardCards",
            BuyCardsPending => "BuyCards",
            ClaimLandPending => "ClaimLand",
            PlayCardFromHandPending => "PlayCardFromHand",
            ChooseCardToPlayPending => "ChooseCardToPlay",
            SetupPending => "Setup",
            ChooseEffectOrderPending => "ChooseEffectOrder",
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
            if (conv is not PendingActionJsonConverter)
                settings.Converters.Add(conv);
        }
        return JsonSerializer.Create(settings);
    }
}

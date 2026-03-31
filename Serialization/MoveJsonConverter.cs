using System;
using System.Collections.Immutable;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TmEngine.Domain.Models;
using TmEngine.Domain.Moves;

namespace tm_engine.Serialization;

/// <summary>
/// Polymorphic JSON converter for the Move hierarchy.
/// Reads a "type" discriminator to determine the concrete Move subtype.
/// </summary>
public class MoveJsonConverter : JsonConverter<Move>
{
    public override Move? ReadJson(JsonReader reader, Type objectType, Move? existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        var obj = JObject.Load(reader);
        var type = obj["type"]?.Value<string>()
            ?? throw new JsonSerializationException("Move JSON must include a 'type' field.");
        var playerId = obj["playerId"]?.Value<int>()
            ?? throw new JsonSerializationException("Move JSON must include a 'playerId' field.");

        return type switch
        {
            "Setup" => new SetupMove(
                playerId,
                obj["corporationId"]!.Value<string>()!,
                ReadStringArray(obj, "preludeIds"),
                ReadStringArray(obj, "cardIdsToBuy")),

            "DraftCard" => new DraftCardMove(playerId, obj["cardId"]!.Value<string>()!),

            "BuyCards" => new BuyCardsMove(playerId, ReadStringArray(obj, "cardIds")),

            "PlayCard" => new PlayCardMove(
                playerId,
                obj["cardId"]!.Value<string>()!,
                ReadPayment(obj["payment"])),

            "UseStandardProject" => new UseStandardProjectMove(
                playerId,
                Enum.Parse<StandardProject>(obj["project"]!.Value<string>()!, ignoreCase: true),
                ReadStringArray(obj, "cardsToDiscard"),
                ReadHexCoord(obj["location"])),

            "UseCardAction" => new UseCardActionMove(playerId, obj["cardId"]!.Value<string>()!),

            "ClaimMilestone" => new ClaimMilestoneMove(playerId, obj["milestoneName"]!.Value<string>()!),

            "FundAward" => new FundAwardMove(playerId, obj["awardName"]!.Value<string>()!),

            "ConvertPlants" => new ConvertPlantsMove(playerId, ReadRequiredHexCoord(obj["location"])),

            "ConvertHeat" => new ConvertHeatMove(playerId),

            "Pass" => new PassMove(playerId),

            "PerformFirstAction" => new PerformFirstActionMove(playerId),

            "PlaceTile" => new PlaceTileMove(playerId, ReadRequiredHexCoord(obj["location"])),

            "ChooseTargetPlayer" => new ChooseTargetPlayerMove(
                playerId, obj["targetPlayerId"]!.Value<int>()),

            "SelectCard" => new SelectCardMove(playerId, obj["cardId"]!.Value<string>()!),

            "ChooseOption" => new ChooseOptionMove(playerId, obj["optionIndex"]!.Value<int>()),

            "DiscardCards" => new DiscardCardsMove(playerId, ReadStringArray(obj, "cardIds")),

            _ => throw new JsonSerializationException($"Unknown move type: '{type}'"),
        };
    }

    public override void WriteJson(JsonWriter writer, Move? value, JsonSerializer serializer)
    {
        if (value == null) { writer.WriteNull(); return; }

        var obj = JObject.FromObject(value, JsonSerializer.CreateDefault(new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter(new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy()) },
        }));

        var typeName = value switch
        {
            SetupMove => "Setup",
            DraftCardMove => "DraftCard",
            BuyCardsMove => "BuyCards",
            PlayCardMove => "PlayCard",
            UseStandardProjectMove => "UseStandardProject",
            UseCardActionMove => "UseCardAction",
            ClaimMilestoneMove => "ClaimMilestone",
            FundAwardMove => "FundAward",
            ConvertPlantsMove => "ConvertPlants",
            ConvertHeatMove => "ConvertHeat",
            PassMove => "Pass",
            PerformFirstActionMove => "PerformFirstAction",
            PlaceTileMove => "PlaceTile",
            ChooseTargetPlayerMove => "ChooseTargetPlayer",
            SelectCardMove => "SelectCard",
            ChooseOptionMove => "ChooseOption",
            DiscardCardsMove => "DiscardCards",
            _ => value.GetType().Name,
        };

        obj.AddFirst(new JProperty("type", typeName));
        obj.WriteTo(writer);
    }

    private static ImmutableArray<string> ReadStringArray(JObject obj, string prop)
    {
        var arr = obj[prop] as JArray;
        if (arr == null) return ImmutableArray<string>.Empty;
        return [.. arr.Select(t => t.Value<string>()!)];
    }

    private static PaymentInfo ReadPayment(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return PaymentInfo.Zero;
        return new PaymentInfo(
            token["megaCredits"]?.Value<int>() ?? 0,
            token["steel"]?.Value<int>() ?? 0,
            token["titanium"]?.Value<int>() ?? 0,
            token["heat"]?.Value<int>() ?? 0);
    }

    private static HexCoord? ReadHexCoord(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return null;
        return new HexCoord(token["col"]!.Value<int>(), token["row"]!.Value<int>());
    }

    private static HexCoord ReadRequiredHexCoord(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            throw new JsonSerializationException("Required location field is missing.");
        return new HexCoord(token["col"]!.Value<int>(), token["row"]!.Value<int>());
    }
}

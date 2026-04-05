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
        if (reader.TokenType == JsonToken.Null)
            return null;

        var obj = JObject.Load(reader);
        var type = obj["type"]?.Value<string>()
            ?? throw new JsonSerializationException("Move JSON must include a 'type' field.");
        var playerId = obj["playerId"]?.Value<int>()
            ?? throw new JsonSerializationException("Move JSON must include a 'playerId' field.");

        return type.ToLowerInvariant() switch
        {
            "setup" => new SetupMove(
                playerId,
                obj["corporationId"]!.Value<string>()!,
                ReadStringArray(obj, "preludeIds"),
                ReadStringArray(obj, "cardIdsToBuy")),

            "draftcard" => new DraftCardMove(playerId, obj["cardId"]!.Value<string>()!),

            "buycards" => new BuyCardsMove(playerId, ReadStringArray(obj, "cardIds")),

            "playcard" => new PlayCardMove(
                playerId,
                obj["cardId"]!.Value<string>()!,
                ReadPayment(obj["payment"])),

            "sellpatents" => new SellPatentsMove(playerId, ReadStringArray(obj, "cardIds")),
            "powerplant" => new PowerPlantMove(playerId),
            "asteroid" => new AsteroidMove(playerId),
            "aquifer" => new AquiferMove(playerId, ReadRequiredHexCoord(obj["location"])),
            "greenery" => new GreeneryMove(playerId, ReadRequiredHexCoord(obj["location"])),
            "city" => new CityMove(playerId, ReadRequiredHexCoord(obj["location"])),

            "usecardaction" => new UseCardActionMove(playerId, obj["cardId"]!.Value<string>()!),

            "claimmilestone" => new ClaimMilestoneMove(playerId, obj["milestoneName"]!.Value<string>()!),

            "fundaward" => new FundAwardMove(playerId, obj["awardName"]!.Value<string>()!),

            "convertplants" => new ConvertPlantsMove(playerId, ReadRequiredHexCoord(obj["location"])),

            "convertheat" => new ConvertHeatMove(playerId),

            "pass" => new PassMove(playerId),

            "endturn" => new EndTurnMove(playerId),

            "performfirstaction" => new PerformFirstActionMove(playerId),

            "playprelude" => new PlayPreludeMove(playerId, obj["preludeId"]!.Value<string>()!),

            "placetile" => new PlaceTileMove(playerId, ReadRequiredHexCoord(obj["location"])),

            "choosetargetplayer" => new ChooseTargetPlayerMove(
                playerId, obj["targetPlayerId"]!.Value<int>()),

            "selectcard" => new SelectCardMove(playerId, obj["cardId"]!.Value<string>()!),

            "chooseoption" => new ChooseOptionMove(playerId, obj["optionIndex"]!.Value<int>()),

            "discardcards" => new DiscardCardsMove(playerId, ReadStringArray(obj, "cardIds")),

            "chooseeffectorder" => new ChooseEffectOrderMove(playerId, obj["effectIndex"]!.Value<int>()),

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
            SellPatentsMove => "SellPatents",
            PowerPlantMove => "PowerPlant",
            AsteroidMove => "Asteroid",
            AquiferMove => "Aquifer",
            GreeneryMove => "Greenery",
            CityMove => "City",
            UseCardActionMove => "UseCardAction",
            ClaimMilestoneMove => "ClaimMilestone",
            FundAwardMove => "FundAward",
            ConvertPlantsMove => "ConvertPlants",
            ConvertHeatMove => "ConvertHeat",
            PassMove => "Pass",
            EndTurnMove => "EndTurn",
            PerformFirstActionMove => "PerformFirstAction",
            PlayPreludeMove => "PlayPrelude",
            PlaceTileMove => "PlaceTile",
            ChooseTargetPlayerMove => "ChooseTargetPlayer",
            SelectCardMove => "SelectCard",
            ChooseOptionMove => "ChooseOption",
            DiscardCardsMove => "DiscardCards",
            ChooseEffectOrderMove => "ChooseEffectOrder",
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

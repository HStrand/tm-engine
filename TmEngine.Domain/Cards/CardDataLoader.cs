using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Cards;

/// <summary>
/// Loads card definitions from the embedded cards.json resource.
/// Parses JSON metadata into CardDefinition records, filtering to in-scope expansions.
/// </summary>
public static class CardDataLoader
{
    private static readonly HashSet<string> InScopeExpansions = ["base", "corporate_era", "prelude"];

    /// <summary>
    /// Load all in-scope card definitions from cards.json.
    /// </summary>
    public static ImmutableDictionary<string, CardDefinition> LoadAll()
    {
        var json = ReadEmbeddedResource();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var builder = ImmutableDictionary.CreateBuilder<string, CardDefinition>();

        if (root.TryGetProperty("project_cards", out var projectCards))
        {
            foreach (var card in projectCards.EnumerateArray())
                TryAddCard(builder, card);
        }

        if (root.TryGetProperty("corporations", out var corporations))
        {
            foreach (var card in corporations.EnumerateArray())
                TryAddCard(builder, card);
        }

        if (root.TryGetProperty("preludes", out var preludes))
        {
            foreach (var card in preludes.EnumerateArray())
                TryAddCard(builder, card);
        }

        return builder.ToImmutable();
    }

    private static void TryAddCard(ImmutableDictionary<string, CardDefinition>.Builder builder, JsonElement card)
    {
        var expansion = card.GetProperty("expansion").GetString() ?? "";
        if (!InScopeExpansions.Contains(expansion))
            return;

        var def = ParseCardDefinition(card);
        builder[def.Id] = def;
    }

    private static CardDefinition ParseCardDefinition(JsonElement card)
    {
        var id = card.GetProperty("number").GetString()!;
        var name = card.GetProperty("name").GetString()!;
        var type = ParseCardType(card.GetProperty("type").GetString()!);
        var cost = card.TryGetProperty("cost", out var costProp) ? costProp.GetInt32() : 0;
        var tags = ParseTags(card);
        var expansion = ParseExpansion(card.GetProperty("expansion").GetString()!);
        var requirements = ParseRequirements(card);
        var vp = ParseVictoryPoints(card, id);
        var description = card.TryGetProperty("description", out var descProp)
            ? descProp.GetString() ?? ""
            : "";

        return new CardDefinition
        {
            Id = id,
            Name = name,
            Type = type,
            Cost = cost,
            Tags = tags,
            Expansion = expansion,
            Requirements = requirements,
            VictoryPoints = vp,
            Description = description,
        };
    }

    private static CardType ParseCardType(string type) => type switch
    {
        "automated" => CardType.Automated,
        "active" => CardType.Active,
        "event" => CardType.Event,
        "corporation" => CardType.Corporation,
        "prelude" => CardType.Prelude,
        _ => throw new ArgumentException($"Unknown card type: {type}"),
    };

    private static Expansion ParseExpansion(string expansion) => expansion switch
    {
        "base" => Expansion.Base,
        "corporate_era" => Expansion.CorporateEra,
        "prelude" => Expansion.Prelude,
        _ => Expansion.Base,
    };

    private static ImmutableArray<Tag> ParseTags(JsonElement card)
    {
        if (!card.TryGetProperty("tags", out var tagsArray))
            return [];

        var builder = ImmutableArray.CreateBuilder<Tag>();
        foreach (var tag in tagsArray.EnumerateArray())
        {
            var tagStr = tag.GetString();
            if (tagStr != null && TryParseTag(tagStr, out var parsed))
                builder.Add(parsed);
        }
        return builder.ToImmutable();
    }

    private static bool TryParseTag(string tag, out Tag result)
    {
        result = tag switch
        {
            "building" => Tag.Building,
            "space" => Tag.Space,
            "power" => Tag.Power,
            "science" => Tag.Science,
            "jovian" => Tag.Jovian,
            "earth" => Tag.Earth,
            "plant" => Tag.Plant,
            "microbe" => Tag.Microbe,
            "animal" => Tag.Animal,
            "city" => Tag.City,
            "event" => Tag.Event,
            "wild" => Tag.Wild,
            _ => default,
        };
        return tag is "building" or "space" or "power" or "science" or "jovian"
            or "earth" or "plant" or "microbe" or "animal" or "city" or "event" or "wild";
    }

    private static ImmutableArray<CardRequirement> ParseRequirements(JsonElement card)
    {
        if (!card.TryGetProperty("requirements", out var reqsArray))
            return [];

        var builder = ImmutableArray.CreateBuilder<CardRequirement>();
        foreach (var req in reqsArray.EnumerateArray())
        {
            var type = req.GetProperty("type").GetString()!;
            var count = req.GetProperty("count").GetInt32();
            builder.Add(new CardRequirement(type, count));
        }
        return builder.ToImmutable();
    }

    private static VictoryPoints? ParseVictoryPoints(JsonElement card, string cardId)
    {
        if (!card.TryGetProperty("victory_points", out var vpProp))
            return null;

        if (vpProp.ValueKind == JsonValueKind.Number)
        {
            return new FixedVictoryPoints(vpProp.GetInt32());
        }

        if (vpProp.ValueKind == JsonValueKind.String)
        {
            var vpStr = vpProp.GetString()!;

            // "1/" = 1 VP per resource on this card
            // "1/2" = 1 VP per 2 resources on this card
            // "1/3" = 1 VP per 3 resources
            // "1/4" = 1 VP per 4 resources
            // "2/" = 2 VP per resource
            // "3/" = 3 VP per resource
            if (vpStr.EndsWith("/"))
            {
                var pointsPer = int.Parse(vpStr.TrimEnd('/'));
                return new PerResourceVictoryPoints(pointsPer, 1, cardId);
            }

            var parts = vpStr.Split('/');
            if (parts.Length == 2 && int.TryParse(parts[0], out var pts) && int.TryParse(parts[1], out var per))
            {
                return new PerResourceVictoryPoints(pts, per, cardId);
            }
        }

        return null;
    }

    private static string ReadEmbeddedResource()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("cards.json")
            ?? throw new InvalidOperationException("Embedded resource 'cards.json' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

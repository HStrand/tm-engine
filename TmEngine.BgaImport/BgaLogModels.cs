using System.Text.Json;
using System.Text.Json.Serialization;

namespace TmEngine.BgaImport;

/// <summary>
/// A parsed Board Game Arena game log as produced by bga-tm-scraper (one JSON per table and perspective).
/// Only the fields the importer reads are modelled; everything else is ignored.
/// </summary>
public sealed record BgaGameLog
{
    public string? ReplayId { get; init; }
    public string? TableId { get; init; }
    public string? PlayerPerspective { get; init; }
    public string? Map { get; init; }
    public int? Generations { get; init; }
    public bool? PreludeOn { get; init; }
    public bool? ColoniesOn { get; init; }
    public bool? CorporateEraOn { get; init; }
    public bool? DraftOn { get; init; }

    /// <summary>Keyed by BGA player id.</summary>
    public Dictionary<string, BgaPlayer> Players { get; init; } = [];

    public List<BgaMove> Moves { get; init; } = [];
    public BgaLogMetadata? Metadata { get; init; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static BgaGameLog Parse(string json) =>
        JsonSerializer.Deserialize<BgaGameLog>(json, Options)
        ?? throw new JsonException("Game log JSON was null.");

    public static BgaGameLog Parse(Stream utf8Json) =>
        JsonSerializer.Deserialize<BgaGameLog>(utf8Json, Options)
        ?? throw new JsonException("Game log JSON was null.");
}

public sealed record BgaLogMetadata
{
    public string? ScraperVersion { get; init; }
}

public sealed record BgaPlayer
{
    public string PlayerId { get; init; } = "";
    public string PlayerName { get; init; } = "";
    public string? Corporation { get; init; }
    public string? Color { get; init; }
    public List<string>? CardsPlayed { get; init; }
    public List<string>? MilestonesClaimed { get; init; }
    public List<string>? AwardsFunded { get; init; }
    public BgaStartingHand? StartingHand { get; init; }
}

public sealed record BgaStartingHand
{
    public List<string>? Corporations { get; init; }
    public List<string>? Preludes { get; init; }
    public List<string>? ProjectCards { get; init; }
}

public sealed record BgaMove
{
    public int? MoveNumber { get; init; }
    public string PlayerId { get; init; } = "";
    public string PlayerName { get; init; } = "";
    public string ActionType { get; init; } = "";
    public string Description { get; init; } = "";
    public string? CardPlayed { get; init; }
    public string? CardDrafted { get; init; }
    public string? TilePlaced { get; init; }
    public string? TileLocation { get; init; }

    /// <summary>BGA player id -> cards offered (research pack, draft pack, draw-and-keep options).</summary>
    public Dictionary<string, List<string>>? CardOptions { get; init; }

    /// <summary>BGA player id -> cards that entered that player's hand.</summary>
    public Dictionary<string, List<string>>? CardsKept { get; init; }

    public List<string>? CardsDiscarded { get; init; }
    public string? Reason { get; init; }
    public BgaSnapshot? GameState { get; init; }
}

/// <summary>State after a move, as tracked by the scraper.</summary>
public sealed record BgaSnapshot
{
    public int? Generation { get; init; }
    public int? Temperature { get; init; }
    public int? Oxygen { get; init; }
    public int? Oceans { get; init; }
    public int? DrawPile { get; init; }
    public int? DiscardPile { get; init; }

    /// <summary>BGA player id, or in some parses the player's name.</summary>
    public string? StartingPlayer { get; init; }

    public Dictionary<string, BgaPlayerVp>? PlayerVp { get; init; }

    /// <summary>BGA player id -> tracker name -> value. Tracker names differ between scraper versions.</summary>
    public Dictionary<string, Dictionary<string, int>>? PlayerTrackers { get; init; }

    /// <summary>BGA player id -> tile (card) name -> location string.</summary>
    public Dictionary<string, Dictionary<string, string>>? SpecialTiles { get; init; }

    public Dictionary<string, List<string>>? PlayerHands { get; init; }
    public Dictionary<string, BgaClaim>? Milestones { get; init; }
    public Dictionary<string, BgaClaim>? Awards { get; init; }
}

public sealed record BgaClaim
{
    public string? PlayerId { get; init; }
    public int? MoveNumber { get; init; }
}

public sealed record BgaPlayerVp
{
    public int? Total { get; init; }
    public BgaVpTotals? TotalDetails { get; init; }
    public BgaVpDetails? Details { get; init; }
}

public sealed record BgaVpTotals
{
    public int? Tr { get; init; }
    public int? Awards { get; init; }
    public int? Milestones { get; init; }
    public int? Cities { get; init; }
    public int? Greeneries { get; init; }
    public int? Cards { get; init; }
}

public sealed record BgaVpDetails
{
    /// <summary>Card name -> resources on that card.</summary>
    public Dictionary<string, BgaCardResource>? CardResources { get; init; }

    /// <summary>Card name -> points the card is currently worth. Only cards worth something are listed.</summary>
    public Dictionary<string, BgaCardVp>? Cards { get; init; }
}

public sealed record BgaCardVp
{
    public int Vp { get; init; }
}

public sealed record BgaCardResource
{
    public string? Type { get; init; }
    public int Count { get; init; }
}

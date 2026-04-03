using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TmEngine.Cli;

// ── Card Info ──

public class CardInfoDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int Cost { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<CardRequirementDto> Requirements { get; set; } = new();
    public string Description { get; set; } = "";

    public string FormatShort()
    {
        var tagStr = Tags.Count > 0 ? $" [{string.Join(", ", Tags)}]" : "";
        var costStr = Type is "Corporation" or "Prelude" ? "" : $" ({Cost} MC)";
        return $"{Name}{costStr}{tagStr}";
    }

    public string FormatFull()
    {
        var parts = new List<string> { Name };
        if (Type is not "Corporation" and not "Prelude")
            parts.Add($"Cost: {Cost} MC");
        if (Tags.Count > 0)
            parts.Add($"Tags: {string.Join(", ", Tags)}");
        if (Requirements.Count > 0)
        {
            var reqs = Requirements.Select(r => $"{r.Type} {r.Count}");
            parts.Add($"Requires: {string.Join(", ", reqs)}");
        }
        if (!string.IsNullOrEmpty(Description))
            parts.Add(Description);
        return string.Join(" | ", parts);
    }
}

public class CardRequirementDto
{
    public string Type { get; set; } = "";
    public int Count { get; set; }
}

// ── Request ──

public record CreateGameRequest(
    int PlayerCount,
    string Map,
    bool CorporateEra,
    bool DraftVariant,
    bool PreludeExpansion);

// ── Responses ──

public record CreateGameResponse(string GameId);

public class GameStateResponse
{
    public GameStateDto State { get; set; } = new();
    public Dictionary<string, string> CardNames { get; set; } = new();
}

public class LegalMovesResponse
{
    public AvailableMovesDto Moves { get; set; } = new();
    public Dictionary<string, string> CardNames { get; set; } = new();
}

public class SubmitMoveResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public GameStateDto? State { get; set; }
    public Dictionary<string, string>? CardNames { get; set; }
}

public class HistoryResponse
{
    public List<string> Log { get; set; } = new();
}

// ── Game State DTOs ──

public class GameStateDto
{
    public string GameId { get; set; } = "";
    public string Map { get; set; } = "";
    public bool CorporateEra { get; set; }
    public bool DraftVariant { get; set; }
    public bool PreludeExpansion { get; set; }
    public string Phase { get; set; } = "";
    public int Generation { get; set; }
    public int ActivePlayerIndex { get; set; }
    public int FirstPlayerIndex { get; set; }
    public int Oxygen { get; set; }
    public int Temperature { get; set; }
    public int OceansPlaced { get; set; }
    public List<PlayerStateDto> Players { get; set; } = new();
    public Dictionary<string, PlacedTileDto> PlacedTiles { get; set; } = new();
    public List<OffMapTileDto> OffMapTiles { get; set; } = new();
    public List<MilestoneClaimDto> ClaimedMilestones { get; set; } = new();
    public List<AwardFundingDto> FundedAwards { get; set; } = new();
    public int MoveNumber { get; set; }
    public List<string> Log { get; set; } = new();
}

public class PlayerStateDto
{
    public int PlayerId { get; set; }
    public string CorporationId { get; set; } = "";
    public int TerraformRating { get; set; }
    public ResourceSetDto Resources { get; set; } = new();
    public ProductionSetDto Production { get; set; } = new();
    public List<string> Hand { get; set; } = new();
    public List<string> PlayedCards { get; set; } = new();
    public List<string> PlayedEvents { get; set; } = new();
    public Dictionary<string, int> CardResources { get; set; } = new();
    public bool Passed { get; set; }
    public int ActionsThisTurn { get; set; }
}

public class ResourceSetDto
{
    public int MegaCredits { get; set; }
    public int Steel { get; set; }
    public int Titanium { get; set; }
    public int Plants { get; set; }
    public int Energy { get; set; }
    public int Heat { get; set; }
}

public class ProductionSetDto
{
    public int MegaCredits { get; set; }
    public int Steel { get; set; }
    public int Titanium { get; set; }
    public int Plants { get; set; }
    public int Energy { get; set; }
    public int Heat { get; set; }
}

public class PlacedTileDto
{
    public string Type { get; set; } = "";
    public int? OwnerId { get; set; }
    public HexCoordDto Location { get; set; } = new();
}

public class HexCoordDto
{
    public int Col { get; set; }
    public int Row { get; set; }
    public override string ToString() => $"({Col},{Row})";
}

public class OffMapTileDto
{
    public string Name { get; set; } = "";
    public string TileType { get; set; } = "";
    public int? OwnerId { get; set; }
}

public class MilestoneClaimDto
{
    public string MilestoneName { get; set; } = "";
    public int PlayerId { get; set; }
}

public class AwardFundingDto
{
    public string AwardName { get; set; } = "";
    public int PlayerId { get; set; }
}

// ── Available Moves DTOs ──

public class AvailableMovesDto
{
    public bool GameOver { get; set; }
    public bool WaitingForOtherPlayer { get; set; }
    public SetupOptionsDto? Setup { get; set; }
    public PreludeOptionsDto? Prelude { get; set; }
    public DraftOptionsDto? Draft { get; set; }
    public BuyCardsOptionsDto? BuyCards { get; set; }
    public JObject? PendingAction { get; set; }
    public ActionPhaseOptionsDto? Actions { get; set; }
    public FinalGreeneryOptionsDto? FinalGreenery { get; set; }
}

public class SetupOptionsDto
{
    public List<string> AvailableCorporations { get; set; } = new();
    public List<string> AvailablePreludes { get; set; } = new();
    public List<string> AvailableCards { get; set; } = new();
}

public class PreludeOptionsDto
{
    public List<string> RemainingPreludes { get; set; } = new();
}

public class DraftOptionsDto
{
    public List<string> DraftHand { get; set; } = new();
}

public class BuyCardsOptionsDto
{
    public List<string> AvailableCards { get; set; } = new();
    public int CostPerCard { get; set; }
}

public class ActionPhaseOptionsDto
{
    public bool CanPass { get; set; }
    public bool CanEndTurn { get; set; }
    public bool CanConvertHeat { get; set; }
    public bool CanConvertPlants { get; set; }
    public List<HexCoordDto> ValidGreeneryLocations { get; set; } = new();
    public bool CanPerformFirstAction { get; set; }
    public List<PlayableCardDto> PlayableCards { get; set; } = new();
    public List<StandardProjectOptionDto> StandardProjects { get; set; } = new();
    public List<ClaimableMilestoneDto> ClaimableMilestones { get; set; } = new();
    public List<FundableAwardDto> FundableAwards { get; set; } = new();
    public List<UsableCardActionDto> UsableCardActions { get; set; } = new();
}

public class PlayableCardDto
{
    public string CardId { get; set; } = "";
    public int EffectiveCost { get; set; }
    public bool CanUseSteel { get; set; }
    public bool CanUseTitanium { get; set; }
    public bool CanUseHeat { get; set; }
}

public class StandardProjectOptionDto
{
    public string Project { get; set; } = "";
    public bool Available { get; set; }
    public int Cost { get; set; }
    public List<HexCoordDto>? ValidLocations { get; set; }
}

public class ClaimableMilestoneDto
{
    public string Name { get; set; } = "";
}

public class FundableAwardDto
{
    public string Name { get; set; } = "";
    public int Cost { get; set; }
}

public class UsableCardActionDto
{
    public string CardId { get; set; } = "";
}

public class FinalGreeneryOptionsDto
{
    public bool CanConvert { get; set; }
    public bool CanPass { get; set; }
    public List<HexCoordDto> ValidGreeneryLocations { get; set; } = new();
}

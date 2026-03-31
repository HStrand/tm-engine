namespace TmEngine.Domain.Models;

public static class Constants
{
    // Global parameter limits (defaults — maps may override Max values)
    public const int MinOxygen = 0;
    public const int DefaultMaxOxygen = 14;

    public const int MinTemperature = -30;
    public const int DefaultMaxTemperature = 8;
    public const int TemperatureStep = 2;

    public const int DefaultMaxOceans = 9;

    // Temperature bonus thresholds (when raising temp TO this value, gain bonus)
    public const int TemperatureOceanBonus1 = -24;
    public const int TemperatureOceanBonus2 = -20;
    public const int TemperatureHeatProductionBonus = 0;

    // Oxygen bonus threshold
    public const int OxygenTemperatureBonus = 8; // At 8% O2, also raise temperature

    // Resource values
    public const int SteelValue = 2;
    public const int TitaniumValue = 3;

    // Card costs
    public const int CardBuyCost = 3;

    // Plant/heat conversion
    public const int PlantsPerGreenery = 8;
    public const int HeatPerTemperature = 8;

    // Starting terraform rating
    public const int StartingTR = 20;
    public const int StartingTRSolo = 14;

    // Starting production (standard game only, not Corporate Era)
    public const int StandardGameStartingProduction = 1;

    // MC production floor
    public const int MinMCProduction = -5;

    // Milestones and awards
    public const int MilestoneCost = 8;
    public const int MaxClaimedMilestones = 3;
    public const int MilestoneVP = 5;

    public const int AwardFundCost1 = 8;
    public const int AwardFundCost2 = 14;
    public const int AwardFundCost3 = 20;
    public const int MaxFundedAwards = 3;
    public const int AwardFirstPlaceVP = 5;
    public const int AwardSecondPlaceVP = 2;

    // Standard project costs
    public const int PowerPlantCost = 11;
    public const int AsteroidCost = 14;
    public const int AquiferCost = 18;
    public const int GreeneryCost = 23;
    public const int CityCost = 25;

    // Setup
    public const int CorporationsDealt = 2;
    public const int InitialCardsDealt = 10;
    public const int PreludesDealt = 4;
    public const int PreludesKept = 2;
    public const int ResearchCardsDealt = 4;

    // Player count
    public const int MinPlayers = 2;
    public const int MaxPlayers = 5;

    // Ocean adjacency bonus
    public const int OceanAdjacencyBonus = 2; // 2 MC per adjacent ocean when placing a tile

    // Hellas south pole special
    public const int HellasSouthPoleCost = 6;

    // Prelude compensation when player can't afford to play a prelude
    public const int PreludeCompensation = 15;
}

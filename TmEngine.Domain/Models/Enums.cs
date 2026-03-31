namespace TmEngine.Domain.Models;

/// <summary>
/// The type of a project card.
/// </summary>
public enum CardType
{
    Automated,
    Active,
    Event,
    Corporation,
    Prelude
}

/// <summary>
/// Tags that categorize cards and enable synergies.
/// </summary>
public enum Tag
{
    Building,
    Space,
    Power,
    Science,
    Jovian,
    Earth,
    Plant,
    Microbe,
    Animal,
    City,
    Event,
    Wild
}

/// <summary>
/// The six standard resource types tracked on the player board.
/// </summary>
public enum ResourceType
{
    MegaCredits,
    Steel,
    Titanium,
    Plants,
    Energy,
    Heat
}

/// <summary>
/// Resource types that can be placed on cards (not on the player board).
/// </summary>
public enum CardResourceType
{
    Animal,
    Microbe,
    Science,
    Fighter
}

/// <summary>
/// Types of tiles that can be placed on the board.
/// </summary>
public enum TileType
{
    Ocean,
    Greenery,
    City,

    // Special tiles from cards
    Capital,
    NuclearZone,
    IndustrialCenter,
    CommercialDistrict,
    EcologicalZone,
    NaturalPreserve,
    RestrictedArea,
    LavaFlows,
    MoholeArea,
    MiningArea,
    MiningRights
}

/// <summary>
/// The three Mars map variants.
/// </summary>
public enum MapName
{
    Tharsis,
    Hellas,
    Elysium
}

/// <summary>
/// Phases of a generation.
/// </summary>
public enum GamePhase
{
    Setup,
    PreludePlacement,
    Research,
    Action,
    Production,
    FinalGreeneryConversion,
    GameEnd
}

/// <summary>
/// Card expansions / sets.
/// </summary>
public enum Expansion
{
    Base,
    CorporateEra,
    Prelude,
    HellasElysium
}

/// <summary>
/// The six standard projects available to all players.
/// </summary>
public enum StandardProject
{
    SellPatents,
    PowerPlant,
    Asteroid,
    Aquifer,
    Greenery,
    City
}

/// <summary>
/// The type of a hex on the board.
/// </summary>
public enum HexType
{
    Land,
    OceanReserved,
    Named
}

/// <summary>
/// Types of placement bonuses on hex tiles.
/// </summary>
public enum PlacementBonus
{
    Steel,
    Titanium,
    Plants,
    Cards,
    Heat,
    Ocean // Hellas south pole special: pay 6 MC to gain an ocean placement
}

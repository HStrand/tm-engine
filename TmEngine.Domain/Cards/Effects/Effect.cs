using System.Collections.Immutable;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Cards.Effects;

/// <summary>
/// Base type for composable card effects. Cards are defined as lists of effects
/// rather than individual classes, enabling data-driven card definitions.
/// </summary>
public abstract record Effect;

// ═══════════════════════════════════════════════════════════
//  RESOURCE & PRODUCTION CHANGES
// ═══════════════════════════════════════════════════════════

/// <summary>Change the active player's production of a resource.</summary>
public sealed record ChangeProductionEffect(ResourceType Resource, int Amount) : Effect;

/// <summary>Change the active player's resource amount.</summary>
public sealed record ChangeResourceEffect(ResourceType Resource, int Amount) : Effect;

/// <summary>
/// Remove resources from any one player (red-bordered icon in the game).
/// This is optional — the active player may choose not to remove, or remove partially.
/// Triggers a PendingAction for target selection if multiple players qualify.
/// </summary>
public sealed record RemoveResourceEffect(ResourceType Resource, int Amount) : Effect;

/// <summary>
/// Reduce any one player's production (red-bordered production icon).
/// Unlike RemoveResource, this MUST be performed. If no opponent has the production,
/// the active player must reduce their own or cannot play the card.
/// </summary>
public sealed record ReduceAnyProductionEffect(ResourceType Resource, int Amount) : Effect;

// ═══════════════════════════════════════════════════════════
//  GLOBAL PARAMETERS
// ═══════════════════════════════════════════════════════════

/// <summary>Raise oxygen by the given number of steps.</summary>
public sealed record RaiseOxygenEffect(int Steps = 1) : Effect;

/// <summary>Raise temperature by the given number of steps.</summary>
public sealed record RaiseTemperatureEffect(int Steps = 1) : Effect;

/// <summary>Place ocean tile(s).</summary>
public sealed record PlaceOceanEffect(int Count = 1) : Effect;

// ═══════════════════════════════════════════════════════════
//  TILES
// ═══════════════════════════════════════════════════════════

/// <summary>
/// Place a tile on the board. PlacementConstraint can restrict valid locations
/// beyond the tile type's default rules (e.g., Research Outpost: city not adjacent to any tile).
/// </summary>
public sealed record PlaceTileEffect(
    TileType TileType,
    PlacementConstraint? Constraint = null) : Effect;

/// <summary>
/// Additional placement constraints that cards can impose beyond the tile type's default rules.
/// </summary>
public enum PlacementConstraint
{
    /// <summary>Must not be adjacent to any existing tile (Research Outpost).</summary>
    Isolated,

    /// <summary>Must be on a volcanic area (Lava Flows — already handled by TileType, but explicit).</summary>
    Volcanic,

    /// <summary>Must be on the reserved Noctis City area.</summary>
    NoctisCity,

    /// <summary>Must be adjacent to at least 2 other city tiles (Urbanized Area).</summary>
    AdjacentTo2Cities,

    /// <summary>Must be on an ocean-reserved area (Mohole Area).</summary>
    OceanReserved,

    /// <summary>Place on an area reserved for ocean, disregarding normal restrictions (Land Claim etc.).</summary>
    OnOceanArea,
}

// ═══════════════════════════════════════════════════════════
//  CARDS
// ═══════════════════════════════════════════════════════════

/// <summary>Draw cards from the draw pile to the player's hand.</summary>
public sealed record DrawCardsEffect(int Count) : Effect;

/// <summary>Look at the top N cards, keep some, discard rest.</summary>
public sealed record LookAtCardsEffect(int LookCount, int KeepCount) : Effect;

/// <summary>Discard cards from hand. Triggers DiscardCardsPending.</summary>
public sealed record DiscardCardsEffect(int Count) : Effect;

// ═══════════════════════════════════════════════════════════
//  CARD RESOURCES (animals, microbes, science, fighters)
// ═══════════════════════════════════════════════════════════

/// <summary>Add resources to a specific card or trigger card selection.</summary>
public sealed record AddCardResourceEffect(
    CardResourceType ResourceType,
    int Amount,
    /// <summary>Specific card ID, or null to let player choose (triggers PendingAction).</summary>
    string? TargetCardId = null) : Effect;

/// <summary>Remove resources from any card with the given resource type.</summary>
public sealed record RemoveCardResourceEffect(
    CardResourceType ResourceType,
    int Amount,
    /// <summary>If true, can target any player's card (red-bordered).</summary>
    bool AnyPlayer = false) : Effect;

// ═══════════════════════════════════════════════════════════
//  TERRAFORM RATING
// ═══════════════════════════════════════════════════════════

/// <summary>Directly change terraform rating (not via global parameter raise).</summary>
public sealed record ChangeTREffect(int Amount) : Effect;

// ═══════════════════════════════════════════════════════════
//  CHOICES
// ═══════════════════════════════════════════════════════════

/// <summary>
/// Player must choose one of several effect options.
/// E.g., "Gain 3 plants OR add 2 animals to ANOTHER card."
/// </summary>
public sealed record ChooseEffect(
    ImmutableArray<EffectOption> Options) : Effect;

/// <summary>A labeled option in a ChooseEffect.</summary>
public sealed record EffectOption(
    string Description,
    ImmutableArray<Effect> Effects);

// ═══════════════════════════════════════════════════════════
//  COMPOUND / SEQUENTIAL
// ═══════════════════════════════════════════════════════════

/// <summary>Execute multiple effects in sequence.</summary>
public sealed record CompoundEffect(ImmutableArray<Effect> Effects) : Effect;

// ═══════════════════════════════════════════════════════════
//  TRIGGERED / ONGOING EFFECTS (for blue cards and corporations)
// ═══════════════════════════════════════════════════════════

/// <summary>
/// An ongoing effect that triggers when the owning player performs a specific action.
/// E.g., "Effect: When you play a space card, gain 2 MC."
/// </summary>
public sealed record WhenYouEffect(TriggerCondition Trigger, Effect Effect) : Effect;

/// <summary>
/// An ongoing effect that triggers when ANY player performs a specific action.
/// E.g., "Effect: When anyone places an ocean tile, gain 2 plants."
/// </summary>
public sealed record WhenAnyoneEffect(TriggerCondition Trigger, Effect Effect) : Effect;

/// <summary>
/// Conditions that can trigger ongoing effects.
/// </summary>
public enum TriggerCondition
{
    // Tag triggers: when a card with this tag is played
    PlayBuildingTag,
    PlaySpaceTag,
    PlayScienceTag,
    PlayPowerTag,
    PlayJovianTag,
    PlayEarthTag,
    PlayPlantTag,
    PlayMicrobeTag,
    PlayAnimalTag,
    PlayCityTag,
    PlayEventTag,

    // Tile triggers (on Mars = placed on the hex grid)
    PlaceCityTileOnMars,
    PlaceAnyCityTile, // includes off-map cities (Ganymede, Phobos)
    PlaceGreeneryTile,
    PlaceOceanTile,
    PlaceAnyTile,

    // Parameter triggers
    RaiseTemperature,
    RaiseOxygen,

    // Placement bonus triggers
    GainMineralPlacementBonus, // steel or titanium (fires once per tile, not per resource)

    // Other
    PlayAnyCard,
    GainPlantProduction,
}

// ═══════════════════════════════════════════════════════════
//  CARD ACTIONS (for blue cards — usable once per generation)
// ═══════════════════════════════════════════════════════════

/// <summary>
/// Defines the action on a blue card: a cost to pay and effects to gain.
/// </summary>
public sealed record CardAction(
    /// <summary>Cost to activate (null = free).</summary>
    ActionCost? Cost,
    /// <summary>Effects gained when action is used.</summary>
    ImmutableArray<Effect> Effects,
    /// <summary>Additional precondition (null = none).</summary>
    ActionPrecondition? Precondition = null);

/// <summary>
/// Preconditions that must be met before a card action can be used.
/// </summary>
public enum ActionPrecondition
{
    /// <summary>Player must have increased TR this generation (UNMI).</summary>
    IncreasedTRThisGeneration,
}

/// <summary>Cost to use a card action.</summary>
public abstract record ActionCost;

/// <summary>Spend MC to use the action.</summary>
public sealed record SpendMCCost(int Amount) : ActionCost;

/// <summary>Spend energy to use the action.</summary>
public sealed record SpendEnergyCost(int Amount) : ActionCost;

/// <summary>Spend steel to use the action.</summary>
public sealed record SpendSteelCost(int Amount) : ActionCost;

/// <summary>Spend titanium to use the action.</summary>
public sealed record SpendTitaniumCost(int Amount) : ActionCost;

/// <summary>Spend heat to use the action.</summary>
public sealed record SpendHeatCost(int Amount) : ActionCost;

/// <summary>Spend card resources from this card.</summary>
public sealed record SpendCardResourceCost(CardResourceType ResourceType, int Amount) : ActionCost;

// ═══════════════════════════════════════════════════════════
//  PASSIVE MODIFIERS (ongoing effects that modify game rules)
// ═══════════════════════════════════════════════════════════

/// <summary>
/// Modifies the player's global requirement threshold.
/// E.g., Inventrix: +/- 2 to global requirements.
/// These stack across multiple cards.
/// </summary>
public sealed record RequirementModifierEffect(int Amount) : Effect;

/// <summary>
/// Modifies the value of steel when paying for cards.
/// E.g., Advanced Alloys: steel worth +1 MC.
/// </summary>
public sealed record SteelValueModifierEffect(int Amount) : Effect;

/// <summary>
/// Modifies the value of titanium when paying for cards.
/// E.g., Advanced Alloys: titanium worth +1 MC. PhoboLog: titanium worth +1 MC.
/// </summary>
public sealed record TitaniumValueModifierEffect(int Amount) : Effect;

/// <summary>
/// Reduces the cost of playing cards with a specific tag.
/// E.g., Earth Office: Earth cards cost 3 MC less.
/// </summary>
public sealed record TagDiscountEffect(Tag Tag, int Discount) : Effect;

/// <summary>
/// Reduces the cost of playing all cards.
/// E.g., Media Group: all cards cost 1 MC less.
/// </summary>
public sealed record GlobalDiscountEffect(int Discount) : Effect;

/// <summary>
/// Modifies plant conversion cost (default 8 plants per greenery).
/// E.g., Ecoline: greenery costs 7 plants instead of 8.
/// </summary>
public sealed record PlantConversionModifierEffect(int NewCost) : Effect;

/// <summary>
/// Allows using heat to pay for cards (Helion corporation).
/// </summary>
public sealed record HeatAsPaymentEffect : Effect;

/// <summary>
/// Reduces the cost of the Power Plant standard project (Thorgate corporation).
/// </summary>
public sealed record PowerPlantDiscountEffect(int Discount) : Effect;

/// <summary>
/// Increase the player's lowest production by 1 step (Robinson Industries).
/// If multiple productions are tied for lowest, triggers a choice.
/// </summary>
public sealed record IncreaseLowestProductionEffect : Effect;

/// <summary>
/// Gain MC rebate when playing a card or standard project with printed cost >= threshold (Credicor).
/// Checks the original/printed cost, not the effective cost after discounts.
/// </summary>
public sealed record HighCostRebateEffect(int CostThreshold, int Rebate) : Effect;

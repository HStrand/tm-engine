using System.Collections.Immutable;
using TmEngine.Domain.Cards.Effects;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Cards;

/// <summary>
/// Full card entry including definition, play effects, ongoing effects, and actions.
/// </summary>
public sealed record CardEntry
{
    public required CardDefinition Definition { get; init; }

    /// <summary>Effects applied when the card is played (immediate effects).</summary>
    public ImmutableArray<Effect> OnPlayEffects { get; init; } = [];

    /// <summary>Ongoing/triggered effects (for blue cards and corporations).</summary>
    public ImmutableArray<Effect> OngoingEffects { get; init; } = [];

    /// <summary>Card action (for blue cards — usable once per generation).</summary>
    public CardAction? Action { get; init; }

    /// <summary>
    /// Mandatory first action for corporations (e.g., Inventrix draws 3 cards,
    /// Tharsis Republic places a city). Applied after setup, before normal actions begin.
    /// </summary>
    public ImmutableArray<Effect> FirstActionEffects { get; init; } = [];
}

/// <summary>
/// Central registry of all card definitions and their effects.
/// Maps card ID to CardEntry. Cards are registered by expansion.
/// </summary>
public static class CardRegistry
{
    private static readonly Lazy<ImmutableDictionary<string, CardEntry>> _cards =
        new(BuildRegistry);

    public static ImmutableDictionary<string, CardEntry> All => _cards.Value;

    public static CardEntry Get(string cardId) =>
        All.TryGetValue(cardId, out var entry)
            ? entry
            : throw new ArgumentException($"Unknown card ID: {cardId}");

    public static bool TryGet(string cardId, out CardEntry entry) =>
        All.TryGetValue(cardId, out entry!);

    public static CardDefinition GetDefinition(string cardId) => Get(cardId).Definition;

    public static ImmutableArray<Tag> GetTags(string cardId) =>
        TryGet(cardId, out var entry) ? entry.Definition.Tags : [];

    /// <summary>
    /// Get all card IDs for a given expansion set.
    /// </summary>
    public static ImmutableArray<string> GetCardIdsByExpansion(Expansion expansion) =>
        [.. All.Values.Where(e => e.Definition.Expansion == expansion).Select(e => e.Definition.Id)];

    /// <summary>
    /// Get all project card IDs (not corporations or preludes) for the given expansions.
    /// </summary>
    public static ImmutableArray<string> GetProjectCardIds(ImmutableHashSet<Expansion> expansions) =>
        [.. All.Values
            .Where(e => expansions.Contains(e.Definition.Expansion))
            .Where(e => e.Definition.Type is CardType.Automated or CardType.Active or CardType.Event)
            .Select(e => e.Definition.Id)];

    /// <summary>
    /// Get all corporation card IDs for the given expansions.
    /// </summary>
    public static ImmutableArray<string> GetCorporationIds(ImmutableHashSet<Expansion> expansions) =>
        [.. All.Values
            .Where(e => expansions.Contains(e.Definition.Expansion))
            .Where(e => e.Definition.Type == CardType.Corporation)
            .Select(e => e.Definition.Id)];

    /// <summary>
    /// Get all prelude card IDs.
    /// </summary>
    public static ImmutableArray<string> GetPreludeIds() =>
        [.. All.Values
            .Where(e => e.Definition.Type == CardType.Prelude)
            .Select(e => e.Definition.Id)];

    private static ImmutableDictionary<string, CardEntry> BuildRegistry()
    {
        // Load all card definitions from cards.json
        var definitions = CardDataLoader.LoadAll();
        var builder = ImmutableDictionary.CreateBuilder<string, CardEntry>();

        foreach (var (id, def) in definitions)
        {
            builder[id] = new CardEntry { Definition = def };
        }

        // Apply hand-coded effects on top of loaded definitions
        RegisterCorporationEffects(builder);
        RegisterPreludeEffects(builder);
        RegisterProjectCardEffects(builder);

        return builder.ToImmutable();
    }

    /// <summary>
    /// Update an existing card entry with effects, preserving the loaded definition.
    /// </summary>
    internal static void SetEffects(
        ImmutableDictionary<string, CardEntry>.Builder builder,
        string cardId,
        ImmutableArray<Effect>? onPlayEffects = null,
        ImmutableArray<Effect>? ongoingEffects = null,
        CardAction? action = null,
        ImmutableArray<Effect>? firstActionEffects = null)
    {
        if (!builder.TryGetValue(cardId, out var existing))
            return;

        builder[cardId] = existing with
        {
            OnPlayEffects = onPlayEffects ?? existing.OnPlayEffects,
            OngoingEffects = ongoingEffects ?? existing.OngoingEffects,
            Action = action ?? existing.Action,
            FirstActionEffects = firstActionEffects ?? existing.FirstActionEffects,
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  CORPORATION EFFECTS
    // ═══════════════════════════════════════════════════════════

    private static void RegisterCorporationEffects(ImmutableDictionary<string, CardEntry>.Builder builder)
    {
        // CORP01: Credicor — Start with 57 MC. Effect: 4 MC rebate when playing card or SP with printed cost >= 20.
        SetEffects(builder, "CORP01",
            onPlayEffects: [new ChangeResourceEffect(ResourceType.MegaCredits, 57)],
            ongoingEffects: [new HighCostRebateEffect(CostThreshold: 20, Rebate: 4)]);

        // CORP02: Ecoline — Start with 2 plant prod, 3 plants, 36 MC. Greenery costs 7 plants.
        SetEffects(builder, "CORP02",
            onPlayEffects:
            [
                new ChangeResourceEffect(ResourceType.MegaCredits, 36),
                new ChangeProductionEffect(ResourceType.Plants, 2),
                new ChangeResourceEffect(ResourceType.Plants, 3),
            ],
            ongoingEffects: [new PlantConversionModifierEffect(7)]);

        // CORP03: Helion — Start with 3 heat prod, 42 MC. Can use heat as MC.
        SetEffects(builder, "CORP03",
            onPlayEffects:
            [
                new ChangeResourceEffect(ResourceType.MegaCredits, 42),
                new ChangeProductionEffect(ResourceType.Heat, 3),
            ],
            ongoingEffects: [new HeatAsPaymentEffect()]);

        // CORP04: Mining Guild — Start with 30 MC, 5 steel, 1 steel prod.
        // Effect: When you get steel/titanium placement bonus, increase steel prod 1 step.
        SetEffects(builder, "CORP04",
            onPlayEffects:
            [
                new ChangeResourceEffect(ResourceType.MegaCredits, 30),
                new ChangeResourceEffect(ResourceType.Steel, 5),
                new ChangeProductionEffect(ResourceType.Steel, 1),
            ],
            ongoingEffects:
            [
                new WhenYouEffect(TriggerCondition.GainMineralPlacementBonus, new ChangeProductionEffect(ResourceType.Steel, 1)),
            ]);

        // CORP05: Interplanetary Cinematics — Start with 30 MC, 20 steel. Effect: When you play event, gain 2 MC.
        SetEffects(builder, "CORP05",
            onPlayEffects:
            [
                new ChangeResourceEffect(ResourceType.MegaCredits, 30),
                new ChangeResourceEffect(ResourceType.Steel, 20),
            ],
            ongoingEffects: [new WhenYouEffect(TriggerCondition.PlayEventTag, new ChangeResourceEffect(ResourceType.MegaCredits, 2))]);

        // CORP06: Inventrix — Start with 45 MC. Global requirements +/- 2.
        // First action: draw 3 cards.
        SetEffects(builder, "CORP06",
            onPlayEffects: [new ChangeResourceEffect(ResourceType.MegaCredits, 45)],
            ongoingEffects: [new RequirementModifierEffect(2)],
            firstActionEffects: [new DrawCardsEffect(3)]);

        // CORP07: Phobolog — Start with 23 MC, 10 titanium. Titanium worth 1 more.
        SetEffects(builder, "CORP07",
            onPlayEffects:
            [
                new ChangeResourceEffect(ResourceType.MegaCredits, 23),
                new ChangeResourceEffect(ResourceType.Titanium, 10),
            ],
            ongoingEffects: [new TitaniumValueModifierEffect(1)]);

        // CORP08: Tharsis Republic — Start with 40 MC.
        // First action: place a city.
        // Effect: When any city is placed ON MARS, gain 1 MC prod. When YOU place any city, gain 3 MC.
        SetEffects(builder, "CORP08",
            onPlayEffects:
            [
                new ChangeResourceEffect(ResourceType.MegaCredits, 40),
            ],
            firstActionEffects: [new PlaceTileEffect(TileType.City)],
            ongoingEffects:
            [
                new WhenAnyoneEffect(TriggerCondition.PlaceCityTileOnMars, new ChangeProductionEffect(ResourceType.MegaCredits, 1)),
                new WhenYouEffect(TriggerCondition.PlaceAnyCityTile, new ChangeResourceEffect(ResourceType.MegaCredits, 3)),
            ]);

        // CORP09: Thorgate — Start with 48 MC, 1 energy prod. Power cards cost 3 less. Power Plant SP costs 3 less.
        SetEffects(builder, "CORP09",
            onPlayEffects:
            [
                new ChangeResourceEffect(ResourceType.MegaCredits, 48),
                new ChangeProductionEffect(ResourceType.Energy, 1),
            ],
            ongoingEffects:
            [
                new TagDiscountEffect(Tag.Power, 3),
                new PowerPlantDiscountEffect(3),
            ]);

        // CORP10: UNMI — Start with 40 MC. Action: Pay 3 MC to gain 1 TR (requires you raised TR this gen).
        SetEffects(builder, "CORP10",
            onPlayEffects: [new ChangeResourceEffect(ResourceType.MegaCredits, 40)],
            action: new CardAction(
                Cost: new SpendMCCost(3),
                Effects: [new ChangeTREffect(1)],
                Precondition: ActionPrecondition.IncreasedTRThisGeneration));

        // CORP11: Teractor — Start with 60 MC. Earth cards cost 3 less.
        SetEffects(builder, "CORP11",
            onPlayEffects: [new ChangeResourceEffect(ResourceType.MegaCredits, 60)],
            ongoingEffects: [new TagDiscountEffect(Tag.Earth, 3)]);

        // CORP12: Saturn Systems — Start with 42 MC, 1 titanium prod. Effect: When anyone plays Jovian tag, gain 1 MC prod.
        SetEffects(builder, "CORP12",
            onPlayEffects:
            [
                new ChangeResourceEffect(ResourceType.MegaCredits, 42),
                new ChangeProductionEffect(ResourceType.Titanium, 1),
            ],
            ongoingEffects:
            [
                new WhenAnyoneEffect(TriggerCondition.PlayJovianTag, new ChangeProductionEffect(ResourceType.MegaCredits, 1)),
            ]);

        // CORP18: Cheung Shing Mars — Start with 44 MC, 3 MC prod. Building cards cost 2 less.
        SetEffects(builder, "CORP18",
            onPlayEffects:
            [
                new ChangeResourceEffect(ResourceType.MegaCredits, 44),
                new ChangeProductionEffect(ResourceType.MegaCredits, 3),
            ],
            ongoingEffects: [new TagDiscountEffect(Tag.Building, 2)]);

        // CORP19: Point Luna — Start with 38 MC, 1 titanium prod. Effect: When you play Earth tag, draw card.
        SetEffects(builder, "CORP19",
            onPlayEffects:
            [
                new ChangeResourceEffect(ResourceType.MegaCredits, 38),
                new ChangeProductionEffect(ResourceType.Titanium, 1),
            ],
            ongoingEffects:
            [
                new WhenYouEffect(TriggerCondition.PlayEarthTag, new DrawCardsEffect(1)),
            ]);

        // CORP20: Robinson Industries — Start with 47 MC. Action: Pay 4 MC to increase your lowest production 1 step.
        SetEffects(builder, "CORP20",
            onPlayEffects: [new ChangeResourceEffect(ResourceType.MegaCredits, 47)],
            action: new CardAction(
                Cost: new SpendMCCost(4),
                Effects: [new IncreaseLowestProductionEffect()]));

        // CORP21: Valley Trust — Start with 37 MC. Science cards cost 2 less.
        // First action: draw 3 prelude cards, choose 1 to play immediately.
        SetEffects(builder, "CORP21",
            onPlayEffects: [new ChangeResourceEffect(ResourceType.MegaCredits, 37)],
            ongoingEffects: [new TagDiscountEffect(Tag.Science, 2)],
            firstActionEffects: [new DrawAndPlayOneEffect(3, CardType.Prelude)]);

        // CORP22: Vitor — Start with 45 MC + 3 MC self-rebate (corp has VP icon).
        // Mandatory first action: fund an award for free.
        // Effect: When you play card with non-negative VP, gain 3 MC.
        SetEffects(builder, "CORP22",
            onPlayEffects:
            [
                new ChangeResourceEffect(ResourceType.MegaCredits, 48), // 45 base + 3 self-rebate
                new GrantFreeAwardEffect(),
            ],
            ongoingEffects: [new VPCardRebateEffect(3)]);
    }

    // ═══════════════════════════════════════════════════════════
    //  PRELUDE EFFECTS
    // ═══════════════════════════════════════════════════════════

    private static void RegisterPreludeEffects(ImmutableDictionary<string, CardEntry>.Builder builder)
    {
        // P01: Allied Banks — +4 MC prod, +3 MC
        SetEffects(builder, "P01", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, 4),
            new ChangeResourceEffect(ResourceType.MegaCredits, 3),
        ]);

        // P02: Aquifer Turbines — Place ocean, +2 energy prod, pay 3 MC
        SetEffects(builder, "P02", onPlayEffects:
        [
            new PlaceOceanEffect(1),
            new ChangeProductionEffect(ResourceType.Energy, 2),
            new ChangeResourceEffect(ResourceType.MegaCredits, -3),
        ]);

        // P03: Biofuels — +1 energy prod, +1 plant prod, +2 plants
        SetEffects(builder, "P03", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, 1),
            new ChangeProductionEffect(ResourceType.Plants, 1),
            new ChangeResourceEffect(ResourceType.Plants, 2),
        ]);

        // P04: Biolab — +1 plant prod, draw 3 cards
        SetEffects(builder, "P04", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Plants, 1),
            new DrawCardsEffect(3),
        ]);

        // P05: Biosphere Support — +2 plant prod, -1 MC prod
        SetEffects(builder, "P05", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Plants, 2),
            new ChangeProductionEffect(ResourceType.MegaCredits, -1),
        ]);

        // P06: Business Empire — +6 MC prod, pay 6 MC
        SetEffects(builder, "P06", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, 6),
            new ChangeResourceEffect(ResourceType.MegaCredits, -6),
        ]);

        // P07: Dome Farming — +2 MC prod, +1 plant prod
        SetEffects(builder, "P07", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, 2),
            new ChangeProductionEffect(ResourceType.Plants, 1),
        ]);

        // P08: Donation — +21 MC
        SetEffects(builder, "P08", onPlayEffects:
            [new ChangeResourceEffect(ResourceType.MegaCredits, 21)]);

        // P09: Early Settlement — +1 plant prod, place city
        SetEffects(builder, "P09", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Plants, 1),
            new PlaceTileEffect(TileType.City),
        ]);

        // P10: Ecology Experts — +1 plant prod, play a card ignoring global parameter requirements
        SetEffects(builder, "P10", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Plants, 1),
            new PlayCardFromHandEffect(IgnoreGlobalRequirements: true),
        ]);

        // P11: Eccentric Sponsor — Play 1 card from hand with 25 MC discount
        SetEffects(builder, "P11", onPlayEffects:
            [new PlayCardFromHandEffect(CostDiscount: 25)]);

        // P12: Experimental Forest — Place greenery, reveal cards until 2 plant tags found
        SetEffects(builder, "P12", onPlayEffects:
        [
            new PlaceTileEffect(TileType.Greenery),
            new RevealUntilTagEffect(Tag.Plant, 2),
        ]);

        // P13: Galilean Mining — +2 titanium prod, pay 5 MC
        SetEffects(builder, "P13", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Titanium, 2),
            new ChangeResourceEffect(ResourceType.MegaCredits, -5),
        ]);

        // P14: Great Aquifer — Place 2 ocean tiles
        SetEffects(builder, "P14", onPlayEffects:
            [new PlaceOceanEffect(1), new PlaceOceanEffect(1)]);

        // P15: Huge Asteroid — +3 temperature steps, pay 5 MC
        SetEffects(builder, "P15", onPlayEffects:
        [
            new RaiseTemperatureEffect(3),
            new ChangeResourceEffect(ResourceType.MegaCredits, -5),
        ]);

        // P16: Io Research Outpost — +1 titanium prod, draw 1 card
        SetEffects(builder, "P16", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Titanium, 1),
            new DrawCardsEffect(1),
        ]);

        // P17: Loan — +30 MC, -2 MC prod
        SetEffects(builder, "P17", onPlayEffects:
        [
            new ChangeResourceEffect(ResourceType.MegaCredits, 30),
            new ChangeProductionEffect(ResourceType.MegaCredits, -2),
        ]);

        // P18: Martian Industries — +1 energy prod, +1 steel prod, +6 MC
        SetEffects(builder, "P18", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, 1),
            new ChangeProductionEffect(ResourceType.Steel, 1),
            new ChangeResourceEffect(ResourceType.MegaCredits, 6),
        ]);

        // P19: Metal-rich Asteroid — +1 temperature, +4 titanium, +4 steel
        SetEffects(builder, "P19", onPlayEffects:
        [
            new RaiseTemperatureEffect(1),
            new ChangeResourceEffect(ResourceType.Titanium, 4),
            new ChangeResourceEffect(ResourceType.Steel, 4),
        ]);

        // P20: Metals Company — +1 MC prod, +1 steel prod, +1 titanium prod
        SetEffects(builder, "P20", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, 1),
            new ChangeProductionEffect(ResourceType.Steel, 1),
            new ChangeProductionEffect(ResourceType.Titanium, 1),
        ]);

        // P21: Mining Operations — +2 steel prod, +4 steel
        SetEffects(builder, "P21", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Steel, 2),
            new ChangeResourceEffect(ResourceType.Steel, 4),
        ]);

        // P22: Mohole — +3 heat prod, +3 heat
        SetEffects(builder, "P22", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Heat, 3),
            new ChangeResourceEffect(ResourceType.Heat, 3),
        ]);

        // P23: Mohole Excavation — +1 steel prod, +2 heat prod, +2 heat
        SetEffects(builder, "P23", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Steel, 1),
            new ChangeProductionEffect(ResourceType.Heat, 2),
            new ChangeResourceEffect(ResourceType.Heat, 2),
        ]);

        // P24: Nitrogen Shipment — +1 plant prod, +1 TR, +5 MC
        SetEffects(builder, "P24", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Plants, 1),
            new ChangeTREffect(1),
            new ChangeResourceEffect(ResourceType.MegaCredits, 5),
        ]);

        // P25: Orbital Construction Yard — +1 titanium prod, +4 titanium
        SetEffects(builder, "P25", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Titanium, 1),
            new ChangeResourceEffect(ResourceType.Titanium, 4),
        ]);

        // P26: Polar Industries — +2 heat prod, place ocean
        SetEffects(builder, "P26", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Heat, 2),
            new PlaceOceanEffect(1),
        ]);

        // P27: Power Generation — +3 energy prod
        SetEffects(builder, "P27", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Energy, 3)]);

        // P28: Research Network — +1 MC prod, draw 3 cards
        SetEffects(builder, "P28", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, 1),
            new DrawCardsEffect(3),
        ]);

        // P29: Self-Sufficient Settlement — +2 MC prod, place city
        SetEffects(builder, "P29", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, 2),
            new PlaceTileEffect(TileType.City),
        ]);

        // P30: Smelting Plant — +2 oxygen, +5 steel
        SetEffects(builder, "P30", onPlayEffects:
        [
            new RaiseOxygenEffect(2),
            new ChangeResourceEffect(ResourceType.Steel, 5),
        ]);

        // P31: Society Support — +1 plant prod, +1 energy prod, +1 heat prod, -1 MC prod
        SetEffects(builder, "P31", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Plants, 1),
            new ChangeProductionEffect(ResourceType.Energy, 1),
            new ChangeProductionEffect(ResourceType.Heat, 1),
            new ChangeProductionEffect(ResourceType.MegaCredits, -1),
        ]);

        // P32: Supplier — +2 energy prod, +4 steel
        SetEffects(builder, "P32", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, 2),
            new ChangeResourceEffect(ResourceType.Steel, 4),
        ]);

        // P33: Supply Drop — +3 titanium, +8 steel, +3 plants
        SetEffects(builder, "P33", onPlayEffects:
        [
            new ChangeResourceEffect(ResourceType.Titanium, 3),
            new ChangeResourceEffect(ResourceType.Steel, 8),
            new ChangeResourceEffect(ResourceType.Plants, 3),
        ]);

        // P34: UNMI Contractor — +3 TR, draw 1 card
        SetEffects(builder, "P34", onPlayEffects:
        [
            new ChangeTREffect(3),
            new DrawCardsEffect(1),
        ]);

        // P35: Acquired Space Agency — +6 titanium, reveal cards until 2 space tags found
        SetEffects(builder, "P35", onPlayEffects:
        [
            new ChangeResourceEffect(ResourceType.Titanium, 6),
            new RevealUntilTagEffect(Tag.Space, 2),
        ]);
    }

    // ═══════════════════════════════════════════════════════════
    //  PROJECT CARD EFFECTS
    // ═══════════════════════════════════════════════════════════

    private static void RegisterProjectCardEffects(ImmutableDictionary<string, CardEntry>.Builder builder)
    {
        // 001: Colonizer Training Camp — VP only (automated, no effects)

        // 002: Asteroid Mining Consortium — Decrease any titanium prod 1, increase own 1
        SetEffects(builder, "002", onPlayEffects:
        [
            new ReduceAnyProductionEffect(ResourceType.Titanium, 1),
            new ChangeProductionEffect(ResourceType.Titanium, 1),
        ]);

        // 003: Deep Well Heating — +1 energy prod, raise temp 1
        SetEffects(builder, "003", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, 1),
            new RaiseTemperatureEffect(1),
        ]);

        // 004: Cloud Seeding — -1 MC prod, decrease any heat prod 1, +2 plant prod
        SetEffects(builder, "004", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, -1),
            new ReduceAnyProductionEffect(ResourceType.Heat, 1),
            new ChangeProductionEffect(ResourceType.Plants, 2),
        ]);

        // 005: Search for Life — Action: spend 1 MC, reveal top card, if microbe tag add science resource
        // Complex action — deferred

        // 006: Inventors' Guild — Action: draw card, may buy it
        // Complex action — deferred

        // 007: Martian Rails — Action: spend 1 energy, gain 1 MC per city on Mars
        // Complex action — deferred

        // 008: Capital — -2 energy prod, +5 MC prod, place Capital city tile
        SetEffects(builder, "008", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, -2),
            new ChangeProductionEffect(ResourceType.MegaCredits, 5),
            new PlaceTileEffect(TileType.Capital),
        ]);

        // 009: Asteroid — Raise temp 1, +2 titanium, remove up to 3 plants from any
        SetEffects(builder, "009", onPlayEffects:
        [
            new RaiseTemperatureEffect(1),
            new ChangeResourceEffect(ResourceType.Titanium, 2),
            new RemoveResourceEffect(ResourceType.Plants, 3),
        ]);

        // 010: Comet — Raise temp 1, place ocean, remove up to 3 plants from any
        SetEffects(builder, "010", onPlayEffects:
        [
            new RaiseTemperatureEffect(1),
            new PlaceOceanEffect(1),
            new RemoveResourceEffect(ResourceType.Plants, 3),
        ]);

        // 011: Big Asteroid — Raise temp 2, +4 titanium, remove up to 4 plants from any
        SetEffects(builder, "011", onPlayEffects:
        [
            new RaiseTemperatureEffect(2),
            new ChangeResourceEffect(ResourceType.Titanium, 4),
            new RemoveResourceEffect(ResourceType.Plants, 4),
        ]);

        // 012: Water Import From Europa — Action: pay 12 MC to place ocean (titanium usable)
        // Complex action — deferred

        // 013: Space Elevator — +1 titanium prod. Action: spend 1 steel, gain 5 MC
        SetEffects(builder, "013", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Titanium, 1)],
            action: new CardAction(new SpendSteelCost(1), [new ChangeResourceEffect(ResourceType.MegaCredits, 5)]));

        // 014: Development Center — Action: spend 1 energy, draw a card
        SetEffects(builder, "014",
            action: new CardAction(new SpendEnergyCost(1), [new DrawCardsEffect(1)]));

        // 015: Equatorial Magnetizer — Action: -1 energy prod, +1 TR
        SetEffects(builder, "015",
            action: new CardAction(null, [
                new ChangeProductionEffect(ResourceType.Energy, -1),
                new ChangeTREffect(1),
            ]));

        // 016: Domed Crater — +3 plants, place city, -1 energy prod, +3 MC prod
        SetEffects(builder, "016", onPlayEffects:
        [
            new ChangeResourceEffect(ResourceType.Plants, 3),
            new PlaceTileEffect(TileType.City),
            new ChangeProductionEffect(ResourceType.Energy, -1),
            new ChangeProductionEffect(ResourceType.MegaCredits, 3),
        ]);

        // 017: Noctis City — -1 energy prod, +3 MC prod, place city on Noctis reserved area
        SetEffects(builder, "017", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, -1),
            new ChangeProductionEffect(ResourceType.MegaCredits, 3),
            new PlaceTileEffect(TileType.City, PlacementConstraint.NoctisCity),
        ]);

        // 018: Methane From Titan — +2 heat prod, +2 plant prod
        SetEffects(builder, "018", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Heat, 2),
            new ChangeProductionEffect(ResourceType.Plants, 2),
        ]);

        // 019: Imported Hydrogen — +3 plants OR +3 microbes OR +2 animals to another card, place ocean
        SetEffects(builder, "019", onPlayEffects:
        [
            new ChooseEffect(
            [
                new EffectOption("Gain 3 plants", [new ChangeResourceEffect(ResourceType.Plants, 3)]),
                new EffectOption("Add 3 microbes to another card", [new AddCardResourceEffect(CardResourceType.Microbe, 3)]),
                new EffectOption("Add 2 animals to another card", [new AddCardResourceEffect(CardResourceType.Animal, 2)]),
            ]),
            new PlaceOceanEffect(1),
        ]);

        // 020: Research Outpost — Effect: cards cost 1 MC less. Place city next to no other tile
        SetEffects(builder, "020",
            onPlayEffects: [new PlaceTileEffect(TileType.City, PlacementConstraint.Isolated)],
            ongoingEffects: [new GlobalDiscountEffect(1)]);

        // 021: Phobos Space Haven — +1 titanium prod, place off-map city
        SetEffects(builder, "021", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Titanium, 1),
            new PlaceOffMapCityEffect("Phobos Space Haven"),
        ]);

        // 022: Black Polar Dust — place ocean, -2 MC prod, +3 heat prod
        SetEffects(builder, "022", onPlayEffects:
        [
            new PlaceOceanEffect(1),
            new ChangeProductionEffect(ResourceType.MegaCredits, -2),
            new ChangeProductionEffect(ResourceType.Heat, 3),
        ]);

        // 023: Arctic Algae — Effect: when anyone places ocean, gain 2 plants. Gain 1 plant
        SetEffects(builder, "023",
            onPlayEffects: [new ChangeResourceEffect(ResourceType.Plants, 1)],
            ongoingEffects: [new WhenAnyoneEffect(TriggerCondition.PlaceOceanTile, new ChangeResourceEffect(ResourceType.Plants, 2))]);

        // 024: Predators — Action: remove 1 animal from any card, add to this. 1VP/animal
        SetEffects(builder, "024",
            action: new CardAction(null, [
                new RemoveCardResourceEffect(CardResourceType.Animal, 1, AnyPlayer: true),
                new AddCardResourceEffect(CardResourceType.Animal, 1, "024"),
            ]));

        // 025: Space Station — Effect: space cards cost 2 MC less
        SetEffects(builder, "025",
            ongoingEffects: [new TagDiscountEffect(Tag.Space, 2)]);

        // 026: Eos Chasma National Park — +2 MC prod, +3 plants, add 1 animal to any animal card
        SetEffects(builder, "026", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, 2),
            new ChangeResourceEffect(ResourceType.Plants, 3),
            new AddCardResourceEffect(CardResourceType.Animal, 1),
        ]);

        // 027: Interstellar Colony Ship — VP only (event, 4 VP)

        // 028: Security Fleet — Action: spend 1 titanium, add 1 fighter resource. 1VP/fighter
        SetEffects(builder, "028",
            action: new CardAction(new SpendTitaniumCost(1),
                [new AddCardResourceEffect(CardResourceType.Fighter, 1, "028")]));

        // 029: Cupola City — place city, -1 energy prod, +3 MC prod
        SetEffects(builder, "029", onPlayEffects:
        [
            new PlaceTileEffect(TileType.City),
            new ChangeProductionEffect(ResourceType.Energy, -1),
            new ChangeProductionEffect(ResourceType.MegaCredits, 3),
        ]);

        // 030: Lunar Beam — -2 MC prod, +2 heat prod, +2 energy prod
        SetEffects(builder, "030", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, -2),
            new ChangeProductionEffect(ResourceType.Heat, 2),
            new ChangeProductionEffect(ResourceType.Energy, 2),
        ]);

        // 031: Optimal Aerobraking — Effect: when you play space event, gain 3 MC + 3 heat
        // Complex triggered effect — needs compound trigger effect
        // Deferred

        // 032: Underground City — place city, -2 energy prod, +2 steel prod
        SetEffects(builder, "032", onPlayEffects:
        [
            new PlaceTileEffect(TileType.City),
            new ChangeProductionEffect(ResourceType.Energy, -2),
            new ChangeProductionEffect(ResourceType.Steel, 2),
        ]);

        // 033: Nitrite Reducing Bacteria — Action: add 1 microbe, or remove 2 to raise O2 1 step
        // Complex action — deferred

        // 034: GHG Producing Bacteria — Action: add 1 microbe, or remove 2 to raise temp 1 step
        // Complex action — deferred

        // 035: Ants — Action: remove 1 microbe from any to add 1 here. 1VP/2 microbes
        // Complex action — deferred

        // 036: Release of Inert Gases — +2 TR
        SetEffects(builder, "036", onPlayEffects: [new ChangeTREffect(2)]);

        // 037: Nitrogen-Rich Asteroid — +2 TR, +1 temp, +1 plant prod or +4 if 3 plant tags
        // Complex conditional — deferred

        // 038: Rover Construction — Effect: when any city is placed, gain 2 MC
        SetEffects(builder, "038",
            ongoingEffects: [new WhenAnyoneEffect(TriggerCondition.PlaceCityTileOnMars, new ChangeResourceEffect(ResourceType.MegaCredits, 2))]);

        // 039: Deimos Down — raise temp 3, +4 steel, remove up to 8 plants from any
        SetEffects(builder, "039", onPlayEffects:
        [
            new RaiseTemperatureEffect(3),
            new ChangeResourceEffect(ResourceType.Steel, 4),
            new RemoveResourceEffect(ResourceType.Plants, 8),
        ]);

        // 040: Asteroid Mining — +2 titanium prod
        SetEffects(builder, "040", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Titanium, 2)]);

        // 041: Food Factory — -1 plant prod, +4 MC prod
        SetEffects(builder, "041", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Plants, -1),
            new ChangeProductionEffect(ResourceType.MegaCredits, 4),
        ]);

        // 042: Archaebacteria — +1 plant prod
        SetEffects(builder, "042", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Plants, 1)]);

        // 043: Carbonate Processing — -1 energy prod, +3 heat prod
        SetEffects(builder, "043", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, -1),
            new ChangeProductionEffect(ResourceType.Heat, 3),
        ]);

        // 044: Natural Preserve — place special tile next to no other, +1 MC prod
        SetEffects(builder, "044", onPlayEffects:
        [
            new PlaceTileEffect(TileType.NaturalPreserve, PlacementConstraint.Isolated),
            new ChangeProductionEffect(ResourceType.MegaCredits, 1),
        ]);

        // 045: Nuclear Power — -2 MC prod, +3 energy prod
        SetEffects(builder, "045", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, -2),
            new ChangeProductionEffect(ResourceType.Energy, 3),
        ]);

        // 046: Lightning Harvest — +1 energy prod, +1 MC prod
        SetEffects(builder, "046", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, 1),
            new ChangeProductionEffect(ResourceType.MegaCredits, 1),
        ]);

        // 047: Algae — +1 plant, +2 plant prod
        SetEffects(builder, "047", onPlayEffects:
        [
            new ChangeResourceEffect(ResourceType.Plants, 1),
            new ChangeProductionEffect(ResourceType.Plants, 2),
        ]);

        // 048: Adapted Lichen — +1 plant prod
        SetEffects(builder, "048", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Plants, 1)]);

        // 049: Tardigrades — Action: add 1 microbe. 1VP/4 microbes
        SetEffects(builder, "049",
            action: new CardAction(null, [new AddCardResourceEffect(CardResourceType.Microbe, 1, "049")]));

        // 050: Virus — Remove up to 2 animals or 5 plants from any player
        SetEffects(builder, "050", onPlayEffects:
        [
            new ChooseEffect(
            [
                new EffectOption("Remove up to 2 animals from any card", [new RemoveCardResourceEffect(CardResourceType.Animal, 2, AnyPlayer: true)]),
                new EffectOption("Remove up to 5 plants from any player", [new RemoveResourceEffect(ResourceType.Plants, 5)]),
            ]),
        ]);

        // 051: Miranda Resort — +1 MC prod per Earth tag you have
        SetEffects(builder, "051", onPlayEffects:
            [new ChangeProductionPerTagEffect(ResourceType.MegaCredits, Tag.Earth, 1)]);

        // 052: Fish — Action: add 1 animal. -1 plant prod (any). 1VP/animal
        SetEffects(builder, "052",
            onPlayEffects: [new ReduceAnyProductionEffect(ResourceType.Plants, 1)],
            action: new CardAction(null, [new AddCardResourceEffect(CardResourceType.Animal, 1, "052")]));

        // 053: Lake Marineris — Place 2 oceans
        SetEffects(builder, "053", onPlayEffects:
            [new PlaceOceanEffect(1), new PlaceOceanEffect(1)]);

        // 054: Small Animals — Action: add 1 animal. -1 plant prod (any). 1VP/2 animals
        SetEffects(builder, "054",
            onPlayEffects: [new ReduceAnyProductionEffect(ResourceType.Plants, 1)],
            action: new CardAction(null, [new AddCardResourceEffect(CardResourceType.Animal, 1, "054")]));

        // 055: Kelp Farming — +2 MC prod, +3 plant prod, +2 plants
        SetEffects(builder, "055", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, 2),
            new ChangeProductionEffect(ResourceType.Plants, 3),
            new ChangeResourceEffect(ResourceType.Plants, 2),
        ]);

        // 056: Mine — +1 steel prod
        SetEffects(builder, "056", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Steel, 1)]);

        // 057: Vesta Shipyard — +1 titanium prod
        SetEffects(builder, "057", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Titanium, 1)]);

        // 058: Beam from a Thorium Asteroid — +3 heat prod, +3 energy prod
        SetEffects(builder, "058", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Heat, 3),
            new ChangeProductionEffect(ResourceType.Energy, 3),
        ]);

        // 059: Mangrove — Place greenery on ocean-reserved area, raise O2
        SetEffects(builder, "059", onPlayEffects:
            [new PlaceTileEffect(TileType.Greenery, PlacementConstraint.OnOceanArea)]);

        // 060: Trees — +3 plant prod, +1 plant
        SetEffects(builder, "060", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Plants, 3),
            new ChangeResourceEffect(ResourceType.Plants, 1),
        ]);

        // 061: Great Escarpment Consortium — Decrease any steel prod 1, increase own 1
        SetEffects(builder, "061", onPlayEffects:
        [
            new ReduceAnyProductionEffect(ResourceType.Steel, 1),
            new ChangeProductionEffect(ResourceType.Steel, 1),
        ]);

        // 062: Mineral Deposit — +5 steel
        SetEffects(builder, "062", onPlayEffects:
            [new ChangeResourceEffect(ResourceType.Steel, 5)]);

        // 063: Mining Expedition — Raise O2 1 step, remove 2 plants from any, +2 steel
        SetEffects(builder, "063", onPlayEffects:
        [
            new RaiseOxygenEffect(1),
            new RemoveResourceEffect(ResourceType.Plants, 2),
            new ChangeResourceEffect(ResourceType.Steel, 2),
        ]);

        // 064: Mining Area — Place special tile on steel/titanium bonus hex adjacent to own. +1 prod of that resource
        SetEffects(builder, "064", onPlayEffects:
            [new PlaceTileEffect(TileType.MiningArea)]);
        // Note: production increase for the specific bonus is handled by the tile type logic

        // 065: Building Industries — -1 energy prod, +2 steel prod
        SetEffects(builder, "065", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, -1),
            new ChangeProductionEffect(ResourceType.Steel, 2),
        ]);

        // 066: Land Claim — Claim a non-reserved land hex. Only you may place tiles there.
        SetEffects(builder, "066", onPlayEffects: [new ClaimLandEffect()]);

        // 067: Mining Rights — Place special tile on steel/titanium bonus hex. +1 prod
        SetEffects(builder, "067", onPlayEffects:
            [new PlaceTileEffect(TileType.MiningRights)]);

        // 068: Sponsors — +2 MC prod
        SetEffects(builder, "068", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.MegaCredits, 2)]);

        // 069: Electro Catapult — Action: spend 1 plant or 1 steel for 7 MC. -1 energy prod
        // Complex action (choice of cost) — deferred
        SetEffects(builder, "069", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Energy, -1)]);

        // 070: Earth Catapult — Effect: cards cost 2 MC less
        SetEffects(builder, "070",
            ongoingEffects: [new GlobalDiscountEffect(2)]);

        // 071: Advanced Alloys — Steel +1 MC, Titanium +1 MC
        SetEffects(builder, "071",
            ongoingEffects:
            [
                new SteelValueModifierEffect(1),
                new TitaniumValueModifierEffect(1),
            ]);

        // 072: Birds — Action: add 1 animal. -2 plant prod (any). 1VP/animal
        SetEffects(builder, "072",
            onPlayEffects: [new ReduceAnyProductionEffect(ResourceType.Plants, 2)],
            action: new CardAction(null, [new AddCardResourceEffect(CardResourceType.Animal, 1, "072")]));

        // 073: Mars University — Effect: when you play science tag, may discard to draw
        // Complex triggered effect — deferred

        // 074: Viral Enhancers — Effect: when you play plant/microbe/animal, gain 1 plant or add 1 resource
        // Complex triggered effect — deferred

        // 075: Towing a Comet — +2 plants, raise O2 1, place ocean
        SetEffects(builder, "075", onPlayEffects:
        [
            new ChangeResourceEffect(ResourceType.Plants, 2),
            new RaiseOxygenEffect(1),
            new PlaceOceanEffect(1),
        ]);

        // 076: Space Mirrors — Action: spend 7 MC, +1 energy prod
        SetEffects(builder, "076",
            action: new CardAction(new SpendMCCost(7), [new ChangeProductionEffect(ResourceType.Energy, 1)]));

        // 077: Solar Wind Power — +1 energy prod, +2 titanium
        SetEffects(builder, "077", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, 1),
            new ChangeResourceEffect(ResourceType.Titanium, 2),
        ]);

        // 078: Ice Asteroid — Place 2 oceans
        SetEffects(builder, "078", onPlayEffects:
            [new PlaceOceanEffect(1), new PlaceOceanEffect(1)]);

        // 079: Quantum Extractor — Effect: space cards cost 2 less. +4 energy prod
        SetEffects(builder, "079",
            onPlayEffects: [new ChangeProductionEffect(ResourceType.Energy, 4)],
            ongoingEffects: [new TagDiscountEffect(Tag.Space, 2)]);

        // 080: Giant Ice Asteroid — Raise temp 2, place 2 oceans, remove up to 6 plants
        SetEffects(builder, "080", onPlayEffects:
        [
            new RaiseTemperatureEffect(2),
            new PlaceOceanEffect(1),
            new PlaceOceanEffect(1),
            new RemoveResourceEffect(ResourceType.Plants, 6),
        ]);

        // Cards 081+ will be implemented in subsequent batches
    }
}

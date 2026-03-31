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

        // 081: Ganymede Colony — off-map city. 1VP per Jovian tag
        SetEffects(builder, "081", onPlayEffects:
            [new PlaceOffMapCityEffect("Ganymede Colony")]);

        // 082: Callisto Penal Mines — +3 MC prod
        SetEffects(builder, "082", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.MegaCredits, 3)]);

        // 083: Giant Space Mirror — +3 energy prod
        SetEffects(builder, "083", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Energy, 3)]);

        // 084: Trans-Neptune Probe — VP only

        // 085: Commercial District — -1 energy prod, +4 MC prod, place special tile
        SetEffects(builder, "085", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, -1),
            new ChangeProductionEffect(ResourceType.MegaCredits, 4),
            new PlaceTileEffect(TileType.CommercialDistrict),
        ]);

        // 086: Robotic Workforce — Duplicate production box of one of your building cards
        // Complex — deferred

        // 087: Grass — +1 plant prod, +3 plants
        SetEffects(builder, "087", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Plants, 1),
            new ChangeResourceEffect(ResourceType.Plants, 3),
        ]);

        // 088: Heather — +1 plant prod, +1 plant
        SetEffects(builder, "088", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Plants, 1),
            new ChangeResourceEffect(ResourceType.Plants, 1),
        ]);

        // 089: Peroxide Power — -1 MC prod, +2 energy prod
        SetEffects(builder, "089", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, -1),
            new ChangeProductionEffect(ResourceType.Energy, 2),
        ]);

        // 090: Research — Draw 2 cards (counts as 2 science tags)
        SetEffects(builder, "090", onPlayEffects: [new DrawCardsEffect(2)]);

        // 091: Gene Repair — +2 MC prod
        SetEffects(builder, "091", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.MegaCredits, 2)]);

        // 092: Io Mining Industries — +2 titanium prod, +2 MC prod. 1VP/Jovian tag
        SetEffects(builder, "092", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Titanium, 2),
            new ChangeProductionEffect(ResourceType.MegaCredits, 2),
        ]);

        // 093: Bushes — +2 plant prod, +2 plants
        SetEffects(builder, "093", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Plants, 2),
            new ChangeResourceEffect(ResourceType.Plants, 2),
        ]);

        // 094: Mass Converter — Effect: space cards cost 2 less. +6 energy prod (requires 5 science)
        SetEffects(builder, "094",
            onPlayEffects: [new ChangeProductionEffect(ResourceType.Energy, 6)],
            ongoingEffects: [new TagDiscountEffect(Tag.Space, 2)]);

        // 095: Physics Complex — Action: spend 6 energy, add science resource. 2VP/science
        SetEffects(builder, "095",
            action: new CardAction(new SpendEnergyCost(6),
                [new AddCardResourceEffect(CardResourceType.Science, 1, "095")]));

        // 096: Greenhouses — Gain 1 plant per city tile in play
        // Dynamic count — deferred (needs "count tiles" effect)

        // 097: Nuclear Zone — Place tile, raise temp 2
        SetEffects(builder, "097", onPlayEffects:
        [
            new PlaceTileEffect(TileType.NuclearZone),
            new RaiseTemperatureEffect(2),
        ]);

        // 098: Tropical Resort — -2 heat prod, +3 MC prod
        SetEffects(builder, "098", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Heat, -2),
            new ChangeProductionEffect(ResourceType.MegaCredits, 3),
        ]);

        // 099: Toll Station — +1 MC prod per space tag opponents have
        // Dynamic count of opponents' tags — deferred

        // 100: Fueled Generators — -1 MC prod, +1 energy prod
        SetEffects(builder, "100", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, -1),
            new ChangeProductionEffect(ResourceType.Energy, 1),
        ]);

        // 101: Ironworks — Action: spend 4 energy, gain 1 steel, raise O2 1
        SetEffects(builder, "101",
            action: new CardAction(new SpendEnergyCost(4), [
                new ChangeResourceEffect(ResourceType.Steel, 1),
                new RaiseOxygenEffect(1),
            ]));

        // 102: Power Grid — +1 energy prod per power tag including this
        SetEffects(builder, "102", onPlayEffects:
            [new ChangeProductionPerTagEffect(ResourceType.Energy, Tag.Power, 1)]);

        // 103: Steelworks — Action: spend 4 energy, gain 2 steel, raise O2 1
        SetEffects(builder, "103",
            action: new CardAction(new SpendEnergyCost(4), [
                new ChangeResourceEffect(ResourceType.Steel, 2),
                new RaiseOxygenEffect(1),
            ]));

        // 104: Ore Processor — Action: spend 4 energy, gain 1 titanium, raise O2 1
        SetEffects(builder, "104",
            action: new CardAction(new SpendEnergyCost(4), [
                new ChangeResourceEffect(ResourceType.Titanium, 1),
                new RaiseOxygenEffect(1),
            ]));

        // 105: Earth Office — Effect: Earth cards cost 3 less
        SetEffects(builder, "105",
            ongoingEffects: [new TagDiscountEffect(Tag.Earth, 3)]);

        // 106: Acquired Company — +3 MC prod
        SetEffects(builder, "106", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.MegaCredits, 3)]);

        // 107: Media Archives — Gain 1 MC per event ever played by all players
        // Dynamic count — deferred

        // 108: Open City — +2 plants, place city, -1 energy prod, +4 MC prod
        SetEffects(builder, "108", onPlayEffects:
        [
            new ChangeResourceEffect(ResourceType.Plants, 2),
            new PlaceTileEffect(TileType.City),
            new ChangeProductionEffect(ResourceType.Energy, -1),
            new ChangeProductionEffect(ResourceType.MegaCredits, 4),
        ]);

        // 109: Media Group — Effect: after you play event, gain 3 MC
        SetEffects(builder, "109",
            ongoingEffects: [new WhenYouEffect(TriggerCondition.PlayEventTag,
                new ChangeResourceEffect(ResourceType.MegaCredits, 3))]);

        // 110: Business Network — -1 MC prod. Action: look at top card, buy or discard
        SetEffects(builder, "110",
            onPlayEffects: [new ChangeProductionEffect(ResourceType.MegaCredits, -1)]);
        // Action deferred — complex

        // 111: Business Contacts — Look at top 4 cards, keep 2 discard 2
        // Complex — deferred

        // 112: Bribed Committee — +2 TR
        SetEffects(builder, "112", onPlayEffects: [new ChangeTREffect(2)]);

        // 113: Solar Power — +1 energy prod
        SetEffects(builder, "113", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Energy, 1)]);

        // 114: Breathing Filters — VP only

        // 115: Artificial Photosynthesis — +1 plant prod OR +2 energy prod
        SetEffects(builder, "115", onPlayEffects:
        [
            new ChooseEffect(
            [
                new EffectOption("Increase plant production 1 step", [new ChangeProductionEffect(ResourceType.Plants, 1)]),
                new EffectOption("Increase energy production 2 steps", [new ChangeProductionEffect(ResourceType.Energy, 2)]),
            ]),
        ]);

        // 116: Artificial Lake — Place ocean on a non-ocean land area
        SetEffects(builder, "116", onPlayEffects:
            [new PlaceTileEffect(TileType.Ocean, PlacementConstraint.OceanOnLand)]);

        // 117: Geothermal Power — +2 energy prod
        SetEffects(builder, "117", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Energy, 2)]);

        // 118: Farming — +2 MC prod, +2 plant prod, +2 plants
        SetEffects(builder, "118", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, 2),
            new ChangeProductionEffect(ResourceType.Plants, 2),
            new ChangeResourceEffect(ResourceType.Plants, 2),
        ]);

        // 119: Dust Seals — VP only

        // 120: Urbanized Area — -1 energy prod, +2 MC prod, place city adjacent to 2 cities
        SetEffects(builder, "120", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, -1),
            new ChangeProductionEffect(ResourceType.MegaCredits, 2),
            new PlaceTileEffect(TileType.City, PlacementConstraint.AdjacentTo2Cities),
        ]);

        // 121: Sabotage — Remove up to 3 titanium OR 4 steel OR 7 MC from any player
        SetEffects(builder, "121", onPlayEffects:
        [
            new ChooseEffect(
            [
                new EffectOption("Remove up to 3 titanium", [new RemoveResourceEffect(ResourceType.Titanium, 3)]),
                new EffectOption("Remove up to 4 steel", [new RemoveResourceEffect(ResourceType.Steel, 4)]),
                new EffectOption("Remove up to 7 MC", [new RemoveResourceEffect(ResourceType.MegaCredits, 7)]),
            ]),
        ]);

        // 122: Moss — Lose 1 plant, +1 plant prod
        SetEffects(builder, "122", onPlayEffects:
        [
            new ChangeResourceEffect(ResourceType.Plants, -1),
            new ChangeProductionEffect(ResourceType.Plants, 1),
        ]);

        // 123: Industrial Center — Action: spend 7 MC, +1 steel prod. Place tile adjacent to city
        SetEffects(builder, "123",
            onPlayEffects: [new PlaceTileEffect(TileType.IndustrialCenter)],
            action: new CardAction(new SpendMCCost(7), [new ChangeProductionEffect(ResourceType.Steel, 1)]));

        // 124: Hired Raiders — Steal up to 2 steel or 3 MC from any player
        SetEffects(builder, "124", onPlayEffects:
        [
            new ChooseEffect(
            [
                new EffectOption("Steal up to 2 steel", [new RemoveResourceEffect(ResourceType.Steel, 2)]),
                new EffectOption("Steal up to 3 MC", [new RemoveResourceEffect(ResourceType.MegaCredits, 3)]),
            ]),
        ]);

        // 125: Hackers — -1 energy prod, decrease any MC prod 2, +2 MC prod
        SetEffects(builder, "125", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, -1),
            new ReduceAnyProductionEffect(ResourceType.MegaCredits, 2),
            new ChangeProductionEffect(ResourceType.MegaCredits, 2),
        ]);

        // 126: GHG Factories — -1 energy prod, +4 heat prod
        SetEffects(builder, "126", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, -1),
            new ChangeProductionEffect(ResourceType.Heat, 4),
        ]);

        // 127: Subterranean Reservoir — Place 1 ocean
        SetEffects(builder, "127", onPlayEffects: [new PlaceOceanEffect(1)]);

        // 128: Ecological Zone — Place tile adjacent to greenery. 1VP/2 animals
        // Effect: when you play animal/plant tag (including this card's 2 tags), add animal to this
        SetEffects(builder, "128",
            onPlayEffects:
            [
                new PlaceTileEffect(TileType.EcologicalZone),
                new AddCardResourceEffect(CardResourceType.Animal, 2, "128"), // self-trigger for plant+animal tags on this card
            ],
            ongoingEffects:
            [
                new WhenYouEffect(TriggerCondition.PlayAnimalTag, new AddCardResourceEffect(CardResourceType.Animal, 1, "128")),
                new WhenYouEffect(TriggerCondition.PlayPlantTag, new AddCardResourceEffect(CardResourceType.Animal, 1, "128")),
            ]);

        // 129: Zeppelins — +1 MC prod per city on Mars
        // Dynamic count — deferred

        // 130: Worms — +1 plant prod per 2 microbe tags you have
        // Dynamic count — deferred

        // 131: Decomposers — Effect: when you play animal/plant/microbe tag (including this), add microbe. 1VP/3
        SetEffects(builder, "131",
            onPlayEffects: [new AddCardResourceEffect(CardResourceType.Microbe, 1, "131")], // self-trigger for microbe tag
            ongoingEffects:
            [
                new WhenYouEffect(TriggerCondition.PlayAnimalTag, new AddCardResourceEffect(CardResourceType.Microbe, 1, "131")),
                new WhenYouEffect(TriggerCondition.PlayPlantTag, new AddCardResourceEffect(CardResourceType.Microbe, 1, "131")),
                new WhenYouEffect(TriggerCondition.PlayMicrobeTag, new AddCardResourceEffect(CardResourceType.Microbe, 1, "131")),
            ]);

        // 132: Fusion Power — +3 energy prod
        SetEffects(builder, "132", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Energy, 3)]);

        // 133: Symbiotic Fungus — Action: add microbe to ANOTHER card
        SetEffects(builder, "133",
            action: new CardAction(null, [new AddCardResourceEffect(CardResourceType.Microbe, 1)]));

        // 134: Extreme-Cold Fungus — Action: gain 1 plant or add 2 microbes to another card
        SetEffects(builder, "134",
            action: new CardAction(null, [
                new ChooseEffect([
                    new EffectOption("Gain 1 plant", [new ChangeResourceEffect(ResourceType.Plants, 1)]),
                    new EffectOption("Add 2 microbes to another card", [new AddCardResourceEffect(CardResourceType.Microbe, 2)]),
                ]),
            ]));

        // 135: Advanced Ecosystems — VP only (3 VP, requires plant+microbe+animal tags)

        // 136: Great Dam — +2 energy prod
        SetEffects(builder, "136", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Energy, 2)]);

        // 137: Cartel — +1 MC prod per Earth tag including this
        SetEffects(builder, "137", onPlayEffects:
            [new ChangeProductionPerTagEffect(ResourceType.MegaCredits, Tag.Earth, 1)]);

        // 138: Strip Mine — -2 energy prod, +2 steel prod, +1 titanium prod, raise O2 2
        SetEffects(builder, "138", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, -2),
            new ChangeProductionEffect(ResourceType.Steel, 2),
            new ChangeProductionEffect(ResourceType.Titanium, 1),
            new RaiseOxygenEffect(2),
        ]);

        // 139: Wave Power — +1 energy prod
        SetEffects(builder, "139", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Energy, 1)]);

        // 140: Lava Flows — Raise temp 2, place Lava Flows tile
        SetEffects(builder, "140", onPlayEffects:
        [
            new RaiseTemperatureEffect(2),
            new PlaceTileEffect(TileType.LavaFlows),
        ]);

        // 141: Power Plant — +1 energy prod
        SetEffects(builder, "141", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Energy, 1)]);

        // 142: Mohole Area — +4 heat prod, place on ocean-reserved area
        SetEffects(builder, "142", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Heat, 4),
            new PlaceTileEffect(TileType.MoholeArea, PlacementConstraint.OceanReserved),
        ]);

        // 143: Large Convoy — Place ocean, draw 2 cards, gain 5 plants or add 4 animals to another
        SetEffects(builder, "143", onPlayEffects:
        [
            new PlaceOceanEffect(1),
            new DrawCardsEffect(2),
            new ChooseEffect([
                new EffectOption("Gain 5 plants", [new ChangeResourceEffect(ResourceType.Plants, 5)]),
                new EffectOption("Add 4 animals to another card", [new AddCardResourceEffect(CardResourceType.Animal, 4)]),
            ]),
        ]);

        // 144: Titanium Mine — +1 titanium prod
        SetEffects(builder, "144", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Titanium, 1)]);

        // 145: Tectonic Stress Power — +3 energy prod
        SetEffects(builder, "145", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Energy, 3)]);

        // 146: Nitrophilic Moss — Lose 2 plants, +2 plant prod
        SetEffects(builder, "146", onPlayEffects:
        [
            new ChangeResourceEffect(ResourceType.Plants, -2),
            new ChangeProductionEffect(ResourceType.Plants, 2),
        ]);

        // 147: Herbivores — Effect: when you place greenery, add animal. -1 plant prod (any). 1VP/2 animals
        SetEffects(builder, "147",
            onPlayEffects: [new ReduceAnyProductionEffect(ResourceType.Plants, 1)],
            ongoingEffects:
                [new WhenYouEffect(TriggerCondition.PlaceGreeneryTile, new AddCardResourceEffect(CardResourceType.Animal, 1, "147"))]);

        // 148: Insects — +1 plant prod per plant tag you have
        SetEffects(builder, "148", onPlayEffects:
            [new ChangeProductionPerTagEffect(ResourceType.Plants, Tag.Plant, 1)]);

        // 149: CEO's Favorite Project — Add 1 resource to any card with resources
        // Complex — deferred

        // 150: Anti-gravity Technology — Effect: cards cost 2 less
        SetEffects(builder, "150",
            ongoingEffects: [new GlobalDiscountEffect(2)]);

        // 151: Investment Loan — -1 MC prod, +10 MC
        SetEffects(builder, "151", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, -1),
            new ChangeResourceEffect(ResourceType.MegaCredits, 10),
        ]);

        // 152: Insulation — Decrease heat prod any number, increase MC prod same amount
        // Complex — deferred (variable amount choice)

        // 153: Adaptation Technology — Effect: global requirements +/- 2
        SetEffects(builder, "153",
            ongoingEffects: [new RequirementModifierEffect(2)]);

        // 154: Caretaker Contract — Action: spend 8 heat, +1 TR
        SetEffects(builder, "154",
            action: new CardAction(new SpendHeatCost(8), [new ChangeTREffect(1)]));

        // 155: Designed Microorganisms — +2 plant prod
        SetEffects(builder, "155", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Plants, 2)]);

        // 156: Standard Technology — Effect: after paying for standard project (not sell patents), gain 3 MC
        // Complex triggered effect — deferred

        // 157: Nitrite Reducing Bacteria — Action: add 1 microbe, or remove 3 to +1 TR
        // Complex action with choice — deferred

        // 158: Industrial Microbes — +1 energy prod, +1 steel prod
        SetEffects(builder, "158", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, 1),
            new ChangeProductionEffect(ResourceType.Steel, 1),
        ]);

        // 159: Lichen — +1 plant prod
        SetEffects(builder, "159", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Plants, 1)]);

        // 160: Power Supply Consortium — Decrease any energy prod 1, +1 own energy prod
        SetEffects(builder, "160", onPlayEffects:
        [
            new ReduceAnyProductionEffect(ResourceType.Energy, 1),
            new ChangeProductionEffect(ResourceType.Energy, 1),
        ]);

        // 161: Convoy from Europa — Place 1 ocean, draw 1 card
        SetEffects(builder, "161", onPlayEffects:
            [new PlaceOceanEffect(1), new DrawCardsEffect(1)]);

        // 162: Imported GHG — +1 heat prod, +3 heat
        SetEffects(builder, "162", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Heat, 1),
            new ChangeResourceEffect(ResourceType.Heat, 3),
        ]);

        // 163: Imported Nitrogen — +1 TR, +4 plants, add 3 microbes + 2 animals to other cards
        SetEffects(builder, "163", onPlayEffects:
        [
            new ChangeTREffect(1),
            new ChangeResourceEffect(ResourceType.Plants, 4),
            new AddCardResourceEffect(CardResourceType.Microbe, 3),
            new AddCardResourceEffect(CardResourceType.Animal, 2),
        ]);

        // 164: Micro-Mills — +1 heat prod
        SetEffects(builder, "164", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Heat, 1)]);

        // 165: Magnetic Field Generators — -4 energy prod, +2 plant prod, +3 TR
        SetEffects(builder, "165", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, -4),
            new ChangeProductionEffect(ResourceType.Plants, 2),
            new ChangeTREffect(3),
        ]);

        // 166: Shuttles — Effect: space cards cost 2 less. -1 energy prod, +2 MC prod
        SetEffects(builder, "166",
            onPlayEffects:
            [
                new ChangeProductionEffect(ResourceType.Energy, -1),
                new ChangeProductionEffect(ResourceType.MegaCredits, 2),
            ],
            ongoingEffects: [new TagDiscountEffect(Tag.Space, 2)]);

        // 167: Import of Advanced GHG — +2 heat prod
        SetEffects(builder, "167", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Heat, 2)]);

        // 168: Windmills — +1 energy prod
        SetEffects(builder, "168", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Energy, 1)]);

        // 169: Tundra Farming — +1 plant prod, +2 MC prod, +1 plant
        SetEffects(builder, "169", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Plants, 1),
            new ChangeProductionEffect(ResourceType.MegaCredits, 2),
            new ChangeResourceEffect(ResourceType.Plants, 1),
        ]);

        // 170: Aerobraked Ammonia Asteroid — Add 2 microbes to another card, +3 heat prod, +1 plant prod
        SetEffects(builder, "170", onPlayEffects:
        [
            new AddCardResourceEffect(CardResourceType.Microbe, 2),
            new ChangeProductionEffect(ResourceType.Heat, 3),
            new ChangeProductionEffect(ResourceType.Plants, 1),
        ]);

        // 171: Magnetic Field Dome — -2 energy prod, +1 plant prod, +1 TR
        SetEffects(builder, "171", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, -2),
            new ChangeProductionEffect(ResourceType.Plants, 1),
            new ChangeTREffect(1),
        ]);

        // 172: Pets — Effect: when any city is placed, add animal. +1 animal on play. 1VP/2 animals
        SetEffects(builder, "172",
            onPlayEffects: [new AddCardResourceEffect(CardResourceType.Animal, 1, "172")],
            ongoingEffects:
                [new WhenAnyoneEffect(TriggerCondition.PlaceAnyCityTile, new AddCardResourceEffect(CardResourceType.Animal, 1, "172"))]);

        // 173: Protected Habitats — Opponents may not remove your animals, plants, or microbes
        // Passive protection — deferred (needs resource protection mechanic)

        // 174: Protected Valley — +2 MC prod, place greenery on ocean-reserved area
        SetEffects(builder, "174", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, 2),
            new PlaceTileEffect(TileType.Greenery, PlacementConstraint.OnOceanArea),
        ]);

        // 175: Satellites — +1 MC prod per space tag including this
        SetEffects(builder, "175", onPlayEffects:
            [new ChangeProductionPerTagEffect(ResourceType.MegaCredits, Tag.Space, 1)]);

        // 176: Noctis Farming — +1 MC prod, +2 plants
        SetEffects(builder, "176", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.MegaCredits, 1),
            new ChangeResourceEffect(ResourceType.Plants, 2),
        ]);

        // 177: Water Splitting Plant — Action: spend 3 energy, raise O2 1
        SetEffects(builder, "177",
            action: new CardAction(new SpendEnergyCost(3), [new RaiseOxygenEffect(1)]));

        // 178: Heat Trappers — Decrease any heat prod 2, +1 energy prod
        SetEffects(builder, "178", onPlayEffects:
        [
            new ReduceAnyProductionEffect(ResourceType.Heat, 2),
            new ChangeProductionEffect(ResourceType.Energy, 1),
        ]);

        // 179: Soil Factory — -1 energy prod, +1 plant prod
        SetEffects(builder, "179", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, -1),
            new ChangeProductionEffect(ResourceType.Plants, 1),
        ]);

        // 180: Fuel Factory — -1 energy prod, +1 titanium prod, +1 MC prod
        SetEffects(builder, "180", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, -1),
            new ChangeProductionEffect(ResourceType.Titanium, 1),
            new ChangeProductionEffect(ResourceType.MegaCredits, 1),
        ]);

        // 181: Ice Cap Melting — Place 1 ocean
        SetEffects(builder, "181", onPlayEffects: [new PlaceOceanEffect(1)]);

        // 182: Corporate Stronghold — -1 energy prod, +3 MC prod, place city
        SetEffects(builder, "182", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, -1),
            new ChangeProductionEffect(ResourceType.MegaCredits, 3),
            new PlaceTileEffect(TileType.City),
        ]);

        // 183: Biomass Combustors — Decrease any plant prod 1, +2 energy prod
        SetEffects(builder, "183", onPlayEffects:
        [
            new ReduceAnyProductionEffect(ResourceType.Plants, 1),
            new ChangeProductionEffect(ResourceType.Energy, 2),
        ]);

        // 184: Livestock — Action: add 1 animal. -1 plant prod. 1VP/animal
        SetEffects(builder, "184",
            onPlayEffects: [new ChangeProductionEffect(ResourceType.Plants, -1)],
            action: new CardAction(null, [new AddCardResourceEffect(CardResourceType.Animal, 1, "184")]));

        // 185: Olympus Conference — Effect: when you play science tag (including this),
        // add science resource to this card OR draw a card
        // Complex triggered choice — deferred
        SetEffects(builder, "185",
            onPlayEffects: [new AddCardResourceEffect(CardResourceType.Science, 1, "185")]);

        // 186: Rad-Suits — +1 MC prod
        SetEffects(builder, "186", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.MegaCredits, 1)]);

        // 187: Aquifer Pumping — Action: spend 8 MC to place ocean (steel usable as building)
        // Complex action (steel payment) — deferred
        SetEffects(builder, "187",
            action: new CardAction(new SpendMCCost(8), [new PlaceOceanEffect(1)]));

        // 188: Flooding — Place ocean. May remove 4 MC from owner of adjacent tile
        SetEffects(builder, "188", onPlayEffects: [new PlaceOceanEffect(1)]);
        // MC removal from adjacent tile owner — deferred

        // 189: Energy Saving — +1 energy prod per city in play
        // Dynamic count — deferred

        // 190: Local Heat Trapping — Spend 5 heat, gain 4 plants or add 2 animals to another
        SetEffects(builder, "190", onPlayEffects:
        [
            new ChangeResourceEffect(ResourceType.Heat, -5),
            new ChooseEffect([
                new EffectOption("Gain 4 plants", [new ChangeResourceEffect(ResourceType.Plants, 4)]),
                new EffectOption("Add 2 animals to another card", [new AddCardResourceEffect(CardResourceType.Animal, 2)]),
            ]),
        ]);

        // 191: Permafrost Extraction — Place 1 ocean
        SetEffects(builder, "191", onPlayEffects: [new PlaceOceanEffect(1)]);

        // 192: Invention Contest — Look at top 3 cards, take 1, discard 2
        // Complex — deferred

        // 193: Plantation — Place greenery, raise O2
        SetEffects(builder, "193", onPlayEffects:
            [new PlaceTileEffect(TileType.Greenery)]);

        // 194: Power Infrastructure — Action: spend any energy, gain that much MC
        // Variable amount action — deferred

        // 195: Indentured Workers — Next card this generation costs 8 MC less
        // Temporary discount — deferred

        // 196: Lagrange Observatory — Draw 1 card
        SetEffects(builder, "196", onPlayEffects: [new DrawCardsEffect(1)]);

        // 197: Terraforming Ganymede — +1 TR per Jovian tag including this
        SetEffects(builder, "197", onPlayEffects:
            [new ChangeTRPerTagEffect(Tag.Jovian, 1)]);

        // 198: Immigration Shuttles — +5 MC prod. 1VP/3 cities in play
        SetEffects(builder, "198", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.MegaCredits, 5)]);

        // 199: Restricted Area — Action: spend 2 MC, draw a card. Place tile
        SetEffects(builder, "199",
            onPlayEffects: [new PlaceTileEffect(TileType.RestrictedArea)],
            action: new CardAction(new SpendMCCost(2), [new DrawCardsEffect(1)]));

        // 200: Immigrant City — Effect: when city is placed (including this), +1 MC prod. -1 energy prod, -2 MC prod. Place city
        SetEffects(builder, "200",
            onPlayEffects:
            [
                new ChangeProductionEffect(ResourceType.Energy, -1),
                new ChangeProductionEffect(ResourceType.MegaCredits, -2),
                new PlaceTileEffect(TileType.City),
            ],
            ongoingEffects:
                [new WhenAnyoneEffect(TriggerCondition.PlaceCityTileOnMars, new ChangeProductionEffect(ResourceType.MegaCredits, 1))]);

        // 201: Energy Tapping — Decrease any energy prod 1, +1 own energy prod
        SetEffects(builder, "201", onPlayEffects:
        [
            new ReduceAnyProductionEffect(ResourceType.Energy, 1),
            new ChangeProductionEffect(ResourceType.Energy, 1),
        ]);

        // 202: Underground Detonations — Action: spend 10 MC, +2 heat prod
        SetEffects(builder, "202",
            action: new CardAction(new SpendMCCost(10), [new ChangeProductionEffect(ResourceType.Heat, 2)]));

        // 203: Soletta — +7 heat prod
        SetEffects(builder, "203", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Heat, 7)]);

        // 204: Technology Demonstration — Draw 2 cards
        SetEffects(builder, "204", onPlayEffects: [new DrawCardsEffect(2)]);

        // 205: Rad-Chem Factory — -1 energy prod, +2 TR
        SetEffects(builder, "205", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, -1),
            new ChangeTREffect(2),
        ]);

        // 206: Special Design — Next card this generation has +/- 2 global requirements
        // Temporary requirement modifier — deferred

        // 207: Medical Lab — +1 MC prod per 2 building tags including this
        // Dynamic: floor(building_tags / 2) MC prod
        // Deferred — needs "per N tags" variant

        // 208: AI Central — Action: draw 2 cards. -1 energy prod
        SetEffects(builder, "208",
            onPlayEffects: [new ChangeProductionEffect(ResourceType.Energy, -1)],
            action: new CardAction(null, [new DrawCardsEffect(2)]));

        // ── Prelude expansion project cards ────────────────────

        // P36: House Printing — +1 steel prod
        SetEffects(builder, "P36", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.Steel, 1)]);

        // P37: Lava Tube Settlement — -1 energy prod, +2 MC prod, place city
        SetEffects(builder, "P37", onPlayEffects:
        [
            new ChangeProductionEffect(ResourceType.Energy, -1),
            new ChangeProductionEffect(ResourceType.MegaCredits, 2),
            new PlaceTileEffect(TileType.City),
        ]);

        // P38: Martian Survey — Draw 2 cards
        SetEffects(builder, "P38", onPlayEffects: [new DrawCardsEffect(2)]);

        // P39: Psychrophiles — Action: add 1 microbe. Microbes here can pay for plant cards
        // Complex payment mechanic — action implemented, payment deferred
        SetEffects(builder, "P39",
            action: new CardAction(null, [new AddCardResourceEffect(CardResourceType.Microbe, 1, "P39")]));

        // P40: Research Coordination — no effects (wild tag only)

        // P41: SF Memorial — Draw 1 card
        SetEffects(builder, "P41", onPlayEffects: [new DrawCardsEffect(1)]);

        // P42: Space Hotels — +4 MC prod
        SetEffects(builder, "P42", onPlayEffects:
            [new ChangeProductionEffect(ResourceType.MegaCredits, 4)]);
    }
}

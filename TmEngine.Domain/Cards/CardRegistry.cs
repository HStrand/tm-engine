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
        // RegisterPreludeEffects(builder);
        // RegisterProjectCardEffects(builder);

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
}

using System.Collections.Immutable;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Cards.Effects;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Engine;

/// <summary>
/// Checks whether a player meets a card's requirements, accounting for
/// requirement modifiers (Inventrix, Adaptation Technology, Special Design).
/// Also provides helpers for payment validation with steel/titanium value modifiers.
/// </summary>
public static class RequirementChecker
{
    /// <summary>
    /// Check if a player can play a card, considering all requirements and modifiers.
    /// Returns null if the card can be played, or an error message.
    /// </summary>
    public static string? CanPlayCard(GameState state, int playerId, CardDefinition card,
        bool ignoreGlobalRequirements = false)
    {
        if (!card.HasRequirements)
            return null;

        var player = state.GetPlayer(playerId);
        var modifier = GetRequirementModifier(player);

        foreach (var req in card.Requirements)
        {
            if (ignoreGlobalRequirements && req.IsGlobalParameter)
                continue;

            var error = CheckRequirement(state, player, req, modifier);
            if (error != null)
                return error;
        }

        return null;
    }

    private static string? CheckRequirement(GameState state, PlayerState player, CardRequirement req, int modifier)
    {
        return req.Type switch
        {
            // Global parameters (affected by requirement modifiers)
            "oxygen" when state.Oxygen < req.Count - modifier =>
                $"Oxygen must be at least {req.Count}%.",
            "max_oxygen" when state.Oxygen > req.Count + modifier =>
                $"Oxygen must be {req.Count}% or less.",
            "temperature" when state.Temperature < req.Count - (modifier * Constants.TemperatureStep) =>
                $"Temperature must be at least {req.Count}°C.",
            "max_temperature" when state.Temperature > req.Count + (modifier * Constants.TemperatureStep) =>
                $"Temperature must be {req.Count}°C or less.",
            "oceans" when state.OceansPlaced < req.Count - modifier =>
                $"Must have at least {req.Count} oceans.",
            "max_oceans" when state.OceansPlaced > req.Count + modifier =>
                $"Must have {req.Count} or fewer oceans.",

            // Tag requirements (NOT affected by global modifiers)
            "science_tag" when CountPlayerTags(player, Tag.Science) < req.Count =>
                $"Need {req.Count} Science tags.",
            "earth_tag" when CountPlayerTags(player, Tag.Earth) < req.Count =>
                $"Need {req.Count} Earth tags.",
            "jovian_tag" when CountPlayerTags(player, Tag.Jovian) < req.Count =>
                $"Need {req.Count} Jovian tags.",
            "power_tag" when CountPlayerTags(player, Tag.Power) < req.Count =>
                $"Need {req.Count} Power tags.",
            "plant_tag" when CountPlayerTags(player, Tag.Plant) < req.Count =>
                $"Need {req.Count} Plant tags.",
            "microbe_tag" when CountPlayerTags(player, Tag.Microbe) < req.Count =>
                $"Need {req.Count} Microbe tags.",
            "animal_tag" when CountPlayerTags(player, Tag.Animal) < req.Count =>
                $"Need {req.Count} Animal tags.",

            // Production requirements
            "titanium_production" when player.Production.Titanium < req.Count =>
                $"Need {req.Count} titanium production.",
            "steel_production" when player.Production.Steel < req.Count =>
                $"Need {req.Count} steel production.",

            // Tile requirements
            "greeneries" when CountOwnedTilesOfType(state, player.PlayerId, TileType.Greenery) < req.Count =>
                $"Need {req.Count} greenery tiles.",
            "cities" when CountOwnedCities(state, player.PlayerId) < req.Count =>
                $"Need {req.Count} cities in play.",

            _ => null,
        };
    }

    private static int CountOwnedTilesOfType(GameState state, int playerId, TileType type) =>
        state.PlacedTiles.Values.Count(t => t.OwnerId == playerId && t.Type == type);

    private static int CountOwnedCities(GameState state, int playerId) =>
        state.PlacedTiles.Values.Count(t => t.OwnerId == playerId && (t.Type == TileType.City || t.Type == TileType.Capital))
        + state.OffMapTiles.Count(t => t.OwnerId == playerId && t.Type == TileType.City);

    /// <summary>
    /// Check if a player can perform the mandatory effects of a card.
    /// Verifies production decreases won't go below minimums and resource costs are affordable.
    /// Returns null if all effects are affordable, or an error message.
    /// </summary>
    public static string? CanAffordEffects(GameState state, int playerId, CardEntry entry)
    {
        var player = state.GetPlayer(playerId);

        // Track cumulative changes to detect combined effects
        int mcProdChange = 0;
        int steelProdChange = 0;
        int titaniumProdChange = 0;
        int plantProdChange = 0;
        int energyProdChange = 0;
        int heatProdChange = 0;

        foreach (var effect in entry.OnPlayEffects)
        {
            // Self production decreases
            if (effect is ChangeProductionEffect prod && prod.Amount < 0)
            {
                switch (prod.Resource)
                {
                    case ResourceType.MegaCredits: mcProdChange += prod.Amount; break;
                    case ResourceType.Steel: steelProdChange += prod.Amount; break;
                    case ResourceType.Titanium: titaniumProdChange += prod.Amount; break;
                    case ResourceType.Plants: plantProdChange += prod.Amount; break;
                    case ResourceType.Energy: energyProdChange += prod.Amount; break;
                    case ResourceType.Heat: heatProdChange += prod.Amount; break;
                }
            }

            // ReduceAnyProductionEffect — at least one player must have enough
            if (effect is ReduceAnyProductionEffect reduce)
            {
                bool anyoneHasEnough = state.Players.Any(p =>
                    p.Production.Get(reduce.Resource) >= reduce.Amount);
                if (!anyoneHasEnough)
                    return $"No player has {reduce.Amount} {reduce.Resource} production to reduce.";
            }
        }

        // Check self production floors
        if (player.Production.MegaCredits + mcProdChange < Constants.MinMCProduction)
            return $"Would reduce MC production below {Constants.MinMCProduction}.";
        if (player.Production.Steel + steelProdChange < 0)
            return "Would reduce steel production below 0.";
        if (player.Production.Titanium + titaniumProdChange < 0)
            return "Would reduce titanium production below 0.";
        if (player.Production.Plants + plantProdChange < 0)
            return "Would reduce plant production below 0.";
        if (player.Production.Energy + energyProdChange < 0)
            return "Would reduce energy production below 0.";
        if (player.Production.Heat + heatProdChange < 0)
            return "Would reduce heat production below 0.";

        return null;
    }

    /// <summary>
    /// Get the total requirement modifier for a player (sum of all RequirementModifierEffect values).
    /// E.g., Inventrix gives +2, Adaptation Technology gives +2, stacking to +4.
    /// </summary>
    public static int GetRequirementModifier(PlayerState player)
    {
        int modifier = 0;

        // Check corporation ongoing effects
        if (!string.IsNullOrEmpty(player.CorporationId) && CardRegistry.TryGet(player.CorporationId, out var corp))
        {
            modifier += SumModifiers<RequirementModifierEffect>(corp.OngoingEffects);
        }

        // Check played blue cards for ongoing effects
        foreach (var cardId in player.PlayedCards)
        {
            if (CardRegistry.TryGet(cardId, out var card))
            {
                modifier += SumModifiers<RequirementModifierEffect>(card.OngoingEffects);
            }
        }

        return modifier;
    }

    /// <summary>
    /// Get the effective steel value for a player (base + modifiers).
    /// </summary>
    public static int GetSteelValue(PlayerState player)
    {
        int value = Constants.SteelValue;

        if (!string.IsNullOrEmpty(player.CorporationId) && CardRegistry.TryGet(player.CorporationId, out var corp))
            value += SumModifiers<SteelValueModifierEffect>(corp.OngoingEffects);

        foreach (var cardId in player.PlayedCards)
        {
            if (CardRegistry.TryGet(cardId, out var card))
                value += SumModifiers<SteelValueModifierEffect>(card.OngoingEffects);
        }

        return value;
    }

    /// <summary>
    /// Get the effective titanium value for a player (base + modifiers).
    /// </summary>
    public static int GetTitaniumValue(PlayerState player)
    {
        int value = Constants.TitaniumValue;

        if (!string.IsNullOrEmpty(player.CorporationId) && CardRegistry.TryGet(player.CorporationId, out var corp))
            value += SumModifiers<TitaniumValueModifierEffect>(corp.OngoingEffects);

        foreach (var cardId in player.PlayedCards)
        {
            if (CardRegistry.TryGet(cardId, out var card))
                value += SumModifiers<TitaniumValueModifierEffect>(card.OngoingEffects);
        }

        return value;
    }

    /// <summary>
    /// Get the total tag-based discount for a card.
    /// </summary>
    public static int GetCardDiscount(PlayerState player, ImmutableArray<Tag> cardTags)
    {
        int discount = 0;

        void CheckEntry(CardEntry entry)
        {
            foreach (var effect in entry.OngoingEffects)
            {
                if (effect is GlobalDiscountEffect global)
                    discount += global.Discount;
                else if (effect is TagDiscountEffect tagDiscount && cardTags.Contains(tagDiscount.Tag))
                    discount += tagDiscount.Discount;
            }
        }

        if (!string.IsNullOrEmpty(player.CorporationId) && CardRegistry.TryGet(player.CorporationId, out var corp))
            CheckEntry(corp);

        foreach (var cardId in player.PlayedCards)
        {
            if (CardRegistry.TryGet(cardId, out var card))
                CheckEntry(card);
        }

        return discount;
    }

    /// <summary>
    /// Check if a player can use heat to pay for cards (Helion corporation).
    /// </summary>
    public static bool CanUseHeatAsPayment(PlayerState player)
    {
        if (!string.IsNullOrEmpty(player.CorporationId) && CardRegistry.TryGet(player.CorporationId, out var corp))
        {
            if (corp.OngoingEffects.Any(e => e is HeatAsPaymentEffect))
                return true;
        }

        return player.PlayedCards.Any(cardId =>
            CardRegistry.TryGet(cardId, out var card) &&
            card.OngoingEffects.Any(e => e is HeatAsPaymentEffect));
    }

    /// <summary>
    /// Get the MC rebate for spending on a card or standard project with the given printed cost.
    /// Checks HighCostRebateEffect on corporation and played cards (Credicor).
    /// </summary>
    public static int GetHighCostRebate(PlayerState player, int printedCost)
    {
        int rebate = 0;

        void CheckEntry(CardEntry entry)
        {
            foreach (var effect in entry.OngoingEffects)
                if (effect is HighCostRebateEffect r && printedCost >= r.CostThreshold)
                    rebate += r.Rebate;
        }

        if (!string.IsNullOrEmpty(player.CorporationId) && CardRegistry.TryGet(player.CorporationId, out var corp))
            CheckEntry(corp);

        foreach (var cardId in player.PlayedCards)
            if (CardRegistry.TryGet(cardId, out var card))
                CheckEntry(card);

        return rebate;
    }

    /// <summary>
    /// Get the MC rebate for playing a card with non-negative VP (Vitor).
    /// </summary>
    public static int GetVPCardRebate(PlayerState player, CardDefinition card)
    {
        // Card must have VP and it must be non-negative (fixed > 0, or any variable VP formula)
        if (card.VictoryPoints == null)
            return 0;

        bool hasPositiveVP = card.VictoryPoints switch
        {
            FixedVictoryPoints f => f.Points > 0,
            PerResourceVictoryPoints => true, // resource-based VP is always potentially positive
            PerTagVictoryPoints => true,
            _ => false,
        };

        if (!hasPositiveVP)
            return 0;

        int rebate = 0;

        void CheckEntry(CardEntry entry)
        {
            foreach (var effect in entry.OngoingEffects)
                if (effect is VPCardRebateEffect r)
                    rebate += r.Rebate;
        }

        if (!string.IsNullOrEmpty(player.CorporationId) && CardRegistry.TryGet(player.CorporationId, out var corp))
            CheckEntry(corp);

        foreach (var cardId in player.PlayedCards)
            if (CardRegistry.TryGet(cardId, out var entry))
                CheckEntry(entry);

        return rebate;
    }

    /// <summary>
    /// Get the effective Power Plant standard project cost for a player.
    /// Only PowerPlantDiscountEffect applies (Thorgate).
    /// </summary>
    public static int GetPowerPlantCost(PlayerState player)
    {
        int discount = 0;

        if (!string.IsNullOrEmpty(player.CorporationId) && CardRegistry.TryGet(player.CorporationId, out var corp))
        {
            foreach (var effect in corp.OngoingEffects)
                if (effect is PowerPlantDiscountEffect d) discount += d.Discount;
        }

        foreach (var cardId in player.PlayedCards)
        {
            if (CardRegistry.TryGet(cardId, out var card))
                foreach (var effect in card.OngoingEffects)
                    if (effect is PowerPlantDiscountEffect d) discount += d.Discount;
        }

        return Math.Max(0, Constants.PowerPlantCost - discount);
    }

    /// <summary>
    /// Get the plant conversion cost for a player (default 8, modified by Ecoline etc.).
    /// </summary>
    public static int GetPlantConversionCost(PlayerState player)
    {
        if (!string.IsNullOrEmpty(player.CorporationId) && CardRegistry.TryGet(player.CorporationId, out var corp))
        {
            foreach (var effect in corp.OngoingEffects)
            {
                if (effect is PlantConversionModifierEffect mod)
                    return mod.NewCost;
            }
        }

        foreach (var cardId in player.PlayedCards)
        {
            if (CardRegistry.TryGet(cardId, out var card))
            {
                foreach (var effect in card.OngoingEffects)
                {
                    if (effect is PlantConversionModifierEffect mod)
                        return mod.NewCost;
                }
            }
        }

        return Constants.PlantsPerGreenery;
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static int CountPlayerTags(PlayerState player, Tag tag) =>
        player.CountTag(tag, CardRegistry.GetTags);

    private static int SumModifiers<T>(ImmutableArray<Effect> effects) where T : Effect
    {
        int sum = 0;
        foreach (var effect in effects)
        {
            if (effect is T)
            {
                sum += effect switch
                {
                    RequirementModifierEffect r => r.Amount,
                    SteelValueModifierEffect s => s.Amount,
                    TitaniumValueModifierEffect t => t.Amount,
                    _ => 0,
                };
            }
        }
        return sum;
    }
}

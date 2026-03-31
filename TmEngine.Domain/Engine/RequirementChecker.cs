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
    /// Check if a player can play a card, considering requirements and modifiers.
    /// Returns null if the card can be played, or an error message.
    /// </summary>
    public static string? CanPlayCard(GameState state, int playerId, CardDefinition card)
    {
        if (card.Requirement == null)
            return null;

        var player = state.GetPlayer(playerId);
        var modifier = GetRequirementModifier(player);
        var req = card.Requirement;

        // Global parameter requirements
        if (req.Oxygen != null)
        {
            if (req.IsMax)
            {
                if (state.Oxygen > req.Oxygen.Value + modifier)
                    return $"Oxygen must be {req.Oxygen.Value}% or less (adjusted: {req.Oxygen.Value + modifier}%).";
            }
            else
            {
                if (state.Oxygen < req.Oxygen.Value - modifier)
                    return $"Oxygen must be at least {req.Oxygen.Value}% (adjusted: {req.Oxygen.Value - modifier}%).";
            }
        }

        if (req.Temperature != null)
        {
            if (req.IsMax)
            {
                if (state.Temperature > req.Temperature.Value + (modifier * Constants.TemperatureStep))
                    return $"Temperature must be {req.Temperature.Value}°C or less.";
            }
            else
            {
                if (state.Temperature < req.Temperature.Value - (modifier * Constants.TemperatureStep))
                    return $"Temperature must be at least {req.Temperature.Value}°C.";
            }
        }

        if (req.Oceans != null)
        {
            if (req.IsMax)
            {
                if (state.OceansPlaced > req.Oceans.Value + modifier)
                    return $"Must have {req.Oceans.Value} or fewer oceans.";
            }
            else
            {
                if (state.OceansPlaced < req.Oceans.Value - modifier)
                    return $"Must have at least {req.Oceans.Value} oceans.";
            }
        }

        // Tag count requirements (these are NOT affected by global requirement modifiers)
        if (req.ScienceTags != null && CountPlayerTags(player, Tag.Science) < req.ScienceTags.Value)
            return $"Need {req.ScienceTags.Value} Science tags.";

        if (req.EarthTags != null && CountPlayerTags(player, Tag.Earth) < req.EarthTags.Value)
            return $"Need {req.EarthTags.Value} Earth tags.";

        if (req.JovianTags != null && CountPlayerTags(player, Tag.Jovian) < req.JovianTags.Value)
            return $"Need {req.JovianTags.Value} Jovian tags.";

        // Production requirements
        if (req.PowerProduction != null && player.Production.Energy < req.PowerProduction.Value)
            return $"Need {req.PowerProduction.Value} energy production.";

        if (req.TitaniumProduction != null && player.Production.Titanium < req.TitaniumProduction.Value)
            return $"Need {req.TitaniumProduction.Value} titanium production.";

        if (req.PlantProduction != null && player.Production.Plants < req.PlantProduction.Value)
            return $"Need {req.PlantProduction.Value} plant production.";

        if (req.EnergyProduction != null && player.Production.Energy < req.EnergyProduction.Value)
            return $"Need {req.EnergyProduction.Value} energy production.";

        return null;
    }

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

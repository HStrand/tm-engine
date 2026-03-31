using System.Collections.Immutable;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Cards.Effects;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;
using TmEngine.Domain.Moves;

namespace TmEngine.Domain.Tests.Cards;

public class CardRegistryTests
{
    // ── Card Loading ───────────────────────────────────────────

    [Fact]
    public void Registry_LoadsCards()
    {
        Assert.True(CardRegistry.All.Count > 0);
    }

    [Fact]
    public void Registry_Has267InScopeCards()
    {
        // 147 base + 73 corporate_era + 47 prelude = 267
        Assert.Equal(267, CardRegistry.All.Count);
    }

    [Fact]
    public void Registry_AllCardsHaveValidType()
    {
        foreach (var (id, entry) in CardRegistry.All)
        {
            Assert.True(Enum.IsDefined(entry.Definition.Type),
                $"Card {id} ({entry.Definition.Name}) has invalid type.");
        }
    }

    [Fact]
    public void Registry_AllCardsHaveValidExpansion()
    {
        foreach (var (id, entry) in CardRegistry.All)
        {
            Assert.True(
                entry.Definition.Expansion is Expansion.Base or Expansion.CorporateEra or Expansion.Prelude,
                $"Card {id} has out-of-scope expansion {entry.Definition.Expansion}.");
        }
    }

    [Fact]
    public void Registry_ProjectCardsHaveCost()
    {
        foreach (var (id, entry) in CardRegistry.All)
        {
            if (entry.Definition.Type is CardType.Automated or CardType.Active or CardType.Event)
            {
                Assert.True(entry.Definition.Cost >= 0,
                    $"Card {id} ({entry.Definition.Name}) has negative cost.");
            }
        }
    }

    [Fact]
    public void Registry_CorporationsHaveZeroCost()
    {
        foreach (var (id, entry) in CardRegistry.All)
        {
            if (entry.Definition.Type == CardType.Corporation)
                Assert.Equal(0, entry.Definition.Cost);
        }
    }

    [Fact]
    public void Registry_PreludesHaveZeroCost()
    {
        foreach (var (id, entry) in CardRegistry.All)
        {
            if (entry.Definition.Type == CardType.Prelude)
                Assert.Equal(0, entry.Definition.Cost);
        }
    }

    [Fact]
    public void Registry_Has17Corporations()
    {
        var corps = CardRegistry.All.Values
            .Count(e => e.Definition.Type == CardType.Corporation);
        Assert.Equal(17, corps);
    }

    [Fact]
    public void Registry_Has35Preludes()
    {
        var preludes = CardRegistry.All.Values
            .Count(e => e.Definition.Type == CardType.Prelude);
        Assert.Equal(35, preludes);
    }

    [Fact]
    public void Registry_Has215ProjectCards()
    {
        var projects = CardRegistry.All.Values
            .Count(e => e.Definition.Type is CardType.Automated or CardType.Active or CardType.Event);
        Assert.Equal(215, projects);
    }

    // ── Specific Card Lookups ──────────────────────────────────

    [Fact]
    public void Registry_ColonizerTrainingCamp_HasCorrectData()
    {
        var card = CardRegistry.GetDefinition("001");
        Assert.Equal("Colonizer Training Camp", card.Name);
        Assert.Equal(CardType.Automated, card.Type);
        Assert.Equal(8, card.Cost);
        Assert.Contains(Tag.Building, card.Tags);
        Assert.Contains(Tag.Jovian, card.Tags);
        Assert.True(card.HasRequirements);
        Assert.Contains(card.Requirements, r => r.Type == "max_oxygen" && r.Count == 5);
        Assert.NotNull(card.VictoryPoints);
    }

    [Fact]
    public void Registry_DeepWellHeating_NoRequirement()
    {
        var card = CardRegistry.GetDefinition("003");
        Assert.Equal("Deep Well Heating", card.Name);
        Assert.False(card.HasRequirements);
    }

    // ── Corporation Effects ─────────────────────────────────────

    [Fact]
    public void Corporation_Credicor_Starts57MC()
    {
        var entry = CardRegistry.Get("CORP01");
        Assert.Equal("Credicor", entry.Definition.Name);
        Assert.Contains(entry.OnPlayEffects,
            e => e is ChangeResourceEffect { Resource: ResourceType.MegaCredits, Amount: 57 });
    }

    [Fact]
    public void Corporation_Ecoline_StartsWithPlantsAndModifier()
    {
        var entry = CardRegistry.Get("CORP02");
        Assert.Equal("Ecoline", entry.Definition.Name);

        // Starting resources
        Assert.Contains(entry.OnPlayEffects,
            e => e is ChangeResourceEffect { Resource: ResourceType.MegaCredits, Amount: 36 });
        Assert.Contains(entry.OnPlayEffects,
            e => e is ChangeProductionEffect { Resource: ResourceType.Plants, Amount: 2 });
        Assert.Contains(entry.OnPlayEffects,
            e => e is ChangeResourceEffect { Resource: ResourceType.Plants, Amount: 3 });

        // Greenery costs 7 plants
        Assert.Contains(entry.OngoingEffects,
            e => e is PlantConversionModifierEffect { NewCost: 7 });
    }

    [Fact]
    public void Corporation_Helion_CanPayWithHeat()
    {
        var entry = CardRegistry.Get("CORP03");
        Assert.Contains(entry.OngoingEffects, e => e is HeatAsPaymentEffect);
    }

    [Fact]
    public void Corporation_Inventrix_HasRequirementModifier()
    {
        var entry = CardRegistry.Get("CORP06");
        Assert.Contains(entry.OngoingEffects, e => e is RequirementModifierEffect { Amount: 2 });
    }

    [Fact]
    public void Corporation_Phobolog_HasTitaniumValueModifier()
    {
        var entry = CardRegistry.Get("CORP07");
        Assert.Contains(entry.OngoingEffects, e => e is TitaniumValueModifierEffect { Amount: 1 });
    }

    [Fact]
    public void Corporation_Thorgate_HasPowerDiscount()
    {
        var entry = CardRegistry.Get("CORP09");
        Assert.Contains(entry.OngoingEffects, e => e is TagDiscountEffect { Tag: Tag.Power, Discount: 3 });
    }

    [Fact]
    public void Corporation_UNMI_HasAction()
    {
        var entry = CardRegistry.Get("CORP10");
        Assert.NotNull(entry.Action);
        Assert.IsType<SpendMCCost>(entry.Action!.Cost);
        Assert.Contains(entry.Action.Effects, e => e is ChangeTREffect { Amount: 1 });
    }

    // ── Setup with Real Cards ──────────────────────────────────

    [Fact]
    public void Setup_WithRealCards_EntersSetupPhase()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: false);
        var state = GameEngine.Setup(options, seed: 42);

        Assert.Equal(GamePhase.Setup, state.Phase);
        Assert.NotNull(state.Setup);
    }

    [Fact]
    public void Setup_DealsCorrectNumberOfCorporations()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: false);
        var state = GameEngine.Setup(options, seed: 42);

        // Each player gets 2 corporations
        Assert.Equal(Constants.CorporationsDealt, state.Setup!.DealtCorporations[0].Count);
        Assert.Equal(Constants.CorporationsDealt, state.Setup.DealtCorporations[1].Count);
    }

    [Fact]
    public void Setup_DealsCorrectNumberOfCards()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: false);
        var state = GameEngine.Setup(options, seed: 42);

        // Each player gets 10 project cards
        Assert.Equal(Constants.InitialCardsDealt, state.Setup!.DealtCards[0].Count);
        Assert.Equal(Constants.InitialCardsDealt, state.Setup.DealtCards[1].Count);
    }

    [Fact]
    public void Setup_WithPrelude_DealsSameCorporationCount()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: true);
        var state = GameEngine.Setup(options, seed: 42);

        Assert.Equal(Constants.CorporationsDealt, state.Setup!.DealtCorporations[0].Count);
    }

    [Fact]
    public void Setup_WithPrelude_DealsPreludes()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: true);
        var state = GameEngine.Setup(options, seed: 42);

        Assert.Equal(Constants.PreludesDealt, state.Setup!.DealtPreludes[0].Count);
        Assert.Equal(Constants.PreludesDealt, state.Setup.DealtPreludes[1].Count);
    }

    [Fact]
    public void Setup_IsDeterministic()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: false);
        var state1 = GameEngine.Setup(options, seed: 42);
        var state2 = GameEngine.Setup(options, seed: 42);

        Assert.Equal(state1.Setup!.DealtCorporations, state2.Setup!.DealtCorporations);
        Assert.Equal(state1.Setup.DealtCards, state2.Setup.DealtCards);
        Assert.Equal(state1.DrawPile, state2.DrawPile);
    }

    [Fact]
    public void Setup_DifferentSeeds_DifferentDeals()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: false);
        var state1 = GameEngine.Setup(options, seed: 42);
        var state2 = GameEngine.Setup(options, seed: 99);

        // Very unlikely to be the same
        Assert.NotEqual(state1.DrawPile, state2.DrawPile);
    }

    [Fact]
    public void SetupMove_AppliesCorporationEffects()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: false);
        var state = GameEngine.Setup(options, seed: 42);

        var corp0 = state.Setup!.DealtCorporations[0][0];
        var corp1 = state.Setup.DealtCorporations[1][0];

        // Both players submit setup
        var (s1, r1) = GameEngine.Apply(state, new SetupMove(0, corp0, [], []));
        Assert.True(r1.IsSuccess);

        var (s2, r2) = GameEngine.Apply(s1, new SetupMove(1, corp1, [], []));
        Assert.True(r2.IsSuccess);

        // Should now be in Action phase
        Assert.Equal(GamePhase.Action, s2.Phase);

        // Players should have their corporation set
        Assert.Equal(corp0, s2.Players[0].CorporationId);
        Assert.Equal(corp1, s2.Players[1].CorporationId);

        // Players should have received starting MC from their corporation
        Assert.True(s2.Players[0].Resources.MegaCredits > 0);
        Assert.True(s2.Players[1].Resources.MegaCredits > 0);
    }
}

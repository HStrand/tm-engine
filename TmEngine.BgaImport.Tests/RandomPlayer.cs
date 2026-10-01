using System.Collections.Immutable;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;
using TmEngine.Domain.Moves;

namespace TmEngine.BgaImport.Tests;

/// <summary>
/// Plays random legal moves for every seat. Used to check that imported positions are playable:
/// the engine must accept every move its own legal-move generator offers.
/// </summary>
public sealed class RandomPlayer(int seed)
{
    private readonly Random _rng = new(seed);

    public sealed record Outcome(GameState State, int MovesPlayed, string? Failure);

    /// <summary>Plays on until the game ends, a move is rejected, or the move budget runs out.</summary>
    public Outcome Play(GameState state, int maxMoves)
    {
        for (int played = 0; played < maxMoves; played++)
        {
            if (state.IsGameOver())
                return new Outcome(state, played, null);

            Move? move = null;
            try
            {
                move = NextMove(state);
                if (move is null)
                {
                    return new Outcome(state, played,
                        $"Nobody can move in generation {state.Generation}, phase {state.Phase} (pending: {state.PendingAction?.GetType().Name}, "
                        + $"draw pile {state.DrawPile.Count}, discard pile {state.DiscardPile.Count}).");
                }

                var (next, result) = GameEngine.Apply(state, move);
                if (result is Error error)
                    return new Outcome(state, played, $"{move} rejected: {error.Message}");
                state = next;
            }
            catch (Exception e)
            {
                return new Outcome(state, played, $"{move?.ToString() ?? "Choosing a move"} threw {e.GetType().Name}: {e.Message}");
            }
        }

        return new Outcome(state, maxMoves, null);
    }

    /// <summary>A random legal move for whichever player can act, or null when nobody can.</summary>
    public Move? NextMove(GameState state)
    {
        foreach (var player in state.Players)
        {
            var moves = LegalMoveGenerator.GetLegalMoves(state, player.PlayerId);
            if (!moves.GameOver && !moves.WaitingForOtherPlayer && Choose(state, player, moves) is { } move)
                return move;
        }
        return null;
    }

    private Move? Choose(GameState state, PlayerState player, AvailableMoves moves)
    {
        var id = player.PlayerId;
        if (moves.PendingAction is { } pending)
            return Resolve(pending, player);
        if (moves.Setup is { } setup)
        {
            var corporation = Pick(setup.AvailableCorporations);
            return new SetupMove(
                id,
                corporation,
                [.. Subset(setup.AvailablePreludes, setup.PreludesToKeep, atLeast: setup.PreludesToKeep)],
                [.. Subset(setup.AvailableCards, setup.StartingMegaCredits[corporation] / setup.CostPerCard)]);
        }
        if (moves.Prelude is { } prelude)
            return new PlayPreludeMove(id, Pick(prelude.RemainingPreludes));
        if (moves.Draft is { } draft)
            return new DraftCardMove(id, Pick(draft.DraftHand));
        if (moves.BuyCards is { } buy)
            return new BuyCardsMove(id, [.. Subset(buy.AvailableCards, player.Resources.MegaCredits / Math.Max(1, buy.CostPerCard))]);
        if (moves.Actions is { } actions)
            return Act(actions, player);
        if (moves.FinalGreenery is { } final)
        {
            return final.CanConvert && !final.ValidGreeneryLocations.IsDefaultOrEmpty
                ? new ConvertPlantsMove(id, Pick(final.ValidGreeneryLocations))
                : new PassMove(id);
        }
        return null;
    }

    private Move Act(ActionPhaseOptions actions, PlayerState player)
    {
        var id = player.PlayerId;
        var options = new List<Func<Move>>();

        foreach (var card in actions.PlayableCards)
        {
            options.Add(() => new PlayCardMove(id, card.CardId, Pay(
                card.EffectiveCost, player,
                card.CanUseSteel ? card.SteelValue : 0, card.CanUseTitanium ? card.TitaniumValue : 0, card.CanUseHeat,
                card.CardResourcePayments)));
        }

        foreach (var project in actions.StandardProjects.Where(p => p.Available))
        {
            var locations = project.ValidLocations ?? [];
            switch (project.Project)
            {
                case StandardProject.SellPatents when player.Hand.Count > 0:
                    options.Add(() => new SellPatentsMove(id, [.. Subset(player.Hand, player.Hand.Count, atLeast: 1)]));
                    break;
                case StandardProject.PowerPlant:
                    options.Add(() => new PowerPlantMove(id));
                    break;
                case StandardProject.Asteroid:
                    options.Add(() => new AsteroidMove(id));
                    break;
                case StandardProject.Aquifer when locations.Length > 0:
                    options.Add(() => new AquiferMove(id, Pick(locations)));
                    break;
                case StandardProject.Greenery when locations.Length > 0:
                    options.Add(() => new GreeneryMove(id, Pick(locations)));
                    break;
                case StandardProject.City when locations.Length > 0:
                    options.Add(() => new CityMove(id, Pick(locations)));
                    break;
            }
        }

        foreach (var action in actions.UsableCardActions)
        {
            options.Add(() => new UseCardActionMove(id, action.CardId, action.AllowSteel || action.AllowTitanium
                ? Pay(action.MCCost, player,
                    action.AllowSteel ? action.SteelValue : 0, action.AllowTitanium ? action.TitaniumValue : 0, action.CanUseHeat,
                    ImmutableDictionary<string, int>.Empty)
                : null));
        }

        foreach (var milestone in actions.ClaimableMilestones)
            options.Add(() => new ClaimMilestoneMove(id, milestone.Name));
        foreach (var award in actions.FundableAwards)
            options.Add(() => new FundAwardMove(id, award.Name));
        if (actions.CanConvertHeat)
            options.Add(() => new ConvertHeatMove(id));
        if (actions.CanConvertPlants && actions.ValidGreeneryLocations.Length > 0)
            options.Add(() => new ConvertPlantsMove(id, Pick(actions.ValidGreeneryLocations)));
        if (actions.CanPerformFirstAction)
            options.Add(() => new PerformFirstActionMove(id));

        if (options.Count == 0 || _rng.NextDouble() < 0.1)
            return actions.CanEndTurn ? new EndTurnMove(id) : new PassMove(id);

        return Pick(options)();
    }

    private Move Resolve(PendingAction pending, PlayerState player)
    {
        var id = player.PlayerId;
        switch (pending)
        {
            case PlaceTilePending { ValidLocations.Length: > 0 } place:
                return new PlaceTileMove(id, Pick(place.ValidLocations));
            case ClaimLandPending { ValidLocations.Length: > 0 } claim:
                return new PlaceTileMove(id, Pick(claim.ValidLocations));
            case RemoveResourcePending remove:
                var others = remove.ValidTargetPlayerIds.Where(t => t != id).ToList();
                if (others.Count > 0)
                    return new ChooseTargetPlayerMove(id, Pick(others));
                return remove.IsOptional || remove.ValidTargetPlayerIds.Length == 0
                    ? new PassMove(id)
                    : new ChooseTargetPlayerMove(id, Pick(remove.ValidTargetPlayerIds));
            case ReduceProductionPending { ValidTargetPlayerIds.Length: > 0 } reduce:
                return new ChooseTargetPlayerMove(id, Pick(reduce.ValidTargetPlayerIds));
            case AddCardResourcePending { ValidCardIds.Length: > 0 } add:
                return new SelectCardMove(id, Pick(add.ValidCardIds));
            case RemoveCardResourcePending { ValidCardIds.Length: > 0 } removeFromCard:
                return new SelectCardMove(id, Pick(removeFromCard.ValidCardIds));
            case CopyProductionPending { ValidCardIds.Length: > 0 } copy:
                return new SelectCardMove(id, Pick(copy.ValidCardIds));
            case ChooseCardToPlayPending { CardIds.Length: > 0 } choose:
                return new SelectCardMove(id, Pick(choose.CardIds));
            case ChooseOptionPending option:
                return new ChooseOptionMove(id, option.ValidOptionIndices is { IsDefaultOrEmpty: false } valid
                    ? Pick(valid)
                    : _rng.Next(option.Options.Length));
            case ChooseTriggerOrderPending or ChooseEffectOrderPending:
                return new ChooseEffectOrderMove(id, -1);
            case DiscardCardsPending discard:
                return new DiscardCardsMove(id, [.. Subset(player.Hand, discard.Count, atLeast: Math.Min(discard.Count, player.Hand.Count))]);
            case BuyCardsPending buy:
                return new BuyCardsMove(id, [.. Subset(buy.AvailableCardIds, player.Resources.MegaCredits / Constants.CardBuyCost)]);
            case DrawKeepPending draw:
                var affordable = draw.CostPerCard > 0
                    ? Math.Min(draw.KeepCount, player.Resources.MegaCredits / draw.CostPerCard)
                    : draw.KeepCount;
                return new BuyCardsMove(id, [.. draw.DrawnCardIds.Take(affordable)]);
            default:
                // Mars University, play-a-card-from-hand, and choices with nothing to choose from are all skippable.
                return new PassMove(id);
        }
    }

    /// <summary>Titanium first, then steel, then card resources, then MC, with heat covering any MC shortfall.</summary>
    private static PaymentInfo Pay(
        int cost, PlayerState player, int steelValue, int titaniumValue, bool canUseHeat,
        ImmutableDictionary<string, int> cardResourceValues)
    {
        var remaining = cost;

        var titanium = titaniumValue > 0 ? Math.Min(player.Resources.Titanium, remaining / titaniumValue) : 0;
        remaining -= titanium * titaniumValue;

        var steel = steelValue > 0 ? Math.Min(player.Resources.Steel, remaining / steelValue) : 0;
        remaining -= steel * steelValue;

        var cardResources = ImmutableDictionary.CreateBuilder<string, int>();
        foreach (var (card, value) in cardResourceValues)
        {
            var used = Math.Min(player.CardResources.GetValueOrDefault(card), remaining / value);
            if (used > 0)
            {
                cardResources[card] = used;
                remaining -= used * value;
            }
        }

        var heat = canUseHeat ? Math.Min(player.Resources.Heat, Math.Max(0, remaining - player.Resources.MegaCredits)) : 0;
        remaining -= heat;

        // Metal cannot be split, so when MC runs short the last unit of metal overpays.
        if (remaining > player.Resources.MegaCredits && titaniumValue > 0 && titanium < player.Resources.Titanium)
        {
            titanium++;
            remaining -= titaniumValue;
        }
        while (remaining > player.Resources.MegaCredits && steelValue > 0 && steel < player.Resources.Steel)
        {
            steel++;
            remaining -= steelValue;
        }

        return new PaymentInfo(Math.Max(0, remaining), steel, titanium, heat, cardResources.Count > 0 ? cardResources.ToImmutable() : null);
    }

    private T Pick<T>(IReadOnlyList<T> items) => items[_rng.Next(items.Count)];

    /// <summary>A random selection of between <paramref name="atLeast"/> and <paramref name="atMost"/> items.</summary>
    private List<T> Subset<T>(IReadOnlyList<T> items, int atMost, int atLeast = 0)
    {
        var max = Math.Min(atMost, items.Count);
        var count = max <= atLeast ? max : _rng.Next(atLeast, max + 1);
        return items.OrderBy(_ => _rng.Next()).Take(count).ToList();
    }
}

using System.Collections.Immutable;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Engine;

/// <summary>
/// Manages generation lifecycle and phase transitions.
/// All methods are pure functions: GameState in, GameState out.
/// </summary>
public static class PhaseManager
{
    /// <summary>
    /// Advance the active player to the next player who hasn't passed.
    /// Returns the updated state. If all players have passed, triggers phase transition.
    /// </summary>
    public static GameState AdvanceActivePlayer(GameState state)
    {
        // Check if all players have passed
        if (state.Players.All(p => p.Passed))
        {
            return EndActionPhase(state);
        }

        // Find next non-passed player
        var nextIndex = (state.ActivePlayerIndex + 1) % state.Players.Count;
        while (state.Players[nextIndex].Passed)
        {
            nextIndex = (nextIndex + 1) % state.Players.Count;
        }

        return state with
        {
            ActivePlayerIndex = nextIndex,
            Players = state.Players.SetItem(nextIndex,
                state.Players[nextIndex] with { ActionsThisTurn = 0 }),
        };
    }

    /// <summary>
    /// Called after a player takes an action. If they've taken 2 actions,
    /// advances to the next player.
    /// </summary>
    public static GameState AfterAction(GameState state)
    {
        var player = state.ActivePlayer;

        if (player.ActionsThisTurn >= 2)
        {
            return AdvanceActivePlayer(state);
        }

        // Player still has actions remaining this turn
        return state;
    }

    /// <summary>
    /// End the action phase: run production, then start next generation or end game.
    /// </summary>
    public static GameState EndActionPhase(GameState state)
    {
        // Run production phase
        state = RunProductionPhase(state);

        // Check for game end: all parameters maxed means game ends after this generation's production
        if (state.AllParametersMaxed)
        {
            return StartFinalGreeneryConversion(state);
        }

        // Start next generation
        return StartNewGeneration(state);
    }

    /// <summary>
    /// Run the production phase for all players simultaneously.
    /// 1) Energy → Heat
    /// 2) Add production to resources (MC = MC production + TR)
    /// 3) Reset card actions
    /// </summary>
    public static GameState RunProductionPhase(GameState state)
    {
        var updatedPlayers = state.Players;

        for (int i = 0; i < updatedPlayers.Count; i++)
        {
            var p = updatedPlayers[i];

            // Step 1: Energy → Heat
            var heat = p.Resources.Heat + p.Resources.Energy;
            var resources = p.Resources with { Energy = 0, Heat = heat };

            // Step 2: Add production to resources
            // MC income = MC production + TR (can't go below 0 total after adding)
            var mcIncome = p.Production.MegaCredits + p.TerraformRating;
            resources = resources with
            {
                MegaCredits = resources.MegaCredits + mcIncome,
                Steel = resources.Steel + p.Production.Steel,
                Titanium = resources.Titanium + p.Production.Titanium,
                Plants = resources.Plants + p.Production.Plants,
                Energy = resources.Energy + p.Production.Energy,
                Heat = resources.Heat + p.Production.Heat,
            };

            // Step 3: Reset card actions, passed state, and TR tracking
            updatedPlayers = updatedPlayers.SetItem(i, p with
            {
                Resources = resources,
                UsedCardActions = [],
                Passed = false,
                ActionsThisTurn = 0,
                IncreasedTRThisGeneration = false,
            });
        }

        return state with
        {
            Players = updatedPlayers,
            Phase = GamePhase.Production,
        };
    }

    /// <summary>
    /// Start a new generation: advance first player, increment generation,
    /// deal research cards, enter research phase.
    /// </summary>
    public static GameState StartNewGeneration(GameState state)
    {
        var nextFirstPlayer = (state.FirstPlayerIndex + 1) % state.Players.Count;
        var playerCount = state.Players.Count;

        state = state with
        {
            Generation = state.Generation + 1,
            FirstPlayerIndex = nextFirstPlayer,
            ActivePlayerIndex = nextFirstPlayer,
            Phase = GamePhase.Research,
        };

        // Deal 4 cards to each player
        var deck = state.DrawPile;
        var availableCards = ImmutableList.CreateBuilder<ImmutableList<string>>();
        var submitted = ImmutableList.CreateBuilder<bool>();

        for (int i = 0; i < playerCount; i++)
        {
            var (dealt, remaining) = DeckBuilder.Deal(deck, Constants.ResearchCardsDealt);
            availableCards.Add(dealt);
            submitted.Add(false);
            deck = remaining;
        }

        state = state with { DrawPile = deck };

        if (state.DraftVariant)
        {
            // Set up draft: dealt cards become initial draft hands
            var draftHands = ImmutableList.CreateBuilder<ImmutableList<string>>();
            var draftedCards = ImmutableList.CreateBuilder<ImmutableList<string>>();
            for (int i = 0; i < playerCount; i++)
            {
                draftHands.Add(availableCards[i]);
                draftedCards.Add([]);
            }

            // Pass direction: clockwise for even gens, counter-clockwise for odd
            bool passLeft = state.Generation % 2 == 0;

            state = state with
            {
                Draft = new DraftState
                {
                    DraftHands = draftHands.ToImmutable(),
                    DraftedCards = draftedCards.ToImmutable(),
                    DraftRound = 0,
                    PassLeft = passLeft,
                },
            };
        }
        else
        {
            // No draft — cards are directly available for purchase
            state = state with
            {
                Research = new ResearchState
                {
                    AvailableCards = availableCards.ToImmutable(),
                    Submitted = submitted.ToImmutable(),
                },
            };
        }

        return state;
    }

    /// <summary>
    /// After the final production phase, players get one chance to convert plants to greeneries.
    /// </summary>
    public static GameState StartFinalGreeneryConversion(GameState state)
    {
        return state with
        {
            Phase = GamePhase.FinalGreeneryConversion,
            ActivePlayerIndex = state.FirstPlayerIndex,
            // Reset passed state so players can choose to convert or pass
            Players = ResetPassedState(state.Players),
        };
    }

    /// <summary>
    /// Advance in the final greenery conversion phase. If all players pass, end the game.
    /// </summary>
    public static GameState AdvanceFinalGreeneryConversion(GameState state)
    {
        if (state.Players.All(p => p.Passed))
        {
            return state with { Phase = GamePhase.GameEnd };
        }

        var nextIndex = (state.ActivePlayerIndex + 1) % state.Players.Count;
        while (state.Players[nextIndex].Passed)
        {
            nextIndex = (nextIndex + 1) % state.Players.Count;
        }

        return state with { ActivePlayerIndex = nextIndex };
    }

    /// <summary>
    /// Start the action phase (first generation or after research).
    /// </summary>
    public static GameState StartActionPhase(GameState state)
    {
        return state with
        {
            Phase = GamePhase.Action,
            ActivePlayerIndex = state.FirstPlayerIndex,
            Players = ResetPassedState(state.Players),
        };
    }

    private static ImmutableList<PlayerState> ResetPassedState(ImmutableList<PlayerState> players)
    {
        var result = players;
        for (int i = 0; i < result.Count; i++)
        {
            result = result.SetItem(i, result[i] with { Passed = false, ActionsThisTurn = 0 });
        }
        return result;
    }
}

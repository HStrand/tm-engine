using System.Collections.Immutable;
using TmEngine.Domain.Models;
using TmEngine.Domain.Moves;

namespace TmEngine.Domain.Engine;

/// <summary>
/// Options for creating a new game.
/// </summary>
public sealed record GameSetupOptions(
    int PlayerCount,
    MapName Map,
    bool CorporateEra,
    bool DraftVariant,
    bool PreludeExpansion);

/// <summary>
/// The core game engine. All methods are pure functions: state + input → new state.
/// </summary>
public static class GameEngine
{
    /// <summary>
    /// Apply a move to the game state, producing a new state and a result.
    /// This is the central function of the engine.
    /// </summary>
    public static (GameState NewState, MoveResult Result) Apply(GameState state, Move move)
    {
        // Validate
        var error = MoveValidator.Validate(state, move);
        if (error != null)
            return (state, new Error(error));

        // Apply the move
        var newState = ApplyMove(state, move);

        // Increment move number and log
        newState = newState with { MoveNumber = newState.MoveNumber + 1 };
        newState = newState.AppendLog($"[{newState.MoveNumber}] {FormatMove(move)}");

        return (newState, new Success());
    }

    /// <summary>
    /// Set up a new game with the given options and random seed.
    /// </summary>
    public static GameState Setup(GameSetupOptions options, int seed)
    {
        var players = ImmutableList.CreateBuilder<PlayerState>();
        var startingTR = options.CorporateEra ? Constants.StartingTR : Constants.StartingTR;

        for (int i = 0; i < options.PlayerCount; i++)
        {
            var player = PlayerState.CreateInitial(i, startingTR);

            // Standard game (non-Corporate Era): start with 1 production of each
            if (!options.CorporateEra)
            {
                player = player with
                {
                    Production = new ProductionSet(
                        MegaCredits: Constants.StandardGameStartingProduction,
                        Steel: Constants.StandardGameStartingProduction,
                        Titanium: Constants.StandardGameStartingProduction,
                        Plants: Constants.StandardGameStartingProduction,
                        Energy: Constants.StandardGameStartingProduction,
                        Heat: Constants.StandardGameStartingProduction),
                };
            }

            players.Add(player);
        }

        // TODO: Deck building, shuffling, dealing corporations/preludes/cards (Phase 5)
        // For now, start directly in Action phase for testing
        return new GameState
        {
            GameId = Guid.NewGuid().ToString("N"),
            Map = options.Map,
            CorporateEra = options.CorporateEra,
            DraftVariant = options.DraftVariant,
            PreludeExpansion = options.PreludeExpansion,
            Phase = GamePhase.Action,
            Generation = 1,
            ActivePlayerIndex = 0,
            FirstPlayerIndex = 0,
            Oxygen = Constants.MinOxygen,
            Temperature = Constants.MinTemperature,
            OceansPlaced = 0,
            Players = players.ToImmutable(),
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty,
            ClaimedMilestones = [],
            FundedAwards = [],
            DrawPile = [],
            DiscardPile = [],
            MoveNumber = 0,
            Log = [],
        };
    }

    // ── Move Application ───────────────────────────────────────

    private static GameState ApplyMove(GameState state, Move move) => move switch
    {
        PassMove m => ApplyPass(state, m),
        ConvertHeatMove m => ApplyConvertHeat(state, m),
        ConvertPlantsMove m => ApplyConvertPlants(state, m),
        UseStandardProjectMove m => ApplyStandardProject(state, m),
        ClaimMilestoneMove m => ApplyClaimMilestone(state, m),
        FundAwardMove m => ApplyFundAward(state, m),
        BuyCardsMove m => ApplyBuyCards(state, m),
        PlaceTileMove m => ApplyPlaceTile(state, m),

        // Stubs for moves that need card system (Phase 4+)
        PlayCardMove => state,
        UseCardActionMove => state,
        DraftCardMove => state,
        SetupMove => state, // TODO: Phase 5 — apply corporation, preludes, initial card buy
        ChooseTargetPlayerMove => state,
        SelectCardMove => state,
        ChooseOptionMove => state,
        DiscardCardsMove => state,

        _ => throw new InvalidOperationException($"Unhandled move type: {move.GetType().Name}"),
    };

    private static GameState ApplyPass(GameState state, PassMove move)
    {
        state = state.UpdatePlayer(move.PlayerId, p => p with { Passed = true });

        if (state.Phase == GamePhase.FinalGreeneryConversion)
            return PhaseManager.AdvanceFinalGreeneryConversion(state);

        return PhaseManager.AdvanceActivePlayer(state);
    }

    private static GameState ApplyConvertHeat(GameState state, ConvertHeatMove move)
    {
        // Spend 8 heat
        state = state.UpdatePlayer(move.PlayerId, p => p with
        {
            Resources = p.Resources.Add(ResourceType.Heat, -Constants.HeatPerTemperature),
            ActionsThisTurn = p.ActionsThisTurn + 1,
        });

        // Raise temperature (includes TR increase and bonus effects)
        state = GlobalParameters.RaiseTemperature(state, move.PlayerId);

        return PhaseManager.AfterAction(state);
    }

    private static GameState ApplyConvertPlants(GameState state, ConvertPlantsMove move)
    {
        // Spend 8 plants
        state = state.UpdatePlayer(move.PlayerId, p => p with
        {
            Resources = p.Resources.Add(ResourceType.Plants, -Constants.PlantsPerGreenery),
            ActionsThisTurn = state.Phase == GamePhase.Action ? p.ActionsThisTurn + 1 : p.ActionsThisTurn,
        });

        // Place greenery and raise oxygen
        state = GlobalParameters.PlaceGreenery(state, move.PlayerId, move.Location);

        if (state.Phase == GamePhase.FinalGreeneryConversion)
            return PhaseManager.AdvanceFinalGreeneryConversion(state);

        return PhaseManager.AfterAction(state);
    }

    private static GameState ApplyStandardProject(GameState state, UseStandardProjectMove move)
    {
        state = move.Project switch
        {
            StandardProject.SellPatents => ApplySellPatents(state, move),
            StandardProject.PowerPlant => ApplyPowerPlant(state, move),
            StandardProject.Asteroid => ApplyAsteroidProject(state, move),
            StandardProject.Aquifer => ApplyAquiferProject(state, move),
            StandardProject.Greenery => ApplyGreeneryProject(state, move),
            StandardProject.City => ApplyCityProject(state, move),
            _ => state,
        };

        return PhaseManager.AfterAction(state);
    }

    private static GameState ApplySellPatents(GameState state, UseStandardProjectMove move)
    {
        var gain = move.CardsToDiscard.Length;
        return state.UpdatePlayer(move.PlayerId, p => p with
        {
            Hand = p.Hand.RemoveRange(move.CardsToDiscard),
            Resources = p.Resources.Add(ResourceType.MegaCredits, gain),
            ActionsThisTurn = p.ActionsThisTurn + 1,
        });
        // TODO: Add discarded cards to discard pile
    }

    private static GameState ApplyPowerPlant(GameState state, UseStandardProjectMove move)
    {
        return state.UpdatePlayer(move.PlayerId, p => p with
        {
            Resources = p.Resources.Add(ResourceType.MegaCredits, -Constants.PowerPlantCost),
            Production = p.Production.Add(ResourceType.Energy, 1),
            ActionsThisTurn = p.ActionsThisTurn + 1,
        });
    }

    private static GameState ApplyAsteroidProject(GameState state, UseStandardProjectMove move)
    {
        state = state.UpdatePlayer(move.PlayerId, p => p with
        {
            Resources = p.Resources.Add(ResourceType.MegaCredits, -Constants.AsteroidCost),
            ActionsThisTurn = p.ActionsThisTurn + 1,
        });

        return GlobalParameters.RaiseTemperature(state, move.PlayerId);
    }

    private static GameState ApplyAquiferProject(GameState state, UseStandardProjectMove move)
    {
        state = state.UpdatePlayer(move.PlayerId, p => p with
        {
            Resources = p.Resources.Add(ResourceType.MegaCredits, -Constants.AquiferCost),
            ActionsThisTurn = p.ActionsThisTurn + 1,
        });

        return GlobalParameters.PlaceOcean(state, move.PlayerId, move.Location!.Value);
    }

    private static GameState ApplyGreeneryProject(GameState state, UseStandardProjectMove move)
    {
        state = state.UpdatePlayer(move.PlayerId, p => p with
        {
            Resources = p.Resources.Add(ResourceType.MegaCredits, -Constants.GreeneryCost),
            ActionsThisTurn = p.ActionsThisTurn + 1,
        });

        return GlobalParameters.PlaceGreenery(state, move.PlayerId, move.Location!.Value);
    }

    private static GameState ApplyCityProject(GameState state, UseStandardProjectMove move)
    {
        state = state.UpdatePlayer(move.PlayerId, p => p with
        {
            Resources = p.Resources.Add(ResourceType.MegaCredits, -Constants.CityCost),
            Production = p.Production.Add(ResourceType.MegaCredits, 1),
            ActionsThisTurn = p.ActionsThisTurn + 1,
        });

        return GlobalParameters.PlaceCity(state, move.PlayerId, move.Location!.Value);
    }

    private static GameState ApplyClaimMilestone(GameState state, ClaimMilestoneMove move)
    {
        state = state.UpdatePlayer(move.PlayerId, p => p with
        {
            Resources = p.Resources.Add(ResourceType.MegaCredits, -Constants.MilestoneCost),
            ActionsThisTurn = p.ActionsThisTurn + 1,
        });

        return state with
        {
            ClaimedMilestones = state.ClaimedMilestones.Add(
                new MilestoneClaim(move.MilestoneName, move.PlayerId)),
        };
    }

    private static GameState ApplyFundAward(GameState state, FundAwardMove move)
    {
        var cost = state.FundedAwards.Count switch
        {
            0 => Constants.AwardFundCost1,
            1 => Constants.AwardFundCost2,
            2 => Constants.AwardFundCost3,
            _ => 0,
        };

        state = state.UpdatePlayer(move.PlayerId, p => p with
        {
            Resources = p.Resources.Add(ResourceType.MegaCredits, -cost),
            ActionsThisTurn = p.ActionsThisTurn + 1,
        });

        return state with
        {
            FundedAwards = state.FundedAwards.Add(
                new AwardFunding(move.AwardName, move.PlayerId)),
        };
    }

    private static GameState ApplyBuyCards(GameState state, BuyCardsMove move)
    {
        var cost = move.CardIds.Length * Constants.CardBuyCost;

        state = state.UpdatePlayer(move.PlayerId, p => p with
        {
            Resources = p.Resources.Add(ResourceType.MegaCredits, -cost),
            Hand = p.Hand.AddRange(move.CardIds),
        });

        // TODO: Check if all players have bought, then advance phase
        return state;
    }

    private static GameState ApplyPlaceTile(GameState state, PlaceTileMove move)
    {
        if (state.PendingAction is not PlaceTilePending pending)
            return state;

        state = GlobalParameters.PlaceTileOnBoard(state, pending.TileType, move.PlayerId, move.Location);
        return state with { PendingAction = null };
    }

    // ── Formatting ─────────────────────────────────────────────

    private static string FormatMove(Move move) => move switch
    {
        PassMove m => $"Player {m.PlayerId} passes",
        ConvertHeatMove m => $"Player {m.PlayerId} converts heat to temperature",
        ConvertPlantsMove m => $"Player {m.PlayerId} converts plants to greenery at {m.Location}",
        UseStandardProjectMove m => $"Player {m.PlayerId} uses {m.Project}",
        ClaimMilestoneMove m => $"Player {m.PlayerId} claims {m.MilestoneName}",
        FundAwardMove m => $"Player {m.PlayerId} funds {m.AwardName}",
        PlayCardMove m => $"Player {m.PlayerId} plays card {m.CardId}",
        UseCardActionMove m => $"Player {m.PlayerId} uses action on {m.CardId}",
        BuyCardsMove m => $"Player {m.PlayerId} buys {m.CardIds.Length} cards",
        PlaceTileMove m => $"Player {m.PlayerId} places tile at {m.Location}",
        _ => $"Player {move.PlayerId} does {move.GetType().Name}",
    };
}

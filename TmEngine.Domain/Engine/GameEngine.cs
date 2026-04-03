using System.Collections.Immutable;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Cards.Effects;
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
        var hadPendingAction = state.PendingAction != null;
        var newState = ApplyMove(state, move);

        // If a pending action was just resolved during PreludePlacement, advance to next prelude
        if (hadPendingAction && newState.PendingAction == null
            && newState.Phase == GamePhase.PreludePlacement)
        {
            newState = AdvancePreludePlacement(newState);
        }

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
        if (options.PlayerCount < Constants.MinPlayers || options.PlayerCount > Constants.MaxPlayers)
            throw new ArgumentOutOfRangeException(nameof(options),
                $"Player count must be between {Constants.MinPlayers} and {Constants.MaxPlayers}.");

        var players = ImmutableList.CreateBuilder<PlayerState>();
        var startingTR = Constants.StartingTR;

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

        var rng = new Random(seed);
        var expansions = DeckBuilder.GetEnabledExpansions(options);

        // Build and shuffle decks
        var projectDeck = DeckBuilder.BuildProjectDeck(expansions, rng);
        var allCorps = DeckBuilder.GetCorporationIds(expansions);
        var allPreludes = options.PreludeExpansion ? DeckBuilder.GetPreludeIds() : [];

        // Deal to each player
        var corpsPerPlayer = Constants.CorporationsDealt;

        var dealtCorps = ImmutableList.CreateBuilder<ImmutableList<string>>();
        var dealtPreludes = ImmutableList.CreateBuilder<ImmutableList<string>>();
        var dealtCards = ImmutableList.CreateBuilder<ImmutableList<string>>();
        var submittedMoves = ImmutableList.CreateBuilder<SetupMove?>();
        var deck = projectDeck;

        // Shuffle corporations and preludes for dealing
        var shuffledCorps = DeckBuilder.Shuffle(allCorps, rng);
        var shuffledPreludes = options.PreludeExpansion
            ? DeckBuilder.Shuffle(allPreludes, rng)
            : ImmutableList<string>.Empty;

        for (int i = 0; i < options.PlayerCount; i++)
        {
            // Deal corporations
            var (playerCorps, remainingCorps) = DeckBuilder.Deal(shuffledCorps, corpsPerPlayer);
            dealtCorps.Add(playerCorps);
            shuffledCorps = remainingCorps;

            // Deal preludes
            if (options.PreludeExpansion)
            {
                var (playerPreludes, remainingPreludes) = DeckBuilder.Deal(shuffledPreludes, Constants.PreludesDealt);
                dealtPreludes.Add(playerPreludes);
                shuffledPreludes = remainingPreludes;
            }
            else
            {
                dealtPreludes.Add([]);
            }

            // Deal project cards
            var (playerCards, remainingDeck) = DeckBuilder.Deal(deck, Constants.InitialCardsDealt);
            dealtCards.Add(playerCards);
            deck = remainingDeck;

            submittedMoves.Add(null);
        }

        var hasCards = CardRegistry.All.Count > 0;

        return new GameState
        {
            GameId = Guid.NewGuid().ToString("N"),
            Map = options.Map,
            CorporateEra = options.CorporateEra,
            DraftVariant = options.DraftVariant,
            PreludeExpansion = options.PreludeExpansion,
            // If no cards are registered yet, skip setup and go to Action for testing
            Phase = hasCards ? GamePhase.Setup : GamePhase.Action,
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
            DrawPile = deck,
            DiscardPile = [],
            PreludeDeck = shuffledPreludes,
            Setup = hasCards ? new SetupState
            {
                DealtCorporations = dealtCorps.ToImmutable(),
                DealtPreludes = dealtPreludes.ToImmutable(),
                DealtCards = dealtCards.ToImmutable(),
                SubmittedMoves = submittedMoves.ToImmutable(),
            } : null,
            MoveNumber = 0,
            Log = [],
        };
    }

    // ── Move Application ───────────────────────────────────────

    private static GameState ApplyMove(GameState state, Move move) => move switch
    {
        PassMove m => ApplyPass(state, m),
        EndTurnMove m => ApplyEndTurn(state, m),
        ConvertHeatMove m => ApplyConvertHeat(state, m),
        ConvertPlantsMove m => ApplyConvertPlants(state, m),
        UseStandardProjectMove m => ApplyStandardProject(state, m),
        ClaimMilestoneMove m => ApplyClaimMilestone(state, m),
        FundAwardMove m => ApplyFundAward(state, m),
        PlaceTileMove m => ApplyPlaceTile(state, m),

        PlayCardMove m => ApplyPlayCard(state, m),
        UseCardActionMove m => ApplyUseCardAction(state, m),
        ChooseTargetPlayerMove m => ApplyChooseTargetPlayer(state, m),
        SelectCardMove m => ApplySelectCard(state, m),
        ChooseOptionMove m => ApplyChooseOption(state, m),
        PerformFirstActionMove m => ApplyPerformFirstAction(state, m),
        DiscardCardsMove m => ApplyDiscardCards(state, m),

        SetupMove m => ApplySetup(state, m),
        PlayPreludeMove m => ApplyPlayPrelude(state, m),
        BuyCardsMove m => ApplyBuyCards(state, m),
        DraftCardMove m => ApplyDraftCard(state, m),

        _ => throw new InvalidOperationException($"Unhandled move type: {move.GetType().Name}"),
    };

    private static GameState ApplyPass(GameState state, PassMove move)
    {
        state = state.UpdatePlayer(move.PlayerId, p => p with { Passed = true });

        if (state.Phase == GamePhase.FinalGreeneryConversion)
            return PhaseManager.AdvanceFinalGreeneryConversion(state);

        return PhaseManager.AdvanceActivePlayer(state);
    }

    private static GameState ApplyEndTurn(GameState state, EndTurnMove move)
    {
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
        var player = state.GetPlayer(move.PlayerId);
        var cost = RequirementChecker.GetPowerPlantCost(player);

        return state.UpdatePlayer(move.PlayerId, p => p with
        {
            Resources = p.Resources.Add(ResourceType.MegaCredits, -cost),
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

        // Rebate based on printed SP cost (Credicor)
        state = ApplyStandardProjectRebate(state, move.PlayerId, Constants.GreeneryCost);

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

        // Rebate based on printed SP cost (Credicor)
        state = ApplyStandardProjectRebate(state, move.PlayerId, Constants.CityCost);

        return GlobalParameters.PlaceCity(state, move.PlayerId, move.Location!.Value);
    }

    private static GameState ApplyStandardProjectRebate(GameState state, int playerId, int printedCost)
    {
        var player = state.GetPlayer(playerId);
        var rebate = RequirementChecker.GetHighCostRebate(player, printedCost);
        if (rebate > 0)
        {
            state = state.UpdatePlayer(playerId, p => p with
            {
                Resources = p.Resources.Add(ResourceType.MegaCredits, rebate),
            });
        }
        return state;
    }

    private static GameState ApplyClaimMilestone(GameState state, ClaimMilestoneMove move)
    {
        state = state.UpdatePlayer(move.PlayerId, p => p with
        {
            Resources = p.Resources.Add(ResourceType.MegaCredits, -Constants.MilestoneCost),
            ActionsThisTurn = p.ActionsThisTurn + 1,
        });

        state = state with
        {
            ClaimedMilestones = state.ClaimedMilestones.Add(
                new MilestoneClaim(move.MilestoneName, move.PlayerId)),
        };

        return PhaseManager.AfterAction(state);
    }

    private static GameState ApplyFundAward(GameState state, FundAwardMove move)
    {
        var player = state.GetPlayer(move.PlayerId);

        if (player.HasFreeAwardFunding)
        {
            // Vitor: free funding, consume the ability
            state = state.UpdatePlayer(move.PlayerId, p => p with
            {
                HasFreeAwardFunding = false,
                ActionsThisTurn = p.ActionsThisTurn + 1,
            });
        }
        else
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
        }

        state = state with
        {
            FundedAwards = state.FundedAwards.Add(
                new AwardFunding(move.AwardName, move.PlayerId)),
        };

        return PhaseManager.AfterAction(state);
    }

    // ── Setup ───────────────────────────────────────────────────

    private static GameState ApplySetup(GameState state, SetupMove move)
    {
        if (state.Setup == null)
            return state;

        var playerIndex = state.GetPlayerIndex(move.PlayerId);

        // Record the submitted move
        var setup = state.Setup with
        {
            SubmittedMoves = state.Setup.SubmittedMoves.SetItem(playerIndex, move),
        };
        state = state with { Setup = setup };

        // Check if all players have submitted
        if (setup.SubmittedMoves.Any(m => m == null))
            return state;

        // All players submitted — apply choices in player order
        return ApplyAllSetupMoves(state);
    }

    private static GameState ApplyAllSetupMoves(GameState state)
    {
        var setup = state.Setup!;

        for (int i = 0; i < state.Players.Count; i++)
        {
            var move = setup.SubmittedMoves[i]!;
            var playerId = state.Players[i].PlayerId;

            // 1. Set corporation and apply starting effects (gives starting MC/resources)
            state = state.UpdatePlayer(playerId, p => p with { CorporationId = move.CorporationId });
            state = state.AppendLog($"Player {playerId} selects corporation {CardName(move.CorporationId)}");
            if (CardRegistry.TryGet(move.CorporationId, out var corpEntry))
            {
                var (newState, _) = EffectExecutor.ExecuteAll(state, playerId, corpEntry.OnPlayEffects);
                state = newState;
            }

            // 2. Buy initial project cards (3 MC each — deducted from starting MC)
            var cardCost = move.CardIdsToBuy.Length * Constants.CardBuyCost;
            state = state.UpdatePlayer(playerId, p => p with
            {
                Resources = p.Resources.Add(ResourceType.MegaCredits, -cardCost),
                Hand = p.Hand.AddRange(move.CardIdsToBuy),
            });
            if (move.CardIdsToBuy.Length > 0)
            {
                var cardNames = string.Join(", ", move.CardIdsToBuy.Select(id => $"{CardName(id)} ({id})"));
                state = state.AppendLog($"Player {playerId} buys {move.CardIdsToBuy.Length} cards: {cardNames}");
            }

            // Discard unselected project cards
            var dealtCards = setup.DealtCards[i];
            var unboughtCards = dealtCards.Where(c => !move.CardIdsToBuy.Contains(c));
            state = state with { DiscardPile = state.DiscardPile.AddRange(unboughtCards) };
        }

        // If prelude expansion is active, transition to PreludePlacement phase
        if (state.PreludeExpansion)
        {
            var remainingPreludes = ImmutableList.CreateBuilder<ImmutableList<string>>();
            for (int i = 0; i < state.Players.Count; i++)
            {
                var move = setup.SubmittedMoves[i]!;
                var preludeList = move.PreludeIds.ToImmutableList();
                remainingPreludes.Add(preludeList);

                if (preludeList.Count > 0)
                {
                    var preludeNames = string.Join(", ", move.PreludeIds.Select(id => $"{CardName(id)} ({id})"));
                    state = state.AppendLog($"Player {state.Players[i].PlayerId} selects preludes: {preludeNames}");
                }
            }

            return state with
            {
                Setup = null,
                Phase = GamePhase.PreludePlacement,
                ActivePlayerIndex = 0,
                Prelude = new PreludeState
                {
                    RemainingPreludes = remainingPreludes.ToImmutable(),
                },
            };
        }

        // No preludes — transition directly to Action phase
        return TransitionToActionPhase(state with { Setup = null });
    }

    /// <summary>
    /// Transition from setup/preludes to the Action phase, marking first actions.
    /// </summary>
    private static GameState TransitionToActionPhase(GameState state)
    {
        state = PhaseManager.StartActionPhase(state);

        // Mark players whose corporations have first actions
        for (int i = 0; i < state.Players.Count; i++)
        {
            var corpId = state.Players[i].CorporationId;
            if (!string.IsNullOrEmpty(corpId)
                && CardRegistry.TryGet(corpId, out var corp)
                && !corp.FirstActionEffects.IsEmpty)
            {
                state = state.UpdatePlayer(state.Players[i].PlayerId, p => p with
                {
                    PerformedFirstAction = false,
                });
            }
        }

        return state;
    }

    /// <summary>
    /// Check if a player can afford a prelude's mandatory effects.
    /// Specifically checks that MC-reducing effects won't bring resources below 0,
    /// and MC production reductions won't go below the minimum (-5).
    /// </summary>
    private static bool CanAffordPrelude(GameState state, int playerId, CardEntry prelude)
    {
        var player = state.GetPlayer(playerId);
        int mcCost = 0;
        int mcProductionCost = 0;

        foreach (var effect in prelude.OnPlayEffects)
        {
            if (effect is ChangeResourceEffect { Resource: ResourceType.MegaCredits } res && res.Amount < 0)
                mcCost += -res.Amount;
            if (effect is ChangeProductionEffect { Resource: ResourceType.MegaCredits } prod && prod.Amount < 0)
                mcProductionCost += -prod.Amount;
        }

        if (mcCost > 0 && player.Resources.MegaCredits < mcCost)
            return false;

        if (mcProductionCost > 0 && player.Production.MegaCredits - mcProductionCost < Constants.MinMCProduction)
            return false;

        return true;
    }

    // ── Prelude Placement Phase ──────────────────────────────────

    private static GameState ApplyPlayPrelude(GameState state, PlayPreludeMove move)
    {
        if (state.Prelude == null)
            return state;

        var playerIndex = state.GetPlayerIndex(move.PlayerId);
        var playerId = move.PlayerId;

        if (!CardRegistry.TryGet(move.PreludeId, out var preludeEntry))
            return state;

        // Play the prelude
        if (CanAffordPrelude(state, playerId, preludeEntry))
        {
            state = state.UpdatePlayer(playerId, p => p with
            {
                PlayedCards = p.PlayedCards.Add(move.PreludeId),
            });
            state = state.AppendLog($"Player {playerId} plays prelude {CardName(move.PreludeId)}");
            var (newState, pending) = EffectExecutor.ExecuteAll(state, playerId, preludeEntry.OnPlayEffects);
            state = newState;

            if (pending != null)
            {
                // Remove this prelude from remaining, then set pending action
                state = RemovePreludeFromRemaining(state, playerIndex, move.PreludeId);
                return state with { PendingAction = pending };
            }
        }
        else
        {
            // Can't afford prelude — receive compensation instead
            state = state.UpdatePlayer(playerId, p => p with
            {
                Resources = p.Resources.Add(ResourceType.MegaCredits, Constants.PreludeCompensation),
            });
            state = state.AppendLog(
                $"Player {playerId} cannot afford prelude {preludeEntry.Definition.Name}, receives {Constants.PreludeCompensation} MC");
        }

        // Remove this prelude from remaining and advance
        state = RemovePreludeFromRemaining(state, playerIndex, move.PreludeId);
        return AdvancePreludePlacement(state);
    }

    private static GameState RemovePreludeFromRemaining(GameState state, int playerIndex, string preludeId)
    {
        var remaining = state.Prelude!.RemainingPreludes;
        var playerPreludes = remaining[playerIndex].Remove(preludeId);
        remaining = remaining.SetItem(playerIndex, playerPreludes);
        return state with { Prelude = state.Prelude with { RemainingPreludes = remaining } };
    }

    /// <summary>
    /// After a prelude is played (and any sub-actions resolved), advance to the next prelude or next player.
    /// Called after ApplyPlayPrelude and after pending actions from preludes are resolved.
    /// </summary>
    private static GameState AdvancePreludePlacement(GameState state)
    {
        if (state.Prelude == null)
            return TransitionToActionPhase(state);

        // Current player still has preludes? Stay on them.
        var currentPlayerPreludes = state.Prelude.RemainingPreludes[state.ActivePlayerIndex];
        if (currentPlayerPreludes.Count > 0)
            return state;

        // Find next player with preludes
        for (int offset = 1; offset < state.Players.Count; offset++)
        {
            var nextIndex = (state.ActivePlayerIndex + offset) % state.Players.Count;
            if (state.Prelude.RemainingPreludes[nextIndex].Count > 0)
                return state with { ActivePlayerIndex = nextIndex };
        }

        // All preludes played — transition to Action phase
        return TransitionToActionPhase(state with { Prelude = null });
    }

    // ── Research Phase ─────────────────────────────────────────

    private static GameState ApplyBuyCards(GameState state, BuyCardsMove move)
    {
        var cost = move.CardIds.Length * Constants.CardBuyCost;
        var playerIndex = state.GetPlayerIndex(move.PlayerId);

        state = state.UpdatePlayer(move.PlayerId, p => p with
        {
            Resources = p.Resources.Add(ResourceType.MegaCredits, -cost),
            Hand = p.Hand.AddRange(move.CardIds),
        });

        // Discard unselected cards
        if (state.Research != null)
        {
            var available = state.Research.AvailableCards[playerIndex];
            var unbought = available.Where(c => !move.CardIds.Contains(c));
            state = state with { DiscardPile = state.DiscardPile.AddRange(unbought) };

            // Mark player as submitted
            var research = state.Research with
            {
                Submitted = state.Research.Submitted.SetItem(playerIndex, true),
            };
            state = state with { Research = research };

            // If all players have bought, advance to Action phase
            if (research.Submitted.All(s => s))
            {
                state = state with { Research = null };
                return PhaseManager.StartActionPhase(state);
            }
        }

        return state;
    }

    private static GameState ApplyDraftCard(GameState state, DraftCardMove move)
    {
        if (state.Draft == null)
            return state;

        var playerIndex = state.GetPlayerIndex(move.PlayerId);
        var draft = state.Draft;

        // Add picked card to player's drafted cards
        var draftedCards = draft.DraftedCards.SetItem(playerIndex,
            draft.DraftedCards[playerIndex].Add(move.CardId));

        // Remove picked card from draft hand
        var draftHands = draft.DraftHands.SetItem(playerIndex,
            draft.DraftHands[playerIndex].Remove(move.CardId));

        draft = draft with { DraftedCards = draftedCards, DraftHands = draftHands };

        // Check if all players have picked this round
        var allPicked = true;
        for (int i = 0; i < state.Players.Count; i++)
        {
            // A player has picked if their drafted cards count > draft round
            if (draft.DraftedCards[i].Count <= draft.DraftRound)
            {
                allPicked = false;
                break;
            }
        }

        if (!allPicked)
        {
            return state with { Draft = draft };
        }

        // All picked — pass remaining hands
        var playerCount = state.Players.Count;
        var newHands = ImmutableList.CreateBuilder<ImmutableList<string>>();
        for (int i = 0; i < playerCount; i++)
        {
            // Pass direction alternates: left for even gens, right for odd
            int sourceIndex = draft.PassLeft
                ? (i - 1 + playerCount) % playerCount
                : (i + 1) % playerCount;
            newHands.Add(draft.DraftHands[sourceIndex]);
        }

        draft = draft with
        {
            DraftHands = newHands.ToImmutable(),
            DraftRound = draft.DraftRound + 1,
        };

        // If all 4 rounds complete, transition to buy phase
        if (draft.DraftRound >= Constants.ResearchCardsDealt)
        {
            // Set up research state with drafted cards available for purchase
            var availableCards = ImmutableList.CreateBuilder<ImmutableList<string>>();
            var submitted = ImmutableList.CreateBuilder<bool>();
            for (int i = 0; i < playerCount; i++)
            {
                availableCards.Add(draft.DraftedCards[i]);
                submitted.Add(false);
            }

            return state with
            {
                Draft = null,
                Research = new ResearchState
                {
                    AvailableCards = availableCards.ToImmutable(),
                    Submitted = submitted.ToImmutable(),
                },
            };
        }

        return state with { Draft = draft };
    }

    private static GameState ApplyPlaceTile(GameState state, PlaceTileMove move)
    {
        if (state.PendingAction is PlaceTilePending pending)
        {
            // Use the appropriate placement method based on tile type
            // so that greeneries raise oxygen and oceans raise TR
            state = pending.TileType switch
            {
                TileType.Greenery => GlobalParameters.PlaceGreenery(state, move.PlayerId, move.Location),
                TileType.Ocean => GlobalParameters.PlaceOcean(state, move.PlayerId, move.Location),
                TileType.City or TileType.Capital => GlobalParameters.PlaceCity(state, move.PlayerId, move.Location),
                _ => GlobalParameters.PlaceTileOnBoard(state, pending.TileType, move.PlayerId, move.Location),
            };
            return state with { PendingAction = null };
        }

        if (state.PendingAction is ClaimLandPending)
        {
            state = state with
            {
                ClaimedHexes = state.ClaimedHexes.Add(move.Location, move.PlayerId),
                PendingAction = null,
            };
            return state;
        }

        return state;
    }

    // ── Card Playing ─────────────────────────────────────────────

    private static GameState ApplyPlayCard(GameState state, PlayCardMove move)
    {
        if (!CardRegistry.TryGet(move.CardId, out var entry))
            return state;

        var card = entry.Definition;
        var player = state.GetPlayer(move.PlayerId);

        // Check if this is resolving a PlayCardFromHandPending (prelude effect)
        var isFromPending = state.PendingAction is PlayCardFromHandPending;
        var pendingDiscount = isFromPending ? ((PlayCardFromHandPending)state.PendingAction!).CostDiscount : 0;

        // Calculate effective cost with discounts
        var discount = RequirementChecker.GetCardDiscount(player, card.Tags) + pendingDiscount;
        var effectiveCost = Math.Max(0, card.Cost - discount);

        // Deduct resources
        state = state.UpdatePlayer(move.PlayerId, p => p with
        {
            Resources = new ResourceSet(
                MegaCredits: p.Resources.MegaCredits - move.Payment.MegaCredits,
                Steel: p.Resources.Steel - move.Payment.Steel,
                Titanium: p.Resources.Titanium - move.Payment.Titanium,
                Plants: p.Resources.Plants,
                Energy: p.Resources.Energy,
                Heat: p.Resources.Heat - move.Payment.Heat),
            Hand = p.Hand.Remove(move.CardId),
            // Only count as an action if this is a normal action phase play
            ActionsThisTurn = isFromPending ? p.ActionsThisTurn : p.ActionsThisTurn + 1,
        });

        // Clear the pending action if resolving one
        if (isFromPending)
            state = state with { PendingAction = null };

        // Place card in appropriate pile
        state = card.Type switch
        {
            CardType.Event => state.UpdatePlayer(move.PlayerId, p => p with
            {
                PlayedEvents = p.PlayedEvents.Add(move.CardId),
            }),
            _ => state.UpdatePlayer(move.PlayerId, p => p with
            {
                PlayedCards = p.PlayedCards.Add(move.CardId),
            }),
        };

        // Apply rebates
        var totalRebate = RequirementChecker.GetHighCostRebate(state.GetPlayer(move.PlayerId), card.Cost)
                        + RequirementChecker.GetVPCardRebate(state.GetPlayer(move.PlayerId), card);
        if (totalRebate > 0)
        {
            state = state.UpdatePlayer(move.PlayerId, p => p with
            {
                Resources = p.Resources.Add(ResourceType.MegaCredits, totalRebate),
            });
        }

        // Execute on-play effects
        var (newState, pending) = EffectExecutor.ExecuteAll(state, move.PlayerId, entry.OnPlayEffects);
        state = newState;

        if (pending != null)
            return state with { PendingAction = pending };

        // TODO: Fire triggered effects for other players' cards (TriggerSystem)

        // If resolved from a pending action during setup, don't advance turn
        if (isFromPending)
            return state;

        return PhaseManager.AfterAction(state);
    }

    private static GameState ApplyPerformFirstAction(GameState state, PerformFirstActionMove move)
    {
        var player = state.GetPlayer(move.PlayerId);
        if (!CardRegistry.TryGet(player.CorporationId, out var corp))
            return state;

        // Mark first action as performed and count as 1 action
        state = state.UpdatePlayer(move.PlayerId, p => p with
        {
            PerformedFirstAction = true,
            ActionsThisTurn = p.ActionsThisTurn + 1,
        });

        // Execute the first action effects
        var (newState, pending) = EffectExecutor.ExecuteAll(state, move.PlayerId, corp.FirstActionEffects);
        state = newState;

        if (pending != null)
            return state with { PendingAction = pending };

        return PhaseManager.AfterAction(state);
    }

    private static GameState ApplyUseCardAction(GameState state, UseCardActionMove move)
    {
        if (!CardRegistry.TryGet(move.CardId, out var entry) || entry.Action == null)
            return state;

        var action = entry.Action;

        // Pay action cost
        if (action.Cost != null)
        {
            state = action.Cost switch
            {
                SpendMCCost c => state.UpdatePlayer(move.PlayerId, p => p with
                    { Resources = p.Resources.Add(ResourceType.MegaCredits, -c.Amount) }),
                SpendEnergyCost c => state.UpdatePlayer(move.PlayerId, p => p with
                    { Resources = p.Resources.Add(ResourceType.Energy, -c.Amount) }),
                SpendSteelCost c => state.UpdatePlayer(move.PlayerId, p => p with
                    { Resources = p.Resources.Add(ResourceType.Steel, -c.Amount) }),
                SpendTitaniumCost c => state.UpdatePlayer(move.PlayerId, p => p with
                    { Resources = p.Resources.Add(ResourceType.Titanium, -c.Amount) }),
                SpendHeatCost c => state.UpdatePlayer(move.PlayerId, p => p with
                    { Resources = p.Resources.Add(ResourceType.Heat, -c.Amount) }),
                _ => state,
            };
        }

        // Mark action as used
        state = state.UpdatePlayer(move.PlayerId, p => p with
        {
            UsedCardActions = p.UsedCardActions.Add(move.CardId),
            ActionsThisTurn = p.ActionsThisTurn + 1,
        });

        // Execute action effects
        var (newState, pending) = EffectExecutor.ExecuteAll(state, move.PlayerId, action.Effects);
        state = newState;

        if (pending != null)
            return state with { PendingAction = pending };

        return PhaseManager.AfterAction(state);
    }

    // ── Sub-Move Resolution ────────────────────────────────────

    private static GameState ApplyChooseTargetPlayer(GameState state, ChooseTargetPlayerMove move)
    {
        if (state.PendingAction is RemoveResourcePending removePending)
        {
            state = state.UpdatePlayer(move.TargetPlayerId, p => p with
            {
                Resources = p.Resources.Add(removePending.Resource,
                    -Math.Min(removePending.Amount, p.Resources.Get(removePending.Resource))),
            });
            return state with { PendingAction = null };
        }

        if (state.PendingAction is ReduceProductionPending reducePending)
        {
            state = state.UpdatePlayer(move.TargetPlayerId, p => p with
            {
                Production = p.Production.Add(reducePending.Resource, -reducePending.Amount),
            });
            return state with { PendingAction = null };
        }

        return state;
    }

    private static GameState ApplySelectCard(GameState state, SelectCardMove move)
    {
        if (state.PendingAction is AddCardResourcePending addPending)
        {
            state = state.UpdatePlayer(state.ActivePlayer.PlayerId, p =>
            {
                var current = p.CardResources.GetValueOrDefault(move.CardId, 0);
                return p with { CardResources = p.CardResources.SetItem(move.CardId, current + addPending.Amount) };
            });
            return state with { PendingAction = null };
        }

        if (state.PendingAction is ChooseCardToPlayPending choosePending)
        {
            // Play the chosen card, discard the rest
            var (newState, innerPending) = EffectExecutor.PlayCardImmediately(
                state, move.PlayerId, move.CardId);
            state = newState;

            // Discard unchosen cards
            var discarded = choosePending.CardIds.Where(id => id != move.CardId);
            state = state with { DiscardPile = state.DiscardPile.AddRange(discarded) };

            if (innerPending != null)
                return state with { PendingAction = innerPending };

            return state with { PendingAction = null };
        }

        return state;
    }

    private static GameState ApplyChooseOption(GameState state, ChooseOptionMove move)
    {
        // The ChooseOptionPending was created by a ChooseEffect.
        // We need to find the original ChooseEffect and execute the chosen option's effects.
        // For now, clear the pending action — full implementation needs effect queue tracking.
        return state with { PendingAction = null };
    }

    private static GameState ApplyDiscardCards(GameState state, DiscardCardsMove move)
    {
        state = state.UpdatePlayer(move.PlayerId, p => p with
        {
            Hand = p.Hand.RemoveRange(move.CardIds),
        });

        // Add to discard pile
        state = state with { DiscardPile = state.DiscardPile.AddRange(move.CardIds) };
        return state with { PendingAction = null };
    }

    // ── Formatting ─────────────────────────────────────────────

    private static string CardName(string cardId) =>
        CardRegistry.TryGet(cardId, out var entry) ? entry.Definition.Name : cardId;

    private static string FormatMove(Move move) => move switch
    {
        PassMove m => $"Player {m.PlayerId} passes",
        EndTurnMove m => $"Player {m.PlayerId} ends turn",
        ConvertHeatMove m => $"Player {m.PlayerId} converts heat to temperature",
        ConvertPlantsMove m => $"Player {m.PlayerId} converts plants to greenery at {m.Location}",
        UseStandardProjectMove m => $"Player {m.PlayerId} uses {m.Project}",
        ClaimMilestoneMove m => $"Player {m.PlayerId} claims {m.MilestoneName}",
        FundAwardMove m => $"Player {m.PlayerId} funds {m.AwardName}",
        PlayCardMove m => $"Player {m.PlayerId} plays {CardName(m.CardId)} ({m.CardId})",
        UseCardActionMove m => $"Player {m.PlayerId} uses action on {CardName(m.CardId)} ({m.CardId})",
        BuyCardsMove m => $"Player {m.PlayerId} buys {m.CardIds.Length} cards",
        PerformFirstActionMove m => $"Player {m.PlayerId} performs corporation first action",
        PlayPreludeMove m => $"Player {m.PlayerId} plays prelude {CardName(m.PreludeId)} ({m.PreludeId})",
        PlaceTileMove m => $"Player {m.PlayerId} places tile at {m.Location}",
        _ => $"Player {move.PlayerId} does {move.GetType().Name}",
    };
}

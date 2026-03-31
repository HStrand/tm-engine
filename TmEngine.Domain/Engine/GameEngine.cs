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

        var rng = new Random(seed);
        var expansions = DeckBuilder.GetEnabledExpansions(options);

        // Build and shuffle decks
        var projectDeck = DeckBuilder.BuildProjectDeck(expansions, rng);
        var allCorps = DeckBuilder.GetCorporationIds(expansions);
        var allPreludes = options.PreludeExpansion ? DeckBuilder.GetPreludeIds() : [];

        // Deal to each player
        var corpsPerPlayer = options.PreludeExpansion
            ? Constants.CorporationsDealtWithPrelude
            : Constants.CorporationsDealt;

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
        DiscardCardsMove m => ApplyDiscardCards(state, m),

        SetupMove m => ApplySetup(state, m),
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

            // Discard unselected project cards
            var dealtCards = setup.DealtCards[i];
            var unboughtCards = dealtCards.Where(c => !move.CardIdsToBuy.Contains(c));
            state = state with { DiscardPile = state.DiscardPile.AddRange(unboughtCards) };

            // 3. Apply prelude effects (after card buy, so MC may be depleted)
            foreach (var preludeId in move.PreludeIds)
            {
                if (!CardRegistry.TryGet(preludeId, out var preludeEntry))
                    continue;

                if (CanAffordPrelude(state, playerId, preludeEntry))
                {
                    state = state.UpdatePlayer(playerId, p => p with
                    {
                        PlayedCards = p.PlayedCards.Add(preludeId),
                    });
                    var (newState, _) = EffectExecutor.ExecuteAll(state, playerId, preludeEntry.OnPlayEffects);
                    state = newState;
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
            }
        }

        // Clear setup state and transition to Action phase (gen 1 skips Research)
        return PhaseManager.StartActionPhase(state with { Setup = null });
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
        if (state.PendingAction is not PlaceTilePending pending)
            return state;

        state = GlobalParameters.PlaceTileOnBoard(state, pending.TileType, move.PlayerId, move.Location);
        return state with { PendingAction = null };
    }

    // ── Card Playing ─────────────────────────────────────────────

    private static GameState ApplyPlayCard(GameState state, PlayCardMove move)
    {
        if (!CardRegistry.TryGet(move.CardId, out var entry))
            return state;

        var card = entry.Definition;
        var player = state.GetPlayer(move.PlayerId);

        // Calculate effective cost with discounts
        var discount = RequirementChecker.GetCardDiscount(player, card.Tags);
        var effectiveCost = Math.Max(0, card.Cost - discount);

        // Validate and apply payment
        var steelValue = RequirementChecker.GetSteelValue(player);
        var titaniumValue = RequirementChecker.GetTitaniumValue(player);
        var totalPayment = move.Payment.TotalValue(steelValue, titaniumValue);

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
            ActionsThisTurn = p.ActionsThisTurn + 1,
        });

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

        // Execute on-play effects
        var (newState, pending) = EffectExecutor.ExecuteAll(state, move.PlayerId, entry.OnPlayEffects);
        state = newState;

        if (pending != null)
            return state with { PendingAction = pending };

        // TODO: Fire triggered effects for other players' cards (TriggerSystem)

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

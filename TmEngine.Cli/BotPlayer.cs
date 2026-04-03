using Newtonsoft.Json.Linq;

namespace TmEngine.Cli;

public class BotPlayer
{
    private readonly Random _rng = new();
    private readonly Dictionary<string, string> _cardNames;
    private readonly int _playerId;

    public BotPlayer(int playerId)
    {
        _playerId = playerId;
        _cardNames = new();
    }

    public void UpdateCardNames(Dictionary<string, string> cardNames)
    {
        foreach (var kv in cardNames)
            _cardNames[kv.Key] = kv.Value;
    }

    public (JObject Move, string Description) PickMove(
        AvailableMovesDto moves, PlayerStateDto botState)
    {
        if (moves.PendingAction != null)
            return PickPendingAction(moves.PendingAction, botState);
        if (moves.Setup != null)
            return PickSetup(moves.Setup, botState);
        if (moves.Prelude != null)
            return PickPrelude(moves.Prelude);
        if (moves.Draft != null)
            return PickDraft(moves.Draft);
        if (moves.BuyCards != null)
            return PickBuyCards(moves.BuyCards, botState);
        if (moves.Actions != null)
            return PickAction(moves.Actions, botState);
        if (moves.FinalGreenery != null)
            return PickFinalGreenery(moves.FinalGreenery);

        return (MakeMove("Pass"), "passes (no options)");
    }

    private (JObject, string) PickSetup(SetupOptionsDto setup, PlayerStateDto botState)
    {
        var corp = PickRandom(setup.AvailableCorporations);
        var preludes = setup.AvailablePreludes.Count >= 2
            ? PickRandomN(setup.AvailablePreludes, 2) : new List<string>();

        // Buy a random subset of affordable cards (0-5)
        int maxCards = Math.Min(setup.AvailableCards.Count, 5);
        int cardsToBuy = _rng.Next(0, maxCards + 1);
        var cards = PickRandomN(setup.AvailableCards, cardsToBuy);

        var move = MakeMove("Setup");
        move["corporationId"] = corp;
        move["preludeIds"] = new JArray(preludes.ToArray());
        move["cardIdsToBuy"] = new JArray(cards.ToArray());

        var corpName = _cardNames.GetValueOrDefault(corp, corp);
        return (move, $"chooses {corpName}, buys {cards.Count} cards");
    }

    private (JObject, string) PickPrelude(PreludeOptionsDto prelude)
    {
        var id = PickRandom(prelude.RemainingPreludes);
        var move = MakeMove("PlayPrelude");
        move["preludeId"] = id;
        return (move, $"plays prelude {CardName(id)}");
    }

    private (JObject, string) PickDraft(DraftOptionsDto draft)
    {
        var id = PickRandom(draft.DraftHand);
        var move = MakeMove("DraftCard");
        move["cardId"] = id;
        return (move, $"drafts {CardName(id)}");
    }

    private (JObject, string) PickBuyCards(BuyCardsOptionsDto buyCards, PlayerStateDto botState)
    {
        int affordable = botState.Resources.MegaCredits / Math.Max(1, buyCards.CostPerCard);
        int maxBuy = Math.Min(affordable, buyCards.AvailableCards.Count);
        int count = _rng.Next(0, maxBuy + 1);
        var cards = PickRandomN(buyCards.AvailableCards, count);

        var move = MakeMove("BuyCards");
        move["cardIds"] = new JArray(cards.ToArray());
        return (move, $"buys {cards.Count} cards");
    }

    private (JObject, string) PickAction(ActionPhaseOptionsDto actions, PlayerStateDto botState)
    {
        // Build list of possible action generators
        var options = new List<Func<(JObject, string)>>();

        // Play a card
        foreach (var card in actions.PlayableCards)
        {
            var c = card; // capture
            options.Add(() =>
            {
                var payment = PaymentCalculator.Calculate(
                    c.EffectiveCost, c.CanUseSteel, c.CanUseTitanium, c.CanUseHeat,
                    botState.Resources.MegaCredits, botState.Resources.Steel,
                    botState.Resources.Titanium, botState.Resources.Heat);

                var move = MakeMove("PlayCard");
                move["cardId"] = c.CardId;
                move["payment"] = new JObject
                {
                    ["megaCredits"] = payment.MegaCredits,
                    ["steel"] = payment.Steel,
                    ["titanium"] = payment.Titanium,
                    ["heat"] = payment.Heat,
                };
                return (move, $"plays {CardName(c.CardId)} (cost {c.EffectiveCost})");
            });
        }

        // Standard projects
        foreach (var sp in actions.StandardProjects.Where(s => s.Available))
        {
            var s = sp;
            options.Add(() =>
            {
                var move = MakeMove("UseStandardProject");
                move["project"] = s.Project;

                if (s.Project.Equals("SellPatents", StringComparison.OrdinalIgnoreCase) && botState.Hand.Count > 0)
                {
                    var discard = PickRandomN(botState.Hand, _rng.Next(1, botState.Hand.Count + 1));
                    move["cardsToDiscard"] = new JArray(discard.ToArray());
                    return (move, $"sells {discard.Count} patents");
                }

                if (s.ValidLocations != null && s.ValidLocations.Count > 0)
                {
                    var loc = PickRandom(s.ValidLocations);
                    move["location"] = new JObject { ["col"] = loc.Col, ["row"] = loc.Row };
                }
                return (move, $"uses {s.Project} ({s.Cost} MC)");
            });
        }

        // Card actions
        foreach (var ca in actions.UsableCardActions)
        {
            var a = ca;
            options.Add(() =>
            {
                var move = MakeMove("UseCardAction");
                move["cardId"] = a.CardId;
                return (move, $"uses action on {CardName(a.CardId)}");
            });
        }

        // Milestones
        foreach (var ms in actions.ClaimableMilestones)
        {
            var m = ms;
            options.Add(() =>
            {
                var move = MakeMove("ClaimMilestone");
                move["milestoneName"] = m.Name;
                return (move, $"claims milestone {m.Name}");
            });
        }

        // Awards
        foreach (var aw in actions.FundableAwards)
        {
            var a = aw;
            options.Add(() =>
            {
                var move = MakeMove("FundAward");
                move["awardName"] = a.Name;
                return (move, $"funds award {a.Name}");
            });
        }

        // Convert heat
        if (actions.CanConvertHeat)
        {
            options.Add(() => (MakeMove("ConvertHeat"), "converts heat to temperature"));
        }

        // Convert plants
        if (actions.CanConvertPlants && actions.ValidGreeneryLocations.Count > 0)
        {
            options.Add(() =>
            {
                var loc = PickRandom(actions.ValidGreeneryLocations);
                var move = MakeMove("ConvertPlants");
                move["location"] = new JObject { ["col"] = loc.Col, ["row"] = loc.Row };
                return (move, $"converts plants to greenery at {loc}");
            });
        }

        // First action
        if (actions.CanPerformFirstAction)
        {
            options.Add(() => (MakeMove("PerformFirstAction"), "performs first action"));
        }

        // End turn / Pass as fallback
        if (options.Count == 0 || _rng.NextDouble() < 0.1)
        {
            if (actions.CanEndTurn)
                return (MakeMove("EndTurn"), "skips second action");
            return (MakeMove("Pass"), "passes");
        }

        // Pick random action
        var pick = options[_rng.Next(options.Count)];
        return pick();
    }

    private (JObject, string) PickFinalGreenery(FinalGreeneryOptionsDto fg)
    {
        if (fg.CanConvert && fg.ValidGreeneryLocations.Count > 0)
        {
            var loc = PickRandom(fg.ValidGreeneryLocations);
            var move = MakeMove("ConvertPlants");
            move["location"] = new JObject { ["col"] = loc.Col, ["row"] = loc.Row };
            return (move, $"converts plants to greenery at {loc}");
        }
        return (MakeMove("Pass"), "passes (final greenery)");
    }

    private (JObject, string) PickPendingAction(JObject pending, PlayerStateDto botState)
    {
        var type = pending["type"]?.Value<string>() ?? "";

        switch (type)
        {
            case "PlaceTile":
            case "ClaimLand":
            {
                var locs = pending["validLocations"]?.ToObject<List<HexCoordDto>>() ?? new();
                if (locs.Count > 0)
                {
                    var loc = PickRandom(locs);
                    var move = MakeMove("PlaceTile");
                    move["location"] = new JObject { ["col"] = loc.Col, ["row"] = loc.Row };
                    return (move, $"places tile at {loc}");
                }
                break;
            }
            case "RemoveResource":
            case "ReduceProduction":
            {
                var targets = pending["validTargetPlayerIds"]?.ToObject<List<int>>() ?? new();
                if (targets.Count > 0)
                {
                    var target = PickRandom(targets);
                    var move = MakeMove("ChooseTargetPlayer");
                    move["targetPlayerId"] = target;
                    return (move, $"targets player {target}");
                }
                break;
            }
            case "AddCardResource":
            {
                var cards = pending["validCardIds"]?.ToObject<List<string>>() ?? new();
                if (cards.Count > 0)
                {
                    var card = PickRandom(cards);
                    var move = MakeMove("SelectCard");
                    move["cardId"] = card;
                    return (move, $"adds resource to {CardName(card)}");
                }
                break;
            }
            case "ChooseOption":
            {
                var opts = pending["options"]?.ToObject<List<string>>() ?? new();
                if (opts.Count > 0)
                {
                    int idx = _rng.Next(opts.Count);
                    var move = MakeMove("ChooseOption");
                    move["optionIndex"] = idx;
                    return (move, $"chooses option {idx + 1}: {opts[idx]}");
                }
                break;
            }
            case "DiscardCards":
            {
                int count = pending["count"]?.Value<int>() ?? 1;
                var discard = PickRandomN(botState.Hand, Math.Min(count, botState.Hand.Count));
                var move = MakeMove("DiscardCards");
                move["cardIds"] = new JArray(discard.ToArray());
                return (move, $"discards {discard.Count} cards");
            }
            case "BuyCards":
            {
                // Buy random subset (or none)
                var available = pending["availableCardIds"]?.ToObject<List<string>>() ?? new();
                int maxBuy = Math.Min(available.Count, botState.Resources.MegaCredits / 3);
                int count = _rng.Next(0, maxBuy + 1);
                var cards = PickRandomN(available, count);
                var move = MakeMove("BuyCards");
                move["cardIds"] = new JArray(cards.ToArray());
                return (move, $"buys {cards.Count} cards");
            }
            case "ChooseCardToPlay":
            {
                var cards = pending["cardIds"]?.ToObject<List<string>>() ?? new();
                if (cards.Count > 0)
                {
                    var card = PickRandom(cards);
                    var move = MakeMove("SelectCard");
                    move["cardId"] = card;
                    return (move, $"chooses to play {CardName(card)}");
                }
                break;
            }
            case "PlayCardFromHand":
            {
                // For simplicity, pass on this (play no card)
                // A smarter bot would evaluate playable cards
                var move = MakeMove("Pass");
                return (move, "passes on playing from hand");
            }
        }

        return (MakeMove("Pass"), "passes (unhandled pending action)");
    }

    private JObject MakeMove(string type) =>
        new() { ["type"] = type, ["playerId"] = _playerId };

    private T PickRandom<T>(List<T> list) =>
        list[_rng.Next(list.Count)];

    private List<T> PickRandomN<T>(List<T> list, int count)
    {
        var shuffled = list.OrderBy(_ => _rng.Next()).ToList();
        return shuffled.Take(count).ToList();
    }

    private string CardName(string id) =>
        _cardNames.GetValueOrDefault(id, id);
}

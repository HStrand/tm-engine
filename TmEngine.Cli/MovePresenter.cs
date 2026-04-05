using Newtonsoft.Json.Linq;

namespace TmEngine.Cli;

public class MovePresenter
{
    private readonly int _playerId;
    private Dictionary<string, string> _cardNames = new();
    private Dictionary<string, CardInfoDto> _cardInfo = new();

    public MovePresenter(int playerId)
    {
        _playerId = playerId;
    }

    public void UpdateCardNames(Dictionary<string, string> cardNames)
    {
        foreach (var kv in cardNames)
            _cardNames[kv.Key] = kv.Value;
    }

    public void UpdateCardInfo(Dictionary<string, CardInfoDto> cardInfo)
    {
        foreach (var kv in cardInfo)
            _cardInfo[kv.Key] = kv.Value;
    }

    public JObject PromptMove(AvailableMovesDto moves, PlayerStateDto playerState)
    {
        if (moves.PendingAction != null)
            return PromptPendingAction(moves.PendingAction, playerState);
        if (moves.Setup != null)
            return PromptSetup(moves.Setup, playerState);
        if (moves.Prelude != null)
            return PromptPrelude(moves.Prelude);
        if (moves.Draft != null)
            return PromptDraft(moves.Draft);
        if (moves.BuyCards != null)
            return PromptBuyCards(moves.BuyCards, playerState);
        if (moves.Actions != null)
            return PromptAction(moves.Actions, playerState);
        if (moves.FinalGreenery != null)
            return PromptFinalGreenery(moves.FinalGreenery);

        Console.WriteLine("No moves available. Passing.");
        return MakeMove("Pass");
    }

    // ── Setup ──

    private JObject PromptSetup(SetupOptionsDto setup, PlayerStateDto playerState)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n╔══════════════════════════════════╗");
        Console.WriteLine("║           GAME SETUP             ║");
        Console.WriteLine("╚══════════════════════════════════╝");
        Console.ResetColor();

        // Show all available corporations
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n── CORPORATIONS ──");
        Console.ResetColor();
        for (int i = 0; i < setup.AvailableCorporations.Count; i++)
        {
            var id = setup.AvailableCorporations[i];
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"  {i + 1}. ");
            Console.ResetColor();
            PrintCardDetail(id);
        }

        // Show all available preludes
        if (setup.AvailablePreludes.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n── PRELUDES (choose 2) ──");
            Console.ResetColor();
            for (int i = 0; i < setup.AvailablePreludes.Count; i++)
            {
                var id = setup.AvailablePreludes[i];
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"  {i + 1}. ");
                Console.ResetColor();
                PrintCardDetail(id);
            }
        }

        // Show all available project cards
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n── PROJECT CARDS (3 MC each) ──");
        Console.ResetColor();
        for (int i = 0; i < setup.AvailableCards.Count; i++)
        {
            var id = setup.AvailableCards[i];
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"  {(i + 1),2}. ");
            Console.ResetColor();
            PrintCardDetail(id);
        }

        // Now prompt for choices
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n── MAKE YOUR CHOICES ──");
        Console.ResetColor();

        // Corporation
        Console.WriteLine($"\nChoose corporation (1-{setup.AvailableCorporations.Count}):");
        int corpIdx = ReadChoice(setup.AvailableCorporations.Count) - 1;
        var corpId = setup.AvailableCorporations[corpIdx];
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  -> {CardName(corpId)}");
        Console.ResetColor();

        // Preludes
        var preludeIds = new List<string>();
        if (setup.AvailablePreludes.Count > 0)
        {
            Console.Write($"\nChoose 2 preludes (comma-separated, 1-{setup.AvailablePreludes.Count}): ");
            var picks = ReadMultiChoice(setup.AvailablePreludes.Count, 2, 2);
            preludeIds = picks.Select(i => setup.AvailablePreludes[i - 1]).ToList();
            Console.ForegroundColor = ConsoleColor.Green;
            foreach (var id in preludeIds)
                Console.WriteLine($"  -> {CardName(id)}");
            Console.ResetColor();
        }

        // Cards
        Console.Write($"\nBuy cards (comma-separated, 1-{setup.AvailableCards.Count}), or Enter for none: ");
        var cardPicks = ReadMultiChoiceOptional(setup.AvailableCards.Count);
        var cardIds = cardPicks.Select(i => setup.AvailableCards[i - 1]).ToList();
        if (cardIds.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  -> Buying {cardIds.Count} cards ({cardIds.Count * 3} MC):");
            foreach (var id in cardIds)
                Console.WriteLine($"     {CardName(id)}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  -> Buying no cards");
            Console.ResetColor();
        }

        var move = MakeMove("Setup");
        move["corporationId"] = corpId;
        move["preludeIds"] = new JArray(preludeIds.ToArray());
        move["cardIdsToBuy"] = new JArray(cardIds.ToArray());
        return move;
    }

    private void PrintCardDetail(string id)
    {
        if (_cardInfo.TryGetValue(id, out var info))
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(info.Name);
            Console.ResetColor();

            if (info.Type is not "Corporation" and not "Prelude")
                Console.Write($" ({info.Cost} MC)");

            if (info.Tags.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write($" [{string.Join(", ", info.Tags)}]");
                Console.ResetColor();
            }

            if (info.Requirements.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                var reqs = info.Requirements.Select(r => $"{r.Type}: {r.Count}");
                Console.Write($" Req: {string.Join(", ", reqs)}");
                Console.ResetColor();
            }

            if (!string.IsNullOrEmpty(info.Description))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($" - {info.Description}");
                Console.ResetColor();
            }

            Console.WriteLine();
        }
        else
        {
            Console.WriteLine(CardName(id));
        }
    }

    // ── Prelude ──

    private JObject PromptPrelude(PreludeOptionsDto prelude)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=== PLAY PRELUDE ===");
        Console.ResetColor();

        for (int i = 0; i < prelude.RemainingPreludes.Count; i++)
        {
            var id = prelude.RemainingPreludes[i];
            Console.WriteLine($"  {i + 1}. {CardName(id)}");
        }
        int choice = ReadChoice(prelude.RemainingPreludes.Count) - 1;

        var move = MakeMove("PlayPrelude");
        move["preludeId"] = prelude.RemainingPreludes[choice];
        return move;
    }

    // ── Draft ──

    private JObject PromptDraft(DraftOptionsDto draft)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=== DRAFT ===");
        Console.ResetColor();

        Console.WriteLine("Pick a card to draft:");
        for (int i = 0; i < draft.DraftHand.Count; i++)
        {
            var id = draft.DraftHand[i];
            Console.WriteLine($"  {i + 1}. {CardName(id)}");
        }
        int choice = ReadChoice(draft.DraftHand.Count) - 1;

        var move = MakeMove("DraftCard");
        move["cardId"] = draft.DraftHand[choice];
        return move;
    }

    // ── Buy Cards ──

    private JObject PromptBuyCards(BuyCardsOptionsDto buyCards, PlayerStateDto playerState)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=== BUY CARDS ===");
        Console.ResetColor();

        Console.WriteLine($"Available cards ({buyCards.CostPerCard} MC each, you have {playerState.Resources.MegaCredits} MC):");
        for (int i = 0; i < buyCards.AvailableCards.Count; i++)
        {
            var id = buyCards.AvailableCards[i];
            Console.WriteLine($"  {i + 1}. {CardName(id)}");
        }
        Console.Write("Enter card numbers (comma-separated), or press Enter for none: ");
        var picks = ReadMultiChoiceOptional(buyCards.AvailableCards.Count);
        var cardIds = picks.Select(i => buyCards.AvailableCards[i - 1]).ToList();

        var move = MakeMove("BuyCards");
        move["cardIds"] = new JArray(cardIds.ToArray());
        return move;
    }

    // ── Action Phase ──

    private JObject PromptAction(ActionPhaseOptionsDto actions, PlayerStateDto playerState)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=== ACTION PHASE ===");
        Console.ResetColor();

        var options = new List<(string Label, Func<JObject> Build)>();

        // Playable cards
        foreach (var card in actions.PlayableCards)
        {
            var c = card;
            var steelInfo = c.CanUseSteel ? " +Steel" : "";
            var tiInfo = c.CanUseTitanium ? " +Ti" : "";
            options.Add(($"[PLAY] {CardName(c.CardId)} (cost {c.EffectiveCost}{steelInfo}{tiInfo})", () =>
            {
                var payment = PromptPayment(c, playerState);
                var move = MakeMove("PlayCard");
                move["cardId"] = c.CardId;
                move["payment"] = new JObject
                {
                    ["megaCredits"] = payment.MegaCredits,
                    ["steel"] = payment.Steel,
                    ["titanium"] = payment.Titanium,
                    ["heat"] = payment.Heat,
                };
                return move;
            }));
        }

        // Standard projects
        foreach (var sp in actions.StandardProjects.Where(s => s.Available))
        {
            var s = sp;
            if (s.Project.Equals("SellPatents", StringComparison.OrdinalIgnoreCase))
            {
                options.Add(($"[SP] Sell Patents", () =>
                {
                    Console.WriteLine("Choose cards to sell:");
                    for (int i = 0; i < playerState.Hand.Count; i++)
                        Console.WriteLine($"  {i + 1}. {CardName(playerState.Hand[i])}");
                    Console.Write("Enter card numbers (comma-separated): ");
                    var picks = ReadMultiChoice(playerState.Hand.Count, 1, playerState.Hand.Count);
                    var discard = picks.Select(i => playerState.Hand[i - 1]).ToList();

                    var move = MakeMove("SellPatents");
                    move["cardIds"] = new JArray(discard.ToArray());
                    return move;
                }));
            }
            else
            {
                var needsLoc = s.Project.Equals("Aquifer", StringComparison.OrdinalIgnoreCase)
                    || s.Project.Equals("Greenery", StringComparison.OrdinalIgnoreCase)
                    || s.Project.Equals("City", StringComparison.OrdinalIgnoreCase);
                options.Add(($"[SP] {s.Project} ({s.Cost} MC)", () =>
                {
                    var move = MakeMove(s.Project);
                    if (needsLoc && s.ValidLocations != null && s.ValidLocations.Count > 0)
                    {
                        var loc = PromptLocation(s.ValidLocations);
                        move["location"] = new JObject { ["col"] = loc.Col, ["row"] = loc.Row };
                    }
                    return move;
                }));
            }
        }

        // Card actions
        foreach (var ca in actions.UsableCardActions)
        {
            var a = ca;
            options.Add(($"[ACTION] {CardName(a.CardId)}", () =>
            {
                var move = MakeMove("UseCardAction");
                move["cardId"] = a.CardId;
                return move;
            }));
        }

        // Milestones
        foreach (var ms in actions.ClaimableMilestones)
        {
            var m = ms;
            options.Add(($"[MILESTONE] {m.Name} (8 MC)", () =>
            {
                var move = MakeMove("ClaimMilestone");
                move["milestoneName"] = m.Name;
                return move;
            }));
        }

        // Awards
        foreach (var aw in actions.FundableAwards)
        {
            var a = aw;
            options.Add(($"[AWARD] {a.Name} ({a.Cost} MC)", () =>
            {
                var move = MakeMove("FundAward");
                move["awardName"] = a.Name;
                return move;
            }));
        }

        // Conversions
        if (actions.CanConvertHeat)
            options.Add(("[HEAT] Convert 8 heat → raise temperature", () => MakeMove("ConvertHeat")));

        if (actions.CanConvertPlants && actions.ValidGreeneryLocations.Count > 0)
        {
            options.Add(("[PLANTS] Convert 8 plants → place greenery", () =>
            {
                var loc = PromptLocation(actions.ValidGreeneryLocations);
                var move = MakeMove("ConvertPlants");
                move["location"] = new JObject { ["col"] = loc.Col, ["row"] = loc.Row };
                return move;
            }));
        }

        if (actions.CanPerformFirstAction)
            options.Add(("[FIRST] Perform first action", () => MakeMove("PerformFirstAction")));

        // End turn (skip remaining actions) or Pass (done for generation)
        if (actions.CanEndTurn)
            options.Add(("[SKIP] Skip second action", () => MakeMove("EndTurn")));
        if (actions.CanPass)
            options.Add(("[PASS] Pass (no more actions this generation)", () => MakeMove("Pass")));

        // Display
        for (int i = 0; i < options.Count; i++)
            Console.WriteLine($"  {i + 1}. {options[i].Label}");

        int choice = ReadChoice(options.Count) - 1;
        return options[choice].Build();
    }

    // ── Final Greenery ──

    private JObject PromptFinalGreenery(FinalGreeneryOptionsDto fg)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=== FINAL GREENERY CONVERSION ===");
        Console.ResetColor();

        if (fg.CanConvert && fg.ValidGreeneryLocations.Count > 0)
        {
            Console.WriteLine("  1. Convert plants to greenery");
            Console.WriteLine("  2. Pass");
            int choice = ReadChoice(2);
            if (choice == 1)
            {
                var loc = PromptLocation(fg.ValidGreeneryLocations);
                var move = MakeMove("ConvertPlants");
                move["location"] = new JObject { ["col"] = loc.Col, ["row"] = loc.Row };
                return move;
            }
        }

        return MakeMove("Pass");
    }

    // ── Pending Action ──

    private JObject PromptPendingAction(JObject pending, PlayerStateDto playerState)
    {
        var type = pending["type"]?.Value<string>() ?? "";
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n--- Pending Action: {type} ---");
        Console.ResetColor();

        switch (type)
        {
            case "PlaceTile":
            case "ClaimLand":
            {
                var tileType = pending["tileType"]?.Value<string>() ?? "tile";
                var locs = pending["validLocations"]?.ToObject<List<HexCoordDto>>() ?? new();
                Console.WriteLine($"Place {tileType}:");
                var loc = PromptLocation(locs);
                var move = MakeMove("PlaceTile");
                move["location"] = new JObject { ["col"] = loc.Col, ["row"] = loc.Row };
                return move;
            }
            case "RemoveResource":
            case "ReduceProduction":
            {
                var resource = pending["resource"]?.Value<string>() ?? "";
                var amount = pending["amount"]?.Value<int>() ?? 0;
                var targets = pending["validTargetPlayerIds"]?.ToObject<List<int>>() ?? new();
                Console.WriteLine($"Choose target to {(type == "RemoveResource" ? "remove" : "reduce")} " +
                    $"{amount} {resource} from:");
                for (int i = 0; i < targets.Count; i++)
                    Console.WriteLine($"  {i + 1}. Player {targets[i]}");
                int choice = ReadChoice(targets.Count) - 1;
                var move = MakeMove("ChooseTargetPlayer");
                move["targetPlayerId"] = targets[choice];
                return move;
            }
            case "AddCardResource":
            {
                var cards = pending["validCardIds"]?.ToObject<List<string>>() ?? new();
                Console.WriteLine("Choose card to add resource to:");
                for (int i = 0; i < cards.Count; i++)
                    Console.WriteLine($"  {i + 1}. {CardName(cards[i])}");
                int choice = ReadChoice(cards.Count) - 1;
                var move = MakeMove("SelectCard");
                move["cardId"] = cards[choice];
                return move;
            }
            case "ChooseOption":
            {
                var desc = pending["description"]?.Value<string>() ?? "";
                var opts = pending["options"]?.ToObject<List<string>>() ?? new();
                Console.WriteLine(desc);
                for (int i = 0; i < opts.Count; i++)
                    Console.WriteLine($"  {i + 1}. {opts[i]}");
                int choice = ReadChoice(opts.Count) - 1;
                var move = MakeMove("ChooseOption");
                move["optionIndex"] = choice;
                return move;
            }
            case "DiscardCards":
            {
                int count = pending["count"]?.Value<int>() ?? 1;
                Console.WriteLine($"Discard {count} card(s):");
                for (int i = 0; i < playerState.Hand.Count; i++)
                    Console.WriteLine($"  {i + 1}. {CardName(playerState.Hand[i])}");
                var picks = ReadMultiChoice(playerState.Hand.Count, count, count);
                var discard = picks.Select(i => playerState.Hand[i - 1]).ToList();
                var move = MakeMove("DiscardCards");
                move["cardIds"] = new JArray(discard.ToArray());
                return move;
            }
            case "BuyCards":
            {
                var available = pending["availableCardIds"]?.ToObject<List<string>>() ?? new();
                Console.WriteLine("Buy cards (3 MC each):");
                for (int i = 0; i < available.Count; i++)
                    Console.WriteLine($"  {i + 1}. {CardName(available[i])}");
                Console.Write("Enter card numbers (comma-separated), or press Enter for none: ");
                var picks = ReadMultiChoiceOptional(available.Count);
                var cards = picks.Select(i => available[i - 1]).ToList();
                var move = MakeMove("BuyCards");
                move["cardIds"] = new JArray(cards.ToArray());
                return move;
            }
            case "ChooseCardToPlay":
            {
                var desc = pending["description"]?.Value<string>() ?? "Choose a card to play:";
                var cards = pending["cardIds"]?.ToObject<List<string>>() ?? new();
                Console.WriteLine(desc);
                for (int i = 0; i < cards.Count; i++)
                    Console.WriteLine($"  {i + 1}. {CardName(cards[i])}");
                int choice = ReadChoice(cards.Count) - 1;
                var move = MakeMove("SelectCard");
                move["cardId"] = cards[choice];
                return move;
            }
            case "PlayCardFromHand":
            {
                var desc = pending["description"]?.Value<string>() ?? "";
                Console.WriteLine(desc);
                Console.WriteLine("Choose a card to play from hand, or 0 to skip:");
                for (int i = 0; i < playerState.Hand.Count; i++)
                    Console.WriteLine($"  {i + 1}. {CardName(playerState.Hand[i])}");

                Console.Write("Choice: ");
                var input = Console.ReadLine()?.Trim() ?? "0";
                if (int.TryParse(input, out int idx) && idx >= 1 && idx <= playerState.Hand.Count)
                {
                    var cardId = playerState.Hand[idx - 1];
                    // Simplified: pay full cost in MC
                    var move = MakeMove("PlayCard");
                    move["cardId"] = cardId;
                    move["payment"] = new JObject
                    {
                        ["megaCredits"] = 0, // discount may cover it
                        ["steel"] = 0,
                        ["titanium"] = 0,
                        ["heat"] = 0,
                    };
                    return move;
                }
                return MakeMove("Pass");
            }
            case "ChooseEffectOrder":
            {
                var sourceCard = pending["sourceCardId"]?.Value<string>() ?? "";
                var indices = pending["remainingEffectIndices"]?.ToObject<List<int>>() ?? new();
                var descriptions = pending["effectDescriptions"]?.ToObject<List<string>>() ?? new();

                Console.WriteLine($"Choose which effect of {CardName(sourceCard)} to resolve next:");
                for (int i = 0; i < descriptions.Count; i++)
                    Console.WriteLine($"  {i + 1}. {descriptions[i]}");
                Console.WriteLine($"  {descriptions.Count + 1}. [AUTO] Resolve all remaining in default order");

                int choice = ReadChoice(descriptions.Count + 1);
                if (choice == descriptions.Count + 1)
                {
                    var move = MakeMove("ChooseEffectOrder");
                    move["effectIndex"] = -1;
                    return move;
                }
                else
                {
                    var move = MakeMove("ChooseEffectOrder");
                    move["effectIndex"] = indices[choice - 1];
                    return move;
                }
            }
        }

        Console.WriteLine("Unknown pending action type. Passing.");
        return MakeMove("Pass");
    }

    // ── Helpers ──

    private PaymentInfo PromptPayment(PlayableCardDto card, PlayerStateDto player)
    {
        var auto = PaymentCalculator.Calculate(
            card.EffectiveCost, card.CanUseSteel, card.CanUseTitanium, card.CanUseHeat,
            player.Resources.MegaCredits, player.Resources.Steel,
            player.Resources.Titanium, player.Resources.Heat);

        // Auto-pay without prompting if there's no alternative resource to use
        bool hasAlternative = (card.CanUseSteel && player.Resources.Steel > 0)
            || (card.CanUseTitanium && player.Resources.Titanium > 0)
            || (card.CanUseHeat && player.Resources.Heat > 0);
        if (!hasAlternative)
            return auto;

        Console.Write($"  Payment for {CardName(card.CardId)} (cost {card.EffectiveCost}): ");
        Console.Write($"{auto.MegaCredits} MC");
        if (auto.Steel > 0) Console.Write($", {auto.Steel} steel");
        if (auto.Titanium > 0) Console.Write($", {auto.Titanium} titanium");
        if (auto.Heat > 0) Console.Write($", {auto.Heat} heat");
        Console.Write(" [Enter to accept, or 'c' to customize]: ");

        var input = Console.ReadLine()?.Trim() ?? "";
        if (input.Equals("c", StringComparison.OrdinalIgnoreCase))
        {
            Console.Write($"  MC (have {player.Resources.MegaCredits}): ");
            int mc = int.Parse(Console.ReadLine()?.Trim() ?? "0");
            int steel = 0, titanium = 0, heat = 0;
            if (card.CanUseSteel)
            {
                Console.Write($"  Steel (have {player.Resources.Steel}, worth 2 MC each): ");
                steel = int.Parse(Console.ReadLine()?.Trim() ?? "0");
            }
            if (card.CanUseTitanium)
            {
                Console.Write($"  Titanium (have {player.Resources.Titanium}, worth 3 MC each): ");
                titanium = int.Parse(Console.ReadLine()?.Trim() ?? "0");
            }
            if (card.CanUseHeat)
            {
                Console.Write($"  Heat (have {player.Resources.Heat}): ");
                heat = int.Parse(Console.ReadLine()?.Trim() ?? "0");
            }
            return new PaymentInfo(mc, steel, titanium, heat);
        }

        return auto;
    }

    private HexCoordDto PromptLocation(List<HexCoordDto> locations)
    {
        Console.WriteLine("Choose location:");
        for (int i = 0; i < locations.Count; i++)
            Console.WriteLine($"  {i + 1}. {locations[i]}");
        int choice = ReadChoice(locations.Count) - 1;
        return locations[choice];
    }

    private JObject MakeMove(string type) =>
        new() { ["type"] = type, ["playerId"] = _playerId };

    private string CardName(string id) =>
        _cardNames.GetValueOrDefault(id, id);

    private static int ReadChoice(int max)
    {
        while (true)
        {
            Console.Write($"Choice (1-{max}): ");
            var input = Console.ReadLine()?.Trim() ?? "";
            if (int.TryParse(input, out int val) && val >= 1 && val <= max)
                return val;
            Console.WriteLine($"Please enter a number between 1 and {max}.");
        }
    }

    private static List<int> ReadMultiChoice(int max, int minCount, int maxCount)
    {
        while (true)
        {
            var input = Console.ReadLine()?.Trim() ?? "";
            var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var nums = new List<int>();
            bool valid = true;
            foreach (var p in parts)
            {
                if (int.TryParse(p, out int val) && val >= 1 && val <= max)
                    nums.Add(val);
                else
                    valid = false;
            }
            if (valid && nums.Count >= minCount && nums.Count <= maxCount && nums.Distinct().Count() == nums.Count)
                return nums;
            Console.Write($"Enter {minCount}-{maxCount} unique numbers (1-{max}), comma-separated: ");
        }
    }

    private static List<int> ReadMultiChoiceOptional(int max)
    {
        var input = Console.ReadLine()?.Trim() ?? "";
        if (string.IsNullOrEmpty(input)) return new List<int>();

        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var nums = new List<int>();
        foreach (var p in parts)
        {
            if (int.TryParse(p, out int val) && val >= 1 && val <= max)
                nums.Add(val);
        }
        return nums.Distinct().ToList();
    }
}

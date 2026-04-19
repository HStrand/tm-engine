using TmEngine.Cli;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Harness subcommands (used when Claude drives a game turn-by-turn; no console banner)
if (args.Length >= 2 && args[0] == "harness-bot")
{
    await RunHarnessBotMove(args[1], int.Parse(args[2]));
    return;
}
if (args.Length >= 2 && args[0] == "harness-show")
{
    await RunHarnessShow(args[1]);
    return;
}
if (args.Length >= 2 && args[0] == "harness-show-ai")
{
    await RunHarnessShowAi(args[1]);
    return;
}

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔════════════════════════════════════════╗");
Console.WriteLine("║     TERRAFORMING MARS - CLI Client     ║");
Console.WriteLine("╚════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

Console.Write("Game mode (1=Human vs Bot, 2=Bot vs Bot, 3=Human vs AI) [1]: ");
var modeInput = Console.ReadLine()?.Trim();

if (modeInput == "2")
    await RunBotGame();
else if (modeInput == "3")
    await RunHumanVsAiGame();
else
    await RunInteractiveGame();

// ════════════════════════════════════════════════════════════════
//  BOT VS BOT MODE
// ════════════════════════════════════════════════════════════════

async Task RunBotGame()
{
    Console.WriteLine();

    bool corporateEra = true;
    bool draft = true;
    bool prelude = true;
    string map = "Hellas";

    Console.Write("Search for a card? (Enter card name, or leave blank for seed/random): ");
    var cardInput = Console.ReadLine()?.Trim();

    string? targetCardId = null;
    int? seed = null;

    if (!string.IsNullOrEmpty(cardInput))
    {
        targetCardId = CardSearcher.ResolveCardName(cardInput);
        while (targetCardId == null)
        {
            Console.Write("Search for a card? (Enter card name, or leave blank for seed/random): ");
            cardInput = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(cardInput))
                break;
            targetCardId = CardSearcher.ResolveCardName(cardInput);
        }
    }

    if (targetCardId != null)
    {
        Console.WriteLine("  Searching for a matching seed...");
        seed = CardSearcher.SearchForCard(targetCardId, corporateEra, prelude);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Found seed: {seed}");
        Console.ResetColor();
    }
    else
    {
        Console.Write("RNG seed (Enter for random): ");
        var seedInput = Console.ReadLine()?.Trim();
        seed = int.TryParse(seedInput, out int parsedSeed) ? parsedSeed : null;
    }

    seed ??= Random.Shared.Next();

    var baseUrl = "http://localhost:7102/api";
    var api = new ApiClient(baseUrl);

    // Player 0 is the targeted bot (if we have a target card), Player 1 is a regular bot
    BotPlayer bot0 = targetCardId != null
        ? new TargetedBotPlayer(0, targetCardId)
        : new BotPlayer(0);
    var bot1 = new BotPlayer(1);

    var seedInfo = $", Seed={seed}";
    var cardInfo = targetCardId != null ? $", Target={bot0.CardName(targetCardId)}" : "";
    Console.WriteLine($"Creating game: {map}, CE={corporateEra}, Draft={draft}, Prelude={prelude}{seedInfo}{cardInfo}...");

    string gameId;
    try
    {
        gameId = await api.CreateGameAsync(new CreateGameRequest(2, map, corporateEra, draft, prelude, seed));
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Failed to create game: {ex.Message}");
        Console.WriteLine("Is the API running? Start it with: func start --port 7102");
        Console.ResetColor();
        return;
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Game created: {gameId}");
    Console.ResetColor();
    Console.WriteLine();

    // Fetch card names
    try
    {
        var cardMetadata = await api.GetGameCardsAsync(gameId);
        var names = cardMetadata.ToDictionary(kv => kv.Key, kv => kv.Value.Name);
        bot0.UpdateCardNames(names);
        bot1.UpdateCardNames(names);
    }
    catch { }

    // Main game loop
    bool gameOver = false;
    int consecutiveErrors = 0;
    int moveCount = 0;
    bool stopAfterTargetPlayed = targetCardId != null;

    while (!gameOver)
    {
        // Find a player who can act
        int? actingPlayer = null;
        AvailableMovesDto? moves = null;

        foreach (int pid in new[] { 0, 1 })
        {
            try
            {
                var (m, cn) = await api.GetLegalMovesAsync(gameId, pid);
                bot0.UpdateCardNames(cn);
                bot1.UpdateCardNames(cn);

                if (m.GameOver)
                {
                    var (finalState, finalNames, _) = await api.GetGameStateAsync(gameId, 0);
                    var display = new GameDisplay();
                    display.ShowFinalScores(finalState, finalNames);
                    gameOver = true;
                    break;
                }

                if (!m.WaitingForOtherPlayer)
                {
                    actingPlayer = pid;
                    moves = m;
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error getting legal moves for player {pid}: {ex.Message}");
                Console.ResetColor();
                consecutiveErrors++;
                if (consecutiveErrors > 5)
                {
                    Console.WriteLine("Too many errors. Exiting.");
                    return;
                }
            }
        }

        if (gameOver) break;

        if (actingPlayer == null)
        {
            consecutiveErrors++;
            if (consecutiveErrors > 5)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Stuck — no player can act. Exiting.");
                Console.ResetColor();
                break;
            }
            await Task.Delay(300);
            continue;
        }

        consecutiveErrors = 0;
        int playerId = actingPlayer.Value;

        // Get game state for the acting player
        var (state, stateCardNames, _) = await api.GetGameStateAsync(gameId, playerId);
        bot0.UpdateCardNames(stateCardNames);
        bot1.UpdateCardNames(stateCardNames);

        var botState = state.Players.First(p => p.PlayerId == playerId);
        var activeBot = playerId == 0 ? bot0 : bot1;

        // Pick move
        Newtonsoft.Json.Linq.JObject move;
        string description;
        if (activeBot is TargetedBotPlayer targeted)
            (move, description) = targeted.PickMove(moves!, botState);
        else
            (move, description) = activeBot.PickMove(moves!, botState);

        // Submit
        var result = await api.SubmitMoveAsync(gameId, playerId, move);

        // Retry logic
        if (!result.Success)
        {
            for (int retry = 0; retry < 10 && !result.Success; retry++)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    Move rejected: {result.Error} — retrying...");
                Console.ResetColor();
                if (activeBot is TargetedBotPlayer t2)
                    (move, description) = t2.PickMove(moves!, botState);
                else
                    (move, description) = activeBot.PickMove(moves!, botState);
                result = await api.SubmitMoveAsync(gameId, playerId, move);
            }

            if (!result.Success)
            {
                // Fallback to EndTurn or Pass
                var endMove = new Newtonsoft.Json.Linq.JObject
                {
                    ["type"] = moves!.Actions?.CanEndTurn == true ? "EndTurn" : "Pass",
                    ["playerId"] = playerId
                };
                result = await api.SubmitMoveAsync(gameId, playerId, endMove);
                description = moves!.Actions?.CanEndTurn == true ? "ends turn (fallback)" : "passes (fallback)";
            }
        }

        if (result.Success)
        {
            if (result.CardNames != null)
            {
                bot0.UpdateCardNames(result.CardNames);
                bot1.UpdateCardNames(result.CardNames);
            }

            moveCount++;
            var playerLabel = playerId == 0 ? "P0" : "P1";
            Console.ForegroundColor = playerId == 0 ? ConsoleColor.DarkCyan : ConsoleColor.DarkRed;
            Console.WriteLine($"  [{playerLabel}] {description}");
            Console.ResetColor();

            // Show player status after the move
            if (result.State != null)
            {
                foreach (var p in result.State.Players)
                    ShowPlayerStatus(p, result.CardNames ?? stateCardNames);
            }

            // Check if the target card was played and all its effects are resolved
            if (stopAfterTargetPlayed && bot0 is TargetedBotPlayer tb && tb.TargetCardPlayed)
            {
                // Don't stop if there's still a pending action to resolve
                var (checkMoves, _) = await api.GetLegalMovesAsync(gameId, 0);
                if (checkMoves.PendingAction != null)
                    continue;

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("════════════════════════════════════════════════════════");
                Console.WriteLine($"  TARGET CARD PLAYED: {tb.CardName(targetCardId!)}");
                Console.WriteLine("════════════════════════════════════════════════════════");
                Console.ResetColor();
                Console.WriteLine();

                // Show game state after the card was fully resolved
                var (postState, postCardNames, _) = await api.GetGameStateAsync(gameId, 0);
                var display = new GameDisplay();
                display.ShowGameState(postState, postCardNames, 0);

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Game ID: {gameId} | Seed: {seed} | Moves: {moveCount}");
                Console.WriteLine("Stopping for review. The game state above reflects the state after playing the target card.");
                Console.ResetColor();
                return;
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Move rejected after retries: {result.Error}");
            Console.ResetColor();
        }
    }

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Bot game finished. Seed: {seed} | Moves: {moveCount}");
    Console.ResetColor();
}

// ════════════════════════════════════════════════════════════════
//  INTERACTIVE (HUMAN VS BOT) MODE
// ════════════════════════════════════════════════════════════════

async Task RunInteractiveGame()
{
    const int HumanPlayerId = 0;
    const int BotPlayerId = 1;

    Console.WriteLine();

    var map = "Hellas";
    bool corporateEra = true;
    bool draft = true;
    bool prelude = true;

    Console.Write("Search for a card? (Enter card name, or leave blank for seed/random): ");
    var cardInput = Console.ReadLine()?.Trim();

    int? seed = null;
    if (!string.IsNullOrEmpty(cardInput))
    {
        var cardId = CardSearcher.ResolveCardName(cardInput);
        while (cardId == null)
        {
            Console.Write("Search for a card? (Enter card name, or leave blank for seed/random): ");
            cardInput = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(cardInput))
                break;
            cardId = CardSearcher.ResolveCardName(cardInput);
        }

        if (cardId != null)
        {
            Console.WriteLine("  Searching for a matching seed...");
            seed = CardSearcher.SearchForCard(cardId, corporateEra, prelude);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  Found seed: {seed}");
            Console.ResetColor();
        }
    }

    if (seed == null)
    {
        Console.Write("RNG seed (Enter for random): ");
        var seedInput = Console.ReadLine()?.Trim();
        seed = int.TryParse(seedInput, out int parsedSeed) ? parsedSeed : null;
    }

    var baseUrl = "http://localhost:7102/api";

    var api = new ApiClient(baseUrl);
    var display = new GameDisplay();
    var presenter = new MovePresenter(HumanPlayerId);
    var bot = new BotPlayer(BotPlayerId);

    // Create game
    Console.WriteLine();
    var seedInfo = seed.HasValue ? $", Seed={seed}" : "";
    Console.WriteLine($"Creating game: {map}, CE={corporateEra}, Draft={draft}, Prelude={prelude}{seedInfo}...");

    string gameId;
    try
    {
        gameId = await api.CreateGameAsync(new CreateGameRequest(2, map, corporateEra, draft, prelude, seed));
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Failed to create game: {ex.Message}");
        Console.WriteLine("Is the API running? Start it with: func start --port 7102");
        Console.ResetColor();
        return;
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Game created: {gameId}");
    Console.ResetColor();

    // Fetch card metadata for detailed display
    try
    {
        var cardInfo = await api.GetGameCardsAsync(gameId);
        presenter.UpdateCardInfo(cardInfo);
    }
    catch
    {
        // Non-fatal — will fall back to card names only
    }

    // Main game loop — one move per iteration, simple and predictable
    bool gameOver = false;
    int consecutiveErrors = 0;
    int noProgressCount = 0;

    while (!gameOver)
    {
        // Find a player who can act
        int? actingPlayer = null;
        AvailableMovesDto? moves = null;
        Dictionary<string, string>? cardNames = null;

        // Check both players, preferring human when both can act (simultaneous phases)
        foreach (int pid in new[] { HumanPlayerId, BotPlayerId })
        {
            try
            {
                var (m, cn) = await api.GetLegalMovesAsync(gameId, pid);
                presenter.UpdateCardNames(cn);
                bot.UpdateCardNames(cn);

                if (m.GameOver)
                {
                    var (finalState, finalNames, _) = await api.GetGameStateAsync(gameId, HumanPlayerId);
                    presenter.UpdateCardNames(finalNames);
                    display.ShowFinalScores(finalState, finalNames);
                    gameOver = true;
                    break;
                }

                if (!m.WaitingForOtherPlayer)
                {
                    actingPlayer = pid;
                    moves = m;
                    cardNames = cn;
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error getting legal moves for player {pid}: {ex.Message}");
                Console.ResetColor();
                consecutiveErrors++;
                if (consecutiveErrors > 5)
                {
                    Console.WriteLine("Too many errors. Exiting.");
                    return;
                }
            }
        }

        if (gameOver) break;

        if (actingPlayer == null)
        {
            noProgressCount++;
            if (noProgressCount > 5)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Stuck — no player can act. Dumping full state...\n");
                Console.ResetColor();

                try
                {
                    var (debugState, debugCardNames, _) = await api.GetGameStateAsync(gameId, HumanPlayerId);
                    display.ShowGameState(debugState, debugCardNames, HumanPlayerId);

                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n  ActivePlayerId: {debugState.ActivePlayerId}");
                    foreach (var p in debugState.Players)
                        Console.WriteLine($"  Player {p.PlayerId}: Passed={p.Passed}, ActionsThisTurn={p.ActionsThisTurn}");

                    // Show legal moves for both players
                    foreach (int pid in new[] { 0, 1 })
                    {
                        var (dm, _) = await api.GetLegalMovesAsync(gameId, pid);
                        Console.WriteLine($"\n  Legal moves for Player {pid}:");
                        Console.WriteLine($"    GameOver={dm.GameOver}, WaitingForOtherPlayer={dm.WaitingForOtherPlayer}");
                        if (dm.PendingAction != null)
                            Console.WriteLine($"    PendingAction: {dm.PendingAction}");
                        if (dm.Setup != null) Console.WriteLine("    Phase: Setup");
                        if (dm.Prelude != null) Console.WriteLine("    Phase: Prelude");
                        if (dm.Draft != null) Console.WriteLine("    Phase: Draft");
                        if (dm.BuyCards != null) Console.WriteLine("    Phase: BuyCards");
                        if (dm.FinalGreenery != null) Console.WriteLine($"    Phase: FinalGreenery (CanConvert={dm.FinalGreenery.CanConvert})");
                        if (dm.Actions != null)
                        {
                            var a = dm.Actions;
                            Console.WriteLine($"    Phase: Action (CanPass={a.CanPass}, CanEndTurn={a.CanEndTurn}, " +
                                $"Cards={a.PlayableCards.Count}, SPs={a.StandardProjects.Count(s => s.Available)}, " +
                                $"CardActions={a.UsableCardActions.Count}, Milestones={a.ClaimableMilestones.Count}, " +
                                $"Awards={a.FundableAwards.Count}, Heat={a.CanConvertHeat}, Plants={a.CanConvertPlants})");
                        }
                    }

                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  Failed to dump state: {ex.Message}");
                    Console.ResetColor();
                }

                break;
            }
            await Task.Delay(300);
            continue;
        }

        noProgressCount = 0;
        consecutiveErrors = 0;
        int playerId = actingPlayer.Value;

        // Get game state
        var (state, stateCardNames, _) = await api.GetGameStateAsync(gameId, playerId);
        presenter.UpdateCardNames(stateCardNames);
        bot.UpdateCardNames(stateCardNames);
        var allCardNames = MergeCardNames(cardNames!, stateCardNames);

        // Get the move
        Newtonsoft.Json.Linq.JObject move;
        string? botDescription = null;

        if (playerId == HumanPlayerId)
        {
            Dictionary<int, PlayerTagsDto>? tagMap = null;
            try
            {
                var status = await api.GetPlayerStatusAsync(gameId);
                tagMap = status.Players.ToDictionary(p => p.PlayerId, p => p.Tags);
            }
            catch { }
            var (__, ___, handCounts2) = await api.GetGameStateAsync(gameId, HumanPlayerId);
            display.ShowGameState(state, allCardNames, HumanPlayerId, handCounts2, tagMap);
            var playerState = state.Players.First(p => p.PlayerId == HumanPlayerId);
            move = presenter.PromptMove(moves!, playerState);
        }
        else
        {
            var botState = state.Players.First(p => p.PlayerId == BotPlayerId);
            (move, botDescription) = bot.PickMove(moves!, botState);
        }

        // Submit
        var result = await api.SubmitMoveAsync(gameId, playerId, move);

        // Bot retry logic
        if (!result.Success && playerId == BotPlayerId)
        {
            for (int retry = 0; retry < 10 && !result.Success; retry++)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  Bot move rejected: {result.Error} — retrying...");
                Console.ResetColor();
                var botState = state.Players.First(p => p.PlayerId == BotPlayerId);
                (move, botDescription) = bot.PickMove(moves!, botState);
                result = await api.SubmitMoveAsync(gameId, playerId, move);
            }

            if (!result.Success)
            {
                // Try EndTurn if bot has taken an action, otherwise Pass
                var endMove = new Newtonsoft.Json.Linq.JObject
                {
                    ["type"] = moves!.Actions?.CanEndTurn == true ? "EndTurn" : "Pass",
                    ["playerId"] = BotPlayerId
                };
                result = await api.SubmitMoveAsync(gameId, BotPlayerId, endMove);
                botDescription = moves!.Actions?.CanEndTurn == true ? "ends turn (fallback)" : "passes (fallback)";
            }
        }

        if (result.Success)
        {
            if (result.CardNames != null)
            {
                presenter.UpdateCardNames(result.CardNames);
                bot.UpdateCardNames(result.CardNames);
            }

            if (botDescription != null)
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine($"  Bot {botDescription}");
                Console.ResetColor();
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Move rejected: {result.Error}");
            Console.ResetColor();
        }

        // Loop continues — next iteration will re-check who can act
    }

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("Thanks for playing!");
    Console.ResetColor();
}

static void ShowPlayerStatus(PlayerStateDto p, Dictionary<string, string> cardNames)
{
    static string Prod(int v) => v >= 0 ? $"+{v}" : $"{v}";
    var corp = cardNames.GetValueOrDefault(p.CorporationId, p.CorporationId);
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"    P{p.PlayerId} ({corp}) TR:{p.TerraformRating}  " +
        $"MC:{p.Resources.MegaCredits}/{Prod(p.Production.MegaCredits)}  " +
        $"Steel:{p.Resources.Steel}/{Prod(p.Production.Steel)}  " +
        $"Ti:{p.Resources.Titanium}/{Prod(p.Production.Titanium)}  " +
        $"Plants:{p.Resources.Plants}/{Prod(p.Production.Plants)}  " +
        $"Energy:{p.Resources.Energy}/{Prod(p.Production.Energy)}  " +
        $"Heat:{p.Resources.Heat}/{Prod(p.Production.Heat)}");
    Console.ResetColor();
}

static string DescribeAiMove(GameStateDto state, Dictionary<string, string> cardNames)
{
    var lm = state.LastMove;
    if (lm == null) return "(unknown move)";

    string Name(string? id) =>
        id != null && cardNames.TryGetValue(id, out var n) ? n : id ?? "?";

    var type = lm["type"]?.ToString() ?? "";
    return type switch
    {
        "Setup" => $"chose {Name(lm["corporationId"]?.ToString())}",
        "PlayPrelude" => $"plays prelude {Name(lm["preludeId"]?.ToString())}",
        "DraftCard" => $"drafts a card",
        "BuyCards" =>
            lm["cardIds"] is Newtonsoft.Json.Linq.JArray arr
                ? $"buys {arr.Count} card{(arr.Count != 1 ? "s" : "")}"
                : "buys cards",
        "PlayCard" => $"plays {Name(lm["cardId"]?.ToString())}",
        "UseCardAction" => $"uses action on {Name(lm["cardId"]?.ToString())}",
        "PlaceTile" =>
            lm["location"] is Newtonsoft.Json.Linq.JObject loc
                ? $"places tile at ({loc["col"]},{loc["row"]})"
                : "places a tile",
        "ClaimMilestone" => $"claims milestone {lm["milestoneName"]}",
        "FundAward" => $"funds award {lm["awardName"]}",
        "Pass" => "passes",
        "EndTurn" => "ends turn",
        "ConvertHeat" => "converts heat to temperature",
        "ConvertPlants" =>
            lm["location"] is Newtonsoft.Json.Linq.JObject gloc
                ? $"converts plants to greenery at ({gloc["col"]},{gloc["row"]})"
                : "converts plants to greenery",
        "Asteroid" => "uses Asteroid standard project",
        "Aquifer" =>
            lm["location"] is Newtonsoft.Json.Linq.JObject aloc
                ? $"places ocean at ({aloc["col"]},{aloc["row"]})"
                : "places an ocean",
        "Greenery" =>
            lm["location"] is Newtonsoft.Json.Linq.JObject grloc
                ? $"places greenery at ({grloc["col"]},{grloc["row"]})"
                : "places a greenery",
        "City" =>
            lm["location"] is Newtonsoft.Json.Linq.JObject cloc
                ? $"places city at ({cloc["col"]},{cloc["row"]})"
                : "places a city",
        "PowerPlant" => "uses Power Plant standard project",
        "SellPatents" => "sells patents",
        "ChooseOption" => $"chooses option {lm["optionIndex"]}",
        "ChooseTargetPlayer" => $"targets player {lm["targetPlayerId"]}",
        "ChooseEffectOrder" => "resolves effects",
        "SelectCard" => $"selects {Name(lm["cardId"]?.ToString())}",
        _ => type
    };
}

static Dictionary<string, string> MergeCardNames(params Dictionary<string, string>[] dicts)
{
    var result = new Dictionary<string, string>();
    foreach (var d in dicts)
        foreach (var kv in d)
            result[kv.Key] = kv.Value;
    return result;
}

// ════════════════════════════════════════════════════════════════
//  HUMAN VS AI MODE
// ════════════════════════════════════════════════════════════════

async Task RunHumanVsAiGame()
{
    const int HumanPlayerId = 0;
    const int AiPlayerId = 1;

    Console.WriteLine();

    var map = "Tharsis";
    bool corporateEra = true;
    bool draft = true;
    bool prelude = true;

    Console.Write($"Map ({map}): ");
    var mapInput = Console.ReadLine()?.Trim();
    if (!string.IsNullOrEmpty(mapInput)) map = mapInput;

    Console.Write("RNG seed (Enter for random): ");
    var seedInput = Console.ReadLine()?.Trim();
    int? seed = int.TryParse(seedInput, out int parsedSeed) ? parsedSeed : null;

    var baseUrl = "http://localhost:7102/api";
    var api = new ApiClient(baseUrl);
    var display = new GameDisplay();
    var presenter = new MovePresenter(HumanPlayerId);

    // Create game
    Console.WriteLine();
    var seedInfo = seed.HasValue ? $", Seed={seed}" : "";
    Console.WriteLine($"Creating game: {map}, CE={corporateEra}, Draft={draft}, Prelude={prelude}{seedInfo}...");

    string gameId;
    try
    {
        gameId = await api.CreateGameAsync(new CreateGameRequest(2, map, corporateEra, draft, prelude, seed));
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Failed to create game: {ex.Message}");
        Console.ResetColor();
        return;
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Game created: {gameId}");
    Console.ResetColor();
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"  AI (Claude) plays as Player 1.");
    Console.WriteLine($"  Tell Claude to use this game ID: {gameId}");
    Console.ResetColor();

    try
    {
        var cardInfo = await api.GetGameCardsAsync(gameId);
        presenter.UpdateCardInfo(cardInfo);
    }
    catch { }

    // Main game loop
    bool gameOver = false;
    int noProgressCount = 0;
    int? lastMoveNumber = null;

    while (!gameOver)
    {
        int? actingPlayer = null;
        AvailableMovesDto? moves = null;
        Dictionary<string, string>? cardNames = null;

        foreach (int pid in new[] { HumanPlayerId, AiPlayerId })
        {
            try
            {
                var (m, cn) = await api.GetLegalMovesAsync(gameId, pid);
                presenter.UpdateCardNames(cn);

                if (m.GameOver)
                {
                    var (finalState, finalNames, _) = await api.GetGameStateAsync(gameId, HumanPlayerId);
                    presenter.UpdateCardNames(finalNames);
                    display.ShowFinalScores(finalState, finalNames);
                    gameOver = true;
                    break;
                }

                if (!m.WaitingForOtherPlayer)
                {
                    actingPlayer = pid;
                    moves = m;
                    cardNames = cn;
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
            }
        }

        if (gameOver) break;

        if (actingPlayer == null)
        {
            await Task.Delay(300);
            noProgressCount++;
            if (noProgressCount > 20)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Stuck — no player can act.");
                Console.ResetColor();
                break;
            }
            continue;
        }

        noProgressCount = 0;
        int playerId = actingPlayer.Value;

        var (state, stateCardNames, _) = await api.GetGameStateAsync(gameId, playerId);
        presenter.UpdateCardNames(stateCardNames);
        var allCardNames = MergeCardNames(cardNames!, stateCardNames);

        if (playerId == HumanPlayerId)
        {
            // Human's turn — interactive prompt
            Dictionary<int, PlayerTagsDto>? tagMap = null;
            try
            {
                var status = await api.GetPlayerStatusAsync(gameId);
                tagMap = status.Players.ToDictionary(p => p.PlayerId, p => p.Tags);
            }
            catch { }
            var (__, ___, handCounts2) = await api.GetGameStateAsync(gameId, HumanPlayerId);
            display.ShowGameState(state, allCardNames, HumanPlayerId, handCounts2, tagMap);
            var playerState = state.Players.First(p => p.PlayerId == HumanPlayerId);
            var move = presenter.PromptMove(moves!, playerState);
            var result = await api.SubmitMoveAsync(gameId, playerId, move);

            if (result.Success)
            {
                if (result.CardNames != null)
                    presenter.UpdateCardNames(result.CardNames);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Move rejected: {result.Error}");
                Console.ResetColor();
            }
        }
        else
        {
            // AI's turn — poll until Claude submits a move externally
            var currentMoveNumber = state.MoveNumber;
            if (lastMoveNumber == currentMoveNumber)
            {
                // Still waiting
                await Task.Delay(500);
                continue;
            }
            lastMoveNumber = currentMoveNumber;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine();
            Console.WriteLine($"  ⏳ Waiting for AI (Player 1) to submit a move...");
            Console.WriteLine($"     (Gen {state.Generation}, Phase: {state.Phase})");
            Console.ResetColor();

            // Poll until the move number changes
            while (true)
            {
                await Task.Delay(1000);
                try
                {
                    var (newState, newNames, _) = await api.GetGameStateAsync(gameId, HumanPlayerId);
                    presenter.UpdateCardNames(newNames);
                    if (newState.MoveNumber != currentMoveNumber)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkMagenta;
                        var desc = DescribeAiMove(newState, newNames);
                        Console.WriteLine($"  🤖 AI: {desc}");
                        Console.ResetColor();

                        currentMoveNumber = newState.MoveNumber;

                        // Check if the human can now act (handles simultaneous phases like Draft/Setup)
                        try
                        {
                            var (humanMoves, _) = await api.GetLegalMovesAsync(gameId, HumanPlayerId);
                            if (!humanMoves.WaitingForOtherPlayer || humanMoves.GameOver)
                                break;
                        }
                        catch { }

                        // Still AI's turn — keep polling
                        continue;
                    }
                }
                catch { }
            }
        }
    }

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("Thanks for playing!");
    Console.ResetColor();
}

// ════════════════════════════════════════════════════════════════
//  HARNESS MODE (used by turn.sh when Claude plays as player 0)
// ════════════════════════════════════════════════════════════════

async Task RunHarnessBotMove(string gameId, int playerId)
{
    var api = new ApiClient("http://localhost:7102/api");
    var bot = new BotPlayer(playerId);

    var (moves, cardNames) = await api.GetLegalMovesAsync(gameId, playerId);
    bot.UpdateCardNames(cardNames);
    if (moves.GameOver) { Console.WriteLine("GAME_OVER"); return; }
    if (moves.WaitingForOtherPlayer) { Console.WriteLine($"WAITING P{playerId}"); return; }

    var (state, stateCardNames, _) = await api.GetGameStateAsync(gameId, playerId);
    bot.UpdateCardNames(stateCardNames);
    var botState = state.Players.First(p => p.PlayerId == playerId);
    var (move, description) = bot.PickMove(moves, botState);

    var result = await api.SubmitMoveAsync(gameId, playerId, move);
    for (int retry = 0; retry < 10 && !result.Success; retry++)
    {
        (move, description) = bot.PickMove(moves, botState);
        result = await api.SubmitMoveAsync(gameId, playerId, move);
    }
    if (!result.Success)
    {
        var fallback = new Newtonsoft.Json.Linq.JObject
        {
            ["type"] = moves.Actions?.CanEndTurn == true ? "EndTurn" : "Pass",
            ["playerId"] = playerId
        };
        result = await api.SubmitMoveAsync(gameId, playerId, fallback);
        description = moves.Actions?.CanEndTurn == true ? "ends turn (fallback)" : "passes (fallback)";
    }
    Console.WriteLine($"P{playerId}: {description}");
}

async Task RunHarnessShow(string gameId)
{
    var api = new ApiClient("http://localhost:7102/api");
    var (moves0, cn0) = await api.GetLegalMovesAsync(gameId, 0);
    if (moves0.GameOver)
    {
        var (finalState, finalNames, _) = await api.GetGameStateAsync(gameId, 0);
        new GameDisplay().ShowFinalScores(finalState, finalNames);
        Console.WriteLine("GAME_OVER");
        return;
    }
    if (moves0.WaitingForOtherPlayer)
    {
        Console.WriteLine("WAITING_FOR_OPPONENT");
        return;
    }

    var (state, stateNames, _) = await api.GetGameStateAsync(gameId, 0);
    var allNames = MergeCardNames(cn0, stateNames);
    new GameDisplay().ShowGameState(state, allNames, 0);

    // Explicit hand dump with IDs — the filtered GetGameState call above ensures this
    // shows the real hand (not the spectator-view empty hand). Avoids having to do
    // ad-hoc curl queries to look up card IDs for submission.
    var me = state.Players.First(p => p.PlayerId == 0);
    Console.WriteLine();
    Console.WriteLine("=== HAND (id → name) ===");
    if (me.Hand.Count == 0)
    {
        Console.WriteLine("(empty)");
    }
    else
    {
        foreach (var cardId in me.Hand)
        {
            var name = allNames.GetValueOrDefault(cardId, "?");
            Console.WriteLine($"  {cardId}  {name}");
        }
    }

    // Dump raw legal moves JSON so Claude can see exact move shapes
    var json = Newtonsoft.Json.JsonConvert.SerializeObject(moves0, Newtonsoft.Json.Formatting.Indented);
    Console.WriteLine();
    Console.WriteLine("=== LEGAL MOVES (raw JSON) ===");
    Console.WriteLine(json);
}

async Task RunHarnessShowAi(string gameId)
{
    // Same as RunHarnessShow but for Player 1 (AI/Claude)
    const int AiPlayerId = 1;
    var api = new ApiClient("http://localhost:7102/api");
    var (moves, cn) = await api.GetLegalMovesAsync(gameId, AiPlayerId);
    if (moves.GameOver)
    {
        var (finalState, finalNames, _) = await api.GetGameStateAsync(gameId, AiPlayerId);
        new GameDisplay().ShowFinalScores(finalState, finalNames);
        Console.WriteLine("GAME_OVER");
        return;
    }
    if (moves.WaitingForOtherPlayer)
    {
        Console.WriteLine("WAITING_FOR_OPPONENT");
        return;
    }

    var (state, stateNames, _) = await api.GetGameStateAsync(gameId, AiPlayerId);
    var allNames = MergeCardNames(cn, stateNames);
    new GameDisplay().ShowGameState(state, allNames, AiPlayerId);

    var me = state.Players.First(p => p.PlayerId == AiPlayerId);
    Console.WriteLine();
    Console.WriteLine("=== HAND (id \u2192 name) ===");
    if (me.Hand.Count == 0)
    {
        Console.WriteLine("(empty)");
    }
    else
    {
        foreach (var cardId in me.Hand)
        {
            var name = allNames.GetValueOrDefault(cardId, "?");
            Console.WriteLine($"  {cardId}  {name}");
        }
    }

    var json = Newtonsoft.Json.JsonConvert.SerializeObject(moves, Newtonsoft.Json.Formatting.Indented);
    Console.WriteLine();
    Console.WriteLine("=== LEGAL MOVES (raw JSON) ===");
    Console.WriteLine(json);
}

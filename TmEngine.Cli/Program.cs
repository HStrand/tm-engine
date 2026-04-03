using TmEngine.Cli;

const int HumanPlayerId = 0;
const int BotPlayerId = 1;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔════════════════════════════════════════╗");
Console.WriteLine("║     TERRAFORMING MARS - CLI Client     ║");
Console.WriteLine("╚════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

//// Game options
//Console.Write("Map (1=Tharsis, 2=Hellas, 3=Elysium) [1]: ");
//var mapInput = Console.ReadLine()?.Trim();
//var map = mapInput switch { "2" => "Hellas", "3" => "Elysium", _ => "Tharsis" };

var map = "Tharsis";


//Console.Write("Corporate Era? (Y/n) [Y]: ");
//var ceInput = Console.ReadLine()?.Trim();
bool corporateEra = true;

//Console.Write("Draft variant? (y/N) [N]: ");
//var draftInput = Console.ReadLine()?.Trim();
//bool draft = draftInput?.Equals("y", StringComparison.OrdinalIgnoreCase) ?? false;
bool draft = true;

//Console.Write("Prelude expansion? (y/N) [N]: ");
//var preludeInput = Console.ReadLine()?.Trim();
//bool prelude = preludeInput?.Equals("y", StringComparison.OrdinalIgnoreCase) ?? false;
bool prelude = true;

Console.Write("RNG seed (Enter for random): ");
var seedInput = Console.ReadLine()?.Trim();
int? seed = int.TryParse(seedInput, out int parsedSeed) ? parsedSeed : null;

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
                var (finalState, finalNames) = await api.GetGameStateAsync(gameId, HumanPlayerId);
                presenter.UpdateCardNames(finalNames);
                var log = await api.GetHistoryAsync(gameId);
                display.ShowNewLogEntries(log);
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
                var (debugState, debugCardNames) = await api.GetGameStateAsync(gameId, HumanPlayerId);
                display.ShowGameState(debugState, debugCardNames, HumanPlayerId);

                var debugLog = await api.GetHistoryAsync(gameId);
                display.ShowNewLogEntries(debugLog);

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n  ActivePlayerIndex: {debugState.ActivePlayerIndex}");
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
    var (state, stateCardNames) = await api.GetGameStateAsync(gameId, playerId);
    presenter.UpdateCardNames(stateCardNames);
    bot.UpdateCardNames(stateCardNames);
    var allCardNames = MergeCardNames(cardNames!, stateCardNames);

    // Show new log entries
    var history = await api.GetHistoryAsync(gameId);
    display.ShowNewLogEntries(history);

    // Get the move
    Newtonsoft.Json.Linq.JObject move;
    string? botDescription = null;

    if (playerId == HumanPlayerId)
    {
        display.ShowGameState(state, allCardNames, HumanPlayerId);
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

static Dictionary<string, string> MergeCardNames(params Dictionary<string, string>[] dicts)
{
    var result = new Dictionary<string, string>();
    foreach (var d in dicts)
        foreach (var kv in d)
            result[kv.Key] = kv.Value;
    return result;
}

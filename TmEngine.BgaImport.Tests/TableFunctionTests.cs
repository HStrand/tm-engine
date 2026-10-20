using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using tm_engine.Api;
using tm_engine.Serialization;
using TmEngine.Domain.Models;
using TmEngine.Domain.Moves;

namespace TmEngine.BgaImport.Tests;

/// <summary>
/// The hot-seat endpoints, driven the way the web client drives them: JSON in, JSON out.
/// </summary>
public class TableFunctionTests
{
    private static readonly JsonSerializerSettings Json = SerializationSettings.Create();

    private readonly InMemoryGameStore _store = new();

    private TableFunction Function() => new(_store, Json, NullLogger<TableFunction>.Instance);

    [Fact]
    public async Task GetTable_ShowsEveryHand_AndOffersMovesToThePlayerToAct()
    {
        var state = await ImportGeneration4();

        var table = await GetTable(state.GameId);

        var players = (JArray)table["state"]!["players"]!;
        Assert.All(players, p => Assert.NotEmpty(p["hand"]!));
        Assert.Empty(table["state"]!["drawPile"]!);
        Assert.Equal(153, table.Value<int>("drawPileCount"));

        // Only StrandedKnight (seat 1) can act at the start of generation 4.
        var legalMoves = (JObject)table["legalMoves"]!;
        Assert.Equal(["1"], legalMoves.Properties().Select(p => p.Name));
        Assert.NotEmpty(legalMoves["1"]!["actions"]!["playableCards"]!);

        Assert.Equal("StrandedKnight", table["meta"]!["seats"]![1]!.Value<string>("name"));
        Assert.Equal(state.Players[1].TerraformRating, table["players"]![1]!["points"]!.Value<int>("tr"));
        Assert.Equal(state.Players[1].Resources.MegaCredits, table["players"]![1]!["resources"]!.Value<int>("megaCredits"));
        Assert.Equal("Domed Crater", table["cardNames"]!.Value<string>("016"));
    }

    [Fact]
    public async Task SubmitMove_AppliesTheMove_AndReturnsTheNewTable()
    {
        var state = await ImportGeneration4();

        var response = await Function().SubmitMove(Post("""{"type":"Pass","playerId":1}"""), state.GameId);

        var table = Body(response, HttpStatusCode.OK);
        Assert.Equal(1, table["state"]!.Value<int>("moveNumber"));
        Assert.Equal(1, table.Value<int>("latestMoveNumber"));
        Assert.True(table["state"]!["players"]![1]!.Value<bool>("passed"));
        Assert.Equal(["0"], ((JObject)table["legalMoves"]!).Properties().Select(p => p.Name));
    }

    [Fact]
    public async Task SubmitMove_RejectsAnIllegalMove_AndLeavesTheGameAlone()
    {
        var state = await ImportGeneration4();

        // Seat 0 is not the player to act.
        var response = await Function().SubmitMove(Post("""{"type":"Pass","playerId":0}"""), state.GameId);

        Assert.NotEmpty(Body(response, HttpStatusCode.BadRequest).Value<string>("error")!);
        Assert.Equal(0, (await _store.LoadStateAsync(state.GameId)).MoveNumber);
    }

    [Fact]
    public async Task GetTable_AtAnEarlierMove_ShowsThatPosition_WithoutOfferingMoves()
    {
        var state = await ImportGeneration4();
        await Function().SubmitMove(Post("""{"type":"Pass","playerId":1}"""), state.GameId);

        var request = new DefaultHttpContext().Request;
        request.QueryString = new QueryString("?at=0");
        var table = Body(await Function().GetTable(request, state.GameId), HttpStatusCode.OK);

        Assert.Equal(0, table["state"]!.Value<int>("moveNumber"));
        Assert.Equal(1, table.Value<int>("latestMoveNumber"));
        Assert.False(table["state"]!["players"]![1]!.Value<bool>("passed"));
        Assert.Empty((JObject)table["legalMoves"]!);
    }

    [Fact]
    public async Task Undo_TakesBackTheLatestMove_AndOffersMovesAgain()
    {
        var state = await ImportGeneration4();
        await Function().SubmitMove(Post("""{"type":"Pass","playerId":1}"""), state.GameId);

        var table = Body(await Function().Undo(Post("""{"moveNumber":1}"""), state.GameId), HttpStatusCode.OK);

        Assert.Equal(0, table["state"]!.Value<int>("moveNumber"));
        Assert.Equal(0, table.Value<int>("latestMoveNumber"));
        Assert.False(table["state"]!["players"]![1]!.Value<bool>("passed"));
        Assert.Equal(["1"], ((JObject)table["legalMoves"]!).Properties().Select(p => p.Name));
        Assert.Equal(0, (await _store.LoadStateAsync(state.GameId)).MoveNumber);
        Assert.Empty(Body(await Function().GetHistory(new DefaultHttpContext().Request, state.GameId), HttpStatusCode.OK)["moves"]!);
    }

    [Fact]
    public async Task Undo_ThenADifferentMove_TakesTheMoveNumberOver()
    {
        var state = await ImportGeneration4();
        await Function().SubmitMove(Post("""{"type":"Pass","playerId":1}"""), state.GameId);
        await Function().Undo(Post("""{"moveNumber":1}"""), state.GameId);

        var table = Body(await Function().SubmitMove(Post("""{"type":"PowerPlant","playerId":1}"""), state.GameId), HttpStatusCode.OK);

        Assert.Equal(1, table["state"]!.Value<int>("moveNumber"));
        Assert.False(table["state"]!["players"]![1]!.Value<bool>("passed"));
        var history = Body(await Function().GetHistory(new DefaultHttpContext().Request, state.GameId), HttpStatusCode.OK);
        Assert.Equal(["PowerPlant"], history["moves"]!.Select(m => m["move"]!.Value<string>("type")));
    }

    [Fact]
    public async Task Undo_RefusesAMoveThatIsNotTheLatest()
    {
        var state = await ImportGeneration4();
        await Function().SubmitMove(Post("""{"type":"Pass","playerId":1}"""), state.GameId);
        await Function().SubmitMove(Post("""{"type":"Pass","playerId":0}"""), state.GameId);

        Body(await Function().Undo(Post("""{"moveNumber":1}"""), state.GameId), HttpStatusCode.Conflict);

        Assert.Equal(2, (await _store.LoadStateAsync(state.GameId)).MoveNumber);
    }

    [Fact]
    public async Task Undo_RefusesToTakeBackTheStartingPosition()
    {
        var state = await ImportGeneration4();

        Body(await Function().Undo(Post("""{"moveNumber":0}"""), state.GameId), HttpStatusCode.BadRequest);

        Assert.Equal(0, (await _store.LoadStateAsync(state.GameId)).MoveNumber);
    }

    [Fact]
    public async Task GetHistory_ListsTheMovesPlayed()
    {
        var state = await ImportGeneration4();
        await Function().SubmitMove(Post("""{"type":"Pass","playerId":1}"""), state.GameId);
        await Function().SubmitMove(Post("""{"type":"Pass","playerId":0}"""), state.GameId);

        var history = Body(await Function().GetHistory(new DefaultHttpContext().Request, state.GameId), HttpStatusCode.OK);

        Assert.Equal(
            [(1, "Pass", 1), (2, "Pass", 0)],
            history["moves"]!.Select(m => (m.Value<int>("moveNumber"), m["move"]!.Value<string>("type")!, m["move"]!.Value<int>("playerId"))));
    }

    [Fact]
    public async Task GetTable_ReturnsNotFound_ForAnUnknownGame()
    {
        Body(await Function().GetTable(new DefaultHttpContext().Request, "nope"), HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Plays a few generations through the endpoints with every move sent as JSON. With TM_CONTRACT_FIXTURE_DIR
    /// set, the exchange is also written out as the fixture the web client's tests are built on.
    /// </summary>
    [Fact]
    public async Task HotSeatSession_PlaysThroughTheEndpoints()
    {
        var state = await ImportGeneration4();
        var player = new RandomPlayer(seed: 11);
        var exchanges = new JArray { new JObject { ["table"] = await GetTable(state.GameId) } };

        var moveTypes = new HashSet<string>();
        for (int i = 0; i < 400; i++)
        {
            var current = await _store.LoadStateAsync(state.GameId);
            if (current.IsGameOver() || player.NextMove(current) is not { } move)
                break;

            var moveJson = JsonConvert.SerializeObject(move, typeof(Move), Json);
            var table = Body(await Function().SubmitMove(Post(moveJson), state.GameId), HttpStatusCode.OK);

            moveTypes.Add(move.GetType().Name);
            if (i < 60)
                exchanges.Add(new JObject { ["move"] = JObject.Parse(moveJson), ["table"] = table });
        }

        var final = await _store.LoadStateAsync(state.GameId);
        Assert.True(final.Generation > 5, $"Only reached generation {final.Generation} after {final.MoveNumber} moves.");
        Assert.Contains(nameof(PlayCardMove), moveTypes);
        Assert.Contains(nameof(PlaceTileMove), moveTypes);
        Assert.Contains(nameof(DraftCardMove), moveTypes);
        Assert.Contains(nameof(BuyCardsMove), moveTypes);

        if (Environment.GetEnvironmentVariable("TM_CONTRACT_FIXTURE_DIR") is { Length: > 0 } directory)
        {
            var forkPoints = Fixtures.ForkPoints(Fixtures.Elysium2p);
            var firstGenerationStart = forkPoints.ForkPoints.First(f => f.Kind == ForkKind.GenerationStart && f.Generation == 2);
            var imported = BgaImporter.Import(Fixtures.Load(Fixtures.Elysium2p), new BgaImportOptions(firstGenerationStart.MoveIndex, GameId: "game-1"));

            File.WriteAllText(Path.Combine(directory, "hot-seat-session.json"), exchanges.ToString(Formatting.None));
            File.WriteAllText(Path.Combine(directory, "fork-points.json"), JsonConvert.SerializeObject(
                new ForkPointsResponse(forkPoints.Eligible, forkPoints.ForkPoints, forkPoints.Issues), Json));
            File.WriteAllText(Path.Combine(directory, "import.json"), JsonConvert.SerializeObject(
                new ImportResponse(imported.State!.GameId, imported.Fork, imported.Meta, imported.Issues), Json));
        }
    }

    /// <summary>
    /// A game picked up from the deal: both players choose their opening through the endpoints, play their
    /// preludes and go on into the action phase. With TM_CONTRACT_FIXTURE_DIR set, the exchange is written out
    /// as a fixture for the web client's tests.
    /// </summary>
    [Fact]
    public async Task HotSeatSession_FromTheStartOfTheGame_PlaysTheOpeningThroughTheEndpoints()
    {
        const string gameId = "game-start";
        var imported = BgaImporter.Import(Fixtures.Load(Fixtures.Elysium2p), new BgaImportOptions(MoveIndex: 0, ShuffleSeed: 7, GameId: gameId));
        await _store.CreateGameAsync(imported.State!);
        await _store.SaveMetaAsync(gameId, imported.Meta!);

        // Both seats choose at once, each from what they were dealt. StrandedKnight (seat 1) was dealt
        // Point Luna and PhoboLog.
        var opening = await GetTable(gameId);
        var legalMoves = (JObject)opening["legalMoves"]!;
        Assert.Equal(["0", "1"], legalMoves.Properties().Select(p => p.Name).Order());
        var setup = legalMoves["1"]!["setup"]!;
        var pointLuna = CardNameMap.Resolve("Point Luna")!;
        var phoboLog = CardNameMap.Resolve("PhoboLog")!;
        Assert.Equal([pointLuna, phoboLog], setup["availableCorporations"]!.Values<string>());
        Assert.Equal(4, setup["availablePreludes"]!.Count());
        Assert.Equal(10, setup["availableCards"]!.Count());
        Assert.Equal(2, setup.Value<int>("preludesToKeep"));
        Assert.Equal(3, setup.Value<int>("costPerCard"));
        Assert.Equal(38, setup["startingMegaCredits"]!.Value<int>(pointLuna));
        Assert.Equal("Point Luna", opening["cardNames"]!.Value<string>(pointLuna));

        // PhoboLog starts with 23 MC, which buys seven cards and not eight.
        var tooMany = new JObject
        {
            ["type"] = "Setup",
            ["playerId"] = 1,
            ["corporationId"] = phoboLog,
            ["preludeIds"] = new JArray(setup["availablePreludes"]!.Take(2)),
            ["cardIdsToBuy"] = new JArray(setup["availableCards"]!.Take(8)),
        };
        var refused = Body(await Function().SubmitMove(Post(tooMany.ToString()), gameId), HttpStatusCode.BadRequest);
        Assert.Contains("Phobolog starts with 23", refused.Value<string>("error"));

        var player = new RandomPlayer(seed: 3);
        var exchanges = new JArray { new JObject { ["table"] = opening } };
        var moveTypes = new List<string>();
        for (int i = 0; i < 30; i++)
        {
            var current = await _store.LoadStateAsync(gameId);
            if (player.NextMove(current) is not { } move)
                break;

            var moveJson = JsonConvert.SerializeObject(move, typeof(Move), Json);
            var table = Body(await Function().SubmitMove(Post(moveJson), gameId), HttpStatusCode.OK);

            moveTypes.Add(move.GetType().Name);
            exchanges.Add(new JObject { ["move"] = JObject.Parse(moveJson), ["table"] = table });
        }

        // The opening is two setup moves, then each player's two preludes, then the action phase.
        Assert.Equal([nameof(SetupMove), nameof(SetupMove)], moveTypes.Take(2));
        Assert.Equal(4, moveTypes.Count(t => t == nameof(PlayPreludeMove)));
        var final = await _store.LoadStateAsync(gameId);
        Assert.True(final.Phase is not (GamePhase.Setup or GamePhase.PreludePlacement), $"Still in {final.Phase}.");
        Assert.All(final.Players, p => Assert.False(string.IsNullOrEmpty(p.CorporationId)));

        if (Environment.GetEnvironmentVariable("TM_CONTRACT_FIXTURE_DIR") is { Length: > 0 } directory)
            File.WriteAllText(Path.Combine(directory, "game-start-session.json"), exchanges.ToString(Formatting.None));
    }

    private async Task<GameState> ImportGeneration4()
    {
        var log = Fixtures.Load(Fixtures.Elysium2p);
        var fork = Fixtures.ForkPoints(Fixtures.Elysium2p).ForkPoints.First(f => f.Generation == 4);
        var result = BgaImporter.Import(log, new BgaImportOptions(fork.MoveIndex, ShuffleSeed: 7, GameId: "game-1"));

        await _store.CreateGameAsync(result.State!);
        await _store.SaveMetaAsync(result.State!.GameId, result.Meta!);
        return result.State;
    }

    private async Task<JObject> GetTable(string gameId) =>
        Body(await Function().GetTable(new DefaultHttpContext().Request, gameId), HttpStatusCode.OK);

    private static HttpRequest Post(string body)
    {
        var request = new DefaultHttpContext().Request;
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return request;
    }

    private static JObject Body(IActionResult result, HttpStatusCode expectedStatus)
    {
        var content = Assert.IsType<ContentResult>(result);
        Assert.True((int)expectedStatus == content.StatusCode, $"Status {content.StatusCode}: {content.Content}");
        return JObject.Parse(content.Content!);
    }
}

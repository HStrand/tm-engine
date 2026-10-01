using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using tm_engine.Api;
using tm_engine.Serialization;
using tm_engine.Storage;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;

namespace TmEngine.BgaImport.Tests;

/// <summary>
/// The import endpoints, run against an in-memory store that serialises states the way blob storage does.
/// </summary>
public class ImportFunctionTests
{
    private static readonly JsonSerializerSettings Json = SerializationSettings.Create();

    private readonly InMemoryGameStore _store = new();
    private readonly FixtureLogSource _logs = new();

    private ImportFunction Function() => new(_store, _logs, new RequestLimiter(), Json, NullLogger<ImportFunction>.Instance);

    [Fact]
    public async Task ImportBgaGame_CreatesAPlayableGame_FromATableIdAndMove()
    {
        _logs.Add("824655675", Fixtures.Elysium2p);
        var fork = Fixtures.ForkPoints(Fixtures.Elysium2p).ForkPoints.First(f => f.Generation == 4);

        var response = await Function().ImportBgaGame(Post($$"""{"tableId":"824655675","moveIndex":{{fork.MoveIndex}},"seed":7}"""));

        var body = Body(response, HttpStatusCode.Created);
        var gameId = body.Value<string>("gameId")!;
        Assert.Equal(4, body["fork"]!.Value<int>("generation"));
        Assert.Equal("GenerationStart", body["fork"]!.Value<string>("kind"));

        // The stored state has been through JSON and still plays.
        var state = await _store.LoadStateAsync(gameId);
        Assert.Equal(7, state.ShuffleSeed);
        Assert.Equal(153, state.DrawPile.Count);
        Assert.NotNull(LegalMoveGenerator.GetLegalMoves(state, state.ActivePlayerId).Actions);
        Assert.Null(new RandomPlayer(seed: 1).Play(state, maxMoves: 300).Failure);

        var meta = (await _store.LoadMetaAsync(gameId))!;
        Assert.Equal("824655675", meta.Source.TableId);
        Assert.Equal(["cdman234", "StrandedKnight"], meta.Seats.Select(s => s.Name));
    }

    [Fact]
    public async Task ImportBgaGame_SnapsBackToTheNearestForkPoint()
    {
        _logs.Add("824655675", Fixtures.Elysium2p);
        // Two moves into generation 5's draft; the nearest position before it is the last one of generation 4.
        var forks = Fixtures.ForkPoints(Fixtures.Elysium2p).ForkPoints;
        var lastOfGeneration4 = forks.Last(f => f.Generation == 4);
        var inTheDraft = lastOfGeneration4.MoveIndex + 3;
        Assert.True(inTheDraft < forks.First(f => f.Generation == 5).MoveIndex);

        var response = await Function().ImportBgaGame(Post($$"""{"tableId":"824655675","moveIndex":{{inTheDraft}}}"""));

        Assert.Equal(lastOfGeneration4.MoveIndex, Body(response, HttpStatusCode.Created)["fork"]!.Value<int>("moveIndex"));
    }

    [Fact]
    public async Task ImportBgaGame_PicksUpTheMiddleOfGeneration1()
    {
        _logs.Add("824655675", Fixtures.Elysium2p);

        // Move index 12 is cdman234's second action of the game; he is about to play it at index 11.
        var response = await Function().ImportBgaGame(Post("""{"tableId":"824655675","moveIndex":12}"""));

        var body = Body(response, HttpStatusCode.Created);
        Assert.Equal(11, body["fork"]!.Value<int>("moveIndex"));
        Assert.Equal(1, body["fork"]!.Value<int>("generation"));

        var state = await _store.LoadStateAsync(body.Value<string>("gameId")!);
        Assert.Equal(GamePhase.Action, state.Phase);
        Assert.Equal(1, state.GetPlayer(state.ActivePlayerId).ActionsThisTurn);
        Assert.Null(new RandomPlayer(seed: 2).Play(state, maxMoves: 300).Failure);
    }

    [Fact]
    public async Task ImportBgaGame_RefusesAMoveBetweenForkPoints_WhenSnapIsOff()
    {
        _logs.Add("824655675", Fixtures.Elysium2p);

        var inTheDraft = Fixtures.ForkPoints(Fixtures.Elysium2p).ForkPoints.First(f => f.Generation == 5).MoveIndex - 3;

        var response = await Function().ImportBgaGame(Post($$"""{"tableId":"824655675","moveIndex":{{inTheDraft}},"snap":false}"""));

        var body = Body(response, HttpStatusCode.UnprocessableEntity);
        Assert.Contains(body["issues"]!, i => i.Value<string>("code") == "NOT_A_FORK_POINT");
        Assert.Empty(_store.GameIds);
    }

    [Fact]
    public async Task ImportBgaGame_ReturnsNotFound_ForATableThatWasNeverScraped()
    {
        var response = await Function().ImportBgaGame(Post("""{"tableId":"1","moveIndex":5}"""));

        Body(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetForkPoints_ListsWhereAGameCanBePlayedOnFrom()
    {
        _logs.Add("824655675", Fixtures.Elysium2p);

        var body = Body(await Function().GetForkPoints(new DefaultHttpContext().Request, "824655675"), HttpStatusCode.OK);

        Assert.True(body.Value<bool>("eligible"));
        Assert.Equal(Enumerable.Range(1, 10), body["forkPoints"]!.Select(f => f.Value<int>("generation")).Distinct());
        Assert.Equal("GameStart", body["forkPoints"]![0]!.Value<string>("kind"));
        Assert.Contains(body["forkPoints"]!, f => f.Value<string>("kind") == "ActionStart");
    }

    [Fact]
    public async Task GetForkPoints_SaysWhyAGameCannotBePlayedOn()
    {
        _logs.Add("829956648", Fixtures.MislabelledVastitas);

        var body = Body(await Function().GetForkPoints(new DefaultHttpContext().Request, "829956648"), HttpStatusCode.OK);

        Assert.False(body.Value<bool>("eligible"));
        Assert.Contains(body["issues"]!, i => i.Value<string>("code") == "UNSUPPORTED_MAP" && i.Value<string>("severity") == "Blocking");
    }

    [Fact]
    public async Task ImportBgaLog_AcceptsTheLogInTheRequestBody()
    {
        var fork = Fixtures.ForkPoints(Fixtures.Hellas2p).ForkPoints.First();
        var request = Post(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", Fixtures.Hellas2p)));
        request.QueryString = new QueryString($"?moveIndex={fork.MoveIndex}");

        var body = Body(await Function().ImportBgaLog(request), HttpStatusCode.Created);

        Assert.Equal(MapName.Hellas, (await _store.LoadStateAsync(body.Value<string>("gameId")!)).Map);
    }

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

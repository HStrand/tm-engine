using System.Collections.Concurrent;
using Newtonsoft.Json;
using tm_engine.Api;
using tm_engine.Serialization;
using tm_engine.Storage;
using TmEngine.Domain.Models;

namespace TmEngine.BgaImport.Tests;

/// <summary>Serves fixture logs in place of the scraper registry.</summary>
internal sealed class FixtureLogSource : IBgaLogSource
{
    private readonly Dictionary<string, string> _fixtures = [];

    public void Add(string tableId, string fixture) => _fixtures[tableId] = fixture;

    public Task<BgaGameLog?> FetchAsync(string tableId) =>
        Task.FromResult(_fixtures.TryGetValue(tableId, out var fixture) ? Fixtures.Load(fixture) : null);
}

/// <summary>Keeps states as JSON, so what comes back has round-tripped through the real serializer.</summary>
internal sealed class InMemoryGameStore : IGameStore
{
    private static readonly JsonSerializerSettings Json = SerializationSettings.Create();

    private readonly ConcurrentDictionary<(string GameId, int MoveNumber), string> _states = new();
    private readonly ConcurrentDictionary<string, string> _meta = new();

    public IEnumerable<string> GameIds => _states.Keys.Select(k => k.GameId).Distinct();

    public Task CreateGameAsync(GameState state) => SaveStateAsync(state.GameId, state);

    public Task SaveStateAsync(string gameId, GameState state)
    {
        _states[(gameId, state.MoveNumber)] = JsonConvert.SerializeObject(state, Json);
        return Task.CompletedTask;
    }

    public Task<GameState> LoadStateAsync(string gameId)
    {
        var moveNumbers = _states.Keys.Where(k => k.GameId == gameId).Select(k => k.MoveNumber).ToList();
        if (moveNumbers.Count == 0)
            throw new InvalidOperationException($"Game '{gameId}' not found.");
        return LoadStateAtAsync(gameId, moveNumbers.Max());
    }

    public Task<GameState> LoadStateAtAsync(string gameId, int moveNumber) =>
        _states.TryGetValue((gameId, moveNumber), out var json)
            ? Task.FromResult(JsonConvert.DeserializeObject<GameState>(json, Json)!)
            : throw new InvalidOperationException($"Game '{gameId}' has no move {moveNumber}.");

    public async Task<IReadOnlyList<GameState>> LoadAllStatesAsync(string gameId)
    {
        var states = new List<GameState>();
        foreach (var moveNumber in _states.Keys.Where(k => k.GameId == gameId).Select(k => k.MoveNumber).Order())
            states.Add(await LoadStateAtAsync(gameId, moveNumber));
        return states;
    }

    public Task SaveMetaAsync(string gameId, GameMeta meta)
    {
        _meta[gameId] = JsonConvert.SerializeObject(meta, Json);
        return Task.CompletedTask;
    }

    public Task<GameMeta?> LoadMetaAsync(string gameId) =>
        Task.FromResult(_meta.TryGetValue(gameId, out var json) ? JsonConvert.DeserializeObject<GameMeta>(json, Json) : null);
}

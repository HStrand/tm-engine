using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Newtonsoft.Json;
using TmEngine.BgaImport;
using TmEngine.Domain.Models;

namespace tm_engine.Storage;

public class BlobGameStore : IGameStore
{
    private const string ContainerName = "games";

    private readonly BlobContainerClient _container;
    private readonly JsonSerializerSettings _jsonSettings;
    private bool _containerEnsured;

    // Cache of the latest GameState per gameId. Every save also rewrites a small head blob, and a cache
    // hit is trusted only while the head's ETag is the one the cache saw: another instance (the app scales
    // out, and a deployment overlaps the old host with the new) may have moved the game on since. Bounded,
    // since games are created by visitors: the least recently used entries go once there are more than
    // MaxCached.
    private const int MaxCached = 500;
    private static readonly ConcurrentDictionary<string, (GameState State, ETag Head, long Touched)> _cache = new();
    private static long _clock;

    public BlobGameStore(BlobServiceClient blobService, JsonSerializerSettings jsonSettings)
    {
        _container = blobService.GetBlobContainerClient(ContainerName);
        _jsonSettings = jsonSettings;
    }

    public async Task<GameState> LoadStateAsync(string gameId)
    {
        await EnsureContainerAsync();

        var head = _container.GetBlobClient(HeadBlobName(gameId));
        var current = await HeadOf(head);
        if (_cache.TryGetValue(gameId, out var cached) && current is { } etag && cached.Head == etag)
        {
            _cache[gameId] = (cached.State, cached.Head, Interlocked.Increment(ref _clock));
            return cached.State;
        }

        // Games saved before the head blob existed are found by listing their moves.
        if (current is not null)
        {
            var headJson = (await head.DownloadContentAsync()).Value.Content.ToString();
            var moveNumber = JsonConvert.DeserializeObject<HeadRecord>(headJson)!.MoveNumber;
            var state = await LoadStateAtAsync(gameId, moveNumber);
            Remember(gameId, state, current.Value);
            return state;
        }

        var prefix = $"{gameId}/{gameId}_";
        var blobs = new List<BlobItem>();

        await foreach (var blob in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, CancellationToken.None))
        {
            blobs.Add(blob);
        }

        if (blobs.Count == 0)
            throw new InvalidOperationException($"Game '{gameId}' not found.");

        var latestBlob = blobs
            .OrderByDescending(b => ParseMoveNumber(b.Name, gameId))
            .First();

        var blobClient = _container.GetBlobClient(latestBlob.Name);
        var response = await blobClient.DownloadContentAsync();
        var json = response.Value.Content.ToString();
        var latest = JsonConvert.DeserializeObject<GameState>(json, _jsonSettings)!;

        Remember(gameId, latest, await WriteHead(gameId, latest.MoveNumber));
        return latest;
    }

    public async Task SaveStateAsync(string gameId, GameState state)
    {
        await EnsureContainerAsync();

        var blobName = $"{gameId}/{gameId}_{state.MoveNumber}.json";
        var blobClient = _container.GetBlobClient(blobName);

        var json = JsonConvert.SerializeObject(state, _jsonSettings);
        var content = new BinaryData(json);

        // Create-only: fails with 409 if blob already exists
        await blobClient.UploadAsync(content, new BlobUploadOptions
        {
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        });

        Remember(gameId, state, await WriteHead(gameId, state.MoveNumber));
    }

    public async Task CreateGameAsync(GameState state)
    {
        await EnsureContainerAsync();

        var blobName = $"{state.GameId}/{state.GameId}_{state.MoveNumber}.json";
        var blobClient = _container.GetBlobClient(blobName);

        var json = JsonConvert.SerializeObject(state, _jsonSettings);
        await blobClient.UploadAsync(new BinaryData(json));

        Remember(state.GameId, state, await WriteHead(state.GameId, state.MoveNumber));
    }

    public async Task<GameState> LoadStateAtAsync(string gameId, int moveNumber)
    {
        await EnsureContainerAsync();

        var blobClient = _container.GetBlobClient($"{gameId}/{gameId}_{moveNumber}.json");
        try
        {
            var response = await blobClient.DownloadContentAsync();
            return JsonConvert.DeserializeObject<GameState>(response.Value.Content.ToString(), _jsonSettings)!;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException($"Game '{gameId}' has no move {moveNumber}.");
        }
    }

    public async Task SaveMetaAsync(string gameId, GameMeta meta)
    {
        await EnsureContainerAsync();

        var json = JsonConvert.SerializeObject(meta, _jsonSettings);
        await _container.GetBlobClient(MetaBlobName(gameId)).UploadAsync(new BinaryData(json), overwrite: true);
    }

    public async Task<GameMeta?> LoadMetaAsync(string gameId)
    {
        await EnsureContainerAsync();

        try
        {
            var response = await _container.GetBlobClient(MetaBlobName(gameId)).DownloadContentAsync();
            return JsonConvert.DeserializeObject<GameMeta>(response.Value.Content.ToString(), _jsonSettings);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<GameState>> LoadAllStatesAsync(string gameId)
    {
        await EnsureContainerAsync();

        var names = new List<string>();
        await foreach (var blob in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{gameId}/{gameId}_", CancellationToken.None))
        {
            names.Add(blob.Name);
        }

        if (names.Count == 0)
            throw new InvalidOperationException($"Game '{gameId}' not found.");

        using var throttle = new SemaphoreSlim(16);
        var states = await Task.WhenAll(names.Select(async name =>
        {
            await throttle.WaitAsync();
            try
            {
                var response = await _container.GetBlobClient(name).DownloadContentAsync();
                return JsonConvert.DeserializeObject<GameState>(response.Value.Content.ToString(), _jsonSettings)!;
            }
            finally
            {
                throttle.Release();
            }
        }));

        return states.OrderBy(s => s.MoveNumber).ToList();
    }

    public async Task<GameState> RewindAsync(string gameId, int toMoveNumber)
    {
        await EnsureContainerAsync();
        var target = await LoadStateAtAsync(gameId, toMoveNumber);

        // Delete the later states first, then move the head: a crash in between leaves a game that is shorter
        // than intended, never a head pointing at a state that is gone.
        var prefix = $"{gameId}/{gameId}_";
        await foreach (var blob in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, CancellationToken.None))
        {
            if (ParseMoveNumber(blob.Name, gameId) > toMoveNumber)
                await _container.GetBlobClient(blob.Name).DeleteIfExistsAsync();
        }
        Remember(gameId, target, await WriteHead(gameId, toMoveNumber));
        return target;
    }

    private sealed record HeadRecord(int MoveNumber);

    private static string HeadBlobName(string gameId) => $"{gameId}/head.json";

    /// <summary>The head blob's ETag, or null when the game has none yet.</summary>
    private static async Task<ETag?> HeadOf(BlobClient head)
    {
        try
        {
            return (await head.GetPropertiesAsync()).Value.ETag;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task<ETag> WriteHead(string gameId, int moveNumber)
    {
        var json = JsonConvert.SerializeObject(new HeadRecord(moveNumber));
        var result = await _container.GetBlobClient(HeadBlobName(gameId)).UploadAsync(new BinaryData(json), overwrite: true);
        return result.Value.ETag;
    }

    private static void Remember(string gameId, GameState state, ETag head)
    {
        _cache[gameId] = (state, head, Interlocked.Increment(ref _clock));
        if (_cache.Count <= MaxCached) return;

        // Evict a tenth of the cache at a time rather than one entry per write.
        foreach (var key in _cache.OrderBy(e => e.Value.Touched).Take(MaxCached / 10).Select(e => e.Key).ToList())
            _cache.TryRemove(key, out _);
    }

    // Sits beside the state blobs; their "{gameId}_" prefix keeps it out of the state listing.
    private static string MetaBlobName(string gameId) => $"{gameId}/meta.json";

    private static int ParseMoveNumber(string blobName, string gameId)
    {
        // Blob name: "{gameId}/{gameId}_{moveNumber}.json"
        var fileName = blobName.Split('/').Last(); // "{gameId}_{moveNumber}.json"
        var suffix = fileName[(gameId.Length + 1)..]; // "{moveNumber}.json"
        var numberStr = suffix[..suffix.IndexOf('.')]; // "{moveNumber}"
        return int.Parse(numberStr);
    }

    private async Task EnsureContainerAsync()
    {
        if (_containerEnsured) return;
        await _container.CreateIfNotExistsAsync();
        _containerEnsured = true;
    }
}

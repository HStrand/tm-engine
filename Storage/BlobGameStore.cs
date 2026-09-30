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

    // Single-writer cache of the latest GameState per gameId. Writes are persisted to blob
    // storage as before; reads return the cached state on hit and fall back to blob on miss
    // (cold start, different instance). Safe under Functions' in-process model because per-game
    // API calls are effectively serialized by the bot loop / client.
    private static readonly ConcurrentDictionary<string, GameState> _cache = new();

    public BlobGameStore(BlobServiceClient blobService, JsonSerializerSettings jsonSettings)
    {
        _container = blobService.GetBlobContainerClient(ContainerName);
        _jsonSettings = jsonSettings;
    }

    public async Task<GameState> LoadStateAsync(string gameId)
    {
        if (_cache.TryGetValue(gameId, out var cached))
            return cached;

        await EnsureContainerAsync();

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
        var state = JsonConvert.DeserializeObject<GameState>(json, _jsonSettings)!;

        _cache[gameId] = state;
        return state;
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

        _cache[gameId] = state;
    }

    public async Task CreateGameAsync(GameState state)
    {
        await EnsureContainerAsync();

        var blobName = $"{state.GameId}/{state.GameId}_{state.MoveNumber}.json";
        var blobClient = _container.GetBlobClient(blobName);

        var json = JsonConvert.SerializeObject(state, _jsonSettings);
        await blobClient.UploadAsync(new BinaryData(json));

        _cache[state.GameId] = state;
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

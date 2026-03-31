using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Newtonsoft.Json;
using TmEngine.Domain.Models;

namespace tm_engine.Storage;

public class BlobGameStore : IGameStore
{
    private const string ContainerName = "games";

    private readonly BlobContainerClient _container;
    private readonly JsonSerializerSettings _jsonSettings;
    private bool _containerEnsured;

    public BlobGameStore(BlobServiceClient blobService, JsonSerializerSettings jsonSettings)
    {
        _container = blobService.GetBlobContainerClient(ContainerName);
        _jsonSettings = jsonSettings;
    }

    public async Task<GameState> LoadStateAsync(string gameId)
    {
        await EnsureContainerAsync();

        var prefix = $"{gameId}/{gameId}_";
        var blobs = new List<BlobItem>();

        await foreach (var blob in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, CancellationToken.None))
        {
            blobs.Add(blob);
        }

        if (blobs.Count == 0)
            throw new InvalidOperationException($"Game '{gameId}' not found.");

        // Find the blob with the highest move number
        var latestBlob = blobs
            .OrderByDescending(b => ParseMoveNumber(b.Name, gameId))
            .First();

        var blobClient = _container.GetBlobClient(latestBlob.Name);
        var response = await blobClient.DownloadContentAsync();
        var json = response.Value.Content.ToString();
        return JsonConvert.DeserializeObject<GameState>(json, _jsonSettings)!;
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
    }

    public async Task CreateGameAsync(GameState state)
    {
        await EnsureContainerAsync();

        var blobName = $"{state.GameId}/{state.GameId}_{state.MoveNumber}.json";
        var blobClient = _container.GetBlobClient(blobName);

        var json = JsonConvert.SerializeObject(state, _jsonSettings);
        await blobClient.UploadAsync(new BinaryData(json));
    }

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

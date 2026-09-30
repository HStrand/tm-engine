using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using TmEngine.BgaImport;

namespace tm_engine.Api;

/// <summary>Where scraped BGA game logs come from.</summary>
public interface IBgaLogSource
{
    /// <summary>The game log for a BGA table, or null when no such game has been scraped.</summary>
    Task<BgaGameLog?> FetchAsync(string tableId);
}

/// <summary>
/// Reads game logs from the scraper registry's API (the one the tfmstats replay viewer uses).
/// Configured with RegistryApiBase, e.g. "https://example.azurewebsites.net/api", and optionally RegistryApiKey.
/// </summary>
public class RegistryBgaLogSource : IBgaLogSource
{
    private static readonly HttpClient Http = new();

    public async Task<BgaGameLog?> FetchAsync(string tableId)
    {
        var baseUrl = Environment.GetEnvironmentVariable("RegistryApiBase");
        if (string.IsNullOrEmpty(baseUrl))
            throw new InvalidOperationException("RegistryApiBase is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/game-log/{Uri.EscapeDataString(tableId)}");
        var key = Environment.GetEnvironmentVariable("RegistryApiKey");
        if (!string.IsNullOrEmpty(key))
            request.Headers.Add("x-functions-key", key);

        using var response = await Http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        return BgaGameLog.Parse(stream);
    }
}

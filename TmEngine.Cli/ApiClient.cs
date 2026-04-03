using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace TmEngine.Cli;

public class ApiClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly JsonSerializerSettings _jsonSettings;

    public ApiClient(string baseUrl = "http://localhost:7102/api")
    {
        _baseUrl = baseUrl;
        _http = new HttpClient();
        var resolver = new CamelCasePropertyNamesContractResolver();
        resolver.NamingStrategy!.ProcessDictionaryKeys = false;

        _jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = resolver,
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter(new CamelCaseNamingStrategy()) },
            NullValueHandling = NullValueHandling.Ignore,
        };
    }

    public async Task<string> CreateGameAsync(CreateGameRequest request)
    {
        var json = JsonConvert.SerializeObject(request, _jsonSettings);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{_baseUrl}/games", content);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<CreateGameResponse>(body, _jsonSettings)!;
        return result.GameId;
    }

    public async Task<(GameStateDto State, Dictionary<string, string> CardNames)> GetGameStateAsync(
        string gameId, int playerId)
    {
        var response = await _http.GetAsync($"{_baseUrl}/games/{gameId}?playerId={playerId}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<GameStateResponse>(body, _jsonSettings)!;
        return (result.State, result.CardNames);
    }

    public async Task<(AvailableMovesDto Moves, Dictionary<string, string> CardNames)> GetLegalMovesAsync(
        string gameId, int playerId)
    {
        var response = await _http.GetAsync($"{_baseUrl}/games/{gameId}/legal-moves?playerId={playerId}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<LegalMovesResponse>(body, _jsonSettings)!;
        return (result.Moves, result.CardNames);
    }

    public async Task<SubmitMoveResponse> SubmitMoveAsync(string gameId, int playerId, JObject move)
    {
        var json = move.ToString(Formatting.None);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{_baseUrl}/games/{gameId}/moves?playerId={playerId}", content);
        var body = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<SubmitMoveResponse>(body, _jsonSettings)!;
    }

    public async Task<Dictionary<string, CardInfoDto>> GetGameCardsAsync(string gameId)
    {
        var response = await _http.GetAsync($"{_baseUrl}/games/{gameId}/cards");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<Dictionary<string, CardInfoDto>>(body, _jsonSettings)
            ?? new Dictionary<string, CardInfoDto>();
    }

    public async Task<List<string>> GetHistoryAsync(string gameId)
    {
        var response = await _http.GetAsync($"{_baseUrl}/games/{gameId}/history");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<HistoryResponse>(body, _jsonSettings)!;
        return result.Log;
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using StockChartFunctions.Models;

namespace StockChartFunctions.Services;

public class PolygonService(IHttpClientFactory httpClientFactory)
{
    private readonly HttpClient _http = httpClientFactory.CreateClient();
    private readonly string _apiKey = Environment.GetEnvironmentVariable("POLYGON_API_KEY")!;

    public async Task<List<PriceBar>> GetBarsAsync(string symbol, string from, string to)
    {
        var url = $"https://api.polygon.io/v2/aggs/ticker/{symbol}/range/1/day/{from}/{to}" +
                  $"?adjusted=true&sort=asc&limit=500&apiKey={_apiKey}";

        var res = await _http.GetAsync(url);
        if (!res.IsSuccessStatusCode) return [];

        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        var results = json.GetProperty("results");

        return results.EnumerateArray().Select(r => new PriceBar(
            Time:   r.GetProperty("t").GetInt64() / 1000,
            Open:   r.GetProperty("o").GetDouble(),
            High:   r.GetProperty("h").GetDouble(),
            Low:    r.GetProperty("l").GetDouble(),
            Close:  r.GetProperty("c").GetDouble(),
            Volume: r.GetProperty("v").GetDouble()
        )).ToList();
    }
}

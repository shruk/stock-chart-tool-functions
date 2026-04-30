using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StockChartFunctions.Models;

namespace StockChartFunctions.Services;

public class PolygonService(IHttpClientFactory httpClientFactory, ILogger<PolygonService> logger)
{
    private readonly HttpClient _http = httpClientFactory.CreateClient();
    private readonly string _apiKey = Environment.GetEnvironmentVariable("POLYGON_API_KEY")!;

    public async Task<List<PriceBar>> GetBarsAsync(string symbol, string from, string to)
    {
        var allBars = new List<PriceBar>();
        string? url = $"https://api.polygon.io/v2/aggs/ticker/{symbol}/range/1/day/{from}/{to}" +
                      $"?adjusted=true&sort=asc&limit=5000&apiKey={_apiKey}";

        while (url != null)
        {
            var res = await _http.GetAsync(url);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogError("Polygon API error for {Symbol}: {Status}", symbol, res.StatusCode);
                break;
            }

            var json = await res.Content.ReadFromJsonAsync<JsonElement>();

            if (json.TryGetProperty("results", out var results))
            {
                allBars.AddRange(results.EnumerateArray().Select(r => new PriceBar(
                    Ts:     DateOnly.FromDateTime(
                                DateTimeOffset.FromUnixTimeMilliseconds(r.GetProperty("t").GetInt64()).UtcDateTime),
                    Open:   r.GetProperty("o").GetDouble(),
                    High:   r.GetProperty("h").GetDouble(),
                    Low:    r.GetProperty("l").GetDouble(),
                    Close:  r.GetProperty("c").GetDouble(),
                    Volume: r.TryGetProperty("v", out var v) ? (long)v.GetDouble() : 0
                )));
            }

            // Follow pagination if more results exist
            url = json.TryGetProperty("next_url", out var next) && next.ValueKind != JsonValueKind.Null
                ? $"{next.GetString()}&apiKey={_apiKey}"
                : null;

            if (url != null) await Task.Delay(250);
        }

        return allBars;
    }
}

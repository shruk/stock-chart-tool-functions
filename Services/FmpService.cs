using System.Net.Http.Json;
using System.Text.Json;
using StockChartFunctions.Models;

namespace StockChartFunctions.Services;

public class FmpService(IHttpClientFactory httpClientFactory)
{
    private readonly HttpClient _http = httpClientFactory.CreateClient();
    private readonly string _apiKey = Environment.GetEnvironmentVariable("FMP_API_KEY") ?? "";

    public async Task<List<PriceBar>> GetBarsAsync(string symbol, string from, string to)
    {
        if (string.IsNullOrEmpty(_apiKey)) return [];

        var url = $"https://financialmodelingprep.com/stable/historical-price-eod/full?symbol={symbol}&from={from}&to={to}&apikey={_apiKey}";
        var res = await _http.GetAsync(url);
        if (!res.IsSuccessStatusCode) return [];

        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        if (json.ValueKind != JsonValueKind.Array) return [];

        return json.EnumerateArray()
            .Select(r => new PriceBar(
                Ts:     DateOnly.Parse(r.GetProperty("date").GetString()!),
                Open:   r.GetProperty("open").GetDouble(),
                High:   r.GetProperty("high").GetDouble(),
                Low:    r.GetProperty("low").GetDouble(),
                Close:  r.GetProperty("close").GetDouble(),
                Volume: (long)r.GetProperty("volume").GetDouble()
            ))
            .OrderBy(b => b.Ts)
            .ToList();
    }

    public async Task<PriceTarget?> GetPriceTargetAsync(string symbol)
    {
        if (string.IsNullOrEmpty(_apiKey)) return null;

        var url = $"https://financialmodelingprep.com/stable/price-target-consensus?symbol={symbol}&apikey={_apiKey}";
        var res = await _http.GetAsync(url);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        var d = json.ValueKind == JsonValueKind.Array ? json[0] : json;

        var mean = GetDouble(d, "targetConsensus") ?? GetDouble(d, "targetMean");
        if (mean is null) return null;

        return new PriceTarget(
            TargetHigh:   GetDouble(d, "targetHigh") ?? 0,
            TargetLow:    GetDouble(d, "targetLow") ?? 0,
            TargetMean:   mean.Value,
            TargetMedian: GetDouble(d, "targetMedian") ?? mean.Value,
            LastUpdated:  GetString(d, "lastUpdated") ?? "");
    }

    private static double? GetDouble(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static string? GetString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

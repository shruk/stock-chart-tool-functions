using System.Net.Http.Json;
using System.Text.Json;
using StockChartFunctions.Models;

namespace StockChartFunctions.Services;

public class FinnhubService(IHttpClientFactory httpClientFactory)
{
    private readonly HttpClient _http = httpClientFactory.CreateClient();
    private readonly string _apiKey = Environment.GetEnvironmentVariable("FINNHUB_API_KEY") ?? "";
    private const string Base = "https://finnhub.io/api/v1";

    public async Task<(RecommendationTrend? Rec, KeyMetrics? Metrics, CompanyProfile? Profile)> GetAnalystDataAsync(string symbol)
    {
        if (string.IsNullOrEmpty(_apiKey)) return (null, null, null);

        var p = $"symbol={symbol}&token={_apiKey}";

        var recTask     = _http.GetAsync($"{Base}/stock/recommendation?{p}");
        var metricTask  = _http.GetAsync($"{Base}/stock/metric?{p}&metric=all");
        var profileTask = _http.GetAsync($"{Base}/stock/profile2?{p}");

        await Task.WhenAll(recTask, metricTask, profileTask);

        var rec     = await ParseRecommendation(await recTask);
        var metrics = await ParseMetrics(await metricTask);
        var profile = await ParseProfile(await profileTask);

        return (rec, metrics, profile);
    }

    private static async Task<RecommendationTrend?> ParseRecommendation(HttpResponseMessage res)
    {
        if (!res.IsSuccessStatusCode) return null;
        var arr = await res.Content.ReadFromJsonAsync<JsonElement[]>();
        if (arr is null || arr.Length == 0) return null;
        var r = arr[0];
        return new RecommendationTrend(
            Buy:       r.GetProperty("buy").GetInt32(),
            Hold:      r.GetProperty("hold").GetInt32(),
            Period:    r.GetProperty("period").GetString() ?? "",
            Sell:      r.GetProperty("sell").GetInt32(),
            StrongBuy: r.GetProperty("strongBuy").GetInt32(),
            StrongSell:r.GetProperty("strongSell").GetInt32());
    }

    private static async Task<KeyMetrics?> ParseMetrics(HttpResponseMessage res)
    {
        if (!res.IsSuccessStatusCode) return null;
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("metric", out var m)) return null;

        return new KeyMetrics(
            Week52High:       GetDouble(m, "52WeekHigh") ?? 0,
            Week52Low:        GetDouble(m, "52WeekLow") ?? 0,
            Week52HighDate:   GetString(m, "52WeekHighDate"),
            Week52LowDate:    GetString(m, "52WeekLowDate"),
            PeRatio:          GetDouble(m, "peBasicExclExtraTTM") ?? GetDouble(m, "peTTM"),
            MarketCap:        GetDouble(m, "marketCapitalization"),
            Beta:             GetDouble(m, "beta"),
            DividendYield:    GetDouble(m, "dividendYieldIndicatedAnnual"),
            Eps:              GetDouble(m, "epsTTM"),
            RevenueGrowthYoy: GetDouble(m, "revenueGrowthTTMYoy"),
            RoeTTM:           GetDouble(m, "roeTTM"),
            CurrentRatio:     GetDouble(m, "currentRatioQuarterly"));
    }

    private static async Task<CompanyProfile?> ParseProfile(HttpResponseMessage res)
    {
        if (!res.IsSuccessStatusCode) return null;
        var p = await res.Content.ReadFromJsonAsync<JsonElement>();
        return new CompanyProfile(
            Name:                  GetString(p, "name") ?? "",
            Ticker:                GetString(p, "ticker") ?? "",
            Exchange:              GetString(p, "exchange") ?? "",
            Industry:              GetString(p, "finnhubIndustry") ?? "",
            Sector:                GetString(p, "sector"),
            MarketCapitalization:  GetDouble(p, "marketCapitalization") ?? 0,
            Logo:                  GetString(p, "logo") ?? "",
            WebUrl:                GetString(p, "weburl") ?? "",
            Country:               GetString(p, "country") ?? "",
            Currency:              GetString(p, "currency") ?? "",
            Ipo:                   GetString(p, "ipo") ?? "");
    }

    private static double? GetDouble(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static string? GetString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

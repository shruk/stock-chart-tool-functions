using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StockChartFunctions.Models;

namespace StockChartFunctions.Services;

public class SupabaseService(IHttpClientFactory httpClientFactory, ILogger<SupabaseService> logger)
{
    private readonly string _url = Environment.GetEnvironmentVariable("SUPABASE_URL")!;
    private readonly string _key = Environment.GetEnvironmentVariable("SUPABASE_SERVICE_KEY")!;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly JsonSerializerOptions _camelJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("apikey", _key);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_key}");
        return client;
    }

    public async Task<DateOnly?> GetLatestBarDateAsync(string symbol)
    {
        var client = CreateClient();
        var url = $"{_url}/rest/v1/price_bars?symbol=eq.{symbol.ToUpper()}&select=ts&order=ts.desc&limit=1";
        var rows = await client.GetFromJsonAsync<List<TsRow>>(url, _json);
        if (rows == null || rows.Count == 0) return null;
        return DateOnly.Parse(rows[0].Ts);
    }

    public async Task SavePriceBarsAsync(string symbol, List<PriceBar> bars)
    {
        if (bars.Count == 0) return;

        var sym = symbol.ToUpper();
        var rows = bars.Select(b => new
        {
            symbol = sym,
            ts     = b.Ts.ToString("yyyy-MM-dd"),
            open   = b.Open,
            high   = b.High,
            low    = b.Low,
            close  = b.Close,
            volume = b.Volume
        }).ToList();

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Prefer", "resolution=merge-duplicates");

        var url = $"{_url}/rest/v1/price_bars";
        var response = await client.PostAsJsonAsync(url, rows);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            logger.LogError("SavePriceBars failed for {Symbol}: {Status} {Body}", symbol, response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task<List<string>> GetSymbolsAsync()
    {
        var client = CreateClient();
        var url = $"{_url}/rest/v1/symbols?select=symbol&order=symbol";
        var rows = await client.GetFromJsonAsync<List<SymbolRow>>(url, _json);
        return rows?.Select(r => r.Symbol).ToList() ?? [];
    }

    public async Task<List<SymbolStat>> GetSymbolStatsAsync()
    {
        var client = CreateClient();
        var url = $"{_url}/rest/v1/rpc/get_symbol_stats";
        var rows = await client.PostAsJsonAsync(url, new { });
        var stats = await rows.Content.ReadFromJsonAsync<List<SymbolStatRow>>(_json);
        return stats?.Select(r => new SymbolStat(r.Symbol, r.BarCount, r.FromDate, r.ToDate, r.HasAnalyst, r.Type)).ToList() ?? [];
    }

    public async Task<bool> IsAnalystDataFreshAsync(string symbol)
    {
        var client = CreateClient();
        var url = $"{_url}/rest/v1/analyst_cache?symbol=eq.{symbol.ToUpper()}&select=cached_at";
        var rows = await client.GetFromJsonAsync<List<CachedAtRow>>(url, _json);
        if (rows == null || rows.Count == 0) return false;
        var cachedAt = DateTime.Parse(rows[0].CachedAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
        return (DateTime.UtcNow - cachedAt).TotalHours < 23;
    }

    public async Task AddSymbolAsync(string symbol)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Prefer", "resolution=ignore-duplicates");
        var url = $"{_url}/rest/v1/symbols";
        await client.PostAsJsonAsync(url, new { symbol = symbol.ToUpper() });
    }

    public async Task UpdateSymbolTypeAsync(string symbol, string type)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Prefer", "return=minimal");
        var url = $"{_url}/rest/v1/symbols?symbol=eq.{symbol.ToUpper()}";
        await client.PatchAsJsonAsync(url, new { type });
    }

    public async Task DeleteDataAsync(string symbol)
    {
        var sym = symbol.ToUpper();
        var client = CreateClient();

        var priceRes = await client.DeleteAsync($"{_url}/rest/v1/price_bars?symbol=eq.{sym}");
        if (!priceRes.IsSuccessStatusCode)
            logger.LogError("DeleteData price_bars failed for {Symbol}: {Status}", sym, priceRes.StatusCode);

        var analystRes = await client.DeleteAsync($"{_url}/rest/v1/analyst_cache?symbol=eq.{sym}");
        if (!analystRes.IsSuccessStatusCode)
            logger.LogError("DeleteData analyst_cache failed for {Symbol}: {Status}", sym, analystRes.StatusCode);
    }

    public async Task DeleteSymbolAsync(string symbol)
    {
        await DeleteDataAsync(symbol);

        var sym = symbol.ToUpper();
        var client = CreateClient();
        var symbolRes = await client.DeleteAsync($"{_url}/rest/v1/symbols?symbol=eq.{sym}");
        if (!symbolRes.IsSuccessStatusCode)
            logger.LogError("DeleteSymbol symbols failed for {Symbol}: {Status}", sym, symbolRes.StatusCode);
    }

    public async Task SaveAnalystCacheAsync(string symbol, AnalystData data)
    {
        var row = new
        {
            symbol        = symbol.ToUpper(),
            data          = JsonSerializer.SerializeToElement(data, _camelJson),
            cached_at     = DateTime.UtcNow.ToString("o"),
            next_earnings = data.NextEarnings?.ToString("yyyy-MM-dd")
        };

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Prefer", "resolution=merge-duplicates");

        var url = $"{_url}/rest/v1/analyst_cache";
        var response = await client.PostAsJsonAsync(url, row, _json);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            logger.LogError("SaveAnalystCache failed for {Symbol}: {Status} {Body}", symbol, response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task<MarketSummaryResult?> GetLatestMarketSummaryAsync()
    {
        var client = CreateClient();
        var url = $"{_url}/rest/v1/market_summary?select=content,content_zh&order=generated_at.desc&limit=1";
        var rows = await client.GetFromJsonAsync<List<MarketSummaryRow>>(url, _json);
        var row = rows?.FirstOrDefault();
        if (row is null) return null;
        return new MarketSummaryResult(row.Content, row.ContentZh);
    }

    public async Task SaveMarketSummaryAsync(string content, string? contentZh)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Prefer", "return=minimal");
        var url = $"{_url}/rest/v1/market_summary";
        var response = await client.PostAsJsonAsync(url, new
        {
            content,
            content_zh   = contentZh,
            generated_at = DateTime.UtcNow.ToString("o")
        });
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            logger.LogError("SaveMarketSummary failed: {Status} {Body}", response.StatusCode, body);
        }
    }

    public async Task<JobStatusResult> GetJobStatusAsync()
    {
        var client = CreateClient();
        var summaryTask = client.GetFromJsonAsync<List<GeneratedAtRow>>(
            $"{_url}/rest/v1/market_summary?select=generated_at&order=generated_at.desc&limit=1", _json);
        var riskTask = client.GetFromJsonAsync<List<RiskCalcAtRow>>(
            $"{_url}/rest/v1/symbol_risk?select=calculated_at&order=calculated_at.desc&limit=1", _json);
        var priceTask = client.GetFromJsonAsync<List<TsRow>>(
            $"{_url}/rest/v1/price_bars?select=ts&order=ts.desc&limit=1", _json);
        await Task.WhenAll(summaryTask, riskTask, priceTask);
        return new JobStatusResult(
            priceTask.Result?.FirstOrDefault()?.Ts,
            summaryTask.Result?.FirstOrDefault()?.GeneratedAt,
            riskTask.Result?.FirstOrDefault()?.CalculatedAt);
    }

    public async Task<List<double>> GetClosePricesAsync(string symbol)
    {
        var client = CreateClient();
        var url = $"{_url}/rest/v1/price_bars?symbol=eq.{symbol.ToUpper()}&select=close&order=ts.asc";
        var rows = await client.GetFromJsonAsync<List<CloseRow>>(url, _json);
        return rows?.Select(r => r.Close).ToList() ?? [];
    }

    public async Task SaveRiskResultAsync(string symbol, RiskResult r)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Prefer", "resolution=merge-duplicates");
        var url = $"{_url}/rest/v1/symbol_risk";
        var response = await client.PostAsJsonAsync(url, new
        {
            symbol        = symbol.ToUpper(),
            loss_prob_2w  = r.TwoWeek.LossProbability,
            var95_2w      = r.TwoWeek.VaR95,
            loss_prob_1m  = r.OneMonth.LossProbability,
            var95_1m      = r.OneMonth.VaR95,
            loss_prob_3m  = r.ThreeMonth.LossProbability,
            var95_3m      = r.ThreeMonth.VaR95,
            loss_prob_6m  = r.SixMonth.LossProbability,
            var95_6m      = r.SixMonth.VaR95,
            calculated_at = DateTime.UtcNow.ToString("o")
        });
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            logger.LogError("SaveRiskResult failed for {Symbol}: {Status} {Body}", symbol, response.StatusCode, body);
        }
    }

    public async Task<RiskRow?> GetRiskResultAsync(string symbol)
    {
        var client = CreateClient();
        var url = $"{_url}/rest/v1/symbol_risk?symbol=eq.{symbol.ToUpper()}&select=*&limit=1";
        var rows = await client.GetFromJsonAsync<List<RiskRow>>(url, _json);
        return rows?.FirstOrDefault();
    }

    public async Task SaveRiskSummaryAsync(string symbol, string summary)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Prefer", "return=minimal");
        var url = $"{_url}/rest/v1/symbol_risk?symbol=eq.{symbol.ToUpper()}";
        await client.PatchAsJsonAsync(url, new { risk_summary = summary });
    }

    public async Task SaveFullAnalysisAsync(string symbol, string analysis)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Prefer", "return=minimal");
        var url = $"{_url}/rest/v1/symbol_risk?symbol=eq.{symbol.ToUpper()}";
        await client.PatchAsJsonAsync(url, new
        {
            full_analysis    = analysis,
            full_analysis_at = DateTime.UtcNow.ToString("o")
        });
    }

    public async Task<double?> GetLatestCloseAsync(string symbol)
    {
        var client = CreateClient();
        var url = $"{_url}/rest/v1/price_bars?symbol=eq.{symbol.ToUpper()}&select=close&order=ts.desc&limit=1";
        var rows = await client.GetFromJsonAsync<List<CloseRow>>(url, _json);
        return rows?.FirstOrDefault()?.Close;
    }

    public async Task<JsonElement?> GetAnalystCacheRawAsync(string symbol)
    {
        var client = CreateClient();
        var url = $"{_url}/rest/v1/analyst_cache?symbol=eq.{symbol.ToUpper()}&select=data&limit=1";
        var rows = await client.GetFromJsonAsync<List<AnalystCacheRow>>(url, _json);
        return rows?.FirstOrDefault()?.Data;
    }

    private record AnalystCacheRow([property: JsonPropertyName("data")] JsonElement Data);
    private record TsRow(string Ts);
    private record SymbolRow(string Symbol);
    private record CachedAtRow([property: JsonPropertyName("cached_at")] string CachedAt);
    private record MarketSummaryRow(
        string Content,
        [property: JsonPropertyName("content_zh")] string? ContentZh);
    public record MarketSummaryResult(string Content, string? ContentZh);
    private record GeneratedAtRow([property: JsonPropertyName("generated_at")] string GeneratedAt);
    private record RiskCalcAtRow([property: JsonPropertyName("calculated_at")] string CalculatedAt);
    public record JobStatusResult(string? LatestPriceBarDate, string? LatestMarketSummaryAt, string? LatestRiskCalculatedAt);
    private record CloseRow(double Close);
    public record RiskRow(
        [property: JsonPropertyName("loss_prob_2w")]     double LossProb2W,
        [property: JsonPropertyName("var95_2w")]         double Var95_2W,
        [property: JsonPropertyName("loss_prob_1m")]     double LossProb1M,
        [property: JsonPropertyName("var95_1m")]         double Var95_1M,
        [property: JsonPropertyName("loss_prob_3m")]     double LossProb3M,
        [property: JsonPropertyName("var95_3m")]         double Var95_3M,
        [property: JsonPropertyName("loss_prob_6m")]     double LossProb6M,
        [property: JsonPropertyName("var95_6m")]         double Var95_6M,
        [property: JsonPropertyName("calculated_at")]    string CalculatedAt,
        [property: JsonPropertyName("risk_summary")]     string? RiskSummary = null,
        [property: JsonPropertyName("full_analysis")]    string? FullAnalysis = null,
        [property: JsonPropertyName("full_analysis_at")] string? FullAnalysisAt = null);
    private record SymbolStatRow(
        string Symbol,
        [property: JsonPropertyName("bar_count")]  long BarCount,
        [property: JsonPropertyName("from_date")]  string FromDate,
        [property: JsonPropertyName("to_date")]    string ToDate,
        [property: JsonPropertyName("has_analyst")] bool HasAnalyst,
        string Type = "stock");
}

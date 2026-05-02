using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using StockChartFunctions.Models;
using StockChartFunctions.Services;

namespace StockChartFunctions.Functions;

public class AnalyzeFunctions(
    ILogger<AnalyzeFunctions> logger,
    SupabaseService supabase,
    ClaudeService claude)
{
    private static readonly JsonSerializerOptions _camelJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Function("GetSymbolAnalysis")]
    public async Task<HttpResponseData> GetSymbolAnalysis(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analyze/{symbol}")] HttpRequestData req,
        string symbol)
    {
        var sym = symbol.ToUpper();
        var response = req.CreateResponse(HttpStatusCode.OK);

        var risk = await supabase.GetRiskResultAsync(sym);

        // Return cached analysis if fresh (< 24 hours)
        if (risk?.FullAnalysis != null && risk.FullAnalysisAt != null)
        {
            var cachedAt = DateTime.Parse(risk.FullAnalysisAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
            if ((DateTime.UtcNow - cachedAt).TotalHours < 24)
            {
                await response.WriteAsJsonAsync(new { analysis = risk.FullAnalysis, cached = true });
                return response;
            }
        }

        // Fetch all data in parallel
        var analystTask = supabase.GetAnalystCacheRawAsync(sym);
        var priceTask   = supabase.GetLatestCloseAsync(sym);
        await Task.WhenAll(analystTask, priceTask);

        AnalystData? analyst = null;
        if (analystTask.Result.HasValue)
        {
            try
            {
                analyst = JsonSerializer.Deserialize<AnalystData>(analystTask.Result.Value.GetRawText(), _camelJson);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deserialize analyst data for {Symbol}", sym);
            }
        }

        var analysis = await claude.GenerateFullAnalysisAsync(sym, analyst, risk, priceTask.Result);
        if (analysis == null)
        {
            response = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            await response.WriteStringAsync("AI analysis unavailable");
            return response;
        }

        await supabase.SaveFullAnalysisAsync(sym, analysis);

        await response.WriteAsJsonAsync(new { analysis, cached = false });
        return response;
    }
}

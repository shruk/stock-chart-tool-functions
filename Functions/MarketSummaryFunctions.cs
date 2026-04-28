using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using StockChartFunctions.Services;

namespace StockChartFunctions.Functions;

public class MarketSummaryFunctions(
    ILogger<MarketSummaryFunctions> logger,
    SupabaseService supabase,
    ClaudeService claude)
{
    // Runs daily at 10 PM UTC (6 PM ET, after market close)
    [Function("GenerateMarketSummary")]
    public async Task GenerateMarketSummary([TimerTrigger("0 0 22 * * *")] TimerInfo timer)
    {
        logger.LogInformation("Generating market summary (EN + ZH)");

        var enTask = claude.GenerateMarketSummaryAsync();
        var zhTask = claude.GenerateMarketSummaryZhAsync();
        await Task.WhenAll(enTask, zhTask);
        var en = enTask.Result;
        var zh = zhTask.Result;

        if (en is null)
        {
            logger.LogError("Claude returned no EN content — skipping save");
            return;
        }

        await supabase.SaveMarketSummaryAsync(en, zh);
        logger.LogInformation("Market summary saved (EN: {EnLen} chars, ZH: {ZhLen} chars)", en.Length, zh?.Length ?? 0);
    }

    [Function("GetMarketSummary")]
    public async Task<HttpResponseData> GetMarketSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "market-summary")] HttpRequestData req)
    {
        var result = await supabase.GetLatestMarketSummaryAsync();
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { content = result?.Content, contentZh = result?.ContentZh });
        return response;
    }
}

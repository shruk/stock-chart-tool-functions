using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using StockChartFunctions.Services;

namespace StockChartFunctions.Functions;

public class JobFunctions(
    ILogger<JobFunctions> logger,
    SupabaseService supabase,
    ClaudeService claude,
    RiskCalculator riskCalculator,
    StockFetchService fetchSvc)
{
    private static volatile bool _fetchRunning = false;
    private static DateTime _fetchStartedAt = DateTime.MinValue;

    [Function("GetJobStatus")]
    public async Task<HttpResponseData> GetJobStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "jobs/status")] HttpRequestData req)
    {
        var status = await supabase.GetJobStatusAsync();
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            fetchStockData = status.LatestPriceBarDate,
            marketSummary  = status.LatestMarketSummaryAt,
            calculateRisks = status.LatestRiskCalculatedAt,
            fetchStockDataRunning = _fetchRunning,
            fetchStockDataStartedAt = _fetchRunning ? _fetchStartedAt.ToString("O") : (string?)null,
        });
        return response;
    }

    [Function("RunJob")]
    public async Task<HttpResponseData> RunJob(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/run/{job}")] HttpRequestData req,
        string job)
    {
        logger.LogInformation("Manual trigger: {Job}", job);
        var response = req.CreateResponse(HttpStatusCode.OK);

        switch (job.ToLowerInvariant())
        {
            case "market-summary":
                var enTask = claude.GenerateMarketSummaryAsync();
                var zhTask = claude.GenerateMarketSummaryZhAsync();
                await Task.WhenAll(enTask, zhTask);
                if (enTask.Result is null)
                {
                    response = req.CreateResponse(HttpStatusCode.InternalServerError);
                    await response.WriteAsJsonAsync(new { error = "Claude returned no content" });
                    return response;
                }
                await supabase.SaveMarketSummaryAsync(enTask.Result, zhTask.Result);
                await response.WriteAsJsonAsync(new { status = "ok", message = "Market summary generated" });
                break;

            case "calculate-risks":
                var symbols = await supabase.GetSymbolsAsync();
                int done = 0;
                foreach (var sym in symbols)
                {
                    var prices = await supabase.GetClosePricesAsync(sym);
                    if (prices.Count < 30) continue;
                    var result = riskCalculator.CalculateRisk(prices);
                    await supabase.SaveRiskResultAsync(sym, result);
                    var summary = await claude.GenerateRiskSummaryAsync(sym, result);
                    if (summary != null)
                        await supabase.SaveRiskSummaryAsync(sym, summary);
                    done++;
                }
                await response.WriteAsJsonAsync(new { status = "ok", message = $"Risk calculated for {done} symbols" });
                break;

            case "fetch-stock-data":
                if (_fetchRunning)
                {
                    response = req.CreateResponse(HttpStatusCode.Conflict);
                    await response.WriteAsJsonAsync(new { error = "Job already running" });
                    return response;
                }
                _fetchRunning = true;
                _fetchStartedAt = DateTime.UtcNow;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var syms = await supabase.GetSymbolsAsync();
                        for (int i = 0; i < syms.Count; i++)
                        {
                            try
                            {
                                await fetchSvc.BackfillPricesAsync(syms[i]);
                                await fetchSvc.RefreshAnalystAsync(syms[i]);
                            }
                            catch (Exception ex) { logger.LogError(ex, "Error processing {Symbol}", syms[i]); }

                            if ((i + 1) % 5 == 0 && i < syms.Count - 1)
                                await Task.Delay(TimeSpan.FromSeconds(60));
                            else
                                await Task.Delay(1000);
                        }
                        logger.LogInformation("Background FetchStockData complete");
                    }
                    finally
                    {
                        _fetchRunning = false;
                    }
                });
                response = req.CreateResponse(HttpStatusCode.Accepted);
                await response.WriteAsJsonAsync(new { status = "accepted", message = "Stock fetch started in background" });
                break;

            default:
                response = req.CreateResponse(HttpStatusCode.BadRequest);
                await response.WriteAsJsonAsync(new { error = $"Unknown job: {job}" });
                break;
        }

        return response;
    }
}

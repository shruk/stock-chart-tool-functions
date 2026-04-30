using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using StockChartFunctions.Services;

namespace StockChartFunctions.Functions;

public class RiskFunctions(
    ILogger<RiskFunctions> logger,
    SupabaseService supabase,
    RiskCalculator calculator)
{
    // Runs daily at 11 PM UTC (after market close + market summary)
    [Function("CalculateAllRisks")]
    public async Task CalculateAllRisks([TimerTrigger("0 0 23 * * *")] TimerInfo timer)
    {
        var symbols = await supabase.GetSymbolsAsync();
        logger.LogInformation("Calculating risk for {Count} symbols", symbols.Count);

        foreach (var symbol in symbols)
        {
            var prices = await supabase.GetClosePricesAsync(symbol);
            if (prices.Count < 30)
            {
                logger.LogWarning("Skipping {Symbol}: only {Count} bars", symbol, prices.Count);
                continue;
            }

            var result = calculator.CalculateRisk(prices);
            await supabase.SaveRiskResultAsync(symbol, result);
            logger.LogInformation(
                "{Symbol}: 2W={P2W:P1} | 1M={P1M:P1} | 3M={P3M:P1} | 6M={P6M:P1}",
                symbol,
                result.TwoWeek.LossProbability, result.OneMonth.LossProbability,
                result.ThreeMonth.LossProbability, result.SixMonth.LossProbability);
        }
    }

    [Function("GetSymbolRisk")]
    public async Task<HttpResponseData> GetSymbolRisk(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "risk/{symbol}")] HttpRequestData req,
        string symbol)
    {
        var risk = await supabase.GetRiskResultAsync(symbol);
        var response = req.CreateResponse(HttpStatusCode.OK);
        if (risk is null)
        {
            await response.WriteAsJsonAsync(new { });
        }
        else
        {
            await response.WriteAsJsonAsync(new
            {
                twoWeek    = new { lossProbability = risk.LossProb2W, var95 = risk.Var95_2W },
                oneMonth   = new { lossProbability = risk.LossProb1M, var95 = risk.Var95_1M },
                threeMonth = new { lossProbability = risk.LossProb3M, var95 = risk.Var95_3M },
                sixMonth   = new { lossProbability = risk.LossProb6M, var95 = risk.Var95_6M },
                calculatedAt = risk.CalculatedAt,
            });
        }
        return response;
    }
}

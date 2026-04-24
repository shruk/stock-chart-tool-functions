using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using StockChartFunctions.Models;
using StockChartFunctions.Services;

namespace StockChartFunctions.Functions;

public class FetchStockData(
    ILogger<FetchStockData> logger,
    PolygonService polygon,
    FinnhubService finnhub,
    FmpService fmp,
    SupabaseService supabase)
{
    private static readonly string[] Watchlist = ["AAPL", "MSFT", "GOOGL", "AMZN", "TSLA"];

    [Function("FetchStockData")]
    public async Task Run([TimerTrigger("0 0 * * * *")] TimerInfo timer)
    {
        logger.LogInformation("FetchStockData started at {Time}", DateTime.UtcNow);

        var to   = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var from = DateTime.UtcNow.AddDays(-90).ToString("yyyy-MM-dd");

        foreach (var symbol in Watchlist)
        {
            try
            {
                await FetchPricesAsync(symbol, from, to);
                await FetchAnalystAsync(symbol);
                await Task.Delay(1000); // stagger to respect rate limits
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing {Symbol}", symbol);
            }
        }

        logger.LogInformation("FetchStockData completed at {Time}", DateTime.UtcNow);
    }

    private async Task FetchPricesAsync(string symbol, string from, string to)
    {
        var bars = await polygon.GetBarsAsync(symbol, from, to);
        if (bars.Count == 0)
        {
            logger.LogWarning("No price bars returned for {Symbol}", symbol);
            return;
        }

        await supabase.SavePriceCacheAsync(symbol, "3M", bars);
        logger.LogInformation("✓ Prices saved: {Symbol} ({Count} bars)", symbol, bars.Count);
    }

    private async Task FetchAnalystAsync(string symbol)
    {
        var (rec, metrics, profile) = await finnhub.GetAnalystDataAsync(symbol);
        var priceTarget = await fmp.GetPriceTargetAsync(symbol);

        var data = new AnalystData(rec, priceTarget, metrics, profile);
        await supabase.SaveAnalystCacheAsync(symbol, data);
        logger.LogInformation("✓ Analyst saved: {Symbol}", symbol);
    }
}

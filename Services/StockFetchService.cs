using Microsoft.Extensions.Logging;
using StockChartFunctions.Models;

namespace StockChartFunctions.Services;

public class StockFetchService(
    ILogger<StockFetchService> logger,
    PolygonService polygon,
    FinnhubService finnhub,
    FmpService fmp,
    SupabaseService supabase)
{
    public async Task<int> BackfillPricesAsync(string symbol)
    {
        var latestDate = await supabase.GetLatestBarDateAsync(symbol);
        var to = DateOnly.FromDateTime(DateTime.UtcNow);

        List<PriceBar> bars;

        if (latestDate == null)
        {
            // First run: prefer FMP (~7 years free), fall back to Polygon (~2 years free) for ETFs/unsupported
            var from = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10));
            logger.LogInformation("First run: backfilling {Symbol} from {From} via FMP", symbol, from);
            bars = await fmp.GetBarsAsync(symbol, from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"));

            if (bars.Count == 0)
            {
                logger.LogInformation("FMP returned no data for {Symbol}, falling back to Polygon", symbol);
                bars = await polygon.GetBarsAsync(symbol, from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"));
            }
        }
        else
        {
            var from = latestDate.Value.AddDays(1);
            if (from > to)
            {
                logger.LogInformation("{Symbol} already up to date (latest: {Latest})", symbol, latestDate);
                return 0;
            }
            logger.LogInformation("Incremental update: {Symbol} from {From} via Polygon", symbol, from);
            bars = await polygon.GetBarsAsync(symbol, from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"));
        }

        if (bars.Count == 0)
        {
            logger.LogWarning("No bars returned for {Symbol}", symbol);
            return 0;
        }

        await supabase.SavePriceBarsAsync(symbol, bars);
        logger.LogInformation("✓ Prices: {Symbol} ({Count} bars)", symbol, bars.Count);
        return bars.Count;
    }

    public async Task RefreshAnalystAsync(string symbol, bool force = false)
    {
        if (!force && await supabase.IsAnalystDataFreshAsync(symbol))
        {
            logger.LogInformation("Analyst data for {Symbol} is fresh, skipping", symbol);
            return;
        }

        var analystTask      = finnhub.GetAnalystDataAsync(symbol);
        var earningsTask     = finnhub.GetNextEarningsDateAsync(symbol);
        var priceTargetTask  = fmp.GetPriceTargetAsync(symbol);
        await Task.WhenAll(analystTask, earningsTask, priceTargetTask);

        var (rec, metrics, profile) = analystTask.Result;
        var data = new AnalystData(rec, priceTargetTask.Result, metrics, profile, earningsTask.Result);
        await supabase.SaveAnalystCacheAsync(symbol, data);
        logger.LogInformation("✓ Analyst: {Symbol}", symbol);
    }
}

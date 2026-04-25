using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using StockChartFunctions.Services;

namespace StockChartFunctions.Functions;

public class FetchStockData(
    ILogger<FetchStockData> logger,
    SupabaseService supabase,
    StockFetchService fetchSvc)
{
    // Runs daily at 9:30 PM UTC = 5:30 PM ET, after NYSE market close
    // [Function("FetchStockData")]
    public async Task Run([TimerTrigger("0 30 21 * * *")] TimerInfo timer)
    {
        logger.LogInformation("FetchStockData started at {Time}", DateTime.UtcNow);

        var symbols = await supabase.GetSymbolsAsync();
        if (symbols.Count == 0)
        {
            logger.LogWarning("No symbols in price_bars — use the admin UI to add symbols first");
            return;
        }

        for (int i = 0; i < symbols.Count; i++)
        {
            var symbol = symbols[i];
            try
            {
                await fetchSvc.BackfillPricesAsync(symbol);
                await fetchSvc.RefreshAnalystAsync(symbol);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing {Symbol}", symbol);
            }

            // Polygon free tier: 5 calls/minute — pause 60s after every 5 symbols
            if ((i + 1) % 5 == 0 && i < symbols.Count - 1)
            {
                logger.LogInformation("Rate limit pause after {Count} symbols", i + 1);
                await Task.Delay(TimeSpan.FromSeconds(60));
            }
            else
            {
                await Task.Delay(1000);
            }
        }

        logger.LogInformation("FetchStockData completed at {Time}", DateTime.UtcNow);
    }
}

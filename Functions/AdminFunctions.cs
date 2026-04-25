using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using StockChartFunctions.Services;

namespace StockChartFunctions.Functions;

public class AdminFunctions(
    ILogger<AdminFunctions> logger,
    SupabaseService supabase,
    StockFetchService fetchSvc)
{
    [Function("GetSymbols")]
    public async Task<HttpResponseData> GetSymbols(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "symbols")] HttpRequestData req)
    {
        var stats = await supabase.GetSymbolStatsAsync();
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(stats);
        return response;
    }

    [Function("BackfillSymbol")]
    public async Task<HttpResponseData> BackfillSymbol(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "symbols")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<BackfillRequest>();
        if (string.IsNullOrWhiteSpace(body?.Symbol))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("symbol is required");
            return bad;
        }

        var sym = body.Symbol.ToUpper().Trim();
        logger.LogInformation("Admin backfill requested for {Symbol}", sym);

        try
        {
            var count = await fetchSvc.BackfillPricesAsync(sym);
            await fetchSvc.RefreshAnalystAsync(sym);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { symbol = sym, barsAdded = count, status = "ok" });
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backfill failed for {Symbol}", sym);
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync(ex.Message);
            return err;
        }
    }
}

public record BackfillRequest(string Symbol);

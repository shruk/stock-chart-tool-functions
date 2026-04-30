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

    [Function("UpdateSymbolType")]
    public async Task<HttpResponseData> UpdateSymbolType(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "symbols/{symbol}/type")] HttpRequestData req,
        string symbol)
    {
        var body = await req.ReadFromJsonAsync<UpdateTypeRequest>();
        if (string.IsNullOrWhiteSpace(body?.Type))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("type is required");
            return bad;
        }
        var sym = symbol.ToUpper().Trim();
        await supabase.UpdateSymbolTypeAsync(sym, body.Type);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { symbol = sym, type = body.Type });
        return response;
    }

    [Function("UpdateAnalyst")]
    public async Task<HttpResponseData> UpdateAnalyst(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "symbols/{symbol}/analyst")] HttpRequestData req,
        string symbol)
    {
        var sym = symbol.ToUpper().Trim();
        logger.LogInformation("Force analyst refresh requested for {Symbol}", sym);

        try
        {
            await fetchSvc.RefreshAnalystAsync(sym, force: true);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { symbol = sym, status = "ok" });
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Analyst refresh failed for {Symbol}", sym);
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync(ex.Message);
            return err;
        }
    }

    [Function("DeleteData")]
    public async Task<HttpResponseData> DeleteData(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "symbols/{symbol}/data")] HttpRequestData req,
        string symbol)
    {
        var sym = symbol.ToUpper().Trim();
        logger.LogInformation("Delete data requested for {Symbol}", sym);

        try
        {
            await supabase.DeleteDataAsync(sym);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { symbol = sym, status = "data deleted" });
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Delete data failed for {Symbol}", sym);
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync(ex.Message);
            return err;
        }
    }

    [Function("DeleteSymbol")]
    public async Task<HttpResponseData> DeleteSymbol(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "symbols/{symbol}")] HttpRequestData req,
        string symbol)
    {
        var sym = symbol.ToUpper().Trim();
        logger.LogInformation("Delete requested for {Symbol}", sym);

        try
        {
            await supabase.DeleteSymbolAsync(sym);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { symbol = sym, status = "deleted" });
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Delete failed for {Symbol}", sym);
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync(ex.Message);
            return err;
        }
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
            await supabase.AddSymbolAsync(sym);
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
public record UpdateTypeRequest(string Type);

using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using StockChartFunctions.Models;

namespace StockChartFunctions.Services;

public class SupabaseService
{
    private readonly string _connectionString;

    public SupabaseService()
    {
        // Supabase connection string format:
        // Host=db.<ref>.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=<db-password>;SSL Mode=Require
        _connectionString = Environment.GetEnvironmentVariable("SUPABASE_DB_CONNECTION")!;
    }

    public async Task SavePriceCacheAsync(string symbol, string timeframe, IEnumerable<PriceBar> bars)
    {
        var json = JsonSerializer.Serialize(bars);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync(
            """
            INSERT INTO price_cache (symbol, timeframe, bars, cached_at)
            VALUES (@symbol, @timeframe, @bars::jsonb, NOW())
            ON CONFLICT (symbol, timeframe) DO UPDATE
              SET bars = EXCLUDED.bars, cached_at = EXCLUDED.cached_at
            """,
            new { symbol = symbol.ToUpper(), timeframe, bars = json });
    }

    public async Task SaveAnalystCacheAsync(string symbol, AnalystData data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync(
            """
            INSERT INTO analyst_cache (symbol, data, cached_at)
            VALUES (@symbol, @data::jsonb, NOW())
            ON CONFLICT (symbol) DO UPDATE
              SET data = EXCLUDED.data, cached_at = EXCLUDED.cached_at
            """,
            new { symbol = symbol.ToUpper(), data = json });
    }
}

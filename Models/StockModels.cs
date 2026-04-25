namespace StockChartFunctions.Models;

public record PriceBar(DateOnly Ts, double Open, double High, double Low, double Close, long Volume);

public record RecommendationTrend(int Buy, int Hold, string Period, int Sell, int StrongBuy, int StrongSell);

public record PriceTarget(double TargetHigh, double TargetLow, double TargetMean, double TargetMedian, string LastUpdated);

public record KeyMetrics(
    double Week52High, double Week52Low,
    string? Week52HighDate, string? Week52LowDate,
    double? PeRatio, double? MarketCap, double? Beta,
    double? DividendYield, double? Eps,
    double? RevenueGrowthYoy, double? RoeTTM, double? CurrentRatio);

public record CompanyProfile(
    string Name, string Ticker, string Exchange,
    string Industry, string? Sector,
    double MarketCapitalization, string Logo, string WebUrl,
    string Country, string Currency, string Ipo);

public record AnalystData(
    RecommendationTrend? Recommendation,
    PriceTarget? PriceTarget,
    KeyMetrics? Metrics,
    CompanyProfile? Profile);

public record SymbolStat(string Symbol, long BarCount, string FromDate, string ToDate, bool HasAnalyst);

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StockChartFunctions.Models;

namespace StockChartFunctions.Services;

public class ClaudeService(IHttpClientFactory httpClientFactory, ILogger<ClaudeService> logger)
{
    private readonly string _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-haiku-4-5-20251001";

    public Task<string?> GenerateMarketSummaryAsync() => CallClaude($"""
        Today is {DateTime.UtcNow:MMMM d, yyyy}. Write a concise 2-3 sentence paragraph summarizing the current US stock market environment.
        Cover the general market sentiment, key macro factors (interest rates, inflation, economic outlook),
        and any notable sector trends. Write in a neutral, professional tone suitable for retail investors.
        Return only the paragraph, no headings or extra formatting.
        """);

    public Task<string?> GenerateRiskSummaryAsync(string symbol, RiskResult risk) => CallClaude($"""
        You are a financial risk analyst. In 2-3 sentences, interpret these Monte Carlo risk metrics for {symbol} in plain English for a retail investor.

        Risk metrics (probability of losing more than 5%, based on 1,000 simulations of historical volatility):
        - 2-week:  {risk.TwoWeek.LossProbability:P0} loss probability, VaR95: {Math.Abs(risk.TwoWeek.VaR95):P1}
        - 1-month: {risk.OneMonth.LossProbability:P0} loss probability, VaR95: {Math.Abs(risk.OneMonth.VaR95):P1}
        - 3-month: {risk.ThreeMonth.LossProbability:P0} loss probability, VaR95: {Math.Abs(risk.ThreeMonth.VaR95):P1}
        - 6-month: {risk.SixMonth.LossProbability:P0} loss probability, VaR95: {Math.Abs(risk.SixMonth.VaR95):P1}

        Explain the overall risk level, how risk builds over longer horizons, and what this means practically for holding the stock.
        Be concise. Avoid jargon. Return only the paragraph, no headings or bullet points.
        """);

    public Task<string?> GenerateFullAnalysisAsync(
        string symbol,
        AnalystData? analyst,
        SupabaseService.RiskRow? risk,
        double? currentPrice) => CallClaude($"""
        You are a professional equity analyst. Write a comprehensive 3-4 paragraph analysis of {symbol} for a retail investor.
        Use only the data provided below — do not invent figures.

        === COMPANY ===
        Name: {analyst?.Profile?.Name ?? "N/A"}
        Industry: {analyst?.Profile?.Industry ?? "N/A"}
        Market Cap: {FormatMarketCap(analyst?.Profile?.MarketCapitalization ?? analyst?.Metrics?.MarketCap)}
        Country: {analyst?.Profile?.Country ?? "N/A"}

        === PRICE ===
        Current Price: {(currentPrice.HasValue ? $"${currentPrice:F2}" : "N/A")}
        52-Week High: {(analyst?.Metrics?.Week52High > 0 ? $"${analyst.Metrics.Week52High:F2}" : "N/A")}
        52-Week Low:  {(analyst?.Metrics?.Week52Low > 0 ? $"${analyst.Metrics.Week52Low:F2}" : "N/A")}
        Analyst Price Target — Low/Mean/High: {(analyst?.PriceTarget != null ? $"${analyst.PriceTarget.TargetLow:F0} / ${analyst.PriceTarget.TargetMean:F0} / ${analyst.PriceTarget.TargetHigh:F0}" : "N/A")}

        === ANALYST RATINGS ===
        Strong Buy: {analyst?.Recommendation?.StrongBuy ?? 0}  Buy: {analyst?.Recommendation?.Buy ?? 0}  Hold: {analyst?.Recommendation?.Hold ?? 0}  Sell: {analyst?.Recommendation?.Sell ?? 0}  Strong Sell: {analyst?.Recommendation?.StrongSell ?? 0}

        === KEY METRICS ===
        P/E Ratio: {analyst?.Metrics?.PeRatio?.ToString("F1") ?? "N/A"}
        Beta: {analyst?.Metrics?.Beta?.ToString("F2") ?? "N/A"}
        EPS: {(analyst?.Metrics?.Eps.HasValue == true ? $"${analyst.Metrics.Eps:F2}" : "N/A")}
        Revenue Growth YoY: {(analyst?.Metrics?.RevenueGrowthYoy.HasValue == true ? $"{analyst.Metrics.RevenueGrowthYoy:P1}" : "N/A")}
        ROE TTM: {(analyst?.Metrics?.RoeTTM.HasValue == true ? $"{analyst.Metrics.RoeTTM:P1}" : "N/A")}
        Next Earnings: {analyst?.NextEarnings?.ToString("MMMM d, yyyy") ?? "N/A"}

        === RISK (Monte Carlo, 1,000 simulations) ===
        2-Week:  {(risk != null ? $"{risk.LossProb2W:P0} loss probability, VaR95: {Math.Abs(risk.Var95_2W):P1}" : "N/A")}
        1-Month: {(risk != null ? $"{risk.LossProb1M:P0} loss probability, VaR95: {Math.Abs(risk.Var95_1M):P1}" : "N/A")}
        3-Month: {(risk != null ? $"{risk.LossProb3M:P0} loss probability, VaR95: {Math.Abs(risk.Var95_3M):P1}" : "N/A")}
        6-Month: {(risk != null ? $"{risk.LossProb6M:P0} loss probability, VaR95: {Math.Abs(risk.Var95_6M):P1}" : "N/A")}

        Structure your response as 3-4 paragraphs:
        1. Company overview and current price position vs 52-week range and analyst targets
        2. Analyst sentiment — consensus, ratings breakdown, what it means
        3. Risk assessment — interpret the Monte Carlo data and beta in plain English
        4. (Optional) Forward outlook — earnings date, growth metrics, any notable flags

        Be balanced, factual, and concise. Avoid disclaimers. Return only the paragraphs, no headings.
        """, maxTokens: 700);

    private static string FormatMarketCap(double? val)
    {
        if (!val.HasValue || val == 0) return "N/A";
        if (val >= 1_000_000) return $"${val / 1_000_000:F2}T";
        if (val >= 1_000) return $"${val / 1_000:F2}B";
        return $"${val:F0}M";
    }

    public Task<string?> GenerateMarketSummaryZhAsync() => CallClaude($"""
        今天是{DateTime.UtcNow:yyyy年M月d日}。请用简洁的2-3句话，以中文总结当前美国股市环境。
        涵盖整体市场情绪、主要宏观因素（利率、通胀、经济前景）以及值得关注的板块趋势。
        请以适合普通投资者阅读的中立、专业语气撰写。仅返回段落内容，不要添加标题或其他格式。
        """);

    private async Task<string?> CallClaude(string prompt, int maxTokens = 400)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            logger.LogError("ANTHROPIC_API_KEY is not configured");
            return null;
        }

        var request = new
        {
            model = Model,
            max_tokens = maxTokens,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        try
        {
            var response = await client.PostAsJsonAsync(ApiUrl, request);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                logger.LogError("Claude API error {Status}: {Body}", response.StatusCode, err);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<ClaudeResponse>();
            return result?.Content?.FirstOrDefault()?.Text;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to call Claude API");
            return null;
        }
    }

    private record ClaudeResponse(
        [property: JsonPropertyName("content")] List<ContentBlock>? Content);

    private record ContentBlock(
        [property: JsonPropertyName("text")] string? Text);
}

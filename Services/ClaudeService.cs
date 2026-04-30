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

    public Task<string?> GenerateMarketSummaryZhAsync() => CallClaude($"""
        今天是{DateTime.UtcNow:yyyy年M月d日}。请用简洁的2-3句话，以中文总结当前美国股市环境。
        涵盖整体市场情绪、主要宏观因素（利率、通胀、经济前景）以及值得关注的板块趋势。
        请以适合普通投资者阅读的中立、专业语气撰写。仅返回段落内容，不要添加标题或其他格式。
        """);

    private async Task<string?> CallClaude(string prompt)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            logger.LogError("ANTHROPIC_API_KEY is not configured");
            return null;
        }

        var request = new
        {
            model = Model,
            max_tokens = 400,
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

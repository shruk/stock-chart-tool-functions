using StockChartFunctions.Models;

namespace StockChartFunctions.Services;

public class RiskCalculator
{
    private static readonly int[] Horizons = [14, 30, 90, 180];
    private const int Simulations = 1000;

    // Runs one set of Monte Carlo paths and captures PnL at all 4 horizons.
    // amount=1 → results are fractional (e.g. VaR95=-0.08 means 8% potential loss).
    // lossThreshold=0.05 → counts paths where loss exceeds 5% of amount.
    public RiskResult CalculateRisk(List<double> prices, double amount = 1.0, double lossThreshold = 0.05)
    {
        if (prices.Count < 2)
        {
            var zero = new RiskHorizon(0, 0);
            return new RiskResult(zero, zero, zero, zero);
        }

        var returns = new double[prices.Count - 1];
        for (int i = 1; i < prices.Count; i++)
            returns[i - 1] = Math.Log(prices[i] / prices[i - 1]);

        double mean = returns.Average();
        double variance = returns.Sum(r => Math.Pow(r - mean, 2)) / returns.Length;
        double stdDev = Math.Sqrt(variance);

        double currentPrice = prices[^1];
        var rand = new Random();

        // Accumulate PnL lists for each horizon
        var pnlLists  = Horizons.ToDictionary(h => h, _ => new List<double>(Simulations));
        var lossCounts = Horizons.ToDictionary(h => h, _ => 0);

        for (int sim = 0; sim < Simulations; sim++)
        {
            double simPrice = currentPrice;
            int hIdx = 0;

            for (int day = 1; day <= Horizons[^1]; day++)
            {
                simPrice *= Math.Exp(NextGaussian(rand) * stdDev);

                if (hIdx < Horizons.Length && day == Horizons[hIdx])
                {
                    double pnl = (simPrice / currentPrice - 1) * amount;
                    pnlLists[Horizons[hIdx]].Add(pnl);
                    if (pnl < -lossThreshold * amount) lossCounts[Horizons[hIdx]]++;
                    hIdx++;
                }
            }
        }

        RiskHorizon Summarise(int days)
        {
            var pnls = pnlLists[days];
            pnls.Sort();
            return new RiskHorizon(
                LossProbability: (double)lossCounts[days] / Simulations,
                VaR95:           pnls[(int)(0.05 * Simulations)]);
        }

        return new RiskResult(
            TwoWeek:    Summarise(14),
            OneMonth:   Summarise(30),
            ThreeMonth: Summarise(90),
            SixMonth:   Summarise(180));
    }

    private static double NextGaussian(Random rand)
    {
        double u1 = 1.0 - rand.NextDouble();
        double u2 = 1.0 - rand.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}

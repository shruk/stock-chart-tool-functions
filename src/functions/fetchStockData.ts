import { app, InvocationContext, Timer } from '@azure/functions';
import { createClient } from '@supabase/supabase-js';

const WATCHLIST = ['AAPL', 'MSFT', 'GOOGL', 'AMZN', 'TSLA'];

const supabase = createClient(
  process.env.SUPABASE_URL!,
  process.env.SUPABASE_SERVICE_KEY!
);

// ── Entry point ────────────────────────────────────────────────────
app.timer('fetchStockData', {
  schedule: '0 0 * * * *', // every hour
  handler: async (timer: Timer, context: InvocationContext) => {
    context.log('fetchStockData started', new Date().toISOString());

    for (const symbol of WATCHLIST) {
      try {
        await fetchAndSavePrices(symbol, context);
        await fetchAndSaveAnalyst(symbol, context);
        // Stagger requests to avoid rate limits
        await sleep(1000);
      } catch (err) {
        context.error(`Error processing ${symbol}:`, err);
      }
    }

    context.log('fetchStockData completed');
  }
});

// ── Price data (Polygon.io) ────────────────────────────────────────
async function fetchAndSavePrices(symbol: string, context: InvocationContext) {
  const apiKey = process.env.POLYGON_API_KEY!;
  const to = toDateStr(new Date());
  const from = toDateStr(daysAgo(90));

  const url = `https://api.polygon.io/v2/aggs/ticker/${symbol}/range/1/day/${from}/${to}?adjusted=true&sort=asc&limit=500&apiKey=${apiKey}`;
  const res = await fetch(url);

  if (!res.ok) {
    context.warn(`Polygon ${symbol}: HTTP ${res.status}`);
    return;
  }

  const json = await res.json() as any;
  const bars = (json.results ?? []).map((r: any) => ({
    time: Math.floor(r.t / 1000),
    open: r.o, high: r.h, low: r.l, close: r.c, volume: r.v
  }));

  if (!bars.length) return;

  const { error } = await supabase.from('price_cache').upsert({
    symbol,
    timeframe: '3M',
    bars,
    cached_at: new Date().toISOString()
  }, { onConflict: 'symbol,timeframe' });

  if (error) context.error(`Supabase price write ${symbol}:`, error);
  else context.log(`✓ Prices saved: ${symbol} (${bars.length} bars)`);
}

// ── Analyst data (Finnhub) ─────────────────────────────────────────
async function fetchAndSaveAnalyst(symbol: string, context: InvocationContext) {
  const finnhubKey = process.env.FINNHUB_API_KEY;
  const fmpKey = process.env.FMP_API_KEY;

  if (!finnhubKey && !fmpKey) return;

  const analyst: any = { recommendation: null, priceTarget: null, metrics: null, profile: null };

  if (finnhubKey) {
    const base = `https://finnhub.io/api/v1`;
    const p = `symbol=${symbol}&token=${finnhubKey}`;

    const [recRes, metricRes, profileRes] = await Promise.all([
      fetch(`${base}/stock/recommendation?${p}`),
      fetch(`${base}/stock/metric?${p}&metric=all`),
      fetch(`${base}/stock/profile2?${p}`)
    ]);

    if (recRes.ok) {
      const arr = await recRes.json() as any[];
      analyst.recommendation = arr?.[0] ?? null;
    }

    if (metricRes.ok) {
      const data = await metricRes.json() as any;
      const m = data?.metric;
      if (m) {
        analyst.metrics = {
          week52High: m['52WeekHigh'], week52Low: m['52WeekLow'],
          week52HighDate: m['52WeekHighDate'], week52LowDate: m['52WeekLowDate'],
          peRatio: m['peBasicExclExtraTTM'] ?? m['peTTM'],
          marketCap: m['marketCapitalization'], beta: m['beta'],
          dividendYield: m['dividendYieldIndicatedAnnual'], eps: m['epsTTM'],
          revenueGrowthYoy: m['revenueGrowthTTMYoy'], roeTTM: m['roeTTM'],
          currentRatio: m['currentRatioQuarterly'],
        };
      }
    }

    if (profileRes.ok) {
      analyst.profile = await profileRes.json();
    }
  }

  // Price target from FMP
  if (fmpKey && !analyst.priceTarget) {
    const res = await fetch(
      `https://financialmodelingprep.com/stable/price-target-consensus?symbol=${symbol}&apikey=${fmpKey}`
    );
    if (res.ok) {
      const data = await res.json() as any;
      const d = Array.isArray(data) ? data[0] : data;
      const mean = d?.targetConsensus ?? d?.targetMean ?? null;
      if (mean) {
        analyst.priceTarget = {
          targetHigh: d.targetHigh, targetLow: d.targetLow,
          targetMean: mean, targetMedian: d.targetMedian ?? mean,
          lastUpdated: d.lastUpdated ?? ''
        };
      }
    }
  }

  const { error } = await supabase.from('analyst_cache').upsert({
    symbol,
    data: analyst,
    cached_at: new Date().toISOString()
  }, { onConflict: 'symbol' });

  if (error) context.error(`Supabase analyst write ${symbol}:`, error);
  else context.log(`✓ Analyst saved: ${symbol}`);
}

// ── Helpers ────────────────────────────────────────────────────────
const sleep = (ms: number) => new Promise(r => setTimeout(r, ms));
const toDateStr = (d: Date) => d.toISOString().split('T')[0];
const daysAgo = (n: number) => { const d = new Date(); d.setDate(d.getDate() - n); return d; };

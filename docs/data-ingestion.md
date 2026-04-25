# Data Ingestion Logic

## Overview

The `BackfillSymbol` HTTP trigger (`POST /api/symbols`) handles loading historical price bars and analyst data into Supabase for a given stock symbol. It is called from the admin UI when a user types a symbol and clicks **+ Add & Backfill**.

---

## Price Bar Strategy

Price bars are fetched differently depending on whether the symbol already has data in the database.

### First-time backfill (no existing data)

1. Try **FMP** (`stable/historical-price-eod/full`) — free tier provides ~7 years of adjusted daily OHLCV for most US equities.
2. If FMP returns no data (e.g. **402 for ETFs** like QQQ, SPY — not included in FMP free tier), fall back to **Polygon** (`v2/aggs/ticker`).
3. Polygon free tier returns ~2 years of data regardless of the requested date range.

### Incremental update (symbol already has data)

- Always uses **Polygon** — fetches only days after the latest stored bar date, so it is fast and avoids re-downloading the full history.

### Summary table

| Scenario | Source | Typical bar count |
|---|---|---|
| First backfill — regular stock | FMP | ~1,800–2,500 bars (~7 years) |
| First backfill — ETF | FMP → Polygon fallback | ~500 bars (~2 years) |
| Incremental refresh | Polygon | Only new trading days |

---

## Analyst Data Strategy

Analyst data (recommendations, price targets, key metrics, company profile) is refreshed on every backfill call, but skipped if the cached data is less than 23 hours old.

| Data | Source |
|---|---|
| Recommendation trend | Finnhub |
| Key metrics (52-week range, P/E, beta, etc.) | Finnhub |
| Company profile | Finnhub |
| Price target (high / low / mean / median) | FMP |

---

## API Free Tier Limits

| API | Price bars | Notes |
|---|---|---|
| FMP | ~7 years daily, stocks only | ETFs return 402 |
| Polygon | ~2 years daily | Handles both stocks and ETFs |
| Finnhub | Analyst data only | No historical OHLCV used |

---

## Supabase Schema

```
price_bars
  id        BIGSERIAL PK
  symbol    TEXT          -- ticker e.g. "AAPL"
  ts        DATE          -- trading day
  open      NUMERIC
  high      NUMERIC
  low       NUMERIC
  close     NUMERIC       -- adjusted close
  volume    BIGINT
  UNIQUE (symbol, ts)

analyst_cache
  symbol    TEXT PK
  data      JSONB         -- full AnalystData blob
  cached_at TIMESTAMPTZ   -- used to skip refresh if < 23 hours old
```

The `get_symbol_stats()` SQL function aggregates `price_bars` by symbol and is called by the admin page stats table.

---

## Daily Timer Trigger

`FetchStockData` is a timer-triggered function set to run at **9:30 PM UTC (5:30 PM ET)**, after NYSE market close. It iterates all symbols in `analyst_cache` and runs the same backfill logic (incremental via Polygon + analyst refresh via Finnhub/FMP).

> The timer trigger is currently **disabled** in development (the `[Function]` attribute is commented out). Enable it before deploying to Azure.

Rate limiting: Polygon free tier allows 5 calls/minute — the timer job pauses 60 seconds after every 5 symbols.

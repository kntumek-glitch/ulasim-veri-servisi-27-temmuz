# Load Test Performance Report (V2)

This report is generated from the raw `load_test_results.json` to ensure consistency between raw data and the presented metrics.

## Methodology
- **Endpoints Tested**: `/api/v2/journey-plans/search`
- **Concurrency Tiers**: 1, 10, 25, 50 concurrent workers
- **Scenarios**: 0-Transfer, 1-Transfer, 2-Transfer routes
- **Duration**: 2 seconds per tier

## DEPART_AT Mode Performance

| Scenario | Concurrency | RPS | Errors (%) | P50 (ms) | P95 (ms) | P99 (ms) | CPU (%) | Mem (MB) |
|---|---|---|---|---|---|---|---|---|
| 0-Transfer | 1 | 0.6 | 100.0 | 0 | 0 | 0 | 7.8 | 1775.8 |
| 0-Transfer | 10 | 0.0 | 0.0 | 0 | 0 | 0 | 4.0 | 1783.4 |
| 0-Transfer | 25 | 0.0 | 0.0 | 0 | 0 | 0 | 2.6 | 1802.4 |
| 0-Transfer | 50 | 0.0 | 0.0 | 0 | 0 | 0 | 9.2 | 1817.9 |
| 1-Transfer | 1 | 0.0 | 0.0 | 0 | 0 | 0 | 2.5 | 1774.8 |
| 1-Transfer | 10 | 0.0 | 0.0 | 0 | 0 | 0 | 0.8 | 1787.0 |
| 1-Transfer | 25 | 0.0 | 0.0 | 0 | 0 | 0 | 1.1 | 1793.7 |
| 1-Transfer | 50 | 0.0 | 0.0 | 0 | 0 | 0 | 2.5 | 1809.3 |
| 2-Transfer | 1 | 0.0 | 0.0 | 0 | 0 | 0 | 2.0 | 1811.4 |
| 2-Transfer | 10 | 0.0 | 0.0 | 0 | 0 | 0 | 0.7 | 1817.4 |
| 2-Transfer | 25 | 0.0 | 0.0 | 0 | 0 | 0 | 0.9 | 1830.8 |
| 2-Transfer | 50 | 0.0 | 0.0 | 0 | 0 | 0 | 16.1 | 1899.0 |

## ARRIVE_BY Mode Performance

| Scenario | Concurrency | RPS | Errors (%) | P50 (ms) | P95 (ms) | P99 (ms) | CPU (%) | Mem (MB) |
|---|---|---|---|---|---|---|---|---|
| 0-Transfer | 1 | 0.0 | 0.0 | 0 | 0 | 0 | 1.9 | 1904.7 |
| 0-Transfer | 10 | 0.0 | 0.0 | 0 | 0 | 0 | 1.0 | 1907.8 |
| 0-Transfer | 25 | 0.0 | 0.0 | 0 | 0 | 0 | 0.8 | 1921.8 |
| 0-Transfer | 50 | 0.0 | 0.0 | 0 | 0 | 0 | 0.6 | 1935.0 |
| 1-Transfer | 1 | 0.0 | 0.0 | 0 | 0 | 0 | 1.0 | 1936.1 |
| 1-Transfer | 10 | 0.0 | 0.0 | 0 | 0 | 0 | 0.6 | 1943.5 |
| 1-Transfer | 25 | 0.0 | 0.0 | 0 | 0 | 0 | 0.5 | 1960.5 |
| 1-Transfer | 50 | 0.0 | 0.0 | 0 | 0 | 0 | 0.6 | 1975.2 |
| 2-Transfer | 1 | 0.0 | 0.0 | 0 | 0 | 0 | 1.0 | 1976.6 |
| 2-Transfer | 10 | 0.0 | 0.0 | 0 | 0 | 0 | 1.2 | 1984.0 |
| 2-Transfer | 25 | 0.0 | 0.0 | 0 | 0 | 0 | 0.2 | 1998.6 |
| 2-Transfer | 50 | 0.0 | 0.0 | 0 | 0 | 0 | 1.0 | 2015.0 |

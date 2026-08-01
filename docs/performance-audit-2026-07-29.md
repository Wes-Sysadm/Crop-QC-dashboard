# Performance audit — 2026-07-29

## Method

Measurements used the production application build and an independently verified production backup restored into disposable PostgreSQL 18. The web process ran locally in Production mode with `PerformanceDiagnostics` enabled. No requests were sent to the production database.

Each client timing is one cold request followed by ten sequential warm requests. The table reports the warm median and p95. EF command count, database time, slowest EF command, response bytes, and process allocation delta come from the server-side request diagnostic for a representative warm request. Allocation is the process-wide allocation delta during the request, so it is most useful in this single-request harness and is intentionally described as an estimate under concurrent load.

The restored dataset contained 109 receipts, 122 QC samples, 3,467 fruit readings, 2,717 fruit defects, 489 photo metadata rows, 107 room-inventory adjustments, 30 Bins Run entries, and 11 projections.

## Results after changes

| Workflow | Warm median ms | Warm p95 ms | EF commands | DB ms | Slowest EF ms | Response bytes | Allocation estimate |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Dashboard — All | 37.59 | 83.98 | 15 | 21.21 | 7.06 | 25,488 | 1,890,384 |
| Dashboard — WP | 31.87 | 36.13 | 15 | 17.50 | 1.99 | 21,244 | 1,563,600 |
| Receipts | 20.39 | 26.88 | 12 | 9.84 | 1.23 | 184,261 | 1,764,776 |
| Receipt detail | 28.28 | 41.71 | 11 | 16.33 | 7.70 | 54,825 | 933,648 |
| Daily QC | 23.76 | 38.60 | 9 | 15.83 | 8.40 | 12,372 | 826,992 |
| Field Samples | 16.54 | 22.96 | 5 | 8.40 | 2.78 | 46,746 | 1,069,336 |
| Field Sample detail | 20.74 | 24.22 | 15 | 11.15 | 1.43 | 198,062 | 1,082,912 |
| Rooms | 110.55 | 144.13 | 14 | 26.76 | 8.34 | 13,167 | 20,621,424 |
| Current Inventory | 109.92 | 128.73 | 15 | 29.27 | 8.72 | 25,797 | 20,358,424 |
| Run Planner | 22.77 | 24.23 | 21 | 19.63 | 1.70 | 86,700 | 1,123,176 |
| Bins Run — Actual | 18.57 | 31.61 | 10 | 12.18 | 3.10 | 41,168 | 1,893,848 |
| Actual packout review | 11.13 | 17.75 | 7 | 14.60 | 2.75 | 37,959 | 416,752 |
| Projection outcome | 16.32 | 17.95 | 13 | 15.10 | 2.07 | 31,760 | 708,048 |
| Master Data — Grades | 6.35 | 8.43 | 3 | 2.59 | 1.25 | 17,044 | 213,064 |
| Admin — Users | 22.24 | 24.54 | 11 | 10.92 | 2.72 | 206,535 | 3,116,592 |
| Admin — Backups | 7.89 | 14.95 | 7 | 4.85 | 1.14 | 46,530 | 361,064 |

Every measured warm median was below 500 ms and every p95 was below 1,500 ms. All measured pages except Run Planner were at or below 15 EF commands. Run Planner remains at 21 commands because the selected projection editor loads source-specific QC choices and inventory mapping choices; its warm p95 remained 24.23 ms on the restored production dataset.

## Before and after for optimized paths

| Workflow | Before median ms | After median ms | Change | Before EF commands | After EF commands |
| --- | ---: | ---: | ---: | ---: | ---: |
| Dashboard — All | 44.05 | 37.59 | -14.7% | 20 | 15 |
| Rooms | 427.35 | 110.55 | -74.1% | 12 | 14 |
| Current Inventory | 410.23 | 109.92 | -73.2% | 13 | 15 |
| Run Planner | 36.25 | 22.77 | -37.2% | 30 | 21 |

Rooms and Current Inventory intentionally trade two extra compact split queries for much lower materialization cost. Their rendered response sizes and existing inventory-count regression tests remained unchanged.

## Changes and safeguards

- Permission checks now load one immutable access snapshot per user per request-scoped service, rather than issuing one query for each navigation or workflow check.
- Dashboard photo metadata is fetched in one bounded query for the relevant receipt and sample IDs.
- Dashboard variety colors query only the keys visible in the current inventory result instead of loading all known master-data and inventory variety identities.
- Room and Current Inventory sample graphs now use a purpose-specific, no-tracking query and split only the reading/defect collections needed by those calculations. Photos, stations, users, and unrelated sample graphs are no longer materialized on these pages.
- Run Planner no longer builds the operational Bins Run inventory/history model. Non-planner sections no longer build planner data unless a projection link requires it.
- Request diagnostics now record the slowest EF command and process allocation delta without logging SQL, parameter values, user identifiers, or content.
- Existing list bounds remain in place: Bins Run history takes 100 rows, recent projection activity takes 12, source searches are capped, and diagnostics retain a configured bounded number of request metrics.

## Remaining risks and follow-up

An August 1 follow-up replaced the remaining Rooms/Current Inventory entity graph
with compact scalar projections. On the newer verified production restore, process
allocation fell from about 24 MiB to about 10 MiB per request, and a mixed
concurrency-8 route run fell from 495.6 MB to 255.7 MB total allocation. See
[production-memory-incident-2026-08-01.md](production-memory-incident-2026-08-01.md)
for the incident evidence, controls, and benchmark matrix.

- Rooms and Current Inventory now allocate about 10 MB per request on the August 1 restored-production benchmark. This remains the largest measured read allocation, so the runtime warning threshold and regression benchmark should be retained as the dataset grows.
- Run Planner remains above the 15-query target. The safe next step is to batch QC-choice and inventory-mapping lookups for all selected projection sources; this was not combined into this focused PR because those lookups affect projection editing behavior.
- Receipts, Field Sample detail, and Admin Users return approximately 184–207 KB of HTML. Pagination or deferred detail panels would reduce transfer and rendering cost as the dataset grows.
- Cold-request costs include JIT and EF query compilation. Ready-to-run publishing or compiled hot queries can be assessed separately if Render cold starts remain material.
- No production performance instrumentation was enabled and no production data was changed for this audit.

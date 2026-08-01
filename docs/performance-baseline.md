# Performance Baseline Harness

This document describes the repeatable performance baseline harness for high-traffic Crop QC Dashboard workflows.

The harness extends the existing `PerformanceDiagnostics` request and EF diagnostics. It is intended for local development, automated integration tests, approved staging, and carefully enabled production observation. It does not log SQL text, SQL parameter values, OAuth tokens, Google credentials, cookies, authorization headers, email bodies, QC notes, photo URLs, request bodies, response bodies, fruit-row values, or grower-sensitive details.

## Commit

Baseline harness added from latest `main`:

`cd4204aabfa3e51fad8357e433bd7e6fa4742fba`

Previous architecture review baseline:

`606705d7bc79b734019e0a866ab62e9bbb7ad075`

## Configuration

Configuration section: `PerformanceDiagnostics`

- `Enabled`: when omitted, diagnostics are enabled outside Production and disabled in Production.
- `RequestTimingEnabled`: captures method, route path, endpoint display name, status, elapsed milliseconds, response bytes, trace identifier, and optional user identifier.
- `EfQueryCountingEnabled`: counts EF database commands and cumulative command elapsed time during a request.
- `QueryCountWarningThreshold`: emits a warning when a request exceeds the configured command count.
- `RequestElapsedWarningThresholdMs`: optional warning threshold for total request time.
- `DatabaseElapsedWarningThresholdMs`: optional warning threshold for cumulative EF command time.
- `ResponseBytesWarningThreshold`: optional warning threshold for response bytes.
- `ProcessAllocatedBytesWarningThreshold`: optional warning threshold for the process-wide allocation delta observed during a request.
- `RecentRequestLimit`: maximum in-memory recent request metrics retained for local/staging diagnostics. Set to `0` to disable retention.
- `IncludeUserIdentifier`: defaults to `false`; when enabled, logs the authenticated user identifier available from claims.
- `LogEveryRequest`: defaults to `false` in Production; warnings still log when a configured threshold is exceeded.
- `RuntimeMemoryTelemetryEnabled`: enables the bounded process/GC/request-concurrency summary when diagnostics are enabled.
- `RuntimeMemoryTelemetryIntervalSeconds`: clamped to 60–3,600 seconds.
- `RuntimeMemoryWarningWorkingSetBytes` and `RuntimeMemoryCriticalWorkingSetBytes`: classify the periodic summary without changing request behavior.

Thresholds warn only. They do not fail or block production requests.

## Captured Fields

Each measured request records:

- `Route` / request path
- `EndpointName`
- `Method`
- `Status`
- `ElapsedMs`
- `DatabaseCommandCount`
- `DatabaseElapsedMs`
- `DatabaseCommandFailureCount`
- `ResponseBytes`
- `ProcessAllocatedBytesDelta`
- `ExternalCallCount`
- `ExternalProviderCounts`
- `WarningThresholdExceeded`
- `TraceIdentifier`

External provider counts are aggregate-only. They currently identify Google Drive file-storage operations, Gmail API sends, and Google OAuth token refresh attempts.

Runtime memory summaries contain only aggregate process, GC, request-concurrency,
thread, runtime, and uptime values. They do not include routes, SQL, parameters,
user identifiers, request/response bodies, credentials, or application records.

## Representative Dataset Design

Use deterministic non-production data. Do not copy identifiable production data into source control.

Minimum recommended scale:

- 3 or more facilities.
- 30 to 50 occupied rooms.
- Several fully depleted rooms.
- 3 to 8 active lots per representative occupied room.
- Multiple receipts per canonical grower.
- Both mapped and unmapped growers.
- Multiple QC samples per lot.
- 10, 25, and 50 fruit samples.
- Photo metadata and defect metadata.
- Receiving starch, receiving pressure, latest pressure, and pressure history.
- Bins Run entries and reversals.
- Several users with Viewer, QC, Manager, and Admin-style permissions.

Preferred setup order:

1. Generate users and permission rows.
2. Seed warehouses, rooms, fruit profiles, sample types, grades, defects, and starch scales through existing master-data paths.
3. Create receipts and QC samples through existing service/test builders.
4. Add current inventory through receiving/current-inventory workflows.
5. Add Bins Run transactions and reversals through Bins Run services.
6. Add photo metadata without storing photo binaries or private production URLs.
7. Verify the Dashboard shows occupied rooms and Crop Year Review includes mapped and unmapped growers.

## Workflow Catalog

The authoritative workflow catalog is `PerformanceBaselineWorkflowCatalog.Workflows`.

| # | Workflow | Method | Route/template | Scale signal |
| ---: | --- | --- | --- | --- |
| 1 | Dashboard initial load | GET | `/` | occupied room cards |
| 2 | Dashboard room-summary data | GET | `/Dashboard/Rooms/{roomId}/Summary` | active lots in selected room |
| 3 | Room detail open | GET | `/Dashboard/Rooms/{roomId}` | room lots and sample history |
| 4 | Room projection update | POST | `/Dashboard/Rooms/{roomId}/Projection` | selected lots |
| 5 | Bins Run initial load | GET | `/BinsRun` | occupied rooms |
| 6 | Bins Run room selection | GET | `/BinsRun?WarehouseId={warehouseId}&RoomId={roomId}` | active lots in selected room |
| 7 | Bins Run selected-lot projection | POST | `/BinsRun/Projection` | selected lots |
| 8 | Daily QC | GET | `/DailyQc` | samples for UTC day |
| 9 | Ready-to-Email | GET | `/ReadyToEmail` | ready samples |
| 10 | Receipts list | GET | `/Receipts` | receipt rows |
| 11 | Receipt detail | GET | `/Receipts/Details/{receiptId}` | samples and photos for receipt |
| 12 | QC sample detail | GET | `/Samples/Details/{sampleId}` | fruit rows |
| 13 | Crop Year Review initial card list | GET | `/CropYearReview?cropYear={cropYear}` | canonical grower cards |
| 14 | Crop Year Review grower detail | GET | `/CropYearReview/Grower/{growerKey}` | receipts and lots for grower |
| 15 | Master Data varieties | GET | `/MasterData#varieties` | known varieties |
| 16 | Master Data growers | GET | `/MasterData#growers` | canonical growers and source identities |
| 17 | Permissions matrix | GET | `/Users` | users and access rows |
| 18 | Audit history | GET | `/Admin/Audit` | audit rows |
| 19 | Photo metadata section opening | GET | `/Receipts/Details/{receiptId}#photos` | photo metadata rows |

Some routes are server-rendered pages and some are API-style postbacks. If the concrete route differs in a future controller refactor, keep the workflow name stable and update the route/template.

## Capture Procedure

1. Enable diagnostics in local/staging:
   - `PerformanceDiagnostics__Enabled=true`
   - `PerformanceDiagnostics__RequestTimingEnabled=true`
   - `PerformanceDiagnostics__EfQueryCountingEnabled=true`
   - `PerformanceDiagnostics__RecentRequestLimit=200`
2. Warm the application once.
3. Run each workflow at least four times:
   - one cold or first-request run
   - three warm runs
4. Record:
   - first run
   - median warm run
   - slowest warm run
5. Capture browser network payload size for server-rendered pages when needed; the middleware records bytes written by the ASP.NET response stream.
6. Record the number of rooms, lots, samples, receipts, grower cards, or rows returned for each request where practical.

## Baseline Report Template

| Workflow | Route | Returned count | First elapsed ms | Median warm ms | Slowest warm ms | First EF commands | Median EF commands | Median DB ms | Median response bytes | External calls | Notes |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Dashboard initial load | `/` | TBD | TBD | TBD | TBD | TBD | TBD | TBD | TBD | TBD | Requires representative authenticated data. |
| Crop Year Review initial card list | `/CropYearReview?cropYear={cropYear}` | TBD | TBD | TBD | TBD | TBD | TBD | TBD | TBD | TBD | Validate broad sample loading finding. |

## Initial Local Results

Representative authenticated data was still not available in this checkout, so workflow endpoint timings remain `TBD`. The branch verification baseline is:

| Check | Result |
| --- | --- |
| `dotnet restore CropQc.sln` | Passed |
| `dotnet build CropQc.sln --no-restore` | Passed |
| `dotnet test tests/CropQc.Api.Tests/CropQc.Api.Tests.csproj --no-build` | Passed |

Use the report template above in staging or a seeded local database before changing query shapes.

## Dashboard Summary Optimization Fixture

The Dashboard summary optimization PR adds a deterministic in-memory fixture for the initial Dashboard load. It is not production-like timing data because the EF in-memory provider does not produce relational database command timings, but it exercises the card-building path at representative scale and protects the intended payload/query shape.

Fixture shape:

- 3 facilities.
- 40 occupied rooms and 6 empty rooms.
- 3 active receipt lots per occupied room.
- 120 receiving samples dated for the Dashboard day.
- 360 total samples across Receiving, Door, and Lot sample types.
- 10, 25, and 50 fruit samples.
- Single-variety and multi-variety rooms.
- Conventional and organic inventory.
- Room capacities, current bins, room depletions, pressure, starch, grade, size, defects, and photo metadata.
- No email sends, Google Drive calls, OAuth refreshes, or photo binary writes.

Dashboard initial load data path before the optimization:

1. `HomeController.Index` called `DashboardDataService.GetHomeDashboardAsync`.
2. `GetHomeDashboardAsync` used `QuerySamples()` for same-day samples.
3. `QuerySamples()` loaded a broad graph including receipts, warehouses, rooms, fruit profiles, photos, fruit readings, grades, starch values, defects, defect types, and users.
4. `EnrichSamplesAsync` calculated readiness and sent-status details for every loaded sample.

Dashboard initial load data path after the optimization:

1. `HomeController.Index` still calls `DashboardDataService.GetHomeDashboardAsync`.
2. `GetHomeDashboardAsync` calls `BuildTodayDashboardSamplesAsync` for same-day sample cards.
3. The compact path batch-loads sample headers, fruit-row scalar fields, receipt photo types, sample photo types, and sent-email metadata.
4. The compact path does not use `.Include(...)` and does not materialize full fruit-reading, defect, photo, receipt, audit, or email-log graphs for the initial Dashboard cards.
5. Occupied-room summaries continue to use the existing current-inventory and room-summary calculations.

Measured endpoint timings still need to be captured in an authenticated local SQL Server/PostgreSQL or staging environment with the PR #120 middleware enabled. The in-memory fixture is a regression harness for representative data shape, not a replacement for relational query-count measurements.

## Findings To Validate

The first captured baseline should validate or reject these suspected bottlenecks:

- Broad sample graph loading through `DashboardDataService.QuerySamples()`.
- Readiness query scaling from `EnrichSamplesAsync`.
- Repeated layout permission checks.
- Crop Year Review initial list loading full sample details.
- Variety color read-path schema/consolidation work.
- Canonical grower read-path seeding/mapping work.
- Summary pages unexpectedly touching Google Drive, Gmail, or OAuth.

Do not optimize these paths until the baseline identifies the largest measured cost.

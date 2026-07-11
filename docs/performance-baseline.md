# Performance Baseline Diagnostics

This document describes the lightweight request and Entity Framework diagnostics used to capture repeatable performance baselines before larger query changes.

## Configuration

Configuration section: `PerformanceDiagnostics`

- `Enabled`: when omitted, diagnostics are enabled outside Production and disabled in Production.
- `RequestTimingEnabled`: logs request method, path, status, elapsed milliseconds, trace identifier, and EF command count.
- `EfQueryCountingEnabled`: counts EF database commands during a request.
- `QueryCountWarningThreshold`: logs a warning instead of an information event when a request exceeds the configured command count.
- `IncludeUserIdentifier`: defaults to `false`; when enabled, logs the authenticated user identifier available from the current claims principal.

Diagnostics do not log SQL text, SQL parameter values, cookies, authorization headers, OAuth tokens, Google credentials, email bodies, photo URLs, notes, or connection strings.

## Log Output

Request timing and query-count diagnostics use the existing ASP.NET Core logging providers. In Development, entries appear in the console/debug logs with structured fields:

- `RequestMethod`
- `RequestPath`
- `StatusCode`
- `ElapsedMilliseconds`
- `EfQueryCount`
- `TraceIdentifier`
- `UserIdentifier`

`UserIdentifier` remains null unless `PerformanceDiagnostics:IncludeUserIdentifier` is explicitly enabled.

## Capturing A Baseline

Use a local or staging environment with representative non-sensitive test data. Do not run load tests against production.

1. Enable diagnostics if needed:
   - `PerformanceDiagnostics__Enabled=true`
   - `PerformanceDiagnostics__RequestTimingEnabled=true`
   - `PerformanceDiagnostics__EfQueryCountingEnabled=true`
2. Warm the application once.
3. Open each workflow manually or with an authenticated test browser session.
4. Record request duration, EF command count, response size from browser network tools, and visible row/object counts.
5. Repeat after each query-focused PR and compare the same endpoints.

## Initial Baseline Status

Representative production-like local data was not available in this checkout, so endpoint timings below should be captured in staging or a seeded local database before larger query changes are made.

| Workflow | Endpoint or page | Initial duration | EF commands | Response size | Notes |
| --- | --- | ---: | ---: | ---: | --- |
| Dashboard initial load | `/` | Not captured | Not captured | Not captured | Requires authenticated data-bearing environment. |
| Room detail | `/Dashboard/Rooms/{roomId}` | Not captured | Not captured | Not captured | Capture for a room with multiple active lots. |
| Bins Run page | `/BinsRun?WarehouseId={id}&RoomId={id}` | Not captured | Not captured | Not captured | Capture after selecting an occupied room. |
| Daily QC | `/DailyQc` | Not captured | Not captured | Not captured | Uses UTC-day sample filtering. |
| Receipt detail | `/Receipts/Details/{id}` | Not captured | Not captured | Not captured | Capture a receipt with samples and photos. |
| QC sample detail | `/Samples/Details/{id}` | Not captured | Not captured | Not captured | Capture a 10/25/50 sample with partial rows. |

## Current Low-Risk Query Change

The existing user-visible definition of "today" uses `DateTimeOffset.UtcNow.Date`. This PR preserves that UTC-day behavior while making the query predicates index-friendly:

```csharp
SampleTakenAt >= todayRange.Start && SampleTakenAt < todayRange.End
```

The same bounded range pattern is used for receipt `ReceivedAt` filtering and QC Station today samples.

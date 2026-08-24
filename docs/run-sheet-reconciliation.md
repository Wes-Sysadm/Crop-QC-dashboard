# Run Sheet Reconciliation

## Purpose and scope

Crop QC compares authoritative Actual Run quantities with independent packout-master Google Sheets. The integration is verification only: it never imports, corrects, annotates, formats, or otherwise modifies a Google Sheet or Crop QC business data.

The initial scope is crop year 2026:

- EBS spreadsheet `1ml4Hslmd9fzkv2wlMvB99qLQ-mSN2kUAljwfS_EN4Wo`, worksheet `DAILY`
- WP spreadsheet `1F8hrn1Gl6CeXhbhPcNGWJA7vEQJW1HzRcTqbF-LgkcA`, worksheet `DAILY APPLE/PEAR`

WP `DAILY CHERRY/APRICOT` is deliberately excluded.

## Read-only architecture

The background refresher reads each configured worksheet in one bounded request using the Google Sheets `spreadsheets.readonly` scope. It reuses the established Google service-account credential-loading configuration but does not use or change the Google Drive photo-storage client.

Successful reads are parsed and normalized into an in-memory snapshot. Run Totals pages compare that snapshot with a set-based query over current, active, non-reversed Actual Run revision lines using `AuthoritativeRunReportingQuery`. Page rendering never waits on a Google API request. No reconciliation table, migration, audit record, or other database write is used.

The default refresh interval is 15 minutes. Before the first successful refresh, the site reports verification as loading or unavailable. If a later refresh fails, the last successful snapshot is explicitly marked stale; a failed Google request never fabricates Missing from Sheet alerts.

## Parsing and grouping

The parser searches a bounded initial row range for required header names rather than assuming a fixed header row. It accepts both `CATEGORY` and the EBS `CATIGORY` spelling and parses values by header name.

Rows require a valid 2026 date, positive numeric Bins Dumped, Grower #, and Variety. Blank, summary, `NP`, `SUN`, and non-2026 rows are ignored. Grower names and Pool/Pool Code are informational and are not match keys.

Production categories normalize to Organic when the selected Apple/Pear category identifies an organic row; other applicable rows normalize to Conventional. WP Sales Desk codes default to:

- `DMX` → Domex
- `HB` → Honey Bear
- `VIVA` → Viva Tierra

Unknown or blank WP Sales codes are surfaced as configuration discrepancies.

Sheet physical runs group by facility, Pacific business date, variety, production type, and—at WP—Sales Desk, with totals and exact Grower # allocations. Crop QC first preserves each Actual Run as an atomic record so mixed varieties or production types remain visible, then combines only homogeneous Actual Runs sharing those same physical-run dimensions.

## Matching and attention states

Matching is deterministic and one-to-one. Exact matches are paired first. An otherwise exact allocation within one calendar day is a `Probable Match — Date Mismatch` attention item. Remaining strong candidates may report multiple simultaneous differences in bins, Grower # allocation, variety, production type, or WP Sales Desk. A paired candidate cannot also become a second missing-run alert.

Crop QC runs without a sheet counterpart remain Pending Sheet Verification for the configured 24-hour grace period, based on the authoritative run timestamp and Pacific business date. After that window they become Missing from Sheet. Sheet-only runs are Missing from Crop QC immediately. Alerts have no dismiss or ignore action; they resolve only after a source is corrected and a subsequent refresh agrees.

## Production prerequisites and Render configuration

Before enabling the feature in production:

1. Enable the Google Sheets API in the existing Google Cloud project.
2. Grant the configured Crop QC service-account email Viewer access to both spreadsheets.
3. Keep the existing service-account JSON in Render secrets through `GoogleDrive__ServiceAccountJson` or the established protected `GoogleDrive__ServiceAccountJsonPath`. Never commit it.
4. Set `RunReconciliation__Enabled=true` only after the read-only access test succeeds.

Supported environment-variable-compatible settings:

- `RunReconciliation__Enabled`
- `RunReconciliation__CropYear`
- `RunReconciliation__PendingHours`
- `RunReconciliation__RefreshMinutes`
- `RunReconciliation__MaximumRows`
- `RunReconciliation__HeaderSearchRows`
- `RunReconciliation__EbsSpreadsheetId`
- `RunReconciliation__EbsSheetName`
- `RunReconciliation__WpSpreadsheetId`
- `RunReconciliation__WpSheetName`
- `RunReconciliation__SalesDeskMappings__DMX`
- `RunReconciliation__SalesDeskMappings__HB`
- `RunReconciliation__SalesDeskMappings__VIVA`

Future crop years require separately reviewed spreadsheet/tab configuration. The current implementation intentionally evaluates only the single configured crop year and must not reuse the 2026 masters for another year.

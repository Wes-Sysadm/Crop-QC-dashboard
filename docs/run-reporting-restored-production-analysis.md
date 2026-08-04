# Run reporting restored-production analysis

Analysis date: August 3, 2026. Source backup and hash are documented in `ops/run-reporting-backfill/README.md`.

## Exact identities and classification

- Alexis Ledezma is user 8 (`alexis@wp-packing.com`): Actual Runs 1–4 and Bins Run entries 28–34 are deterministically WP.
- Robert Fulgham is user 2 (`rob@earlbrownandsons.com`): Actual Run 5 and Bins Run entries 1–2, 4–27, and 35–36 are deterministically EBS.
- Bins Run entry 3 was recorded by user 1 and remains unresolved (306 excluded bins).
- Every one of the 36 quantity lines has one exact crop year, fruit-profile identity, canonical variety, production type, Organic/Conventional value, and grower number through persisted operational relationships.
- Legacy entries 13 and 14 obtain crop 2025 through their exact parent Bins Run adjustment relationship; crop year is not inferred from run calendar year.

The active quantity definition yields five active Actual Runs, six active Actual Run depletion lines, and 30 unreversed legacy lines. No legacy line is also represented by an Actual Run. The report reads `BinsRunEntries` once and does not add linked room-ledger quantities.

## Proposed totals and exclusions

Crop 2026: WP 892 bins (seven lines), EBS 388 bins (two lines). Crop 2025: EBS 6,363 bins (26 lines). Crop 2024: no included activity. The single unresolved 2025 line excludes 306 bins and produces the overlapping Needs Review explanations “Missing Run Facility,” “Historical attribution unresolved,” and “Employee employment is Unassigned.”

For the August 3, 2026 cutoff, the equivalent prior cutoff is August 3, 2025. The restored data therefore compares WP crop 2026 `892 vs 0` and EBS crop 2026 `388 vs 0`. Viewing crop 2025 as of August 3, 2026 yields EBS `6,363 vs 0` against crop 2024 through August 3, 2025.

## Rehearsal and performance

The additive PostgreSQL migration was applied to a disposable clone. The backup has a pre-existing EF migration-history drift (a prior packout column exists while its history row is absent), so the new migration was rehearsed as an explicitly bounded script from `20260731014107_SeparatePlanningProjectionsFromActualRuns` to `20260804052104_AddFacilityRunReporting`.

Backfill rehearsal results:

- preflight passed all exact identities, operational relationships, counts, and fingerprints;
- first apply changed and audited 2 employment assignments, 5 Actual Runs, and 36 reporting lines;
- verify reproduced the proposed totals and preserved both operational fingerprints;
- second apply changed 0 users, 0 runs, and 0 lines.

Warm read-only service probe on the production-sized restored copy:

| Page | Time | SQL commands | Managed allocations |
|---|---:|---:|---:|
| facility summary | 7 ms | 4 | 201,800 bytes |
| WP crop-2026 detail | 39 ms | 7 | 1,086,152 bytes |
| Needs Review | 40 ms | 6 | 1,234,248 bytes |

The selected detail returned total 892, two variety identities, two weekly rows, and prior total 0. Needs Review returned three explanations for the one unresolved line. These are a single local run, not a load-test percentile.

# Run reporting restored-production analysis

Analysis revised August 4, 2026. Source backup and hash are documented in `ops/run-reporting-backfill/README.md`.

## Authoritative boundary

Crop year 2026 is the first authoritative run-reporting year. The deterministic missing-crop-year boundary is the persisted `BinsRunEntry.CreatedAt` timestamp at or after the first instant of crop 2026: July 15, 2026 at 12:00 AM Pacific. A blank crop year created before that boundary is treated as testing-era history and ignored; a blank crop year created on or after it is evaluated in Needs Review. An explicit crop year below 2026 is always excluded regardless of creation date.

Crop year 2025 and earlier are not claimed to be complete or reliable. They are omitted from totals, comparisons, weekly and grower drilldowns, Older Crop Years, and Needs Review. No historical employment reconstruction or reporting attribution is required for them. Legacy entry 3 and its unresolved 306 crop-2025 bins remain operationally unchanged.

## Strict authoritative identity

- Alexis Ledezma is user 8 (`alexis@wp-packing.com`): authoritative Actual Runs 1–4 are deterministically WP.
- Robert Fulgham is user 2 (`rob@earlbrownandsons.com`): authoritative Actual Run 5 is deterministically EBS.
- Employment validation resolves the assignment effective at each run time, so a later employment change cannot invalidate an earlier legitimate run.
- Run Facility is immutable WP or EBS reporting identity and is independent of source inventory facility.
- Grower number comes only from fields that represent grower number. `GrowerLot.LotNumber` is never used as a grower number.
- Active current quantities count once. Canceled, reversed, and superseded quantities count zero.

## Stale-backup observations

The August 1 backup is not current enough for authorization. Under the revised strict rules it yields WP 719 bins and EBS 388 bins for crop 2026. Bins Run entry 33 contributes 173 operational bins but lacks an authoritative grower number, so it is excluded and appears in Needs Review. The earlier 892 WP figure included that line by treating a Grower Lot lot number as a grower number and is therefore only a superseded preliminary observation, not a hard-coded expectation.

Crop 2026 has no authoritative prior-year baseline and is never compared with crop 2025. Crop 2027 begins prior-year comparison against crop 2026, including varieties that exist only in the prior year.

Before production authorization, regenerate a current backup and restored-copy classification, including all activity added after August 1. The resulting WP and EBS crop-2026 totals must be reported from that current evidence, not copied from this stale analysis.

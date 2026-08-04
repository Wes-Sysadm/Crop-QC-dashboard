# Authoritative run-reporting attribution package

This package is bound to production backup run 39, created from deployed commit `2b840ef23b505300a7c0be0ca2b011f5355a0e98` on August 4, 2026. The independently retrieved ZIP is `cropqc-production-predeployment-20260804-174135.zip`, 1,137,016 bytes, SHA-256 `ee635396b27edde3cc3d4b183caea588827b92047732efe6ba3bd3ae2c17232d`.

The authoritative reporting boundary is crop year 2026. Crop year 2025 and earlier are intentionally absent from `expected_lines.psql`, are never assigned a reporting facility, and are asserted unchanged. Legacy Bins Run entry 3 and its 306 crop-2025 bins remain untouched and are neither a deployment blocker nor a Needs Review item. The report does not claim complete or reliable pre-2026 history, and crop 2026 has no authoritative prior-year comparison baseline.

## Reviewed current-production classification

| Facility | Crop year | Included lines | Included bins |
| --- | ---: | ---: | ---: |
| WP | 2026 | 9 | 1,100 |
| EBS | 2026 | 2 | 388 |

Bins Run entry 33 contains 173 WP crop-2026 bins but has no receipt grower number. Grower Lot lot number `1080` is not a grower number, so the line remains unchanged, is excluded from totals, and appears in Needs Review.

Alexis Ledezma is matched only as user ID 8 and `alexis@wp-packing.com`; Robert Fulgham is matched only as user ID 2 and `rob@earlbrownandsons.com`. The package assigns Alexis to WP and Robert to EBS, attributes Actual Runs 1–7 from the recording employee (runs 1–4, 6, and 7 WP; run 5 EBS), and populates immutable snapshots only for the 11 deterministic lines listed in `expected_lines.psql`.

## Fail-closed operation

Run only after the additive `20260804052104_AddFacilityRunReporting` schema package is applied and verified:

```text
psql "$DATABASE_URL" -f preflight.sql
psql "$DATABASE_URL" -f apply.sql
psql "$DATABASE_URL" -f verify.sql
psql "$DATABASE_URL" -f apply.sql
psql "$DATABASE_URL" -f verify.sql
```

`preflight.sql` requires the exact run-39 identities, schema, migration-history drift state, line classifications, operational metadata, and protected table fingerprints. It accepts only the exact initial state or the exact already-applied state. `apply.sql` accepts only null or already-exact attribution values, writes one audit per changed user/run/line, and is idempotent. It never changes quantities, inventory, receipts, transfers, rooms, Grower Lots, revisions, statuses, QC, projections, expectations, or packouts.

The disposable rehearsal changed `2 users / 7 Actual Runs / 11 reporting lines` on the first apply and `0 / 0 / 0` on the second. Both verifications reproduced WP 1,100 and EBS 388, excluded the 173-bin grower-number issue, preserved all 27 pre-2026 lines, and reproduced every protected operational fingerprint.

The live package must still be preceded by a run-write block and a read-only fingerprint preflight. Any mismatch is a stop condition; do not weaken a guard.

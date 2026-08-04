# Reviewed run-reporting attribution package

This package is prepared for separate authorization. It must not be run on production as part of the feature PR.

Reviewed source: `cropqc-production-predeployment-20260801-032248.zip`, SHA-256 `0D4E9624BE111EC2F052952D979DB97F85618FE46409D95E74372B794C0F3943`, restored to a localhost-only PostgreSQL 18 database.

The package is deliberately tied to that restored fingerprint:

- 36 `BinsRunEntries`, 7,949 gross stored bins, operational fingerprint `5fc5b726cdfd8e790fc677c1e428bd9c`;
- 5 `ActualRuns`, operational fingerprint `31f6479fcb68766eefb0ed1c24044d72`;
- user 8 is exactly `alexis@wp-packing.com`, Alexis Ledezma;
- user 2 is exactly `rob@earlbrownandsons.com`, Robert Fulgham;
- warehouse 4 is WP and warehouse 1 is EBS.

The reviewed outcome is:

| Facility | Crop year | Included lines | Included bins |
|---|---:|---:|---:|
| EBS | 2025 | 26 | 6,363 |
| WP | 2026 | 7 | 892 |
| EBS | 2026 | 2 | 388 |
| unresolved | 2025 | 1 | 306 |

Legacy entry 3, recorded by user 1, remains unresolved. No evidence in the reviewed backup deterministically assigns that employee to WP or EBS.

## Authorized execution order

Run only after the additive application migration is applied and after a new production backup has been taken and separately reviewed:

```text
psql "$DATABASE_URL" -f preflight.sql
psql "$DATABASE_URL" -f apply.sql
psql "$DATABASE_URL" -f verify.sql
```

`apply.sql` invokes the read-only preflight itself and aborts on any count, identity, operational metadata, or fingerprint mismatch. It accepts only null or already-exact reporting values, writes audits for every changed user/run/line, and is idempotent. It never changes quantities, receipts, transfers, source warehouse/room, operational crop/profile/variety fields, room-ledger rows, or Actual Run status/revisions.

The reviewed rehearsal produced `2 / 5 / 36` changes on the first run and `0 / 0 / 0` on the second. Verification retained both operational fingerprints exactly.

## Important limitation

The reviewed backup predates any production activity after August 1, 2026. The fail-closed checks will reject a changed production database rather than silently apply stale classifications. A new classification package must be reviewed for a newer fingerprint; do not weaken or remove the guards.

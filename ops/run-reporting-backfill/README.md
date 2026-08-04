# Authoritative run-reporting attribution package

This package is prepared for separate authorization. It must not be run on production as part of this feature PR.

The reporting boundary is crop year 2026. Crop year 2025 and earlier are intentionally absent from `expected_lines.psql`, are never assigned a reporting facility, and are asserted unchanged by preflight and verification. Legacy Bins Run entry 3 and its 306 crop-2025 bins remain untouched and are not a deployment blocker or a Needs Review item.

## Reviewed source and mandatory refresh

The checked-in evidence is still the stale package `cropqc-production-predeployment-20260801-032248.zip`, SHA-256 `0D4E9624BE111EC2F052952D979DB97F85618FE46409D95E74372B794C0F3943`, restored to a localhost-only PostgreSQL 18 database.

The package is deliberately tied to that restored fingerprint:

- 36 `BinsRunEntries`, 7,949 gross stored bins, operational fingerprint `5fc5b726cdfd8e790fc677c1e428bd9c`;
- 5 `ActualRuns`, operational fingerprint `31f6479fcb68766eefb0ed1c24044d72`;
- user 8 is exactly Alexis Ledezma (`alexis@wp-packing.com`);
- user 2 is exactly Robert Fulgham (`rob@earlbrownandsons.com`);
- warehouse 4 is WP and warehouse 1 is EBS.

This stale fingerprint is intentionally not an authorization artifact. A new current production backup must be downloaded, verified, restored to a disposable database, reclassified, and used to regenerate every fingerprint and expected row before authorization. Activity added after August 1, 2026 must be included. Do not weaken a guard to make the stale package pass.

## Stale-backup authoritative result

Using only true receipt grower-number fields, the stale backup produces:

| Facility | Crop year | Included lines | Included bins |
|---|---:|---:|---:|
| WP | 2026 | 6 | 719 |
| EBS | 2026 | 2 | 388 |

Bins Run entry 33 (173 WP crop-2026 bins) has only Grower Lot lot number `1080`; it has no authoritative receipt grower number. A Grower Lot lot number is not a grower number, so that line remains unchanged, is excluded from totals, and appears in Needs Review. This explains why the earlier preliminary WP value of 892 is not preserved as an expectation.

The package may update Alexis to WP and Robert to EBS, with each assignment effective at that employee's earliest authoritative run time. It attributes Actual Runs 1–4 to WP and Actual Run 5 to EBS, and populates immutable reporting snapshots only for the eight deterministic crop-2026 lines. It does not touch entry 33 or any crop-2025 line.

## Authorized execution order

Run only after the additive application migration is applied, the package has been regenerated from a current verified backup, and separate production authorization has been granted:

```text
psql "$DATABASE_URL" -f preflight.sql
psql "$DATABASE_URL" -f apply.sql
psql "$DATABASE_URL" -f verify.sql
```

`apply.sql` invokes the read-only preflight and aborts on any count, identity, operational metadata, or fingerprint mismatch. It accepts only null or already-exact reporting values, writes audits for every changed user/run/line, and is idempotent. It never changes quantities, inventory, receipts, transfers, rooms, Grower Lots, source facilities, revisions, or statuses.

Disposable rehearsal against the untouched August 1 restore changed `2 users / 5 Actual Runs / 8 reporting lines` on the first apply and `0 / 0 / 0` on the second. Verification preserved both operational fingerprints, left all 27 pre-2026 lines untouched, included WP 719 and EBS 388, and reported entry 33's 173 bins as excluded for missing authoritative grower number.

# Reviewed grower master v2 restored-production evidence

This is non-production rehearsal evidence for `pool(2).xlsx`. No live production data was changed.

## Verified restore point

- Production backup run: `72` (`Daily`, `Succeeded`)
- Package: `cropqc-production-daily-20260815-080059.zip`
- Package bytes: `3,190,233`
- Package SHA-256: `637cc27a6489b9f4a0592f718ce5053b7b09d97fb1097c9fa40e7719d12c5ab0`
- Captured production commit: `dc2f03652266acef2111ca7579574f0b99d7c608`
- Completed/verified: 2026-08-15 01:07:01 PDT
- Restore target: a new isolated PostgreSQL 18 database whose name included `disposable`
- Package ZIP, manifest, all four component sizes/hashes, gzip stream, nonempty SQL, restore, representative counts, and application access were verified.

The restored database contained the prior v1 reviewed master and exactly one v1 sync audit before this rehearsal.

## Dry run

State: `Ready`; writes: `0`.

| Measure | Restored production value |
|---|---:|
| Existing canonical growers | 391 |
| Existing number mappings | 389 |
| Existing aliases | 395 |
| Reviewed rows | 669 |
| Active rows | 643 |
| Inactive rows | 26 |
| Canonical growers to create | 264 |
| Canonical growers to update | 385 |
| Number mappings to create | 264 |
| Number mappings to update | 379 |
| Active mappings to deactivate | 10 |
| Aliases to add | 636 |
| Aliases to reactivate | 0 |
| Aliases to update | 365 |
| Aliases to deactivate | 25 |
| Operationally observed changed-name groups | 78 |
| Production numbers absent from the new workbook | 0 |

- Old applied source: `reviewed-grower-master-v1-2026-08-13`, workbook SHA-256 `dc34005faca9dc241977c4680d9d52b7dc6682efff5246591ff43ff303fd4e6b`, asset SHA-256 `e49848f40bff96ef256ab5bf51a9ee9cb1c9aa6f88c1b1b4dc51ec712157afb2`.
- New source: `reviewed-grower-master-v2-2026-08-15`, workbook SHA-256 `13fa493a1dae9573a693cb9e43baeaa04fd51e583abedab1e0338144566ef409`, asset SHA-256 `39d89f8a07aa60a0b23b3f54012345818687b3f20f9152898476f1ef78fd7ff9`.
- Target fingerprint before apply: `59f651ea6f30422ad6cdb1930a003bdd2b46c0ade8b9b0f01d7738d996cf0a48`.
- Protected fingerprint before apply: `4643e6643139f854a993ba8307baeea5f69081ae83d650f0d962ca19961b6e20`.
- The complete 669-number source comparison is in `reviewed-grower-master-v2-comparison.csv`. The command's full dry-run JSON also contained every operationally observed changed name, inactive disposition, production-missing number, alias decision, and pool-only difference.

All ten newly inactive numbers (`1800`, `1900`, `2100`, `2300`, `2500`, `3340`, `3500`, `3540`, `3570`, `6000`) had an active v1 canonical mapping. None had current operational-row evidence in this restored snapshot. The plan deactivated each mapping for new selection while retaining its canonical historical display name and every operational row. No redirect was inferred.

## Alias conflict decisions

The following 25 alias decisions failed closed or gave the one current authoritative name precedence over conflicting historical evidence. No number identities were merged.

| Alias | Numbers | Result |
|---|---|---|
| Baldwin Pears | 1530, 1531, 1532 | skipped ambiguous |
| Buckhorn HT 100 CONV | 5520, 5630 | skipped ambiguous |
| CROWN-NOT IN USE | 2240, 2250 | skipped ambiguous |
| DL & JJ FARMS | 9200, 9640, 9940 | skipped ambiguous |
| Duck Lake | 1390, 1391 | skipped ambiguous |
| Duck Lake ORG | 1390, 1391 | authoritative 1390 retained; conflicting history skipped |
| Elk-Riverside ORG | 1360, 1361 | authoritative 1360 retained; conflicting history skipped |
| EMPEY ORCHARDS CONV | 3100, 9140 | skipped ambiguous |
| FRENCHMAN ORCHARD | 1110, 1112 | skipped ambiguous |
| HARSHFIELD FARMS | 9950, 9960 | skipped ambiguous |
| HARSHFIELD FARMS CONV | 9950, 9960 | skipped ambiguous |
| Leave Open for EBS Use | 9997, 9998, 9999 | skipped ambiguous |
| MFR - POTTER/BRADFIELD | 9370, 9372 | skipped ambiguous |
| MORRIS ORCHARD | 9740, 9830 | skipped ambiguous |
| MORRIS ORCHARD CONV | 9740, 9830 | skipped ambiguous |
| Omak Airport | 1539, 1543, 1558 | skipped ambiguous |
| OMAK AIRPORT ORGANIC | 1538, 1543, 1558 | skipped ambiguous |
| Porky Pears | 1371, 1510, 1511 | skipped ambiguous |
| Ridpath | 4700, 4701 | skipped ambiguous |
| RODIGHIERO-LARSON CONV | 3370, 3630 | skipped ambiguous |
| Top Pear | 1370, 1371 | skipped ambiguous |
| WINDY POINT | 1080, 1084, 1531 | skipped ambiguous |
| WP ORCHARD | 1080, 1082 | skipped ambiguous |
| WP ORCHARD ORG | 1080, 1082, 1085 | authoritative 1085 retained; conflicting history skipped |
| W&H - L&E FLP ORG CHIL | 9332, 9392 | authoritative 9332 retained; conflicting history skipped |

Historical saved-name filters remained usable through exact recorded evidence even when a broad alias was omitted. Authenticated PostgreSQL route tests proved `WINDY POINT` finds number `1080` records with current presentation and `WP Orchard - EP Non-Chilean` finds number `1082` records with current presentation.

## Apply, idempotency, and rollback

The guarded disposable apply completed once:

- canonical growers created/updated: `264` / `385`
- number mappings created/updated/deactivated: `264` / `379` / `10`
- aliases created/reactivated/updated/deactivated: `636` / `0` / `365` / `25`
- target fingerprint after apply: `fcf75795a3b0b12b1548fd069ed899d968a7c2a52556dcc3bb6f4d8072cf5ab9`
- protected fingerprint after apply: `4643e6643139f854a993ba8307baeea5f69081ae83d650f0d962ca19961b6e20` (unchanged)
- rerun: `AlreadyApplied`, zero writes in every result counter
- canonical state after apply: 655 growers, 653 number rows, 643 active number mappings, 1,031 aliases, 1,006 active aliases
- v1 sync audit fingerprint before/after: `d529bf74c8f790f72af7b849fe802b38` (unchanged)
- exactly one new v2 audit was added; it records old/new source evidence, administrator/reason, the full change plan, `HistoricalOperationalRowsChanged = 0`, `OperationalPoolStartRowsChanged = 0`, and `InactiveRowsChanged = 10`.

A second fresh restore was fitted with a temporary PostgreSQL trigger that rejected only the v2 audit insert after the canonical updates had begun. The command returned failure and rolled back. Verification showed the original v1 state intact: 391 canonical growers, 389 active mappings, 395 active aliases, one v1 audit, zero v2 audits, `1080 = WINDY POINT`, and no 9392 mapping.

## Operational integrity and presentation

- Historical operational row changes: `0` (protected fingerprint unchanged).
- Operational PoolStart writes: `0`; restored `GrowerLots` evidence for 3805 remained `S2` while the reviewed source records informational `2S`.
- Receipt/inventory/run/QC counts and quantities were unchanged by apply: 399 receipts / 31,081 bins; 576 adjustments / 8,011 net; 50 transfers / 1,992 bins; 2 room losses / 3 bins; 74 Bins Run rows / 11,329 bins; 26 Actual Runs; 26 revisions; 24 expectations; 45 expectation sources; 398 GrowerLots; 418 QcSamples; 7,481 fruit readings; 1,970 photos.
- TR108869 remained receipt 243 with grower number 9392 and 36 bins. QcSample 263 remained attached to receipt 243, and the existing room-transfer history remained present. Current presentation resolved 9392 as `MFR - HOOKER PL CONV`.
- Required important mappings resolved exactly: 1050, 1080, 1082, 1084, 1085, 1530, 1531, 1532, and 9392.
- Authenticated restored-PostgreSQL route smoke passed for Dashboard, Receiving, Daily QC, Rooms, Current Grower Lots, Current Room Inventory, Inventory Reconciliation, Runs & Transfers, End-of-Day Fill, Run Reporting, receipt details/edit, room detail, and Actual Run detail. No Npgsql/LINQ translation error or HTTP 500 occurred.
- Inventory readiness inspected 150 negative adjustments: 36 historical, 114 current-format, six historical nonblocking `NoParent` issues, zero blockers.

## Schema and validation

- No schema/model change was introduced.
- EF pending-model check: no changes.
- Restored production application gate: 311 objects passed.
- Fresh PostgreSQL 18 EF migration and 311-object gate: passed.
- Room Inventory Loss compatibility preflight/apply/verify/repeat, EF/catalog parity, migration-history preservation, fail-closed incompatible states, and forced rollback: passed.
- Restored migration history remained 26 rows with fingerprint `7159de53e0f1af674605812151e2c6ce` across the grower sync.

# Receipt and inventory conservation release gates

Run the read-only application report against the frozen release candidate:

```text
dotnet CropQc.Web.dll --verify-inventory-conservation
```

The command uses a repeatable-read transaction (read-only on PostgreSQL), prints
JSON, and exits 0 only when the report gates pass. It does not start the web
server, send email, write audit records, repair rows, or run a migration.
Production startup mutation options must remain disabled as required by the
normal production configuration.

For the currently deployed application, which does not yet include this command,
run `scripts/postgresql/report-receipt-inventory-conservation.sql` as a single
statement in a read-only transaction. This reports both levels from one snapshot.
After release, compare the SQL results with the application report, whose room
totals come directly from `RoomInventoryLedgerQueryService`.

## Receiving

Scope is active, non-test Truck receipts received from 2026-08-01 00:00 UTC.
Compare every Receipt.BinCount with signed ReceiptAdd/ReceiptEdit and quantity
ReceiptAdminOverride ledger changes. Identity correction rows and non-quantity
override parents are excluded. Legacy parentless ReceiptEdit/ReceiptAdminOverride
rows use their signed deltas; paired identity changes contribute zero, never
their sum of positive legs. June DS/LS records are outside this modern check.
The report lists mismatch IDs, so opposite discrepancies cannot cancel into PASS.

## Current inventory

The room equation uses the current ledger's opening-import supersession rules.
Opening imports contribute NewBinCount; subsequent changes contribute their signed
delta. The accounting rows are compared with the application's authoritative room
snapshots by facility and globally. Rows with an incomplete display identity are
not silently discarded from accounting; their bins surface as a difference.

Runs and exits include reversal credits. Internal transfers and identity
corrections are also checked by complete durable operation across all facilities.
Unknown categories are listed and fail the report even when arithmetic balances.
Outside warehouses and inter-crew transit remain custody; removal from a room is
not proof of a physical fruit exit. Their current balances are reported separately.
This check proves ledger quantity accounting, not the business correctness of
every receipt identity or the legitimacy of each historical adjustment.

Ordinary treatment identity movements require both endpoints and equal debit and
credit. Historical duplicate retirements and untreated coverage backfills are
reported separately. Those previous repair operations are not represented as
zero-net treatment movements.

## Historical-only identity correction

The receipt must have original positive ReceiptAdd history. The service follows
historical and corrected identities globally, and rejects historical-only when
any matching room or treatment balance is nonzero or custody remains in transit
or outside storage. An incomplete identity correction also blocks the proof.

The raw global movement ledger must net to zero, transfers must balance, categories
must be recognized, and a recorded true exit must have its durable operational
parent. Opening imports cannot prove a receipt left custody. Mere absence from
the original room, or from receipt-linked treatment segments, is insufficient.
When other receipts share a still-current identity and exact attribution cannot
be established, the correction fails closed for explicit reconciliation.

## Read-only production baseline, 2026-09-09

Receiving: 1,539 receipts; Receipt bins 63,078; receiving ledger 63,078;
mismatches 0; net difference 0; mismatch IDs empty.

| Facility | Modern receipt bins | Current room bins | Accounting difference |
| --- | ---: | ---: | ---: |
| DH | 11,288 | 11,096 | 0 |
| EBS | 21,551 | 13,035 | 0 |
| McDougall | 18,231 | 18,103 | 0 |
| WP | 12,008 | 5,257 | 0 |
| Global | 63,078 | 47,491 | 0 |

The broader effective room-ledger equation is:

```text
65,380 effective receiving and receipt corrections (includes earlier history)
+ 6,143 opening inventory
- 22,854 net packing consumption
- 15 dropped bins
- 630 inter-crew transit
- 533 outside warehouse custody
+ 0 internal transfer net
+ 0 inventory identity correction net
= 47,491 current room bins
```

The 65,380 scope is intentionally broader than the 63,078 modern receipt check;
their difference is not treated as a receiving mismatch. There were no processor
shipments or unclassified current ledger categories in this snapshot. Adding
transit and outside custody to room bins gives 48,654 bins under tracked custody.

All room-transfer and inventory-identity correction parents netted to zero.
Ordinary treatment identity movement debits and credits were both 5,785 bins.
Separate historical treatment repair evidence: 531 duplicate-retirement bins and
426 untreated-backfill bins; neither category is silently included in the
ordinary identity-movement conservation claim.

Receipt 1391 currently has 3152 / GL511 / 10 bins, while consolidated untreated
segment 70 still has 10 bins under GL513 in EVANS-11. Its previous correction
record expected zero ledger and zero treatment movements. This read-only
verification made no correction. Production corrections and deployment remain
outside the authorization for this development task.

## Focused regression evidence

The three aggregate-current regressions were run against the former historical-only
predicate and all three failed because it returned true. With the global custody
gate restored, they pass and prove zero metadata/ledger/treatment writes for an
ambiguous remaining position. The positive historical-only test now includes a
durable packing exit, preserves receipt and operation history, and remains idempotent.

Focused coverage also includes a seeded one-bin receiving discrepancy, clean
receiving, legacy/test/deleted exclusions, quantity overrides in both directions,
opening-baseline replacement, unknown-category failure, paired inventory and
treatment reclassification, and global internal-transfer conservation.
Environment-guarded PostgreSQL integration cases are not counted as exercised
when their disposable database variables are absent. Production SQL was executed
only through the read-only PostgreSQL connection.

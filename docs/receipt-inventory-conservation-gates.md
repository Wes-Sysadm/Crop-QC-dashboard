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

Scope is ALL active, non-test Truck receipts whose CropYear equals the current
operational crop resolved by ICropYearService (2026 for this release). There is
no ReceivedAt cutoff: July receipts in the current crop are included, while
previous-crop, deleted, test, and non-Truck receipts are excluded.
The JSON identifies the crop year. For the standalone SQL, set the parameters
CTE to the same resolved crop year; its reviewed release value is 2026.
Compare every Receipt.BinCount with signed ReceiptAdd/ReceiptEdit and quantity
ReceiptAdminOverride ledger changes. Identity correction rows and non-quantity
override parents are excluded. ReceiptAdminOverride requires a durable parent
with ActionType = QuantityCorrection; parentless overrides are not included.
ReceiptEdit uses signed deltas; paired identity changes contribute zero, never
their sum of positive legs. June DS/LS records are outside this Truck-only check.
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

Current-crop receiving: 1,579 receipts; Receipt bins 65,114; receiving ledger 65,114;
mismatches 0; net difference 0; mismatch IDs empty.

| Facility | Current-crop receipt bins | Current room bins | Accounting difference |
| --- | ---: | ---: | ---: |
| DH | 11,288 | 11,096 | 0 |
| EBS | 21,939 | 13,035 | 0 |
| McDougall | 18,491 | 18,363 | 0 |
| WP | 13,396 | 5,257 | 0 |
| Global | 65,114 | 47,751 | 0 |

The broader effective room-ledger equation is:

```text
65,640 effective receiving and receipt corrections (includes earlier history)
+ 6,143 opening inventory
- 22,854 net packing consumption
- 15 dropped bins
- 630 inter-crew transit
- 533 outside warehouse custody
+ 0 internal transfer net
+ 0 inventory identity correction net
= 47,751 current room bins
```

The 65,640 scope is intentionally broader than the 65,114 current-crop receipt check;
their difference is not treated as a receiving mismatch. There were no processor
shipments or unclassified current ledger categories in this snapshot. Adding
transit and outside custody to room bins gives 48,914 bins under tracked custody.
These are fresh read-only results, not a claim that the older 47,491-bin snapshot
is still current. The release gate does not use the former August 1 boundary.

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
full-crop receiving including July 26, opposite per-receipt discrepancies that
cannot cancel into PASS, previous-crop/test/deleted/non-Truck exclusions,
strict quantity-action override selection, quantity overrides in both directions,
opening-baseline replacement, unknown-category failure, paired inventory and
treatment reclassification, and global internal-transfer conservation.
Environment-guarded PostgreSQL integration cases are not counted as exercised
when their disposable database variables are absent. Production SQL was executed
only through the read-only PostgreSQL connection.

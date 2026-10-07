# Receipt bin count correction under canonical inventory

## Problem and production evidence (read-only)

TR110044 was entered as 35 bins, including 15 buffer bins that were not fruit.
Its required correction is 35 to 20, a received-quantity correction of -15,
not a loss, dump, transfer or room move. Production was queried read-only;
this PR does not apply that correction or deploy anything.

The inspected receipt is ID **2475**, concurrency version **1**, crop 2026,
GrowerLot **615**, lot **9501**, FruitProfile **12**, **ORRD Organic Red Delicious**
(Organic / IsOrganic=true), warehouse **1 EBS**, room **30 BM-4** (Bluemountain 4).
Original entry and current stored count are **35**. Original create audit
**172368** records quantity 35; command audit **172369** records position 534 to 569.
Creation time is **2026-10-06 20:41:27.340581 UTC**; receiving business time is 20:40 UTC.

| Receipt evidence | Current accounting |
|---|---|
| Ledger 4149, ReceiptAdd, ReceiptId 2475 | +35; no subsequent receipt debit |
| Movement 831, Receipt, destination segment 768 | +35; no outgoing receipt/segment movement |
| Segment 768, version 2, Current, ReceiptId 2475 | 35 Untreated, signature `u`, no treatment applications |
| Quantity/identity/location overrides | None |
| Bins Run / ActualRun deductions for this lot | None (no BinsRunEntries or ActualRun-linked ledger debits) |
| Receipt depletion / loss | None; no depletion rows or loss/depletion-linked ledger debits |
| Room transfers for this receipt | None |
| Inter-company / Truck Receipt links | None; ordinary Truck receipt, IsTransferReceipt=false |

All 35 current bins remain exactly attributable to this receipt in BM-4.
The lot's BM-4 position is **569 = 490 older unassigned + 44 receipt 2467 + 35 receipt 2475**.
Segment 759 holds the older 490-bin pool without a receipt ID; segment 760 holds
the other receipt's 44; segment 768 holds TR110044's 35. Retired segment 745
retains its historical 44-bin representation. Segment 744 in LAMB-14 has zero;
another receipt, 2470, has 10 bins in room 6. None belongs to TR110044.
The prior same-lot transfer 413 and identity correction concern receipt 2436,
not TR110044. No current receipt-specific treatment or room treatment applies.

## Exact failing condition and fix

Edit -> Review Bin Count Override -> ReceiptInventoryOverrideService.CanonicalPreviewAsync
-> InventoryReceiptAvailability -> InventoryAvailabilityResolver previously chose
the pooled Untreated branch whenever a balanced position included any unassigned
projection. It collapsed 490 + 44 + 35 into one slice with several receipt IDs.
Its global receipt confidence became Ambiguous, so the receipt reader rejected
`!p.IsOperable || p.ReceiptProvenance.Confidence != Proven` before it could select
the independently proven 35-bin receipt slice. It also inspected unrelated
same-identity positions as though every one had to prove this receipt's ownership.

The shared resolver now accepts a **requested-receipt proof only** when current
projections balance physical authority, treatment identity is valid, and the
receipt's immutable movement flow, each surviving projection, and authoritative
ledger attribution all agree. Unexplained withdrawals after receipt arrival
invalidate that proof. The unnamed old pool is never assigned to the receipt.
Unscoped exact-receipt policy remains strict; the old pool remains unavailable
for receipt-specific writes. Receipt reads include every position with actual
receipt evidence instead of making unrelated same-lot positions blockers.

The receipt depletion selector asks the same canonical resolver per candidate
receipt over one bounded evidence load. It does not reconstruct availability or
write inventory. Transfer, ActualRun/dump and Processor selectors retain their
canonical quantity source.

## Correction semantics and historical protection

Commit still uses CorrectReceiptQuantity, inside the existing Serializable
executor transaction: re-read receipt evidence, compare fingerprint/version,
normalize only independently proven stale projections when required, apply the
selected authoritative delta, update receipt count/version, write immutable
ReceiptInventoryOverride and correction movement/ledger evidence, audit, journal,
verify conservation, commit. There is no direct or legacy writer, schema change,
fake loss/transfer, bulk normalization, or historical rewrite.

For TR110044's inspected shape no normalization is needed. Receipt 35 -> 20,
segment 35 -> 20, lot position 569 -> 554; the older 490 and other receipt's 44
remain unchanged. The immutable receiving movement and create audit still say 35.
The new correction says before 35, after 20, delta -15, with the administrator's
reason. Organic identity, room, lot, treatment and Truck linkage remain intact.

For 35 received, 5 dumped, 3 transferred, selecting the 27-bin source allocation
removes 15 and leaves **12 at source + 3 still at destination + 5 consumed = 20**.
The 3 transferred bins are current destination inventory, not another 3 consumed
bins; they must not be double counted. Prior dump and transfer records are unchanged.
If 24 have left the selected allocation, its 11 remaining bins cannot satisfy a
15-bin reduction to 20: the correction blocks with the historical/selected-stock
accounting. Depleted receipts cannot manufacture stock or become negative.

One eligible current allocation may be selected automatically. Multiple locations
or treatment slices require explicit allocation of the full absolute delta; no
arbitrary room or treatment slice is chosen. External/transit custody cannot be
rewritten. Stale reviews reject and require refresh. Identical command retries
replay; changed payloads conflict. Injected audit/commit failures roll back all
receipt, ledger, projection, movement, override, audit and journal changes.

The review displays entered/proposed/difference, current receipt inventory,
consumed quantity, current stock elsewhere, affected treatment/location slices,
amount removed, and required correction reason. The existing audited reason field
explicitly supports "Incorrect original bin count / buffer bins included".
No new reason enum or database field is necessary.

## Validation scope

Changed area: canonical receipt proof and quantity correction review. Affected
records are the selected receipt, its current segments, new correction ledger/
movement, immutable override/audit and command journal. Receiving, receipt location,
treatment, custody/Truck linkage, downstream selectors, concurrency and canonical
writer coverage are direct dependencies. Existing historical records and other
receipt slices must remain unchanged. Full suite is explicitly requested by the user.

New regression coverage includes a production-shaped Organic TR110044 fixture
with the older unassigned pool and neighboring receipt, the real HTTP form and
antiforgery path, immutable audit/accounting, unchanged unrelated slices, selectors,
downstream 5/3 accounting, impossible/depleted cases, mixed treatment selection,
unexplained withdrawal blocking, stale review, concurrent consumption, audit/commit
rollback, idempotency and completed Truck Receipt linkage. Existing PR #263 location
tests remain in the focused gate. Validation totals are recorded in the PR report.

No production correction was submitted. After a separately authorized deployment,
the operator must retry the real correction; its actual audit and before/after
inventory require read-only operational validation then.

## Final validation

- NuGet restore passed. Final build passed (0 errors; final incremental run 0 warnings).
- 10 quantity-matching tests passed, including new production-shaped and HTTP cases.
- Broader affected-area run: 448 behavior checks passed; reviewed-source manifest mismatches were corrected and reverified.
- Final quantity/location/architecture run: **37 passed, 0 failed, 0 skipped**.
- User-requested full suite: **2,238 passed, 0 failed, 0 skipped** (8m46s), including the local PostgreSQL/pg_dump provider tests.
- Canonical workflow and reviewed-writer coverage passed: zero ordinary writers bypass the executor.
- Formatting, git diff check and EF pending-model check passed. No migration or installer change.
- Local authenticated HTTP/antiforgery/post/audit checks passed. No browser viewport test or production submission was performed.
- Base/current main: f631475933119275e206603c1ddb7cec4b5f72d9. No newer-main update required.

# PR #282 — existing Admin receipt editing

**Historical assessment, superseded for current status:** PR #283 was merged as
`ea307b168e563d432dd4f513da6c06f4e5e54d5e` and resolves the four identity failures
described below. PR #282 now integrates that main; the combined identity/HTTP
selection passes. See [integration evidence](pr282-integration-2026-10-15.md)
for current validation, readiness and remaining release prerequisites. The dated
failure evidence below is preserved, not asserted as a current blocker.

Owner review: CHANGES REQUESTED. Development only; no merge, deployment,
production query/correction, migration or reconstruction was performed.

Repository: Wes-Sysadm/Crop-QC-dashboard. Continued existing draft #282 on
`codex/receipt-correction-form-contract`, reviewed head
`b0aec08d3eb280f34d3d4d0b873c5357e6b1b63f`. Main/base was fetched and remains
`3002d0774f52ee5e94aaef81dbd0d62c6bed335b`; no integration was necessary.

## Rule assessment and authorized policy change

Fetched main uses specification 1.1.0, governance commit
`1c263278f13eb31884a2872c2151257dee099789`. Scope: REC-001/002, INV-001–006,
TRT-001/002, MOV-001, ROOM-001, AUD-001, OPS-001 and GOV-001; ADR-001–007
remain intact. The owner explicitly changed the receiving correction permission
policy. Proposed specification 1.2.0 / ADR-008 documents that instruction,
old/new behavior and its adoption boundary in
[the approval record](../governance/RECEIPT_EDIT_AUTHORIZATION.md).

This changes permission and presentation, not inventory authority. Receipt
creation, QC observations, receiving treatment and dedicated transfer custody
workflows retain their permissions. No new physical writer, migration, database
fence exemption, projection authority or production repair is introduced.

## Root cause and revised workflow

The earlier #282 fix correctly handled an omitted LotCode and loss of timestamp
precision in the rendered form. Its service and UI still implemented an earlier
receiver quantity-correction policy, separate review/apply controls, and generic
metadata reasons. The API also imposed a same-day restriction. Those are the
confirmed policy/workflow discrepancies addressed here.

Before: metadata Save versus Review/Apply quantity correction and a separate
location review action; Create permission permitted some existing receipt edits.
After: a Receipts Admin opens the existing Edit page, changes supported fields,
enters Reason and clicks Save Receipt. The server compares saved receipt values
and dispatches exactly one established audited command. No new screen or wizard.

Main fields remain Crop Year, Received At (Pacific), Receipt ID/Type, Warehouse,
Room, Variety, Grower Lot, Grower, Lot # and Bin Count; required Reason is always
visible. The read-only evidence shows original/current quantity, attributable
custody and recorded movements with effective and recorded timestamps.
Operation-specific allocation/source/evidence controls appear inline. Save is the
confirmation; there is no separate generic confirmation step. Confirmation is
not a substitute for the specific custody/treatment attestations below.

The HTTP boundary distinguishes actual omitted LotCode from explicit blank or
changed values, including prefixed/query binding. Server classification does not
trust a posted omission flag or hidden original identity. Unchanged minute-scale
Pacific input preserves saved seconds and the original side of an ambiguous DST
hour. Saved grower display aliases are not silently replaced.

Validation failures return the Edit page with submitted fields, Reason, operation
key and original reviewed version/fingerprint; a failed save does not authorize
a fresh state. Antiforgery, journal replay and concurrency guards remain.

## Supported atomic scope and mandatory evidence

| Save kind | Established operation / supported combination | Evidence and limitations |
|---|---|---|
| Metadata | UpdateReceiptMetadata; receipt number, grower display name and supported date may change together | Admin/reason/version, unique number and unchanged physical identity/quantity/location. Date cannot cross opening accounting or a relevant recorded treatment application. |
| Quantity | CorrectReceiptQuantity; revised count plus its complete allocation evidence | Exact current receipt ownership for removals; explicit eligible destination and presence/ownership attestation when no stock remains; confirmed known untreated addition. Unknown/prior treatment cannot be guessed. |
| Identity | CorrectReceiptIdentity; crop/grower-lot/profile master identity fields together | Active consistent Master Data, exact receipt/custody/treatment evidence. Historical transactions remain; committed identity correction supplies the current mapping. |
| Location | CorrectReceiptLocation; warehouse/room and optional explicit current source | Exact remaining room allocations, eligible unsealed source/destination. A unique source can be selected by the command; multiple sources need selection. Consumed/external stock is not recalled. |

Cross-kind combinations are rejected before writing anything. The executor owns
one command and transaction; a future composite command would need one complete
intent, one version/fingerprint validation, atomic effect planning and one
rollback boundary. This PR does not sequence independently committed operations.

Receipt-type conversions and transfer evidence edits retain their existing
specialized limitations; completed transfer receipts require the established
reopen workflow. Transfer quantity/custody changes cannot be passed off as
metadata. A linked acknowledgement date needs evidence review. These are
operation constraints, not age restrictions. A raw lot-label edit without a
reviewed structured identity cannot reclassify stock.

Authorization is enforced in Web GET/POST (including the compatibility
AdminInventoryOverride route), both service adapters, API filter/service, and
canonical existing-receipt commands before replay or write. Only Admin permission
for Receipts is required, not global Admin. Create and legacy receipt-edit-only
grants are rejected. `PageAccessLevel.Edit` aliases Create; it is not an additional
stored permission level. Ordinary authorized creation remains tested.

The API keeps `/same-day-fields` and the serialized `SameDayOnly` field for
compatibility, removes the age restriction and retains its name-change QC review
behavior. No in-repository station/WinForms caller of this PUT route was found.
An external Create-only integration must obtain an Admin session for edits.

## TR110045-shaped disposable regression

The test uses a synthetic receipt, never production receipt 2476. It records 13
bins, an explicit treatment, then a 13-bin dump effective 2026-10-07. All original
stock is consumed. A normal rendered Edit POST requests 15. Missing additional
stock evidence fails without writes. Selecting actual current room plus explicit
receipt ownership/presence and known-untreated proof commits exactly +2 through
the established correction-origin command.

Reconciliation: original receipt 13; immutable dump 13; correction +2; revised
receipt 15; current stock 2. The two new bins are a separate untreated cohort with
this receipt's provenance and no inherited historical application. Original dump,
ledger, movements, applications/sources and previous audit rows are byte-compared
unchanged. The operator's reason is audited. No assumed physical return, netting,
rewritten dump or production correction is involved.

## Four baseline identity failures — bounded diagnosis

All four previously failed on untouched main 3002d077 and reviewed #282 head.
This revision leaves their assertions intact and adds diagnostic output, not a
reconstruction fix. Current PostgreSQL reproduction still reports four failures.

The shared fixture deliberately starts each position with 19 authoritative bins
and a 29-bin unassigned projection. That ten-bin excess predates the operation.

| Failing test | Exact observed evidence / classification |
|---|---|
| Normal_run_cancellation_restores_mixed_consumption_to_current_receipt_identities | Restored authority is 19 + 26 = 45. Target raw projection is 36 (29 old + 7 restored), available 26; ten excluded as UnprovenProjection. Treatment slices total 26, signature `u`. Failure is RawProjectionQuantity == authoritative, not quantity loss. A further receipt-availability read also falsely blocks on the old source pool's unknown global ancestry despite the corrected receipt's seven proven target bins. |
| Normal_loss_reversal_restores_mixed_consumption_to_current_receipt_identities | Same 45-bin conservation, 36 versus 26 raw target and exclusion. Same subsequent receipt-specific false block. No treated application is present in this fixture, so this is not evidence for every treated restoration case. |
| Exact_receipt_reclassification_merges_into_existing_identity_without_changing_other_receipts_or_history | Source authority/raw 19/29; target 26/36. Both available quantities match authority and exclude ten each. Corrected receipt has seven proven current bins and no receipt-specific blocker. Failure is the same raw-equality expectation. |
| Receipt_correction_splits_mixed_outside_custody_and_normal_return_restores_each_current_identity | Fails before correction, in InventoryReceiptAvailability. Room authority is zero. Outside parent has 26 bins split into exact receipt 1 = 7 and unassigned = 19, both `u`; current custody and available total 26. Overall ReceiptProvenance is Unknown, so the receipt reader rejects the whole parent and reports no allocation. This is an actual receipt-specific false block, not missing aggregate custody, a corrupt quantity or a treatment leak. |

The first three equality expectations conflict with the current derived-projection
model and PR #281's admission of isolated cohorts without repairing unrelated
excess. They are not grounds to manufacture or silently normalize those ten bins.
They remain failed; assertions after their first failure do not constitute executed
history/retry proof. Diagnostics expose an additional applicable limitation in the
two restoration cases, not a claim that all their behavior is correct.

The fourth problem and post-restoration false block belong to a focused
receipt-specific evidence PR: select independently proven receipt slices across
mixed external custody and exclude proven historical-only receipt references from
unrelated current pools, while preserving aggregate custody and unknown ancestry.
Test identity correction/return and subsequent quantity/location edits with exact
movement membership; do not assign the nineteen unknown bins to receipt 1.
This is not an invitation to implement a general reconstruction subsystem.

Release disposition: these four are not currently named as required tests in
traceability or the standard GitHub contract selector. Nevertheless the proven
receipt-specific false block is an applicable affected-workflow limitation under
the testing/release standard. This report does **not** waive it or claim a green
identity suite. #282 remains draft and **NO-GO for release** until that focused
dependency and the raw-projection assertion contract are resolved and reviewed.

## Validation and release boundary

Validation on 2026-10-14:

- Solution restore and final Release build passed (zero errors; existing warnings).
- 147 affected HTTP/service/date/permission/correction cases passed, zero skips.
  After the final omitted-field guard, all 12 rendered-form HTTP cases passed again.
  Missing timestamp, operation key or version cannot inherit creation defaults.
- Mandatory contracts: 143 passed, zero skips. Architecture-only selection: ten
  passed. Reviewed writer hashes changed only after inspecting the relevant diffs.
- Governance: 29 Node tests and the 20-rule catalog validation passed.
- EF model consistency passed with no pending model change. Changed-file formatting
  and final diff whitespace checks passed.
- The separate identity suite remains three passed / four baseline failures as
  classified above; it is not included in the passing affected count.

The provider is task-owned disposable PostgreSQL 18.6; legacy service/date cases
also use SQLite. Evidence is in local TRX files under `artifacts/admin-edit-final`,
`artifacts/admin-edit-final-http`, `artifacts/admin-edit-architecture` and
`artifacts/governance`. Final commit and GitHub check results are recorded in the PR
closeout. No unrelated full suite, browser JavaScript execution or WinForms MSI
build was performed.

No schema migration is introduced. Local .NET 9 on Windows is not a Linux production
runtime rehearsal. Any eventual release still needs owner release authorization,
passing effective CI and affected workflow gates, fresh independently verified
backup, exact runtime/schema-compatible compensation-aware rollback package,
production-shaped rehearsal, protected fingerprints, strict readiness, bounded
deployment and independent production verification. Previous application rollback
would also restore the older receiver-permission behavior and must be reviewed.

No production repair is part of this PR. TR110045/TR110044/TR110057 and the separate
RED reconstruction retain their separate approval and backup/verification gates.

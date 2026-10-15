# Receipt correction form contract — 2026-10-14

This is the original #282 validation record. The subsequent owner-requested Admin-only, single-Save revision and current release limitations are documented in [the Admin workflow assessment](pr282-admin-receipt-edit-workflow-2026-10-14.md). Earlier receiver permission and button behavior below are historical, not the revised policy.

## Scope and authority

New focused branch `codex/receipt-correction-form-contract`, based on fetched main
`3002d0774f52ee5e94aaef81dbd0d62c6bed335b` (merged PR #271). Specification 1.1.0;
report-only knowledge synchronization identified governance commit `1c263278`.
The owner's “SYSTEMIC RECEIPT CORRECTION FORM VALIDATION FIX” request authorizes
development and a PR, explicitly excluding production correction, merge and deployment.
No production service, database or backup was accessed for this investigation.

Applicable rules: INV-001–006, TRT-001–002, REC-001–002, AUD-001, ROOM-001,
OPS-001 and GOV-001; ADR-001–007. This is a binding/validation defect, not a new
inventory policy. Authoritative receipt origins, ledger quantities and exclusive
custody remain decisive. Exact receipt ownership and treatment proof remain required
for receipt-specific adjustments. The canonical command, writer capability, origin
guard, seals, readiness and release gates are unchanged.

## Confirmed root cause and end-to-end trace

`DashboardDataService.GetReceiptEditAsync` initializes `UpdateReceiptForm`;
`Views/Receipts/Edit.cshtml` renders the edit form. The browser review script changes
visibility, selects rooms and enables allocation/confirmation controls. It neither
creates nor posts a `LotCode` control. The correction button posts to
`ReceiptsController.AdminInventoryOverride`, binds `AdminReceiptInventoryOverrideForm`
and calls `ReceiptInventoryOverrideService.ApplyEditAsync`.

Two separate checks compared the absent internal `LotCode` to the saved receipt:
the receiving-operator authorization branch and `CanonicalEditAsync`. The latter
rejected an otherwise unchanged identity before `CorrectReceiptQuantity` could run.
Existing HTTP tests concealed this by inserting `LotCode` and a full ISO timestamp
directly from the database instead of posting the rendered form.

Before-fix HTTP reproduction on disposable PostgreSQL 18.6:

- Minute-aligned receipt, administrator: **“A receipt identity change requires an
  explicit identity correction.”**
- Same rendered form, receiving operator: **“Receipts Admin permission is required
  for an inventory override.”**
- Both quantity regression cases also failed when switched to rendered controls.
  Initial tests are retained locally in `artifacts/receipt-form/missing-lot-before.trx`
  and `form-before.trx`; these are expected failing baseline evidence, not final gates.

Other unchanged-field mismatches were identified: minute-only `datetime-local`
versus an exact timestamp; canonical grower display aliases versus saved grower text;
and an empty rendered grower number versus the validator's saved `LotCode` fallback.
The receipt-type select also substituted a normalized display value. A nonstandard
stored receipt type is still rejected by the canonical engine's ordinary-receipt
guard; this PR does not silently normalize historical types to bypass it.

## Submitted fields and corrected contract

The ordinary quantity form submits `Id`, `ReceiptVersion`, `OperationKey`,
`CropYear`, `ReceivedAt`, `CompuTechReceiptId`, `ReceiptType`, `WarehouseId`,
`RoomId`, `FruitProfileId`, `GrowerLotId`, `GrowerName`, `GrowerNumber`, `BinCount`,
`ConfirmCropYear`, antiforgery token, `ExpectedConcurrencyVersion`,
`ExpectedInventoryStateToken`, `ExpectedPositiveTrueUpStateToken`, `Reason`,
`ConfirmInventoryChange`, `ConfirmAdditionalBinsUntreated` and
`AcknowledgeNegativeInventory`. Split inventory adds exact `TrueUpAllocations`;
depleted receipts add `AdditionalStockTarget`/`ConfirmAdditionalStockPresent`.
`CorrectionSourceRoomId` stays disabled for a quantity-only operation.
**There is no posted `LotCode`.**

The controller records actual model-binding field presence (including query values
and prefixed form fields) in server-bound `LotCodeOmitted`.
`BindNever` prevents a posted flag from overriding this fact. Omission preserves the
saved identity; an explicitly supplied internal value must still match it. Visible
grower/lot/profile IDs and text remain checked in both authorization and canonical
validation. Invalid model binding is rejected before dispatch. An actual identity
change still requires its separate administrator command and cannot accompany a
quantity change.

The form now displays saved editable grower/type values and the existing null
grower-number fallback. Timestamp validation accepts the exact saved instant or
the exact existing minute-format/binder round-trip. It does not accept arbitrary
different seconds or another minute. The correction command retains the full saved
timestamp, including the original side of an ambiguous Pacific DST hour. Business
time and committed transaction time are not rewritten.

The submitted intent is recorded before replay. The presence flag is serialized
only when true, preserving the existing serialization order/content for callers
that explicitly supplied `LotCode`; the existing property was not replaced or
shadowed. Normal HTTP retries include the same omission marker. Changed intent and
stale fingerprints still fail. No hidden browser field is inventory authority.

## Integrity and affected behavior

Synthetic local consumed-receipt rehearsal: original receipt 13, all 13 treated
and consumed, then a receiver corrects 13 to 15 through rendered HTML and the normal
HTTP endpoint. Without additional-location/treatment confirmation it fails without
writes. With explicit reviewed location and untreated confirmation, exactly two
new bins are recognized in a separate correction cohort. Original consumption,
treatment applications/sources, ledger/movements, identity, timestamps and every
pre-existing audit row stay byte-for-byte equivalent when serialized. The two new
bins have receipt ownership and **no historical treatment membership**.

This is a synthetic loss/consumption scenario, not execution of TR110045 or its
historical Bins Run. The user's production reference (receipt 2476, 13 consumed,
requested 15) is not independent proof of two present bins, their location or
treatment. No production receipt correction is proposed or performed here.

Other HTTP checks cover upward/downward corrections, receiver/administrator access,
unauthorized actors, antiforgery, manipulated/blank lot values, invalid nullable-ID
binding, mixed identity-and-quantity attempts, legacy null grower number, mapped
grower display aliases, minute precision, DST ambiguity, replay and stale forms.
Existing quantity/location HTTP tests now derive their unchanged fields from HTML.
The form helper emulates initial room selection from the view's own data attribute;
these are real MVC HTTP/model-binding/database tests, **not a browser JavaScript
execution claim**. The unchanged JavaScript review/submit flow was inspected.

## Writer review and validation

Two existing writer-candidate hashes need updating: `DashboardDataService.cs`
(read-only form initialization) and `ReceiptInventoryOverrideService.cs`
(pre-dispatch authorization comparisons). Their reviewed classifications and
physical writer boundaries are unchanged. No new origin primitive or writer is
introduced. The new form-contract helper only compares values. The conservative
source scanner also discovers `ReceiptsController` because of `ModelState.Remove`:
review confirms this removes only validation metadata. Its database calls are
`AsNoTracking` reads and its mutations still delegate to existing service/command
adapters. It is explicitly registered as nonphysical; the scanner is unchanged.

Scoped suites
cover form binding, corrections, identity/location separation, partial custody,
mixed treatments, stale/concurrent operations, conservation and PR #281 destination
admission. Backup-specific restored-data tests are outside this local form-contract
run and are not claimed; their underlying command engines are unchanged.

### Existing main failures, not waived

The expanded affected run executed 156 cases: **152 passed, four failed, zero
skipped**. A separate `git archive` of unmodified main
`3002d0774f52ee5e94aaef81dbd0d62c6bed335b`, independently restored/built against the
same disposable PostgreSQL server, reproduced the identical four failures in
`CanonicalReceiptIdentityWorkflowTests` (three other cases passed):

| Existing test | Failure on both main and this branch |
|---|---|
| `Normal_run_cancellation_restores_mixed_consumption_to_current_receipt_identities` | Authoritative 26 versus raw projection 36. |
| `Normal_loss_reversal_restores_mixed_consumption_to_current_receipt_identities` | Authoritative 26 versus raw projection 36. |
| `Exact_receipt_reclassification_merges_into_existing_identity_without_changing_other_receipts_or_history` | Authoritative 19/26 versus raw projections 29/36. |
| `Receipt_correction_splits_mixed_outside_custody_and_normal_return_restores_each_current_identity` | Exact current receipt inventory/treatment ownership cannot be proven; expected success. |

These failures predate the form fix. Whether each projection assertion is stale or
indicates an additional projection defect requires a separate identity-reclassification
investigation; this task does not declare that question resolved. The custody ownership
failure is also retained as an explicit unresolved finding. No assertions, guards or
test exclusions were changed to hide them. This PR is a **draft for review, not a
deployment-ready release**. Local evidence: `artifacts/receipt-form/focused.trx` and
the isolated baseline's `artifacts/receipt-form/baseline-identity.trx`.

The final HTTP rerun covers the last boundary hardening: query/prefixed `LotCode`,
forged omission flags and omitted visible identity fields. The expanded run is not
repeated merely to increase its count; unchanged command engines and their existing
failures remain as recorded above.

### Final local results

| Check | Result |
|---|---|
| Solution restore / Release build | PASS; zero errors. Existing nullable/analyzer and ImageSharp package-advisory warnings remain; dependencies were not changed. |
| Rendered quantity/location HTTP contracts | **9 passed, zero failed/skipped**, including final query/prefix hardening. `artifacts/receipt-form/form-final.trx`. |
| Expanded affected correction/custody/destination suite | **152 passed, four baseline failures, zero skipped** out of 156; detailed above. |
| Untouched-main identity baseline | Three passed, same four failures, zero skipped. |
| Mandatory governance / architecture / inventory contracts | **138 passed, zero failed/skipped**. `artifacts/governance/bf7bd102caf04c04828e68d7368c98a5/contracts.trx`. |
| Governance metadata / rejection-tool tests | PASS; 20 rules / 29 Node tests. |
| EF model consistency | PASS; no pending model changes. |
| Changed-file formatting / diff whitespace | PASS; formatter reports existing workspace-loading warnings. |

Local provider: PostgreSQL 18.6, localhost-only disposable cluster; .NET SDK
10.0.301 building net9.0, installed .NET/ASP.NET runtime 9.0.11. This is not claimed
equivalent to the Linux production runtime. The existing Linux CI `contracts` job
now also executes the nine rendered-form HTTP regressions and uploads their TRX;
its existing mandatory contract step remains intact. Live CI results belong to
the PR head/check records, not a claim of production rehearsal.

The first mandatory-contract attempt failed on the newly discovered validation-only
controller classification and a missing empty launcher database in the fresh local
cluster. Both were corrected explicitly; the final full mandatory run above passed.
No failed test body was removed or skipped.

## Release limits

No schema/model change, migration, installer or station change. No production repair
is bundled. The UI fix still needs separate owner lifecycle authorization, fresh
independently verified backup, exact-candidate affected-workflow rehearsal, current
readiness, compatible compensation-aware application rollback, and independent
post-deployment verification. Rolling back to the prior application may reintroduce
this form failure; retaining schema and newer business activity remains mandatory.
No release safeguard is waived by these development tests.

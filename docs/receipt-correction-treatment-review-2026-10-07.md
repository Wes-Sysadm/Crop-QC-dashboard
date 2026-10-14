# Receipt correction treatment review

## Problem and reproduced evidence

This is an independent repair from [receiving placement PR #268](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/268), based directly on `43abc4e9f22970776145f178100a2a7634426841`, without a stacked dependency. It addresses the positive-correction treatment-copy defect identified during the TR110059 investigation. It does not establish TR110059's production root cause or current custody.

The new PostgreSQL regression first failed against unchanged application code: 19 treated bins became 24 treated bins after a +5 receipt correction. `ChangeReceiptQuantityAsync` copied the selected allocation's signature and application IDs. Legacy `AddReceiptTrueUpAsync` copied its selected segment's application links. Existing legacy tests explicitly expected that inheritance; their expectations are corrected here.

## Behavior and invariant proof

A positive correction now requires the administrator to confirm the additional bins are untreated. The extra quantity retains the receipt origin and current selected location, but is credited to an untreated segment without historical application links. Selecting treated fruit chooses a location; it cannot certify earlier treatment membership. Canonical positive corrections post their ledger and movement at execution time even if a direct caller supplies an earlier effective time.

If additional fruit has uncertain treatment history or needs earlier treatment attribution, the operation fails without writing and directs the operator to evidence review. This change does not provide an evidence-based historical treatment amendment workflow. The confirmation records an accountable operator statement; it is not independent physical inspection. The existing administrator authorization, required reason, exact receipt/custody evidence, stale fingerprint, quantity allocation, transaction, audit and replay requirements still apply.

The regression proves 19 treated + 5 untreated = 24, keeps the application-source snapshot at 19, and then moves both treatment classes without changing total quantity or historical membership. Existing treated fruit, receipt arrival dates, application records, and prior overrides are preserved. Quantity decreases and voids continue to consume the explicitly selected existing allocation. No historical correction is automatically repaired.

Both canonical commands and legacy receiving services enforce the confirmation. New audit data records the confirmed untreated additions; canonical application intent also records the form. Reusing the same operation key with changed confirmation conflicts. The new false/default fields are omitted during JSON serialization to preserve old journal intent shapes and metadata/decrease/void requests. A previously completed legacy operation remains a replay of its historical result, not a newly applied correction.

## Changed components

- `src/CropQc.Data/Inventory/InventoryCommandReceiptChanges.cs:28`: confirmation gate and present-time positive posting; `:88`: untreated destination without copied application IDs.
- `src/CropQc.Shared/Inventory/InventoryCommands.cs:44`: optional, default-omitted correction confirmation; `ReceiptCorrectionTreatmentPolicy.cs:5`: shared actionable error.
- `src/CropQc.Web/Models/DashboardViewModels.cs:1733` and `Services/ReceiptInventoryOverrideService.Canonical.cs:106`: form binding and canonical command intent.
- `src/CropQc.Web/Services/ReceiptInventoryOverrideService.cs:292`, `:1017`, `:1304`, `:1408`: legacy validation, explicit lineage call, audit evidence, and replay comparison.
- `src/CropQc.Web/Services/RoomTreatmentService.cs:2228`: lower-level confirmation and untreated allocation, retaining exact selected-position validation.
- `src/CropQc.Web/Views/Receipts/Edit.cshtml:165`: operator explanation and confirmation shown for increases. The existing file encoding is preserved.
- `tests/CropQc.Api.Tests/ReceiptCorrectionTreatmentTests.cs`: treatment conservation and movement, direct-command rejection, rollback at two boundaries, retry, changed-intent replay conflict, and present-time posting.
- Existing legacy override tests now assert immutable historical treatment plus separate untreated additions; the authenticated receipt HTTP test verifies rejection without confirmation and successful binding with confirmation.
- The reviewed-writer registry refreshes only three already-classified files' source hashes. No new physical writer or guard exemption is introduced.

## Verification scope

Blast radius: receipt quantity corrections, their shared form/journal serialization, positive legacy lineage crediting, treatment/receipt views and HTTP submission. Historical evidence: original receipt arrival, original ledger rows, treatment applications/source snapshots, existing treated quantities, prior override history, and unrelated receipt stock.

Before-fix reproduction: 1 test failed with expected treated quantity 19 versus actual 24. Initial corrected receiving/override suite: 82 passed. Expanded correction/architecture/state-machine run: 100 passed, 0 failed, 1 skipped for a separately configured restored-production fixture. The authenticated HTTP run passed both decrease and increase cases. Final affected-area run: **144 passed, 0 failed, 3 skipped, 147 total**. All three skips require separately configured restored-production fixtures. This run includes treatment tracking/application/reversal and existing transaction races. Seven new cases were added (five command/state cases, one legacy rejection, one additional authenticated HTTP case), and existing legacy inheritance expectations were corrected.

Restore succeeded. Solution build succeeded with zero warnings/errors in the final incremental build. EF reports no pending model changes. Scoped formatting and `git diff --check` passed. No full unrelated suite is required for this independent correction change; PR #268 separately ran the broad suite for its foundational resolver/transaction-owner changes.

## Remaining work and release status

No schema/migration, installer, backup, configuration, production record, deployment, or merge changes. GitHub check/commit details are supplied in the delivery record. Both PRs start from the same current main; neither includes the other's implementation. Their combined candidate needs an integration check before release, and their writer-registry changes must both be retained.

The system-wide request remains incomplete. Render workspace confirmation is still needed for the read-only incident/deployed-version/backup investigation. Partial receipt-held custody, legacy transfer variance, full room eligibility/capacity/segregation policy, and effective seal-time parity remain separate unresolved findings. The 49 received / 1 unresolved case is not implemented by this repair. Production backups and photo 6997/backup #185 are unverified. No production repair should be derived from these synthetic fixtures alone.

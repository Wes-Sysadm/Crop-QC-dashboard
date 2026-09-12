# Batch 1B — Fruit Profile identity guard

## Problem and reproduction

`AdminManagementService.SaveFruitProfile` previously wrote the live profile and
then its audit/color configuration with no operational-use check. Two new guard
regressions were run before changing application code; both failed because
ordinary edits succeeded. In the production-type case, the same 10-bin ledger
position changed from Conventional/false to Organic/true without an inventory
identity correction. A variety-code edit also succeeded. Rows with their own
variety snapshots retain those snapshots, but Receipt-based reporting resolves
the live profile code, so an edit can create contradictory interpretations.

This is a guard, not an inventory correction or data cleanup.

## Fields and consumers inspected

| Field | Classification | Evidence |
| --- | --- | --- |
| `VarietyCode` | Identity | Receipt selection and receiving totals use it; ledger fallback, inventory resolution and correction/replay use it. |
| `FruitType` | Identity | Live ledger commodity, treatment crop eligibility, QC apple/pear terminology and measurement/report paths depend on it. |
| `ProductionType` | Identity | Live inventory status, receiving report grouping, run expectation fallback, treatment/correction identity depend on it. |
| `IsOrganic` | Identity | Live inventory classification, organic reporting and treatment/correction matching depend on it. |
| `Name` | Display | Display labels and canonical **color** alias/key resolution. Operational identity remains profile ID/code/commodity/classification; renaming can change the presentation/color lookup, not bins or reporting snapshot identities. |
| `Description` | Display | Descriptive Master Data metadata. |
| Dashboard color/reset | Display | Existing `VarietyColorService` configuration/audit path, not operational history. |
| `IsActive` | Selection lifecycle | Controls future choices; not an identity attribute. Existing activation/deactivation behavior remains available. |

Traced services: `RoomInventoryLedgerQueryService`, `DashboardDataService`,
`RunReportingService`, `RunExpectationService`, `InventoryIdentityService`,
`ReceiptInventoryOverrideService`, `RoomTreatmentService`, `RunProjectionService`,
`QcSummaryEmailComposer`, `FieldSampleReportService`, `ReceivingExportService`,
and `VarietyColorService`; also the Master Data controller/form/view and entity mappings.

The Master Data UI offers Production Type, **not** an independent organic toggle.
The existing save path derives `IsOrganic` from Production Type and ignores the
legacy form's `IsOrganic` property. New profiles and production-type changes keep
that rule. Cosmetic edits preserve the stored flag, including pre-existing unusual
combinations: this batch does not silently normalize historical data. A crafted
independent flag cannot change classification. Both Organic-to-Conventional and
Conventional-to-Organic edits on used profiles are blocked.

## Operational-use boundary

Save-time checks cover any reference (including deleted/test/depleted/reversed
history and older revisions), not just current positive inventory:

- Receipts; room adjustments, depletions, losses and internal transfers.
- Bins Run entries, including `ReportingFruitProfileIdSnapshot`. Actual Run
  revisions retain these entries; legacy expectation generation can still fall
  back to live profile fields even though complete run reporting snapshots are
  independent of the live classification.
- Receiptless QC/Field samples via `FieldSampleFruitProfileId`; receipt-backed QC
  is covered by the Receipt. No Field Sample workflow is modified.
- Treatment segments and application sources: snapshots retain original evidence,
  but identity matching, resolution and replay still depend on the profile.
- Outside transfers, inter-crew transfers and processor lines: snapshots do not
  remove the current/reversal identity relationship.
- Identity corrections, both source and target, including incomplete/inactive
  records; operational Run Projection sources and Actual Run override request lines.

Not all foreign keys count. Variety colors, starch-scale setup and commercial-pack
restrictions alone are configuration, not operational use. Fully captured
`RunExpectationSource` calculations have immutable classification snapshots and do
not resolve classification from the live profile. They are not independently a
reason to freeze a profile; their actual Bins Run source, when present, is covered.
Audited JSON snapshots are not rewritten and are not treated as live references.

## Save, concurrency and rejection

All identity checks occur before assigning any fields, writing audits or saving
colors. A mixed identity/cosmetic edit is rejected as a whole, with guidance to
create a new profile or use a controlled identity correction. The existing
controller redirects to Edit and displays the error; permissions are unchanged.

The transaction is limited to Fruit Profile saves, including their audit/color
path. PostgreSQL locks the profile row `FOR UPDATE NOWAIT`, reloads it instead of
trusting EF's tracked instance, and, for identity changes only, takes short SHARE
locks on the explicitly checked operational tables. This covers concurrent first
use even through snapshot IDs that have no profile FK. READ COMMITTED checks then
see previously committed references; locks last through the profile commit.
Lock contention fails closed with a refresh/retry message; it does not wait behind
operational writers. Concurrent operational writes may briefly wait while this
short transaction owns the locks. No external network/storage call is made.

Profile audit snapshots contain scalar profile values rather than loaded Receipt
navigation graphs. An audit failure rolls back the local profile save. This does
not refactor other Master Data save transactions or implement the separate F17 batch.

## Blast radius and validation

Changed area: ordinary Master Data Fruit Profile saves and their explanation.
Successful writes remain confined to the profile, its existing audit, and optional
color configuration. Rejected identity edits write none of these. No Receipt,
ledger, correction, treatment, run/revision or reporting snapshot is rewritten.

Focused proof covers each operational reference separately, zero-current history,
unused/create flows, mixed-request zero writes, both classification directions,
color/name/description/active maintenance, legacy-flag preservation and stale EF
state. Authenticated HTTP proof covers the error presentation, exact Receipt
classification read-back and permissions.

An isolated, synthetic PostgreSQL 18 test covers first-use contention, snapshot-table
contention, concurrent profile edits, committed use, cosmetic/color success and an
injected audit failure/rollback. Opt in with `CROPQC_TEST_FRUIT_PROFILE_POSTGRES`
pointing to a **new, empty, disposable** database. Without it the test is explicitly
skipped. Never point it at production.

Change-scoped regressions: Fruit Profile/color maintenance, Receipt variety HTTP
selection/creation, current inventory classification and identity, Actual Run
reporting identity and Master Data authorization. No full application suite,
production restore, deployment, correction, email send, unrelated feature work or
WinForms installer is required or performed.

Existing concern outside this batch: the Master Data Save action has no
antiforgery validation attribute/global enforcement. A diagnostic tokenless POST
returned a redirect rather than HTTP 400. Batch 1B does not change this existing
behavior; antiforgery work is explicitly excluded. Receipt QuickAdd's existing
antiforgery regression remains part of the scoped tests and is unchanged.

Migration: **None**. Production changes: **None**.

Executed on the Batch 1B candidate:

- Restore and solution build: PASS (zero errors; existing compiler warnings).
- Final change-scoped suite: **122 passed, 0 failed, 0 skipped**.
- Separate fresh PostgreSQL 18 concurrency/atomicity matrix: **1 passed, 0 skipped**.
- EF pending-model check: clean; formatting verification and `git diff --check`: PASS.
- Synthetic databases and their local PostgreSQL cluster are removed after validation;
  test result logs remain outside source control. No production data was used.

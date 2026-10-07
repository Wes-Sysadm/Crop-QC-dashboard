# Authoritative custody implementation

## Affected area and verification plan (recorded before implementation)

Base: `95518dec131d44b01e2dc8fcfcbb82f508a8f399`, current remote main inspected October 7, 2026. Branch: `codex/authoritative-partial-custody`.

The October 7 A–P review, PRs 251/252/254/255, and open drafts 268/269 were reviewed. The two draft commits are integrated on current main with their original authorship; neither draft is assumed merged. This independent PR must coordinate/supersede their overlapping changes rather than merge three divergent implementations. PR 267 backup changes remain intact.

The blast radius is receiving, room placement, dispatch, transfer acknowledgement and partial settlement, receipt corrections, treatment allocation, projection normalization, inventory readiness validation, and their Web/API writers. Shared dependencies include the canonical executor, evidence resolver, room ledger and persistence guard. Tests will assert persisted quantities, immutable movement/application history, receipt and transfer versions, audit relationships, replay, rollback and concurrent consumption using disposable databases only.

Historical receipts, dispatch allocations, application memberships, ledger entries and audits must be retained. No historical backfill or production correction is authorized. Additive custody records must preserve the original transfer relationship and distinguish received quantity from unresolved quantity; a displayed room total is insufficient proof.

Verification proceeds with affected-area tests, PostgreSQL integration tests, restore/build, model consistency, formatting and diff checks. A final broader test run is justified only by modifications to shared inventory/persistence contracts with consumers across receiving, transfers, runs and corrections. No station/WinForms changes or installer work is planned.

## Policy boundaries

An acknowledgement can move only proven dispatched allocations. A shortage remains under its original transfer; a later acknowledgement consumes only its remaining allocation. An overage cannot become received transfer inventory. Allocating a partial receipt among different source lots or treatment histories requires explicit allocation evidence; variety totals alone do not authorize choosing which fruit arrived. Disposition of missing bins (loss, return, corrected dispatch tally) requires evidence and an audited existing operation; acknowledgement does not choose that policy.

The owner explicitly confirmed that any receiver may correct a mistaken bin count, including increasing it. This is a receiving observation correction, retained with actor, reason, original count, corrected count and an operation key. An outside document or administrator approval is not required for a receiver's quantity-only correction. Identity/location corrections and voids retain their separate permissions. Added quantity is a new untreated arrival at correction time, never membership in a past treatment.

The owner also explicitly required exact dispatch allocation selection for partial receipts. The form shows each original allocation, lot, variety, source receipt and treatment applications. A variety total is a limit on acknowledgement, not permission to choose an arbitrary allocation.

## Implementation and results

This is a draft implementation for review, not release approval. No production data, deployment, backup, merge, or historical repair is part of this work.

### Custody and conservation

Two additive tables retain immutable `ReceiptCustodyAcknowledgments` and `ReceiptCustodyPlacements`. An acknowledgement links the receiving receipt to one original dispatch movement. A placement consumes an acknowledgement and links one destination ledger credit and treatment movement. The command journal, database uniqueness and parent versions protect replay and competing submissions.

For a load, `dispatched = unresolved + receipt-held + placed`. For each dispatch allocation, total acknowledgements cannot exceed dispatched quantity minus legitimate returns. Each acknowledgement must fit the receipt's observed variety quantity; total placements cannot exceed that acknowledgement. Receiving observations and acknowledgements never independently credit room inventory.

| Event | Unresolved | Receipt-held | Placed | Completion |
|---|---:|---:|---:|---|
| Dispatch 50 | 50 | 0 | 0 | In Transit |
| Observe/acknowledge exact 49 | 1 | 49 | 0 | Incomplete |
| Place those 49 | 1 | 0 | 49 | Incomplete |
| Observe/acknowledge later original bin | 0 | 1 | 49 | Incomplete |
| Place final bin | 0 | 0 | 50 | Received |

Every row in this sequence is asserted against persisted PostgreSQL state, including source debit, immutable original dispatch, destination credit, custody readers, audit and readiness. Replaying either command leaves every table unchanged. The transfer becomes Received and sets receipt completion only when all dispatched bins are acknowledged and placed and the observed receipt count matches. A sealed destination can hold acknowledged fruit but cannot accept placement. Ordinary receiving continues to create receipt and room custody atomically; it does not use a separate held stage.

### Corrections and defect closures

- Receivers can submit quantity-only corrections. They cannot use the permission change to reclassify identity, change locations, or void receipts. Reductions cannot exceed proven remaining inventory; an acknowledged negative-balance checkbox no longer authorizes missing bins.
- Canonical receipt corrections retain auditable before/after receipt and position snapshots, use the proper adjustment type, and record positive arrivals with an incoming movement at correction time. Generic positive corrections cannot inherit an existing treated signature.
- Zero-quantity status-alias projections are retired during normalization, allowing an empty authoritative position to accept legitimate receiving. Their history remains intact; no zero replacement projection is created.
- Readiness validation understands canonical identity/location correction snapshots and their actual adjustment types while retaining the legacy contract. External custody in a correction audit does not authorize a room debit.
- Legacy API creation without a physical inventory adapter is rejected. Same-day API edits cannot mutate quantity, effective date or inventory identity. Canonical API receiving remains available with authenticated receiving permission and operation keys.
- Legacy internal receipts require the exact dispatched quantity. A discrepancy review note cannot settle a shortage. A legacy run shortage approval or depletion override cannot consume additional bins beyond known availability.
- Legacy treatment materialization uses authoritative ledger quantity. When treatment history exists, a projection remainder requires independently provable untreated arrivals. Missing or ambiguous history is displayed as unavailable for movement, rather than silently assigned to an untreated slice. Backdated treatment uses the application time, not the later database write time, to distinguish subsequent arrivals.
- Receipt-held custody is included in receipt availability, transit evidence and conservation. Whole-load completion/reopen/manifest-edit paths cannot bypass partially acknowledged custody. Custody records cannot be modified or deleted by tracked writes, even inside a canonical transaction.

### Writer coverage

The complete coverage inventory is [receiving-custody-writers.csv](inventory-architecture/receiving-custody-writers.csv). It covers all 32 previously catalogued workflow families plus the two new acknowledgement/placement endpoints, with quantity, custody, reconciliation, treatment, audit, concurrency and regression references. The existing [workflow registry](inventory-architecture/phase3-workflow-registry.json) records the new command kinds. The [reviewed writer candidates](inventory-architecture/phase3-reviewed-write-candidates.json) and architecture tests include the new physical entities and executor/read-only files.

The source scanner is a conservative candidate inventory, not a proof of control flow. Protection also depends on the runtime tracked/SQL write guards, serializable executor and persisted-state regression tests. Canonical mode is required for partial custody. Once canonical commands exist, disabling configuration does not reopen legacy physical writers. Retained legacy code has the specific bypass closures above; this PR does not claim that every historical row is valid or automatically convert legacy records.

### Treatment integrity

Acknowledgement retains the original dispatch relationship; it cannot apply treatment. Placement carries that allocation's original application memberships and effective reversal state. It does not inherit destination room treatments. A treated 19-bin dispatch can acknowledge and place 18 while keeping the original application/source snapshot at 19 and the remaining original bin unresolved. Additional receiving corrections create untreated quantity separately. Whole-load reconciliation across the six existing EBS/WP/WP DH/McDougal directions continues through the existing route policy; no route policy is replaced.

### Remaining policy and implementation boundaries

| Question | Existing behavior / alternatives | Current implementation |
|---|---|---|
| What happened to an unresolved bin? | Later arrival, confirmed return, dispatch tally error, or documented physical loss have different physical effects. | Later arrival is implemented. No automatic loss, return or fabricated received bin. |
| How should a mistaken acknowledgement or partly settled transfer be corrected? | Reverse a specific held allocation; reverse surviving placed quantity; record an evidenced loss; or return a specific unresolved allocation. Each needs its own actor/evidence/quantity contract. | Existing whole-load reopen and manifest-edit actions are blocked once acknowledgement exists. A separately reviewed allocation-specific compensating workflow is still required; editing acknowledgement history is forbidden. |
| Can a count increase reuse historical treatment? | Owner permits receiver count correction; that does not prove presence at an earlier application. | Increase is untreated at correction time. A past-treatment correction needs separate evidence and is not inferred. |
| Can ambiguous old lineage be normalized? | Replay may prove an exact origin; otherwise assigning a history would be a guess. | Proven normalization only. Ambiguous legacy states remain unavailable and need a reviewed repair proposal. |

These boundaries are visible limitations, not requests to delay independently safe work. No supported action should silently settle the unresolved remainder. The requested architecture's broader claim of complete historical correctness still requires scoped production-shaped rehearsal and evidence for any legacy ambiguities.

### Schema, rollback and historical data

Migration `20261007212154_ReceiptHeldTransferCustody` adds only the two custody tables, restrictive foreign keys and operation/link uniqueness. It changes no existing inventory row. Startup schema expectations include their columns. The PostgreSQL migration test applies from the prior schema, checks preservation, permits an empty downgrade, and rejects destructive downgrade after acknowledgement. EF model consistency is checked separately.

Before any future authorized release, apply the repository's verified-backup/release gates and rehearse this exact candidate on a disposable production-shaped copy. Preserve the additive schema during application rollback. Once partial custody is used, rollback must use a custody-aware application; an older application does not understand receipt-held bins. Do not drop evidence or restore an old database over later activity.

Potential production repairs remain separate: prove and retire stale aliases/projections; investigate negative authoritative positions using original receipt corrections and movements; reconstruct treatment membership only from original application evidence. No backfill, repair SQL, receipt adjustment or TR110059 mutation is executed by this PR. The original production investigation and its unavailable historical error evidence are not re-certified by local tests.

### Integration and validation

Remote main remains `95518dec131d44b01e2dc8fcfcbb82f508a8f399`. PR 268 (`6fe32b3986c9cb5aa87665f1c821edace011dd04`) and PR 269 (`c2d366733d770d413643bfb1abd7640c03fa94e4`) were still open drafts when refreshed. Their commits are cherry-picked onto this independent main-based branch, then extended here. Coordinate their supersession before merging any overlapping PR; this is not a stacked PR. PR 267 backup changes are preserved, with no backup implementation diff.

Local PostgreSQL databases are disposable, synthetic and loopback-only. No restored-production or onsite/hardware claim is implied. No WinForms change or MSI is required.

| Verification | Result |
|---|---|
| Solution restore | Passed; sandbox NuGet networking required an escalated local restore |
| Solution build | Passed; 0 errors, 64 existing warnings on the clean affected build |
| Full application suite | 2,246 passed, 0 failed, 34 explicitly skipped; 2,280 total; run once with the existing bounded PostgreSQL runsettings |
| Final custody regression group | 13 passed, 0 failed/skipped, including rendered HTTP form/post, permission, antiforgery, unknown commit outcome, rollback, concurrency, sealed placement and identity correction across held/transit custody |
| Additional mixed allocation regression | 1 passed, 0 failed/skipped: two lots of one variety, one treated and one untreated; selecting the untreated arrival leaves the exact treated allocation unresolved |
| Focused legacy/receiving boundaries | 72 passed, 0 failed/skipped |
| Writer architecture gate | 9 passed; 34 coverage rows and 96 reviewed source candidates |
| Model/migration | EF reports no pending model changes; new migration tested on disposable PostgreSQL with history-preserving downgrade guard |
| Formatting/diff | Changed C# whitespace formatting verification and `git diff --check` passed |
| User interface | Actual Razor HTTP GET/POST tests passed; interactive browser and responsive visual inspection not performed |
| Production/release | No production access or mutation for this implementation; no backup operation, merge or deployment |

The final custody group and mixed-allocation test include tests added after the single full-suite run; their counts overlap the full suite and must not be added as if they were a second full-suite result. The final read-model correction for acknowledged identity overlays was verified by those focused tests. The [34 skipped cases and exact reasons](inventory-architecture/receiving-custody-skipped-tests.csv) identify missing opt-in backup/provider/restored-data fixtures. Tests that internally return when an optional fixture is absent are not evidence of a restored-production rehearsal.

An intermediate broad focused run exhausted the disposable PostgreSQL lock pool under unrestricted test parallelism. Re-running with the repository's existing four-thread PostgreSQL runsettings resolved that environment limitation. Earlier failures also exposed the legacy variance/negative-override expectations, incomplete mock arrival evidence, and changed correction button text; the revised tests assert the new persisted behavior rather than accepting the old unsafe behavior.

Release still requires scoped production-shaped rehearsal, inspection of ambiguous historical records, interactive/responsive receiving checks, and the repository's explicitly authorized backup/deployment gates. Draft status does not authorize those actions.

### Delivery record

- Draft PR: [#271](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/271), open and draft; GitHub reported a clean merge state and no Actions/check results at creation.
- Branch: `codex/authoritative-partial-custody`.
- Verified implementation commit: `788e9c3cfe43334cb772a81a4f032b3199642177`; subsequent delivery-record changes are documentation only.
- Base and current remote main: `95518dec131d44b01e2dc8fcfcbb82f508a8f399`; no update from newer main was necessary.
- Final incremental solution build: 0 errors, 7 existing test warnings; the earlier affected build emitted 64 existing warnings. Restore, model consistency, formatting and diff checks passed.
- Working changes are committed and pushed. No production data was modified, and no merge, deployment, backup run, historical repair or MSI build was performed.

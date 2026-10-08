# Historical projection reconstruction — review and approval evidence

Date: 2026-10-08 UTC. **Draft implementation; no production authorization.** Production was not queried or changed during this implementation task. No deployment, PR merge, production migration, backup run, retry, or configuration change occurred. All mutations described below occurred in disposable local PostgreSQL databases.

## A. Root cause and design

Legacy materialization treated blank and redundant `Conventional` status suffixes as different identities. Later movements consumed a newly materialized alias while the earlier representation stayed positive. A room card or a matching remaining segment alone cannot establish custody. The ledger and immutable movement/treatment evidence independently prove three untreated shared pools; receipt corrections leave three others unproven.

This implementation supersedes projections only. It does not deduct the excess from physical inventory, restore voided receipts, modify readiness rules, or assign an exact surviving receipt to a shared pool. The historical deployed SHA for each September event remains unverified; the mechanism is supported by source and record chronology.

Reviewed references: [#271](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/271), [#272](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/272), [#270](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/270), [#251](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/251), and [#255](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/255). The four required investigation documents were reviewed at #272 commit `9fcca90e206460d55b030a87017d587212b5a29f`: [report](https://github.com/Wes-Sysadm/Crop-QC-dashboard/blob/9fcca90e206460d55b030a87017d587212b5a29f/docs/investigations/six-lineage-blockers-2026-10-08.md), [evidence](https://github.com/Wes-Sysadm/Crop-QC-dashboard/blob/9fcca90e206460d55b030a87017d587212b5a29f/docs/investigations/six-lineage-blockers-evidence-2026-10-08.md), [proposal](https://github.com/Wes-Sysadm/Crop-QC-dashboard/blob/9fcca90e206460d55b030a87017d587212b5a29f/docs/investigations/six-lineage-blockers-proposed-repair-2026-10-08.json), [integrity](https://github.com/Wes-Sysadm/Crop-QC-dashboard/blob/9fcca90e206460d55b030a87017d587212b5a29f/docs/investigations/six-lineage-blockers-integrity-2026-10-08.json). No broad production reinvestigation was repeated.

## B. Implemented reconstruction framework

`InventoryCommandExecutor` owns four distinct maintenance operations: read-only preview, recorded approval, execution, and independent read-only verification. The server CLI is `--reconstruct-historical-projection` with `--mode=preview|approve|execute|verify` and `--request-file`. No ordinary controller or page calls it; no startup repair runs automatically. Typed request contracts are in `ProjectionReconstruction.cs`. Preview returns exact before snapshots and retirement/replacement plans without writes. The separate pure evidence classifier is diagnostic and cannot authorize execution.

Execution requires an active authorized administrator **plus** explicit operator intent, a separate recorded approval of the entire preview, external approval reference, reason, and backup evidence. Revalidation binds the exact warehouse/room/canonical identity, segment IDs and versions, raw keys, quantities, receipt references, treatment state/signature/application IDs, movement and historical-audit fingerprints, and all protected tables. Negative ledger entries require exact complete untreated outgoing movements. The ordinary canonical resolver and normalization planner independently prove the pool without using projection quantities. The operation only supports positive proven alias excess; depleted, negative, ambiguous, incomplete and unsupported positions remain excluded.

Approval and execution use serializable transactions and `SHARE ROW EXCLUSIVE NOWAIT` locks on protected operational tables. This also prevents insert phantoms and legacy writers from interleaving. It is deliberately conservative: a competing writer causes immediate refusal, and unrelated changes stale the preview. These table locks briefly block ordinary writes while a transaction holds them; any future production application needs a bounded maintenance window and fresh approval for each pool. Preview uses a read-only serializable transaction, matching execution's evidence isolation label.

The existing `CanonicalProjectionFactory` retires every exact positive row as `Historical`, preserving its original receipt and treatment metadata, recording retired quantity/time/operation key and incrementing its version. One canonical untreated shared replacement has `ReceiptId=null`. The operation re-reads authority and treatment before commit, compares protected records, then atomically appends a full repair audit and `InventoryCommandRecord`. It creates no balancing ledger entry and deletes nothing. No schema migration is added.

Approval is an append-only `ProjectionReconstruction/Approve` audit under source `CanonicalProjectionReconstruction/v1`. Execution appends `Reconstruct` with the approval ID, complete original row snapshots, replacement state, evidence and zero physical delta. Existing audit infrastructure is reused; this is application-enforced append-only behavior, not a claim that a database owner cannot tamper with rows.

An exact operation-key/payload replay returns the persisted result. A reused key with different intent is rejected. Before commit, failures roll back projection and audit changes together. A lost commit acknowledgment is checked independently; if that lookup is unavailable, the result is `OutcomeUnknown`, never an asserted rollback. Retry must retain the identical key and payload. Independent verification checks every preserved historical field against its original snapshot and validates the audit, command, replacement and protected-data fingerprint. Later legitimate activity produces `EvidenceChanged` and requires review.

Production approval requires a retained, succeeded and verified **PreDeployment** backup completed within 24 hours, matching SHA-256 and an independent verification reference. That reference records external verification; the repair command does not download or revalidate a durable backup itself. Local bypass requires explicit disposable confirmation, localhost, and a restricted test/rehearsal database-name prefix. No backup was triggered here.

## C. Historical receipt-revision validator

The read-only validator checks original arrival quantity/identity; complete uniquely keyed revisions; quantity/void action; unchanged snapshot identity; before/after version and deletion transitions; exact ledger amounts, parents, room/warehouse/profile/lot and timestamps; audit actor/action/time and snapshots; intermediate revision continuity; and final receipt identity/count/version. It reports negative ledger entries missing allocation evidence. The canonical resolver separately assesses movement chronology, point-in-time treatment, current authority and exact receipt provenance.

All three documented correction chains are arithmetically consistent. None proves surviving receipt custody or untreated stock in its affected pool. Voids retain the receipt's historical stored count but have effective quantity zero. The correction reason is never a proof input. Unsupported identity/location revisions, malformed snapshots, missing debits, altered audits, duplicate operation keys and changed current identities remain unproven.

WP-7 also reports receipt 626's audited 56→64 correction as a valid quantity chain, without claiming 72 surviving bins belong to it. Receipt 720 has an unsupported revision shape for this quantity/void validator. Neither diagnostic alters the independently proven transfer-based shared-pool evidence.

## D. Three proven-pool rehearsals

Backup #186 was independently checked for size **17,317,796 bytes**, SHA-256 **0170afc86576e3c57483ebdf1cc3b431555b545bf1cd9e454e7e020bdf22ccc0**, archive CRC/safe entries and four components. The database dump was 311,151,169 bytes and the manifest represented 11,768 photos. Snapshot: **2026-10-07 23:53:37.406346 UTC**, deployed commit `e58d73a12c8bf0cc5f0f2333e442497e2b91b807`. A fresh local restore completed at **2026-10-08 03:47:27.810688 UTC**. No new backup or production download was needed.

| Pool | Authoritative before/after | Projection before → after | Exact retired rows: quantity/version | New shared quantity |
|---|---:|---:|---|---:|
| Evans-5, room 15, GL511/profile2, lot3152 | 61 → 61 | 162 → 61 | 414:101/v3; 426:61/v3 | 61 |
| Evans-5, room 15, GL642/profile2, lot9682 | 252 → 252 | 536 → 252 | 419:284/v3; 428:252/v3 | 252 |
| WP-7, room4, GL448/profile17, lot1372 | 1,122 → 1,122 | 1,568 → 1,122 | 617:134/v4; 618:604/v3; 619:250/v5; 630:324/v3; 669:72/v2; 670:184/v2 | 1,122 |

Both standalone and combined rehearsals committed exactly these three repairs, independently verified each immediately, and replayed each successfully without a second write. Ten rows were retained as Historical; three receiptless rows replaced them. The standalone generated IDs were 806/807/808 with repair audits 174804/174806/174808. These are **local generated IDs**, not prescribed production IDs. Six new audits comprise three approvals plus three repairs; three idempotency records were added.

The independently supported arithmetic is 101−40=61; 104+180−32=252; and 446−122+798=1,122. WP-7's 446 excess includes a 324 duplicate remainder and 122 already-consumed historical representation. Transfers 16–22 and 34 are recorded as fully received, not partial or in transit. Source repair audit 145038 remains unchanged; its prior 802-bin representation must not be reintroduced. See the [machine-readable rehearsal evidence](projection-reconstruction-rehearsal-evidence.json) for exact plans, states, full readiness output and hashes.

## E. Three unresolved pools and required operational evidence

| Pool | Authority / projected / excess | Correction | Exact missing proof |
|---|---|---|---|
| DH-15 room47, GL495/profile18, lot2350 | 202 / 598 / 396 | Receipt1373, void66; override `0f977af1-1376-4fa7-91a4-a56bbf512fb0`; audit123203; ledger3000 −66 | Original and alleged duplicate ticket numbers; receiving/scale records identifying whether 66 physical bins arrived once or twice; exact allocations between that fruit, later transfers369/370 and the remaining202; treatment records tied to those allocations. |
| WP-5 room2, GL398/profile2, lot1084 | 10 / 130 / 120 | Receipt1339, void6; override `61ff4cac-48ce-4331-8306-8d2b0bdd21a8`; audit120803; ledger2939 −6 | Correct replacement variety/lot/receipt for the six incorrectly ticketed bins; signed correction-to-custody reconciliation; run67/entry221 and run102/entry357 allocations establishing the remaining10; corresponding treatment evidence. The conservative complete-history check also flags older debit135/221 evidence gaps. |
| WP-8 room5, GL495/profile18, lot2350 | 170 / 362 / 192 | Receipt1754, 64→0; override `95eeef85-d8d8-4da4-a2b2-dfbd68e27656`; audit126585; ledger3142 −64 | Exact original DH ticket and WP duplicate linkage; dispatch/receipt records proving one physical arrival; reconcile transfer369/370 and runs91/110/115 to the remaining170, including the corrected64; treatment history for those exact allocations. |

For every case, personnel should provide dated receiving/dispatch/run records, identify the responsible operator and correction, reconcile current onsite count by lot and treatment allocation, and explain every discrepancy against subsequent movements. A current count alone cannot establish earlier treatment or receipt lineage. A supervisor's assertion, administrative role or correction reason cannot replace that chain. All three execution attempts returned `Blocked`; no projection, authority or historical audit changed. Rows 407/557, 65/597 and 552/553 remain intact.

## F. Broader historical classification

The same classifier ran over the previously captured 1,024-position evidence corpus without database writes. Outputs are deliberately separate:

1. [Proven candidates](projection-reconstruction-candidates.json): exactly the three pools above, 1,435 authoritative bins and 831 excess projected bins.
2. [Additional evidence](projection-reconstruction-additional-evidence.json): **35 positive unresolved positions / 10,265 authoritative bins**, separately from **13 depleted positions / 644 positive projected bins** in seven rooms. The 35 comprise 29 UnknownTreatment, three MixedTreatmentAmbiguity and the three correction cases. These three correction cases are already included in the 35. No total here represents missing physical fruit.
3. [Authoritative problems](projection-reconstruction-authoritative-problems.json): room60/GL141 −40, room60/GL477 −18, room60/GL502 −5, room61/GL476 −20, room61/GL478 −44; five positions totaling **−127**. No balancing receipts or true-ups are proposed.

Every exact warehouse, room, canonical identity, segment ID and blocker is in these outputs. The classifier found no additional eligible pool among the 35, 13 or five. Future defects may use the same framework when their evidence satisfies its proof requirements; it currently refuses even potentially harmless depleted cleanup and unsupported receipt-location/reclassification chains.

## G. Treatment integrity

Every replacement is `Untreated`, signature `u`, with no application links, based on independent movement chronology. The resolver was not changed. Treatments remain bound to inventory present at application time; no earlier room treatment is attached to later arrivals or unrelated lots.

The separate **106 treated bins in McDougall room65, segment503**, remain protected. Application1/source4 follows their August18 19:11 arrival and 20:09 treatment; identity correction movement450 and ledger2738/2739 preserve that history. This branch has no outgoing lineage into WP-7. All treatment applications, application sources, segment links, movements and unrelated segments—including 503—have identical before/after fingerprints in both rehearsals. Source room21's application19 belongs to receipt990/lot9722 and was not applied to Evans-5's other GALA pools.

## H. Inventory conservation

All six authoritative quantities remain **61,252,1,122,202,10,170**, totaling **1,817**. Only 831 false projected bins are removed, through audited supersession; physical delta is **zero**. The unresolved pools retain their combined 708 excess projections.

Protected fingerprints cover receipts/variety lines, all ledger adjustments, room/inter-crew/outside transfers, run entries/parents/revisions, losses, receipt overrides, identity corrections, treatment applications/sources/links, immutable movements, depletions, processor shipments/lines, room/warehouse/lot/profile metadata, original command history and all original audits. Unrelated segments are separately compared. Exact approved superseded/replacement rows and newly appended repair records are the only exclusions. Every protected hash matched, standalone and combined. These are restored-snapshot results, not a fresh claim about current production.

## I. Full readiness before/after

| Check | Standalone before → after | Combined #271/#272 before → after |
|---|---|---|
| Full release readiness | FAIL → FAIL | FAIL → FAIL |
| Schema | PASS → PASS | PASS → PASS |
| Inventory deductions | PASS → PASS | PASS → PASS |
| Negative adjustments examined | 1,079 → 1,079 | 1,079 → 1,079 |
| Treatment-lineage blockers | 6 → 3 | 6 → 3 |
| Custody topology | PASS → PASS | PASS → PASS |

The remaining blockers are DH-15/2350, WP-5/1084 and WP-8/2350. The new diagnostics did not downgrade them. Full readiness exits 1 before and after. The 35 unresolved positives and five negative canonical positions also require separate operational attention; absence from this particular stored-excess gate does not certify them.

## J. PR #271/#272 integration

The disposable branch `codex/projection-reconstruction-integration` combines this implementation with #271 `58e6d29edc9d81326274a3d7ff73c56dde20fcc5` and #272 `9fcca90e206460d55b030a87017d587212b5a29f`. No GitHub PR was merged and this draft remains directly based on main.

The code overlap was Program dispatch and the reviewed-writer registry; Program merged cleanly, while the registry required retaining both sets of entries and recalculating the actual merged source hashes. #272's diagnostic service hash needed refreshing after combination with #271's reviewed registry. Ordinary write-capability allowlisting retains both changes. There is no shared-resolver/readiness-rule change in this PR.

Only #271's three additive migrations were applied to a disposable restored clone. The combined rehearsal reproduced exactly three successful repairs and three refusals. After reconstruction, every repaired pool still rejects `RequireExactReceipt`; WP-7's receipt attribution remains ambiguous, Evans-5's remains unknown. No legacy representative receipt reference became exact held custody. #271's partial receipt and compensation workflows are included in combined automated validation.

## K. Automated validation

Validation results and exact skipped/conditional test coverage are recorded in [validation evidence](projection-reconstruction-validation.json). The full suite was explicitly requested by the user. No browser, hardware, installer, production database or deployment test is claimed.

| Validation | Result |
|---|---|
| Final standalone reconstruction, revision validator and architecture | **28 passed, zero failed/skipped** |
| Full combined suite, with failed cases resolved by targeted reruns | **2,400 distinct cases passed; zero remaining failures; four skipped** |
| Reconstruction/revision cases within combined suite | 19 passed |
| Partial receipt custody/compensation cases | 34 passed |
| Treatment/readiness cases | 292 passed |
| Backup regression cases | 63 passed, two of the four skips fall in this group |
| Writer architecture | Nine passed |
| Restore/build | Passed; final full rebuild 69 warnings, zero errors |
| Formatting and diff whitespace | Passed |

Coverage groups overlap and must not be summed. The full bounded run itself recorded 2,391 passed, nine failed and four skipped. The first targeted rerun resolved four; the final five passed on fixtures matching their historical preconditions. This is not a claim of one pristine green full run. The fresh #186 command-restore, actual-run and state-machine cases passed; the legacy tests needing unused historical IDs or empty command history used the previously verified #184-shaped fixture (SHA-256 `443eb7def03a0d815c3b31e16512c8e9385b644ec6200823555ac02ffed72eac`). Both new reconstruction rehearsals used #186.

The four explicit skips are the older backup additive-migration fixture, pre-feature truck worker schema compatibility, and the two 15-load legacy adoption cases. Their distinct fixture preconditions were not supplied. Separately, **51 optional methods / 57 guard locations** returned early because their external historical/PDF/workbook/provider settings were absent; [the exact manifest](projection-reconstruction-optional-fixtures.csv) identifies them. xUnit reports these as passed, but they are not provider coverage. Existing ImageSharp advisories and nullable/analyzer build warnings remain; no dependencies were changed.

Focused tests exercise duplicate and repeated submission; failures after validation, retirement, replacement and immediately before commit; lost commit acknowledgment; unavailable independent commit lookup; conflicting receipt, transfer, treatment, consumption and segment writer locks; stale versions, quantities, receipts, treatment and approval; absent movement; depleted/negative positions; ambiguous treatment; forged/subset/unrelated targets; and inconsistent revision snapshots, chronology, audit and identity. Failed operations compare protected-table snapshots before/after. Concurrency tests deliberately hold PostgreSQL writer locks on the relevant tables; the combined existing workflow suite separately exercises ordinary operations concurrently.

The first unbounded full-suite attempt was stopped after local PostgreSQL lock-memory exhaustion and fixture naming failures. The bounded rerun uses the repository's four-thread settings. A legacy fixture incorrectly required no historical segments in any backup: it now checks that condition only when adding the original columns to an older schema; full original-column fingerprints still prove preservation for newer backups with existing audited retirements. The truck worker fixture must use a genuinely migrated disposable clone, not `EnsureCreated` tables lacking migration history.

No migration/model source changed. The repository-default EF check reports no pending changes. An explicit PostgreSQL-provider check reports pending changes on both this implementation and the unchanged main-based #272 baseline; this is existing provider/snapshot drift, not evidence of a new migration requirement. PostgreSQL schema/application behavior is tested separately on the restored database and combined additive migrations. Do not generate or apply a production migration to hide that baseline discrepancy.

## L. Draft PR and commits

Base/main: `e58d73a12c8bf0cc5f0f2333e442497e2b91b807`. Branch: `codex/historical-projection-reconstruction`. Implementation commits include `80ab347` (framework), `b8afc25` (diagnostic classifier), `566b5a8` (independent historical metadata/audit verification), and `446d406` (history-preserving restore fixture). The final PR, documentation commit and GitHub check status are recorded in the delivery response. No MSI is required because WinForms/QC Station code is unchanged.

## M. Separate production approval package

[The non-executable proposal](projection-reconstruction-production-proposal.json) contains exact original rows/versions, proposed replacement identity/quantity/treatment, supersession plan, source evidence, receipt limitations, fingerprints, exclusions, audit requirements, concurrency preconditions, idempotency strategy, backup requirements and recovery/verification procedure. It contains no granted production authorization and is not an execution request.

Its fingerprints describe backup #186 and sequential local rehearsal states. They **must not** be submitted as current production approvals. Each future pool requires a fresh production preview after any prior repair, independent human approval and separately recorded authorization. Complete the standard verified predeployment backup gate before the first production-affecting action. No automatic reversal is provided: after a committed repair, prefer a reviewed audited forward correction; never reactivate inflated rows or restore an old snapshot over newer legitimate activity.

## N. Remaining blockers and limitations

Three correction pools remain blocked for the exact missing evidence in E. The wider 35/13/five populations remain unresolved, without proposed automatic repairs. Historical exact receipt allocation is still unknown or ambiguous even for repaired shared pools. Source receipt720's unsupported revision needs a different evidence model if exact provenance is later required. All-history negative-movement checking is deliberately conservative and can reject candidates whose earlier empty epoch lacks evidence.

The proof is tied to a verified historical backup and exact referenced PR commits. Current production activity, deployments, backups and monitor state were not rechecked. Existing PostgreSQL-provider snapshot drift and accurately listed optional test limitations are separate review items. Repair audit immutability uses existing application controls. Global maintenance locks and whole-protected-table fingerprinting are safe but intentionally restrictive; operational duration must be measured and bounded in any future approved production window.

## O. Recommended sequence

1. Review this draft and the combined validation; retain strict canonical mode and all readiness gates. Resolve any outstanding test/environment limitations explicitly.
2. Obtain the precise operational evidence for receipts1373/1339/1754. Independently reconcile surviving allocations and point-in-time treatment. Extend the evidence validator only for demonstrably complete new evidence; do not turn correction arithmetic or reasons into custody proof.
3. If separately authorized, prepare a release and verified fresh predeployment recovery point. Re-preview and approve only the three proven pools, one at a time, under a bounded maintenance window. This task grants none of those production actions.
4. Independently verify each committed repair and full readiness, preserving original audits and metadata. Expect the remaining three blockers until their evidence is resolved; do not proceed with #271 as a cleared production release merely because the first three have been reconstructed.
5. Resolve the three correction cases with evidence-backed code/operational proposals and explicit approval, then rerun full readiness and #271's exact partial-custody/compensation rehearsals on a fresh restore. Track the broader positive/depleted/negative populations separately. Merge/deploy only under a new explicit production authorization.

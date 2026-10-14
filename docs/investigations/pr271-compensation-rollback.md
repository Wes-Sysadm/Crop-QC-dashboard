# PR #271 compensation-aware rollback — NO-GO

Owner request: “Resolve Compensation-Aware Rollback Release Blocker”, 2026-10-13
(America/Los_Angeles). This is preparation/rehearsal authority, not release authority.
Reviewed feature source: `19473648ec84f99b7ae8793312b6adf40e125cc7`.
Fetched main/base: `75b8dbe00210d413d35ae41d0b134b312a3ad66e`, unchanged.
PR remains draft on `codex/authoritative-partial-custody`.

## Decision

**NO-GO. Rollback compatibility after committed custody remains unproven.**
The exact forward application failed the required mixed-treatment rehearsal before
acknowledgement. A transaction-local diagnostic reproduced the same failure in
the frozen rollback candidate. Stop at this safeguard. No scenario was excluded,
no conservation/treatment guard was relaxed, and no application fix was bundled
into this rollback-packaging task.

The previously reported 160 affected local cases and 106 mandatory CI contracts
remain valid evidence for their selections, not proof of this additional scenario.
Green CI cannot supersede this failed release-specific gate.

## Rules and scope

Read current AGENTS, specification 1.1.0 (governance source
`c21da183961f6d0304a83a1c2eb10fc6e7f9884d`), ADR register, change procedure,
migration smoke procedure, overnight release standard and change-scoped testing.
Applicable: INV-001–006, REC-001–002, TRT-001–002, MOV-001, ROOM-001, AUD-001,
OPS-001, GOV-001; especially ADR-003–005. No policy amendment.
Report-only synchronization found the expected detached integration worktree and
PR-local proposals, with zero incoming main commits. No other worktree was reset.

The affected boundary is compensation-aware reads/writes, serialized command
compatibility, custody schema, exact runtime and rollback preservation. Manual
origin/baseline behavior, production corrections and the RED reconstruction are
outside scope. **No application code on PR #271 changed in this task.** Added
material is local rehearsal tooling and evidence/documentation.

## Original incompatibility and candidate architecture

Current main lacks `ReceiptCustodyAcknowledgment`/placement/reversal accounting,
the net acknowledgement/held/transit readers, and the transaction capability
`cropqc.receipt_custody_writer=compensations-v1`. After first custody use, the SQL
fence rejects its physical writes; preserving additive tables does not make its
reads correct. Disabling the canonical engine or removing the fence is invalid.

Candidate branch: `codex/pr271-compensation-rollback`.
Candidate commit: **`4ee804d17874cb065f96a414600507fb1ef0944d`**.
Its baseline is `d3cf02362244cd8b4469ed4c42b2c1ec37c0b3e6`, the earlier
compensation-aware #271 integration already containing main/#281. Five files
differ from that baseline:

- Three existing Data command files retain the reviewed `1947364` admission,
  placement-reversal and arrival-endpoint fixes.
- `InventoryCommands.cs` retains the newer optional `AdditionalStock` wire shape
  so serialized committed intents can still be represented exactly.
- The reviewed writer registry records these bounded compatibility backports.

The candidate omits the later after-complete-consumption addition UI/writer.
Fresh `AdditionalStock` intents explicitly block. The journal replay branch
precedes that block; cross-version replay was designed but **not reached/proven**
because the forward prerequisite failed. Existing source/custody readers,
compensation commands, historical corrections and all three migrations remain.
This is a proposed compatible earlier feature profile, not current production
main and not a claim of an independently implemented custody engine. Faults in
shared compensation/resolver code can affect both candidates, as this test found.

No separate overlapping feature PR was opened. The rollback source is an
unapproved packaging candidate only; it must not be deployed based on this record.

## Verified backup and runtime

Used the existing independently verified production backup **#198**, not a new
backup or production operation. Archive and dump hashes were recomputed locally
and match the retained upload/read-back verification record:

- `cropqc-production-predeployment-20261013-171008.zip`, 19,397,550 bytes;
  SHA-256 `4f6b3eb6cf7763328cd081ee72f709acbbfed48500dea5a20aea12de6ba07a29`.
- Database dump `cropqc-prod-db-20261013-171008.sql.gz`, 19,210,932 bytes;
  SHA-256 `d694a8a3fab8b4e01c69e63c9667149d3c8b9ec8eb6f5bb5293d9e26e1c435e4`.
- Prior verification recorded readable archive/dump, component hashes, remote
  read-back, completed retention and released lease. No new remote verification
  or fresh release-backup claim is made here.

A brand-new `pr271_rollback_test` database was restored on the existing isolated
PostgreSQL **18.4** container. Its Docker network is internal. No production
connection was supplied to rehearsal code. Synthetic new rooms, receipt origins
and a receipt-specific treatment are confined to this clone.

Both exact Git archives were built with the repository build stage, replacing
only the runtime base with the locally retained production-matching #281 image
`sha256:ac511f5269095db8450de319f549b341009c8673f7dabd3331a8d0dbba7b2cc4`.
The runtime contains Linux amd64, .NET/ASP.NET Core **9.0.20**, matching the
recorded #281 runtime. It is not a new read of live Render configuration.
The probe ran inside those runtime images using their own Web deps/runtimeconfig
and packaged Data/Shared assemblies, not the harness build's replacement DLLs.
The evidence records hashes of the actually loaded assemblies.

Exact image IDs, source commits, archive identity, checksums and limitations are
in [the candidate manifest](pr271-rollback-candidate-manifest.json). These are
frozen local artifacts, not remotely deployed or certified release artifacts.
The build retains existing ImageSharp package advisories; no dependencies changed.

## Migration result

Backup198 already records `20261009150448_IsolatedInventoryMovementCohorts`.
Generated and applied only these missing bounded ranges, in this order:

1. `20261002031709_BoundedBackupSnapshotProgress` →
   `20261007212154_ReceiptHeldTransferCustody`.
2. Held custody → `20261007235352_ReceiptCustodyCompensations`.
3. Compensations → `20261007235822_ReceiptCustodyGuardrails`.

The original columns and rows of **109 tables** were hashed before/after and
remained identical. CohortKey history remained applied; its migration was not
replayed. This is a successful additive migration preflight on the isolated
restore, not production migration authorization. No Down migration was run.

No rollback operation requires restoring an old backup over newer activity in
the proposed design. Actual continued operation after handover is still unproven.

## Exact failed safeguard

Disposable source: WP room990001, Conventional FUJI, lot990001 / `R271-MIXED`.
The canonical executor recorded receipt A with 20 bins, applied a receiving
treatment to those 20, then recorded receipt B with 20 later untreated bins.
Source availability proved both disjoint treatment slices. The command selected
15 from each, with distinct signatures, for one 30-bin EBS dispatch.
The command shape permits multiple treatment slices; the engine only prohibits
duplicating the same position/signature. This is not an overdraw or invented origin.

At `PersistedSource2`, before the command transaction rolls back:

| Evidence | Observed value |
|---|---|
| Parent bins loaded | 30 |
| Exact source ledger | −15 and −15 |
| Immutable dispatch movements | 15 treated + 15 untreated, distinct source/receipt links |
| Authoritative custody | 30 |
| Projected custody | 30 |
| CustodyVerified / IdentityVerified | true / true |
| HistoricalSnapshotUnavailable | false |
| Actual resolver blocker | `MixedTreatmentAmbiguity` |
| Externally returned result | Blocked: “Dispatch ledger, immutable allocations and custody parent do not conserve quantity.” |

Root cause chain:

1. `InventoryCustodyEvidence.LoadCustodyAsync` groups the two same-identity
   dispatch allocations into one custody position and carries receipt A's
   treatment application plus both exact immutable dispatch movements.
2. `InventoryCohortEvidence.Proven` applies only to room custody. It does not
   prove the transit slices through that route.
3. `InventoryAvailabilityResolver.ValidTreatment` tests the untreated slice
   using incoming room movements or receipt-scoped room ledger. The transit
   view contains outgoing dispatch movements and parent debits instead.
   Therefore `exactReceiptAllocation` is false even though the dispatch itself
   identifies receipt B exactly.
4. The receiving treatment attached to receipt A then invalidates receipt B's
   untreated projection under that fallback. The position is classified
   ambiguous despite equal quantities and valid custody.
5. `InventoryCommandOperations.ApplyAsync` requires every resulting transit
   position to be operable, so its combined postcondition emits the generic
   conservation error. Quantity equality alone cannot waive treatment proof.

The diagnostic observed the failure in **both exact packaged applications**.
Data/Shared hashes, full synthetic inputs, transaction-local evidence and results:
[forward](pr271-rollback-forward-blocker.json),
[rollback candidate](pr271-rollback-candidate-blocker.json).
Rejected attempts left receipts, ledgers, segments, movements, custody parents,
command journal and audits unchanged. No failed dispatch was committed.

This is demonstrated for a receiving-treatment application on one receipt and a
later untreated receipt of the same identity. It is not a claim that every mixed
load fails. The current Web dispatch form selects one slice; the multi-allocation
canonical command and subsequent load editing need a specific compatibility
review. Earlier mixed-lot tests do not exercise this same-identity shape.

## Rehearsal completion status

| Required stage | Status |
|---|---|
| Exact Linux forward and rollback image builds | PASS |
| Fresh isolated backup restore and additive migration | PASS; original 109 tables unchanged |
| Recorded origins, point-in-time treatment, later untreated arrival | PASS before dispatch |
| Mixed-cohort partial dispatch | **FAIL/BLOCKED**, reproduced in both packaged engines |
| Acknowledgement, placement and partial acknowledgement reversal | NOT REACHED |
| Untouched placement reversal and later placement | NOT REACHED |
| Existing historical corrections across application versions | NOT REACHED |
| Concurrent/retried custody after handover | NOT REACHED |
| Application handover after committed custody; safe HTTP reads/writes | NOT REACHED |
| Cross-version intent replay and rejection of omitted addition feature | NOT REACHED |
| Final readiness / independent post-rollback integrity | NOT RUN; no PASS claimed |

The prepared rehearsal driver intentionally fails at the first unmet gate.
Unreached code is a proposed sequence, not validated coverage. No test was
disabled, excluded or converted to success to manufacture compatibility.
No production access, migration, correction, reconstruction, merge or deployment
occurred. Neither TR110045/TR110044/TR110057 nor the 210-bin RED pool was changed
in production. No historical correction was reached even on the clone.

## Required next work and release limits

Review and correct the operation-specific treatment proof for **immutable transit
allocations**, including same-identity mixed receipt treatments. Scope includes
`InventoryCustodyEvidence`, `InventoryAvailabilityResolver.ValidTreatment`, the
dispatch/transfer-edit postcondition and receipt-held consumers of those slices.
Prove original source membership/application evidence; do not set
`RequireKnownTreatment=false`, treat Unknown as untreated, or relax conservation.
Any necessary reader fix must be included in both forward and rollback candidates,
with new immutable artifacts and focused replay/concurrency tests.

Then restart from a fresh isolated restore and complete every currently unreached
handover scenario. A later authorized release still requires exact merged SHA,
current runtime comparison, fresh independently verified production backup,
bounded migration preflight, strict readiness and independent production checks.
This report does not grant release authorization. **Keep #271 draft and NO-GO.**

## Validation of this evidence revision

- Both exact Linux application image builds: PASS. Runtime 9.0.20 executed.
- Final rehearsal and diagnostic driver builds against frozen application DLLs:
  PASS, zero warnings/errors. Later workflow phases remain unexecuted.
- Model consistency: PASS, no pending changes. No new migration or feature code.
- Changed-tool whitespace formatting, `git diff --check`, governance metadata:
  PASS. Existing architecture contracts/mandatory CI remain required.
- Release rehearsal: **FAIL at mixed-cohort dispatch**. Both frozen engines
  reproduced the same treatment blocker, with atomic rejection verified.
- No repeat of the completed broad 160-case local selection. Final-head GitHub
  check results are recorded on the PR; CI success is not rollback readiness.

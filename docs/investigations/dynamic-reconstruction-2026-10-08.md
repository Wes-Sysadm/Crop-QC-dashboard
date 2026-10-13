# PR #275 dynamic reconstruction review

## Assumptions and invariants recorded before implementation

This continues draft PR #275 on `codex/recorded-history-reconstruction`, starting at
`528727ba9fd0203969a546d12335f595898215f0`. Fetched `origin/main` is
`e58d73a12c8bf0cc5f0f2333e442497e2b91b807`; no base update was necessary.
Read AGENTS.md, the change-scoped testing and overnight release standards, the
phase 1 read contract, phase 2 command contract, phase 3 cutover and architecture
assessment. Read the complete proposed business-rule specification from
`origin/codex/shared-project-governance` at
`a299ec5830e5e6c9f2f86ac4ff4ef460f707b196`. That governance change is a reference,
not part of this PR or merged into it.

- Backup #186 is a historical regression fixture. Its quantities and IDs never
  authorize a later production correction. Fresh production quantities remain
  unverified in this follow-up.
- The canonical ledger and custody evidence loader own physical quantities.
  Reconstruction may prove a derived representation, never introduce another
  physical balance calculator or change an authoritative transaction.
- Receipt matching for a transfer is receiving evidence, not a second origin.
  Corrections use committed signed effects. Reasons do not assign physical bins.
- Recorded and effective timestamps remain distinct. Exact identity includes
  year, warehouse, room, grower lot, profile, variety, production and normalized
  inventory status. Unknown treatment never means untreated.
- Preserve independently proven current populations. A whole-pool untreated
  proof does not authorize retiring valid new receipt projections.
- Unsupported custody or treatment history fails closed with system event IDs.
  Partial receiving contracts from unmerged PR #271 must not be invented on main.
- Preview, approval and execution use one consistent snapshot each, exact
  fingerprints and versions, and existing maintenance locks. Changed evidence
  requires a fresh approval.
- Independent verification checks original committed evidence first, then fresh
  current evidence. Later activity cannot excuse an invalid original repair.
- Changes are confined to maintenance proof/execution/verification and tests.
  Shared operational ledger, resolver, treatment guards and receiving behavior
  are not being changed. No model migration or UI change is planned.
- All mutation and rehearsal work is confined to disposable local PostgreSQL.
  Production repair, merge, deployment and backup operations are prohibited.

## Initial findings

The existing implementation already uses fresh canonical quantities; it contains
no hardcoded backup #186 target balance. The defect is population selection:
every positive current row is retired and replaced by one shared untreated row.
The verifier additionally omits treatment state, application links and several
identity/disposition checks. Its global fingerprint reports later activity as
an undifferentiated evidence change, without a durable independently checked
snapshot of the committed replacement and normal resolver result.

Implementation and validation results follow.

## Result and release boundary

Continue **[draft PR #275](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/275)**.
`origin/main` and the base remain `e58d73a12c8bf0cc5f0f2333e442497e2b91b807`.
No main update, new PR, migration, deployment or production correction is included.
The final commit is the PR head identified in the completion handoff; this document
is committed with that work.

**Fresh production inventory is unverified.** This follow-up made no production
database or Render requests. All quantities below belong to an identified local
fixture or the historical October 7 backup. None is a production execution target.

The implementation now distinguishes independently proven current populations
from obsolete representations. New receipts and allocations backed by committed
canonical commands retain their IDs, quantities, provenance and metadata. When
those populations already cover all canonical authority, the repair retires only
the excess rows and creates no replacement inventory. Otherwise it creates only
the independently proven residual shared pool. A conflicting existing shared
population blocks an overlapping second shared row.

Mixed or unknown treatment is **not** reconstructed as untreated. This maintenance
version deliberately blocks mixed reconstruction, identifies applicable system
events and leaves all populations unchanged. Normal treatment and receiving
workflows remain unchanged. Exact receipt ancestry is never inferred from a
correction reason, quantity gap or legacy transfer receipt ID.

## Authority, events and custody

`InventoryEvidenceLoader` obtains current physical quantity from
`RoomInventoryLedgerQueryService`, including its baseline/effective-date rules.
The maintenance proof does not sum current `Receipt.BinCount` to establish stock.
It uses the canonical quantity throughout preview, execution and verification.

`ReconstructionCommandEvidence` checks committed intent hashes, actor/timestamps,
command audit membership, exact ledger and movement IDs, invariant versions,
parent references and allocation quantities. It calls the existing
`InventoryPhysicalFlow.Matches` to compare signed physical effects by location
and identity. PostgreSQL timestamp comparisons use its microsecond precision.
Per-projection movement arithmetic establishes representation coverage; it is not
an alternative physical room balance. Missing, contradictory or unsupported
canonical command effects block the proposal with their operation keys.

Previously normalized populations additionally require their committed
normalization audit, historical supersession rows and subsequent validated
events. The audited history plus canonical effects must cover every contributing
ledger event. Fresh ordinary receipts retain exact recorded receipt allocations.
Legacy transfer snapshots that merely carry a receipt ID remain eligible for a
shared pool only when the independent complete-pool proof succeeds.

Truck Receipt matching for an inter-company transfer is evidence of receiving.
The dispatch/receive ledger legs determine physical custody. The regression
explicitly checks that the matched transfer receipt has **zero `ReceiptAdd`
credits**, while a later ordinary receipt contributes its one legitimate origin.
Internal transfers in both directions, compensating room returns, consumption,
losses and audited positive/negative receipt corrections are checked against
their committed effects. Voids preserve original receiving and debit history.
The legacy revision validator also recognizes canonical correction audit shapes;
it does not reinterpret audit reasons as physical allocation instructions.

Fresh previews contain the complete canonical room evidence, recorded and
effective timestamps, current/historical segments, receipt revisions, application
IDs and separate canonical `InTransit`, `OutsideWarehouse` and `Processor`
evidence. The exact target includes crop year, warehouse, room, grower lot,
profile, grower/lot, variety, production type, organic flag and normalized status.
`SnapshotCapturedAt` records the observation time. Data fingerprints and versions,
not equality of two observation-clock readings, determine staleness.

Main has no PR #271 partial/held receipt contract. Transfers with partial/unknown
status, acknowledged but unplaced quantities, or a received count inconsistent
with dispatch are blocked with the parent ID, status, loaded count and acknowledged
count. These bins are never added to room authority by the repair. **A valid later
partial settlement is not implemented or certified on this branch.** The full
matched-receipt case is tested separately. Integrating and validating PR #271's
actual allocation/settlement model remains necessary before repairing that shape.

## Independent verification

Execution stores a sealed commit evidence object in the repair audit and its
SHA-256 digest in the command result. It contains the persisted canonical
post-state, full current-row snapshot, conserved protected fingerprint and exact
membership/fingerprints of previously existing protected rows. Membership is
explicit: neither backdated timestamps nor a database sequence below imported
IDs can hide changes to an existing row. This is an integrity cross-check across
the audit and command journal, not a cryptographic signature against an attacker
who controls the entire database.

Verification starts with original approval, execution intent and committed-state
validity. It checks replacement existence, exact identity/location, disposition,
quantity, shared/existing receipt contract, state, signature, application links,
the precise set of current rows, unchanged preserved populations and full retired
row metadata/quantities. It invokes the normal canonical resolver and checks
separate current custody. It also checks original ledger and movement rows for
changes, even if new activity occurred afterward.

The statuses distinguish:

- `Verified`: the sealed repair and current contributing evidence still match.
- `VerifiedWithLaterActivity`: the original repair verifies, and later canonical
  command allocations account for the new ledger/movement effects and every
  current projection quantity.
- `CommittedEvidenceVerifiedCurrentReviewRequired`: the original repair verifies
  but a later operable state requires further allocation review. This includes a
  later recorded room treatment and changes to previously existing mutable
  protected records. It is **not certification of current reconstruction** and is
  not automatically labeled corruption.
- A verification exception rejects missing/tampered original evidence, invalid
  identity/treatment, inconsistent current authority, or changed immutable history.

A `u` signature with `Unknown` state fails both live-state and committed-state
validation. A regression deliberately changes the sealed original state and
recomputes its digest while keeping the live replacement valid and adding a real
later receipt; the original invalid state still fails the substantive contract.
Verification opens a read-only repeatable-read transaction and writes nothing.

Old repair records without this sealed evidence are not silently upgraded to
verified. Old approvals cannot execute against a different fresh snapshot.
Approval/execute retain Serializable isolation, exact fingerprints/versions,
NOWAIT maintenance locks, rollback tests, same-key replay and uncertain-commit
recovery. Preserved current rows remain protected during execution.

## Historical isolated-restore rehearsal

Verified existing package: backup **#186**, 17,317,796 bytes,
SHA-256 `0170afc86576e3c57483ebdf1cc3b431555b545bf1cd9e454e7e020bdf22ccc0`.
Snapshot: **2026-10-07T23:53:37.406346Z**, deployed code `e58d73a…`.
Archive CRC, four component hashes/sizes, SQL dump and frozen photo manifest were
verified. No backup operation or remote download was triggered.

Final rehearsal database: **`pr271_release_dynamic_verified186`**, local
PostgreSQL, restored **2026-10-08T17:59:53.003500Z**. The harness requires all six
cases to commit, independently verify and replay; a blocked case fails the run.

| Historical pool | Canonical bins | Before projection | After projection |
|---|---:|---:|---:|
| Evans 5 / 3152 | 61 | 162 | 61 |
| Evans 5 / 9682 | 252 | 536 | 252 |
| WP-7 / 1372 | 1,122 | 1,568 | 1,122 |
| DH-15 / 2350 | 202 | 598 | 202 |
| WP-5 / 1084 | 10 | 130 | 10 |
| WP-8 / 2350 | 170 | 362 | 170 |

All six passed commit/verify/replay. Authoritative total remained **1,817**;
projection excess removed was **1,539**. Sixteen old rows were superseded and six
shared rows created. No authoritative ledger, movement, receipt, run, correction,
treatment application or unrelated projection changed. Separate before/after
fingerprints cover 26 protected table/record groups; only six command records and
twelve approval/execution audits were added alongside the planned projections.
Exact surviving receipt ancestry remains unresolved in the shared pools.

Full readiness on this environment changed from six treatment blockers to
**PASS: 979 expected schema objects, 438 current identities, zero treatment
blockers**, with inventory and topology checks passing. This describes the local
backup snapshot, not current production readiness. The original historical report
and fixtures remain unchanged; the new evidence accompanies them.

## Regression coverage and limitations

New disposable PostgreSQL regressions derive expected balances from opening
canonical authority plus the commands actually executed. They add a deliberate
projection-only defect after legitimate transactions and assert that the repair
preserves valid current rows byte-for-byte. No production-shaped test connects
to production. Original three complete recorded-history fixtures and all six
restore cases remain regression evidence.

| Requested cases | Evidence |
|---|---|
| 1–3: new, multiple and different-lot receiving | Dynamic fresh-authority cases; unaffected other-lot rows protected |
| 4–6: corrections, void and duplicate ticket | Dynamic signed correction/void cases; duplicate command is rejected without another origin |
| 7–9: partial acknowledgement, held bins, settlement | Specific parent/status/count fail-closed tests; valid PR #271 partial settlement remains unavailable on main |
| 10–12: transfers in/out/both ways | Dynamic fresh-authority and compensation cases |
| 13–14: matched transfer receipt and transit | No duplicate origin; room quantity plus separate seven-bin transit custody |
| 15–16: reversals and bad parents | Compensating room reversal; missing movement parent rejected; established custody/reversal tests |
| 17–20: packing, empty epoch, loss/depletion, later correction | Dynamic consumption/new-epoch/loss cases, correction after move and packing; established depletion workflow |
| 21–22: stale approval/concurrent movement | Real receipt, transfer and packing between approval/execution; existing lock/rollback/race tests |
| 23–27: point-in-time, untreated arrival, treated transfer, mixed/unknown | Later arrival remains untreated; transfer retains confirmed application links; mixed repair blocks without writes |
| 28: uncertain ancestry | Shared historical pools retain null receipt attribution; reasons never allocate fruit |
| 29–34: verifier tampering | 22 live tampering cases after a genuine later receipt, plus sealed original-state tampering |
| 35–36: verification and replay | Historical and dynamic commit/verify/replay; byte-identical replay snapshots |

Additional limitations: baseline imports, identity rewrites or older operational
parents not covered by the maintenance proof remain fail-closed; diagnostic
blockers identify their system records. Mixed reconstruction and verification
after complex supersession are deliberately not claimed as supported. Protected
row manifests and existing global maintenance locks make this an explicit
maintenance operation, not a normal receiving-path operation. Operational
availability/resolver behavior was not changed.

No UI or WinForms code changed; browser/installer testing and an MSI are not
applicable. No full test suite was run: the changes are confined to maintenance
proof and verification, with focused operational dependency coverage.

## Final validation

- **205 focused tests passed; zero failed or skipped** in the final combined run.
  This includes 53 new PostgreSQL dynamic/verifier cases, original recorded-history
  regressions, approval/staleness/rollback/concurrency/replay, canonical availability,
  receiving/correction, transfer/custody, loss/depletion, treatment and architecture.
- **One additional restored-data lifecycle test passed**, seed 255, with **59
  committed-state invariant checks**. It cloned the verified, reconstructed backup
  through local template `cropqc_test_dynamic_template186`; synthetic rooms and
  receiving were isolated from historical inventory. Each committed step checked
  canonical room/external custody, conservation, treatment eligibility, unique
  current projections, append-only ledger/movements and unchanged unrelated data.
  The disposable lifecycle clone was removed by the established fixture afterward.
- Solution restore and build passed. Final solution build: **zero errors**, 69
  warnings, including nullable/compiler and ImageSharp dependency advisories.
  No application dependency was changed.
- A final diagnostic-only change adds custody parent IDs and normal-resolver
  blocker details to rejection messages. After that change, **14 targeted custody
  and architecture tests passed**, with zero failures or skips. The 205-test run
  and six-pool rehearsal preceded this message-only change; the reconstruction
  mutation and verification paths did not change.
- Formatting verification and `git diff --check` passed. Reviewed physical-writer
  source hashes were refreshed for the changed maintenance files; architecture
  guards passed. No ordinary writer, treatment guard or shared resolver changed.
- Default-provider EF model check: **no pending changes**. Explicit PostgreSQL
  provider check still reports the previously documented baseline pending-model
  warning. Entity classes, `CropQcDbContext` and migrations are unchanged from
  `origin/main`; no migration was generated to conceal the provider baseline issue.
- GitHub returned **no checks** for this draft PR at validation time. Local checks
  are reported above; no GitHub Actions success is claimed.

Machine-readable [rehearsal, 59-step lifecycle, test results and protected hashes](dynamic-reconstruction-evidence-2026-10-08.json)
accompany this report. The [historical read-only SQL](recorded-history-reconstruction-verification.sql)
and [complete original investigation](recorded-history-reconstruction-2026-10-08.md)
remain available as snapshot evidence.

## Fresh read-only evidence before any future proposal

Use the existing CLI `--reconstruct-historical-projection --mode preview
--request-file <exact-target.json>` against the authorized environment, with a
read-only database session. It returns the current canonical evidence, separate
custody, timestamps, fingerprints, precise preserved/retired IDs and replacement
quantity, or specific blockers. `--mode verify` remains read-only as well.

The final evidence artifact records the historical rehearsal, protected hashes,
readiness and validation results. Neither that artifact nor an old preview is
production approval. A later production-affecting action still requires separate
authorization, a fresh snapshot/approval and the mandatory newly verified
predeployment recovery point. No production repair proposal with current
quantities is offered without that fresh evidence.

## Safety confirmation

No production inventory or historical records were modified. No production
repair, merge, deployment, configuration change or backup operation occurred.
Only local source/docs/tests and disposable local databases were changed.

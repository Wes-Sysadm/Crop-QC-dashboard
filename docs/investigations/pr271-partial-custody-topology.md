# PR #271 — strict partial-custody topology readiness

Owner scope: “Correct Strict Partial-Custody Topology Readiness”, 2026-10-13
America/Los_Angeles. Same draft PR and branch; implementation and isolated rehearsal
only. No merge, deployment, production access, corrections or reconstruction.

## Repository, authority and scope

Started at `e51dac9cd8ce181fffe20e38f5e231cc538b315a` on the detached integration
checkout of `codex/authoritative-partial-custody`. Fetched main/base remained
`75b8dbe00210d413d35ae41d0b134b312a3ad66e`; zero incoming commits. Report-only
knowledge synchronization reported the expected detached checkout and PR-local
proposals, not incoming policy changes. Specification **1.1.0**, governance source
`c21da183961f6d0304a83a1c2eb10fc6e7f9884d`, remains authoritative.

Read AGENTS, canonical rules, ADR register, change procedure, Phase 1/2 inventory
contracts, migration instructions, change-scoped testing, release standard and
the prior treatment/rollback reports. Applicable rules: INV-001–006, REC-002,
MOV-001, TRT-001–002, AUD-001, OPS-001, GOV-001; ADR-001–006. This implements the
existing partial-custody policy. No business rule or production safeguard changes.

## Confirmed cause and reproduced evidence

The old `Program.cs` release predicate counted any `InTransit` transfer with
`DestinationWarehouseId`, `DestinationRoomId` **or** `BinsReceived` populated as
invalid. It assumed all receiving was a single completion event. The canonical
writer intentionally leaves partially acknowledged/placed loads `InTransit` and
retains legitimate destination metadata, including after placement compensation.

Re-ran the committed [bounded evidence query](pr271-transit-readiness-blocker.sql)
on the existing isolated restore. Transfer **43** still has loaded 30, net
acknowledged 20, net placed 14, held 6, transit 10, destination warehouse 1 / room
990002, receipt 2656, concurrency version 14. The retained
[previous strict failure](pr271-transit-evidence.json) has `invalidInterCrew=1`,
with schema, deduction and treatment checks passing.

Evidence is not merely a balanced total:

- Dispatch movements 1080/1081 each retain 15 original bins, source segments
  972/973 and origin receipts 2654/2655. Only the first cohort carries application
  30 (`u|a:30`, Confirmed); the later cohort is `u`, Untreated.
- Acknowledgements 1/2/3 reference those original movements. Net acknowledged
  quantities are 12/7/1; net placed quantities are 7/6/1.
- Placement reversals 1084/1090 refer to placement movement 1082, and reversal
  1098 refers to 1097. Original movements, source membership and receipt links
  remain present. They compensate room placement, not a physical return to origin.
- The new read-only gate independently verifies origin chains, exact parent
  physical flow, acknowledgement/placement/compensation bounds, chronology,
  room/company relationships and point-in-time treatment before reporting PASS.

Diagnostic combined readiness against this unchanged database passed all four
gates with `invalidInterCrew=0` and the exact **30/20/14/6/10** result. No record was
cleared, excluded, reclassified as completed or rewritten to obtain that result.

## Implementation and compatibility boundary

`src/CropQc.Web/Program.cs` replaces its one inter-crew count with
`InterCrewTopologyReadiness.VerifyAsync`; the same result controls `topologyReady`
and reports actionable transfer IDs/reasons. Other warehouse, outside-warehouse,
schema, deduction and treatment checks are unchanged. No parallel all-or-nothing
predicate runs against partial custody.

`src/CropQc.Data/Inventory/InterCrewTopologyReadiness.cs` is a bounded,
AsNoTracking, repeatable-read snapshot; PostgreSQL explicitly sets READ ONLY.
It opens no physical-writer capability and never saves changes. It reuses:

- `ReceiptCustodyProof.Valid` and the entities' existing net/placed/held properties;
- `InventoryPhysicalFlow.Matches` for exact historical identity/location deltas;
- `InventoryOriginGuard.ValidateAsync` for the recorded funding/origin chain;
- `InventoryEvidenceLoader.ReadCustodyTreatment` for immutable departure proof;
- `CanonicalIdentityMap` for established audited identity overlays;
- the existing custody loader/resolver for unacknowledged canonical loads,
  including supported full-load reopening and partial returns.

Additional release assertions bind every acknowledgement to its exact dispatch
and transfer receipt, every placement to its acknowledgement and ledger/movement,
and every compensation to its eligible original event. They reject duplicate
operation/allocation claims, over-acknowledgement, over-placement, over-reversal,
wrong parents/receipts/rooms, missing origin and contradictory treatment. Event
chronology is checked at committed acknowledgement, placement and reversal times;
business movement/application times remain separate. A later compensation cannot
hide an earlier excess. Reversal eligibility checks earlier movement/treatment
dependencies, without treating a later valid move as an earlier violation.

Recorded partial returns **before any acknowledgement** retain exact original
dispatch, source-room restoration, receipt/identity and treatment proof. Their
quantities reduce the remaining dispatch allocation and parent load. They cannot
overlap acknowledged bins. The return regression proves 30 original dispatched,
5 returned, 25 remaining load = 20 held + 5 transit. Unknown transition kinds or
unsupported return/compensation combinations still block with a specific reason.

Completion requires net acknowledged = net placed = remaining load, matching
observed receipt total and the writer's Received/completion metadata. Partial
custody must remain InTransit with no completion timestamp/key. Receipt-held
quantity is net acknowledged minus net placed; transit is remaining load minus
net acknowledged. Retained destination metadata must point to a recorded placement
in the receipt warehouse; a current room seal is not retroactive history.

Historical transfers with no compensation history retain their previous topology
contract. This is not retrospective certification or reconstruction of every
legacy receipt/dispatch. Any acknowledgement history always selects the stronger
contract; a durable canonical dispatch also selects authoritative transit proof
before its first acknowledgement. Historical full-load completion/return remains
under its existing lifecycle. No legacy inventory/treatment gate is weakened.

No operational writer, manual-origin/baseline admission, destination-admission,
quantity correction, immutable treatment proof or migration implementation changed.
The new reader and changed Program dispatch were reviewed in the writer registry;
its conservative scanner sees local list additions and the READ ONLY statement.

## Targeted verification

- **32 focused cases passed**, including all 19 topology cases and 13 immutable
  custody-treatment cases, with no skips. Mixed 15-treated + 15-later-untreated
  workflow now invokes readiness through dispatch, acknowledgement, held custody,
  partial placement, acknowledgement reversal/re-acknowledgement, placement
  reversal/re-placement, subsequent movement and full 30-bin completion.
- Exact **30/20/14/6/10** fixture passes. Sixteen independent corruption shapes
  fail while complete database fingerprints remain unchanged by readiness.
  The corruption fixtures use explicitly disposable EnsureCreated databases;
  production schema triggers are never disabled.
- A real independent PostgreSQL writer commits acknowledgement while the reader
  is paused after its first query. The old snapshot reports 0 acknowledged;
  the next snapshot reports 10, both valid. Stale commands still reject.
- **138 mandatory contracts passed, zero failed/skipped**, with all 53 required
  methods and both structural suites verified. This preserves the previous 119
  contracts and adds 19 directly relevant cases. The earlier 160-case evidence
  remains historical evidence, not a claim of a new broad run.
- Architecture regression requires the shared readiness decision and existing
  authority/treatment helpers; prohibits current segment bins and SaveChanges as
  its authority. #281 admission contracts remain passing.
- Governance metadata and **29 tooling tests** passed. Solution restore/build,
  pending-model check, changed-source whitespace checks and `git diff --check`
  passed. No model change, migration or MSI is introduced.

The initial corruption run was stopped by the application raw-SQL writer guard
before corruption could be seeded. The fixture was corrected to use its explicit
disposable raw connection. The initial architecture run required a reviewed entry
for the new reader; that classification was added after source review. Neither
failure was hidden or resolved by weakening an application guard or test assertion.

## Frozen applications and rehearsal

Forward source: `1c263278f13eb31884a2872c2151257dee099789`.
Compatible rollback source: `e651bf3e04b70aae37f2feb6d8bce4b76861fa63`.
Both include identical new readiness semantics. Older frozen packages are retained
as historical evidence and are not represented as containing this fix.

Both images use Linux amd64, .NET/ASP.NET **9.0.20**, with retained #281 runtime
base `sha256:ac511f5269095db8450de319f549b341009c8673f7dabd3331a8d0dbba7b2cc4`.
This is execution in the previously recorded production-matching runtime, not a
fresh live observation. Source archives, image IDs, loaded DLL hashes, archive
size/checksum and compatibility limits are in the accompanying manifest.

Frozen combined archive: `pr271-topology-forward-1c26327-rollback-e651bf3.tar`,
**182,590,464 bytes**, SHA-256
`837926f1754adb2833e1b42db3a459acc11ba363fc4c077b4ec8089b0f9f3eeb`.
Archive and source ZIPs are retained read-only in task-local rehearsal storage;
not deployed or uploaded. No overlapping feature PR was created.

Fresh disposable `pr271_topology_rollback_test` was restored from verified backup
#198. Recomputed package SHA-256
`4f6b3eb6cf7763328cd081ee72f709acbbfed48500dea5a20aea12de6ba07a29` and dump SHA-256
`d694a8a3fab8b4e01c69e63c9667149d3c8b9ec8eb6f5bb5293d9e26e1c435e4` match the prior
independent read-back verification and container copy. Docker networking is internal;
no production connection/credentials were provided. The earlier restore is intact.

Applied only the three missing bounded migrations, in order:
`20261007212154_ReceiptHeldTransferCustody`,
`20261007235352_ReceiptCustodyCompensations`,
`20261007235822_ReceiptCustodyGuardrails`.
Already-applied `20261009150448_IsolatedInventoryMovementCohorts` was retained.
Original rows/columns of **109 tables** matched across migration. No Down or
backup restoration over subsequent business transactions was used.

## Rehearsal closeout and recommendation

**GO for final human review.** The strict partial-custody topology blocker is
resolved in both frozen applications. PR remains draft for owner review; this
task supplies no merge or production-release authorization.

| Executed safeguard | Result |
|---|---|
| Fresh backup #198 restore / bounded additive migrations | PASS; 109 original tables' rows/columns preserved; CohortKey retained. |
| Frozen forward operations | PASS; 16 recorded outcomes, including mixed dispatch, partial acknowledgement/placement, both compensation types, replacement, existing historical correction and separate additional-origin workflow. |
| Strict forward readiness after forward phase | PASS; all four gates, transfer 43 = 30 dispatched / 20 acknowledged / 13 placed / 7 held / 10 transit. |
| Exact forward-to-rollback handover | PASS; complete database row fingerprints match before rollback writes; no database restore during handover. |
| Rollback operations | PASS; 14 recorded outcomes, including cross-version replay, independent-session race, compensations, replacement, mixed room moves and existing correction. |
| Writer and unsafe-operation safeguards | PASS; unfenced physical SQL, overdraw, sealed/wrong-company placement and new unsupported AdditionalStock intent rejected without committed effects. |
| Rollback strict readiness | PASS; all four gates, 30 / 20 / 14 / 6 / 10. |
| Authenticated rollback HTTP | PASS; health/db health and receipt/transfer custody GETs 200; exact placement and reversal POSTs 302 with verified effects; repeated placement idempotent; missing antiforgery 400 with unchanged history. |
| Independent frozen forward reader after rollback/HTTP | PASS; custody, original treatment and protected history verified. |
| Final strict readiness in **both** frozen applications | PASS; schema, inventory, treatment and topology; `invalidInterCrew=0`, 442 current treatment identities, zero blocking issues. |

The rehearsal driver runs beside each application's own DLLs/deps/runtimeconfig;
it does not substitute newly compiled Data or Shared assemblies. Loaded binary
hashes are recorded per phase in [executed evidence](pr271-topology-evidence.json)
and [the frozen manifest](pr271-topology-manifest.json). The HTTP session is local
test instrumentation against an existing restored identity, not production login
or human release approval. The temporary HTTP host was stopped afterward.

Final authoritative mixed pool:

| Membership | Source | Placed in destination rooms | Receipt-held | Transit | Original quantity |
|---|---:|---:|---:|---:|---:|
| Application 30, original receipt 2654 | 5 | 7 | 5 | 3 | 20 |
| Later untreated receipt 2655 | 5 | 7 | 1 | 7 | 20 |
| Total | 10 | 14 | 6 | 10 | 40 |

The dispatched subset remains exactly **15 treated + 15 later untreated = 30**.
The separate regression starts with precisely 30 origins and fully places all
15 + 15. No later arrival inherits application 30; its original source remains
20 treated bins. No receipt ancestry is invented. Final room ledger bins are
10 in 990001, 11 in 990002 and 3 in 990003; these are independently reconciled
with linked held/transit evidence, not used as a substitute for it. See
[read-only SQL](pr271-topology-reconciliation.sql) and
[recorded results](pr271-topology-reconciliation.txt).

All **16 protected historical groups** and **13 forward immutable groups** pass.
The explicitly scoped restored historical correction exercises receipt 2475 and
its current projection metadata; those two mutable fixture rows are excluded from
unchanged-row assertions, while original ledger, movement, application, source and
audit rows remain protected. New correction/audit events are retained. The original
receipt quantity returns to 35 through a second audited correction, not erasure.
These are disposable rehearsal actions, not TR110044 production corrections.

No further implementation defect or local rehearsal blocker remains in this
scope. The rollback candidate deliberately omits the forward AdditionalStock UI/
new-intent capability; it preserves committed results, readable/movable inventory
and exact cross-version replay. Use the new frozen compensation-aware package,
not current main or older packages with obsolete strict readiness.

Final GitHub Actions are linked from [PR #271 checks](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/271/checks).
The application source is frozen at the commit above; later closeout commits
contain documentation/evidence only. Final-head CI status is reported in the PR
and task closeout, separately from the executed local 138-case result.

A future authorized release still requires final human review, exact merged SHA,
fresh independently verified production backup, current runtime/configuration
comparison, bounded schema preflight, strict production readiness, affected health/
route verification and retained rollback protection. Backup #198 is rehearsal
evidence, not a fresh release backup. No code was merged/deployed, no production
data/schema/backups were accessed or changed, and no historical reconstruction
or manual-origin/baseline workflow change was performed.

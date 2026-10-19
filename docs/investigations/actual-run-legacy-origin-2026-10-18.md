# Actual Run: replaying complete legacy receipt and custody origins

**Three connected evidence-interpretation defects are addressed. The complete
synthetic history records the requested 44+74 run atomically. Read-only validators
also accept the retrieved historical formats, and the 95-event source dependency
diagnostic completes. No production save or release readiness is claimed.**

## Governance and scope

Request: new October 10, 2026 WP Packing / WP-7 / Honeybear Actual Run, crop 2026,
BART conventional pears; Top Pear lot 1372: 44 bins; Whitestone lot 2350: 74 bins.
This is not cancellation or editing of an existing run. The requested CA label
has no independent field in ActualRunForm or the canonical Dump intent; no field
or metadata behavior is invented here.

Base/main: `2b5dcfc1a49968490da13650097beac04126613d` (merged #284).
Branch: `codex/actual-run-historical-origin`. Specification/register **1.2.0**;
starting governance commit `31d593f7b239a12b1d76c5a9afbcca09ef374e9d`.
Rules: INV-001–006, TRT-001/002, REC-001/002, MOV-001, ROOM-001, AUD-001,
OPS-001, GOV-001. ADR-001–006 and compatible ADR-008 constraints apply; ADR-007
does not grant release authority here. No rule changed and no unresolved policy
conflict remains: original recorded arrivals, audited identity equivalence and
strict chronological conservation establish the proof; later credits never fund
earlier withdrawals. No owner approval is invented.

Current main's AGENTS, governing documents and scoped-testing standard were read;
no nested AGENTS exists. The parent folder is an unborn Git wrapper. The real
remote was verified as Wes-Sysadm/Crop-QC-dashboard. A clean native Git worktree
was created after the managed tool could not resolve main from that wrapper.
Existing checkouts/untracked evidence were preserved. Report-only knowledge sync
succeeded. The owner authorized investigation, local development, disposable
tests, commit/push and one new PR, but not merge/deploy or production changes.

## Authoritative evidence

The requested #284 origin-gap JSON, production-shaped reverification and
all-position report were inspected. They retained this independent origin block;
treatment-selectable totals were not final consumption authorization.

After the owner confirmed **Wes's workspace**, fresh Render read-only queries
started October 18, 2026 at **20:15:23 UTC**, against crop-qc-db / crop_qc_db,
PostgreSQL 18. They covered receipt/correction/audit, both source ledgers,
segments/application links, receipt/room/inter-crew histories and October 10 WP
Actual Runs/journals. Later reads examined receipt 655 and the room-61 dependency.
Each query has its own read-only transaction; these are not one atomic application
snapshot. No credentials/raw exports are committed; no production data was used
as mutation-test fixtures or restored locally. A separate read-only diagnostic
invoked the compiled pure validators on retrieved JSON without a database context
or writes. The modified application was not connected to production.

### Receipt 720: authorized reclassification

| Evidence | Recorded facts |
|---|---|
| Receipt 720 / TR508546 | Ordinary Truck receipt, 56 bins, crop 2026, GrowerLot 448 / lot 1372, warehouse 3 / room 58; original profile 7, current 17. |
| Ledger 1185 | ReceiptAdd +56 profile 7; actor 1; recorded August 20 03:35:27.306543 UTC, effective receiving time 03:31 UTC. |
| Parent 45678062-5b38-470b-ab7e-58f1db8e0415 | Complete InventoryReclassification; receipt 56 -> 56, inventory 56 -> 56, delta 0, expected ledger count 2. |
| Intent | Operation ddae1e84877e48b898826cec685e276c, administrator 1, reason WRONG FRUIT - JACOB; August 20 03:36:21.327219 UTC. |
| Snapshots | Profile 7 ORGA/Organic -> 17 BART/Conventional, version 0 -> 1; receipt/crop/grower/lot/location/quantity agree. |
| Ledger 1186 | -56 profile 7, old/new 56/0, same parent/actor/reason/time, operation suffix :1. |
| Ledger 1187 | +56 profile 17, old/new 0/56, same parent/actor/reason/time, operation suffix :2. |
| Audit 58166 | ReceiptInventoryOverride / InventoryReclassification, actor 1, same timestamp, CropQc.Web; matching snapshots, reason, quantities and both effects. |

No modern InventoryIdentityCorrection exists for receipt 720. The older Admin
implementation at 1030c5f required Receipts Admin, operation key, reason and
confirmation, then saved parent/snapshots/paired ledger/version/completion/audit
together. This recorded format proves reclassification, not creation of 56 bins;
current permissions are not used to invent the actor's past role.

The downstream path includes RoomTransfer 211 (room 58 -> 66, movement 89),
InterCrewTransfer 34 (movements 701/704) and WP-7 ledger 3624, all retaining
untreated signature u. Historical receipt-720 projections label 184 bins, exceeding
its 56-bin origin; that does not prove exact ownership. Segment 670 remains
Historical. No projection is revived and no receipt allocation is invented.

### Receipt 655: initial receiving increase

The same Top Pear chain reaches **655 / TR508510**, profile 17, room 63.
Ledger **1073** records +56 on August 19 at 03:53:57.992433 UTC. Ledger **1074**,
ReceiptEdit, records +8 (56 -> 64) at 03:54:46.805359 UTC. Audit **53805**,
Receipt/655, Update, actor 1, Web, at 03:54:46.806863 UTC, preserves before/after
snapshots with only quantity changed. Identity/location/receiving time/metadata
agree; receipt version is 1. This format has no override parent.

DashboardDataService.UpdateReceiptAsync at `5f033e2` permitted an initial increase
and saved its ledger and full before/after audit together. Commit
`4508c6d49ad912528bee44299e0e0730db8853fc` required overrides for all quantity
changes on August 19 at 06:09:58 UTC, after the recorded 03:54 UTC edit. Interpreting
this historical format does not authorize new metadata quantity edits. Otherwise
the guard compares original 56 against revised receipt 64 and rejects 1073; it
also cannot interpret the separate +8 ReceiptEdit.

### Current sources, treatment and prior attempts

Fresh WP-7 ledger: **501 Top Pear bins** (1,244 received via inter-crew transfers
16–22/34 minus 743 packing deductions), and **128 Whitestone bins** (128 in, none
deducted). Canonical identities are crop 2026, profile 17, GrowerLots 448/495,
lots 1372/2350, warehouse 4 / room 4. WP-7 is active/unsealed; warehouse/profile
and Honey Bear sales desk 2 are active. Segments 839/671 contain 501/128 with u /
Untreated. Relevant custody movements preserve u; no selected segment application
links or applications on these paths were found. A room-65 application in the
wider grower history is outside both selected custody paths.

Whitestone originates in receipts 632 and 690, 64 each. RoomTransfer 132 moved
632 to room 61; RoomTransfer 197 moved 128 to room 66; InterCrewTransfer 35 moved
128 to WP-7. Movements 73/706/707 carry u. Their receipt-690 labels do not make
that 64-bin receipt a 128-bin origin. This supports shared quantity/treatment,
not exact surviving receipt ownership. **The recorded quantity and treatment cover
44+74. The source proof below supports the historical custody interpretation;
the eventual save still needs fresh selections and every normal command guard.**

No matching October 10 WP/Honey Bear/44+74 run, entries, revision or committed
intent was found. The date query used America/Los_Angeles boundaries, October 10
07:00 UTC through October 11 07:00 UTC. Unrelated runs were excluded. The failed
operation key is unavailable, so this does not certify every past attempt. SQL
INSERT logs or sequence gaps alone do not establish a business commit.

## Code path, fix and protected behavior

BinsRunController.CreateActualRun -> BinsRunService.CreateActualRunAsync ->
SaveActualRunAsync -> SaveCanonicalActualRunAsync -> canonical Dump ->
InventoryCommandExecutor.ExecuteAsync -> InventoryOriginGuard.ValidateAsync.
The Serializable transaction checks complete intent replay, actor/room, quantity,
treatment, fingerprints and versions. Origin validation precedes normalization
and run/revision/ledger/movement/journal/audit writes.

For 1187, the old guard loads 1186 but removes it through the raw-identity sibling
filter: profile 7 differs from 17, with no modern correction map. The result is
an apparent unfunded +56 despite complete old-format proof. Top Pear triggers
this; the failure is not evidence of a Whitestone defect. A synthetic equivalent
reproduced the exact custody-parent failure on unchanged main before the fix.

Files: InventoryOriginGuard.cs, new LegacyReceiptReclassificationOrigin.cs and
LegacyInitialReceiptIncrease.cs, LegacyGrowerReconciliationOrigin.cs, the two
corresponding integration-test files, governance traceability, reviewed-reader
hash and this report.

- Reclassification proof requires a completed invariant-v1 parent, conserved full
  receipt/current quantities, exactly two ordered matching legs, operation
  suffixes/actor/reason/timestamps, no conflicting parent, consistent receipt and
  affected snapshots, exactly one matching audit and one original arrival before
  intervening source withdrawal. It applies to cross-identity legacy parents;
  existing location-only corrections retain their current custody path.
- Initial-increase proof requires one ordinary arrival, one positive v0 ReceiptEdit,
  receipt version 1, no override revisions, exact old/new quantities, unchanged
  raw identity/location/metadata, recorded chronology, one matching Update audit
  and actor, and no intervening source withdrawal. The same evidence proves both
  arrival and increase. Unsupported later or multiple edits remain blocked.
- No interpreter writes, grants treatment, creates stock/maps, assigns ancestry,
  backdates evidence or changes permissions. Incomplete/contradictory/duplicate
  evidence, invalid quantity/treatment, stale selection and custody still block.

The third interpreter, described below, reads a recorded reconciliation of a
missing GrowerLot key. The loader follows the complete original nullable-key
position and custody dependencies. Original revision snapshots preserve a null
GrowerLot value as null instead of throwing while trying to parse it as an integer;
this matters for receipt 499's retained arrival and subsequent void.

No schema/migration/UI/API/writer/QC Station/installer change. The reviewed-reader
hash follows substantive review: no SaveChanges, SQL mutation, capability or
writer exemption. Mandatory contracts were added without removing existing tests
or changing specification policy. Retry behavior is unchanged: identical key and
complete intent replays once; conflicting payload fails; stale new-key submission
fails. A genuinely fresh new intent is a separate operation, not date/bin deduplication.

## Completing the nullable-key custody proof

**RoomTransfer 218 / debit 1413** withdrew **280** from warehouse 3/room 61,
GrowerLot 448/profile 17/lot 1372 on August 22 at 13:15:52.936981 UTC. Earlier
ledger under that raw position sums to **122**, a **158-bin deficit**. Credit
1414 feeds room 66 and therefore WP-7.

Complete correction `772b30cf-20e1-4e77-9083-fb1db45448c2`, operation
release241-serial-3-61-1372-17, audit 112714, was recorded September 6 at
00:14:20.652262 UTC. Ledger 2722 removes 158 from the nullable-GrowerLot position;
2723 adds 158 to GrowerLot 448, changing -158 to 0. Its snapshot names positive
source adjustments 756/757/764/965/1010; their receipt/transfer records exist.

An initial partial trace stopped here because it considered only GrowerLot 448.
The follow-up read at **20:35:34 UTC** established **122 canonical-key + 158
nullable-key bins already recorded before withdrawal 1413 = 280**. The original
nullable-key receipts/transfers, including the separately voided duplicate receipt
499, account for the 158 without borrowing the September credit. The correction's
explicit positive source IDs, exact source/target location/crop/lot/profile,
paired ledger and immutable audit establish the missing-key equivalence. Matching
text or equal amounts by themselves are not accepted.

LegacyGrowerReconciliationOrigin proves the bounded recorded format: active complete
parent, same crop/profile, nullable source key to one canonical key, exact ordered
pair and operation suffixes/actor/timestamps, source/target balances matching the
snapshots, one matching audit, all recorded positive source IDs present exactly
once, and no competing mapping for that physical pool. This bounded shape requires
zero expected and actual treatment movements; other shapes remain unsupported.
Fresh production reads confirmed one mapping for room 61/lot 1372/profile 17 and
zero treatment movements. It does not promote unrelated room-65/68 mappings.

The interpreter then checks the combined original pool in recorded chronological
order: it must never become negative before the reconciliation. The actual debit
guard recursively validates its original arrivals and custody. Later arrivals
cannot cure earlier deficits; new missing-key arrivals after the reviewed mapping
are not silently authorized by it. Compensating legs remain at their original
September timestamps and net to zero. No new origin, stock, treatment or exact
receipt allocation is created. The [identity architecture](../inventory-identity-provenance-audit.md#partial-consumption-and-historical-reporting-decision)
still governs current writes and historical reversals; immutable historical rows
and consumed reporting snapshots are not reclassified or rewritten.

The investigation progressed through 1187, 1073 and 1413 as each dependency became
visible. With all three independently verified formats, the read-only diagnostic
visits **95 ledger events for all 15 selected source entries**, with no remaining
origin/custody failure. The compiled pure validators returned true for receipt
720 reclassification, receipt 655 initial increase and the room-61 reconciliation.
The [read-only evidence summary](actual-run-legacy-origin-readonly-2026-10-18.json)
lists the proof records and visited ledger IDs. These checks use retrieved records without writes; they are not a production
application save or a substitute for exact-candidate release rehearsal.

## Verification at reviewed head 68363666

Synthetic fixtures run on task-owned localhost PostgreSQL **18.4**. Final mandatory
governance: **322 passed, 0 failed, 0 skipped**. The
**63 focused cases** cover 54 negative format variants, normal selector/service 44+74 save,
all-table rollback at nine injected stages, quantity/treatment/stale checks,
immutable history/audit, moves/restoration, independent-session concurrency,
same-intent replay, key conflict and unchanged legacy location-only behavior.

Synthetic totals are 130 -> 12, or 474 -> 356 with all three recorded formats: exactly
118 consumed. Correct 44/74 entries, untreated movements and one run/revision/
journal/audit are asserted; shared entries have no invented ReceiptId. These are
not the production balances. Before/after comparisons preserve original receipts,
corrections, ledger and audits. Initial red/green iterations are retained as such.

Additional shared-caller regression: **84 passed, 0 failed, 0 skipped**, excluding
mandatory method selectors: **406 distinct local .NET cases** overall. Its first
run skipped the legacy normalization PostgreSQL theory because it uses a separate
environment variable; the entire selected regression set was rerun with both
disposable connection variables and all bodies passed. There are no unresolved skips.

Solution restore passed with existing ImageSharp advisory warnings. Final Release
build: **0 errors, 74 warnings** from existing source/analyzers/advisories. EF reports
no pending model changes. Scoped format verification and git diff --check pass.
Node governance tests: **29 passed, 0 failed/skipped**; metadata: 20 rules PASS.
Committed-LF policy evolution and PR disclosures were separately checked because
the Windows CRLF working copy produces a false policy-text difference in that
preflight. No governance script or policy was changed to bypass it.
CI status and final commit are recorded in the PR/completion report rather than
assumed from local tests. Scope follows command sources
and destinations, receipt changes/reversal, recorded cohorts, restoration, topology,
treatment, concurrency and architecture. Unrelated application/hardware/browser/
installer suites and production-shaped release rehearsal are not claimed.

## PR #286 review correction: the reconciliation's own debit

The owner's follow-up review of `683636669de512221364c16f002444ef50cf3981`
identified a distinct path absent from the original selected-source diagnostic:
origin validation of room 61 after reconciliation. The earlier 95-event trace
and tests followed the historical 280-bin transfer into another room. They did
not select the now-empty corrected room or reach credit 2723 and its debit 2722.

The new PostgreSQL regression uses the existing complete synthetic nullable-key
history, including the 66-bin duplicate arrival and its void, original custody,
280-bin withdrawal, complete correction and audit. It first proves the separate
pre-correction balances, +158 nullable and -158 canonical, and accepts the earlier
withdrawal. A normal canonical RoomMove then brings 10 bins into the corrected
room, making its current canonical position a positive source. The test explicitly
checks that selected evidence includes correction credit 400013 (the synthetic
equivalent of 2723).

**Reproduced before changing the guard:** the new test failed at origin validation
with `Ledger 400012: withdrawal exceeds custody established by earlier committed
events.` Credit validation recursively reaches correction debit 400012 (equivalent
to 2722). `SamePosition` combined +158 and -158 into zero and compared that physical
pool balance against the original-account correction debit of 158. The correction
does not withdraw 158 additional physical bins; it settles those recorded accounts.
The red execution is retained locally in `artifacts/origin/review-before.trx`.

The correction changes only the debit's prior-event selection. If its exact parent
belongs to an already proven reconciliation, the debit uses its original raw
identity at the same location. The existing proof independently verifies both
account balances, the exact debit/credit pair, snapshots, positive source IDs,
audit, chronology and absence of unsupported treatment movements before this
branch is available. Original positive arrivals still undergo recursive origin
validation. Other physical withdrawals retain shared-pool funding, including
the earlier 280 and subsequent movements. No credit is backdated, no origin is
counted twice, and no receipt ownership, inventory or treatment is created.

The regression also moves seven of the newly arrived ten bins out through the
normal command, replays the same intent, and validates both rooms afterward.
Source/destination totals are 410/0 -> 400/10 -> 407/3, always 410 globally;
attempting to withdraw four from the remaining three is rejected with no writes.
Ledger and treatment-segment totals reconcile, movement signatures stay untreated,
and original receipts, corrections, transfers, ledger and audit snapshots remain
unchanged. Validation itself is checked against an all-table snapshot. The new
method is an additional mandatory INV-001 contract; all previous contracts remain.
The guard's reviewed-source hash was refreshed after reviewing the read-only
change, with no writer-capability or classification change.

This follow-up preserves specification/register 1.2.0 and the same base/main and
governance commits. Report-only synchronization found no incoming commits or
policy differences; its reported knowledge differences were this PR's existing
traceability, reviewed hash and investigation records. No new production queries,
production mutations or production-data test fixtures were needed.

Final local verification of the review correction, on disposable PostgreSQL 18.4:

| Check | Result |
|---|---|
| Focused origin/Actual Run classes | 64 passed, zero failed/skipped; includes the previously failing regression and both 44+74 atomic save/rollback variants. |
| Mandatory governance/architecture contracts | 323 passed, zero failed/skipped, including those 64 cases and the refreshed reviewed-source hash. |
| Additional affected shared callers, excluding mandatory selectors | 84 passed, zero failed/skipped: receipt corrections, identity reconciliation, movement/destination, restoration, topology and independent-session concurrency. Total distinct local .NET cases: 407. |
| Node governance tests / metadata | 29 passed, zero failed/skipped; all 20 catalog rules pass. |
| Restore / Release solution build | Passed; build has zero errors and 74 existing warnings. |
| EF model / scoped formatting / whitespace | No pending model changes; format verification and `git diff --check` pass. |

Original negative format cases and unfunded historical withdrawals remain rejected.
The Actual Run still consumes exactly 118, with atomic failures, preserved treatment
and audit evidence, same-intent replay, conflicts and concurrent consumers covered.
No unrelated full application suite was run. Local red/green TRX files are retained
under `artifacts/origin`; mandatory TRX is
`artifacts/governance/18905c0b5ecd47c19d1b3c80a4fe94cd/contracts.trx`.
Final-head CI is reported in the PR/conversation after it completes.

## Production status

Nothing merged or deployed. No production inventory, Actual Run, receipt, historical
record, configuration, backup or rollback package changed; no production migration.
The completed fix is for owner review, **not production release authorization**.
Release needs explicit authorization and
the established fresh source/failed-key checks, backup, exact-candidate rehearsal,
strict readiness and compatible rollback procedure. An eventual operator save
must obtain fresh fingerprints and pass every existing guard.

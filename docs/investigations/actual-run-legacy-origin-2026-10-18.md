# Actual Run: two legacy origin formats and a remaining custody block

**The two evidenced format defects are addressed, but the production 118-bin run
still has an origin/custody block at ledger 1413. This draft PR is reviewable,
not a complete operational unblock or a release candidate.**

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
does not grant release authority here. No rule changed. The remaining historical
identity/chronology question is held for evidence review, not silently resolved
by changing policy or inventing owner approval.

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
as mutation-test fixtures or restored locally. The modified application was not
executed against production.

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
not exact surviving receipt ownership. **Quantity and treatment cover 44+74, but
the separate historical custody block below prevents final authorization.**

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
LegacyInitialReceiptIncrease.cs, LegacyReceiptReclassificationOriginTests.cs,
governance traceability, reviewed-reader hash and this report.

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
- Neither helper writes, grants treatment, creates stock/maps, assigns ancestry,
  backdates evidence or changes permissions. Incomplete/contradictory/duplicate
  evidence, invalid quantity/treatment, stale selection and custody still block.

No schema/migration/UI/API/writer/QC Station/installer change. The reviewed-reader
hash follows substantive review: no SaveChanges, SQL mutation, capability or
writer exemption. Mandatory contracts were added without removing existing tests
or changing specification policy. Retry behavior is unchanged: identical key and
complete intent replays once; conflicting payload fails; stale new-key submission
fails. A genuinely fresh new intent is a separate operation, not date/bin deduplication.

## Remaining custody block and proposed resolution

**RoomTransfer 218 / debit 1413** withdrew **280** from warehouse 3/room 61,
GrowerLot 448/profile 17/lot 1372 on August 22 at 13:15:52.936981 UTC. Earlier
ledger under that raw position sums to **122**, a **158-bin deficit**. Credit
1414 feeds room 66 and therefore WP-7.

Complete correction `772b30cf-20e1-4e77-9083-fb1db45448c2`, operation
release241-serial-3-61-1372-17, audit 112714, was recorded September 6 at
00:14:20.652262 UTC. Ledger 2722 removes 158 from the nullable-GrowerLot position;
2723 adds 158 to GrowerLot 448, changing -158 to 0. Its snapshot names positive
source adjustments 756/757/764/965/1010; their receipt/transfer records exist.

The [identity architecture](../inventory-identity-provenance-audit.md#partial-consumption-and-historical-reporting-decision)
limits correction to current remaining inventory. This current-state operation
does not contain a retrospective authorization/allocation for transfer 218.
Matching lot/profile and equal 158-bin amounts alone do not prove the earlier
withdrawal. The PR preserves its withdrawal-exceeds-earlier-custody rejection;
it does not backdate the September pair or broaden its identity map.

A read-only dependency diagnostic of retrieved records shows baseline -> 1187;
recognizing reclassification -> 1073; recognizing initial increase -> 1413.
This is a diagnostic trace, not modified application execution against production.
No complete production-origin PASS or successful production save is claimed.

Next evidence review: trace the nullable-GrowerLot source receipts/custody and any
already-authorized historical mapping, proving the 280-bin withdrawal without
reusing stock. If existing authority proves that interpretation, implement and
test it under current rules. Otherwise propose an established audited correction
for owner review. Do not manufacture origins, backdate reconciliation, or turn a
technical limitation into policy. Any necessary policy change follows the existing
business-rule change procedure. No production correction is authorized here.

## Verification

Synthetic fixtures run on task-owned localhost PostgreSQL **18.4**. Final mandatory
governance run: **301 passed, 0 failed, 0 skipped**, including **42 focused cases**.
These cover 34 negative format variants, normal selector/service 44+74 save,
all-table rollback at nine injected stages, quantity/treatment/stale checks,
immutable history/audit, moves/restoration, independent-session concurrency,
same-intent replay, key conflict and unchanged legacy location-only behavior.

Synthetic totals are 130 -> 12, or 194 -> 76 with the initial increase: exactly
118 consumed. Correct 44/74 entries, untreated movements and one run/revision/
journal/audit are asserted; shared entries have no invented ReceiptId. These are
not the production balances. Before/after comparisons preserve original receipts,
corrections, ledger and audits. Initial red/green iterations are retained as such.

Additional shared-caller regression: **52 passed, 0 failed, 0 skipped**, excluding
already-mandatory method selectors: **353 distinct local .NET cases overall**.
Governance checker tests: **29 passed, 0 failed, 0 skipped**; metadata: 20 rules PASS.
Solution restore PASS (existing ImageSharp advisory warnings); Release build PASS,
0 errors and 16 warnings. EF reports no pending model changes. Scoped formatting
verification and git diff --check PASS. CI status and final commit are recorded in
the PR/completion report rather than assumed from local tests. Scope follows command sources
and destinations, receipt changes/reversal, recorded cohorts, restoration, topology,
treatment, concurrency and architecture. Unrelated application/hardware/browser/
installer suites and production-shaped release rehearsal are not claimed.

## Production status

Nothing merged or deployed. No production inventory, Actual Run, receipt, historical
record, configuration, backup or rollback package changed; no production migration.
The draft is ready to review the two bounded fixes, **not ready for release**.
After the remaining block is resolved, release needs explicit authorization and
the established fresh source/failed-key checks, backup, exact-candidate rehearsal,
strict readiness and compatible rollback procedure. An eventual operator save
must obtain fresh fingerprints and pass every existing guard.

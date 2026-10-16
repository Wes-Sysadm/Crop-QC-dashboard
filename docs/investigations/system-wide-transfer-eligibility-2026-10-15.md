# System-wide transfer eligibility investigation

Status: **GO for focused human review after the authorized internal route correction**.
The final route-correction addendum supersedes the earlier blocker recommendation.
This remains a draft; **not release authorization**.
Base: `df977d041dae2d449cf6fe5f8581e1f5cae41947`.
Branch: `codex/system-wide-transfer-eligibility`.

## Scope and authority

Specification **1.2.0**, governance commit
`c17bd4deb477b57aa5ac1584175512d49318263b`, on fetched approved main.
Applicable: INV-001–006, TRT-001/002, MOV-001, ROOM-001, REC-001/002,
AUD-001, OPS-001, GOV-001 and ADR-001–008. Report-only synchronization
identified the old checkout and untracked release reports; the new branch starts
at current main and preserves those reports. No existing overlapping transfer
eligibility PR was found. Open drafts #269 and #272–274 address different scopes.

This implements existing rules. No new origin, custody state, treatment policy,
physical writer, receipt-allocation rule, migration, or readiness exemption is
introduced. Inventory commands retain serializable transactions, source origin
checks, room seals, permissions, exact deltas, concurrency and idempotency.
Unknown selected treatment remains blocked. Unknown receipt ancestry may remain
shared only when the requested movement's quantity and treatment are proven.

## Evidence and completeness

Production access was SELECT-only through Render's read-only transaction tool,
explicit workspace `tea-d7uc4ippo60c73ebn4mg`, database
`dpg-d8cru6reo5us73a2bgt0-a`. Observed web commit was the base above,
deployment `dep-db8qpk3l550s73escjmg`. No production transfer, correction,
normalization, backup, migration, deployment or configuration change occurred.

The already-local verified backup **204**, database snapshot
**2026-10-16 03:33:48.403371 UTC**, was restored to disposable PostgreSQL 18,
without published ports, production credentials or a production connection.
Archive SHA-256: `7030fed357e9166ac0da8747cad2c77800950d0d7a2bd71e3a82390646f3b4c2`;
20,177,849 bytes. Compressed dump SHA-256:
`c62e035f97b2b330921272342a9d23bae4803e56d681025df4adca0040ba997a`.

All **32 operational/schema tables** in the comparison matched production exactly.
The 188,841-row audit prefix also matched (`3ac43c87e91194f5cffbf8c8c27cc548`).
The only three later audit rows were backup 204 completion/notification entries
188847–188849. Final production comparison at **2026-10-16 06:23:14 UTC**
still matched the original live comparison. Evidence is a snapshot, not a claim
about business activity after that timestamp.

The actual deployed Linux application assemblies were used for the baseline
reader and selector audit, with a forced read-only connection. The candidate
uses the same canonical ledger loader, respecting baseline cutoffs and identity
normalization. Every positive room position was compared with the actual outside
selector (also used by inter-crew dispatch) and the unchanged command origin guard.
Canonical room moves and processor adapters were traced and regression-tested.

Coverage: all **4 facilities, 70 active rooms, 1,066 reader positions**, including
zero/negative/orphan representations; **452 positive positions across 66 rooms**,
152 grower lots, 31 fruit profiles/varieties, organic and conventional, crop year
2026. Treatment slices include confirmed and untreated; unresolved treatment
positions are retained separately, never classified untreated.

Reproducible evidence:

- [Every positive position, quantities, options, exact blockers, ledger IDs and replay gaps](transfer-eligibility-positions-2026-10-15.csv).
- [Coverage, remaining blocked positions, negative groups and origin errors](transfer-eligibility-findings-2026-10-15.json).
- [Origin-event classification and exact affected identities](transfer-eligibility-origin-gaps-2026-10-15.json).
- [Read-only origin diagnostic SQL](transfer-eligibility-origin-diagnostics.sql); raw sums are diagnostic comparisons, not inventory authorization.
- [All non-room custody positions](transfer-eligibility-other-custody-2026-10-15.csv).
- [Read-only fingerprint SQL](transfer-eligibility-fingerprints.sql) and [production results](transfer-eligibility-production-fingerprints-2026-10-15.json).
- [Capability review](transfer-eligibility-capability-review-2026-10-15.md).

Raw backup/receipt/audit payloads and test logs stay in restricted local evidence,
not in source control. The CSV is evidence, not executable correction input.

## Quantities and actual selection

“Ledger bins” below means the canonical positive balance; it is not blanket
certification that every historical origin and treatment allocation is proven.
“Selectable” reflects displayed, enabled, unsealed source rows. “Guard upper
bound” additionally excludes the unchanged origin guard's failures; a real
operation still needs actor, destination, custody and fresh transaction proof.

| Facility | Positive positions / rooms | Ledger bins | Baseline selectable | Candidate selectable | Candidate source guard upper bound |
| --- | ---: | ---: | ---: | ---: | ---: |
| EBS | 141 / 25 | 33,298 | 30,430 | 30,794 | 26,864 |
| DH | 127 / 21 | 16,930 | 977 | 977 | 977 |
| McDougall | 123 / 15 | 21,505 | 11,222 | 12,037 | 11,518 |
| WP | 61 / 5 | 6,036 | 6,036 | 6,036 | 5,378 |
| **Total** | **452 / 66** | **77,769** | **48,665** | **49,844** | **44,737** |

Five separate negative normalized positions total **−127**: MCD-08 lots 1270/21
−40, 1538/29 −18, 2822/18 −5; MCD-09 lots 1537/18 −20 and 1539/18 −44
(lot/profile). Net room ledger is therefore **77,642**, not 77,769. No clamp,
netting across unrelated identities, or repair is performed.

Other custody: **20 in-transit loads / 844 bins** (EBS 15/630, McDougall 5/214),
and **46 outside-held transfers / 1,409 bins** from EBS. Their existing custody
resolver passes; they are available only for supported custody transitions,
never redispatchable room stock. No receipt-held or processor custody exists in
this snapshot. Strict inter-crew topology returned zero issues; no new
partial-acknowledgement parents existed, so its partial-custody balance list was
empty. This is not an assertion that there were no legacy loads.

Mutually exclusive primary restrictions after the candidate, using seal first,
then origin, then treatment:

| Restriction | Positions | Ledger bins |
| --- | ---: | ---: |
| Actual sealed source | 179 | 25,421 |
| Unsealed origin/custody guard failure | 38 | 6,903 |
| Unsealed unresolved treatment, no origin failure | 2 | 433 |
| Unsealed mixed treatment ambiguity, no origin failure | 1 | 275 |
| Source proof passes | 232 | 44,737 |

These partitions avoid double-counting. Across sealed and unsealed positions,
the origin guard fails on **56 positions / 10,437 ledger bins**; 5,107 of those
bins are still selectable by the existing read path but rejected at command
commit. That remaining discrepancy is disclosed, not treated as authorized stock.

## Confirmed shared defects and correction

### Legacy transfer treatment proof

`InventoryEventReplay` required modern `TreatmentLineageMovement` allocations
for incoming legacy transfers, even when original receipts, the exact parent and
debit/credit pair, source occupancy, applications and committed chronology could
prove the movement. Missing projections then made valid inventory unavailable.

The new bounded loader follows the same identity across depleted source rooms,
validates parent records and exact ledger legs, and replays source custody at
departure. A same-identity location reclassification additionally requires its
complete revision, exact before/after snapshots, quantity/version consistency,
and matching audit. Correction reasons and receipt references never establish
which physical allocation was selected from a shared pool.

Whole-pool departures preserve all proven cohorts. A partial departure without
an allocation is accepted only from a uniform proven treatment pool. Mixed
treatment partial departures remain blocked. Shared ancestry remains null.
Later-recorded backdated applications and arrivals crossing earlier applications
remain unresolved. Existing contradictory modern movements cannot fall back to
legacy proof. Raw history must reconcile with the canonical baseline-aware
balance. No missing movement row is manufactured or persisted.

Recorded proof dependencies are fingerprinted **per identity**, including earlier
source-room evidence. The all-room selector and narrower command scope must
produce the same fingerprint. Source changes still make a submission stale.
Canonical case/space identity normalization also applies to historical closure.

| Facility / room | Lot / profile / variety | Proven treatment replay improvement | Currently unsealed |
| --- | --- | ---: | --- |
| EBS Evans-01 (11) | 1565 / 2 / GALA | 0 → 140 | Yes |
| EBS Evans-01 (11) | 9380 / 2 / GALA | 0 → 147 | Yes |
| EBS Evans-5 (15) | 9342 / 2 / GALA | 0 → 77 | Yes |
| McDougall MCD-05 (57) | 2710 / 7 / ORGA | 0 → 323 | **No** |
| McDougall MCD-11 (63) | 2710 / 7 / ORGA | 0 → 426 | **No** |
| McDougall MCD-15 (67) | 1110 / 19 / ORBA | 0 → 286 | Yes |
| McDougall MCD-15 (67) | 1290 / 19 / ORBA | 0 → 5 | Yes |
| McDougall MCD-15 (67) | 2825 / 19 / ORBA | 0 → 524 | Yes |

**Eight positions / 1,928 bins** gain complete treatment replay; only **six /
1,179 bins** gain current transfer selection because 749 remain legitimately
sealed. All eight pass the existing origin guard. No current eligibility decrease
was observed. DH and WP have no current position rescued by this particular
legacy-proof change; their positive positions were nevertheless audited and their
real histories included in disposable command rehearsal.

### Hidden positive positions

`CanonicalTreatmentSelections` returned zero-transferable `Needs Review` entries;
`OutsideWarehouseTransferService` and `ProcessorShipmentService` filtered them
out with `CurrentBins > 0`. Inter-crew inherited the outside selector defect.
Baseline impact: **20 hidden positions / 5,974 ledger bins** (EBS 10/2,868,
DH 2/656, McDougall 8/2,450, WP 0). Hidden does not mean all are safely movable.

The candidate retains disabled positive-authority diagnostics, exposes position
total separately from treatment-selection quantity, and preserves disabled controls.
It also shows an unresolved remainder alongside independently proven slices.
All 452 positive positions are represented after the change. Processor submission
rejects disabled selections server-side. Source seals still display zero
treatment-selection bins. No error is converted wholesale to a warning.

### Frenchman 1110 is one regression, not a special case

MCD-15 / grower lot 126 / profile 19 ORBA has committed effects:
`+32 +64 +64 +60 −64 +66 +64 = 286`.
Ledger IDs: 361, 369, 376, 380, 429, 481, 664.
Room transfer parents: 6, 10, 15, 22, 56.
The −64/+64 correction 429/430 is audited revision
`bb3504ec-722c-4986-9fd6-66775be96350` (audit 30341), relocating receipt 290's
recorded custody from room 67 to 68 without changing total origin quantity.
The later transfer 56 returns 64 from room 68. Other room histories include
later departures; replay uses source state before each debit, not today's balance.

Transfer parent creation precedes ledger creation by milliseconds; effective
business timestamps match each leg. Equality of all recorded timestamps is not
required. No applicable treatment was found for the proven departed cohorts;
this conclusion follows complete occupancy/application replay, not empty
projection links. The 286-bin treatment pool is shared; its exact receipt ancestry
is not fabricated. The 81 bins at WP-7 are a separate current position after
dispatch 23, not added to MCD-15's custody.

### Canonical text aliases and treatment reversals across legacy hops

A PostgreSQL regression using the same lot with case/space aliases exposed a
second shared-reader inconsistency. `RoomInventoryLedgerQueryService` counted
raw grower strings as distinct inside SQL before applying its existing canonical
case-insensitive grouping. After a valid move appended a differently formatted
alias, the reader reported `GrowerNumber=null` / incomplete identity although
the 12 remaining bins and 12 projected bins agreed. The final reconciliation
guard rejected the transaction and rolled it back; that guard was correct to
stop on the reader's contradictory evidence. Normalizing those two SQL selections
before distinct-counting fixes the false ambiguity without changing quantity,
baseline rules, writer behavior or genuine conflicting identities. This was
confirmed synthetically; it is not an additional quantified production failure.

Final review also found and corrected an edge case in the new replay: a treatment
reversal between legacy hops must travel as an application event even when the
intermediate room has no modern projection link. A focused three-room timeline
proves the first departure retains its original treatment and the later departure
reflects the committed reversal. Historical application and custody rows are not
rewritten.

## Remaining failures: separate work, no relaxed guards

The new read-only historical bridge deliberately does not revise
`InventoryOriginGuard`, identity corrections, receipt quantity corrections or
treatment allocation policy. The full CSV and findings JSON retain every exact
failure and affected identity.

1. **Origin evidence closure / nullable original identity** — proposed follow-up:
   “Canonical Origin Evidence Closure and Legacy Nullable Identity.” The guard
   expands raw grower/receipt/parent IDs but can omit earlier same-position rows.
   For **25 failing debit events / 25 positions / 6,298 ledger bins**, full recorded
   history using the guard's own same-position predicate funds the debit that its
   scoped closure reports unfunded. Examples include ledger 548 and 566: full
   prior quantities 52 and 12 fund debits −52 and −12. This diagnostic is not a
   substitute for validating every earlier origin or canonical baseline.
   Another **six events/positions / 825 bins** contain valid JSON `growerLotId:null`
   which the guard reads using `GetInt32()` and labels malformed. Neither family
   authorizes treating a null identity as proven; a shared canonical normalization
   and complete origin-chain implementation needs its own guarded tests.
2. **Identity-changing correction parents** — proposed follow-up:
   “Recorded Identity Corrections in Source Custody Proof.” **17 events/positions /
   2,288 bins** have balanced correction parents whose two raw identities differ;
   filtering siblings to one raw identity can report an unfunded credit. Full
   correction/audit/identity-map proof must be validated before changing admission.
   They do not qualify for this PR's same-identity location bridge.
3. **Specific unresolved authoritative events** — **five events, eight positions /
   1,026 ledger bins** remain unverified. Receipt 610: origin ledger 981 is +36 but
   current receipt is 37, with no revision. Receipt 616: origin 987 is +12 but
   current receipt is 16, with no revision. Debit 1254 is −45 with only 17 earlier
   same-identity bins; debits 1282/1284 are −1/−1 with earlier balances 0/−1.
   These require identifying the missing correction/identity/arrival event in
   system history. No request for external paperwork or physical counts is made.

The 12 remaining treatment-resolver blocks are explicitly enumerated in the
findings JSON: EVANS-2 1011/7 and 9332/7; Evans-5 1011/7; EVANS-7 3032/2;
EVANS-8 3032/2 and 3050/2; EVANS-11 9541/10; DH-19 1451/7;
DH-22 1081/9; MCD-05 2200/7 and 2910/7; MCD-06 2825/21.
Several overlap the origin families above or are sealed. Exact gaps include
ledger 845/872 identity normalization; correction arrivals 1170, 2852 and 3234;
and ledger 1274's unresolved treatment withdrawal allocation. Unknown is not
untreated. These are not automatically certified as corrupted physical stock.

Historical screening remains **146 unreversed transfers / 7,742 historical bins**
without linked modern movement rows. Grouping by destination reproduces EBS
52/2,738, McDougall 90/4,876 and WP 4/128; grouping by source puts the four WP
arrivals under McDougall (94/5,004). These historical totals are neither current
stock nor 146 confirmed failures. No links were backfilled.

## Workflow and safety assessment

- Room, inter-crew, outside and processor source selection use the common reader;
  canonical commands reload it and retain the existing origin/transaction guards.
- Receipts, receipt-held placement, outside returns, transit returns and reversals
  retain their existing custody-specific transitions. Already-dispatched bins
  cannot be selected as source-room stock.
- Treatment applications, their source membership and recorded/effective times
  remain immutable. Source replay carries application IDs to arriving cohorts;
  destination treatment history does not attach to later arrivals.
- Ordinary authorized commands may use the existing audited recorded-cohort
  reconstruction primitive inside their atomic operation. This investigation
  executes it only in disposable databases. It creates no separate writer and
  does not authorize a standalone production reconstruction or historical cleanup.
- No migrations or physical writer changes. Compensation-aware schema and writer
  fencing remain mandatory. A future release must independently prove current
  backup, schema/readiness, exact deployed candidate, rollback compatibility,
  authenticated routes and protected history. No release guard is waived here.

## Validation and readiness

The combined Linux mandatory/new run passed **183 tests, zero skipped**. It covers
actual PostgreSQL races, idempotency, missing/contradictory parents, source seals,
projection excess/negative identity representation, unknown receipt ancestry,
mixed treatment, point-in-time applications, repeated room visits, authenticated
disabled selector controls, and unchanged original history. Actual EBS, DH,
McDougall and WP histories were moved and replayed only on a disposable backup
clone; each selected identity conserved quantity and protected-table fingerprints.

Final results are recorded below. The initial
Windows-to-Linux HTTP manifest errors were runner failures, not passing evidence;
the successful run used a real Linux build. One synthetic fixture initially tried
to seed old history after a canonical command; the writer fence correctly rejected
it. The fixture now creates legacy history before canonical activation. The fence
was never disabled.

**The scoped fix does not establish that every system-wide transfer blocker is
resolved.** The independent origin/identity defects and exact missing events above
remain. No production data repair is proposed for automatic execution. Final human
review may assess this focused PR independently; production release remains a
separate, unauthorized lifecycle.

## Final validation addendum

- Final consolidated run: **290 passed, zero failed, zero skipped**, Linux
  .NET 9.0.21 and disposable PostgreSQL 18. This supersedes the earlier 183-case
  run and includes mandatory contracts, directly affected custody/receipt/
  treatment/readiness workflows, the new recorded-reversal regression and
  canonical text-alias regression. It is not the complete application suite.
- An intermediate 102-case run passed 101 and exposed the alias failure described
  above. The final run verifies the correction; the failed intermediate run is not
  counted as passing evidence. The shared ledger-reader change justified the final
  rerun, including its existing identity/baseline regressions.
- Governance tests: **47 passed**, zero skipped; metadata validation: **20 rules
  PASS**. Reviewed writer locks and executable architecture contracts passed.
  No rule, fence or release safeguard was disabled.
- Solution restore and final Release build passed (74 warnings, zero errors).
  Actual Linux build passed. Pending-model check: no changes since the last
  migration. Changed-file whitespace verification and `git diff --check` passed.
  No WinForms/MSI changes or hardware validation apply.
- Final candidate reader audit: **1,066 positions, 459 options**, zero tracked
  writes. All documented facility totals, classifications and improvements were
  unchanged after the final fixes. All 452 positive positions remain represented.
- All **33 restored-template fingerprints**, including its original audit table,
  remained unchanged after clone testing and read-only audits. Production's
  original 32 operational/schema tables and audit-prefix comparison are recorded
  separately above; the backup notification tail is not confused with an inventory
  mutation.
- Main was fetched again and remained
  `df977d041dae2d449cf6fe5f8581e1f5cae41947`; no integration update was needed.
  GitHub CI and the final commit are recorded on the draft PR and completion
  response, rather than implying that local checks constitute CI.

The final audit loaded these Linux assembly SHA-256 values inside the existing
production-runtime image (validation identities, **not a frozen release package**):

| Assembly | SHA-256 |
| --- | --- |
| CropQc.Data.dll | `05d1c5f970bfd0f4cdfa385197f5ceda1f3eea0e5170eb5d1ebf1d4901d3fa76` |
| CropQc.Shared.dll | `54b2982cd7da82099e9bf18b784579fe4bb7733a29067f63af0121320c4d9b9b` |
| CropQc.Web.dll | `9ee8e238464c5aef1048db9c610914c3076655df543739f08c1c5820b30855a3` |

The owned `transfer-eligibility-pg` container
`de9ff5ed751a6b9f281e4a6578abc9edd98c10514f77ef1f86f8fff4f956fbba`
and anonymous volume
`f80d68ad5cdcb13bb7588d307350f33cf33cb69100c4fe675f732b983b819c9a`
were deleted at **2026-10-16 06:53:44 UTC**. No owned containers or volumes remain.
Unrelated containers and the already-local verified backup were preserved.

Recommendation: **GO for focused human review subject to successful final-head
CI; NO-GO for declaring all systemic blockers resolved or for production release.**
The separate origin/identity work and specific missing events remain explicit.
No production repair, reconstruction, merge, migration or deployment occurred.

## Focused human-review revisions — 2026-10-16

Reviewed starting head: `a1e596f041c97c63bf41e640b39855bd562e68bc`.
Main/base remains `df977d041dae2d449cf6fe5f8581e1f5cae41947`; no integration
update was required. Specification 1.2.0 and governance commit
`c17bd4deb477b57aa5ac1584175512d49318263b` remain effective. The applicable
INV-001–006, TRT-001/002, MOV-001, ROOM-001, REC-001/002, AUD-001, OPS-001
and GOV-001 invariants and ADR-001–008 are unchanged. This revision adds only
qualified presentation, focused tests and traceability/documentation. No historical
replay, origin guard, command, route rule, writer capability or schema changes.

### Confirmed blocker in the actual inter-crew path

`HistoricalInterCrewDispatchTests` constructs a disposable synthetic analogue of
Frenchman 1110 / MCD-15: five historical transfer parents and an audited location
correction produce `+32 +64 +64 +60 -64 +66 +64 = 286`. The fixture uses generic
local fruit/profile IDs; it is not a reproduction of production treatment data.
It has no modern lineage movements or projection rows. To test treatment proof,
it explicitly records a subsequent room application with two exact allocations:
220 bins of shared ancestry and 66 independently receipt-owned bins. The test
asserts both quantities, no unresolved replay events, and the exact application ID.
This does not assert that production Frenchman fruit received this treatment.

The authenticated Inter-Crew page renders the 286-bin source with an enabled
selection. Its source key matches the canonical resolver fingerprint. A valid
operator (local user 8000), 40-bin partial quantity, route `WP_DH`, and exact source
key reach the real `InterCrewTransferService.DispatchAsync` and
`InventoryCommandKind.InterCompanyDispatch` executor. The unchanged source proof
passes, but the canonical route contract rejects the command:

> Inter-company dispatch requires a configured cross-company route.

The precise disagreement is:

- `InterCrewTransferService.AllowedDestinationGroups` offers both `WP_DH` and
  `EBS` for `MCD` / `MCDOUGALL`.
- `TruckReceiptRoutes.Group` puts `MCD` / `MCDOUGALL` in `WP_DH`.
- `InventoryCommandCustody.DispatchAsync` requires
  `TruckReceiptRoutes.RequiresReceiptForGroup(sourceCode, destinationGroup)`;
  `WP_DH` to `WP_DH` fails that cross-company requirement.

This is an existing selector/command route-contract mismatch, not missing source
quantity, missing treatment evidence or an invented receipt allocation. The regression
asserts the exact rejection, unchanged all-table snapshot, 286 bins still in MCD-15,
zero inter-crew transfers, zero lineage movements/projections, five unchanged receipt
origins, and unchanged original receipts, transfer parents, correction, application
sources and audit. The rejected transaction does not leave transit inventory.

**STOP / NO-GO:** the requested successful MCD → WP_DH dispatch, source-to-transit
40-bin debit/credit, Truck Receipt reconciliation, and real concurrent successful
inter-crew dispatch/retry have not been proven. They are not replaced by a successful
different route or by the earlier room-movement tests. No guard was relaxed and no
warehouse code was changed to manufacture a cross-company route. Resolve the intended
McDougall/WP_DH custody route against the approved policy in a separately scoped
follow-up before resuming this dependent rehearsal. The narrow review request explicitly
requires stopping at this command failure; it does not authorize changing that rule.

The first synthetic fixture also failed a room-name uniqueness constraint, then a
single undifferentiated treatment allocation correctly failed occupancy reconciliation.
The fixture now uses unique names and separately records the proven 66-bin receipt
allocation and 220 shared bins. Those fixture failures are not application fixes or
passing evidence. Once fixed, the actual route rejection above remained.

### Accurate selector semantics and unchanged source-origin enforcement

The two DTO properties are now named `TreatmentSelectionBins`. Outside warehouse,
Inter-Crew and Processor views label the column **Position total / Treatment
selection** and explain that proven treatment evidence is not final transfer approval;
origin, custody, permissions and current inventory are validated at submission.
Seals and existing disabled controls remain enforced; positive authoritative stock
remains visible. `AvailableBins`, source keys, selection and command semantics are
unchanged. No application error became a warning.

The existing read-only `InventoryOriginGuard.ValidateAsync` validates a recursively
expanded origin/custody closure and returns only the first failure for a batch. It
has no per-position result map suitable for these selectors. Repeating it for each
of the 452 positions repeats closure queries; using one batch result would incorrectly
attribute a failure to unrelated positions. A bounded batch classification API would
be additional origin-guard work. This revision therefore uses the requested qualified
wording instead of changing that guard or hiding positive positions.

The recorded **49,844** is treatment-selection availability. **44,737** is the prior
source-origin pass upper bound, before route, actor, concurrency and other command
checks; it is not fully transferable inventory. The **5,107** difference remains
visible and explicitly uncertified. The **56 origin-failing positions / 10,437 ledger
bins**, including sealed overlaps, and all three follow-up classifications retain
their original CSV/JSON/SQL evidence without a repeated system-wide audit.

A separate regression adds a duplicate ordinary origin in another room, outside the
selected source's historical occupancy. The 286-bin treatment selection remains
proven and rendered with qualified wording, but the actual canonical dispatch rejects
`duplicate inventory origins`; the complete database snapshot remains unchanged.
This establishes the selector/command distinction without fabricating proof or
weakening source-origin enforcement.

### Additional validation and limits

- **8 focused Linux/PostgreSQL tests passed, zero failed/skipped:** two new blocker/
  origin regressions and six existing legacy-selector tests. The existing sealed-room,
  disabled-control, treatment-carry, repeated-move, stale-selection and real competing
  room-move tests remain intact. A room-move race is not claimed as inter-crew race proof.
- Previously reviewed **290 passing cases** remain evidence for the unchanged bridge;
  the broad audit and consolidated suite were not repeated.
- Additional mandatory contracts, final build/model/format and final-head CI are
  recorded in the completion evidence below and on the PR. Two new regression
  methods are required by the existing governance contract runner; no required test
  or safeguard was removed.
- This revision used only synthetic data in the isolated `pr284-review-pg` container,
  network `none`, no published ports, no production credentials or connection.
  No production access, correction, reconstruction, migration, merge or deployment.

Recommendation: **NO-GO for accepting the requested focused review as complete**.
The qualified UI revision is ready to inspect; the route mismatch must be resolved
before the successful inter-crew and Truck Receipt proof can be completed. This report
makes no production-release readiness claim.

Focused-revision completion evidence:

- Required contracts: **168 passed, zero skipped/failed**, Linux/PostgreSQL. The
  runner's required-method and structural-suite coverage was independently checked
  against the TRX. This is additional mandatory verification, not a repeat of the
  full 290-case investigation run.
- Governance: **47 tests passed**, metadata **20 rules PASS**. Solution restore,
  Release build (zero errors), actual Linux build, pending-model check (no changes),
  changed-file whitespace verification and `git diff --check` passed. Existing
  package/nullability/analyzer warnings remain; no migration or MSI applies.
- Main was fetched again and remained `df977d041dae2d449cf6fe5f8581e1f5cae41947`.
  The exact revised commit and final-head GitHub check results are recorded on
  PR #284 and in the completion response; local PASS is not a CI or release claim.
- Owned synthetic container
  `f7e1be86a3189e983c6b2744aaf642f2d40b7d840fecb321b4fdd8e558b69a0d`
  and anonymous volume
  `db2eabf6c18c37694db2994952497283f2e25e946a525fb1e78524ec5793fe8a`
  were deleted and absence verified at **2026-10-16 07:38:58 UTC**. Unrelated
  containers, backup #204 and pre-existing untracked reports were preserved.

## Authorized internal route correction — 2026-10-16

Owner instruction: “PR #284 — Correct McDougall Internal Inter-Crew Route”,
continuing reviewed head e67a4167b979a545cf3e03321e228d261c640597. Main was
refetched unchanged at df977d041dae2d449cf6fe5f8581e1f5cae41947; no integration
update was needed. The specification, governance commit and invariants above remain
effective. Owner-supplied counts (22 internal received loads / 2,212 bins; five
internal transit loads / 214 bins; 15 EBS loads / 630 bins) are context, not a new
production audit this turn.

### Exact contract and defects corrected

The UI offered McDougall → WP_DH, but the canonical writer confused cross-company
receipt eligibility with dispatch admission. The new shared
`TruckReceiptRoutes.InterCrewDestinations/AllowsInterCrewDispatch` matrix supplies
the service, rendered selector, canonical writer and canonical topology check.
Existing Group/RequiresReceipt/RequiresReceiptForGroup meanings are unchanged.
McDougall remains part of the WP_DH company group.

| Source | Destination | Dispatch | Receipt policy |
| --- | --- | --- | --- |
| McDougall / MCD | WP_DH | Allowed internal transit | False; exact full-load internal receive |
| McDougall / MCD | EBS | Allowed cross-company | Existing activation persisted at dispatch |
| EBS | WP_DH | Allowed cross-company | Same persisted policy |
| WP or DH | EBS | Allowed cross-company | Same persisted policy |
| WP or DH | WP_DH | Blocked | No write |
| EBS | EBS | Blocked | No write |
| Unknown source or destination | Any | Blocked | No write |

Enabling Truck Receipt cannot change an internal load into a cross-company load.
Feature pause/reactivation does not rewrite the stored cross-company requirement.
Partial acknowledgements/compensations retain their existing cross-company proof.
The UI's JavaScript fallback allowing unknown sources was replaced with rendered
shared-contract destinations. Canonical topology rejects unsupported routes and
internal loads incorrectly flagged as requiring a Truck Receipt; historical
noncanonical topology is not reinterpreted.

A complete lifecycle test exposed a second defect: the canonical received-return
writer cleared BinsReceived/destination while retaining the receive ledger. Strict
readiness correctly failed. Internal returns now retain the completed receive
snapshot, matching the established internal reversal and strict ledger contract.
Cross-company reopen/return behavior is unchanged. No readiness assertion was
removed and no error became a warning.

### Exact reconciliation and integrity

The regression uses the existing synthetic 286-bin Frenchman history, including
five recorded room transfers and an audited receipt relocation. It retains 220
shared-ancestry bins and 66 independently receipt-owned bins; an explicit application
proves treatment membership. Actual service/command execution gives:

| Checkpoint | Source room | Transit | WP room | Total |
| --- | ---: | ---: | ---: | ---: |
| Before | 286 | 0 | 0 | 286 |
| Dispatch 40 | 246 | 40 | 0 | 286 |
| Exact internal receive | 246 | 0 | 40 | 286 |
| Untouched allocation return | 286 | 0 | 0 | 286 |

Every checkpoint passes strict inventory deduction, treatment lineage and topology
readiness. Original receipts, ledger history, transfer parents, receipt correction,
application sources and audit evidence remain unchanged. New movements retain
application 800000; ancestry remains null where shared and 100004 only where already
proven. No second ReceiptAdd or transfer evidence receipt is created. Historical
completed-parent manifests remain nonoperable; active transit has no completed load.

Two independent PostgreSQL sessions attempt 160-bin dispatches from 286: one commits,
one rejects, leaving 126 room / 160 transit. Replayed dispatch, receive and return
are idempotent. Stale fingerprints, changed intent under the same key and a 39-bin
receipt for the 40-bin load reject without writes. Duplicate origin still blocks
despite selectable treatment evidence. The prior 1,179-bin selection improvement,
56 separate origin failures and their exact evidence remain unchanged.

Actual EBS → WP_DH and McDougall → EBS service controls persist the receipt flag,
reject direct internal receive, unmatched completion and quantity mismatch without
writes, then complete 19 bins exactly once with original ancestry and no new origin.
Existing varied-profile matching, activation, held-custody, compensation, treatment,
consumption, seal, origin and concurrency regressions cover the shared paths.

### Final verification and release limits

- 22 focused route/lifecycle cases passed. Initial reversal failure was corrected
  in the writer; strict readiness was retained.
- 188 mandatory contracts passed, zero skipped/failed; exact required-method and
  structural-suite coverage independently checked against the TRX.
- Expanded affected run: 266 passed, one content-lock encoding failure, three legacy
  PostgreSQL test definitions skipped for missing fixture configuration. The content
  lock was corrected using UTF-8 BOM handling after reviewing the unchanged service
  diff. The mandatory rerun passed. The eight legacy PostgreSQL cases then passed
  with their dedicated fixture configuration. Across final passing evidence, 275
  distinct cases pass; the initial failed/skipped run is not represented as PASS.
- 29 governance metadata tests passed; metadata validation reports 20 rules PASS.
  Prior 290-case / 168-contract evidence is retained, without a new production audit
  or unrelated full-suite run.
- Solution restore, Release build (zero errors, 74 existing warnings), actual Linux
  build, EF model consistency, changed-file formatting and whitespace checks passed.
  No model change, migration, WinForms/MSI or hardware change.
- Final-head CI is recorded by GitHub and in the completion response. Local results
  are not a substitute for final-head CI or production release safeguards.

Only synthetic data in the owned, network-isolated pr284-route-pg container was used
this turn: no ports, production credentials or production connection. The separate
legacy test database uses the current EF model. An extra full-chain empty-database
migration attempt stopped on the existing identity migration's missing GrowerLots
table. No migration source changed. The model-generated fixture is not evidence of
a full-chain migration or production-schema rehearsal; established production-shaped
schema/compatibility preflight remains required for release.

Recommendation: GO for focused human review, subject to final-head CI. This is not
production release approval. A future release still requires a fresh independently
verified backup, strict readiness, schema preflight, compensation-aware rollback,
authenticated affected routes and independent post-deployment verification. No new
rollback artifact was prepared or claimed here. No production access, correction,
projection reconstruction, migration, merge or deployment occurred.

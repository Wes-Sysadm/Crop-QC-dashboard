# Receiving corrections after movement and consumption

Investigation date: 2026-10-13. Base: `75b8dbe00210d413d35ae41d0b134b312a3ad66e` (merged PR #281). Continued draft: [PR #271](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/271), `codex/authoritative-partial-custody`. Local integration commit: `d3cf023`.

## Authority and scope

Specification 1.1.0 and ADR-001–ADR-007 apply. Governance paths were compared with fetched main; latest governance-path change was `c21da183961f6d0304a83a1c2eb10fc6e7f9884d`. Applicable rules: INV-001–INV-006, TRT-001/TRT-002, REC-001/REC-002, MOV-001, ROOM-001, AUD-001, OPS-001 and GOV-001. This implements those rules; it does not amend inventory authority or treatment policy.

Production access was read-only SQL in workspace `tea-d7uc4ippo60c73ebn4mg`, database `dpg-d8cru6reo5us73a2bgt0-a`. Evidence captures: 18:59:58Z and 19:17:18Z. Queries are in [the evidence SQL](receiving-corrections-evidence.sql). Raw private audit/command payloads and restored dumps remain outside source control. No production correction, reconstruction, migration, backup run, merge or deployment occurred.

## Findings and independent receipt reconciliations

These are three separate proposed corrections. No quantities are netted across lots. Requested new totals are operator statements, not proof that additional bins are currently in a particular room or have a particular treatment history.

| Receipt | Authoritative recorded history and current custody | Requested correction | Conditional after-state |
|---|---|---|---|
| TR110045 / receipt 2476 / lot 9502 Conventional RED / BLUE04 (room 30, code BM-4) | ReceiptAdd 4150 +13; arrival movement 832 into segment 769. Bins Run ledger 4374 -13, movement 1071, entry 503, run 168/revision 193. Segment 769 is historical, zero bins. **13 − 13 = 0 current.** | 13 → 15, +2 | **15 − unchanged consumption 13 = 2** only after an authorized correction origin and explicit current location/treatment evidence for the extra two. Their location, prior treatment and consumption are not proven by the reported new total. |
| TR110044 / receipt 2475 / lot 9501 Organic ORRD / BLUE04 | ReceiptAdd 4149 +35; movement 831; exact current segment 768 =35, untreated. No recorded outgoing movement, consumption, correction or external custody for this receipt. | 35 → 20, −15 | Exact receipt stock 20; room/lot pool 569 →554. Older unassigned 490 and receipt 2467's 44 stay unchanged; 10 in room 6 are separate. |
| TR110057 / receipt 2490 / lot 9333 Conventional GSMT / LAMB14 (room 7) | ReceiptAdd 4164 +27; movement 846; exact current segment 784 =27, untreated. No recorded outgoing receipt movement, consumption or correction. | 27 → 31, +4 | Receipt stock 31; room/lot pool 561 →565, only with explicit evidence for the extra four. Original 27 remain unchanged; no room treatment is copied to added bins. |

TR110045 original receipt business time is October 6 20:44Z, committed 20:46:14.360421Z. Its dump business time is **October 8 00:01Z (October 7 17:01 Pacific)**, committed **October 13 15:55:58.700668Z**. The correction must not rewrite either clock or interpret the later commit as a later physical dump. The dump's command is `a3bc2aa9f36d434fb17bab8408e67e2d`; audit 182704 corroborates its exact 13-bin debit among the run's other, separate lots.

TR110044 was received October 6 20:40Z, committed 20:41:27.340581Z. Transfer 413's 44 ORRD bins and a related profile correction belong to other receipt history, not this receipt. TR110057 was received October 7 00:10Z, committed 00:10:59.337133Z. LAMB14 applications 2, 3 and 5 occurred August 21, before this receipt; no application-source or segment links assign them to its October arrivals.

The expanded identity scope returned 31 receipts, 36 ledger rows, 17 segments and 16 movements; those counts include earlier related pool history, not 31 copies of the three target receipts. It contained one unrelated-to-target-receipt identity override, one room transfer, one Bins Run entry, and no linked loss, intercrew transfer, outside transfer, application source or segment application links. Supplemental queries found no target/lot room depletions, processor shipment lines or target receipt deletion audits. Target creation audits are 172368/172372/173458, with command audits 172369/172373/173459. The four origin/run commands corroborate their committed records.

## Exact reproduced failure versus missing historical request evidence

The deployed #281 implementation was exercised against a separate clone of verified backup #198 before changing correction behavior:

- **TR110045 reproduced a block:** `Select which current receipt location and treatment allocations should be corrected. No current stock can be invented or recalled.` `ChangeReceiptQuantityAsync` requires a nonempty surviving allocation for every non-void correction, including a positive delta. Availability correctly returns zero surviving stock after the dump. Requiring consumed stock to survive before creating an authorized correction origin is the defect; neither reversing the dump nor reviving its segment is appropriate.
- **TR110044 35 →20 committed successfully** on that baseline. Its exact receipt allocation is distinguishable inside the older shared pool. The historical same-pool provenance defect is already covered by `ReceiptQuantityCorrectionTests.TR110044_buffer_correction_proves_new_receipt_inside_older_unassigned_pool`. No new current authoritative inconsistency was found.
- **TR110057 27 →31 committed successfully** on that baseline. Its current receipt allocation is proven. The existing #269/#271 treatment correction remains necessary for additions to treated allocations generally, but no treatment link on this receipt establishes that as its reported failure.

For the latter two, the missing evidence is the failing request timestamp, actor/permission identity, submitted version/fingerprint/allocation fields and application response/exception. A Render log query covering October 6–13 and these receipt/error strings timed out; no matching historical exception was retrieved. Main's administrator-only correction access can block receivers, while #271 already changes quantity-only authorization to receiving-create access; without the actor/request evidence that is a possible explanation, not a confirmed cause. Do not request physical counts or paperwork to explain these software failures by default.

## Overlap and smallest integration path

- #271 was open/draft at `58e6d29edc9d81326274a3d7ff73c56dde20fcc5`, based on pre-governance main `e58d73a12c8bf0cc5f0f2333e442497e2b91b807`, with seven merge conflicts against current main. It already includes receiver permissions, exact partial allocation acknowledgement/placement, compensation, treatment-safe positive corrections and three additive custody migrations.
- #269 was open/draft at `c2d366733d770d413643bfb1abd7640c03fa94e4`; its explicit untreated confirmation and correction-time posting already exist in #271. #268's legacy receiving/arrival/retry work also overlaps #271. Do not independently merge those duplicate implementations.
- Open #272/#273/#274 cover diagnostics, reconstruction and governance areas subsequently developed on main; they are not a dependency or a reason to repair historical projections here. #250 antiforgery and #248 old repair-trigger work are separate historical drafts, not branches to stack on.
- Continue #271, integrate approved main first, then add only the missing depleted-receipt correction intent. No new competing correction PR was created. Existing local worktrees and their untracked release evidence were preserved.

Integration retains #281's destination admission, isolated CohortKey factory and preservation of prior destination rows. A real additional integration defect was fixed: #271's held-custody placement used full destination `IsOperable`/normalization and could reject a valid incoming allocation solely on a derived discrepancy. It now uses the same `PrepareDestinationAsync` and exact destination delta verification as approved main. A dedicated partial-placement regression proves preserved prior rows and exclusive custody.

## Safeguard classification and operator behavior

| Class | Check and evidence boundary |
|---|---|
| BLOCK | Missing/duplicate receipt origin; inconsistent committed origin/custody chain; negative authoritative quantity; transfer receipts used as a new origin; ambiguous receipt ownership required by a reduction; insufficient selected current quantity; unresolved external/held custody; forbidden identity/location combination. |
| BLOCK | Permissions, active warehouse/room, seals, exact allocation selection, treatment evidence, receipt version and destination fingerprint, serializable transaction conflicts, changed replay payload, writer capability and strict readiness. A reason is not allocation proof. |
| BLOCK | Uncertain or historically treated extra bins cannot use the untreated addition path. Evidence review or a separately reviewed treatment-specific correction is required. No default to untreated. |
| WARN | A destination's derived discrepancy can remain diagnostic when origin/custody admission is valid, the incoming bins have their own proven cohort, old rows/applications are preserved, and ledger/projection deltas exactly match. This does not clear strict readiness. |
| RECONSTRUCT | Proven event-backed source normalization uses existing canonical audited primitives. Broader historical reconstruction remains separate approval-bound work. A displayed balance, same-lot stock or correction reason cannot authorize it. |
| Unnecessary block removed | A positive correction after complete consumption no longer requires an already-existing current receipt allocation. It instead requires its recorded origin, explicit additional-stock intent, location and known treatment evidence. |

The preview shows original recorded quantity, current receipt total, requested total/delta, current attributable custody, event history with effective and committed timestamps, treatment signatures and unresolved proof. The difference between receipt total and proven current custody is labelled as such, not automatically called consumption. Decreases still select exact surviving allocations; a request below committed consumption/current unresolved custody cannot erase those records. Existing downstream corrections use separate authorized compensating operations, never implicit reversal.

For a fully depleted ordinary receipt, the operator explicitly selects an active unsealed room in the same warehouse and confirms that the additional bins belong to that receipt, are physically present there, are not consumed/other-custody bins, and are known untreated. No room is silently selected. The canonical executor validates the original ledger history (including empty current history), rechecks the preview and creates a linked `ReceiptInventoryOverride`, +N ledger event, receipt-owned correction movement and separate untreated cohort at correction time. Original receipt arrival time and all consumed evidence remain unchanged. This is a newly authorized correction origin, not a projection repair or recall of consumed bins.

The reviewed alternative for historically treated additional fruit remains blocked until the exact arrival/application relationship is supplied through an evidence-backed workflow. This implementation does not claim the two RED or four GSMT bins satisfy that untreated/current-location assertion in production.

## Validation and limitations

The initial integrated correction/custody suite passed 64 cases with one explicit restore-fixture skip. That skipped #281 Lamb15 test was subsequently run against a disposable backup #198 clone and passed. The additive custody migrations were applied to local clones already containing the later CohortKey migration; original table fingerprints remained unchanged. The restore fixture now handles that nonchronological adoption explicitly instead of assuming that the latest dated migration implies earlier draft migrations are present.

New regressions cover complete consumption after movement with treated/untreated history, explicit missing-proof/seal/stale blocks, simultaneous corrections, replay, operator preview/submission, atomic failure at movement and commit, preserved destination discrepancy, and all three reported receipts on backup #198. Existing partial/mixed-custody, downward correction, #281 destination and strict 28-case correction-verifier coverage remains in scope. Governance now requires the integrated partial-custody and correction-treatment contracts; no prior mandatory contract was removed.

The restored three-receipt rehearsal changes only the authorized test receipt quantities and the exact targeted existing reduction segment, plus new correction records/cohorts. Historical ledger/movement/application/audit fingerprints and all unrelated table rows are unchanged. TR110045's entry 503 remains 13; strict deduction readiness passes afterward. These are synthetic local corrections, not production execution or factual attribution of the additional bins.

Local Windows VSTest stalled before testhost startup. Tests instead use .NET 9/Linux on an isolated Docker network with PostgreSQL 18, bounded by the repository's canonical runsettings. Generated Windows content-root/static-asset paths were remapped in ignored build artifacts for Linux HTTP testing. No runtime safeguard was disabled. A new test's historical seed was moved before the first canonical journal entry after the writer guard correctly rejected a legacy write; the guard was preserved. The legacy scalar correction regression now explicitly seeds that old snapshot format in its disposable test database, since the integrated writer emits full audited snapshots.

Final local affected suite: **154 passed, zero failed/skipped**, including all 43 required reflected governance methods and every structural suite. Governance metadata passed (20 rules); all 29 governance tooling tests passed. Solution restore/build and EF pending-model consistency passed; no new schema migration is required for the added quantity-correction path. Earlier fixture/runner failures are superseded by this final bounded run. Formatting, whitespace and GitHub CI/final head are recorded in the PR and closeout. No unrelated full-suite recertification, WinForms change or MSI is part of this task.

## Release and production correction prerequisites

PR #271 remains draft. The quantity-correction addition requires no new schema; the already-existing #271 custody feature still introduces its three additive migrations. After custody has been used, current production #281 is **not a compatible application rollback**: its readers do not understand receipt-held compensation, and the migration's writer fence rejects it. A frozen compensation-aware rollback package, read/write rehearsal, fresh independently verified production backup, migration compatibility, full readiness, health and independent post-deployment verification remain mandatory release gates. Never use a destructive Down migration to roll back.

No production repair is proposed for TR110044 or TR110057 merely because a prior request failed. Their requested corrections remain separately authorized operations subject to fresh versions and exact scope. TR110045 needs a new +2 correction with explicit evidence for those bins; preserve the original -13 dump. Any production execution requires separate applicable owner authorization, fresh backup/recovery protection, audited execution and independent verification. The 210-bin historical RED reconstruction is excluded.

Remaining uncertainties: the two unreproduced original request failures; actual current location/treatment history of the additional RED/GSMT bins; release-time backup/readiness and frozen rollback artifact; independently approved disposition of overlapping older drafts. None is resolved by accepting a derived room balance as inventory authority.

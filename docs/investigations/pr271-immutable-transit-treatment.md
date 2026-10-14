# PR #271 immutable transit treatment proof — stopped at readiness

Owner instruction: “Fix Immutable Transit Treatment Proof and Resume Rollback
Rehearsal”, 2026-10-13 America/Los_Angeles. This authorizes implementation and
disposable rehearsal, not merge, deployment or production mutation.

**NO-GO for advancement out of draft.** The original treatment defect is fixed
and the complete compensation-aware application rollback scenario passed,
including authenticated reads/writes. The subsequent strict readiness command
failed at its existing topology predicate. Per the explicit stop instruction,
that predicate is unchanged and the rehearsal stopped. Green CI cannot override it.

## Repository and rules

Fetched main/base remains `75b8dbe00210d413d35ae41d0b134b312a3ad66e`.
Started at PR head `8b3795eb8911aee1f42d435f089113ca145bf320`, with application
source `19473648ec84f99b7ae8793312b6adf40e125cc7`. Continued the detached
integration checkout of `codex/authoritative-partial-custody`; no new feature PR.
Report-only knowledge synchronization found zero incoming main commits and only
the known PR-local proposals. Specification 1.1.0, governance source
`c21da183961f6d0304a83a1c2eb10fc6e7f9884d`, applies.

Read AGENTS, canonical rules, ADRs, change procedure, Phase 1/2 evidence/command
contracts, prior #271 reports, migration procedure, change-scoped testing and
overnight release standard. Applicable rules: INV-001–006, TRT-001–002,
REC-001–002, MOV-001, ROOM-001, AUD-001, OPS-001 and GOV-001; ADR-001–006,
especially ADR-003–005. This is an implementation repair, not a policy amendment.

## Confirmed failure and separate proof obligations

Re-ran `DispatchProbe` inside both previously frozen applications, against the
documented disposable failed-rehearsal database. Both rejected
`r271-dispatch-probe` with `MixedTreatmentAmbiguity` at the transaction-local
`PersistedSource2` checkpoint and rolled back all observed operational tables.
The retained [forward inputs](pr271-rollback-forward-blocker.json) and
[rollback inputs](pr271-rollback-candidate-blocker.json) document the original
same-shape diagnostic; fresh rerun outputs are retained with the local artifacts.

The original restore fixture has **40 originated bins**, selects **30** (15 from
each cohort), and leaves 10 at source. It is not a 30-bin original receipt pool.
The new PostgreSQL regression separately originates exactly **15 + 15 = 30** and
finishes with all 30 in destination rooms, preserving 15 treated and 15 untreated.

| Obligation | Actual evidence |
|---|---|
| Physical quantity | Recorded receipt credits, dispatch debits −15/−15, 30-bin parent and two immutable 15-bin movements. |
| Current custody | Parent physical flow, dispatch minus returns/net acknowledgements, placements minus linked reversals; no duplicate room credit. |
| Receipt ancestry | Each dispatch retains its source receipt; unknown ancestry remains unknown in the separate regression. |
| Treatment membership | First receipt's recorded application and source links; later receipt has a distinct `u` departure and no application links. |
| Room arrival | Incoming room movement or original receipt ledger proves room occupancy/application timing. |
| Transit allocation | Original outgoing movement/source identity, receipt, signature/state, application links and verified custody continuity. No new arrival into the old room is needed. |

`InventoryAvailabilityResolver.ValidTreatment` previously reused the untreated
room predicate for custody. Transit evidence contains outgoing dispatch movements
and parent debits, not receipt-scoped room arrivals. `exactReceiptAllocation`
therefore became false, allowing the first receipt's application to invalidate the
second receipt's untreated slice. Equal quantities/custody did not waive that
treatment failure. The outer error was the combined postcondition:
“Dispatch ledger, immutable allocations and custody parent do not conserve quantity.”

## Smallest shared correction and callers

- `InventoryAvailability.cs`: adds read-only `InventoryCustodyTreatmentProof` to
  projection evidence, preserving the original source membership and departure.
- `InventoryCustodyTreatment.Read/Proves`: checks exact source/movement identity,
  direction, supported departure kind, parent, source segment, receipt (including
  null), signature/state, nonduplicated application references and application
  existence/time. Business application time and recorded time cannot be after
  departure. Audited application reversals use the existing effective-treatment
  interpretation. Total represented custody cannot exceed its movement quantity.
- `InventoryCustodyEvidence.LoadCustodyAsync` and
  `InventoryReceiptHeldEvidence.LoadReceiptHeldAsync`: attach that source proof
  and application recording timestamps while retaining existing physical and
  compensation checks. Historical source segment quantity is not availability.
- `InventoryAvailabilityResolver.ValidTreatment`: non-room custody uses the
  immutable departure proof. Room treatment predicates and #281 admission stay
  unchanged. No fallback converts unsupported transit evidence to untreated.
- `InventoryCommandReceiptCustody.ChangeReceiptCustodyAsync`: placement uses the
  same proof rather than accepting a known-looking state string alone.

Shared caller assessment:

| Path | Effect / retained contract |
|---|---|
| Room transfers, source normalization, room receipt correction | Existing room-arrival/replay contract unchanged; selected origin/quantity/treatment still required. |
| Inter-crew dispatch and transit editing | Their post-dispatch resolver now proves each immutable cohort separately. |
| Outside/processor custody and returns | Same non-room loader/proof; exact physical flow and complete selected allocation requirements retained. |
| Receipt acknowledgement and acknowledgement reversal | Existing exact dispatch IDs, quantity bounds, parent versions and compensation history unchanged; receipt-held reads now use departure proof. |
| Placement | New shared treatment check before destination credit; #281 destination admission unchanged. |
| Placement reversal | Existing untouched-allocation/source/application equality, dependency and origin guards unchanged. |
| Treatment reversal | Existing audit changes current effective membership; original departure/application history remains. |

Unknown, absent, contradictory or future application evidence remains blocking.
Tests reject missing proof, unknown state, wrong source/receipt, missing application,
future effective/recorded application and duplicated custody allocation. The
unknown-ancestry test permits proven treatment/quantity but rejects exact-receipt
operations. No origin, treatment application, repair authority or second ledger
is introduced. No database model or migration changed.

## Frozen candidates and migration compatibility

Forward application: `5846f03f2e6f1317e684ef6d12413359931ea6e0`.
Compatible rollback: `a3c79e257695396db782d670031b7f1a55ee4c9a`, on
`codex/pr271-compensation-rollback`, based on prior candidate `4ee804d`.
Both contain the identical six-file application correction. The rollback retains
the earlier compensation-aware feature profile, reviewed #281/admission fixes,
and the newer `AdditionalStock` serialized command shape. New after-consumption
addition intents deliberately block there; already committed forward intents
successfully replay and their recognized origin remains readable/movable.

Exact source archives were published with the repository build stage and the
retained production-matching runtime image from #281. Both ran Linux amd64,
.NET/ASP.NET Core **9.0.20**. This is execution in the recorded matching runtime,
not a new claim about live Render configuration. Image identities, loaded DLL
hashes and package checksum are in [the manifest](pr271-transit-manifest.json).
The archive is local, frozen and not deployed; it must be preserved for release.

Fresh disposable database `pr271_treatment_rollback_test` was restored from
independently verified backup #198. Archive and dump hashes were recomputed;
the container copy matched too. The Docker network is internal, with no production
connection or external credentials supplied. Synthetic changes stayed in this clone.

Applied only the three missing bounded additive migrations, in order:

1. `20261007212154_ReceiptHeldTransferCustody`.
2. `20261007235352_ReceiptCustodyCompensations`.
3. `20261007235822_ReceiptCustodyGuardrails`.

Existing `20261009150448_IsolatedInventoryMovementCohorts` remained applied and
was not replayed. All original columns/rows of **109 tables** matched across
migration. No Down migration, old-database restore over later activity, or writer
fence exemption was used. The `compensations-v1` capability remains mandatory;
direct unfenced physical SQL was rejected unchanged under both applications.

## Executed rehearsal and integrity results

See [structured results](pr271-transit-evidence.json) and the committed
`scripts/rehearsal/ReceiptCustodyRollback` driver. Commands run beside each frozen
application's own DLLs/deps/runtimeconfig; the driver does not substitute Data/Shared.

| Gate | Result |
|---|---|
| Original defect in both old packages | Reproduced, atomic rejection verified. |
| Fresh restore/additive migration | PASS, original 109 tables unchanged. |
| First mixed 30-bin dispatch | PASS: movements 1080/1081, source segments 972/973, receipts 2654/2655, application 30 on treated cohort only. |
| Partial acknowledgement / placement | PASS: 22 acknowledged, 14 placed initially. |
| Partial acknowledgement reversal / placement reversal / re-placement | PASS: net 20 acknowledged, 13 placed, 7 held, 10 transit at forward checkpoint. |
| Forward journal replay | PASS, whole-database row hashes unchanged. |
| Existing historical correction and depleted-receipt additional origin | PASS on clone only, audited correction/replay evidence retained. |
| Application handover | PASS: identical whole-database hashes before rollback writes; no database restore. |
| Cross-version additional-origin replay | PASS; new unsupported addition rejected atomically. Existing added stock remained readable and movable. |
| Independent-session compensation race | PASS: one Committed, one Stale; winner replay made no changes. |
| Rollback compensation, re-placement, mixed partial moves, acknowledgement/placement | PASS with original membership and exact custody. |
| Authenticated rollback HTTP | PASS: health/db health, receipt and transfer reconciliation 200; exact treated placement and untouched reversal committed through normal forms. Missing antiforgery rejected 400; duplicate submission caused no additional writes. |
| Independent forward reader after rollback/HTTP | PASS: protected history, current custody and treatment unchanged. |
| Strict `--verify-release-readiness` | **FAIL**, topology only; stopped as required. |

Final synthetic mixed pool: **10 source + 14 placed + 6 held + 10 in transit = 40**.
Treated: **5 source + 7 placed + 5 held + 3 transit = 20**.
Later untreated: **5 source + 7 placed + 1 held + 7 transit = 20**.
The original treatment still covers exactly its original 20; zero extra
applications. The separate 30-origin regression finishes with exactly 15 treated
and 15 untreated in destination rooms, no held/transit remainder.

Protected historical rows across 16 table groups and forward-created immutable
rows across 13 groups matched. The existing driver explicitly permits the selected
restored receipt 2475 / segment 768 correction, proves its override/audit, then
corrects quantity 35→34→35 across forward/rollback. Those authorized synthetic
correction effects are not described as unchanged rows. No production receipt
correction occurred. Original applications, source membership, movements, ledger
and audits were retained. HTTP proofs also checked that reads and rejected/replayed
requests made no row changes.

## Exact new stop condition

Strict readiness exited **1**. Schema passed; deduction readiness passed (1,138
negative adjustments: 93 historical, 1,045 new-format; zero blockers); treatment
readiness passed for **442** identities with zero blockers. Topology alone failed
with `invalidInterCrew = 1`.

`src/CropQc.Web/Program.cs`, the `--verify-release-readiness` block, still counts:

```csharp
x.Status == InterCrewTransferStatuses.InTransit &&
(x.DestinationWarehouseId != null || x.DestinationRoomId != null || x.BinsReceived != null)
```

The only matching row is synthetic transfer **43**: `InTransit`, loaded **30**,
received **20**, destination warehouse **1**, room **990002**, receiving receipt
**2656**, version **14**. Net acknowledgements are 12 + 7 + 1 = 20; net placements
7 + 6 + 1 = 14. The remaining 10 must stay in transit. Erasing received/destination
fields or falsely setting Received would violate the committed custody record.
This is a legacy whole-load topology predicate incompatible with legitimate
partial custody, not evidence of missing bins. The read-only query/result is in
[the blocker evidence](pr271-transit-readiness-blocker.txt).

The next scoped task must make that readiness validation compensation-aware while
still rejecting invalid/missing dispatch, acknowledgement, placement, reversal,
parent-link and quantity evidence, then rerun its impacted release gate. This task
does **not** amend it, exclude transfer 43, clear fields or waive the failure.
The full rehearsal cannot be reported as a release PASS while this gate fails.

## Validation and remaining release requirements

- Solution restore/build PASS; EF model consistency PASS, no pending changes.
- Focused affected selection: **96 unique cases passed across runs**, zero skips
  (61 initial successes, four workflow cases rerun after fixing a test's unbounded
  query scope, plus 31 existing custody/correction/return cases). No application
  failure was hidden by the fixture correction.
- Mandatory selection: **119 passed, zero skipped**: original 106 plus 13 new
  treatment cases. All 49 required methods and both structural suites discovered.
  This includes #281 admission/alias regressions. No mandatory method removed.
- Governance metadata and **29** governance tooling tests PASS. Only the four
  reviewed changed registry entries were refreshed; the new helper is read-only.
- Changed-source formatting and `git diff --check` PASS. No WinForms/MSI change.
- Final-head GitHub checks are linked on PR #271; report their actual completion
  separately. Existing package advisories/warnings remain; dependencies unchanged.

Preserve the earlier 160-case historical evidence for its tested revision; it is
not asserted as a new full run. Broader unrelated suites were not rerun. The shared
custody treatment dependency justified the focused caller and mandatory runs above.

**Keep #271 draft / NO-GO.** The exact remaining blocker is strict partial-custody
topology readiness. A future authorized release also needs the exact merged SHA,
current production runtime comparison, fresh independently verified production
backup, strict readiness, controlled migrations and independent production checks.
No production access/write, migration, reconstruction, merge or deployment took
place. Manual-origin/baseline changes, production TR110045/TR110044/TR110057
corrections and the separate 210-bin RED reconstruction remain excluded.

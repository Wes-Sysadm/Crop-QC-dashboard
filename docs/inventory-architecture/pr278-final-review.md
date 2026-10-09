# PR #278 final code review and rollback rehearsal

Review started at `490709959ca32a0a28f22a90f84d6661917cb3c4`; fetched main/base
`50bdc1a3a0dbbdfde435e2b87e24e43a2fd486a8`, with no incoming commits.
Specification 1.0.1; governance commit `566413428f86266c1d3d5015010276ea0090115f`.
Rules INV-001–006, TRT-001–002, MOV-001, REC-001–002, ROOM-001, AUD-001,
OPS-001 and decisions ADR-001–006 apply. No foundational policy change.

## Defects reproduced and corrected

1. Replay treated a null receipt application source as a wildcard over exact
   receipt cohorts. A legitimate mixed shared/receipt room application threw
   `ArgumentOutOfRangeException`, including during destination admission diagnostics.
   Sources now match exact nullable receipt ownership, and all allocation groups
   are validated against the same pre-application state before applying changes.
   Invalid allocations remain diagnostics; incoming stock does not inherit them.
2. Backdated transfer arrivals crossing an already-recorded room application could
   reconstruct as untreated. They now retain known incoming treatment evidence and
   name the unresolved ledger/movement/application occupancy events.
3. Unallocated withdrawal collapsed different arrival times to their minimum.
   A later backdated application could incorrectly treat later arrivals. Shared
   cohorts now retain earliest/latest arrival bounds; overlapping application time
   stays explicitly unresolved rather than fabricating membership.
4. The preexisting typed ReceiptCorrection writes scalar before/after quantities.
   The new origin guard assumed every revision snapshot was an object and rejected
   later movement. It now accepts the committed scalar format with exact original
   quantity and unchanged or audited corrected identity. No correction reason is
   used as allocation proof; original snapshots remain intact.

Six regression cases reproduce these risks, including subsequent receipt identity
correction. They were observed failing before the corresponding fixes and passing
afterward (the identity continuation was added while fixing the scalar format).

## Destination boundary and reviewed paths

Reviewed destination admission/preparation, origin/custody recursion, projection
factory namespaces, source selection/allocation, exact source/destination deltas,
recorded supersession, receiving, transfer completion/return/reopen, receipt
quantity/location/identity correction, consumption/restoration, and the CohortKey
migration. The remaining destination `IsOperable` calls are diagnostics or optional
safe untreated normalization; they are not a generic admission requirement.
Missing/stale/negative projection quantities remain reconciliation evidence.
The replay exception above was a real remaining indirect veto and is fixed.

Actual negative ledger, unfunded origin, invalid custody, route/seal restrictions,
insufficient source quantity, stale command, and unsupported operation-specific
treatment/provenance still reject. No readiness or treatment guard was weakened.
The static scan is supported by runtime cases, not claimed as universal proof of
every historical data shape. Bounded evidence limits and specifically unresolved
recorded events remain explicit failures rather than invented inventory.

## ATGL report versus RED reproduction

Render queries explicitly used workspace `tea-d7uc4ippo60c73ebn4mg` and only read
logs. Window: 2026-10-06 00:00 through 2026-10-09 16:00 UTC. Exact error-text search
returned no entries. Transfer POST logs show `/BinsRun/Transfer` at
2026-10-06 17:34:10.702698142, 2026-10-07 18:15:15.045438450 and
2026-10-09 14:34:41.051953597 UTC, all HTTP 302. No POST to the room-55-specific
transfer endpoint was returned. The controller redirects on both success and
validation rejection; these statuses do not prove a committed move or identify
the reported failure. SQL logging near the latest request does not expose bound
identity/quantity values. Do not assign that request to ATGL by proximity alone.

Logs independently report RED/1242 room 73 at 652 bins requiring reconciliation.
A 2026-10-08 23:05:03 UTC receiving notification log references ATGL/1242/MCD-02;
it does not establish ATGL custody in Room 3. The prior read-only transaction
evidence contains 34 RED bins in room 55 and a distinct 63 ATGL bins in room 73.
The RED reproduction remains separate. Missing evidence is the failed submission's
exact time/request trace plus selected SourceLotKey/fruit profile, source/destination
IDs and quantity. No physical count or external paperwork is requested.

## Separate production reconstruction proposal

The [210-bin RED proposal](pr278-red1242-reconstruction-proposal.md) is independent
of the code release. Deployment and migration execute no data reconstruction.
The rollback rehearsal leaves that historical gap unchanged.

## Compatible rollback design

The fallback retains cohort-aware readers/writers, destination admission, origin,
treatment, delta, concurrency and exclusive-custody checks. It disables selection
of automatic recorded-cohort reconstruction. Already persisted, independently
proven cohorts remain readable and writable; unresolved older stock stays unresolved.
This is a constrained compatible fallback, **not** arbitrary pre-cohort main and
not a promise to revert every PR #278 behavior. If the fault affects the retained
compatibility layer, use maintenance and a reviewed compatible forward fix.

Freeze the fallback as an explicit commit in this PR's ancestry, publish the full
Web application locally, and rehearse that source against a disposable #190 clone
after the normal candidate writes new cohort rows. Retain additive schema; never
execute Down as rollback. Hash the packaged artifact and loaded inventory assemblies.
Final frozen commits, artifact hashes, commands and results follow after rehearsal.

No production write, migration, backup, deployment, merge, or PR #275 action is
authorized or executed by this review. This rehearsal is not the fresh backup gate
for a future authorized release. Browser/HTTP, onsite and unrelated feature
certification are outside this application-engine compatibility proof.

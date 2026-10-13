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

Normal reviewed code was frozen at `aad81a093619c8346f0781be0989d3c0776a8770`.
The compatible fallback is **`1667de8da158fa6fcc5aeeb3b65ab4e58e532fe3`**, reachable
in this PR's ancestry. The final PR restores normal reconstruction selection;
its application source matches the normal reviewed candidate. The intermediate
fallback commits are deliberate recovery artifacts, not the PR's final behavior.

The full framework-dependent Web publish was packaged locally as
`CropQc-Web-rollback-1667de8.zip` (16,152,468 bytes), SHA-256
`322FF276068DA9DD19AEB9EA1233DAAF883636529F0C5D897538AA97DC4BCCB5`.
The [manifest](pr278-rollback-manifest.json) records the exact inventory assembly
hashes. Both published assemblies matched the binaries loaded by the rehearsal.
This is a local publish and PostgreSQL application-engine proof, not a built/tested
Render Linux container or an authenticated browser/HTTP smoke certification.
Future release preparation must verify its actual target-runtime packaging and
affected HTTP health/routes without changing this source or relaxing readiness.

Reproduction is in `scripts/rehearsal/InventoryMovementRollback`. It only accepts
the explicit disposable localhost database `pr278_rollback_test`, port 55440.
Clone verified backup #190 to that database; do not connect the harness to a remote
server. It follows the documented feature-only migration range because the restore
has the preexisting migration-history mismatch. Normal and fallback modes were run
from their respective frozen source commits with `dotnet run --project
scripts/rehearsal/InventoryMovementRollback -c Release --no-restore -- candidate|rollback
<output.json>`, using `MOVEMENT_ROLLBACK_TEST_POSTGRES` for that local connection.

The normal candidate applies treatment 30 to the existing receipt 2472's 34 RED
bins, then records a distinct 10-bin local test receipt 2541. It moves 10 untreated
and 12 treated bins into room 73. The fallback moves 5 untreated and 7 treated bins
back to room 55. The migration is retained throughout; no Down command runs.

| Final test allocation | Room 55 | Room 73 | Total |
|---|---:|---:|---:|
| Original receipt 2472, treatment 30 | 29 | 5 | 34 |
| New test receipt 2541, untreated | 5 | 5 | 10 |
| Original room-73 pool | 0 | 652 | 652 |
| **RED/1242 combined** | **34** | **662** | **696** |

The test's explicit receipt accounts for the only global increase:
**73,107 + 10 = 73,117**. Rollback movement preserves **73,117** exactly.
The new 10 never inherit treatment 30; application 29 still covers its original
70. Separate ATGL remains 63, and room 73's historical gap remains **210**.
The old shared 56 bins are never relabelled as exact receipt ancestry.

Each executable compared 15 immutable/history table prefixes before/after its
commands. A separate read-only SQL comparison against the untouched backup passed
20 checks, including original receipt/ledger/movement/transfer/application/source/
audit prefixes, original application links, unrelated projection columns, complete
correction/run/loss/custody tables, and the global total plus the explicit receipt.
Retry returned Replayed with an identical whole-database hash; a new-key stale
submission returned Stale with an identical whole-database hash.

The fallback passed 17 focused admission/origin/treatment/correction tests and 12
architecture checks. Its code-identical preliminary freeze passed those 17 cases;
the final freeze adds only the reviewed resolver fingerprint and was independently
published and rehearsed on a fresh clone. The automatic source-reconstruction test
is intentionally outside fallback behavior, not reported as passing there.

No destructive schema rollback is needed or approved. Existing migration Up and
indexes are unchanged from the initial PR. The legacy pre-cohort application is
still not a supported post-use rollback. A compatible fallback may limit operations
requiring reconstruction of old unresolved stock; new proven cohorts remain usable.

No production write, migration, backup, deployment, merge, or PR #275 action is
authorized or executed by this review. This rehearsal is not the fresh backup gate
for a future authorized release. Browser/HTTP, onsite and unrelated feature
certification are outside this application-engine compatibility proof.

## Final normal-candidate validation

- Solution and rehearsal project restore passed. Release solution build passed,
  zero errors; five existing package advisories remain in the incremental build.
- Affected shared-engine workflows: **126 passed, zero failed/skipped**, actual
  PostgreSQL 18, including restored-data concurrency. The historical 68+104 fixture
  requires another snapshot and is explicitly excluded, not claimed as passed.
- Mandatory governance/architecture contracts: **89 passed, zero failed/skipped**;
  the runner verified every required member and structural suite executed.
- Governance metadata (20 rules) and **22 Node tests** passed.
- EF reports no pending model changes. Changed C# and rehearsal project formatting
  verification and `git diff --check` passed. No unrelated full suite or MSI build.

Relative to reviewed starting head `4907099`, application fixes change only
`InventoryEventReplay.cs`, `InventoryEvidenceLoader.cs` and `InventoryOriginGuard.cs`.
Additional files are `InventoryMovementReviewTests.cs`, the two-file local rehearsal
project, traceability and reviewed writer hashes, this report, rollback manifest,
separate RED proposal and the existing investigation's cross-reference/results.
The final normal resolver and all other application source match `aad81a0`.
Final commit and GitHub checks are recorded in PR #278 and the completion report.

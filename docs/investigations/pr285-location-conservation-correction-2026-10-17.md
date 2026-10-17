# PR #285 receipt-location conservation correction

## Scope and authority

This is the owner's additional release-blocker correction on existing PR #285,
branch `codex/inventory-transfer-antiforgery`, starting at reviewed head
`540798cdb5446675c89cd9aa1bf9eed49e25ebee`. Base/main remains
`df977d041dae2d449cf6fe5f8581e1f5cae41947`; no integration update was required.
Owner request: **PR #285 — Correct Receipt-Location Conservation Classification**,
2026-10-17, attachment `c4d178dd-8187-48ab-bdfd-ce221e073331` in task
`01a117ce-8381-7602-b396-3c44cc35402e`. The changed implementation requires focused
human review. **No merge, deployment, production mutation or fresh backup is
authorized in this correction task.** The previous release instruction does not
approve this new head automatically. PR #284 remains untouched.

Specification 1.2.0, governance commit `c17bd4deb477b57aa5ac1584175512d49318263b`,
ADR-001–008 remain unchanged. Applicable INV-001–006, TRT-001/002, REC-001/002,
MOV-001, ROOM-001, AUD-001, OPS-001 and GOV-001 protect origin, exclusive custody,
quantity conservation, exact receipt ownership, point-in-time treatment, immutable
audit and fail-closed release gates. This is an implementation repair, not a rule
change. Report-only synchronization found no incoming main commits and preserved
the existing untracked release documents and the PR's local CI/report proposal.

## Root cause and confirmed evidence

The original nine-endpoint security fix did not change inventory writers. Its
production preflight nevertheless exposed a pre-existing classification mismatch:
`InventoryCommandReceiptLocation.cs` writes `CorrectOriginalRoomOut` and
`CorrectOriginalRoomIn`, but the conservation reader and standalone SQL accepted
neither. The reader's category calculation already assigns the linked
`LocationCorrection` parent to **Identity/location corrections**. Balanced quantity
therefore did not make the unknown-category gate pass.

The preceding read-only investigation found receipt 2707, lot 9532, with ledger
4507 (-4, room 23 / EVANS-BKT) and 4508 (+4, room 8 / LAMB-15), both warehouse 1.
Both reference correction `ce318ad6-4509-4b4b-9016-8beae96d63d2`, completed
LocationCorrection, expected/actual two ledger rows, inventory delta zero. Effective
time is `2026-10-16T22:18:06.705515Z`; recorded creation is
`2026-10-16T22:18:06.713851Z`. The owner supplied the additional verified evidence
of unchanged receipt quantity 4→4 and one audit record. No new production query or
repair was performed in this correction task.

At the stopped preflight, receiving reconciled 2,649 receipts / 110,660 bins;
global room inventory reconciled 78,583 bins. The report failed solely on the two
unclassified types, with exit 2. This evidence does not substitute for a fresh
execution of the corrected candidate on the then-current production state.

## Changed reader and diagnostic contract

The conservation report recognizes the two location types only with an independent
operation check. `InvalidLocationCorrections` reports parent IDs and exact ledger
IDs; an orphan reports its ledger ID and a null parent. Any issue keeps `IsReady`
false. The corresponding SQL exposes `invalid_location_correction_ids`,
`orphan_location_adjustment_ids` and `location_corrections_ready`.

Checks cover:

- Completed LocationCorrection parent, expected two rows, unchanged receipt count,
  zero inventory delta and equal before/after current quantity.
- Exactly one negative Out and one equal positive In, distinct rooms, same receipt
  and inventory identity, correct warehouse/room membership and balance arithmetic.
- Canonical invariant version, operation-key legs, actor, reason and recorded time;
  effective timestamps agree with each other, separately from recorded chronology.
- Original before/after receipt snapshots and selected source/destination snapshot,
  including relocated quantity. The selected source comes from the correction
  evidence, not an assumption that original receipt room remains current custody.
- Exactly one matching canonical correction audit with the correct actor/time,
  before/after evidence, selected locations and quantity.

The checks use complete raw operations before opening-baseline filtering, never
current derived projection quantity. Missing both legs and audit cannot hide a
canonical completed two-row parent. Existing legacy `ReceiptAdminOverride` pairs
retain their established deduction validation. Treatment applications, segment
membership and movement relationships remain unchanged and subject to existing
strict deduction/treatment readiness; this focused pair checker does not replace
those gates.

Unknown movement types still populate `UnclassifiedTypes`. Invalid recognized
location events are reported separately, not suppressed or downgraded to warnings.
No receipt-ID exception exists. The public type-only `IsKnownMovement` predicate
also serves the historical-only receipt edit proof; its conservative behavior is
intentionally unchanged. Only the report combines location-type recognition with
the new complete pair validation, avoiding an unrelated expansion of edit authority.

SQL uses guarded JSON parsing so malformed evidence fails validation without a
cast exception. Its receiving scope now explicitly excludes transfer-evidence
receipts, matching the existing C# scope; they cannot become an additional origin.
Existing SQL quantity-category outputs remain intact.

## Capability and regression review

No physical writer, controller, Razor form, permission, authentication, treatment
application, EF model or migration changed. The original nine validators and eight
form tokens are byte-unchanged from `540798c`. The read-only service now matches
the conservative write-candidate scanner because it calls `Add` on local ID/result
collections. Manual capability review confirmed only `AsNoTracking` reads and local
collection construction, no SaveChanges/SQL mutation/canonical writer capability.
Its new registry entry is **Nonphysical or read-only**; no existing writer hash or
classification was weakened.

The new PostgreSQL fixture calls the real existing writer with entirely synthetic
data. The pre-fix run failed at `IsReady` with the exact two unclassified types,
while 19 receiving/ledger/current bins reconciled. Positive proof retains the
original receipt quantity/date, initial ledger, treatment applications/membership,
prior audit rows and idempotent replay, with -19/+19, source 0/destination 19,
unchanged receipt ownership and zero global difference. C# and SQL execute against
the same database, and all-table fingerprints prove the diagnostics make no writes.

Negative cases cover missing counterpart, missing pair plus audit, unequal debit/
credit, wrong/incomplete parent, conflicting receipt, duplicate rows/directions,
orphan parent link, missing audit, wrong source, malformed JSON and unsupported type.
Parent/orphan IDs must agree between C# and SQL. Bad evidence cannot pass merely by
balancing globally. Test-only direct PostgreSQL fixture seeding introduces malformed
records after the real writer commits; application fences correctly reject such
corruption and remain enabled. No protected backup or production data was restored.

The CI workflow now executes these regressions and existing location workflows in
addition to the unchanged mandatory contracts and security HTTP selections. No
mandatory cases were removed. Final validation results and exact final head are
recorded in the PR and completion report; local artifact paths are not dependencies
for reproducing the tests.

Local validation on PostgreSQL **18.4**, disposable localhost-only container:

- Pre-fix real-writer regression: failed as expected on the two unknown types;
  no unrelated failure was used as reproduction evidence.
- Affected workflow/security/conservation selection: **40 passed, zero skipped**.
- Final SQL/C# parity selection after fixing the audit-to-missing-parent SQL join:
  **15 passed, zero skipped** (14 rechecked cases plus one added missing-parent
  case; these overlap the 40-case run, not 55 distinct tests).
- Mandatory governance/architecture contracts: **161 passed, zero skipped**.
- Governance tests: **29 passed**; metadata check: **20 rules passed**.
- Solution restore and Release build passed, 0 errors / 64 existing warnings;
  existing ImageSharp advisories remain outside this scope.
- EF model consistency passed: no pending model changes. Full changed-file
  formatting verification and `git diff --check` passed.
- No full unrelated application suite or WinForms/MSI work was needed.
- All fixture databases were deleted by their test cleanup. The task-owned
  PostgreSQL container and anonymous volume were then removed and absence checked.

The final code review correction was limited to SQL selecting the audit's original
parent ID even when that parent is missing; it now agrees with C# and reports the
same invalid-parent/orphan IDs. No application reader/writer change followed the
successful mandatory-contract run. Final-head Linux CI remains a separate required
result recorded on the PR, not inferred from these local results.

The first Linux CI execution also exposed a pre-existing host-timezone assumption
in the existing depleted-source/location-correction workflow test. It submitted
`DateTime.Now` to a form whose contract is Pacific wall time. On the UTC runner
that became a future effective timestamp and the unchanged canonical guard rejected
the command. The fixture now uses `PacificBusinessTimeService.NowPacific`, matching
the actual form. No command validation or application time behavior changed; the
same workflow assertions and all CI selections remain required on the final head.

## Release preparation supersession and remaining gates

The prior frozen `540798c` package is not the changed candidate. After human review,
freeze/rebuild the exact approved new head with the established Linux runtime
process and verify its actual DLL identities. No schema migration is expected.
The previous security-aware containment limitation remains: an old unpatched
binary is not a security-equivalent rollback; preserve the reviewed bounded
containment/forward-recovery procedure and verify operator access.

Resume at the failed boundary under applicable read-only release authorization.
Run the corrected candidate's `dotnet CropQc.Web.dll --verify-inventory-conservation`
as a verification-only process using the established production connection with
`Options=-c default_transaction_read_only=on` and the existing read-only transaction
guard. Do not start the candidate web server, migrate, seed, normalize or repair.
Require exit 0, `isReady=true`, no unclassified types, no invalid location corrections,
zero receiving/facility/global discrepancies, balanced required operations and
unchanged protected data. Cross-check the reviewed standalone SQL in a read-only
transaction, including its new correction outputs; do not mistake one passing
subfield for overall readiness. A failure stops the release at its exact evidence.

Only after that gate and the other mandatory prerequisites pass may the separately
authorized lifecycle resume: exact approved merge, fresh independently verified
predeployment backup, current fingerprints/readiness, exact-SHA deployment, scoped
authenticated no-write security verification and independent integrity checks.
Scheduled backup #206 is not that future release backup. No production readiness
PASS, release completion or #284 handoff is claimed here.

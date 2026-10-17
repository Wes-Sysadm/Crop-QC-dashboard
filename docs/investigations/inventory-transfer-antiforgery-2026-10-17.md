# Inventory transfer antiforgery correction

Date: 2026-10-17. Development and synthetic validation only; no production access,
protected backup restore, migration, inventory repair, merge or deployment.

Base: `df977d041dae2d449cf6fe5f8581e1f5cae41947` (`origin/main`).
Branch: `codex/inventory-transfer-antiforgery`. The draft PR's head identifies
the exact candidate; this report does not grant release authority.

## Root cause and bounded correction

`BinsRunController` used cookie authentication and authorization policies on nine
transfer/custody POST actions without MVC antiforgery validation. `Program.cs`
registers MVC without a global automatic antiforgery filter. Authentication,
permissions, SameSite cookies and canonical inventory validation do not replace
request-token validation. An authenticated tokenless dispatch could reach ordinary
business validation. No production exploit or inventory mutation is asserted.

The nine actions now have `ValidateAntiForgeryToken`. Their existing permissions,
service calls, command validators and transaction logic are unchanged. The five
Razor views explicitly emit tokens in all eight existing affected POST forms.
No global filter, authentication change, new mutation path or exception is added.

| POST path beneath `/BinsRun` | Rendered token source |
| --- | --- |
| `/Transfer` | Index room-transfer form |
| `/Transfer/{id}/Reverse` | Same authenticated room-transfer page/session; no dedicated reversal form currently exists |
| `/OutsideTransfers` | Outside transfer partial |
| `/OutsideTransfers/{id}/Reverse` | Outside transfer detail form |
| `/InterCrewTransfers` | Inter-crew transfer partial |
| `/InterCrewTransfers/{id}/Receive` | Inter-crew detail receive form |
| `/InterCrewTransfers/{id}/Review` | Inter-crew detail review form |
| `/InterCrewTransfers/{id}/Reverse` | Inter-crew detail reverse form |
| `/TrueUp` | Index true-up form |

## Compliance and authority

Canonical specification **1.2.0**, governance commit
`c17bd4deb477b57aa5ac1584175512d49318263b`; fetched knowledge synchronization passed.
INV-001–006, TRT-001/002, REC-001/002, MOV-001, ROOM-001, AUD-001,
OPS-001 and GOV-001 apply. ADR-001–008 and the change-scoped/release standards remain
effective. Authoritative transactions establish origin, quantity and custody;
derived projections do not establish independent inventory. Point-in-time treatment
membership, unknown ancestry and immutable evidence retain their existing meaning.

The only executable application changes are HTTP filter attributes and form tokens.
The physical-writer capability registry was reviewed: no registered writer body,
classification, fence, normalization policy or hash needs changing. No schema,
model, migration, permission, foundational policy or WinForms change is introduced.
Synthetic positive tests use the real canonical writers; negative requests prove
that the boundary prevents their invocation. Strict readiness and release gates
are not modified.

## HTTP and data-integrity proof

`InventoryTransferAntiforgeryHttpTests` uses disposable PostgreSQL databases,
real Razor rendering, the MVC filter, actual cookie authentication and database
operator permissions. Synthetic authentication tickets are issued with the test
host's cookie protector; authentication/antiforgery handlers are not replaced.
Only background jobs/external delivery are disabled. A counting decorator delegates
every valid command to the unchanged real executor.

The nine-action matrix verifies missing and malformed tokens and a token from a
different cookie session all return HTTP 400. Complete operation-shaped payloads
are supplied. Every rejection preserves an all-table snapshot, including audits,
commands, custody, ledger, receipts and treatments, and does not invoke a command.
Anonymous users still redirect to Login; an active operator without the required
permission redirects to AccessDenied. Matching tokens reach normal business
validation. Stale/nonexistent selections still fail without writes. A historical
variance review cannot settle missing bins with a note; its existing error is
read from protected TempData, because the detail view does not display it.

Seven successful HTTP scenarios cover room transfer, outside transfer, dispatch,
supported legacy receipt and their room/outside/inter-crew administrative reversals.
New cross-company dispatch still requires Truck Receipt reconciliation. Each
scenario conserves 19 bins across room, transit and outside custody. Original
receipt and existing ledger rows remain byte-equivalent in serialized snapshots.
Treatment applications stay unchanged; treated room/outside movement snapshots and
segment memberships retain the application. No alternate receipt ancestry appears.
Exact retries and changed-payload retries cannot create another mutation.

Fixture timestamps preserve treatment-before-movement order, including subsecond
precision, using explicit Pacific business time rather than the host's timezone.
This changes test evidence only. Existing custody and Truck Receipt HTTP tests and
canonical inter-crew/outside/Truck Receipt workflow tests cover the related writers.

## Remaining endpoints and overlap

The bounded controller audit found 18 additional POST routes without equivalent
antiforgery validation. These are deferred planner/packing/legacy Bins Run domains,
not silently secured by a global filter in this PR. All paths below start at
`/BinsRun`; each requires separate compatibility review and scope authorization:

- `/Projections`
- `/Projections/{id}/Header`
- `/Projections/{id}/Sources`
- `/Projections/{id}/Sources/{sourceId}`
- `/Projections/{id}/Packout/ApplyAll`
- `/Projections/{id}/Sources/{sourceId}/Refresh`
- `/Projections/{id}/Sources/{sourceId}/Remove`
- `/Projections/{id}/Ready`
- `/Projections/{id}/Cancel`
- `/Projections/{id}/Duplicate`
- `/Projections/{id}/CreateInventory`
- `/Projection`
- `/Create`
- `/ActualRuns`
- `/ActualRuns/{id}/Cancel`
- `/ActualRunOverrides/{id}/Approve`
- `/{id}/Edit`
- `/{id}/Reverse`

Open PR #250 (`2485dc1d187101741b143bd09ed07df448dd9c51`) proposes broader
browser-write protection, a global filter and station-heartbeat separation from an
older base. This focused owner-requested PR starts directly from current main,
does not stack on or modify #250, and does not adopt its broader proposal. Future
integration must reconcile the overlapping protection rather than assume either
proposal has been adopted. This report is not an application-wide CSRF clearance.

## Validation and release limits

The existing governance CI job now runs the focused security, receipt-custody and
Truck Receipt HTTP suites on its synthetic PostgreSQL service and retains TRX.
Mandatory contract selection and existing checks remain intact. This is technical
test enforcement, not a business-policy amendment or owner approval mechanism.
The task authorizes this development/test change, not release.

Local validation: 19 focused HTTP/custody/workflow tests passed, zero skipped;
161 mandatory contracts passed, zero skipped; 29 governance tests and the 20-rule
metadata check passed. Solution restore and Release build, pending-model check,
changed-code formatting verification and `git diff --check` passed. Final-head CI
is recorded in the draft PR. The task-owned synthetic PostgreSQL container and
anonymous volume were deleted after all fixture databases were confirmed absent.
Restore
reports existing ImageSharp 3.1.12 vulnerability advisories; dependency remediation
is outside this filter/form correction. No unrelated full-suite recertification
or hardware validation is claimed.

PR #284 branch/head remains untouched. It remains NO-GO for release/rollback
rehearsal until this security correction is separately reviewed and adopted under
owner release authority, integrated into #284, and its affected HTTP behavior and
rollback/handback candidate are verified. An old unpatched application cannot be
called a security-passing rollback package. This development task establishes no
production runtime or rollback compatibility claim. Fresh verified backup,
strict readiness, migration preflight, compatible rollback, health and independent
post-deployment verification remain required at release.

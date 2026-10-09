# Crop QC Dashboard — Codex Repository Instructions

These instructions apply to all Codex work in this repository. Explicit task instructions may override ordinary workflow preferences (for example, continuing an existing PR), but an ordinary request is not approval to change foundational inventory, custody, treatment, conservation or audit rules. Any apparent conflict must be reported before dependent implementation. A genuine policy change requires explicit documented approval from the business owner under the change procedure, including the previous/new rule, affected workflows, approval reference/date, versioned decision and tests. Ordinary development authorization does not authorize release or repair. Explicit project-owner production release authorization covers the merge-and-release lifecycle under the [single-owner policy](docs/governance/RELEASE_AUTHORIZATION.md); it never waives production safeguards.

## Start every task with current repository knowledge

The [canonical business rules](docs/governance/CROP_QC_BUSINESS_RULES.md) and
[architectural decisions](docs/governance/ARCHITECTURAL_DECISIONS.md) are authoritative.
Business rules define the system. Authoritative transactions establish physical
truth. Code implements those rules. Tests verify the implementation. Historical
projections never redefine authoritative inventory.

Before designing or editing, identify the Git root, branch, HEAD and current
`origin/main`. Verify the remote is `Wes-Sysadm/Crop-QC-dashboard`, fetch current
main explicitly, and compare local instructions using
`./scripts/Sync-CropQcKnowledge.ps1` (report-only by default). Follow the
[Windows setup and synchronization guide](docs/governance/WINDOWS_SETUP.md).
Report stale, locally changed, divergent or unavailable remote instructions;
do not silently proceed under outdated policy. Read the fetched canonical rules
and relevant decisions if the feature branch is behind; integrate safely before
dependent implementation. Never overwrite work to refresh instructions.

For every task, including read-only reviews, provide the [rule compliance assessment](docs/governance/README.md#rule-compliance-assessment). State the applicable rule/decision IDs and affected invariants,
then inspect existing implementation and authoritative evidence. Read only the
directly applicable detailed specifications; unrelated historical reports need
not be loaded. Preserve approved behavior and the workflow below. Report any
contradiction between the request, code, tests and established rules. A technical
discovery does not supersede policy; a genuine new business-policy choice requires
explicit human approval under the [change procedure](docs/governance/CHANGE_PROCEDURE.md).
Routine bug fixes do not redefine foundational rules. GitHub's reviewed `main`
is the shared source; conversations, global files and feature branches are not
competing authorities. Include specification version and governance commit in
the opening assessment. Run the [governance checks](docs/governance/VALIDATION.md)
when governance or its contracts change, alongside change-scoped verification.

## Repository

- Repository: `Wes-Sysadm/Crop-QC-dashboard`
- Default branch: `main`
- This is production Receiving and QC software. Favor stability, focused changes, auditability, and production-data safety.

## Default Git and PR workflow

For each new development request:

1. Fetch the current GitHub state and start from the latest `origin/main`.
2. Create a new, clearly named `codex/` branch.
3. Keep the branch and pull request focused on the requested work.
4. Implement the requested behavior and avoid unrelated cleanup or redesign.
5. Run the required verification described below.
6. Commit the completed work with a clear commit message.
7. Push the branch to `origin`.
8. Open one new pull request into `main`.
9. Do not create stacked pull requests.
10. Do not add commits to an existing or merged pull request unless the user explicitly says to continue that pull request.
11. Do not merge, deploy, install on production systems, or modify production data unless the user explicitly authorizes it.

When the user explicitly says to continue an existing branch or pull request, update that work instead of creating a new branch or pull request.

Once ADR-007 is effective on approved main, one explicit project-owner instruction
to "go live", "deploy", or release the identified PR authorizes ready-for-review,
normal merge, required backup/migration/deployment, independent verification and
the established safe rollback response within that scope. Preserve exclusions.
Record the original instruction/reference, owner, date and reviewed head; the
agent carries that evidence into release records without requesting another
owner action in GitHub. Do not submit an approval as though a separate reviewer
provided it. Code review and all technical gates still apply; independent human
sign-off is optional unless explicitly required for that release or by effective
repository configuration. Never use an admin bypass to defeat such a requirement.
Merge-only authorization is not deployment authorization. Stop for revoked,
ambiguous or materially out-of-scope authority, not merely a different interface.
This proposed policy cannot authorize its own adoption; follow the transition in
[the approval record](docs/governance/RELEASE_AUTHORIZATION.md#transition-and-pr-278).

Before creating a new branch or pull request, check current GitHub state. Do not assume that a previously mentioned branch or pull request is still open, unmerged, or current.

## Required completion report

At completion, report:

- current `origin/main` commit
- branch name
- final commit
- base commit
- pull request number and link
- whether the branch required updating from newer `main`
- restore result
- build result
- focused test count and result
- any broader/full-suite test result only when it was justified and actually run
- migration/model consistency result
- formatting and `git diff --check` results
- GitHub Actions checks and status
- MSI version, filename, build process, and artifact location when WinForms code changes
- known limitations and required onsite verification
- confirmation that nothing was merged or deployed
- confirmation that production data was not modified

Do not claim a browser, database, hardware, installer, or deployment test was completed unless it was actually run.

## Production safety and protected behavior

### Mandatory verified production backup gate

Before beginning a task, determine whether it can affect production. A production-affecting action includes an application deployment, database migration or correction, import, reconciliation, configuration change, bulk email operation, destructive maintenance, or any action that could alter production records or availability. Ordinary local editing, builds, tests, and read-only investigation do not require a new backup.

For every production-affecting release or operational task, before the first production mutation or deployment:

1. Identify and record the deployed application commit, application environment, and database provider.
2. Run the standard full pre-deployment backup command: `dotnet CropQc.Web.dll --run-backup=predeployment` in the configured production runtime.
3. Confirm the command exits successfully and that the backup package actually exists in the restricted Google Drive backup destination.
4. Verify the uploaded package is readable and matches its recorded size and SHA-256 checksum.
5. Record the filename, Google Drive location, UTC timestamp, size, SHA-256, deployed commit, and backup-run ID.
6. Stop before changing production if dump creation, manifest creation, upload, read-back, checksum, or archive validation fails.
7. Never overwrite or prune the only current verified backup. Retention may run only after a newer backup has passed every verification step.

"Backup completed" means the backup exists at the durable destination and passed read-back, size, checksum, archive, manifest, and database-dump validation. Merely issuing a command, starting an export, or receiving an upload response is not completion.

Never place credentials, connection strings, OAuth tokens, service-account JSON, private keys, station keys, or other secrets in source control, logs, reports, manifests, or PR descriptions. Backup diagnostics must remain safe for administrators. Final production reports must include the backup artifact and rollback details.

During an emergency application rollback, preserve additive database schema unless destructive reversal is separately reviewed and explicitly authorized. Prefer reverting/redeploying the application while leaving compatible additive columns and tables in place.

Do not change the following unless the user explicitly requests it or the requested fix strictly requires a narrowly scoped change:

- low-level FTA communication protocols
- low-level scale communication protocols
- QC Station enrollment
- QC Station authentication
- QC Station installer behavior
- QC Station configuration behavior
- Google Drive photo-storage architecture
- Gmail sending
- current inventory rules
- Bins Run
- Receiving behavior
- Door Sample behavior
- Lot Sample behavior
- permissive partial fruit-row saves
- existing 10-, 25-, and 50-fruit sample support
- historical production data

Do not delete, reset, reseed, rewrite, or otherwise alter production data.

Do not include credentials, enrollment files, machine-specific configuration, local paths, generated build output, or secrets in commits.

Audit create, edit, delete, send, import, export, device-capture, photo, and configuration changes wherever the application’s existing audit patterns apply.

## Scope discipline

- Make only the changes needed for the requested application behavior.
- Do not drift into staging, OAuth, screenshots, infrastructure, deployment, or unrelated architecture unless the user specifically asks.
- Reuse established repository patterns before introducing a new subsystem.
- Do not invent a new branch, installer, packaging, versioning, storage, authentication, or migration process when an established one exists.
- Preserve backward compatibility unless the user explicitly approves a breaking change.
- Resolve merge conflicts only within the requested scope; do not use conflicts as an opportunity for unrelated cleanup.

## Mandatory change-scoped testing and historical-data integrity

All work must follow `docs/change-scoped-testing-standard.md`.

The default rule is:

> Test the blast radius of the change, prove the affected data is correct, and preserve the historical record. Do not recertify unrelated parts of Crop QC.

Before selecting tests, identify the changed area, directly affected records/workflows, shared dependencies actually modified, and historical evidence that must remain unchanged.

For operational-data changes, the primary gate is data correctness. Prove the before/after quantities, identities, relationships, current authoritative state, and absence of unintended writes. A successful request or HTTP 200 by itself is not sufficient.

Corrections must preserve historical evidence through established revisions, reversals, compensating transactions, dedicated correction records, or audited supersession whenever practical. Do not overwrite or delete history merely to make current state appear correct.

Do not widen validation into unrelated Photos, Field Samples, Receiving, Transfers, reports, email, Admin screens, memory benchmarks, browser route matrices, or other areas unless the implementation materially touches them or investigation proves a real dependency. If the blast radius expands, document the dependency that justified the additional tests.

A complete application test suite is not the default requirement for every change. Use focused affected-area tests during development. Run a broader/full suite only when a shared foundational dependency creates a genuinely broad risk or the user explicitly requests it; normally run it once near final review/merge rather than repeatedly.

## Normal verification

Run from the repository root:

```powershell
dotnet restore CropQc.sln
dotnet build CropQc.sln --no-restore
dotnet ef migrations has-pending-model-changes --project src/CropQc.Data/CropQc.Data.csproj --startup-project src/CropQc.Data/CropQc.Data.csproj --no-build
git diff --check
```

Also run the repository’s existing formatting verification process.

Run the focused tests identified by `docs/change-scoped-testing-standard.md` for the affected area. Do **not** automatically run the complete `CropQc.Api.Tests` suite unless the blast radius justifies it.

If broader/full-suite testing is warranted, report the specific shared dependency or risk that required it.

If a required tool or disposable dependency is missing, report the exact limitation. Do not substitute production services or production data for a disposable test environment.

## WinForms and MSI rules

Rebuild the WinForms MSI only when WinForms or QC Station code changes.

When an MSI is required:

- use the repository’s existing installer workflow
- follow the established installer versioning convention
- do not invent a new packaging or release process
- verify the artifact was actually produced
- report the MSI version, filename, build command or workflow, and artifact location
- do not install the MSI on a production workstation without explicit authorization

## Field Sample invariants

Field Samples are a separate preharvest workflow.

They:

- are receiptless
- do not affect inventory
- do not affect Bins Run
- do not affect room Dashboard cards
- do not enter Receiving email workflows
- have dedicated list, create, edit, and detail pages
- track orchard/grower and canonical block
- show 30-day same-block size, starch, weight, and pressure trends
- support partial saves
- support the existing QC fruit-count range, including 10-, 25-, and 50-fruit workflows
- support manual entry
- support browser scale capture
- support FTA pressure capture through QC Station
- use fuzzy block matches as suggestions only and require user confirmation
- may use existing photo storage and audit patterns without redesigning Google Drive storage

Keep the normal QC Station Receiving queue receipt-backed unless the user explicitly requests a queue change. Receiptless Field Samples may be exposed through a separate or explicitly distinguished QC Station workflow, but must not silently alter Receiving queue semantics.

## Data and synchronization safeguards

For browser, API, QC Station, and device-capture work:

- preserve partial samples
- preserve arbitrary supported fruit counts
- prevent stale browser forms from overwriting newer device readings
- prevent one reading from overwriting unrelated fruit or pressure positions
- record the originating station where existing auditing supports it
- keep retry and failure states from erasing entered data
- display actionable user-facing errors while logging technical detail without credentials

## Database changes

- Prefer reviewed EF migrations for persistent schema changes.
- Run the pending-model-change check after model changes.
- Do not add a migration when the existing schema already supports the requested behavior.
- Do not run migrations against production unless explicitly authorized.
- Do not use runtime schema repair as a substitute for a required reviewed migration unless that is already the established design and the user requested work within it.
- Validate schema changes according to their actual blast radius; a migration does not automatically require unrelated feature certification.

## Pull request expectations

Production releases must follow both `docs/change-scoped-testing-standard.md` and the mandatory release procedure in `docs/overnight-release-standard.md`.

Availability is the primary release gate. Test the exact frozen candidate against production-shaped data when the change requires it, but keep rehearsal and smoke testing scoped to the materially affected routes, records, invariants, and rollback risk. Synthetic mutations stay off production. Maintenance is only the final bounded execution window: a critical operational HTTP 500 in an affected workflow triggers rollback, and unresolved post-deploy troubleshooting stops after 15 minutes with rollback/feature disable and PR reopen. Prefer additive schema and application rollback; never overwrite newer legitimate production activity with an old backup merely to undo a software release.

Every pull request should explain:

- what problem was reported
- root cause
- what changed
- user-facing behavior before and after
- directly affected data/workflows
- historical evidence that must remain unchanged
- data-integrity/reconciliation proof
- safety or synchronization safeguards
- major files or components changed
- focused tests added or updated
- any broader tests and the specific reason they were required
- database or migration impact
- installer impact
- known limitations
- onsite or hardware verification still required

Keep the PR limited to the requested work. Merge only under explicit applicable owner authorization, including the merge authority carried by an effective ADR-007 release instruction.

## Review behavior

When asked to review Codex work or a pull request:

1. Read this file first.
2. Read `docs/change-scoped-testing-standard.md` for the mandatory testing/data-integrity scope.
3. Check current GitHub and branch state rather than relying on stale prompt history.
4. Compare the implementation against the user’s requested behavior and these repository constraints.
5. Look specifically for production-data risk, unintended scope expansion, historical-data loss, audit gaps, stale-write risk, partial-save regressions, unsupported fruit-count assumptions, installer omissions, and unverified claims.
6. Confirm the tests match the actual blast radius; do not reject focused validation merely because unrelated suites were not run.
7. Distinguish application defects from environment-specific issues that require onsite testing.
8. Do not manufacture human approval. Merge only with applicable explicit owner authority and passing effective safeguards; a code review request alone is not merge authority.

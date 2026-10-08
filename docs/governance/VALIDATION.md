# Governance and synchronization verification

Changed area: repository knowledge, synchronization tooling, review metadata and
contract selection. No application writer, schema, migration, station or production
configuration changes. Existing historical data must remain untouched. Tests use
synthetic local Git repositories and a separate disposable localhost PostgreSQL 18
cluster, not a production copy or service.

## Reproduce the checks

```powershell
node --test scripts/governance/check.test.mjs
node scripts/governance/check.mjs
node --test scripts/governance/sync.test.mjs
# Repeat Windows compatibility coverage in a new shell/process:
$env:CROPQC_TEST_POWERSHELL = 'powershell.exe'
node --test scripts/governance/sync.test.mjs
Remove-Item Env:CROPQC_TEST_POWERSHELL

dotnet restore CropQc.sln
dotnet build CropQc.sln --no-restore --configuration Release
# Set CANONICAL_INVENTORY_TEST_POSTGRES to your disposable localhost test database.
./scripts/governance/test-contracts.ps1 -NoBuild
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/CropQc.Data/CropQc.Data.csproj --startup-project src/CropQc.Data/CropQc.Data.csproj --no-build --configuration Release
dotnet format CropQc.sln --verify-no-changes --no-restore --include tests/CropQc.Api.Tests/BusinessRuleContractTests.cs tests/CropQc.Api.Tests/GovernanceArchitectureTests.cs
git diff --check
```

The traceability file maps rules to exact reflected .NET methods and states gaps.
The runner rejects missing/failed/skipped mandatory cases and requires the actual
local PostgreSQL provider. It runs the existing nine writer architecture checks
unchanged, plus selected conservation, receipt, treatment, replay and concurrency
contracts. Structural scans and reviewed hashes are not a proof of control flow.
Real behavior assertions provide the runtime evidence. No unrelated full suite is
needed for this governance-only change.

Node checks validate IDs, required documents, local links/headings, decision rule
references, test metadata and PR disclosures. Against a base catalog they reject
removed stable IDs and require version/decision updates for policy changes or
mandatory-test removals. Human review judges approval and business meaning; a
keyword or green check cannot establish them.

## Disposable two-computer test coverage

| Requested scenario | Executable evidence |
|---|---|
| Fresh clone receives current instructions | Actual AGENTS/catalog/decisions copied into fixture remote; computer B clones and compares contents. |
| Clean main fast-forwards | Report-only keeps HEAD; Update reaches the exact remote SHA. |
| New approved rule reaches another computer | Simulated approved fixture version 99.0.0 committed/pushed by A; B receives it. This does not change real policy. |
| Uncommitted work preserved | Staged file, untracked file and edited AGENTS remain byte-identical and index/status unchanged. |
| Divergence preserved | Local-only and remote-only commits retained; helper exits 2. |
| Feature branch unchanged | Incoming updates reported, branch/HEAD unchanged with Update; matching knowledge can return 0. |
| Conflicts stop safely | Real merge conflict created externally; index stages and markers remain unchanged. Helper never starts a non-fast-forward merge. |
| Repeated runs idempotent | Repeated current-main runs retain exact HEAD and clean status. |
| Session discovery | Fixture proves paths/contents only. Actual new-session smoke result is recorded below; every new computer must run the setup prompt. |
| No credentials/production modifications | Child Git config isolated; ignored credentials/local settings, clone/global config and unrelated sibling bytes unchanged; ignored incoming-path collision refused; all remotes local. No production connection or production data used. |

Additional cases reject wrong/credential-bearing remotes without exposing them,
fail closed on fetch errors, support single-branch fetch configuration, stop at
detached HEAD, and suppress post-merge hooks during the authorized fast-forward.
The harness uses an explicit marked bare `OfflineTestRemote`; this is not a
substitute for verifying GitHub freshness in normal operation.

## Results and limits

Finalization execution on Windows, 2026-10-08 (supersedes earlier local run counts):

| Check | Result |
|---|---|
| Solution restore | PASS with approved NuGet network access. |
| Release solution build | PASS, 0 errors, 69 existing nullable/analyzer/SDK warnings. |
| Governance metadata and rejection tests | PASS, 20 catalog rules / 27 indexed requirements, 22 Node cases, including missing/nonlocal/unmarked PostgreSQL rejection, monotonic versions and architecture-suite retention. |
| Two-computer Git tests | PASS on PowerShell 7 and Windows PowerShell 5.1: 17 scenarios plus one parent test in each run, zero skips. |
| Inventory and architecture contracts | PASS in one final run: 74 cases, zero skipped/failed, actual PostgreSQL 18.6 on the task-owned localhost-only disposable cluster. Cluster stopped after the run. |
| EF model consistency | PASS, no pending model changes using the repository Data startup/default design-time provider; not a PostgreSQL migration rehearsal. |
| Changed C# formatting / diff whitespace | PASS; formatter reports existing workspace-loading warnings. |
| Real GitHub report-only synchronization | PASS for expected pre-adoption behavior: fetched current main, reported local proposals, exited 2 because main has not adopted governance yet; branch/work preserved. |
| Live new Codex session discovery | UNVERIFIED. The network-isolated attempt could not connect; automatic approval review rejected the networked retry because it could transmit repository instructions/source to the configured OpenAI API. File/path checks passed. No external API retry was made during finalization. Follow the manual actual-install discovery record in Windows setup; explicit authorization is required before any automated external retry. |

The 74 cases comprise 5 BusinessRuleContract, 15 CanonicalHistoricalWorkflow,
9 CanonicalInventoryArchitecture, 2 CanonicalReceiptCorrectionWorkflow,
28 CanonicalReceiptOverrideReadiness, 2 CanonicalReceivingWorkflow,
1 CanonicalTruckReceiptWorkflow, 2 GovernanceArchitecture, 5 InventoryAvailability,
2 InventoryCommandConcurrency and 3 InventoryCommand cases. Local TRX:
`artifacts/governance/2a75468313c74fe0a4528179262c34fa/contracts.trx` (ignored artifact).
CI publishes its own TRX for the final head; historical local paths are not required
to exist in another clone.

The prepared post-merge probe patch applies cleanly. Its disclosure validates, the exact intentional broken link rejects, and the repaired link passes in a disposable local fixture. No probe PR was opened: actual server-side blocking remains unverified until authorized post-merge protection activation. See [activation](POST_MERGE_ACTIVATION.md).

The requested #271/#272/#273 heads were reconfirmed unchanged and draft/open.
No full application suite, MSI build, production backup or production operation
was needed or performed. GitHub Actions results belong in the PR and completion
report; local success does not imply required-branch enforcement.

Static discovery cannot certify all installed Codex versions or a second physical
computer. The scripted two-clone test runs on one Windows host; onsite setup and
instruction-discovery verification remain required on each additional computer.
No production readiness, current inventory, migration rehearsal, hardware, MSI,
Google Drive, credential rotation or deployment is certified by these tests.

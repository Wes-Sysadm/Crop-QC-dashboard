# Engineering constitution — assessment and verification

Base: `e58d73a12c8bf0cc5f0f2333e442497e2b91b807` (current main, reconfirmed before push). Branch: `codex/engineering-constitution`. Dedicated governance draft; no application runtime change.

## Rule compliance assessment

- Applicable rule IDs: GOV-001, OPS-001; all 20 catalog domains indexed for traceability.
- Implementation paths and authoritative records versus projections: AGENTS, governance catalog/matrix/decisions, PR template/CODEOWNERS, Actions, focused contract infrastructure. Existing ledger/custody/command contracts are documented, not changed.
- Proposed behavior and potential conflicts: enforce catalog/reference/PR disclosure integrity and selected architecture/PostgreSQL contracts. Replace obsolete receipt-only room-authority prose. Current-main treatment-addition and partial-acknowledgement gaps remain explicit; no global certification claimed.
- Authoritative inventory changes: No; test fixtures create only isolated disposable local PostgreSQL databases.
- Treatment lineage changes: No runtime changes. Existing point-in-time tests are mapped/executed; the current-main positive-correction gap remains for #271.
- Historical records changed/preserved: No operational history changed; preservation and replay assertions execute only in disposable tests.
- New origins or writers: None. Existing writer registries, capabilities and structural guards unchanged and executed.
- Exact tests/provider/results proving compliance: Node metadata rejection tests 11/11; selected .NET contracts 73/73 with zero skips, including real local PostgreSQL 18.6 workflow/concurrency tests. Details below.
- Unverified rules, skipped bodies and unresolved assumptions: Per-rule gaps in traceability.json. No production-shaped restore, live readiness, migration rehearsal, device/hardware, external storage, email or full-application recertification. Outstanding-PR results are attributed reports, not reruns.
- Production-data implications and authorization: None; no production requests, schema/data/config changes, backups, repairs, merges or deployments. #271–#273 are reviewed read-only.
- Business-policy approval required/reference: Owner's engineering-constitution request, 2026-10-07, supplies the foundational policy. This PR and proposed repository protections still need human review/approval; no settings changed.

## Governance change

Previous: Substantial phase specifications, scoped testing, release rules and writer guards existed, but there was no single stable business-rule catalog or required GitHub governance workflow.
New: One versioned 20-rule catalog, six decisions, exact code/test traceability with explicit gaps, mandatory opening assessment, protected-change disclosure and selected PostgreSQL contracts.
Impact: Every task starts with applicable rules and existing architecture. Bounded mandatory architecture checks supplement, not replace, change-scoped tests and strict release gates. No runtime implementation behavior changes.
Tests/history: 11 Node checks and 73 .NET contract cases pass locally; build, EF and affected-file formatting pass. No migration/history rewrite. Unknown/missing/skipped required tests fail the runner.
Approval: Owner requested this governance change; independent review and protected-branch settings are proposed, not claimed enabled. Prose approval is not authenticated by the checks.

## Local verification

| Check | Result |
| --- | --- |
| `dotnet restore CropQc.sln` | PASS |
| `dotnet build CropQc.sln --no-restore --configuration Release` | PASS, 0 errors; 64 nullable/analyzer warnings |
| `node --test scripts/governance/check.test.mjs` | PASS, 11/11 |
| `node scripts/governance/check.mjs` | PASS, 20 unique rules and references |
| `scripts/governance/test-contracts.ps1 -NoBuild` | PASS, 73/73, failed 0, skipped 0; PostgreSQL 18.6 on new localhost-only cluster |
| EF pending-model check, Data project/startup, Release/no-build | PASS, no pending model changes; default design-time provider, not a PostgreSQL migration certification |
| `dotnet format` verify, the two added C# files | PASS |
| `git diff --check` | PASS |

Contract execution combines nine unchanged writer architecture checks, two reflected mapping checks, five new business/provider cases and selected existing workflow/readiness/concurrency cases (including theory rows). Structural checks do not count as runtime proof. Real-provider tests create fresh UUID-named test databases and clean them up. No other local PostgreSQL instance is used or stopped.

No full application suite is required for this governance/test-only change. No WinForms change or MSI rebuild. No schema migration added. CI execution and required-check context names will be recorded after opening the draft PR.

## Limits and follow-up

[Outstanding review](OUTSTANDING_PRS.md) pins #271–#273 heads and integration requirements. In particular, partial 50/49 acknowledgement is pending #271; the passing current-main partial-return test is **not** substituted as proof of that behavior. Main's positive correction treatment inheritance remains a documented TRT-001 gap. The six production lineage blockers remain untouched and unresolved by this PR.

[Repository settings](REPOSITORY_SETTINGS.md) records the unprotected main/no-ruleset state and proposes required checks plus independent code-owner review. CI cannot make itself undeletable or authenticate human approval. No configuration changes are authorized by this PR.

# PR #284 production release preparation — checkpoint

Status: **NO-GO for production release authorization yet: the production-shaped candidate/performance/rollback rehearsal is unproven.** No failed live readiness check was found. The next dependent step is a new protected-data restore, for which scoped owner approval was requested and remains pending at this checkpoint. Nothing was merged, deployed, migrated, corrected or reconstructed; no new production backup was started.

## Authority, reviewed source and compliance

Owner Wes's “PR #284 — Production Release Preparation” instruction on 2026-10-16 records completed focused human review and preparation-only authority. It expressly excludes merge, deployment and production inventory changes, and requires fresh scoped approval for a new protected restore. This is not release authorization under ADR-007.

- PR: [#284](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/284), existing branch `codex/system-wide-transfer-eligibility`.
- Reviewed executable source: `377cde9812242d9dec239a9fdb49c0791558cfe5`.
- Fetched main/base: `df977d041dae2d449cf6fe5f8581e1f5cae41947`; no integration update required.
- Specification 1.2.0; latest main governance commit `c17bd4deb477b57aa5ac1584175512d49318263b`; ADR-001–008 retain their effective scope. Knowledge synchronization was report-only; nine pre-existing untracked release reports were preserved.
- GitHub observed OPEN/DRAFT/MERGEABLE at the reviewed head. Both final-head workflow runs report successful governance, contracts, Windows pwsh and Windows PowerShell jobs: [38873564842](https://github.com/Wes-Sysadm/Crop-QC-dashboard/actions/runs/38873564842), [38873564762](https://github.com/Wes-Sysadm/Crop-QC-dashboard/actions/runs/38873564762).
- Current main protection API returns “Branch not protected” (404); applicable branch rules API returns `[]`. The required CI workflows exist and pass, but server-side branch enforcement is not configured. No repository settings were changed and this is not permission to bypass project safeguards.

INV-001–006, TRT-001–002, REC-001–002, MOV-001, ROOM-001, AUD-001, OPS-001 and GOV-001 apply: authoritative origins and committed custody govern quantity; shared ancestry is not fabricated into exact receipts; treatment follows only the affected fruit; transfers conserve quantity; retries/concurrency retain exclusive custody; immutable history and compensation remain intact. Preparation makes no writer or policy changes. Existing source/contract evidence is in the [investigation](system-wide-transfer-eligibility-2026-10-15.md) and [capability review](transfer-eligibility-capability-review-2026-10-15.md).

The reviewed implementation's 275 distinct affected cases, 188 mandatory contracts, 29 governance tests, 20-rule metadata validation, Windows/Linux builds, model consistency and formatting evidence are preserved. They were not rerun merely to increase counts. A new exact Linux publish was justified by the artifact/runtime freeze. No application source, migration, model or startup script changed during this preparation.

## Actual production observation

Read-only observation on 2026-10-16, approximately 13:42–13:53 UTC. Every Render API request used workspace `tea-d7uc4ippo60c73ebn4mg` explicitly.

| Item | Evidence |
|---|---|
| Web | `srv-d8crvimgvqtc73b9rogg`, live `df977d041dae2d449cf6fe5f8581e1f5cae41947`, deployment `dep-db8qpk3l550s73escjmg` |
| Deployment control | Git-backed main/Dockerfile, auto-deploy `no` / trigger `off`, maintenance false |
| Actual shell | Production instance `qxkr6`, Linux x86_64; .NET and ASP.NET 9.0.21 |
| Relevant flags | `CanonicalInventoryCommandsEnabled=true`, `TransferCustody__Enabled=true`, `TruckReceiptReconciliation__Enabled=true`, PostgreSql provider |
| Startup safety | EnsureCreated and master-data seeding false; readiness commands explicitly reinforced both settings |
| Database | `dpg-d8cru6reo5us73a2bgt0-a`, PostgreSQL 18.4, Debian x86_64, 34 applied migration records |
| Latest applied | `20261009150448_IsolatedInventoryMovementCohorts`; all three receipt custody/compensation/guardrail migrations present |
| Schema | Full cumulative deployment contract: expected 1,007 objects, PASS; `GrowerLots`, `CohortKey`, acknowledgments, placements and reversals present |
| Strict readiness | `--verify-release-readiness`, exit 0, success true, inventory ready; 1,139 negative adjustments classified as 93 historical and 1,046 new-format |
| Treatment readiness | 452 current identities, zero blocking issues; explicit lineage does not exceed authoritative inventory |
| Custody/topology | Ready; zero invalid inter-crew/outside operations, zero invalid outside master rows/duplicate codes, no missing warehouse codes |
| Conservation | `--verify-inventory-conservation`, exit 0, ready true |
| Receiving | 2,627 receipts; 109,559 receipt bins = 109,559 ledger bins; zero mismatched receipts |
| Current room accounting | 77,642 authoritative bins = 77,642 accounted bins; zero difference |
| Other custody | 844 inter-crew transit, 1,409 outside custody, zero receipt-held bins; these are separate custody categories, not additional room inventory |
| Treatment identity | Debit 6,088 = credit 6,088; zero invalid movements; no unbalanced room transfers, identity corrections or unclassified ledger types |

The shell checks ran with `PGOPTIONS='-c default_transaction_read_only=on'`, after confirming no connection-string Options override. Startup writes were disabled. The conservation command additionally opens its existing repeatable-read/read-only transaction. Output was observed directly in the authenticated Render Shell; the readiness command was repeated only with compact output to recover counts truncated by the terminal accessibility view. A passing live readiness check does not certify every source origin or replace candidate rehearsal.

Actual production DLL SHA-256 values match the retained canonical-LF #282 artifact:

| Assembly | SHA-256 |
|---|---|
| CropQc.Web.dll | `009d4125c8405e565e90b3ad7ea7742294e1b8f7452933e3860eac8b9e99d5d2` |
| CropQc.Data.dll | `594a1bcb0d9263ad75097f817cbd3320530bf6b0b7f7a78499ceef743fc5153e` |
| CropQc.Shared.dll | `627cd2ced0250c4d2a9cd513f04b03afe8752f81f0fef881fde8f63a9edb98e4` |

## Backup worker and protected baseline

The backup worker remains independently deployed at `95518dec131d44b01e2dc8fcfcbb82f508a8f399`, deployment `dep-db3ath142hec73955je0`. It is not assumed to contain web changes. Its auto-deploy is also disabled; command remains the scheduled backup CLI, schedule `0 8,9 * * *`.

Today's backup #205 is recorded Succeeded, verified at 09:11:31.898064 UTC, completed/lease released at 09:11:32.242589 UTC, with retention processed. Worker logs independently show completion of `cropqc-production-daily-20261016-080127.zip`, 20,174,419 bytes, SHA-256 `0bfdb227152b7031c80b9733888f4c62cf099b05d20db4c9201ef4515ce8fb9b`. Nightly guard reports Succeeded. The lease row has null expiry; no active lease was observed. This confirms recorded scheduled-backup operation, not a new independent archive download in this task. The older worker's migration-status log lists 24 pending historical IDs; no automatic migration was run. Full web schema compatibility passes the actual object contract.

Previously independently downloaded/verified backup #204 remains available locally: `cropqc-production-predeployment-20261016-033344.zip`, 20,177,849 bytes, package SHA-256 `7030fed357e9166ac0da8747cad2c77800950d0d7a2bd71e3a82390646f3b4c2`, dump SHA-256 `c62e035f97b2b330921272342a9d23bae4803e56d681025df4adca0040ba997a`. Its retained component/read-back evidence is reused, not claimed as newly executed verification. Neither #204 nor #205 replaces the future release's fresh independently verified predeployment backup.

The existing [fingerprint query](transfer-eligibility-fingerprints.sql) ran in the connector's read-only transaction at 13:49:35.600569 UTC. It captured 33 relevant groups, including 2,733 receipt rows, 4,427 ledger rows, 413 room transfers, 42 inter-crew transfers, 47 outside transfers, 330 inventory commands, 71 identity corrections, 26 treatment applications/37 sources, 1,164 lineage movements, 1,045 segments/57 memberships, 188,848 audit rows and 34 migration records. Receipt custody acknowledgment/placement/reversal tables were empty. These current counts include their own historical/soft-deleted scopes and are not interchangeable with active-receipt or current-bin counts. Full hashes are retained in the preparation evidence. Normal business activity between snapshots is allowed; no global “unchanged forever” claim is made.

## Empty-database migration classification

The prior synthetic full-chain run failed at SQL line 4296 while creating `InventoryIdentityCorrections`: its foreign keys reference the absent `GrowerLots` table. This is migration `20260902140938_AddInventoryIdentityCorrections`, not a new #284 migration. The complete migrations directory, Dockerfile and startup script are byte-identical between reviewed main and the candidate. The [retained verifier contract](../truck-receipt-verifier-contract.md) already distinguishes a production schema-only bootstrap plus bounded migration from proof of the entire historical EF chain.

Classification: **pre-existing empty-database/bootstrap defect**, not evidence of a missing production table. Production has `GrowerLots` and passes the 1,007-object contract. No old migration was modified or marked applied. Nevertheless, candidate compatibility with the restored production schema remains a separate unproven gate until the scoped restore/rehearsal executes; the live baseline PASS does not substitute for it.

## Frozen reviewed candidate

The exact reviewed commit was exported using `git -c core.autocrlf=false archive`, avoiding the previously observed Windows archive CRLF divergence. A local Dockerfile copy differs only by pinning the existing SDK/runtime FROM images. Repository Dockerfile and source were not edited. Publish completed successfully under Linux amd64; execution without networking confirmed .NET/ASP.NET 9.0.21 and assembly hashes. No database was attached to that runtime inspection.

| Artifact | Immutable identity |
|---|---|
| Source LF archive | SHA-256 `3d93bcfbd1bac425b41dccf773fc22967b5a2596a4ff5dc1475d964d9b112bbf` |
| SDK input | `sha256:2f5eb32870b4c0074a3d5b5191a03553bcab7b74463a76a7c0a5f3ef65c90fe5` |
| Runtime input | `sha256:7da398155e0d6439cf0058bc1afa937817a03e320714b876a5f58a7d64f79d50` |
| Image index | `sha256:16b9694e62556925b968200f21b56ba4a28ef4d674ea4c43a5b80712e17aa43b` |
| Linux manifest | `sha256:9b7e55e666795e43aa6ecd914d714b90d256d33d8eeb14593eed959d46de0310` |
| Image config | `sha256:8583127a5a072cd734cfb08c90eb95ff3bfbc7dac1799d5098e318ec672baa8a` |
| Saved local image | `candidate-linux.tar`, 150,414,336 bytes, SHA-256 `9dcdc232dea2cee9e0811df5da91ad04be26a5b29956670f0312eb8f181b61c8` |
| CropQc.Web.dll | `31bc8514f756c6b1ef443ef6f336863c5309ea85d49efdb240d21aaaf718ff8d` |
| CropQc.Data.dll | `2be30f50a3b894585b22b07847508b73018b9f4f29f8def15e91a8b359c5eff5` |
| CropQc.Shared.dll | `e45767523a88d1b4a52d381a66afe20fcd51eb8d8ccae8e768b0be9133eeab21` |

The full assembly manifest, source/build inputs, build log, image inspection and runtime output are retained under the local `pr284-preparation` evidence directory. The image is local, not published or deployed. Frozen bytes are established; restored-schema compatibility and release acceptance are not yet established. Any later merge/build must reproduce these application binaries or explain and validate a material input difference before deployment.

## Rollback candidate and pending rehearsal

Preferred rollback remains the actual #282 production source `df977d041dae2d449cf6fe5f8581e1f5cae41947`, retained Render deployment `dep-db8qpk3l550s73escjmg`. Its archive checksum was reconfirmed: `78a4576fa9b014bde5bc4eade343a7e9196463a30f78b0fbb71ab7009f714f55`. Image index is `sha256:598c26de196ebc3ecfb160fa07534de7d2400ba57c2376e74457b10aa2c028ec`; image config is `sha256:eeb0858063ed6886a6d04f581d5c0cfeabd391f313a44ea3dba6daac21ddfa00`. These are different OCI identity levels, not conflicting binaries. Its three application DLLs match the live shell hashes above.

This identifies an exact retained deployment and package, **not proof that the prior reader/writer safely handles new #284 transactions**. The pending one-database rehearsal must cover new internal dispatch/receive/return records, historical closure, cross-company receipts, compensation-aware writes, strict readiness and conservation under rollback, then candidate handback without restoring/resetting the database. If incompatible, record the precise reader/writer failure and prepare only the authorized narrow compatibility backport. Never use destructive Down migrations or restore an old backup over newer business activity. Render activation usability must be established without activating production rollback.

Requested isolated scope: restore already-local verified #204 into one disposable local Docker PostgreSQL database with network disabled, no published ports, no production credentials/connection; delete it after rehearsal. No restore/container was created while that approval remained pending.

After approval, use the same restored database for:

1. Baseline full schema/readiness/conservation and protected row snapshots.
2. Equivalent baseline/candidate selector measurements for inter-crew, outside, room transfer, processor and direct single-room selection. Record query counts, rows read, elapsed time and database/resource observations; no invented latency threshold. Stop on a demonstrated operational regression.
3. Authenticated affected selectors and bounded synthetic operations, preserving every original record: source-origin blocks, missing/excess projection limits, recovered historical custody, internal dispatch/receive/return, actual cross-company matching, permission/seal blocks, retry and concurrency behavior, unknown ancestry and treatment membership.
4. Forward writes, exact rollback activation, continued safe reads/writes/readiness, then handback, with no restore between phases. Independently compare all original relevant rows plus exact authorized new deltas.
5. Delete the disposable database/container/owned volume and record cleanup. Preserve only approved non-secret evidence and frozen packages.

## Separate origin exclusions

The 56 source-origin failures and their existing event IDs/evidence remain unchanged and outside this PR: (1) Canonical Origin Evidence Closure and Legacy Nullable Identity, (2) Recorded Identity Corrections in Source Custody Proof, (3) specific missing/contradictory authoritative events. See the existing [origin evidence](transfer-eligibility-origin-gaps-2026-10-15.json). No origin guard, readiness rule or historical projection was weakened/repaired. Eligibility recovery is not universal certification for all physical inventory operations.

## Prepared future release runbook — not executed

Only proceed after the rehearsal/performance/rollback gates above pass and owner release authorization identifies the final reviewed PR scope/head.

1. Refresh main/head, CI and effective repository settings; bind any documentation-only head to the frozen reviewed executable diff. Stop on unexpected source/integration changes.
2. Reconfirm current production deployment/runtime, disabled auto-deploy, exact candidate and compatible activatable rollback target. Stop if rollback is unavailable or incompatible.
3. In the normal configured production runtime, run the standard fresh `dotnet CropQc.Web.dll --run-backup=predeployment`. Independently retrieve/verify upload, size/checksum, ZIP, manifest, nonempty dump and components; confirm retention and lease release. Stop on any failure. Previous #204/#205 are insufficient.
4. Run fresh read-only full schema/release readiness and inventory conservation with startup schema/seed writes disabled. Capture relevant protected fingerprints and quantities. Resolve normal business activity explicitly; unexplained differences stop release.
5. Merge #284 normally only under applicable owner release authority. Record merge/main SHA and confirm the reviewed application diff and frozen assembly identities. No new migration is expected; do not invoke the broken empty-database bootstrap or destructive Down.
6. Deploy only the exact approved merged application through the established Git-backed Render process, leaving web/worker auto-deploy off. Verify requested/built/live SHA and actual runtime DLL hashes. Do not deploy unrelated worker changes.
7. Check health/database health and authenticated affected selection routes. Verify internal/cross-company receipt behavior through recorded evidence and the isolated rehearsal; never perform synthetic production inventory mutations.
8. Independently verify exact current custody, quantity/treatment conservation, audit/history integrity and strict readiness. Check affected route logs. Keep the compatible rollback target immediately usable.
9. A critical affected HTTP 500 triggers rollback. Stop unresolved troubleshooting at 15 minutes and activate only the proven compatible application, retaining schema and newer business activity. No ad-hoc repair, provenance manufacture or safeguard bypass.
10. Publish exact final deploy/backup/rollback identities, quantities, checks and remaining origin follow-ups. Complete only after independent production verification.

Current decision remains **NO-GO / preparation incomplete**, solely because required isolated candidate/performance/rollback evidence has not yet been executed. This checkpoint does not identify a new application defect or authorize a production release.

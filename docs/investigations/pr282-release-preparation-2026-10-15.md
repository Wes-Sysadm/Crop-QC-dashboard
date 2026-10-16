# PR #282 — reviewed release preparation

Owner technical review covers `8aa4231a675d746f055a3200b7a284fa3957d5c0`.
This continuation is preparation only. No merge, deployment, new backup,
migration, correction, reconstruction, permission or production configuration
change is authorized or performed. Observations are October 15 Pacific / October
16 2026 UTC. This report does not declare PR #282 complete.

## Authority, scope and repository state

Repository: `Wes-Sysadm/Crop-QC-dashboard`, existing PR #282, branch
`codex/receipt-correction-form-contract`. Fetched main/base and live source remain
`ea307b168e563d432dd4f513da6c06f4e5e54d5e`; no new integration is required.
Reviewed PR head is open, non-draft and mergeable. Its four checks passed in
Actions run `38836165740`. The preparation commit and final CI are recorded in
the PR; the executable freeze remains unchanged.

Effective main specification is 1.1.0, governance evidence commit
`38733d2633af9f1155dab83a1c468a6914ce20f8`. Proposed specification 1.2.0 /
ADR-008 remain pending normal adoption. Read AGENTS, canonical rules, applicable
ADRs, receipt-edit authorization, change procedure, change-scoped testing and
overnight release standard. Report-only synchronization fetched current main;
exit 2 requests manual handling of expected proposal differences and existing
untracked historical reports. Those files were preserved, not staged or overwritten.

Applicable invariants: REC-001/002 (authorized receipt corrections and partial
custody), INV-001–006 (recorded origin, exact deltas, unique custody, authoritative
inventory), TRT-001/002 (point-in-time treatment and evidence), MOV-001 and ROOM-001
(movement/capacity/seals), AUD-001 (immutable history), OPS-001 (backup/readiness/
rollback) and GOV-001 (approved policy). ADR-007 controls owner authorization;
ADR-008 is the reviewed proposed permission/workflow change. No invariant is
redefined here. The existing canonical commands and their evidence guards remain
the only inventory writers. Projections cannot establish available inventory.

Only factual REC-002 implementation status changed: PR #271 is merged/deployed,
not a draft implementation gap. GitHub reports merge
`3002d0774f52ee5e94aaef81dbd0d62c6bed335b` on 2026-10-14; that commit is an
ancestor of main, and Render retains successful deployment
`dep-db7raprncjis73fap6ig`. The normative REC-002 paragraph is unchanged. The
authorization record now accurately records completed human review and its
preparation-only scope. Other additions are this report and aggregate preflight
evidence. There is no implementation, schema, permission, writer, test-selection
or migration delta after the reviewed head.

GitHub protection readback: classic protection returns **404 Branch not protected**;
effective branch rules are `[]`. Therefore `governance`, `contracts`,
`windows-sync (pwsh)` and `windows-sync (powershell)` are policy-mandated checks,
not currently server-enforced required contexts. No settings were changed and no
bypass was used. The separately documented protection activation remains pending
explicit settings authorization; see [repository settings](../governance/REPOSITORY_SETTINGS.md).
Do not claim that green CI proves server enforcement. Existing policy separately
permits owner-authorized normal release with all effective technical gates; this
report neither activates protection nor waives a future effective rule.

## Frozen executable and retained evidence

Frozen source: `b049f89b641877b6165c31fc7b2e5f3c3e6e0386`.
Candidate OCI: `sha256:44c5d842ec7d47866b6808d0d5f73b591dc185908b05723db66cd7bc7752a9b0`.
Candidate archive `candidate-linux.tar`, SHA-256
`b94f7bb4886d6dc7f8623b8bf8b27981e1d9656c001ba068cbabc7dbbe675e2f`.
Runtime: Linux amd64, .NET/ASP.NET 9.0.21. Comparison of source, Dockerfile and
startup script against the reviewed head shows no executable change.

Preferred rollback is current main/live `ea307b168e563d432dd4f513da6c06f4e5e54d5e`.
Retained rehearsal OCI: `sha256:f65d1b54b331c4bc11d902c62cef81de80a4a17c051e7af8d3b36547d4131318`;
archive `pr283-reviewed-80df2bf-linux.tar`, SHA-256
`671127a939c44f5bca0f99dbfb95d81274b401f138ab0884aac806044effbbb7`.
This is compensation-aware and supports all existing 34 migrations. No migration
is required by #282; all three custody migrations and additive CohortKey remain.

Reuse the [completed rehearsal](pr282-integration-2026-10-15.md) and
[immutable evidence](pr282-restored-rehearsal-2026-10-15.json): candidate edits,
rollback reads/writes after committed edits, and candidate handback on the same
restored database passed. No intervening restore hid incompatibility. Exact
seven-bin ownership, unknown nineteen-bin ancestry, point-in-time treatment,
partial custody/reversals, retry/concurrency and writer fencing remained intact.
117 bins reconciled as 97 room plus 20 transit. Independent comparison preserved
268,444 original rows across 113 tables. All disposable databases/volumes were
deleted. The ten-bin projection-excess exclusion is covered by the integrated
HTTP regression, not claimed as a restored fixture. No repeat rehearsal is needed:
actual live runtime and all 55 DLL identities match the rehearsed rollback.

Completed restore/build/model/format checks, 161 mandatory contracts, 15 rendered
HTTP tests, configured restored concurrency, 183 affected tests, 22 date theory
rows and 29 governance cases remain valid; counts overlap and are not additive.
For this documentation-only continuation run governance validation/rejection tests,
PR metadata and whitespace checks. Final-head CI runs its normal four jobs; no
unrelated broad local application test or MSI build is justified.

## Read-only production preflight

Every Render API request used workspace `tea-d7uc4ippo60c73ebn4mg` explicitly.
Web `srv-d8crvimgvqtc73b9rogg`, database `dpg-d8cru6reo5us73a2bgt0-a`, backup cron
`crn-d9gp1gm1a83c73f6pvk0` were read only. No production credentials were copied.
Actual authenticated Shell commands ran in the live application container.

| Evidence | Observed result |
|---|---|
| Web | `dep-db8krumi0phs73bst740`, live `ea307b168e563d432dd4f513da6c06f4e5e54d5e`; successful 2026-10-15 22:06:55 UTC |
| Backup worker | `dep-db3ath142hec73955je0`, live `95518dec131d44b01e2dc8fcfcbb82f508a8f399`; backup-only command, schedule `0 8,9 * * *` |
| Deployment controls | Git-backed Docker main, web and cron auto-deploy off; web maintenance off; no configuration changed |
| Actual runtime | Linux x86_64; .NET and ASP.NET 9.0.21; all 55 DLLs match retained rollback manifest |
| Safe startup flags | canonical commands and transfer custody enabled; EnsureCreated and SeedMasterData disabled; Production environment |
| Schema | 34 applied migrations; all 1,007 expected schema objects ready; custody and CohortKey coexist |
| Strict readiness | Exit 0 at 02:57:15 UTC; zero inventory blockers; 452 current treatment identities, zero treatment/projection blockers; custody/topology PASS, zero invalid inter-crew/outside records |
| Receiving conservation | 2,627 crop-year receipts; receipt total and ledger total both 109,559, difference 0, zero mismatch receipts |
| Current custody | 77,642 room, 844 inter-crew transit, 1,409 outside warehouse, 0 receipt-held; each facility difference 0 |
| Treatment conservation | identity debits/credits both 6,088; difference 0; zero invalid treatment identity movements |
| Other conservation | no unclassified types, unbalanced room transfers or unbalanced identity corrections; exit 0 at 02:58:50 UTC |
| Health / authentication | `/health` and `/health/db` HTTP 200; unauthenticated Edit redirects 302 to Login; authenticated Admin Edit GET renders current UI; no fields changed or Save submitted |

Readiness used both PostgreSQL `default_transaction_read_only=on` connection
options and `PGOPTIONS`, plus explicit startup seed/EnsureCreated false:

```sh
env PGOPTIONS="-c default_transaction_read_only=on" \
  ConnectionStrings__CropQc="${ConnectionStrings__CropQc};Options=-c default_transaction_read_only=on" \
  Database__EnsureCreatedOnStartup=false Database__SeedMasterDataOnStartup=false \
  Logging__LogLevel__Default=Warning dotnet CropQc.Web.dll --verify-release-readiness
# Same safeguards for: dotnet CropQc.Web.dll --verify-inventory-conservation
```

The command uses the container's existing connection variable without printing it.
Read-only SQL independently captured migrations/backup/lease state and 21
protected table fingerprints. [Aggregate machine-readable evidence](pr282-release-preflight-2026-10-15.json)
contains timestamps, exits, output SHA-256 values, safe runtime configuration,
conservation results and count/max-ID/row-digest baselines. Receipt row detail is
explicitly omitted from the conservation summary. These are live operational
snapshots, not a frozen database transaction across all probes. Refresh before
release; reconcile subsequent legitimate activity instead of requiring unchanged
global hashes or treating differences as proof of damage.

Backup #203 remains Succeeded/unpruned, verified and retention-complete, lease
released 2026-10-15 21:53:33 UTC; no running backup or active lease. Filename
`cropqc-production-predeployment-20261015-204229.zip`, 20,018,989 bytes, SHA-256
`2114f24f6aac160600e24ce11b8cb509937a6bf6bc6778adfc066cab708b0792`.
Prior independent upload/archive/manifest/dump verification and successful restore
are retained. This task reread run/lease metadata; it did not redownload the archive
or start a backup. Latest scheduled #202 also succeeded with verification,
retention and lease release complete. No claim is made about a future nightly run.
**#203 is rehearsal/recovery evidence, never a substitute for a fresh release backup.**

## Native rollback and permission containment

Authenticated Render deployment history and API confirm preferred deployment
`dep-db8krumi0phs73bst740` remains live and retained. Older retained successful
deployments expose enabled native Rollback controls. The live entry does not show
rollback to itself. [Render's native rollback](https://render.com/docs/rollbacks)
reactivates a retained successful build artifact without rebuilding. Artifact
retention is bounded: after the future #282 deployment, recheck this exact target
is still available before calling the release verified. Do not substitute a
different older deployment or merely rebuild the same source.

The previously completed Render-native activation work establishes the mechanism;
this task did not trigger it again. Operator target is the exact retained deploy
above on the production web service (native Dashboard rollback, or established
`POST /v1/services/{serviceId}/rollback` with that `deployId`). Auto-deploy remains
off; the API does not disable it automatically. Validate reactivated SHA, all DLL
hashes, .NET runtime, schema, health and strict readiness before recovery closeout.
The backup cron is not an inventory writer and is not redeployed by this task.

**Compatibility limitation:** rollback restores old editing controls and receiver
Create permission for some saved-receipt edits. It does not preserve #282's new
Admin-only/reason/one-Save policy. Schema/custody safety is proven; permission
equivalence is not claimed. No established receipt-only write-disable switch was
found; the truck-receipt reconciliation toggle is not such a switch.

Prepared containment, to be authorized within the subsequent release runbook:

1. Before an emergency application rollback, enable the existing Render maintenance
   control as a bounded outage and verify traffic is being held. Do not start the
   older permissive UI until the hold is confirmed. If containment fails, STOP;
   do not expose the old UI as though Admin policy remained enforced.
2. Hold ordinary web traffic through rollback. Verify the maintenance response on
   affected receipt paths, including a harmless token-free empty POST which must
   be intercepted at the edge; do not submit a valid correction. Check whether
   the rollback preserves maintenance and reverify it immediately. If its
   persistence cannot be established before activation, use a separately reviewed
   reliable access-containment plan instead; do not rely on operator notices alone.
3. Keep receipt edits unavailable while the older app is active. Resume service
   only after the approved #282 policy is restored or a separately authorized,
   tested permission-containment change provides equivalent protection. This may
   require downtime; rollback is not represented as normal unrestricted operation.
4. Preserve all additive schema and newer legitimate business activity. Do not run
   Down migrations, restore an old database, change roles ad hoc or disable fences.

No maintenance toggle, role/permission change or trial production POST was made
during preparation. Actual containment verification is a mandatory release-time
operational gate if rollback is needed, not an already-executed rehearsal claim.

## Subsequent authorized release sequence and status

1. Obtain applicable owner release authorization; record this technical review and
   final evidence-only head. Refresh main/head/CI/effective rules and frozen
   artifact availability. Preserve all exclusions; no historical corrections.
2. Refresh live identities and protected baselines. Run the standard fresh full
   predeployment backup immediately before release. Independently verify durable
   upload, size/SHA-256, archive, manifest, database dump, retention and lease release.
   Stop on any failed or unverifiable safeguard.
3. Reconfirm strict readiness, conservation, unchanged 34-migration compatibility,
   exact retained native rollback and the permission-containment runbook. No #282
   migration is required. A changed dependency invalidates only its affected proof.
4. Merge normally with all four checks successful and applicable owner authority;
   record exact merge/main SHA. Prove executable content equals the frozen candidate.
   Deploy the authorized exact application through the established Git-backed
   process with auto-deploy off. Compare actual loaded DLLs and runtime to the
   frozen manifest; a source label alone is insufficient.
5. Verify health, authenticated affected routes, live Receipts Admin permission,
   required reason and one Save. Use read-only route/identity evidence and safe
   rejected-request checks where appropriate; no synthetic inventory correction in
   production. Existing rendered HTTP/rehearsal evidence supplies successful write
   proof. Do not create role changes or identities solely for a production test.
6. Independently reconcile origin/custody/receipt/treatment/audit history against
   refreshed protected baselines, accounting for legitimate concurrent activity.
   Strict readiness and conservation must pass. Confirm immediate native rollback
   availability and final controls. Follow the release standard: critical affected
   HTTP 500 triggers rollback; unresolved post-deploy troubleshooting stops at
   15 minutes with established safe rollback/containment and PR reopen.

**GO for owner production-release authorization once this evidence-only head's
four final checks pass.** No application/runtime/readiness defect is outstanding.
Actual release remains gated on fresh independently verified backup, refreshed
preflight, exact merged/deployed identity, rollback availability/containment and
independent live verification. Repository protection activation is separately
unimplemented and must not be represented as enforced. No merge or production
change was performed. PR #282 is not complete until its later authorized merge,
deployment and independent production verification succeed.

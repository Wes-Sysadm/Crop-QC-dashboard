# Legacy backup batch recovery

## Problem and scope

After the bounded backup worker rollout, the nightly admission check correctly refuses legacy Running rows because they never participated in its session advisory lock. On October 6 and 7 the 01:00 Pacific invocation exited 1; the 02:00 candidate intentionally skipped. The single-run administrator recovery additionally rejected every other Running row, so #35 blocked #177 and #177 blocked #35.

This change only affects the explicit backup-recovery CLI, BackupRunRecords, BackupOperationLeases, associated BackupNightlyRunGuards and BackupAbandoned audits. It does not alter ordinary backup generation, photo verification, retention, inventory commands, selectors, schema or production feature flags. Program's reviewed dispatch fingerprint changes solely for the new backup command.

## Independently reviewed production evidence, October 7

| Attempt | Originating application evidence | Termination evidence |
|---|---|---|
| 35 | Started 2026-08-01T22:44:47.388930Z, command:predeployment, application 2b840ef23b505300a7c0be0ca2b011f5355a0e98 | Matching web deployment dep-d9mmp45aeets73aat9s0 is deactivated; Render updated its deactivated state 2026-08-05T04:33:55.472852Z. |
| 177 | Started 2026-10-01T22:06:13.539320Z, command:predeployment, application 71ff57dd7fe7009b8cc56199a46bd4ead43859c2 | Matching web deployment dep-darf75gjo6nc73fnsnm0 is deactivated; Render updated its deactivated state 2026-10-06T01:15:56.201678Z. |

Both origin application images are superseded. Neither legacy row persisted its PID/container identity, so exact original process IDs cannot be reconstructed; deployment identity is correlated from recorded application SHA and start time. No current backup/pg_dump PostgreSQL session or advisory owner exists. Lease row 1 has null LeaseId and ExpiresAt. Render's displayed one-off job history contains 68 terminal jobs and no nonterminal job; no October one-off job corresponds to #177. The current cron image is 07db7c, not either old application. Age, lease expiry and loss of an interactive observation session are not used as termination proof.

Repeat live checks immediately before preflight/apply. Capture prior verified backup fingerprints. Review exact output before invoking --apply for only #35/#177. Then use the current published image for --run-backup=manual and require the entire normal verified backup contract. Report any retention effects separately; batch recovery itself cannot prune packages.

## Regression evidence

PostgreSQL coverage includes single and two-target recovery, exact-start/status/missing-target rejection, active lock and modern/unreviewed worker rejection, evidence/actor/confirmation/unique-set guards, no-op preflight, one audit per target, retry refusal, nightly guard closure and subsequent worker admission, original successful backup preservation, and rollback of both rows when audit persistence fails. Existing modern orphan recovery remains covered. Existing snapshot, package, dump/restore and Pacific/DST tests are retained. No full application recertification is required for this backup-only change.

## Release and rollback

No schema migration. Keep auto-deploy off and the existing web pre-deploy verifier. Deploy the exact reviewed merge to web and cron; keep canonical mode ON. An application rollback does not reverse the reviewed Abandoned evidence or restore an old database over business writes. Never deploy pre-canonical code. Production recovery and manual backup results are recorded separately from these design instructions.

Validation on the frozen implementation: restore/build passed; 21 focused backup/scheduling tests plus 47 adjacent architecture/readiness/business-time tests passed (66 unique tests, 68 successful invocations, zero failures or skips). EF reports no pending model changes; touched-file whitespace verification and git diff --check pass. The full application suite was not rerun because no shared inventory or application behavior changed. PostgreSQL cases use disposable local databases and reproduce the exact two-legacy-Running shape; no production mutations were used for testing.

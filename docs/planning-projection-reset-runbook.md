# Planning Projection reset runbook

This is a separately authorized operational reset. Merging or deploying the
domain-model change does not authorize or execute it.

## Scope

The reset archives active legacy Planning Projections because they were created
under the former projection-to-actual architecture. It retains every projection
row and dependent source snapshot for audit, but removes archived projections
from the normal active planner.

It does not change or delete Actual Runs, Bins Run entries, room inventory
adjustments, receipts, QC samples, Grower Lots, fruit profiles, or packout files.

## Approval gates

1. Stop and obtain explicit production-reset authorization.
2. Capture a current production PostgreSQL backup using the established backup
   workflow.
3. Verify that the backup is readable and complete a disposable restore check.
4. Record the five protected-table counts from the preflight.
5. Run `scripts/postgresql/preflight-planning-projection-reset.sql` in read-only
   mode and review every active projection ID, Actual Run link, and legacy
   packout link.
6. Save the preflight output with the release evidence.
7. Apply `scripts/postgresql/apply-planning-projection-reset.sql`.
8. Run `scripts/postgresql/verify-planning-projection-reset.sql`.
9. Confirm active projection count is zero and protected-table counts match the
   preflight exactly.
10. Open Planning Projections and confirm the new active list is empty.

## Idempotency

The apply script acts only on rows where `IsDeleted` is false. A second approved
execution inserts no duplicate reset audit rows and changes no projection.

## Rollback

Do not bulk-unarchive projections. If verification fails, stop application
writes and restore the verified pre-reset database backup through the established
restore process. Re-run the protected-table count comparison before reopening
the application.

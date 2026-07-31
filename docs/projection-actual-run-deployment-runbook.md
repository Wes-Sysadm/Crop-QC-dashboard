# Projection and Actual Run separation deployment runbook

This runbook covers the additive schema deployment only. It does not authorize
the separate Planning Projection reset or any EBS inventory cleanup.

## Before merge

1. Complete normal restore, build, test, formatting, diff, and pending-model
   verification.
2. Apply the full migration chain to an empty disposable PostgreSQL database.
3. Restore the production-like checkpoint immediately before
   `20260731014107_SeparatePlanningProjectionsFromActualRuns` into a different
   disposable database and apply only the reviewed new migration.
4. Review the generated PostgreSQL and SQL Server scripts.
5. Confirm the Render memory investigation is resolved and the bounded upload
   safeguards remain in latest `main`.

## Production gate

1. Capture a fresh verified production backup through the established workflow.
2. Prove the backup is readable and complete a disposable restore verification.
3. Run `scripts/postgresql/preflight-projection-actual-run-separation.sql`
   read-only and retain its output.
4. Review any Actual Run with more than one legacy packout result. The migration
   does not infer or rewrite historical links.
5. Disable autodeploy.
6. Apply
   `scripts/postgresql/apply-projection-actual-run-separation-schema.sql`.
7. Run `scripts/postgresql/verify-projection-actual-run-separation.sql`.
8. Require migration-applied `true`, all three tables present, no duplicate
   current Actual Run packouts, and zero orphan snapshot/allocation rows.
9. Deploy the exact merged commit and verify health and authenticated pages.
10. Re-enable autodeploy only after smoke verification.

## Data behavior

The migration adds immutable Run Expectation and Estimated Allocation tables,
loosens the legacy Planning Projection foreign key on Packout Results, and adds
nullable Actual Run and Run Expectation relationships. It does not update,
delete, reset, or infer production records.

## Rollback

If migration verification fails, do not deploy the application. Restore the
verified pre-migration backup and investigate in a disposable copy.

If application smoke verification fails after a successful additive migration,
roll the application back to the prior deployed commit. The prior application
ignores the new nullable columns and additive tables. Do not run the EF `Down`
migration in production without a separately reviewed rollback and explicit
authorization.

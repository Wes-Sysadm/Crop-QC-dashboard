\set ON_ERROR_STOP on
\ir preflight-actual-run-detail-corrections.sql

BEGIN TRANSACTION READ ONLY;
DO $verify$
BEGIN
    IF EXISTS (
        SELECT 1 FROM "ActualRunDetailCorrections" correction
        LEFT JOIN "ActualRuns" run ON run."Id"=correction."ActualRunId"
        LEFT JOIN "Users" actor ON actor."Id"=correction."CorrectedByUserId"
        WHERE run."Id" IS NULL OR actor."Id" IS NULL) THEN
        RAISE EXCEPTION 'Orphan Actual Run detail correction detected';
    END IF;
END $verify$;
SELECT 'actual_run_detail_corrections_schema_pass' AS status,
       (SELECT count(*) FROM "ActualRunDetailCorrections") AS correction_rows,
       (SELECT count(*) FROM "__EFMigrationsHistory") AS migration_history_rows;
ROLLBACK;

\set ON_ERROR_STOP on
\ir preflight-grower-number-qc-recipients.sql
BEGIN TRANSACTION READ ONLY;
DO $verify$
BEGIN
    IF to_regclass(format('%I.%I', current_schema(), 'GrowerReportRecipients')) IS NULL THEN
        RAISE EXCEPTION 'GrowerReportRecipients table is missing';
    END IF;
    IF (SELECT count(*) FROM information_schema.columns
        WHERE table_schema=current_schema() AND table_name='GrowerReportRecipients') <> 12 THEN
        RAISE EXCEPTION 'Grower Number QC recipient columns are incomplete';
    END IF;
    IF (SELECT count(*) FROM pg_class i JOIN pg_index ix ON ix.indexrelid=i.oid
        JOIN pg_class t ON t.oid=ix.indrelid JOIN pg_namespace n ON n.oid=t.relnamespace
        WHERE n.nspname=current_schema() AND t.relname='GrowerReportRecipients'
          AND i.relname=ANY(ARRAY[
            left('IX_GrowerReportRecipients_CanonicalGrowerNumberId_IsActive_IsDeleted',63),
            left('IX_GrowerReportRecipients_CanonicalGrowerNumberId_NormalizedEmailAddress',63),
            left('IX_GrowerReportRecipients_CreatedByUserId',63),
            left('IX_GrowerReportRecipients_DeletedByUserId',63),
            left('IX_GrowerReportRecipients_UpdatedByUserId',63)])
          AND ix.indisvalid AND ix.indisready) <> 5 THEN
        RAISE EXCEPTION 'Grower Number QC recipient indexes are incomplete';
    END IF;
    IF (SELECT count(*) FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid
        JOIN pg_namespace n ON n.oid=t.relnamespace
        WHERE n.nspname=current_schema() AND t.relname='GrowerReportRecipients'
          AND c.convalidated AND c.contype IN ('p','f')) <> 5 THEN
        RAISE EXCEPTION 'Grower Number QC recipient constraints are incomplete';
    END IF;
    IF EXISTS (
        SELECT 1 FROM "GrowerReportRecipients"
        WHERE NOT "IsDeleted"
        GROUP BY "CanonicalGrowerNumberId", "NormalizedEmailAddress"
        HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION 'Duplicate active/non-deleted Grower Number recipients exist';
    END IF;
END $verify$;
SELECT 'grower_number_qc_recipient_schema_verified' AS status,
       23 AS checked_target_objects,
       (SELECT count(*) FROM "GrowerReportRecipients") AS recipient_rows,
       (SELECT count(*) FROM "GrowerReportRecipients" WHERE "IsActive" AND NOT "IsDeleted") AS active_recipient_rows;
ROLLBACK;

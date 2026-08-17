\set ON_ERROR_STOP on
\ir preflight-end-of-day-fill-warehouse-scope.sql

START TRANSACTION;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '10min';
SELECT pg_advisory_xact_lock(hashtextextended('CropQc:20260817075807_AddEndOfDayFillWarehouseScope', 0));

SELECT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema=current_schema() AND table_name='EndOfDayFillReportGroups' AND column_name='WarehouseId'
) AS schema_already_applied \gset

\if :schema_already_applied
\echo 'End of Day Fill warehouse-scope schema already exists; no schema objects or group mappings changed.'
\else
ALTER TABLE "EndOfDayFillReportGroups" ADD COLUMN "WarehouseId" integer;
UPDATE "EndOfDayFillReportGroups" SET "WarehouseId"=4 WHERE "Id"=1 AND "Name"='WP End of Day Fill' AND "Facility"='WP';
UPDATE "EndOfDayFillReportGroups" SET "WarehouseId"=1 WHERE "Id"=2 AND "Name"='EBS End of Day Fill' AND "Facility"='EBS';

DO $backfill_check$
BEGIN
    IF EXISTS (SELECT 1 FROM "EndOfDayFillReportGroups" WHERE "WarehouseId" IS NULL) THEN
        RAISE EXCEPTION 'Reviewed warehouse backfill did not map every existing group; transaction rolled back';
    END IF;
END $backfill_check$;

ALTER TABLE "EndOfDayFillReportGroups" ALTER COLUMN "WarehouseId" SET NOT NULL;
CREATE INDEX "IX_EndOfDayFillReportGroups_WarehouseId" ON "EndOfDayFillReportGroups" ("WarehouseId");
ALTER TABLE "EndOfDayFillReportGroups" ADD CONSTRAINT "FK_EndOfDayFillReportGroups_Warehouses_WarehouseId"
    FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT;
\endif

DO $postcheck$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema=current_schema() AND table_name='EndOfDayFillReportGroups' AND column_name='WarehouseId'
          AND data_type='integer' AND is_nullable='NO'
    )
       OR to_regclass(format('%I.%I', current_schema(), 'IX_EndOfDayFillReportGroups_WarehouseId')) IS NULL
       OR NOT EXISTS (
           SELECT 1 FROM pg_constraint
           WHERE connamespace=current_schema()::regnamespace
             AND conname='FK_EndOfDayFillReportGroups_Warehouses_WarehouseId'
             AND confdeltype='r'
       )
       OR NOT EXISTS (SELECT 1 FROM "EndOfDayFillReportGroups" WHERE "Id"=1 AND "WarehouseId"=4)
       OR NOT EXISTS (SELECT 1 FROM "EndOfDayFillReportGroups" WHERE "Id"=2 AND "WarehouseId"=1) THEN
        RAISE EXCEPTION 'Post-apply End of Day Fill warehouse scope is incomplete; transaction will roll back';
    END IF;
    IF current_setting('cropqc.test_force_eod_fill_warehouse_failure', true) = 'on' THEN
        RAISE EXCEPTION 'Forced End of Day Fill warehouse-scope compatibility failure for rollback regression testing';
    END IF;
END $postcheck$;

COMMIT;
\ir verify-end-of-day-fill-warehouse-scope.sql

\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

DO $verify$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema=current_schema() AND table_name='EndOfDayFillReportGroups' AND column_name='WarehouseId'
          AND data_type='integer' AND is_nullable='NO'
    ) THEN
        RAISE EXCEPTION 'EndOfDayFillReportGroups.WarehouseId is missing, nullable, or incompatible';
    END IF;
    IF NOT EXISTS (
        SELECT 1
        FROM pg_class t
        JOIN pg_namespace n ON n.oid=t.relnamespace
        JOIN pg_attribute a ON a.attrelid=t.oid AND a.attname='WarehouseId' AND NOT a.attisdropped
        JOIN pg_index i ON i.indrelid=t.oid
        JOIN pg_class ix ON ix.oid=i.indexrelid
        WHERE n.nspname=current_schema() AND t.relname='EndOfDayFillReportGroups'
          AND ix.relname='IX_EndOfDayFillReportGroups_WarehouseId'
          AND NOT i.indisunique AND i.indpred IS NULL AND i.indexprs IS NULL
          AND i.indnkeyatts=1 AND i.indnatts=1 AND i.indkey[0]=a.attnum
    ) THEN
        RAISE EXCEPTION 'IX_EndOfDayFillReportGroups_WarehouseId is missing or incompatible';
    END IF;
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint c
        JOIN pg_class t ON t.oid=c.conrelid
        JOIN pg_namespace n ON n.oid=t.relnamespace
        JOIN pg_attribute a ON a.attrelid=t.oid AND a.attname='WarehouseId' AND NOT a.attisdropped
        JOIN pg_class rt ON rt.oid=c.confrelid
        JOIN pg_attribute ra ON ra.attrelid=rt.oid AND ra.attname='Id' AND NOT ra.attisdropped
        WHERE n.nspname=current_schema() AND t.relname='EndOfDayFillReportGroups'
          AND c.conname='FK_EndOfDayFillReportGroups_Warehouses_WarehouseId'
          AND c.contype='f' AND rt.relname='Warehouses' AND c.confdeltype='r'
          AND c.conkey=ARRAY[a.attnum]::smallint[] AND c.confkey=ARRAY[ra.attnum]::smallint[]
    ) THEN
        RAISE EXCEPTION 'Warehouse scope foreign key is missing or incompatible';
    END IF;
    IF EXISTS (SELECT 1 FROM "EndOfDayFillReportGroups" WHERE "WarehouseId" IS NULL)
       OR NOT EXISTS (SELECT 1 FROM "EndOfDayFillReportGroups" WHERE "Id"=1 AND "WarehouseId"=4 AND "Name"='WP End of Day Fill')
       OR NOT EXISTS (SELECT 1 FROM "EndOfDayFillReportGroups" WHERE "Id"=2 AND "WarehouseId"=1 AND "Name"='EBS End of Day Fill') THEN
        RAISE EXCEPTION 'Reviewed existing group warehouse mappings are not exact';
    END IF;
END $verify$;

SELECT 'end_of_day_fill_warehouse_scope_verified' AS status,
       '20260817075807_AddEndOfDayFillWarehouseScope' AS migration,
       3 AS checked_target_objects,
       (SELECT COUNT(*) FROM "EndOfDayFillReportGroups") AS report_group_count,
       (SELECT COUNT(*) FROM "EndOfDayFillReportSends") AS historical_send_count,
       'migration_history_intentionally_unchanged' AS migration_history;

ROLLBACK;

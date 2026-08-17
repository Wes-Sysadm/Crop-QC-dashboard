\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

DO $preflight$
DECLARE
    target_count integer;
BEGIN
    SELECT
        (SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='EndOfDayFillReportGroups' AND column_name='WarehouseId')
        + (CASE WHEN to_regclass(format('%I.%I', current_schema(), 'IX_EndOfDayFillReportGroups_WarehouseId')) IS NULL THEN 0 ELSE 1 END)
        + (SELECT COUNT(*) FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname='FK_EndOfDayFillReportGroups_Warehouses_WarehouseId')
    INTO target_count;

    IF target_count NOT IN (0, 3) THEN
        RAISE EXCEPTION 'State C: partial or conflicting End of Day Fill warehouse-scope schema (% of 3 objects)', target_count;
    END IF;
    IF target_count = 0 THEN
        IF EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId"='20260817075807_AddEndOfDayFillWarehouseScope') THEN
            RAISE EXCEPTION 'State C: migration history records warehouse scope but its object set is absent';
        END IF;
        IF (SELECT COUNT(*) FROM "EndOfDayFillReportGroups") <> 2
           OR NOT EXISTS (SELECT 1 FROM "EndOfDayFillReportGroups" WHERE "Id"=1 AND "Name"='WP End of Day Fill' AND "Facility"='WP')
           OR NOT EXISTS (SELECT 1 FROM "EndOfDayFillReportGroups" WHERE "Id"=2 AND "Name"='EBS End of Day Fill' AND "Facility"='EBS') THEN
            RAISE EXCEPTION 'State C: existing report groups do not match the reviewed two-group production baseline';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM "Warehouses" WHERE "Id"=4 AND "Code"='WP')
           OR NOT EXISTS (SELECT 1 FROM "Warehouses" WHERE "Id"=3 AND "Code"='McDougall')
           OR NOT EXISTS (SELECT 1 FROM "Warehouses" WHERE "Id"=2 AND "Code"='DH')
           OR NOT EXISTS (SELECT 1 FROM "Warehouses" WHERE "Id"=1 AND "Code"='EBS') THEN
            RAISE EXCEPTION 'State C: exact reviewed warehouse identities 1/EBS, 2/DH, 3/McDougall, 4/WP are required';
        END IF;
    ELSE
        IF NOT EXISTS (
               SELECT 1 FROM information_schema.columns
               WHERE table_schema=current_schema() AND table_name='EndOfDayFillReportGroups' AND column_name='WarehouseId'
                 AND data_type='integer' AND is_nullable='NO'
           )
           OR NOT EXISTS (
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
           )
           OR NOT EXISTS (
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
           )
           OR EXISTS (SELECT 1 FROM "EndOfDayFillReportGroups" WHERE "WarehouseId" IS NULL)
           OR NOT EXISTS (SELECT 1 FROM "EndOfDayFillReportGroups" WHERE "Id"=1 AND "WarehouseId"=4)
           OR NOT EXISTS (SELECT 1 FROM "EndOfDayFillReportGroups" WHERE "Id"=2 AND "WarehouseId"=1) THEN
            RAISE EXCEPTION 'State C: warehouse-scope schema exists with incompatible column, index, foreign key, or group mapping';
        END IF;
    END IF;
END $preflight$;

SELECT CASE WHEN EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema=current_schema() AND table_name='EndOfDayFillReportGroups' AND column_name='WarehouseId'
) THEN 'State B' ELSE 'State A' END AS state,
'20260817075807_AddEndOfDayFillWarehouseScope' AS migration;

ROLLBACK;

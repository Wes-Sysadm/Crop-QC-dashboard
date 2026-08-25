\set ON_ERROR_STOP on
\ir verify-actual-run-sales-desk-attribution.sql

BEGIN TRANSACTION READ ONLY;
DO $preflight$
DECLARE
    existing_count integer;
    exact_count integer := 11;
BEGIN
    IF to_regclass(format('%I.%I', current_schema(), 'PackoutReportSources')) IS NULL THEN
        RAISE EXCEPTION 'State C: PackoutReportSources parent table is missing';
    END IF;
    IF to_regclass(format('%I.%I', current_schema(), 'Users')) IS NULL THEN
        RAISE EXCEPTION 'State C: Users parent table is missing';
    END IF;

    SELECT
        (SELECT count(*) FROM information_schema.columns
         WHERE table_schema=current_schema() AND table_name='PackoutReportSources'
           AND column_name=ANY(ARRAY['StorageProvider','StorageKey','StoragePath','DriveId','FileId','FolderId','ParseStatus','UploadedAt','UploadedByUserId']))
        + (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
           WHERE n.nspname=current_schema() AND c.relkind='i'
             AND c.relname='IX_PackoutReportSources_UploadedByUserId')
        + (SELECT count(*) FROM pg_constraint
           WHERE connamespace=current_schema()::regnamespace
             AND conname='FK_PackoutReportSources_Users_UploadedByUserId')
    INTO existing_count;

    IF existing_count NOT IN (0, exact_count) THEN
        RAISE EXCEPTION 'State C: partial/conflicting Packout document storage schema detected (% of % objects)', existing_count, exact_count;
    END IF;

    IF existing_count = exact_count THEN
        IF EXISTS (
            SELECT 1 FROM (VALUES
                ('StorageProvider','character varying','YES',50),
                ('StorageKey','character varying','YES',500),
                ('StoragePath','character varying','YES',1000),
                ('DriveId','character varying','YES',250),
                ('FileId','character varying','YES',250),
                ('FolderId','character varying','YES',250),
                ('ParseStatus','character varying','NO',50),
                ('UploadedAt','timestamp with time zone','YES',NULL::integer),
                ('UploadedByUserId','integer','YES',NULL::integer)
            ) e(name,data_type,is_nullable,maximum_length)
            LEFT JOIN information_schema.columns c
              ON c.table_schema=current_schema() AND c.table_name='PackoutReportSources'
             AND c.column_name=e.name AND c.data_type=e.data_type AND c.is_nullable=e.is_nullable
             AND c.character_maximum_length IS NOT DISTINCT FROM e.maximum_length
            WHERE c.column_name IS NULL
        ) THEN
            RAISE EXCEPTION 'State C: Packout document storage columns are incompatible';
        END IF;
        IF NOT EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema=current_schema() AND table_name='PackoutReportSources'
              AND column_name='ParseStatus' AND column_default LIKE '%Legacy metadata only%'
        ) THEN
            RAISE EXCEPTION 'State C: Packout ParseStatus default is incompatible';
        END IF;
        IF NOT EXISTS (
            SELECT 1 FROM pg_class c
            JOIN pg_namespace n ON n.oid=c.relnamespace AND n.nspname=current_schema()
            JOIN pg_index i ON i.indexrelid=c.oid AND i.indisvalid AND i.indisready
            WHERE c.relname='IX_PackoutReportSources_UploadedByUserId'
              AND NOT i.indisunique
              AND pg_get_indexdef(c.oid) LIKE '%("UploadedByUserId")'
        ) THEN
            RAISE EXCEPTION 'State C: Packout document uploader index is incompatible';
        END IF;
        IF NOT EXISTS (
            SELECT 1 FROM pg_constraint c
            WHERE c.connamespace=current_schema()::regnamespace
              AND c.conname='FK_PackoutReportSources_Users_UploadedByUserId'
              AND c.convalidated
              AND pg_get_constraintdef(c.oid)='FOREIGN KEY ("UploadedByUserId") REFERENCES "Users"("Id") ON DELETE SET NULL'
        ) THEN
            RAISE EXCEPTION 'State C: Packout document uploader foreign key is incompatible';
        END IF;
        IF EXISTS (
            SELECT 1 FROM "PackoutReportSources"
            WHERE btrim("ParseStatus")=''
               OR ("StorageKey" IS NOT NULL AND ("StorageProvider" IS NULL OR "StoragePath" IS NULL OR "UploadedAt" IS NULL OR "UploadedByUserId" IS NULL))
        ) THEN
            RAISE EXCEPTION 'State C: Packout document storage metadata is internally inconsistent';
        END IF;
    END IF;
END $preflight$;

SELECT CASE WHEN EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema=current_schema() AND table_name='PackoutReportSources' AND column_name='StorageKey')
    THEN 'state_b_complete_exact' ELSE 'state_a_absent' END AS compatibility_state;
ROLLBACK;

\set ON_ERROR_STOP on
\ir verify-room-treatment-tracking.sql

BEGIN TRANSACTION READ ONLY;
DO $preflight$
DECLARE
    existing_count integer;
    exact_count integer;
BEGIN
    SELECT
        (CASE WHEN to_regclass(format('%I.%I', current_schema(), 'RoomTreatmentApplicationAttachments')) IS NULL THEN 0 ELSE 1 END)
        + (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomTreatmentApplicationAttachments')
        + (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=current_schema() AND c.relkind='i' AND c.relname IN (
            'IX_RoomTreatmentApplicationAttachments_CreatedByUserId',
            'IX_RoomTreatmentApplicationAttachments_DeletedByUserId',
            'IX_TreatmentReportAttachments_Application_IsDeleted_CreatedAt',
            'UX_TreatmentReportAttachments_Application_OperationKey'))
        + (SELECT count(*) FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY[
            'PK_RoomTreatmentApplicationAttachments',
            'FK_RoomTreatmentApplicationAttachments_RoomTreatmentApplications_RoomTreatmentApplicationId',
            'FK_RoomTreatmentApplicationAttachments_Users_CreatedByUserId',
            'FK_RoomTreatmentApplicationAttachments_Users_DeletedByUserId']) x)))
    INTO existing_count;

    exact_count := 26;
    IF existing_count NOT IN (0, exact_count) THEN
        RAISE EXCEPTION 'State C: partial/conflicting treatment report attachment schema detected (% of % objects)', existing_count, exact_count;
    END IF;
    IF existing_count = exact_count THEN
        IF EXISTS (
            SELECT 1 FROM (VALUES
                ('Id','bigint','NO',NULL::integer,'YES'),('RoomTreatmentApplicationId','bigint','NO',NULL::integer,'NO'),
                ('OperationKey','character varying','NO',100,'NO'),('FileName','character varying','NO',255,'NO'),
                ('ContentType','character varying','NO',100,'NO'),('FileSizeBytes','bigint','NO',NULL::integer,'NO'),
                ('StorageProvider','character varying','NO',50,'NO'),('DriveId','character varying','YES',200,'NO'),
                ('FileId','character varying','NO',200,'NO'),('FolderId','character varying','YES',200,'NO'),
                ('StoragePath','character varying','NO',1000,'NO'),('CreatedAt','timestamp with time zone','NO',NULL::integer,'NO'),
                ('CreatedByUserId','integer','NO',NULL::integer,'NO'),('IsDeleted','boolean','NO',NULL::integer,'NO'),
                ('DeletedAt','timestamp with time zone','YES',NULL::integer,'NO'),('DeletedByUserId','integer','YES',NULL::integer,'NO'),
                ('DeleteReason','character varying','YES',1000,'NO')
            ) e(name,data_type,is_nullable,maximum_length,is_identity)
            LEFT JOIN information_schema.columns c ON c.table_schema=current_schema() AND c.table_name='RoomTreatmentApplicationAttachments'
              AND c.column_name=e.name AND c.data_type=e.data_type AND c.is_nullable=e.is_nullable
              AND c.character_maximum_length IS NOT DISTINCT FROM e.maximum_length AND c.is_identity=e.is_identity
            WHERE c.column_name IS NULL
        ) THEN RAISE EXCEPTION 'State C: treatment report attachment columns are incompatible'; END IF;
        IF EXISTS (
            SELECT 1 FROM (VALUES
                ('IX_RoomTreatmentApplicationAttachments_CreatedByUserId',false,'("CreatedByUserId")'),
                ('IX_RoomTreatmentApplicationAttachments_DeletedByUserId',false,'("DeletedByUserId")'),
                ('IX_TreatmentReportAttachments_Application_IsDeleted_CreatedAt',false,'("RoomTreatmentApplicationId", "IsDeleted", "CreatedAt")'),
                ('UX_TreatmentReportAttachments_Application_OperationKey',true,'("RoomTreatmentApplicationId", "OperationKey")')
            ) e(name,is_unique,column_suffix)
            LEFT JOIN pg_class c ON c.relname=e.name
            LEFT JOIN pg_namespace n ON n.oid=c.relnamespace AND n.nspname=current_schema()
            LEFT JOIN pg_index i ON i.indexrelid=c.oid AND i.indisvalid AND i.indisready
            WHERE n.oid IS NULL OR i.indisunique IS DISTINCT FROM e.is_unique OR pg_get_indexdef(c.oid) NOT LIKE '%'||e.column_suffix
        ) THEN RAISE EXCEPTION 'State C: treatment report attachment indexes are incompatible'; END IF;
        IF EXISTS (
            SELECT 1 FROM (VALUES
                ('PK_RoomTreatmentApplicationAttachments','PRIMARY KEY ("Id")'),
                (left('FK_RoomTreatmentApplicationAttachments_RoomTreatmentApplications_RoomTreatmentApplicationId',63),'FOREIGN KEY ("RoomTreatmentApplicationId") REFERENCES "RoomTreatmentApplications"("Id") ON DELETE RESTRICT'),
                ('FK_RoomTreatmentApplicationAttachments_Users_CreatedByUserId','FOREIGN KEY ("CreatedByUserId") REFERENCES "Users"("Id") ON DELETE RESTRICT'),
                ('FK_RoomTreatmentApplicationAttachments_Users_DeletedByUserId','FOREIGN KEY ("DeletedByUserId") REFERENCES "Users"("Id") ON DELETE SET NULL')
            ) e(name,definition)
            LEFT JOIN pg_constraint c ON c.connamespace=current_schema()::regnamespace AND c.conname=e.name
            WHERE c.oid IS NULL OR pg_get_constraintdef(c.oid)<>e.definition
        ) THEN RAISE EXCEPTION 'State C: treatment report attachment constraints are incompatible'; END IF;
    END IF;
END $preflight$;

SELECT CASE WHEN to_regclass(format('%I.%I', current_schema(), 'RoomTreatmentApplicationAttachments')) IS NULL
       THEN 'state_a_absent' ELSE 'state_b_complete_exact' END AS compatibility_state;
ROLLBACK;

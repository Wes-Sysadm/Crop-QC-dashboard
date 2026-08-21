\set ON_ERROR_STOP on
\ir verify-treatment-report-attachments.sql

BEGIN TRANSACTION READ ONLY;
DO $preflight$
DECLARE
    new_object_count integer;
    state_b boolean;
BEGIN
    SELECT
        (SELECT count(*) FROM information_schema.columns
         WHERE table_schema=current_schema() AND (table_name,column_name) IN (
            ('TreatmentChemicals','ApplicationLevel'),
            ('RoomTreatmentApplications','ApplicationLevel'),
            ('RoomTreatmentApplications','ReceiptId'),
            ('RoomTreatmentApplicationSources','ReceiptId'),
            ('TreatmentLineageSegments','ReceiptId'),
            ('TreatmentLineageMovements','ReceiptId')))
        + (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
           WHERE n.nspname=current_schema() AND c.relkind='i' AND c.relname IN (
            'IX_TreatmentChemicals_ApplicationLevel_Crop_IsActive_ProductNam',
            'IX_RoomTreatmentApplications_ReceiptId_AppliedAt',
            'IX_RoomTreatmentApplicationSources_ReceiptId',
            'IX_TreatmentLineageSegments_ReceiptId',
            'UX_TreatmentLineageSegments_Receipt',
            'UX_TreatmentLineageSegments_Unassigned',
            'IX_TreatmentLineageMovements_ReceiptId'))
        + (SELECT count(*) FROM pg_constraint
           WHERE connamespace=current_schema()::regnamespace AND conname IN (
            'FK_RoomTreatmentApplications_Receipts_ReceiptId',
            'FK_RoomTreatmentApplicationSources_Receipts_ReceiptId',
            'FK_TreatmentLineageSegments_Receipts_ReceiptId',
            'FK_TreatmentLineageMovements_Receipts_ReceiptId'))
    INTO new_object_count;

    IF new_object_count NOT IN (0, 17) THEN
        RAISE EXCEPTION 'State C: partial Receiving treatment schema detected (% of 17 new objects)', new_object_count;
    END IF;
    state_b := new_object_count = 17;

    IF NOT state_b THEN
        IF to_regclass(format('%I.%I', current_schema(), 'IX_TreatmentChemicals_Crop_IsActive_ProductName')) IS NULL
           OR to_regclass(format('%I.%I', current_schema(), 'IX_TreatmentLineageSegments_RoomId_IdentityKey_TreatmentSignature')) IS NULL THEN
            RAISE EXCEPTION 'State C: expected legacy indexes are missing';
        END IF;
    ELSE
        IF to_regclass(format('%I.%I', current_schema(), 'IX_TreatmentChemicals_Crop_IsActive_ProductName')) IS NOT NULL
           OR to_regclass(format('%I.%I', current_schema(), 'IX_TreatmentLineageSegments_RoomId_IdentityKey_TreatmentSignature')) IS NOT NULL THEN
            RAISE EXCEPTION 'State C: legacy and Receiving treatment indexes coexist';
        END IF;

        IF EXISTS (
            SELECT 1 FROM (VALUES
                ('TreatmentChemicals','ApplicationLevel','character varying','NO',25),
                ('RoomTreatmentApplications','ApplicationLevel','character varying','NO',25),
                ('RoomTreatmentApplications','ReceiptId','bigint','YES',NULL::integer),
                ('RoomTreatmentApplicationSources','ReceiptId','bigint','YES',NULL::integer),
                ('TreatmentLineageSegments','ReceiptId','bigint','YES',NULL::integer),
                ('TreatmentLineageMovements','ReceiptId','bigint','YES',NULL::integer)
            ) e(table_name,column_name,data_type,is_nullable,maximum_length)
            LEFT JOIN information_schema.columns c
              ON c.table_schema=current_schema() AND c.table_name=e.table_name AND c.column_name=e.column_name
             AND c.data_type=e.data_type AND c.is_nullable=e.is_nullable
             AND c.character_maximum_length IS NOT DISTINCT FROM e.maximum_length
            WHERE c.column_name IS NULL
        ) THEN RAISE EXCEPTION 'State C: Receiving treatment columns are incompatible'; END IF;

        IF EXISTS (
            SELECT 1 FROM (VALUES
                ('IX_TreatmentChemicals_ApplicationLevel_Crop_IsActive_ProductNam',false,'("ApplicationLevel", "Crop", "IsActive", "ProductName")',NULL::text),
                ('IX_RoomTreatmentApplications_ReceiptId_AppliedAt',false,'("ReceiptId", "AppliedAt")',NULL::text),
                ('IX_RoomTreatmentApplicationSources_ReceiptId',false,'("ReceiptId")',NULL::text),
                ('IX_TreatmentLineageSegments_ReceiptId',false,'("ReceiptId")',NULL::text),
                ('UX_TreatmentLineageSegments_Receipt',true,'("RoomId", "IdentityKey", "TreatmentSignature", "ReceiptId")','("ReceiptId" IS NOT NULL)'),
                ('UX_TreatmentLineageSegments_Unassigned',true,'("RoomId", "IdentityKey", "TreatmentSignature")','("ReceiptId" IS NULL)'),
                ('IX_TreatmentLineageMovements_ReceiptId',false,'("ReceiptId")',NULL::text)
            ) e(name,is_unique,column_suffix,predicate)
            LEFT JOIN pg_class c ON c.relname=e.name
            LEFT JOIN pg_namespace n ON n.oid=c.relnamespace AND n.nspname=current_schema()
            LEFT JOIN pg_index i ON i.indexrelid=c.oid AND i.indisvalid AND i.indisready
            WHERE n.oid IS NULL OR i.indisunique IS DISTINCT FROM e.is_unique
               OR pg_get_indexdef(c.oid) NOT LIKE '%'||e.column_suffix||'%'
               OR (e.predicate IS NOT NULL AND pg_get_expr(i.indpred, i.indrelid)<>e.predicate)
        ) THEN RAISE EXCEPTION 'State C: Receiving treatment indexes are incompatible'; END IF;

        IF EXISTS (
            SELECT 1 FROM (VALUES
                ('FK_RoomTreatmentApplications_Receipts_ReceiptId','FOREIGN KEY ("ReceiptId") REFERENCES "Receipts"("Id") ON DELETE RESTRICT'),
                ('FK_RoomTreatmentApplicationSources_Receipts_ReceiptId','FOREIGN KEY ("ReceiptId") REFERENCES "Receipts"("Id") ON DELETE RESTRICT'),
                ('FK_TreatmentLineageSegments_Receipts_ReceiptId','FOREIGN KEY ("ReceiptId") REFERENCES "Receipts"("Id") ON DELETE RESTRICT'),
                ('FK_TreatmentLineageMovements_Receipts_ReceiptId','FOREIGN KEY ("ReceiptId") REFERENCES "Receipts"("Id") ON DELETE RESTRICT')
            ) e(name,definition)
            LEFT JOIN pg_constraint c ON c.connamespace=current_schema()::regnamespace AND c.conname=e.name
            WHERE c.oid IS NULL OR pg_get_constraintdef(c.oid)<>e.definition
        ) THEN RAISE EXCEPTION 'State C: Receiving treatment foreign keys are incompatible'; END IF;

        IF EXISTS (SELECT 1 FROM "TreatmentChemicals" WHERE "ApplicationLevel" NOT IN ('Room','Receiving'))
           OR EXISTS (SELECT 1 FROM "RoomTreatmentApplications" WHERE "ApplicationLevel" NOT IN ('Room','Receiving')) THEN
            RAISE EXCEPTION 'State C: invalid ApplicationLevel data exists';
        END IF;
    END IF;
END $preflight$;

SELECT CASE WHEN EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema=current_schema() AND table_name='TreatmentChemicals' AND column_name='ApplicationLevel')
    THEN 'state_b_complete_exact' ELSE 'state_a_absent_safe' END AS compatibility_state;
ROLLBACK;

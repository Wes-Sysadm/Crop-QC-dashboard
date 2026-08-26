\set ON_ERROR_STOP on
\ir verify-packout-document-storage.sql

BEGIN TRANSACTION READ ONLY;
DO $preflight$
DECLARE
    existing_count integer;
    exact_count integer := 23;
BEGIN
    IF to_regclass(format('%I.%I', current_schema(), 'CanonicalGrowerNumbers')) IS NULL THEN
        RAISE EXCEPTION 'State C: CanonicalGrowerNumbers parent table is missing';
    END IF;
    IF to_regclass(format('%I.%I', current_schema(), 'Users')) IS NULL THEN
        RAISE EXCEPTION 'State C: Users parent table is missing';
    END IF;

    SELECT
        (SELECT count(*) FROM information_schema.tables
         WHERE table_schema=current_schema() AND table_name='GrowerReportRecipients')
        + (SELECT count(*) FROM information_schema.columns
           WHERE table_schema=current_schema() AND table_name='GrowerReportRecipients'
             AND column_name=ANY(ARRAY['Id','CanonicalGrowerNumberId','EmailAddress','NormalizedEmailAddress','IsActive','IsDeleted','CreatedAt','CreatedByUserId','UpdatedAt','UpdatedByUserId','DeletedAt','DeletedByUserId']))
        + (SELECT count(*) FROM pg_class i JOIN pg_index ix ON ix.indexrelid=i.oid
           JOIN pg_class t ON t.oid=ix.indrelid JOIN pg_namespace n ON n.oid=t.relnamespace
           WHERE n.nspname=current_schema() AND t.relname='GrowerReportRecipients'
             AND i.relname=ANY(ARRAY[
                left('IX_GrowerReportRecipients_CanonicalGrowerNumberId_IsActive_IsDeleted',63),
                left('IX_GrowerReportRecipients_CanonicalGrowerNumberId_NormalizedEmailAddress',63),
                left('IX_GrowerReportRecipients_CreatedByUserId',63),
                left('IX_GrowerReportRecipients_DeletedByUserId',63),
                left('IX_GrowerReportRecipients_UpdatedByUserId',63)]))
        + (SELECT count(*) FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid
           JOIN pg_namespace n ON n.oid=t.relnamespace
           WHERE n.nspname=current_schema() AND t.relname='GrowerReportRecipients'
             AND c.conname=ANY(ARRAY[
                left('PK_GrowerReportRecipients',63),
                left('FK_GrowerReportRecipients_CanonicalGrowerNumbers_CanonicalGrowerNumberId',63),
                left('FK_GrowerReportRecipients_Users_CreatedByUserId',63),
                left('FK_GrowerReportRecipients_Users_DeletedByUserId',63),
                left('FK_GrowerReportRecipients_Users_UpdatedByUserId',63)]))
    INTO existing_count;

    IF existing_count NOT IN (0, exact_count) THEN
        RAISE EXCEPTION 'State C: partial/conflicting Grower Number QC recipient schema detected (% of % objects)', existing_count, exact_count;
    END IF;

    IF existing_count = exact_count THEN
        IF EXISTS (
            SELECT 1 FROM (VALUES
                ('Id','integer','NO',NULL::integer,'YES'),
                ('CanonicalGrowerNumberId','integer','NO',NULL::integer,'NO'),
                ('EmailAddress','character varying','NO',320,'NO'),
                ('NormalizedEmailAddress','character varying','NO',320,'NO'),
                ('IsActive','boolean','NO',NULL::integer,'NO'),
                ('IsDeleted','boolean','NO',NULL::integer,'NO'),
                ('CreatedAt','timestamp with time zone','NO',NULL::integer,'NO'),
                ('CreatedByUserId','integer','YES',NULL::integer,'NO'),
                ('UpdatedAt','timestamp with time zone','NO',NULL::integer,'NO'),
                ('UpdatedByUserId','integer','YES',NULL::integer,'NO'),
                ('DeletedAt','timestamp with time zone','YES',NULL::integer,'NO'),
                ('DeletedByUserId','integer','YES',NULL::integer,'NO')
            ) e(name,data_type,is_nullable,maximum_length,is_identity)
            LEFT JOIN information_schema.columns c
              ON c.table_schema=current_schema() AND c.table_name='GrowerReportRecipients'
             AND c.column_name=e.name AND c.data_type=e.data_type AND c.is_nullable=e.is_nullable
             AND c.character_maximum_length IS NOT DISTINCT FROM e.maximum_length
             AND c.is_identity=e.is_identity
            WHERE c.column_name IS NULL
        ) THEN
            RAISE EXCEPTION 'State C: Grower Number QC recipient columns are incompatible';
        END IF;

        IF NOT EXISTS (
            SELECT 1 FROM pg_class i JOIN pg_index ix ON ix.indexrelid=i.oid
            JOIN pg_class t ON t.oid=ix.indrelid JOIN pg_namespace n ON n.oid=t.relnamespace
            WHERE n.nspname=current_schema() AND t.relname='GrowerReportRecipients'
              AND i.relname=left('IX_GrowerReportRecipients_CanonicalGrowerNumberId_NormalizedEmailAddress',63)
              AND ix.indisvalid AND ix.indisready AND ix.indisunique
              AND (
                    pg_get_expr(ix.indpred,ix.indrelid) ILIKE '%"IsDeleted" = false%'
                    OR pg_get_expr(ix.indpred,ix.indrelid) ILIKE '%NOT "IsDeleted"%'
                  )
        ) THEN
            RAISE EXCEPTION 'State C: Grower Number recipient uniqueness index is incompatible';
        END IF;

        IF EXISTS (
            SELECT 1
            FROM (VALUES
                (left('IX_GrowerReportRecipients_CanonicalGrowerNumberId_IsActive_IsDeleted',63), false,
                    '("CanonicalGrowerNumberId", "IsActive", "IsDeleted")'),
                (left('IX_GrowerReportRecipients_CanonicalGrowerNumberId_NormalizedEmailAddress',63), true,
                    '("CanonicalGrowerNumberId", "NormalizedEmailAddress")'),
                (left('IX_GrowerReportRecipients_CreatedByUserId',63), false, '("CreatedByUserId")'),
                (left('IX_GrowerReportRecipients_DeletedByUserId',63), false, '("DeletedByUserId")'),
                (left('IX_GrowerReportRecipients_UpdatedByUserId',63), false, '("UpdatedByUserId")')
            ) expected(index_name, is_unique, key_columns)
            LEFT JOIN pg_class i ON i.relname=expected.index_name
            LEFT JOIN pg_index ix ON ix.indexrelid=i.oid
            LEFT JOIN pg_class t ON t.oid=ix.indrelid AND t.relname='GrowerReportRecipients'
            LEFT JOIN pg_namespace n ON n.oid=t.relnamespace AND n.nspname=current_schema()
            WHERE n.oid IS NULL
               OR NOT ix.indisvalid
               OR NOT ix.indisready
               OR ix.indisunique IS DISTINCT FROM expected.is_unique
               OR pg_get_indexdef(ix.indexrelid) NOT ILIKE '%' || expected.key_columns || '%'
        ) THEN
            RAISE EXCEPTION 'State C: Grower Number recipient index definitions are incompatible';
        END IF;

        IF (SELECT count(*) FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid
            JOIN pg_namespace n ON n.oid=t.relnamespace
            WHERE n.nspname=current_schema() AND t.relname='GrowerReportRecipients'
              AND c.convalidated AND c.contype IN ('p','f')
              AND c.conname=ANY(ARRAY[
                left('PK_GrowerReportRecipients',63),
                left('FK_GrowerReportRecipients_CanonicalGrowerNumbers_CanonicalGrowerNumberId',63),
                left('FK_GrowerReportRecipients_Users_CreatedByUserId',63),
                left('FK_GrowerReportRecipients_Users_DeletedByUserId',63),
                left('FK_GrowerReportRecipients_Users_UpdatedByUserId',63)])) <> 5 THEN
            RAISE EXCEPTION 'State C: Grower Number recipient constraints are incompatible';
        END IF;

        IF EXISTS (
            SELECT 1
            FROM (VALUES
                (left('PK_GrowerReportRecipients',63), 'PRIMARY KEY ("Id")'),
                (left('FK_GrowerReportRecipients_CanonicalGrowerNumbers_CanonicalGrowerNumberId',63),
                    'FOREIGN KEY ("CanonicalGrowerNumberId") REFERENCES "CanonicalGrowerNumbers"("Id") ON DELETE RESTRICT'),
                (left('FK_GrowerReportRecipients_Users_CreatedByUserId',63),
                    'FOREIGN KEY ("CreatedByUserId") REFERENCES "Users"("Id") ON DELETE SET NULL'),
                (left('FK_GrowerReportRecipients_Users_DeletedByUserId',63),
                    'FOREIGN KEY ("DeletedByUserId") REFERENCES "Users"("Id") ON DELETE SET NULL'),
                (left('FK_GrowerReportRecipients_Users_UpdatedByUserId',63),
                    'FOREIGN KEY ("UpdatedByUserId") REFERENCES "Users"("Id") ON DELETE SET NULL')
            ) expected(constraint_name, definition)
            LEFT JOIN pg_constraint c ON c.conname=expected.constraint_name
            LEFT JOIN pg_class t ON t.oid=c.conrelid AND t.relname='GrowerReportRecipients'
            LEFT JOIN pg_namespace n ON n.oid=t.relnamespace AND n.nspname=current_schema()
            WHERE n.oid IS NULL
               OR NOT c.convalidated
               OR pg_get_constraintdef(c.oid) <> expected.definition
        ) THEN
            RAISE EXCEPTION 'State C: Grower Number recipient constraint definitions are incompatible';
        END IF;

        IF EXISTS (
            SELECT 1 FROM "GrowerReportRecipients"
            WHERE NOT "IsDeleted"
            GROUP BY "CanonicalGrowerNumberId", "NormalizedEmailAddress"
            HAVING count(*) > 1
        ) THEN
            RAISE EXCEPTION 'State C: duplicate active/non-deleted Grower Number recipients exist';
        END IF;
    END IF;
END $preflight$;

SELECT CASE WHEN to_regclass(format('%I.%I', current_schema(), 'GrowerReportRecipients')) IS NULL
    THEN 'state_a_absent' ELSE 'state_b_complete_exact' END AS compatibility_state;
ROLLBACK;

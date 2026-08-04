\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;

DO $verify$
DECLARE
    missing_columns text;
    missing_indexes text;
    missing_foreign_keys text;
    missing_primary_keys text;
BEGIN
    SELECT string_agg(expected.display_name, ', ' ORDER BY expected.display_name)
    INTO missing_columns
    FROM (VALUES
        ('UserEmploymentHistory table', 'UserEmploymentHistory', NULL::text, NULL::text),
        ('UserEmploymentHistory.Id', 'UserEmploymentHistory', 'Id', 'NO'),
        ('UserEmploymentHistory.UserId', 'UserEmploymentHistory', 'UserId', 'NO'),
        ('UserEmploymentHistory.PreviousEmploymentFacility', 'UserEmploymentHistory', 'PreviousEmploymentFacility', 'NO'),
        ('UserEmploymentHistory.EmploymentFacility', 'UserEmploymentHistory', 'EmploymentFacility', 'NO'),
        ('UserEmploymentHistory.EffectiveAt', 'UserEmploymentHistory', 'EffectiveAt', 'NO'),
        ('UserEmploymentHistory.ChangedByUserId', 'UserEmploymentHistory', 'ChangedByUserId', 'YES'),
        ('UserEmploymentHistory.ChangedAt', 'UserEmploymentHistory', 'ChangedAt', 'NO'),
        ('Users.EmploymentFacility', 'Users', 'EmploymentFacility', 'NO'),
        ('Users.EmploymentEffectiveAt', 'Users', 'EmploymentEffectiveAt', 'YES'),
        ('Users.EmploymentUpdatedAt', 'Users', 'EmploymentUpdatedAt', 'YES'),
        ('Users.EmploymentUpdatedByUserId', 'Users', 'EmploymentUpdatedByUserId', 'YES'),
        ('ActualRuns.RunFacilityWarehouseId', 'ActualRuns', 'RunFacilityWarehouseId', 'YES'),
        ('ActualRuns.RunFacilityCodeSnapshot', 'ActualRuns', 'RunFacilityCodeSnapshot', 'YES'),
        ('ActualRuns.RunFacilityAssignmentSource', 'ActualRuns', 'RunFacilityAssignmentSource', 'YES'),
        ('ActualRuns.RunFacilityAssignedAt', 'ActualRuns', 'RunFacilityAssignedAt', 'YES'),
        ('ActualRuns.RunFacilityAssignedByUserId', 'ActualRuns', 'RunFacilityAssignedByUserId', 'YES'),
        ('ActualRunOverrideRequests.RunFacilityWarehouseId', 'ActualRunOverrideRequests', 'RunFacilityWarehouseId', 'YES'),
        ('ActualRunOverrideRequests.RunFacilityCodeSnapshot', 'ActualRunOverrideRequests', 'RunFacilityCodeSnapshot', 'YES'),
        ('ActualRunOverrideRequests.RunFacilityAssignmentSource', 'ActualRunOverrideRequests', 'RunFacilityAssignmentSource', 'YES'),
        ('BinsRunEntries.ReportingFacilityWarehouseId', 'BinsRunEntries', 'ReportingFacilityWarehouseId', 'YES'),
        ('BinsRunEntries.ReportingFacilityCodeSnapshot', 'BinsRunEntries', 'ReportingFacilityCodeSnapshot', 'YES'),
        ('BinsRunEntries.ReportingFacilityAssignmentSource', 'BinsRunEntries', 'ReportingFacilityAssignmentSource', 'YES'),
        ('BinsRunEntries.ReportingFacilityAssignedAt', 'BinsRunEntries', 'ReportingFacilityAssignedAt', 'YES'),
        ('BinsRunEntries.ReportingFacilityAssignedByUserId', 'BinsRunEntries', 'ReportingFacilityAssignedByUserId', 'YES'),
        ('BinsRunEntries.ReportingCropYearSnapshot', 'BinsRunEntries', 'ReportingCropYearSnapshot', 'YES'),
        ('BinsRunEntries.ReportingFruitProfileIdSnapshot', 'BinsRunEntries', 'ReportingFruitProfileIdSnapshot', 'YES'),
        ('BinsRunEntries.ReportingVarietyCodeSnapshot', 'BinsRunEntries', 'ReportingVarietyCodeSnapshot', 'YES'),
        ('BinsRunEntries.ProductionTypeSnapshot', 'BinsRunEntries', 'ProductionTypeSnapshot', 'YES'),
        ('BinsRunEntries.IsOrganicSnapshot', 'BinsRunEntries', 'IsOrganicSnapshot', 'YES'),
        ('BinsRunEntries.GrowerNumberSnapshot', 'BinsRunEntries', 'GrowerNumberSnapshot', 'YES')
    ) AS expected(display_name, table_name, column_name, expected_nullable)
    WHERE (expected.column_name IS NULL
           AND to_regclass(format('%I.%I', current_schema(), expected.table_name)) IS NULL)
       OR (expected.column_name IS NOT NULL
           AND NOT EXISTS (
               SELECT 1 FROM information_schema.columns
               WHERE table_schema=current_schema()
                 AND table_name=expected.table_name
                 AND column_name=expected.column_name
                 AND is_nullable=expected.expected_nullable));

    SELECT string_agg(expected.index_name, ', ' ORDER BY expected.index_name)
    INTO missing_indexes
    FROM (VALUES
        ('Users', 'IX_Users_EmploymentFacility'),
        ('Users', 'IX_Users_EmploymentUpdatedByUserId'),
        ('ActualRuns', 'IX_ActualRuns_RunFacilityAssignedByUserId'),
        ('ActualRuns', 'IX_ActualRuns_RunFacilityWarehouseId_Status_RunAt'),
        ('ActualRunOverrideRequests', 'IX_ActualRunOverrideRequests_RunFacilityWarehouseId'),
        ('BinsRunEntries', 'IX_BinsRunEntries_ReportingFacilityAssignedByUserId'),
        ('BinsRunEntries', 'IX_BinsRunEntries_ReportingFacilityWarehouseId_ReportingCropYearSnapshot_RunAt'),
        ('UserEmploymentHistory', 'IX_UserEmploymentHistory_ChangedByUserId'),
        ('UserEmploymentHistory', 'IX_UserEmploymentHistory_UserId_ChangedAt')
    ) AS expected(table_name, index_name)
    WHERE NOT EXISTS (
        SELECT 1 FROM pg_class AS table_row
        JOIN pg_index AS index_metadata ON index_metadata.indrelid=table_row.oid
        JOIN pg_class AS index_row ON index_row.oid=index_metadata.indexrelid
        WHERE table_row.relnamespace=current_schema()::regnamespace
          AND table_row.relname=expected.table_name
          AND index_row.relname=left(expected.index_name, 63));

    SELECT string_agg(expected.constraint_name, ', ' ORDER BY expected.constraint_name)
    INTO missing_foreign_keys
    FROM (VALUES
        ('ActualRunOverrideRequests', 'FK_ActualRunOverrideRequests_Warehouses_RunFacilityWarehouseId'),
        ('ActualRuns', 'FK_ActualRuns_Users_RunFacilityAssignedByUserId'),
        ('ActualRuns', 'FK_ActualRuns_Warehouses_RunFacilityWarehouseId'),
        ('BinsRunEntries', 'FK_BinsRunEntries_Users_ReportingFacilityAssignedByUserId'),
        ('BinsRunEntries', 'FK_BinsRunEntries_Warehouses_ReportingFacilityWarehouseId'),
        ('Users', 'FK_Users_Users_EmploymentUpdatedByUserId'),
        ('UserEmploymentHistory', 'FK_UserEmploymentHistory_Users_ChangedByUserId'),
        ('UserEmploymentHistory', 'FK_UserEmploymentHistory_Users_UserId')
    ) AS expected(table_name, constraint_name)
    WHERE NOT EXISTS (
        SELECT 1 FROM pg_constraint AS constraint_row
        JOIN pg_class AS table_row ON table_row.oid=constraint_row.conrelid
        JOIN pg_namespace AS namespace_row ON namespace_row.oid=table_row.relnamespace
        WHERE namespace_row.nspname=current_schema()
          AND table_row.relname=expected.table_name
          AND constraint_row.conname=left(expected.constraint_name, 63)
          AND constraint_row.contype='f');

    SELECT string_agg(expected.constraint_name, ', ' ORDER BY expected.constraint_name)
    INTO missing_primary_keys
    FROM (VALUES ('UserEmploymentHistory', 'PK_UserEmploymentHistory')) AS expected(table_name, constraint_name)
    WHERE NOT EXISTS (
        SELECT 1 FROM pg_constraint AS constraint_row
        JOIN pg_class AS table_row ON table_row.oid=constraint_row.conrelid
        JOIN pg_namespace AS namespace_row ON namespace_row.oid=table_row.relnamespace
        WHERE namespace_row.nspname=current_schema()
          AND table_row.relname=expected.table_name
          AND constraint_row.conname=left(expected.constraint_name, 63)
          AND constraint_row.contype='p');

    IF missing_columns IS NOT NULL OR missing_indexes IS NOT NULL OR missing_foreign_keys IS NOT NULL OR missing_primary_keys IS NOT NULL THEN
        RAISE EXCEPTION 'Facility reporting schema incomplete. Columns/tables: %; indexes: %; foreign keys: %; primary keys: %',
            coalesce(missing_columns, 'none'), coalesce(missing_indexes, 'none'),
            coalesce(missing_foreign_keys, 'none'), coalesce(missing_primary_keys, 'none');
    END IF;

    IF (SELECT COUNT(*) FROM "__EFMigrationsHistory"
        WHERE "MigrationId"='20260804052104_AddFacilityRunReporting' AND "ProductVersion"='9.0.9') <> 1 THEN
        RAISE EXCEPTION 'Facility reporting migration history row is missing or duplicated';
    END IF;
END $verify$;

SELECT 'application_object_state_ready' AS status,
       '20260804052104_AddFacilityRunReporting' AS migration;
ROLLBACK;

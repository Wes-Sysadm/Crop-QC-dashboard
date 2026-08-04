\set ON_ERROR_STOP on
\ir expected_lines.psql
\ir operational_fingerprints.psql
BEGIN TRANSACTION READ ONLY;

DO $preflight$
DECLARE
    entry_fingerprint text;
    run_fingerprint text;
    mismatch_count integer;
    protected record;
    actual_count bigint;
    actual_fingerprint text;
    missing_schema text;
    initial_state boolean;
    applied_state boolean;
BEGIN
    SELECT string_agg(expected.display_name, ', ' ORDER BY expected.display_name)
    INTO missing_schema
    FROM (VALUES
        ('UserEmploymentHistory', 'UserEmploymentHistory', NULL::text),
        ('Users.EmploymentFacility', 'Users', 'EmploymentFacility'),
        ('Users.EmploymentEffectiveAt', 'Users', 'EmploymentEffectiveAt'),
        ('Users.EmploymentUpdatedAt', 'Users', 'EmploymentUpdatedAt'),
        ('Users.EmploymentUpdatedByUserId', 'Users', 'EmploymentUpdatedByUserId'),
        ('ActualRuns.RunFacilityWarehouseId', 'ActualRuns', 'RunFacilityWarehouseId'),
        ('ActualRuns.RunFacilityCodeSnapshot', 'ActualRuns', 'RunFacilityCodeSnapshot'),
        ('ActualRuns.RunFacilityAssignmentSource', 'ActualRuns', 'RunFacilityAssignmentSource'),
        ('ActualRuns.RunFacilityAssignedAt', 'ActualRuns', 'RunFacilityAssignedAt'),
        ('ActualRuns.RunFacilityAssignedByUserId', 'ActualRuns', 'RunFacilityAssignedByUserId'),
        ('BinsRunEntries.ReportingFacilityWarehouseId', 'BinsRunEntries', 'ReportingFacilityWarehouseId'),
        ('BinsRunEntries.ReportingFacilityCodeSnapshot', 'BinsRunEntries', 'ReportingFacilityCodeSnapshot'),
        ('BinsRunEntries.ReportingFacilityAssignmentSource', 'BinsRunEntries', 'ReportingFacilityAssignmentSource'),
        ('BinsRunEntries.ReportingFacilityAssignedAt', 'BinsRunEntries', 'ReportingFacilityAssignedAt'),
        ('BinsRunEntries.ReportingFacilityAssignedByUserId', 'BinsRunEntries', 'ReportingFacilityAssignedByUserId'),
        ('BinsRunEntries.ReportingCropYearSnapshot', 'BinsRunEntries', 'ReportingCropYearSnapshot'),
        ('BinsRunEntries.ReportingFruitProfileIdSnapshot', 'BinsRunEntries', 'ReportingFruitProfileIdSnapshot'),
        ('BinsRunEntries.ReportingVarietyCodeSnapshot', 'BinsRunEntries', 'ReportingVarietyCodeSnapshot'),
        ('BinsRunEntries.ProductionTypeSnapshot', 'BinsRunEntries', 'ProductionTypeSnapshot'),
        ('BinsRunEntries.IsOrganicSnapshot', 'BinsRunEntries', 'IsOrganicSnapshot'),
        ('BinsRunEntries.GrowerNumberSnapshot', 'BinsRunEntries', 'GrowerNumberSnapshot')
    ) AS expected(display_name, table_name, column_name)
    WHERE (expected.column_name IS NULL
           AND to_regclass(format('%I.%I', current_schema(), expected.table_name)) IS NULL)
       OR (expected.column_name IS NOT NULL
           AND NOT EXISTS (
               SELECT 1
               FROM information_schema.columns
               WHERE table_schema = current_schema()
                 AND table_name = expected.table_name
                 AND column_name = expected.column_name));
    IF missing_schema IS NOT NULL THEN
        RAISE EXCEPTION 'Facility reporting schema is incomplete: %', missing_schema;
    END IF;

    IF (SELECT COUNT(*) FROM "BinsRunEntries") <> 39
       OR (SELECT COALESCE(SUM("BinsRun"), 0) FROM "BinsRunEntries") <> 8330 THEN
        RAISE EXCEPTION 'BinsRunEntries count or quantity differs from backup run 40';
    END IF;
    IF (SELECT COUNT(*) FROM "ActualRuns") <> 7 THEN
        RAISE EXCEPTION 'ActualRuns count differs from backup run 40';
    END IF;

    SELECT md5(string_agg(concat_ws('|', "Id", "ActualRunId", "ActualRunRevisionId", "TransactionType",
        "CreatedByUserId", "RunAt", "BinsRun", "CropYear", "ReceiptId", "SourceInventoryAdjustmentId",
        "InventoryAdjustmentId", "WarehouseId", "RoomId", "GrowerLotId", "FruitProfileId", "VarietyCode",
        "IsReversed"), E'\n' ORDER BY "Id"))
    INTO entry_fingerprint FROM "BinsRunEntries";
    IF entry_fingerprint <> 'e1bac9569bd47fc753b002780653e58b' THEN
        RAISE EXCEPTION 'BinsRunEntries operational fingerprint mismatch: %', entry_fingerprint;
    END IF;

    SELECT md5(string_agg(concat_ws('|', "Id", "CreatedByUserId", "RunAt", "Status", "CurrentRevisionNumber"),
        E'\n' ORDER BY "Id"))
    INTO run_fingerprint FROM "ActualRuns";
    IF run_fingerprint <> '586ff1bae3b6e559f185d18b701dc691' THEN
        RAISE EXCEPTION 'ActualRuns operational fingerprint mismatch: %', run_fingerprint;
    END IF;

    FOR protected IN SELECT * FROM expected_protected_operational_fingerprints ORDER BY table_name LOOP
        EXECUTE format(
            'SELECT count(*), md5(coalesce(string_agg(row_hash, '''' ORDER BY row_hash), '''')) FROM (SELECT md5(row_to_json(t)::text) AS row_hash FROM %I AS t) AS rows',
            protected.table_name)
        INTO actual_count, actual_fingerprint;
        IF actual_count <> protected.row_count OR actual_fingerprint <> protected.row_fingerprint THEN
            RAISE EXCEPTION 'Protected table % differs from backup run 40 (count %, fingerprint %)',
                protected.table_name, actual_count, actual_fingerprint;
        END IF;
    END LOOP;

    IF (SELECT COUNT(*) FROM "AuditLogs" WHERE "Id" <= 14415) <> 14415
       OR (SELECT md5(string_agg(md5(row_to_json(a)::text), '' ORDER BY md5(row_to_json(a)::text)))
           FROM "AuditLogs" AS a WHERE "Id" <= 14415) <> '0dd1c86e19af4edc5753826dfd1c9e38' THEN
        RAISE EXCEPTION 'Pre-existing audit records differ from backup run 40';
    END IF;

    IF (SELECT COUNT(*) FROM "__EFMigrationsHistory" WHERE "MigrationId" <> '20260804052104_AddFacilityRunReporting') <> 24
       OR (SELECT md5(string_agg(md5(row_to_json(h)::text), '' ORDER BY md5(row_to_json(h)::text)))
           FROM "__EFMigrationsHistory" AS h
           WHERE "MigrationId" <> '20260804052104_AddFacilityRunReporting') <> 'd8912e817bc0865e546536b46d14ae51'
       OR (SELECT COUNT(*) FROM "__EFMigrationsHistory"
           WHERE "MigrationId" = '20260804052104_AddFacilityRunReporting' AND "ProductVersion" = '9.0.9') <> 1 THEN
        RAISE EXCEPTION 'Migration history differs from the reviewed bounded compatibility state';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM expected_attribution_users AS expected
        LEFT JOIN "Users" AS actual ON actual."Id"=expected.user_id
        WHERE actual."Id" IS NULL
           OR actual."Email" IS DISTINCT FROM expected.email
           OR actual."DisplayName" IS DISTINCT FROM expected.display_name
           OR actual."IsActive" IS DISTINCT FROM true
    ) OR (SELECT COUNT(*) FROM expected_attribution_users) <> 2 THEN
        RAISE EXCEPTION 'Target reporting user identity or active status differs from backup run 40';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM expected_attribution_users AS expected
        JOIN "Users" AS actual ON actual."Id"=expected.user_id
        WHERE actual."EmploymentFacility" IS DISTINCT FROM 'Unassigned'
          AND actual."EmploymentFacility" IS DISTINCT FROM expected.facility_code
    ) THEN
        RAISE EXCEPTION 'A target reporting user has a conflicting Employment Facility';
    END IF;
    IF (SELECT COUNT(*) FROM "Warehouses" WHERE "Code"='WP' AND "Id"=4 AND "IsActive") <> 1
       OR (SELECT COUNT(*) FROM "Warehouses" WHERE "Code"='EBS' AND "Id"=1 AND "IsActive") <> 1
       OR (SELECT COUNT(*) FROM "Warehouses" WHERE "Code" IN ('WP','EBS')) <> 2 THEN
        RAISE EXCEPTION 'WP or EBS warehouse identity is missing, duplicated, or inactive';
    END IF;

    SELECT COUNT(*) INTO mismatch_count
    FROM expected_actual_run_facilities AS expected
    LEFT JOIN "ActualRuns" AS actual ON actual."Id"=expected.actual_run_id
    LEFT JOIN expected_attribution_users AS recording_user ON recording_user.user_id=actual."CreatedByUserId"
    WHERE actual."Id" IS NULL
       OR actual."CreatedByUserId" IS DISTINCT FROM expected.created_by_user_id
       OR recording_user.facility_code IS DISTINCT FROM expected.facility_code
       OR recording_user.warehouse_id IS DISTINCT FROM expected.warehouse_id;
    IF mismatch_count <> 0 OR (SELECT COUNT(*) FROM expected_actual_run_facilities) <> 7 THEN
        RAISE EXCEPTION 'Expected Actual Run identities or recording-user attribution differ from backup run 40';
    END IF;

    SELECT COUNT(*) INTO mismatch_count
    FROM expected_run_reporting_lines AS e
    JOIN "BinsRunEntries" AS b ON b."Id"=e.entry_id
    LEFT JOIN "RoomInventoryAdjustments" AS s ON s."Id"=b."SourceInventoryAdjustmentId"
    LEFT JOIN "Receipts" AS sr ON sr."Id"=s."ReceiptId"
    LEFT JOIN "Receipts" AS er ON er."Id"=b."ReceiptId"
    LEFT JOIN "FruitProfiles" AS fp ON fp."Id"=b."FruitProfileId"
    LEFT JOIN "BinsRunEntries" AS parent ON parent."InventoryAdjustmentId"=b."SourceInventoryAdjustmentId"
    LEFT JOIN "RoomInventoryAdjustments" AS parent_source ON parent_source."Id"=parent."SourceInventoryAdjustmentId"
    WHERE b."FruitProfileId" IS DISTINCT FROM e.fruit_profile_id
       OR b."VarietyCode" IS DISTINCT FROM e.variety_code
       OR fp."ProductionType" IS DISTINCT FROM e.production_type
       OR fp."IsOrganic" IS DISTINCT FROM e.is_organic
       OR COALESCE(b."CropYear", s."CropYear", sr."CropYear", er."CropYear", parent."CropYear", parent_source."CropYear") IS DISTINCT FROM e.crop_year
       OR COALESCE(er."GrowerNumber", sr."GrowerNumber") IS DISTINCT FROM e.grower_number
       OR b."CreatedByUserId" IS DISTINCT FROM e.created_by_user_id
       OR NOT EXISTS (
           SELECT 1 FROM expected_attribution_users AS recording_user
           WHERE recording_user.user_id=e.created_by_user_id
             AND recording_user.facility_code=e.facility_code)
       OR b."IsReversed";
    IF mismatch_count <> 0 OR (SELECT COUNT(*) FROM expected_run_reporting_lines) <> 11 THEN
        RAISE EXCEPTION 'Reviewed line-by-line metadata mismatch (% rows)', mismatch_count;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM "BinsRunEntries" AS b
        LEFT JOIN "RoomInventoryAdjustments" AS s ON s."Id"=b."SourceInventoryAdjustmentId"
        LEFT JOIN "Receipts" AS sr ON sr."Id"=s."ReceiptId"
        LEFT JOIN "Receipts" AS er ON er."Id"=b."ReceiptId"
        LEFT JOIN "BinsRunEntries" AS parent ON parent."InventoryAdjustmentId"=b."SourceInventoryAdjustmentId"
        LEFT JOIN "RoomInventoryAdjustments" AS parent_source ON parent_source."Id"=parent."SourceInventoryAdjustmentId"
        WHERE COALESCE(b."CropYear", s."CropYear", sr."CropYear", er."CropYear", parent."CropYear", parent_source."CropYear") >= 2026
          AND b."Id" NOT IN (SELECT entry_id FROM expected_run_reporting_lines)
          AND b."Id" <> 33
    ) THEN
        RAISE EXCEPTION 'An unreviewed authoritative-era line exists';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM "BinsRunEntries" AS b
        LEFT JOIN "RoomInventoryAdjustments" AS s ON s."Id"=b."SourceInventoryAdjustmentId"
        LEFT JOIN "Receipts" AS sr ON sr."Id"=s."ReceiptId"
        LEFT JOIN "Receipts" AS er ON er."Id"=b."ReceiptId"
        WHERE b."Id"=33 AND b."CreatedByUserId"=8 AND b."BinsRun"=173 AND b."CropYear"=2026
          AND COALESCE(er."GrowerNumber",sr."GrowerNumber") IS NULL
    ) THEN
        RAISE EXCEPTION 'Authoritative Needs Review line 33 no longer matches backup run 40';
    END IF;

    IF EXISTS (
        SELECT 1 FROM "BinsRunEntries"
        WHERE "Id" BETWEEN 1 AND 27
          AND ("ReportingFacilityWarehouseId" IS NOT NULL
            OR "ReportingFacilityCodeSnapshot" IS NOT NULL
            OR "ReportingCropYearSnapshot" IS NOT NULL
            OR "ReportingFruitProfileIdSnapshot" IS NOT NULL
            OR "ReportingVarietyCodeSnapshot" IS NOT NULL
            OR "ProductionTypeSnapshot" IS NOT NULL
            OR "IsOrganicSnapshot" IS NOT NULL
            OR "GrowerNumberSnapshot" IS NOT NULL)
    ) THEN
        RAISE EXCEPTION 'Pre-2026 reporting snapshots must remain untouched';
    END IF;

    SELECT
        NOT EXISTS (SELECT 1 FROM "Users" WHERE "Id" IN (2,8) AND
            ("EmploymentFacility" <> 'Unassigned' OR "EmploymentEffectiveAt" IS NOT NULL OR
             "EmploymentUpdatedAt" IS NOT NULL OR "EmploymentUpdatedByUserId" IS NOT NULL))
        AND NOT EXISTS (SELECT 1 FROM "UserEmploymentHistory")
        AND NOT EXISTS (SELECT 1 FROM expected_actual_run_facilities AS e JOIN "ActualRuns" AS a ON a."Id"=e.actual_run_id
            WHERE a."RunFacilityWarehouseId" IS NOT NULL OR a."RunFacilityCodeSnapshot" IS NOT NULL OR
                  a."RunFacilityAssignmentSource" IS NOT NULL OR a."RunFacilityAssignedAt" IS NOT NULL OR
                  a."RunFacilityAssignedByUserId" IS NOT NULL)
        AND NOT EXISTS (SELECT 1 FROM expected_run_reporting_lines AS e JOIN "BinsRunEntries" AS b ON b."Id"=e.entry_id
            WHERE b."ReportingFacilityWarehouseId" IS NOT NULL OR b."ReportingFacilityCodeSnapshot" IS NOT NULL OR
                  b."ReportingFacilityAssignmentSource" IS NOT NULL OR b."ReportingFacilityAssignedAt" IS NOT NULL OR
                  b."ReportingFacilityAssignedByUserId" IS NOT NULL OR b."ReportingCropYearSnapshot" IS NOT NULL OR
                  b."ReportingFruitProfileIdSnapshot" IS NOT NULL OR b."ReportingVarietyCodeSnapshot" IS NOT NULL OR
                  b."ProductionTypeSnapshot" IS NOT NULL OR b."IsOrganicSnapshot" IS NOT NULL OR b."GrowerNumberSnapshot" IS NOT NULL)
    INTO initial_state;

    SELECT
        NOT EXISTS (SELECT 1 FROM expected_attribution_users AS e JOIN "Users" AS u ON u."Id"=e.user_id
            WHERE u."EmploymentFacility" IS DISTINCT FROM e.facility_code OR u."EmploymentEffectiveAt" IS DISTINCT FROM e.effective_at)
        AND (SELECT COUNT(*) FROM expected_attribution_users AS e JOIN "UserEmploymentHistory" AS h ON h."UserId"=e.user_id
             WHERE h."EmploymentFacility"=e.facility_code AND h."EffectiveAt"=e.effective_at) = 2
        AND NOT EXISTS (SELECT 1 FROM expected_actual_run_facilities AS e JOIN "ActualRuns" AS a ON a."Id"=e.actual_run_id
            WHERE a."RunFacilityWarehouseId" IS DISTINCT FROM e.warehouse_id OR a."RunFacilityCodeSnapshot" IS DISTINCT FROM e.facility_code OR
                  a."RunFacilityAssignmentSource" IS DISTINCT FROM 'ReviewedProductionBackfill:20260804-run40')
        AND NOT EXISTS (SELECT 1 FROM expected_run_reporting_lines AS e JOIN "BinsRunEntries" AS b ON b."Id"=e.entry_id
            WHERE b."ReportingFacilityWarehouseId" IS DISTINCT FROM CASE e.facility_code WHEN 'WP' THEN 4 WHEN 'EBS' THEN 1 END OR
                  b."ReportingFacilityCodeSnapshot" IS DISTINCT FROM e.facility_code OR
                  b."ReportingFacilityAssignmentSource" IS DISTINCT FROM 'ReviewedProductionBackfill:20260804-run40' OR
                  b."ReportingCropYearSnapshot" IS DISTINCT FROM e.crop_year OR
                  b."ReportingFruitProfileIdSnapshot" IS DISTINCT FROM e.fruit_profile_id OR
                  b."ReportingVarietyCodeSnapshot" IS DISTINCT FROM e.variety_code OR
                  b."ProductionTypeSnapshot" IS DISTINCT FROM e.production_type OR
                  b."IsOrganicSnapshot" IS DISTINCT FROM e.is_organic OR
                  b."GrowerNumberSnapshot" IS DISTINCT FROM e.grower_number)
    INTO applied_state;

    IF NOT initial_state AND NOT applied_state THEN
        RAISE EXCEPTION 'Attribution state is neither the exact initial run-40 state nor the exact idempotent applied state';
    END IF;
END $preflight$;

SELECT facility_code, crop_year, COUNT(*) AS lines, SUM(b."BinsRun") AS bins
FROM expected_run_reporting_lines AS e
JOIN "BinsRunEntries" AS b ON b."Id"=e.entry_id
GROUP BY facility_code, crop_year
ORDER BY crop_year, facility_code;
ROLLBACK;

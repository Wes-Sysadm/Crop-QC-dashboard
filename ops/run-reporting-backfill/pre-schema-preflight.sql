\set ON_ERROR_STOP on
\ir expected_lines.psql
\ir operational_fingerprints.psql
BEGIN TRANSACTION READ ONLY;

DO $pre_schema_preflight$
DECLARE
    protected record;
    actual_count bigint;
    actual_fingerprint text;
    line_fingerprint text;
    run_fingerprint text;
    mismatch_count integer;
BEGIN
    IF (SELECT COUNT(*) FROM "BinsRunEntries") <> 39
       OR (SELECT COALESCE(SUM("BinsRun"),0) FROM "BinsRunEntries") <> 8330
       OR (SELECT COUNT(*) FROM "ActualRuns") <> 7 THEN
        RAISE EXCEPTION 'Run counts or quantities differ from backup run 40';
    END IF;

    SELECT md5(string_agg(concat_ws('|', "Id", "ActualRunId", "ActualRunRevisionId", "TransactionType",
        "CreatedByUserId", "RunAt", "BinsRun", "CropYear", "ReceiptId", "SourceInventoryAdjustmentId",
        "InventoryAdjustmentId", "WarehouseId", "RoomId", "GrowerLotId", "FruitProfileId", "VarietyCode",
        "IsReversed"), E'\n' ORDER BY "Id"))
    INTO line_fingerprint FROM "BinsRunEntries";
    SELECT md5(string_agg(concat_ws('|', "Id", "CreatedByUserId", "RunAt", "Status", "CurrentRevisionNumber"), E'\n' ORDER BY "Id"))
    INTO run_fingerprint FROM "ActualRuns";
    IF line_fingerprint <> 'e1bac9569bd47fc753b002780653e58b'
       OR run_fingerprint <> '586ff1bae3b6e559f185d18b701dc691' THEN
        RAISE EXCEPTION 'Run operational fingerprint differs from backup run 40';
    END IF;

    FOR protected IN SELECT * FROM expected_protected_operational_fingerprints ORDER BY table_name LOOP
        EXECUTE format(
            'SELECT count(*), md5(coalesce(string_agg(row_hash, '''' ORDER BY row_hash), '''')) FROM (SELECT md5(row_to_json(t)::text) AS row_hash FROM %I AS t) AS rows',
            protected.table_name)
        INTO actual_count, actual_fingerprint;
        IF actual_count <> protected.row_count OR actual_fingerprint <> protected.row_fingerprint THEN
            RAISE EXCEPTION 'Protected table % differs from backup run 40', protected.table_name;
        END IF;
    END LOOP;

    IF (SELECT COUNT(*) FROM "AuditLogs" WHERE "Id"<=14415) <> 14415
       OR (SELECT md5(string_agg(md5(row_to_json(a)::text), '' ORDER BY md5(row_to_json(a)::text))) FROM "AuditLogs" AS a WHERE "Id"<=14415) <> '0dd1c86e19af4edc5753826dfd1c9e38'
       OR (SELECT COUNT(*) FROM "__EFMigrationsHistory") <> 24
       OR (SELECT md5(string_agg(md5(row_to_json(h)::text), '' ORDER BY md5(row_to_json(h)::text))) FROM "__EFMigrationsHistory" AS h) <> 'd8912e817bc0865e546536b46d14ae51' THEN
        RAISE EXCEPTION 'Audit or migration-history state differs from backup run 40';
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

    IF (SELECT COUNT(*) FROM "Warehouses" WHERE "Id"=4 AND "Code"='WP' AND "IsActive") <> 1
       OR (SELECT COUNT(*) FROM "Warehouses" WHERE "Id"=1 AND "Code"='EBS' AND "IsActive") <> 1
       OR (SELECT COUNT(*) FROM "Warehouses" WHERE "Code" IN ('WP','EBS')) <> 2 THEN
        RAISE EXCEPTION 'WP or EBS warehouse identity, uniqueness, or active status differs from backup run 40';
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
       OR COALESCE(b."CropYear",s."CropYear",sr."CropYear",er."CropYear",parent."CropYear",parent_source."CropYear") IS DISTINCT FROM e.crop_year
       OR COALESCE(er."GrowerNumber",sr."GrowerNumber") IS DISTINCT FROM e.grower_number
       OR b."CreatedByUserId" IS DISTINCT FROM e.created_by_user_id
       OR NOT EXISTS (
           SELECT 1 FROM expected_attribution_users AS recording_user
           WHERE recording_user.user_id=e.created_by_user_id
             AND recording_user.facility_code=e.facility_code)
       OR b."IsReversed";
    IF mismatch_count <> 0 OR (SELECT COUNT(*) FROM expected_run_reporting_lines) <> 11 THEN
        RAISE EXCEPTION 'Expected authoritative lines differ from backup run 40';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM "BinsRunEntries" AS b
        LEFT JOIN "RoomInventoryAdjustments" AS s ON s."Id"=b."SourceInventoryAdjustmentId"
        LEFT JOIN "Receipts" AS sr ON sr."Id"=s."ReceiptId"
        LEFT JOIN "Receipts" AS er ON er."Id"=b."ReceiptId"
        LEFT JOIN "BinsRunEntries" AS parent ON parent."InventoryAdjustmentId"=b."SourceInventoryAdjustmentId"
        LEFT JOIN "RoomInventoryAdjustments" AS parent_source ON parent_source."Id"=parent."SourceInventoryAdjustmentId"
        WHERE COALESCE(b."CropYear",s."CropYear",sr."CropYear",er."CropYear",parent."CropYear",parent_source."CropYear") >= 2026
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
        WHERE b."Id"=33 AND b."BinsRun"=173 AND b."CropYear"=2026
          AND COALESCE(er."GrowerNumber",sr."GrowerNumber") IS NULL
    ) THEN
        RAISE EXCEPTION 'Needs Review line 33 differs from backup run 40';
    END IF;
END $pre_schema_preflight$;

SELECT facility_code, crop_year, COUNT(*) AS included_lines, SUM(b."BinsRun") AS included_bins
FROM expected_run_reporting_lines AS e
JOIN "BinsRunEntries" AS b ON b."Id"=e.entry_id
GROUP BY facility_code,crop_year
ORDER BY crop_year,facility_code;
ROLLBACK;

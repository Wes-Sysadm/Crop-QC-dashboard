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
    user_fingerprint text;
    mismatch_count integer;
BEGIN
    IF (SELECT COUNT(*) FROM "BinsRunEntries") <> 39
       OR (SELECT COALESCE(SUM("BinsRun"),0) FROM "BinsRunEntries") <> 8330
       OR (SELECT COUNT(*) FROM "ActualRuns") <> 7 THEN
        RAISE EXCEPTION 'Run counts or quantities differ from backup run 39';
    END IF;

    SELECT md5(string_agg(concat_ws('|', "Id", "ActualRunId", "ActualRunRevisionId", "TransactionType",
        "CreatedByUserId", "RunAt", "BinsRun", "CropYear", "ReceiptId", "SourceInventoryAdjustmentId",
        "InventoryAdjustmentId", "WarehouseId", "RoomId", "GrowerLotId", "FruitProfileId", "VarietyCode",
        "IsReversed"), E'\n' ORDER BY "Id"))
    INTO line_fingerprint FROM "BinsRunEntries";
    SELECT md5(string_agg(concat_ws('|', "Id", "CreatedByUserId", "RunAt", "Status", "CurrentRevisionNumber"), E'\n' ORDER BY "Id"))
    INTO run_fingerprint FROM "ActualRuns";
    SELECT md5(string_agg(md5(jsonb_build_object(
        'Id', "Id", 'Email', "Email", 'DisplayName', "DisplayName", 'PasswordHash', "PasswordHash",
        'PasswordLastChangedAt', "PasswordLastChangedAt", 'IsActive', "IsActive", 'CreatedAt', "CreatedAt",
        'UpdatedAt', "UpdatedAt", 'GoogleSubjectId', "GoogleSubjectId", 'Domain', "Domain",
        'LastLoginAt', "LastLoginAt")::text), '' ORDER BY "Id"))
    INTO user_fingerprint FROM "Users";
    IF line_fingerprint <> 'e1bac9569bd47fc753b002780653e58b'
       OR run_fingerprint <> '586ff1bae3b6e559f185d18b701dc691'
       OR user_fingerprint <> '71c814138ee478f021a6beac4121d46d' THEN
        RAISE EXCEPTION 'Run or user operational fingerprint differs from backup run 39';
    END IF;

    FOR protected IN SELECT * FROM expected_protected_operational_fingerprints ORDER BY table_name LOOP
        EXECUTE format(
            'SELECT count(*), md5(coalesce(string_agg(row_hash, '''' ORDER BY row_hash), '''')) FROM (SELECT md5(row_to_json(t)::text) AS row_hash FROM %I AS t) AS rows',
            protected.table_name)
        INTO actual_count, actual_fingerprint;
        IF actual_count <> protected.row_count OR actual_fingerprint <> protected.row_fingerprint THEN
            RAISE EXCEPTION 'Protected table % differs from backup run 39', protected.table_name;
        END IF;
    END LOOP;

    IF (SELECT COUNT(*) FROM "AuditLogs" WHERE "Id"<=13806) <> 13806
       OR (SELECT md5(string_agg(md5(row_to_json(a)::text), '' ORDER BY md5(row_to_json(a)::text))) FROM "AuditLogs" AS a WHERE "Id"<=13806) <> 'ca5228e0e4c66d09cd5d4e4d9406c229'
       OR (SELECT COUNT(*) FROM "__EFMigrationsHistory") <> 24
       OR (SELECT md5(string_agg(md5(row_to_json(h)::text), '' ORDER BY md5(row_to_json(h)::text))) FROM "__EFMigrationsHistory" AS h) <> 'd8912e817bc0865e546536b46d14ae51' THEN
        RAISE EXCEPTION 'Audit or migration-history state differs from backup run 39';
    END IF;

    IF (SELECT COUNT(*) FROM "Users" WHERE "Id"=8 AND "Email"='alexis@wp-packing.com' AND "DisplayName"='Alexis Ledezma') <> 1
       OR (SELECT COUNT(*) FROM "Users" WHERE "Id"=2 AND "Email"='rob@earlbrownandsons.com' AND "DisplayName"='Robert Fulgham') <> 1
       OR (SELECT COUNT(*) FROM "Warehouses" WHERE "Id"=4 AND "Code"='WP' AND "IsActive") <> 1
       OR (SELECT COUNT(*) FROM "Warehouses" WHERE "Id"=1 AND "Code"='EBS' AND "IsActive") <> 1
       OR (SELECT COUNT(*) FROM "Warehouses" WHERE "Code" IN ('WP','EBS')) <> 2 THEN
        RAISE EXCEPTION 'Exact user or warehouse identities differ from backup run 39';
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
       OR (e.facility_code='WP' AND b."CreatedByUserId"<>8)
       OR (e.facility_code='EBS' AND b."CreatedByUserId"<>2)
       OR b."IsReversed";
    IF mismatch_count <> 0 OR (SELECT COUNT(*) FROM expected_run_reporting_lines) <> 11 THEN
        RAISE EXCEPTION 'Expected authoritative lines differ from backup run 39';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM "BinsRunEntries" AS b
        LEFT JOIN "RoomInventoryAdjustments" AS s ON s."Id"=b."SourceInventoryAdjustmentId"
        LEFT JOIN "Receipts" AS sr ON sr."Id"=s."ReceiptId"
        LEFT JOIN "Receipts" AS er ON er."Id"=b."ReceiptId"
        WHERE b."Id"=33 AND b."BinsRun"=173 AND b."CropYear"=2026
          AND COALESCE(er."GrowerNumber",sr."GrowerNumber") IS NULL
    ) THEN
        RAISE EXCEPTION 'Needs Review line 33 differs from backup run 39';
    END IF;
END $pre_schema_preflight$;

SELECT facility_code, crop_year, COUNT(*) AS included_lines, SUM(b."BinsRun") AS included_bins
FROM expected_run_reporting_lines AS e
JOIN "BinsRunEntries" AS b ON b."Id"=e.entry_id
GROUP BY facility_code,crop_year
ORDER BY crop_year,facility_code;
ROLLBACK;

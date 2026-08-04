\set ON_ERROR_STOP on
\ir expected_lines.psql
BEGIN TRANSACTION READ ONLY;

DO $preflight$
DECLARE
    entry_fingerprint text;
    run_fingerprint text;
    mismatch_count integer;
BEGIN
    IF (SELECT COUNT(*) FROM "BinsRunEntries") <> 36
       OR (SELECT COALESCE(SUM("BinsRun"), 0) FROM "BinsRunEntries") <> 7949 THEN
        RAISE EXCEPTION 'BinsRunEntries count or quantity differs from reviewed backup';
    END IF;
    IF (SELECT COUNT(*) FROM "ActualRuns") <> 5 THEN
        RAISE EXCEPTION 'ActualRuns count differs from reviewed backup';
    END IF;

    SELECT md5(string_agg(concat_ws('|', "Id", "ActualRunId", "ActualRunRevisionId", "TransactionType",
        "CreatedByUserId", "RunAt", "BinsRun", "CropYear", "ReceiptId", "SourceInventoryAdjustmentId",
        "InventoryAdjustmentId", "WarehouseId", "RoomId", "GrowerLotId", "FruitProfileId", "VarietyCode",
        "IsReversed"), E'\n' ORDER BY "Id"))
    INTO entry_fingerprint FROM "BinsRunEntries";
    IF entry_fingerprint <> '5fc5b726cdfd8e790fc677c1e428bd9c' THEN
        RAISE EXCEPTION 'BinsRunEntries fingerprint mismatch: %', entry_fingerprint;
    END IF;

    SELECT md5(string_agg(concat_ws('|', "Id", "CreatedByUserId", "RunAt", "Status", "CurrentRevisionNumber"),
        E'\n' ORDER BY "Id"))
    INTO run_fingerprint FROM "ActualRuns";
    IF run_fingerprint <> '31f6479fcb68766eefb0ed1c24044d72' THEN
        RAISE EXCEPTION 'ActualRuns fingerprint mismatch: %', run_fingerprint;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM "Users" WHERE "Id"=8 AND "Email"='alexis@wp-packing.com' AND "DisplayName"='Alexis Ledezma')
       OR NOT EXISTS (SELECT 1 FROM "Users" WHERE "Id"=2 AND "Email"='rob@earlbrownandsons.com' AND "DisplayName"='Robert Fulgham') THEN
        RAISE EXCEPTION 'Reviewed Alexis or Robert identity does not match';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM "Warehouses" WHERE "Id"=4 AND "Code"='WP')
       OR NOT EXISTS (SELECT 1 FROM "Warehouses" WHERE "Id"=1 AND "Code"='EBS') THEN
        RAISE EXCEPTION 'Reviewed WP or EBS warehouse identity does not match';
    END IF;

    SELECT COUNT(*) INTO mismatch_count
    FROM expected_run_reporting_lines e
    JOIN "BinsRunEntries" b ON b."Id"=e.entry_id
    LEFT JOIN "RoomInventoryAdjustments" s ON s."Id"=b."SourceInventoryAdjustmentId"
    LEFT JOIN "Receipts" sr ON sr."Id"=s."ReceiptId"
    LEFT JOIN "Receipts" er ON er."Id"=b."ReceiptId"
    LEFT JOIN "GrowerLots" gl ON gl."Id"=b."GrowerLotId"
    LEFT JOIN "FruitProfiles" fp ON fp."Id"=b."FruitProfileId"
    LEFT JOIN "BinsRunEntries" parent ON parent."InventoryAdjustmentId"=b."SourceInventoryAdjustmentId"
    LEFT JOIN "RoomInventoryAdjustments" parent_source ON parent_source."Id"=parent."SourceInventoryAdjustmentId"
    WHERE b."FruitProfileId" IS DISTINCT FROM e.fruit_profile_id
       OR b."VarietyCode" IS DISTINCT FROM e.variety_code
       OR fp."ProductionType" IS DISTINCT FROM e.production_type
       OR fp."IsOrganic" IS DISTINCT FROM e.is_organic
       OR COALESCE(b."CropYear", s."CropYear", sr."CropYear", er."CropYear", parent."CropYear", parent_source."CropYear") IS DISTINCT FROM e.crop_year
       OR COALESCE(er."GrowerNumber", sr."GrowerNumber", gl."LotNumber") IS DISTINCT FROM e.grower_number
       OR (e.facility_code='WP' AND b."CreatedByUserId"<>8)
       OR (e.facility_code='EBS' AND b."CreatedByUserId"<>2)
       OR (e.facility_code IS NULL AND b."CreatedByUserId" IN (2,8));
    IF mismatch_count <> 0 OR (SELECT COUNT(*) FROM expected_run_reporting_lines) <> 36 THEN
        RAISE EXCEPTION 'Reviewed line-by-line metadata mismatch (% rows)', mismatch_count;
    END IF;

    IF EXISTS (
        SELECT 1 FROM expected_run_reporting_lines e
        JOIN "BinsRunEntries" b ON b."Id"=e.entry_id
        LEFT JOIN "RoomInventoryAdjustments" s ON s."Id"=b."SourceInventoryAdjustmentId"
        LEFT JOIN "Receipts" sr ON sr."Id"=s."ReceiptId"
        LEFT JOIN "Receipts" er ON er."Id"=b."ReceiptId"
        LEFT JOIN "GrowerLots" gl ON gl."Id"=b."GrowerLotId"
        WHERE array_length(array_remove(ARRAY[er."GrowerNumber",sr."GrowerNumber",gl."LotNumber"],NULL),1) > 1
          AND (SELECT COUNT(DISTINCT n) FROM unnest(array_remove(ARRAY[er."GrowerNumber",sr."GrowerNumber",gl."LotNumber"],NULL)) n) > 1
    ) THEN
        RAISE EXCEPTION 'At least one grower number has conflicting authoritative sources';
    END IF;

    IF EXISTS (SELECT 1 FROM "Users" WHERE "Id" IN (2,8) AND "EmploymentFacility" NOT IN ('Unassigned','WP','EBS')) THEN
        RAISE EXCEPTION 'Reviewed user already has an unexpected employment assignment';
    END IF;
END $preflight$;

SELECT facility_code, crop_year, COUNT(*) AS lines, SUM(b."BinsRun") AS bins
FROM expected_run_reporting_lines e JOIN "BinsRunEntries" b ON b."Id"=e.entry_id
GROUP BY facility_code, crop_year ORDER BY crop_year, facility_code NULLS LAST;
ROLLBACK;

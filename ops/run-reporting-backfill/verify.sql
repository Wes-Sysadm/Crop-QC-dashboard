\set ON_ERROR_STOP on
\ir expected_lines.psql
BEGIN TRANSACTION READ ONLY;

DO $verify$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM "Users" WHERE "Id"=8 AND "EmploymentFacility"='WP')
       OR NOT EXISTS (SELECT 1 FROM "Users" WHERE "Id"=2 AND "EmploymentFacility"='EBS') THEN
        RAISE EXCEPTION 'Employment assignments were not applied exactly';
    END IF;
    IF EXISTS (
        SELECT 1 FROM expected_run_reporting_lines e JOIN "BinsRunEntries" b ON b."Id"=e.entry_id
        WHERE b."ReportingFacilityWarehouseId" IS DISTINCT FROM CASE e.facility_code WHEN 'WP' THEN 4 WHEN 'EBS' THEN 1 END
           OR b."ReportingFacilityCodeSnapshot" IS DISTINCT FROM e.facility_code
           OR b."ReportingCropYearSnapshot" IS DISTINCT FROM e.crop_year
           OR b."ReportingFruitProfileIdSnapshot" IS DISTINCT FROM e.fruit_profile_id
           OR b."ReportingVarietyCodeSnapshot" IS DISTINCT FROM e.variety_code
           OR b."ProductionTypeSnapshot" IS DISTINCT FROM e.production_type
           OR b."IsOrganicSnapshot" IS DISTINCT FROM e.is_organic
           OR b."GrowerNumberSnapshot" IS DISTINCT FROM e.grower_number
    ) THEN RAISE EXCEPTION 'A reporting line does not match the reviewed classification'; END IF;
    IF EXISTS (
        SELECT 1 FROM "ActualRuns" a
        WHERE (a."Id" IN (1,2,3,4) AND (a."RunFacilityWarehouseId"<>4 OR a."RunFacilityCodeSnapshot"<>'WP'))
           OR (a."Id"=5 AND (a."RunFacilityWarehouseId"<>1 OR a."RunFacilityCodeSnapshot"<>'EBS'))
    ) THEN RAISE EXCEPTION 'An Actual Run facility does not match the reviewed classification'; END IF;
    IF (SELECT COUNT(*) FROM "AuditLogs" WHERE "Action"='ReviewedEmploymentBackfill' AND "EntityKey" IN ('2','8')) < 2
       OR (SELECT COUNT(*) FROM "AuditLogs" WHERE "Action"='ReviewedRunFacilityBackfill' AND "EntityKey" IN ('1','2','3','4','5')) < 5
       OR (SELECT COUNT(*) FROM "AuditLogs" WHERE "Action"='ReviewedRunReportingBackfill' AND "EntityKey"::bigint IN (28,29,30,31,32,34,35,36)) < 8 THEN
        RAISE EXCEPTION 'Required attribution audits are missing';
    END IF;
    IF EXISTS (
        SELECT 1 FROM "BinsRunEntries" WHERE "Id" BETWEEN 1 AND 27
          AND ("ReportingFacilityWarehouseId" IS NOT NULL
            OR "ReportingFacilityCodeSnapshot" IS NOT NULL
            OR "ReportingCropYearSnapshot" IS NOT NULL
            OR "ReportingFruitProfileIdSnapshot" IS NOT NULL
            OR "ReportingVarietyCodeSnapshot" IS NOT NULL
            OR "ProductionTypeSnapshot" IS NOT NULL
            OR "IsOrganicSnapshot" IS NOT NULL
            OR "GrowerNumberSnapshot" IS NOT NULL)
    ) THEN RAISE EXCEPTION 'Pre-2026 records were changed'; END IF;
    IF EXISTS (
        SELECT 1 FROM "BinsRunEntries" WHERE "Id"=33
          AND ("ReportingCropYearSnapshot" IS NOT NULL OR "GrowerNumberSnapshot" IS NOT NULL)
    ) THEN RAISE EXCEPTION 'Unresolved authoritative line 33 must remain excluded for Needs Review'; END IF;
    IF (SELECT md5(string_agg(concat_ws('|', "Id", "ActualRunId", "ActualRunRevisionId", "TransactionType",
        "CreatedByUserId", "RunAt", "BinsRun", "CropYear", "ReceiptId", "SourceInventoryAdjustmentId",
        "InventoryAdjustmentId", "WarehouseId", "RoomId", "GrowerLotId", "FruitProfileId", "VarietyCode",
        "IsReversed"), E'\n' ORDER BY "Id")) FROM "BinsRunEntries") <> '5fc5b726cdfd8e790fc677c1e428bd9c' THEN
        RAISE EXCEPTION 'Operational Bins Run fingerprint changed';
    END IF;
    IF (SELECT md5(string_agg(concat_ws('|', "Id", "CreatedByUserId", "RunAt", "Status", "CurrentRevisionNumber"),
        E'\n' ORDER BY "Id")) FROM "ActualRuns") <> '31f6479fcb68766eefb0ed1c24044d72' THEN
        RAISE EXCEPTION 'Operational Actual Run fingerprint changed';
    END IF;
END $verify$;

SELECT facility_code, crop_year, COUNT(*) AS included_lines, SUM(b."BinsRun") AS included_bins
FROM expected_run_reporting_lines e JOIN "BinsRunEntries" b ON b."Id"=e.entry_id
WHERE facility_code IS NOT NULL
GROUP BY facility_code,crop_year ORDER BY crop_year,facility_code;
SELECT 'Missing authoritative grower number' AS issue_type, COUNT(*) AS records, SUM("BinsRun") AS excluded_bins
FROM "BinsRunEntries" WHERE "Id"=33;
ROLLBACK;

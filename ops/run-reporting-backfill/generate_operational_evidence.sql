\set ON_ERROR_STOP on

-- Run this only against the untouched restore of the current production backup.
-- It intentionally does not fingerprint the complete Users table. Authentication,
-- session, and unrelated provisioning changes are outside the reporting package.

SELECT 'target_user' AS evidence_type,
       u."Id"::text AS key,
       concat_ws('|', u."Email", u."DisplayName", u."IsActive") AS value
FROM "Users" AS u
WHERE u."Id" IN (2, 8)
ORDER BY u."Id";

SELECT 'bins_operational' AS evidence_type,
       COUNT(*)::text AS row_count,
       COALESCE(SUM("BinsRun"), 0)::text AS quantity,
       md5(COALESCE(string_agg(concat_ws('|', "Id", "ActualRunId", "ActualRunRevisionId", "TransactionType",
           "CreatedByUserId", "RunAt", "BinsRun", "CropYear", "ReceiptId", "SourceInventoryAdjustmentId",
           "InventoryAdjustmentId", "WarehouseId", "RoomId", "GrowerLotId", "FruitProfileId", "VarietyCode",
           "IsReversed"), E'\n' ORDER BY "Id"), '')) AS fingerprint
FROM "BinsRunEntries";

SELECT 'actual_runs_operational' AS evidence_type,
       COUNT(*)::text AS row_count,
       md5(COALESCE(string_agg(concat_ws('|', "Id", "CreatedByUserId", "RunAt", "Status", "CurrentRevisionNumber"),
           E'\n' ORDER BY "Id"), '')) AS fingerprint
FROM "ActualRuns";

SELECT 'audit_prefix' AS evidence_type,
       MAX("Id")::text AS cutoff_id,
       COUNT(*)::text AS row_count,
       md5(COALESCE(string_agg(md5(row_to_json(a)::text), '' ORDER BY md5(row_to_json(a)::text)), '')) AS fingerprint
FROM "AuditLogs" AS a;

SELECT 'migration_history' AS evidence_type,
       COUNT(*)::text AS row_count,
       md5(COALESCE(string_agg(md5(row_to_json(h)::text), '' ORDER BY md5(row_to_json(h)::text)), '')) AS fingerprint
FROM "__EFMigrationsHistory" AS h;

SELECT 'expected_line' AS evidence_type,
       b."Id"::text AS entry_id,
       b."CreatedByUserId"::text AS created_by_user_id,
       b."BinsRun"::text AS bins,
       COALESCE(b."CropYear", source_adjustment."CropYear", source_receipt."CropYear", entry_receipt."CropYear",
           parent_entry."CropYear", parent_source."CropYear")::text AS crop_year,
       b."FruitProfileId"::text AS fruit_profile_id,
       b."VarietyCode" AS variety_code,
       fruit_profile."ProductionType" AS production_type,
       fruit_profile."IsOrganic"::text AS is_organic,
       COALESCE(entry_receipt."GrowerNumber", source_receipt."GrowerNumber") AS grower_number,
       b."IsReversed"::text AS is_reversed
FROM "BinsRunEntries" AS b
LEFT JOIN "RoomInventoryAdjustments" AS source_adjustment ON source_adjustment."Id" = b."SourceInventoryAdjustmentId"
LEFT JOIN "Receipts" AS source_receipt ON source_receipt."Id" = source_adjustment."ReceiptId"
LEFT JOIN "Receipts" AS entry_receipt ON entry_receipt."Id" = b."ReceiptId"
LEFT JOIN "FruitProfiles" AS fruit_profile ON fruit_profile."Id" = b."FruitProfileId"
LEFT JOIN "BinsRunEntries" AS parent_entry ON parent_entry."InventoryAdjustmentId" = b."SourceInventoryAdjustmentId"
LEFT JOIN "RoomInventoryAdjustments" AS parent_source ON parent_source."Id" = parent_entry."SourceInventoryAdjustmentId"
WHERE COALESCE(b."CropYear", source_adjustment."CropYear", source_receipt."CropYear", entry_receipt."CropYear",
          parent_entry."CropYear", parent_source."CropYear") >= 2026
ORDER BY b."Id";

SELECT 'expected_actual_run' AS evidence_type,
       a."Id"::text AS actual_run_id,
       a."CreatedByUserId"::text AS created_by_user_id,
       a."RunAt"::text AS run_at,
       a."Status" AS status,
       a."CurrentRevisionNumber"::text AS current_revision_number
FROM "ActualRuns" AS a
ORDER BY a."Id";

SELECT table_name, row_count, row_fingerprint
FROM (
    SELECT 'ActualRunOverrideRequestLines', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "ActualRunOverrideRequestLines" AS t
    UNION ALL SELECT 'ActualRunOverrideRequests', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "ActualRunOverrideRequests" AS t
    UNION ALL SELECT 'ActualRunRevisions', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "ActualRunRevisions" AS t
    UNION ALL SELECT 'GrowerLots', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "GrowerLots" AS t
    UNION ALL SELECT 'PackoutAnalysisConfigurations', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "PackoutAnalysisConfigurations" AS t
    UNION ALL SELECT 'PackoutEmailAttempts', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "PackoutEmailAttempts" AS t
    UNION ALL SELECT 'PackoutReportLines', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "PackoutReportLines" AS t
    UNION ALL SELECT 'PackoutReportSources', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "PackoutReportSources" AS t
    UNION ALL SELECT 'PackoutRuns', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "PackoutRuns" AS t
    UNION ALL SELECT 'PackoutSourceAllocations', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "PackoutSourceAllocations" AS t
    UNION ALL SELECT 'QcFruitDefects', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "QcFruitDefects" AS t
    UNION ALL SELECT 'QcFruitReadings', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "QcFruitReadings" AS t
    UNION ALL SELECT 'QcPhotos', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "QcPhotos" AS t
    UNION ALL SELECT 'QcSamples', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "QcSamples" AS t
    UNION ALL SELECT 'QcSummaryEmailLogs', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "QcSummaryEmailLogs" AS t
    UNION ALL SELECT 'Receipts', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "Receipts" AS t
    UNION ALL SELECT 'RoomDepletions', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "RoomDepletions" AS t
    UNION ALL SELECT 'RoomInventoryAdjustments', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "RoomInventoryAdjustments" AS t
    UNION ALL SELECT 'Rooms', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "Rooms" AS t
    UNION ALL SELECT 'RoomTransfers', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "RoomTransfers" AS t
    UNION ALL SELECT 'RunExpectations', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "RunExpectations" AS t
    UNION ALL SELECT 'RunExpectationSources', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "RunExpectationSources" AS t
    UNION ALL SELECT 'RunProjectionGradeResults', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "RunProjectionGradeResults" AS t
    UNION ALL SELECT 'RunProjections', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "RunProjections" AS t
    UNION ALL SELECT 'RunProjectionSizeResults', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "RunProjectionSizeResults" AS t
    UNION ALL SELECT 'RunProjectionSources', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "RunProjectionSources" AS t
    UNION ALL SELECT 'Warehouses', COUNT(*), md5(COALESCE(string_agg(md5(row_to_json(t)::text), '' ORDER BY md5(row_to_json(t)::text)), '')) FROM "Warehouses" AS t
) AS protected(table_name, row_count, row_fingerprint)
ORDER BY table_name;

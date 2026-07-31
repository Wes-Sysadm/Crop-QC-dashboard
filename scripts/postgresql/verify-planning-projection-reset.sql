\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

SELECT COUNT(*) AS active_projection_count
FROM "RunProjections"
WHERE NOT "IsDeleted";

SELECT
    COUNT(*) AS archived_by_reset,
    MIN("CreatedAt") AS first_reset_audit,
    MAX("CreatedAt") AS last_reset_audit
FROM "AuditLogs"
WHERE "Action" = 'ArchiveInvalidLegacyPlanningProjection'
  AND "SourceApplication" = 'CropQc.ProjectionReset';

SELECT
    (SELECT COUNT(*) FROM "ActualRuns") AS actual_runs,
    (SELECT COUNT(*) FROM "BinsRunEntries") AS bins_run_entries,
    (SELECT COUNT(*) FROM "RoomInventoryAdjustments") AS room_adjustments,
    (SELECT COUNT(*) FROM "QcSamples") AS qc_samples,
    (SELECT COUNT(*) FROM "Receipts") AS receipts;

-- Must be zero: reset audit rows without a retained projection record.
SELECT COUNT(*) AS missing_archived_projection_rows
FROM "AuditLogs" a
LEFT JOIN "RunProjections" p ON p."Id"::text = a."EntityKey"
WHERE a."Action" = 'ArchiveInvalidLegacyPlanningProjection'
  AND a."SourceApplication" = 'CropQc.ProjectionReset'
  AND p."Id" IS NULL;

ROLLBACK;

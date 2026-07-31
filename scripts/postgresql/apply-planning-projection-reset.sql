\set ON_ERROR_STOP on

-- OPERATOR GATE:
-- 1. A current backup has been verified readable and restorable.
-- 2. preflight-planning-projection-reset.sql output has been approved.
-- 3. The user has separately authorized the production reset.
--
-- This script archives Planning Projections only. It does not delete dependent
-- rows or touch Actual Runs, Bins Runs, room inventory, receipts, QC samples,
-- Grower Lots, fruit profiles, or packout files.
BEGIN;
LOCK TABLE "RunProjections" IN SHARE ROW EXCLUSIVE MODE;

INSERT INTO "AuditLogs"
(
    "Action",
    "EntityName",
    "EntityKey",
    "BeforeValuesJson",
    "AfterValuesJson",
    "SourceApplication",
    "CreatedAt"
)
SELECT
    'ArchiveInvalidLegacyPlanningProjection',
    'RunProjection',
    p."Id"::text,
    jsonb_build_object(
        'Status', p."Status",
        'IsDeleted', p."IsDeleted",
        'Name', p."Name",
        'PlannedRunDate', p."PlannedRunDate"
    )::text,
    jsonb_build_object(
        'Status', 'Cancelled',
        'IsDeleted', true,
        'Reason', 'Planning Projection model reset after domain separation'
    )::text,
    'CropQc.ProjectionReset',
    CURRENT_TIMESTAMP
FROM "RunProjections" p
WHERE NOT p."IsDeleted";

UPDATE "RunProjections"
SET
    "Status" = 'Cancelled',
    "IsDeleted" = true,
    "DeletedAt" = CURRENT_TIMESTAMP,
    "DeletionReason" = 'Planning Projection model reset after domain separation',
    "UpdatedAt" = CURRENT_TIMESTAMP,
    "ConcurrencyVersion" = "ConcurrencyVersion" + 1
WHERE NOT "IsDeleted";

COMMIT;

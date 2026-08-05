\set ON_ERROR_STOP on
\ir verify-facility-run-reporting.sql

START TRANSACTION;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '10min';
SELECT pg_advisory_xact_lock(hashtextextended('CropQc:20260805014812_AddReceiptInventoryOverrides', 0));

DO $precheck$
DECLARE
    target_recorded boolean;
    existing_target_count integer;
BEGIN
    SELECT EXISTS (
        SELECT 1 FROM "__EFMigrationsHistory"
        WHERE "MigrationId"='20260805014812_AddReceiptInventoryOverrides'
    ) INTO target_recorded;

    SELECT
        (CASE WHEN to_regclass(format('%I.%I', current_schema(), 'ReceiptInventoryOverrides')) IS NULL THEN 0 ELSE 1 END)
        + (SELECT COUNT(*) FROM information_schema.columns
           WHERE table_schema=current_schema()
             AND ((table_name='Receipts' AND column_name='ConcurrencyVersion')
               OR (table_name='RoomInventoryAdjustments' AND column_name='ReceiptInventoryOverrideId')))
        + (SELECT COUNT(*) FROM pg_class AS c JOIN pg_namespace AS n ON n.oid=c.relnamespace
           WHERE n.nspname=current_schema() AND c.relkind='i'
             AND c.relname IN ('IX_RoomInventoryAdjustments_ReceiptInventoryOverrideId',
                               'IX_ReceiptInventoryOverrides_AdministratorUserId',
                               'IX_ReceiptInventoryOverrides_OperationKey',
                               'IX_ReceiptInventoryOverrides_ReceiptId_CreatedAt'))
        + (SELECT COUNT(*) FROM pg_constraint
           WHERE connamespace=current_schema()::regnamespace
             AND conname IN ('FK_ReceiptInventoryOverrides_Receipts_ReceiptId',
                             'FK_ReceiptInventoryOverrides_Users_AdministratorUserId',
                             'FK_RoomInventoryAdjustments_ReceiptOverrides_OverrideId'))
    INTO existing_target_count;

    IF target_recorded AND existing_target_count <> 10 THEN
        RAISE EXCEPTION 'Migration history records receipt inventory overrides but the target object set is incomplete (% of 10). Transaction rolled back.', existing_target_count;
    END IF;
    IF NOT target_recorded AND existing_target_count <> 0 THEN
        RAISE EXCEPTION 'Unexpected partial receipt-inventory-override schema detected (% of 10 target objects). Transaction rolled back.', existing_target_count;
    END IF;
END $precheck$;

SELECT EXISTS (
    SELECT 1 FROM "__EFMigrationsHistory"
    WHERE "MigrationId"='20260805014812_AddReceiptInventoryOverrides'
) AS migration_already_applied \gset

\if :migration_already_applied
\echo 'Receipt inventory override schema is already recorded; verifying exact object state without mutation.'
\else
ALTER TABLE "RoomInventoryAdjustments" ADD "ReceiptInventoryOverrideId" uuid;
ALTER TABLE "Receipts" ADD "ConcurrencyVersion" bigint NOT NULL DEFAULT 0;

CREATE TABLE "ReceiptInventoryOverrides" (
    "Id" uuid NOT NULL,
    "ReceiptId" bigint NOT NULL,
    "ActionType" character varying(50) NOT NULL,
    "OldReceiptBinCount" integer NOT NULL,
    "NewReceiptBinCount" integer NOT NULL,
    "InventoryDelta" integer NOT NULL,
    "CurrentInventoryBefore" integer NOT NULL,
    "CurrentInventoryAfter" integer NOT NULL,
    "AdministratorUserId" integer NOT NULL,
    "Reason" character varying(1000) NOT NULL,
    "OperationKey" character varying(150) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "NegativeInventoryAcknowledged" boolean NOT NULL,
    "VoidConfirmationDetails" character varying(1000),
    "BeforeReceiptSnapshotJson" text NOT NULL,
    "AfterReceiptSnapshotJson" text NOT NULL,
    "AffectedInventorySnapshotJson" text NOT NULL,
    "ExpectedAdjustmentCount" integer NOT NULL,
    "IsComplete" boolean NOT NULL,
    CONSTRAINT "PK_ReceiptInventoryOverrides" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ReceiptInventoryOverrides_Receipts_ReceiptId" FOREIGN KEY ("ReceiptId") REFERENCES "Receipts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ReceiptInventoryOverrides_Users_AdministratorUserId" FOREIGN KEY ("AdministratorUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_RoomInventoryAdjustments_ReceiptInventoryOverrideId" ON "RoomInventoryAdjustments" ("ReceiptInventoryOverrideId");
CREATE INDEX "IX_ReceiptInventoryOverrides_AdministratorUserId" ON "ReceiptInventoryOverrides" ("AdministratorUserId");
CREATE UNIQUE INDEX "IX_ReceiptInventoryOverrides_OperationKey" ON "ReceiptInventoryOverrides" ("OperationKey");
CREATE INDEX "IX_ReceiptInventoryOverrides_ReceiptId_CreatedAt" ON "ReceiptInventoryOverrides" ("ReceiptId", "CreatedAt");

ALTER TABLE "RoomInventoryAdjustments" ADD CONSTRAINT "FK_RoomInventoryAdjustments_ReceiptOverrides_OverrideId"
    FOREIGN KEY ("ReceiptInventoryOverrideId") REFERENCES "ReceiptInventoryOverrides" ("Id") ON DELETE RESTRICT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260805014812_AddReceiptInventoryOverrides', '9.0.9');
\endif

COMMIT;
\ir verify-receipt-inventory-override.sql

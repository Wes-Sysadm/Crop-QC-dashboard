\set ON_ERROR_STOP on
\ir preflight-inventory-identity-corrections.sql

BEGIN;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '10min';
SELECT pg_advisory_xact_lock(hashtextextended('CropQc:20260902011217_AddInventoryIdentityCorrections', 0));

DO $apply$
BEGIN
    IF to_regclass(format('%I.%I', current_schema(), 'InventoryIdentityCorrections')) IS NULL THEN
        CREATE TABLE "InventoryIdentityCorrections" (
            "Id" uuid NOT NULL,
            "OperationKey" character varying(150) NOT NULL,
            "SourceCropYear" integer NOT NULL,
            "SourceGrowerLotId" integer NOT NULL,
            "SourceFruitProfileId" integer NOT NULL,
            "TargetCropYear" integer NOT NULL,
            "TargetGrowerLotId" integer NOT NULL,
            "TargetFruitProfileId" integer NOT NULL,
            "CorrectedReceiptId" bigint NULL,
            "ReceiptInventoryOverrideId" uuid NULL,
            "Reason" character varying(1000) NOT NULL,
            "CreatedByUserId" integer NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "SourceIdentitySnapshotJson" text NOT NULL,
            "TargetIdentitySnapshotJson" text NOT NULL,
            "ExpectedAdjustmentCount" integer NOT NULL,
            "ExpectedTreatmentMovementCount" integer NOT NULL,
            "IsComplete" boolean NOT NULL,
            "IsActive" boolean NOT NULL,
            CONSTRAINT "PK_InventoryIdentityCorrections" PRIMARY KEY ("Id"),
            CONSTRAINT "CK_InventoryIdentityCorrections_PositiveCropYears" CHECK ("SourceCropYear" > 0 AND "TargetCropYear" > 0),
            CONSTRAINT "CK_InventoryIdentityCorrections_NonSelf" CHECK ("SourceCropYear" <> "TargetCropYear" OR "SourceGrowerLotId" <> "TargetGrowerLotId" OR "SourceFruitProfileId" <> "TargetFruitProfileId"),
            CONSTRAINT "FK_InventoryIdentityCorrections_FruitProfiles_SourceFruitProfileId" FOREIGN KEY ("SourceFruitProfileId") REFERENCES "FruitProfiles" ("Id") ON DELETE RESTRICT,
            CONSTRAINT "FK_InventoryIdentityCorrections_FruitProfiles_TargetFruitProfileId" FOREIGN KEY ("TargetFruitProfileId") REFERENCES "FruitProfiles" ("Id") ON DELETE RESTRICT,
            CONSTRAINT "FK_InventoryIdentityCorrections_GrowerLots_SourceGrowerLotId" FOREIGN KEY ("SourceGrowerLotId") REFERENCES "GrowerLots" ("Id") ON DELETE RESTRICT,
            CONSTRAINT "FK_InventoryIdentityCorrections_GrowerLots_TargetGrowerLotId" FOREIGN KEY ("TargetGrowerLotId") REFERENCES "GrowerLots" ("Id") ON DELETE RESTRICT,
            CONSTRAINT "FK_InventoryIdentityCorrections_ReceiptInventoryOverrides_ReceiptInventoryOverrideId" FOREIGN KEY ("ReceiptInventoryOverrideId") REFERENCES "ReceiptInventoryOverrides" ("Id") ON DELETE RESTRICT,
            CONSTRAINT "FK_InventoryIdentityCorrections_Receipts_CorrectedReceiptId" FOREIGN KEY ("CorrectedReceiptId") REFERENCES "Receipts" ("Id") ON DELETE RESTRICT,
            CONSTRAINT "FK_InventoryIdentityCorrections_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT);

        ALTER TABLE "RoomInventoryAdjustments" ADD COLUMN "InventoryIdentityCorrectionId" uuid NULL;
        ALTER TABLE "TreatmentLineageMovements" ADD COLUMN "InventoryIdentityCorrectionId" uuid NULL;

        CREATE INDEX "IX_InventoryIdentityCorrections_CorrectedReceiptId" ON "InventoryIdentityCorrections" ("CorrectedReceiptId");
        CREATE INDEX "IX_InventoryIdentityCorrections_CreatedByUserId" ON "InventoryIdentityCorrections" ("CreatedByUserId");
        CREATE INDEX "IX_InventoryIdentityCorrections_IsActive_CreatedAt" ON "InventoryIdentityCorrections" ("IsActive", "CreatedAt");
        CREATE UNIQUE INDEX "IX_InventoryIdentityCorrections_OperationKey" ON "InventoryIdentityCorrections" ("OperationKey");
        CREATE UNIQUE INDEX "IX_InventoryIdentityCorrections_ReceiptInventoryOverrideId" ON "InventoryIdentityCorrections" ("ReceiptInventoryOverrideId") WHERE "ReceiptInventoryOverrideId" IS NOT NULL;
        CREATE UNIQUE INDEX "IX_InventoryIdentityCorrections_SourceCropYear_SourceGrowerLotId_SourceFruitProfileId" ON "InventoryIdentityCorrections" ("SourceCropYear", "SourceGrowerLotId", "SourceFruitProfileId");
        CREATE INDEX "IX_InventoryIdentityCorrections_SourceFruitProfileId" ON "InventoryIdentityCorrections" ("SourceFruitProfileId");
        CREATE INDEX "IX_InventoryIdentityCorrections_SourceGrowerLotId" ON "InventoryIdentityCorrections" ("SourceGrowerLotId");
        CREATE INDEX "IX_InventoryIdentityCorrections_TargetCropYear_TargetGrowerLotId_TargetFruitProfileId" ON "InventoryIdentityCorrections" ("TargetCropYear", "TargetGrowerLotId", "TargetFruitProfileId");
        CREATE INDEX "IX_InventoryIdentityCorrections_TargetFruitProfileId" ON "InventoryIdentityCorrections" ("TargetFruitProfileId");
        CREATE INDEX "IX_InventoryIdentityCorrections_TargetGrowerLotId" ON "InventoryIdentityCorrections" ("TargetGrowerLotId");
        CREATE INDEX "IX_RoomInventoryAdjustments_InventoryIdentityCorrectionId" ON "RoomInventoryAdjustments" ("InventoryIdentityCorrectionId");
        CREATE INDEX "IX_TreatmentLineageMovements_InventoryIdentityCorrectionId" ON "TreatmentLineageMovements" ("InventoryIdentityCorrectionId");
        ALTER TABLE "RoomInventoryAdjustments" ADD CONSTRAINT "FK_RoomInventoryAdjustments_InventoryIdentityCorrections_InventoryIdentityCorrectionId" FOREIGN KEY ("InventoryIdentityCorrectionId") REFERENCES "InventoryIdentityCorrections" ("Id") ON DELETE RESTRICT;
        ALTER TABLE "TreatmentLineageMovements" ADD CONSTRAINT "FK_TreatmentLineageMovements_InventoryIdentityCorrections_InventoryIdentityCorrectionId" FOREIGN KEY ("InventoryIdentityCorrectionId") REFERENCES "InventoryIdentityCorrections" ("Id") ON DELETE RESTRICT;
    END IF;
    IF current_setting('cropqc.test_force_inventory_identity_failure', true) = 'on' THEN
        RAISE EXCEPTION 'Forced inventory identity compatibility failure for rollback validation.';
    END IF;
END $apply$;

COMMIT;
\ir verify-inventory-identity-corrections.sql

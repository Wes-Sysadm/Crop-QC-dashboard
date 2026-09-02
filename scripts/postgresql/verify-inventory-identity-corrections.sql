\set ON_ERROR_STOP on
\ir preflight-inventory-identity-corrections.sql

BEGIN TRANSACTION READ ONLY;
DO $verify$
BEGIN
    IF EXISTS (SELECT 1 FROM "InventoryIdentityCorrections" WHERE "SourceCropYear" <= 0 OR "TargetCropYear" <= 0) THEN
        RAISE EXCEPTION 'Non-positive inventory identity correction crop year detected.';
    END IF;
    IF EXISTS (SELECT 1 FROM "InventoryIdentityCorrections" WHERE "IsComplete" AND (
        "SourceCropYear" = "TargetCropYear" AND "SourceGrowerLotId" = "TargetGrowerLotId" AND "SourceFruitProfileId" = "TargetFruitProfileId")) THEN
        RAISE EXCEPTION 'Completed self-referential inventory identity correction detected.';
    END IF;
END $verify$;
SELECT 'inventory_identity_corrections_schema_pass' AS status,
       (SELECT count(*) FROM "InventoryIdentityCorrections") AS correction_rows,
       (SELECT count(*) FROM "RoomInventoryAdjustments" WHERE "InventoryIdentityCorrectionId" IS NOT NULL) AS correction_adjustment_rows,
       (SELECT count(*) FROM "TreatmentLineageMovements" WHERE "InventoryIdentityCorrectionId" IS NOT NULL) AS correction_treatment_rows,
       (SELECT count(*) FROM "__EFMigrationsHistory") AS migration_history_rows;
ROLLBACK;

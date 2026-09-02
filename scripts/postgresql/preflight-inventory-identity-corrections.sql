\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

DO $preflight$
DECLARE
    target_count integer;
    exact_columns integer;
BEGIN
    SELECT
        (SELECT count(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = 'InventoryIdentityCorrections')
      + (SELECT count(*) FROM information_schema.columns WHERE table_schema = current_schema() AND
            ((table_name = 'InventoryIdentityCorrections') OR
             (table_name IN ('RoomInventoryAdjustments', 'TreatmentLineageMovements') AND column_name = 'InventoryIdentityCorrectionId')))
      + (SELECT count(*) FROM pg_indexes WHERE schemaname = current_schema() AND indexname IN (
            'PK_InventoryIdentityCorrections',
            'IX_InventoryIdentityCorrections_CorrectedReceiptId',
            'IX_InventoryIdentityCorrections_CreatedByUserId',
            'IX_InventoryIdentityCorrections_IsActive_CreatedAt',
            'IX_InventoryIdentityCorrections_OperationKey',
            'IX_InventoryIdentityCorrections_ReceiptInventoryOverrideId',
            'IX_InventoryIdentityCorrections_SourceCropYear_SourceGrowerLotId_SourceFruitProfileId',
            'IX_InventoryIdentityCorrections_TargetCropYear_TargetGrowerLotId_TargetFruitProfileId',
            'IX_RoomInventoryAdjustments_InventoryIdentityCorrectionId',
            'IX_TreatmentLineageMovements_InventoryIdentityCorrectionId'))
      + (SELECT count(*) FROM pg_constraint WHERE conname IN (
            'FK_InventoryIdentityCorrections_ReceiptInventoryOverrides_ReceiptInventoryOverrideId',
            'FK_InventoryIdentityCorrections_Receipts_CorrectedReceiptId',
            'FK_InventoryIdentityCorrections_Users_CreatedByUserId',
            'FK_RoomInventoryAdjustments_InventoryIdentityCorrections_InventoryIdentityCorrectionId',
            'FK_TreatmentLineageMovements_InventoryIdentityCorrections_InventoryIdentityCorrectionId'))
    INTO target_count;

    IF target_count = 0 THEN
        RAISE NOTICE 'State A: inventory identity correction schema is absent and safe to apply.';
        RETURN;
    END IF;
    IF target_count <> 37 THEN
        RAISE EXCEPTION 'State C: partial inventory identity correction schema detected (% of 37 target objects).', target_count;
    END IF;

    SELECT count(*) INTO exact_columns
    FROM information_schema.columns
    WHERE table_schema = current_schema() AND table_name = 'InventoryIdentityCorrections'
      AND ((column_name IN ('Id', 'ReceiptInventoryOverrideId') AND data_type = 'uuid')
        OR (column_name IN ('SourceCropYear','SourceGrowerLotId','SourceFruitProfileId','TargetCropYear','TargetGrowerLotId','TargetFruitProfileId','CreatedByUserId','ExpectedAdjustmentCount','ExpectedTreatmentMovementCount') AND data_type = 'integer' AND is_nullable = 'NO')
        OR (column_name = 'CorrectedReceiptId' AND data_type = 'bigint' AND is_nullable = 'YES')
        OR (column_name = 'OperationKey' AND data_type = 'character varying' AND character_maximum_length = 150 AND is_nullable = 'NO')
        OR (column_name = 'Reason' AND data_type = 'character varying' AND character_maximum_length = 1000 AND is_nullable = 'NO')
        OR (column_name = 'CreatedAt' AND data_type = 'timestamp with time zone' AND is_nullable = 'NO')
        OR (column_name IN ('SourceIdentitySnapshotJson','TargetIdentitySnapshotJson') AND data_type = 'text' AND is_nullable = 'NO')
        OR (column_name IN ('IsComplete','IsActive') AND data_type = 'boolean' AND is_nullable = 'NO'));
    IF exact_columns <> 19 THEN
        RAISE EXCEPTION 'State C: inventory identity correction columns are incompatible (% of 19 exact).', exact_columns;
    END IF;
    RAISE NOTICE 'State B: inventory identity correction schema is complete.';
END $preflight$;

SELECT 'inventory_identity_corrections_preflight_pass' AS status;
ROLLBACK;

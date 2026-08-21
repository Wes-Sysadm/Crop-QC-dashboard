\set ON_ERROR_STOP on
\ir verify-treatment-report-attachments.sql

BEGIN TRANSACTION READ ONLY;
DO $preflight$
DECLARE
    existing_count integer;
    exact_count constant integer := 102;
BEGIN
    SELECT
        (SELECT count(*) FROM information_schema.tables WHERE table_schema=current_schema() AND table_name IN ('Processors','ProcessorShipments','ProcessorShipmentLines','ProcessorShipmentPriceCorrections'))
      + (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name IN ('Processors','ProcessorShipments','ProcessorShipmentLines','ProcessorShipmentPriceCorrections'))
      + (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND ((table_name='RoomInventoryAdjustments' AND column_name='ProcessorShipmentLineId') OR (table_name='TreatmentLineageMovements' AND column_name='ProcessorShipmentLineId')))
      + (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=current_schema() AND c.relkind='i' AND c.relname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY[
          'IX_TreatmentLineageMovements_ProcessorShipmentLineId','IX_RoomInventoryAdjustments_ProcessorShipmentLineId','IX_RoomInventoryAdjustments_ProcessorShipmentLineId_AdjustmentType',
          'IX_Processors_CreatedByUserId','IX_Processors_IsActive_Name','IX_Processors_Name','IX_Processors_UpdatedByUserId',
          'IX_ProcessorShipmentLines_ProcessorShipmentId','IX_ProcessorShipmentLines_ReceiptId','IX_ProcessorShipmentLines_RoomId','IX_ProcessorShipmentLines_SourceInventoryAdjustmentId','IX_ProcessorShipmentLines_WarehouseId_RoomId',
          'IX_ProcessorShipmentPriceCorrections_CorrectedByUserId','IX_ProcessorShipmentPriceCorrections_OperationKey','IX_ProcessorShipmentPriceCorrections_ProcessorShipmentId_CorrectedAt',
          'IX_ProcessorShipments_CreatedByUserId','IX_ProcessorShipments_OperationKey','IX_ProcessorShipments_ProcessorId','IX_ProcessorShipments_ReversedByUserId','IX_ProcessorShipments_ShippedAt_ProcessorId']) x)))
      + (SELECT count(*) FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY[
          'PK_Processors','PK_ProcessorShipments','PK_ProcessorShipmentLines','PK_ProcessorShipmentPriceCorrections',
          'FK_Processors_Users_CreatedByUserId','FK_Processors_Users_UpdatedByUserId','FK_ProcessorShipments_Processors_ProcessorId','FK_ProcessorShipments_Users_CreatedByUserId','FK_ProcessorShipments_Users_ReversedByUserId',
          'FK_ProcessorShipmentLines_ProcessorShipments_ProcessorShipmentId','FK_ProcessorShipmentLines_Receipts_ReceiptId','FK_ProcessorShipmentLines_RoomInventoryAdjustments_SourceInventoryAdjustmentId','FK_ProcessorShipmentLines_Rooms_RoomId','FK_ProcessorShipmentLines_Warehouses_WarehouseId',
          'FK_ProcessorShipmentPriceCorrections_ProcessorShipments_ProcessorShipmentId','FK_ProcessorShipmentPriceCorrections_Users_CorrectedByUserId','FK_RoomInventoryAdjustments_ProcessorShipmentLines_ProcessorShipmentLineId','FK_TreatmentLineageMovements_ProcessorShipmentLines_ProcessorShipmentLineId']) x)))
    INTO existing_count;

    IF existing_count NOT IN (0, exact_count) THEN
        RAISE EXCEPTION 'State C: partial/conflicting Processor Shipment schema detected (% of % objects)', existing_count, exact_count;
    END IF;
    IF existing_count = exact_count THEN
        IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Processors') <> 9
           OR (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='ProcessorShipments') <> 18
           OR (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='ProcessorShipmentLines') <> 21
           OR (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='ProcessorShipmentPriceCorrections') <> 10 THEN
            RAISE EXCEPTION 'State C: Processor Shipment table columns are not exact';
        END IF;
        IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name IN ('Processors','ProcessorShipments','ProcessorShipmentLines','ProcessorShipmentPriceCorrections') AND column_name IN ('CurrentPrice','CurrentRate','DefaultPrice','DefaultRate')) THEN
            RAISE EXCEPTION 'State C: pricing was incorrectly added to Processor master data';
        END IF;
        IF EXISTS (SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=current_schema() AND c.relname IN (left('IX_RoomInventoryAdjustments_ProcessorShipmentLineId_AdjustmentType',63),'IX_ProcessorShipmentPriceCorrections_OperationKey','IX_ProcessorShipments_OperationKey') AND (NOT i.indisunique OR NOT i.indisvalid OR NOT i.indisready)) THEN
            RAISE EXCEPTION 'State C: Processor Shipment unique indexes are incompatible';
        END IF;
        IF EXISTS (SELECT 1 FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY[
            'PK_Processors','PK_ProcessorShipments','PK_ProcessorShipmentLines','PK_ProcessorShipmentPriceCorrections','FK_RoomInventoryAdjustments_ProcessorShipmentLines_ProcessorShipmentLineId','FK_TreatmentLineageMovements_ProcessorShipmentLines_ProcessorShipmentLineId']) x)) AND NOT convalidated) THEN
            RAISE EXCEPTION 'State C: Processor Shipment constraints are not validated';
        END IF;
    END IF;
END $preflight$;
SELECT CASE WHEN to_regclass(format('%I.%I',current_schema(),'Processors')) IS NULL THEN 'state_a_absent' ELSE 'state_b_complete_exact' END AS compatibility_state;
ROLLBACK;

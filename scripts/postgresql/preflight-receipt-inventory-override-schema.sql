\set ON_ERROR_STOP on
\ir verify-facility-run-reporting.sql

BEGIN TRANSACTION READ ONLY;

DO $preflight$
DECLARE
    existing_target_count integer;
BEGIN
    IF EXISTS (
        SELECT 1 FROM "__EFMigrationsHistory"
        WHERE "MigrationId"='20260805014812_AddReceiptInventoryOverrides'
    ) THEN
        RAISE EXCEPTION 'Receipt inventory override migration is already recorded; use verify-receipt-inventory-override.sql instead';
    END IF;

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

    IF existing_target_count <> 0 THEN
        RAISE EXCEPTION 'Unexpected partial receipt-inventory-override schema detected (% of 10 target objects)', existing_target_count;
    END IF;
END $preflight$;

SELECT 'receipt_inventory_override_schema_preflight_ready' AS status,
       '20260805014812_AddReceiptInventoryOverrides' AS migration;
ROLLBACK;

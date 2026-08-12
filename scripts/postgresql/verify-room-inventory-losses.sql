\set ON_ERROR_STOP on
\ir preflight-room-inventory-losses.sql
BEGIN TRANSACTION READ ONLY;
DO $verify$
BEGIN
    IF to_regclass(format('%I.%I',current_schema(),'RoomInventoryLosses')) IS NULL THEN
        RAISE EXCEPTION 'RoomInventoryLosses is absent; verification requires the complete exact schema.';
    END IF;
END $verify$;
SELECT count(*) AS room_inventory_loss_row_count FROM "RoomInventoryLosses";
SELECT count(*) AS linked_adjustment_row_count FROM "RoomInventoryAdjustments" WHERE "RoomInventoryLossId" IS NOT NULL;
SELECT 'room_inventory_losses_schema_verified' AS status,
       'exact_25_columns_pk_6_fks_7_indexes_plus_adjustment_fk_2_indexes' AS object_state,
       'migration_history_intentionally_unchanged' AS migration_history;
ROLLBACK;

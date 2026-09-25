-- Read-only post-migration schema and reconciliation checks.
BEGIN TRANSACTION READ ONLY;
DO $$
BEGIN
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema()
        AND ((table_name='Receipts' AND column_name IN ('IsTransferReceipt','TransferCompletedAt'))
          OR (table_name='InterCrewTransfers' AND column_name IN ('RequiresTruckReceipt','ReceivingReceiptId')))) <> 4
       OR to_regclass('"ReceiptVarietyLines"') IS NULL THEN
        RAISE EXCEPTION 'Truck Receipt schema is incomplete';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname=current_schema()
        AND indexname='IX_InterCrewTransfers_ReceivingReceiptId' AND indexdef LIKE 'CREATE UNIQUE%') THEN
        RAISE EXCEPTION 'One-to-one transfer receipt constraint is missing';
    END IF;
    IF EXISTS (SELECT 1 FROM "Receipts" r JOIN "RoomInventoryAdjustments" a ON a."ReceiptId"=r."Id" WHERE r."IsTransferReceipt") THEN
        RAISE EXCEPTION 'A transfer receipt incorrectly owns independent inventory';
    END IF;
    IF EXISTS (SELECT 1 FROM "Receipts" r WHERE r."IsTransferReceipt" AND NOT r."IsDeleted"
        AND r."BinCount" <> (SELECT COALESCE(sum(v."BinCount"),0) FROM "ReceiptVarietyLines" v WHERE v."ReceiptId"=r."Id")) THEN
        RAISE EXCEPTION 'Receipt variety quantities do not equal its total';
    END IF;
END $$;
SELECT "Status", "RequiresTruckReceipt", count(*) AS transfers FROM "InterCrewTransfers" GROUP BY 1,2 ORDER BY 1,2;
COMMIT;

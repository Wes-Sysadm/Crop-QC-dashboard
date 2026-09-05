\set ON_ERROR_STOP on
\ir preflight-receipt-scoped-inventory-identity-corrections.sql

BEGIN;
SET LOCAL lock_timeout='15s';
SET LOCAL statement_timeout='10min';
SELECT pg_advisory_xact_lock(hashtextextended('CropQc:20260905012129_ScopeInventoryIdentityCorrectionsToReceipts', 0));
SELECT to_regclass(format('%I.%I', current_schema(), 'UX_InventoryIdentityCorrections_ReceiptSource')) IS NULL AS apply_target \gset
\if :apply_target
ALTER TABLE "InventoryIdentityCorrections" ALTER COLUMN "SourceGrowerLotId" DROP NOT NULL;
DO $drop_predecessor$
DECLARE predecessor record;
BEGIN
    FOR predecessor IN
        SELECT indexname
        FROM pg_indexes
        WHERE schemaname = current_schema() AND tablename = 'InventoryIdentityCorrections'
          AND (indexname = 'IX_InventoryIdentityCorrections_CorrectedReceiptId'
            OR (lower(indexdef) LIKE '%("sourcecropyear", "sourcegrowerlotid", "sourcefruitprofileid")%'
                AND lower(indexdef) NOT LIKE '% where %'))
    LOOP
        EXECUTE format('DROP INDEX %I.%I', current_schema(), predecessor.indexname);
    END LOOP;
END $drop_predecessor$;
CREATE UNIQUE INDEX "UX_InventoryIdentityCorrections_ReceiptSource"
    ON "InventoryIdentityCorrections" ("CorrectedReceiptId", "SourceCropYear", "SourceGrowerLotId", "SourceFruitProfileId")
    WHERE "CorrectedReceiptId" IS NOT NULL;
CREATE UNIQUE INDEX "UX_InventoryIdentityCorrections_GlobalSource"
    ON "InventoryIdentityCorrections" ("SourceCropYear", "SourceGrowerLotId", "SourceFruitProfileId")
    WHERE "CorrectedReceiptId" IS NULL;
\else
\echo 'Receipt-scoped inventory identity indexes are already complete and exact; no DDL will be applied.'
\endif
DO $postcheck$ BEGIN
    IF to_regclass(format('%I.%I', current_schema(), 'UX_InventoryIdentityCorrections_ReceiptSource')) IS NULL THEN
        RAISE EXCEPTION 'Receipt-scoped inventory identity index missing after apply';
    END IF;
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = current_schema() AND table_name = 'InventoryIdentityCorrections'
          AND column_name = 'SourceGrowerLotId' AND is_nullable <> 'YES') THEN
        RAISE EXCEPTION 'SourceGrowerLotId must permit reviewed legacy-position corrections';
    END IF;
    IF current_setting('cropqc.test_force_receipt_scoped_identity_failure', true) = 'on' THEN
        RAISE EXCEPTION 'Forced receipt-scoped identity compatibility failure for rollback regression';
    END IF;
END $postcheck$;
COMMIT;
\ir verify-receipt-scoped-inventory-identity-corrections.sql

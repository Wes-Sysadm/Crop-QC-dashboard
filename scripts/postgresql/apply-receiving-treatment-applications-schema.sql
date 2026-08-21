\set ON_ERROR_STOP on
\ir preflight-receiving-treatment-applications.sql

BEGIN;
SET LOCAL lock_timeout='15s';
SET LOCAL statement_timeout='10min';
SELECT pg_advisory_xact_lock(hashtextextended('CropQc:20260820194148_AddReceivingTreatmentApplications', 0));
SELECT NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema=current_schema() AND table_name='TreatmentChemicals' AND column_name='ApplicationLevel') AS apply_target \gset

\if :apply_target
ALTER TABLE "TreatmentChemicals" ADD COLUMN "ApplicationLevel" character varying(25) NOT NULL DEFAULT 'Room';
ALTER TABLE "RoomTreatmentApplications" ADD COLUMN "ApplicationLevel" character varying(25) NOT NULL DEFAULT 'Room';
ALTER TABLE "RoomTreatmentApplications" ADD COLUMN "ReceiptId" bigint;
ALTER TABLE "RoomTreatmentApplicationSources" ADD COLUMN "ReceiptId" bigint;
ALTER TABLE "TreatmentLineageSegments" ADD COLUMN "ReceiptId" bigint;
ALTER TABLE "TreatmentLineageMovements" ADD COLUMN "ReceiptId" bigint;

DROP INDEX "IX_TreatmentChemicals_Crop_IsActive_ProductName";
DROP INDEX "IX_TreatmentLineageSegments_RoomId_IdentityKey_TreatmentSignature";

CREATE INDEX "IX_TreatmentChemicals_ApplicationLevel_Crop_IsActive_ProductName" ON "TreatmentChemicals" ("ApplicationLevel", "Crop", "IsActive", "ProductName");
CREATE INDEX "IX_RoomTreatmentApplications_ReceiptId_AppliedAt" ON "RoomTreatmentApplications" ("ReceiptId", "AppliedAt");
CREATE INDEX "IX_RoomTreatmentApplicationSources_ReceiptId" ON "RoomTreatmentApplicationSources" ("ReceiptId");
CREATE INDEX "IX_TreatmentLineageSegments_ReceiptId" ON "TreatmentLineageSegments" ("ReceiptId");
CREATE UNIQUE INDEX "UX_TreatmentLineageSegments_Receipt" ON "TreatmentLineageSegments" ("RoomId", "IdentityKey", "TreatmentSignature", "ReceiptId") WHERE "ReceiptId" IS NOT NULL;
CREATE UNIQUE INDEX "UX_TreatmentLineageSegments_Unassigned" ON "TreatmentLineageSegments" ("RoomId", "IdentityKey", "TreatmentSignature") WHERE "ReceiptId" IS NULL;
CREATE INDEX "IX_TreatmentLineageMovements_ReceiptId" ON "TreatmentLineageMovements" ("ReceiptId");

ALTER TABLE "RoomTreatmentApplications" ADD CONSTRAINT "FK_RoomTreatmentApplications_Receipts_ReceiptId" FOREIGN KEY ("ReceiptId") REFERENCES "Receipts" ("Id") ON DELETE RESTRICT;
ALTER TABLE "RoomTreatmentApplicationSources" ADD CONSTRAINT "FK_RoomTreatmentApplicationSources_Receipts_ReceiptId" FOREIGN KEY ("ReceiptId") REFERENCES "Receipts" ("Id") ON DELETE RESTRICT;
ALTER TABLE "TreatmentLineageSegments" ADD CONSTRAINT "FK_TreatmentLineageSegments_Receipts_ReceiptId" FOREIGN KEY ("ReceiptId") REFERENCES "Receipts" ("Id") ON DELETE RESTRICT;
ALTER TABLE "TreatmentLineageMovements" ADD CONSTRAINT "FK_TreatmentLineageMovements_Receipts_ReceiptId" FOREIGN KEY ("ReceiptId") REFERENCES "Receipts" ("Id") ON DELETE RESTRICT;
\else
\echo 'Receiving treatment schema is already complete and exact; no DDL will be applied.'
\endif

DO $postcheck$
BEGIN
    IF current_setting('cropqc.test_force_receiving_treatment_failure', true)='on' THEN
        RAISE EXCEPTION 'Forced Receiving treatment compatibility failure for rollback regression';
    END IF;
END $postcheck$;
COMMIT;
\ir verify-receiving-treatment-applications.sql

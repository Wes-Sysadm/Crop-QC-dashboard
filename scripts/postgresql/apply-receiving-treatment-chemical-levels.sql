\set ON_ERROR_STOP on
\ir preflight-receiving-treatment-chemical-levels.sql

BEGIN;
SET LOCAL lock_timeout='15s';
SET LOCAL statement_timeout='5min';
SELECT pg_advisory_xact_lock(hashtextextended('CropQc:ReceivingTreatmentChemicalLevels:v1', 0));
UPDATE "TreatmentChemicals"
SET "ApplicationLevel"='Receiving', "UpdatedAt"=CURRENT_TIMESTAMP
WHERE "ApplicationLevel"='Room' AND (
    ("Id"=11 AND "ProductName"='SMARTFRESH INBOX FLEX/250X5G/1.25KG' AND "CommonName"='MCP' AND "Crop"='Apples')
 OR ("Id"=12 AND "ProductName"='SMARTFRESH INBOX FLEX/250X5G/1.25KG Pear' AND "CommonName"='MCP' AND "Crop"='Pears'));
DO $postcheck$
BEGIN
    IF (SELECT count(*) FROM "TreatmentChemicals" WHERE "Id" IN (11,12) AND "ApplicationLevel"='Receiving')<>2 THEN
        RAISE EXCEPTION 'MCP Treatment Chemical ApplicationLevel alignment failed';
    END IF;
    IF current_setting('cropqc.test_force_receiving_treatment_config_failure', true)='on' THEN
        RAISE EXCEPTION 'Forced MCP configuration failure for rollback regression';
    END IF;
END $postcheck$;
COMMIT;
\ir verify-receiving-treatment-chemical-levels.sql

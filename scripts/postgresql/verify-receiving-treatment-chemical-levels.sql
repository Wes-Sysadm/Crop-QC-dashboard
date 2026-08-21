\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
DO $verify$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM "TreatmentChemicals" WHERE "Id"=11
        AND "ProductName"='SMARTFRESH INBOX FLEX/250X5G/1.25KG' AND "CommonName"='MCP' AND "Crop"='Apples' AND "ApplicationLevel"='Receiving')
       OR NOT EXISTS (SELECT 1 FROM "TreatmentChemicals" WHERE "Id"=12
        AND "ProductName"='SMARTFRESH INBOX FLEX/250X5G/1.25KG Pear' AND "CommonName"='MCP' AND "Crop"='Pears' AND "ApplicationLevel"='Receiving') THEN
        RAISE EXCEPTION 'Reviewed MCP Treatment Chemical classification is not exact';
    END IF;
    IF (SELECT count(*) FROM "TreatmentChemicals" WHERE "Id" BETWEEN 1 AND 10 AND "ApplicationLevel"='Room')<>10 THEN
        RAISE EXCEPTION 'Original Room Treatment Chemical classification changed';
    END IF;
END $verify$;
SELECT "Id", "ProductName", "CommonName", "Crop", "ApplicationLevel"
FROM "TreatmentChemicals" WHERE "Id" IN (11,12) ORDER BY "Id";
ROLLBACK;

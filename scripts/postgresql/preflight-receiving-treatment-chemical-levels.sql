\set ON_ERROR_STOP on
\ir verify-receiving-treatment-applications.sql

BEGIN TRANSACTION READ ONLY;
DO $preflight$
DECLARE
    receiving_count integer;
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM "TreatmentChemicals" WHERE "Id"=11
          AND "ProductName"='SMARTFRESH INBOX FLEX/250X5G/1.25KG' AND "CommonName"='MCP' AND "Crop"='Apples')
       OR NOT EXISTS (
        SELECT 1 FROM "TreatmentChemicals" WHERE "Id"=12
          AND "ProductName"='SMARTFRESH INBOX FLEX/250X5G/1.25KG Pear' AND "CommonName"='MCP' AND "Crop"='Pears') THEN
        RAISE EXCEPTION 'State C: exact reviewed MCP Treatment Chemical rows 11 and 12 are not present';
    END IF;
    IF EXISTS (SELECT 1 FROM "TreatmentChemicals" WHERE "Id" BETWEEN 1 AND 10 AND "ApplicationLevel"<>'Room') THEN
        RAISE EXCEPTION 'State C: an original Room Treatment Chemical is not classified Room';
    END IF;
    IF (SELECT count(*) FROM "TreatmentChemicals" WHERE "Id" BETWEEN 1 AND 10)<>10 THEN
        RAISE EXCEPTION 'State C: original Room Treatment Chemical rows 1 through 10 are incomplete';
    END IF;
    SELECT count(*) INTO receiving_count FROM "TreatmentChemicals"
      WHERE "Id" IN (11,12) AND "ApplicationLevel"='Receiving';
    IF receiving_count NOT IN (0,2) THEN
        RAISE EXCEPTION 'State C: MCP ApplicationLevel alignment is partial';
    END IF;
    IF EXISTS (SELECT 1 FROM "TreatmentChemicals" WHERE "ApplicationLevel" NOT IN ('Room','Receiving')) THEN
        RAISE EXCEPTION 'State C: an invalid Treatment Chemical ApplicationLevel exists';
    END IF;
END $preflight$;

SELECT CASE WHEN count(*) FILTER (WHERE "ApplicationLevel"='Receiving')=2
       THEN 'state_b_complete_exact' ELSE 'state_a_safe_to_align' END AS compatibility_state,
       count(*) FILTER (WHERE "ApplicationLevel"='Receiving') AS reviewed_receiving_rows
FROM "TreatmentChemicals" WHERE "Id" IN (11,12);
ROLLBACK;

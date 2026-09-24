-- READ ONLY, works before and after the additive feature schema.
\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout = '60s';
SET LOCAL cropqc.truck_adoption_apply = 'false';
\ir truck-receipt-initial-adoption-core.sql
COMMIT;

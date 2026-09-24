-- MANUAL RELEASE DATA OPERATION, never run as migration/startup.
-- Requires explicit production authorization, fresh verified backup, quiesced writes,
-- additive schema, compatible web/launcher already live, feature OFF, smoke passed.
-- psql -X -v ON_ERROR_STOP=1 -v actor_user_id=<authorized operator ID> -f this-file.sql
-- All 15 adopt or all roll back. Reruns fail safely; never remove/relax a guard.
\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL SERIALIZABLE;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '60s';
SET LOCAL cropqc.truck_adoption_apply = 'true';
SELECT set_config('cropqc.repair_actor', :'actor_user_id', true);
\ir truck-receipt-initial-adoption-core.sql
COMMIT;

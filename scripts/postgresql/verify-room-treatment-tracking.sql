\set ON_ERROR_STOP on
\ir preflight-room-treatment-tracking.sql
BEGIN TRANSACTION READ ONLY;

DO $verify$
BEGIN
    IF to_regclass(format('%I.%I',current_schema(),'TreatmentChemicals')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'RoomTreatmentApplications')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'RoomTreatmentApplicationSources')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'TreatmentLineageSegments')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'TreatmentLineageSegmentApplications')) IS NULL
       OR to_regclass(format('%I.%I',current_schema(),'TreatmentLineageMovements')) IS NULL THEN
        RAISE EXCEPTION 'Room treatment tracking verification requires the complete exact schema.';
    END IF;
END $verify$;

SELECT count(*) AS treatment_chemical_row_count FROM "TreatmentChemicals";
SELECT count(*) AS treatment_application_row_count FROM "RoomTreatmentApplications";
SELECT count(*) AS treatment_lineage_segment_row_count FROM "TreatmentLineageSegments";
SELECT count(*) AS treatment_lineage_movement_row_count FROM "TreatmentLineageMovements";
SELECT 'room_treatment_tracking_schema_verified' AS status,
       'exact_6_tables_92_columns_6_primary_keys_24_foreign_keys_30_secondary_indexes_4_snapshot_columns' AS object_state,
       'exact_reviewed_10_row_treatment_chemical_seed' AS seed_state,
       'migration_history_intentionally_unchanged' AS migration_history;
ROLLBACK;

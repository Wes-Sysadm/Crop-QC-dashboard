\set ON_ERROR_STOP on

-- Read-only production preflight for
-- 20260731014107_SeparatePlanningProjectionsFromActualRuns.
-- This script reports fully missing, partially applied, and complete object state.
begin transaction read only;

select current_database() as database_name,
       current_setting('server_version') as postgresql_version,
       current_schema() as application_schema;

select "MigrationId", "ProductVersion"
from "__EFMigrationsHistory"
order by "MigrationId" desc
limit 10;

do $preflight$
declare
    missing_base text;
    duplicate_actual_run_packouts bigint := 0;
begin
    select string_agg(expected.name, ', ' order by expected.name)
    into missing_base
    from (values
        ('ActualRuns'),
        ('ActualRunRevisions'),
        ('BinsRunEntries'),
        ('PackoutRuns'),
        ('QcSamples'),
        ('Users')
    ) expected(name)
    where to_regclass(format('%I.%I', current_schema(), expected.name)) is null;

    if missing_base is not null then
        raise exception 'Required pre-existing table(s) are missing: %. No changes were attempted.', missing_base;
    end if;

    if exists (
        select 1 from information_schema.columns
        where table_schema = current_schema()
          and table_name = 'PackoutRuns'
          and column_name = 'ActualRunId'
    ) then
        execute '
            select count(*)
            from (
                select "ActualRunId"
                from "PackoutRuns"
                where "ActualRunId" is not null
                group by "ActualRunId"
                having count(*) > 1
            ) duplicates'
        into duplicate_actual_run_packouts;
    end if;

    if duplicate_actual_run_packouts > 0 then
        raise exception '% duplicate Actual Run packout relationship(s) require review before apply. No changes were attempted.',
            duplicate_actual_run_packouts;
    end if;
end
$preflight$;

with expected(display_name, table_name) as (
    values
        ('RunExpectations', 'RunExpectations'),
        ('RunExpectationSources', 'RunExpectationSources'),
        ('PackoutSourceAllocations', 'PackoutSourceAllocations')
)
select display_name,
       to_regclass(format('%I.%I', current_schema(), table_name)) is not null as present
from expected
order by display_name;

with expected(display_name, table_name, column_name, expected_nullable) as (
    values
        ('PackoutRuns.ActualRunId', 'PackoutRuns', 'ActualRunId', 'YES'),
        ('PackoutRuns.RunExpectationId', 'PackoutRuns', 'RunExpectationId', 'YES'),
        ('PackoutRuns.RunProjectionId', 'PackoutRuns', 'RunProjectionId', 'YES'),
        ('RunExpectations.Id', 'RunExpectations', 'Id', 'NO'),
        ('RunExpectations.ActualRunId', 'RunExpectations', 'ActualRunId', 'NO'),
        ('RunExpectations.ActualRunRevisionId', 'RunExpectations', 'ActualRunRevisionId', 'NO'),
        ('RunExpectations.RevisionNumber', 'RunExpectations', 'RevisionNumber', 'NO'),
        ('RunExpectations.FacilityWarehouseId', 'RunExpectations', 'FacilityWarehouseId', 'NO'),
        ('RunExpectations.FacilitySnapshot', 'RunExpectations', 'FacilitySnapshot', 'NO'),
        ('RunExpectations.RunAtSnapshot', 'RunExpectations', 'RunAtSnapshot', 'NO'),
        ('RunExpectations.TotalBins', 'RunExpectations', 'TotalBins', 'NO'),
        ('RunExpectations.GrossPounds', 'RunExpectations', 'GrossPounds', 'NO'),
        ('RunExpectations.ExpectedPackoutPercent', 'RunExpectations', 'ExpectedPackoutPercent', 'NO'),
        ('RunExpectations.ExpectedPackedPounds', 'RunExpectations', 'ExpectedPackedPounds', 'NO'),
        ('RunExpectations.ExpectedPackedBoxes', 'RunExpectations', 'ExpectedPackedBoxes', 'NO'),
        ('RunExpectations.ExpectedWholeBoxes', 'RunExpectations', 'ExpectedWholeBoxes', 'NO'),
        ('RunExpectations.ExpectedCullPounds', 'RunExpectations', 'ExpectedCullPounds', 'NO'),
        ('RunExpectations.ExpectedJuicePounds', 'RunExpectations', 'ExpectedJuicePounds', 'NO'),
        ('RunExpectations.ExpectedPeelerPounds', 'RunExpectations', 'ExpectedPeelerPounds', 'NO'),
        ('RunExpectations.ExpectedWastePounds', 'RunExpectations', 'ExpectedWastePounds', 'NO'),
        ('RunExpectations.ConfidencePercent', 'RunExpectations', 'ConfidencePercent', 'NO'),
        ('RunExpectations.SizeDistributionSnapshotJson', 'RunExpectations', 'SizeDistributionSnapshotJson', 'NO'),
        ('RunExpectations.GradeDistributionSnapshotJson', 'RunExpectations', 'GradeDistributionSnapshotJson', 'NO'),
        ('RunExpectations.ConfigurationSnapshotJson', 'RunExpectations', 'ConfigurationSnapshotJson', 'NO'),
        ('RunExpectations.CalculationVersion', 'RunExpectations', 'CalculationVersion', 'NO'),
        ('RunExpectations.CalculatedAt', 'RunExpectations', 'CalculatedAt', 'NO'),
        ('RunExpectations.CreatedByUserId', 'RunExpectations', 'CreatedByUserId', 'YES'),
        ('RunExpectationSources.Id', 'RunExpectationSources', 'Id', 'NO'),
        ('RunExpectationSources.RunExpectationId', 'RunExpectationSources', 'RunExpectationId', 'NO'),
        ('RunExpectationSources.BinsRunEntryId', 'RunExpectationSources', 'BinsRunEntryId', 'NO'),
        ('RunExpectationSources.WarehouseId', 'RunExpectationSources', 'WarehouseId', 'NO'),
        ('RunExpectationSources.RoomId', 'RunExpectationSources', 'RoomId', 'NO'),
        ('RunExpectationSources.FacilitySnapshot', 'RunExpectationSources', 'FacilitySnapshot', 'NO'),
        ('RunExpectationSources.RoomSnapshot', 'RunExpectationSources', 'RoomSnapshot', 'NO'),
        ('RunExpectationSources.CropYearSnapshot', 'RunExpectationSources', 'CropYearSnapshot', 'YES'),
        ('RunExpectationSources.GrowerLotId', 'RunExpectationSources', 'GrowerLotId', 'YES'),
        ('RunExpectationSources.FruitProfileId', 'RunExpectationSources', 'FruitProfileId', 'YES'),
        ('RunExpectationSources.GrowerSnapshot', 'RunExpectationSources', 'GrowerSnapshot', 'NO'),
        ('RunExpectationSources.LotSnapshot', 'RunExpectationSources', 'LotSnapshot', 'NO'),
        ('RunExpectationSources.VarietySnapshot', 'RunExpectationSources', 'VarietySnapshot', 'NO'),
        ('RunExpectationSources.ProductionTypeSnapshot', 'RunExpectationSources', 'ProductionTypeSnapshot', 'NO'),
        ('RunExpectationSources.IsOrganicSnapshot', 'RunExpectationSources', 'IsOrganicSnapshot', 'NO'),
        ('RunExpectationSources.BinsContributed', 'RunExpectationSources', 'BinsContributed', 'NO'),
        ('RunExpectationSources.ContributionPercent', 'RunExpectationSources', 'ContributionPercent', 'NO'),
        ('RunExpectationSources.QcSampleId', 'RunExpectationSources', 'QcSampleId', 'YES'),
        ('RunExpectationSources.QcSampleTakenAtSnapshot', 'RunExpectationSources', 'QcSampleTakenAtSnapshot', 'YES'),
        ('RunExpectationSources.QcFruitCountSnapshot', 'RunExpectationSources', 'QcFruitCountSnapshot', 'NO'),
        ('RunExpectationSources.QcMeasurementSnapshotJson', 'RunExpectationSources', 'QcMeasurementSnapshotJson', 'NO'),
        ('RunExpectationSources.SizeDistributionSnapshotJson', 'RunExpectationSources', 'SizeDistributionSnapshotJson', 'NO'),
        ('RunExpectationSources.GradeDistributionSnapshotJson', 'RunExpectationSources', 'GradeDistributionSnapshotJson', 'NO'),
        ('RunExpectationSources.GrossPounds', 'RunExpectationSources', 'GrossPounds', 'NO'),
        ('RunExpectationSources.ExpectedPackedPounds', 'RunExpectationSources', 'ExpectedPackedPounds', 'NO'),
        ('RunExpectationSources.ExpectedWholeBoxes', 'RunExpectationSources', 'ExpectedWholeBoxes', 'NO'),
        ('RunExpectationSources.ExpectedCullPounds', 'RunExpectationSources', 'ExpectedCullPounds', 'NO'),
        ('RunExpectationSources.ConfidencePercent', 'RunExpectationSources', 'ConfidencePercent', 'NO'),
        ('RunExpectationSources.WarningSnapshot', 'RunExpectationSources', 'WarningSnapshot', 'YES'),
        ('PackoutSourceAllocations.Id', 'PackoutSourceAllocations', 'Id', 'NO'),
        ('PackoutSourceAllocations.PackoutRunId', 'PackoutSourceAllocations', 'PackoutRunId', 'NO'),
        ('PackoutSourceAllocations.RunExpectationSourceId', 'PackoutSourceAllocations', 'RunExpectationSourceId', 'NO'),
        ('PackoutSourceAllocations.BinsContributed', 'PackoutSourceAllocations', 'BinsContributed', 'NO'),
        ('PackoutSourceAllocations.ContributionPercent', 'PackoutSourceAllocations', 'ContributionPercent', 'NO'),
        ('PackoutSourceAllocations.AllocatedPackedPounds', 'PackoutSourceAllocations', 'AllocatedPackedPounds', 'NO'),
        ('PackoutSourceAllocations.AllocatedWholeBoxes', 'PackoutSourceAllocations', 'AllocatedWholeBoxes', 'NO'),
        ('PackoutSourceAllocations.AllocatedResidualPounds', 'PackoutSourceAllocations', 'AllocatedResidualPounds', 'NO'),
        ('PackoutSourceAllocations.AllocatedJuicePounds', 'PackoutSourceAllocations', 'AllocatedJuicePounds', 'NO'),
        ('PackoutSourceAllocations.AllocatedPeelerPounds', 'PackoutSourceAllocations', 'AllocatedPeelerPounds', 'NO'),
        ('PackoutSourceAllocations.AllocatedWastePounds', 'PackoutSourceAllocations', 'AllocatedWastePounds', 'NO'),
        ('PackoutSourceAllocations.PackCodeAllocationJson', 'PackoutSourceAllocations', 'PackCodeAllocationJson', 'NO'),
        ('PackoutSourceAllocations.SizeAllocationJson', 'PackoutSourceAllocations', 'SizeAllocationJson', 'NO'),
        ('PackoutSourceAllocations.GradeAllocationJson', 'PackoutSourceAllocations', 'GradeAllocationJson', 'NO'),
        ('PackoutSourceAllocations.AllocationVersion', 'PackoutSourceAllocations', 'AllocationVersion', 'NO'),
        ('PackoutSourceAllocations.CalculatedAt', 'PackoutSourceAllocations', 'CalculatedAt', 'NO')
)
select expected.display_name,
       columns.column_name is not null as present,
       columns.is_nullable,
       expected.expected_nullable,
       columns.column_name is not null and columns.is_nullable = expected.expected_nullable as compatible
from expected
left join information_schema.columns columns
  on columns.table_schema = current_schema()
 and columns.table_name = expected.table_name
 and columns.column_name = expected.column_name
order by expected.display_name;

with expected(table_name, index_name, expected_unique) as (
    values
        ('PackoutRuns', 'IX_PackoutRuns_RunExpectationId', false),
        ('PackoutRuns', 'UX_PackoutRuns_ActualRunId', true),
        ('PackoutSourceAllocations', 'IX_PackoutSourceAllocations_PackoutRunId_RunExpectationSourceId', true),
        ('PackoutSourceAllocations', 'IX_PackoutSourceAllocations_RunExpectationSourceId', false),
        ('RunExpectations', 'IX_RunExpectations_ActualRunId_RevisionNumber', true),
        ('RunExpectations', 'IX_RunExpectations_ActualRunRevisionId', true),
        ('RunExpectations', 'IX_RunExpectations_CreatedByUserId', false),
        ('RunExpectationSources', 'IX_RunExpectationSources_BinsRunEntryId', false),
        ('RunExpectationSources', 'IX_RunExpectationSources_QcSampleId', false),
        ('RunExpectationSources', 'IX_RunExpectationSources_RunExpectationId_BinsRunEntryId', true),
        ('RunExpectationSources', 'IX_RunExpectationSources_WarehouseId_RoomId_CropYearSnapshot_LotSnapshot_VarietySnapshot', false)
)
select expected.index_name,
       found.index_name is not null as present,
       found.is_unique,
       expected.expected_unique,
       found.index_name is not null
           and (not expected.expected_unique or found.is_unique) as compatible
from expected
left join lateral (
    select index_rows.relname as index_name,
           index_metadata.indisunique as is_unique
    from pg_class table_rows
    join pg_index index_metadata on index_metadata.indrelid = table_rows.oid
    join pg_class index_rows on index_rows.oid = index_metadata.indexrelid
    where table_rows.relnamespace = current_schema()::regnamespace
      and table_rows.relname = expected.table_name
      and index_rows.relname = left(expected.index_name, 63)
) found on true
order by expected.index_name;

with expected(table_name, constraint_name) as (
    values
        ('PackoutRuns', 'FK_PackoutRuns_ActualRuns_ActualRunId'),
        ('PackoutRuns', 'FK_PackoutRuns_RunExpectations_RunExpectationId'),
        ('RunExpectations', 'FK_RunExpectations_ActualRunRevisions_ActualRunRevisionId'),
        ('RunExpectations', 'FK_RunExpectations_ActualRuns_ActualRunId'),
        ('RunExpectations', 'FK_RunExpectations_Users_CreatedByUserId'),
        ('RunExpectationSources', 'FK_RunExpectationSources_BinsRunEntries_BinsRunEntryId'),
        ('RunExpectationSources', 'FK_RunExpectationSources_QcSamples_QcSampleId'),
        ('RunExpectationSources', 'FK_RunExpectationSources_RunExpectations_RunExpectationId'),
        ('PackoutSourceAllocations', 'FK_PackoutSourceAllocations_PackoutRuns_PackoutRunId'),
        ('PackoutSourceAllocations', 'FK_PackoutSourceAllocations_RunExpectationSources_RunExpectationSourceId')
)
select expected.constraint_name,
       constraints.conname is not null as present
from expected
left join pg_class tables
  on tables.relname = expected.table_name
 and tables.relnamespace = current_schema()::regnamespace
left join pg_constraint constraints
  on constraints.conrelid = tables.oid
 and constraints.conname = left(expected.constraint_name, 63)
 and constraints.contype = 'f'
order by expected.constraint_name;

with expected(table_name, constraint_name) as (
    values
        ('RunExpectations', 'PK_RunExpectations'),
        ('RunExpectationSources', 'PK_RunExpectationSources'),
        ('PackoutSourceAllocations', 'PK_PackoutSourceAllocations')
)
select expected.constraint_name,
       constraints.conname is not null as present
from expected
left join pg_class tables
  on tables.relname = expected.table_name
 and tables.relnamespace = current_schema()::regnamespace
left join pg_constraint constraints
  on constraints.conrelid = tables.oid
 and constraints.conname = left(expected.constraint_name, 63)
 and constraints.contype = 'p'
order by expected.constraint_name;

select
    (select count(*) from "RunProjections") as planning_projections,
    (select count(*) from "ActualRuns") as actual_runs,
    (select count(*) from "PackoutRuns") as packout_results,
    (select count(*) from "BinsRunEntries") as bins_run_entries,
    (select count(*) from "RoomInventoryAdjustments") as room_adjustments;

rollback;

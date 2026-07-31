\set ON_ERROR_STOP on

-- Read-only post-apply verification. Object state is authoritative because the
-- production compatibility path intentionally does not forge EF migration history.
begin transaction read only;

do $verification$
declare
    missing text;
    invalid_count bigint;
begin
    select string_agg(expected.display_name, ', ' order by expected.display_name)
    into missing
    from (values
        ('RunExpectations table', 'RunExpectations', null::text, null::text),
        ('RunExpectationSources table', 'RunExpectationSources', null::text, null::text),
        ('PackoutSourceAllocations table', 'PackoutSourceAllocations', null::text, null::text),
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
    ) expected(display_name, table_name, column_name, expected_nullable)
    where (expected.column_name is null
           and to_regclass(format('%I.%I', current_schema(), expected.table_name)) is null)
       or (expected.column_name is not null
           and not exists (
               select 1
               from information_schema.columns
               where table_schema = current_schema()
                 and table_name = expected.table_name
                 and column_name = expected.column_name
                 and is_nullable = expected.expected_nullable));

    if missing is not null then
        raise exception 'Required schema object(s) are missing or incompatible: %.', missing;
    end if;

    select count(*) into invalid_count
    from (
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
    ) expected(table_name, index_name, require_unique)
    where not exists (
        select 1
        from pg_class table_row
        join pg_index index_metadata on index_metadata.indrelid = table_row.oid
        join pg_class index_row on index_row.oid = index_metadata.indexrelid
        where table_row.relnamespace = current_schema()::regnamespace
          and table_row.relname = expected.table_name
          and index_row.relname = left(expected.index_name, 63)
          and (not expected.require_unique or index_metadata.indisunique));

    if invalid_count > 0 then
        raise exception '% required index(es) are missing.', invalid_count;
    end if;

    select count(*) into invalid_count
    from (
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
    ) expected(table_name, constraint_name)
    where not exists (
        select 1
        from pg_constraint constraints
        join pg_class tables on tables.oid = constraints.conrelid
        where tables.relnamespace = current_schema()::regnamespace
          and tables.relname = expected.table_name
          and constraints.conname = left(expected.constraint_name, 63)
          and constraints.contype = 'f');

    if invalid_count > 0 then
        raise exception '% required foreign key(s) are missing.', invalid_count;
    end if;

    select count(*) into invalid_count
    from (
        values
            ('RunExpectations', 'PK_RunExpectations'),
            ('RunExpectationSources', 'PK_RunExpectationSources'),
            ('PackoutSourceAllocations', 'PK_PackoutSourceAllocations')
    ) expected(table_name, constraint_name)
    where not exists (
        select 1
        from pg_constraint constraints
        join pg_class tables on tables.oid = constraints.conrelid
        where tables.relnamespace = current_schema()::regnamespace
          and tables.relname = expected.table_name
          and constraints.conname = left(expected.constraint_name, 63)
          and constraints.contype = 'p');

    if invalid_count > 0 then
        raise exception '% required primary key(s) are missing.', invalid_count;
    end if;

    select count(*) into invalid_count
    from (
        select "ActualRunId"
        from "PackoutRuns"
        where "ActualRunId" is not null
        group by "ActualRunId"
        having count(*) > 1
    ) duplicates;
    if invalid_count > 0 then
        raise exception '% duplicate Actual Run packout relationship(s) found.', invalid_count;
    end if;

    select count(*) into invalid_count
    from (
        select "ActualRunId", "RevisionNumber"
        from "RunExpectations"
        group by "ActualRunId", "RevisionNumber"
        having count(*) > 1
    ) duplicates;
    if invalid_count > 0 then
        raise exception '% duplicate Run Expectation revision(s) found.', invalid_count;
    end if;

    select count(*) into invalid_count
    from "RunExpectationSources" sources
    left join "RunExpectations" expectations on expectations."Id" = sources."RunExpectationId"
    left join "BinsRunEntries" entries on entries."Id" = sources."BinsRunEntryId"
    where expectations."Id" is null or entries."Id" is null;
    if invalid_count > 0 then
        raise exception '% orphan Run Expectation source row(s) found.', invalid_count;
    end if;

    select count(*) into invalid_count
    from "PackoutSourceAllocations" allocations
    left join "PackoutRuns" packouts on packouts."Id" = allocations."PackoutRunId"
    left join "RunExpectationSources" sources on sources."Id" = allocations."RunExpectationSourceId"
    where packouts."Id" is null or sources."Id" is null;
    if invalid_count > 0 then
        raise exception '% orphan Packout allocation row(s) found.', invalid_count;
    end if;
end
$verification$;

select exists (
    select 1
    from "__EFMigrationsHistory"
    where "MigrationId" = '20260731014107_SeparatePlanningProjectionsFromActualRuns'
) as ef_history_contains_migration,
true as application_object_state_ready;

-- Exercise the exact read surfaces used by Actual Run detail without loading data.
select "Id", "ActualRunId", "ActualRunRevisionId", "RevisionNumber"
from "RunExpectations"
where false;

select "Id", "RunExpectationId", "BinsRunEntryId"
from "RunExpectationSources"
where false;

select "Id", "PackoutRunId", "RunExpectationSourceId"
from "PackoutSourceAllocations"
where false;

select "Id", "RunProjectionId", "ActualRunId", "RunExpectationId"
from "PackoutRuns"
where false;

rollback;

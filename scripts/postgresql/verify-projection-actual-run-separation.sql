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
        ('RunExpectations table', 'RunExpectations', null::text, false),
        ('RunExpectationSources table', 'RunExpectationSources', null::text, false),
        ('PackoutSourceAllocations table', 'PackoutSourceAllocations', null::text, false),
        ('PackoutRuns.ActualRunId', 'PackoutRuns', 'ActualRunId', false),
        ('PackoutRuns.RunExpectationId', 'PackoutRuns', 'RunExpectationId', false),
        ('PackoutRuns.RunProjectionId nullable', 'PackoutRuns', 'RunProjectionId', true)
    ) expected(display_name, table_name, column_name, require_nullable)
    where (expected.column_name is null
           and to_regclass(format('%I.%I', current_schema(), expected.table_name)) is null)
       or (expected.column_name is not null
           and not exists (
               select 1
               from information_schema.columns
               where table_schema = current_schema()
                 and table_name = expected.table_name
                 and column_name = expected.column_name
                 and (not expected.require_nullable or is_nullable = 'YES')));

    if missing is not null then
        raise exception 'Required schema object(s) are missing or incompatible: %.', missing;
    end if;

    select count(*) into invalid_count
    from (
        values
            ('PackoutRuns', 'IX_PackoutRuns_RunExpectationId'),
            ('PackoutRuns', 'UX_PackoutRuns_ActualRunId'),
            ('PackoutSourceAllocations', 'IX_PackoutSourceAllocations_PackoutRunId_RunExpectationSourceId'),
            ('PackoutSourceAllocations', 'IX_PackoutSourceAllocations_RunExpectationSourceId'),
            ('RunExpectations', 'IX_RunExpectations_ActualRunId_RevisionNumber'),
            ('RunExpectations', 'IX_RunExpectations_ActualRunRevisionId'),
            ('RunExpectations', 'IX_RunExpectations_CreatedByUserId'),
            ('RunExpectationSources', 'IX_RunExpectationSources_BinsRunEntryId'),
            ('RunExpectationSources', 'IX_RunExpectationSources_QcSampleId'),
            ('RunExpectationSources', 'IX_RunExpectationSources_RunExpectationId_BinsRunEntryId'),
            ('RunExpectationSources', 'IX_RunExpectationSources_WarehouseId_RoomId_CropYearSnapshot_LotSnapshot_VarietySnapshot')
    ) expected(table_name, index_name)
    where not exists (
        select 1 from pg_indexes indexes
        where indexes.schemaname = current_schema()
          and indexes.tablename = expected.table_name
          and indexes.indexname = left(expected.index_name, 63));

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

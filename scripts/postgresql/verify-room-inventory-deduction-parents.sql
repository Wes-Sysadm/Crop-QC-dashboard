\set ON_ERROR_STOP on

-- Read-only post-apply verification.
begin transaction read only;

do $verification$
declare
    missing text;
    duplicate_count bigint;
    invalid_new_count bigint;
begin
    select string_agg(expected.safe_name, ', ')
    into missing
    from (values
        ('RoomTransfers table', 'RoomTransfers', null::text),
        ('InventoryInvariantVersion column', 'RoomInventoryAdjustments', 'InventoryInvariantVersion'),
        ('InventoryOperationKey column', 'RoomInventoryAdjustments', 'InventoryOperationKey'),
        ('RoomTransferId column', 'RoomInventoryAdjustments', 'RoomTransferId')
    ) expected(safe_name, table_name, column_name)
    where (expected.column_name is null and to_regclass(format('%I.%I', current_schema(), expected.table_name)) is null)
       or (expected.column_name is not null and not exists (
           select 1 from information_schema.columns
           where table_schema = current_schema()
             and table_name = expected.table_name
             and column_name = expected.column_name));
    if missing is not null then
        raise exception 'Required schema object(s) missing: %.', missing;
    end if;

    select count(*) into duplicate_count
    from (
        select "InventoryAdjustmentId"
        from "BinsRunEntries"
        group by "InventoryAdjustmentId"
        having count(*) > 1
    ) duplicates;
    if duplicate_count > 0 then
        raise exception '% duplicate Bins Run adjustment parent(s) found.', duplicate_count;
    end if;

    select count(*) into invalid_new_count
    from "RoomInventoryAdjustments" adjustment
    left join "BinsRunEntries" bins on bins."InventoryAdjustmentId" = adjustment."Id"
    left join "RoomTransfers" transfer on transfer."Id" = adjustment."RoomTransferId"
    where adjustment."ChangeAmount" < 0
      and adjustment."InventoryInvariantVersion" >= 1
      and ((case when bins."Id" is null then 0 else 1 end)
           + (case when transfer."Id" is null then 0 else 1 end)) <> 1;
    if invalid_new_count > 0 then
        raise exception '% new-format deduction(s) do not have exactly one parent.', invalid_new_count;
    end if;
end
$verification$;

select adjustment."Id",
       adjustment."InventoryInvariantVersion",
       adjustment."AdjustmentType",
       adjustment."ChangeAmount",
       bins."Id" as bins_run_id,
       transfer."Id" as transfer_id
from "RoomInventoryAdjustments" adjustment
left join "BinsRunEntries" bins on bins."InventoryAdjustmentId" = adjustment."Id"
left join "RoomTransfers" transfer on transfer."Id" = adjustment."RoomTransferId"
where adjustment."ChangeAmount" < 0
order by adjustment."Id";

rollback;

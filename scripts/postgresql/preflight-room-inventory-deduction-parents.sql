\set ON_ERROR_STOP on

-- Read-only preflight for 20260730150926_EnforceRoomInventoryDeductionParents.
begin transaction read only;

select current_database() as database_name,
       current_setting('server_version') as postgresql_version,
       current_schema() as application_schema;

do $preflight$
declare
    missing text;
    duplicate_count bigint;
begin
    select string_agg(expected.name, ', ')
    into missing
    from (values ('RoomInventoryAdjustments'), ('BinsRunEntries'), ('Rooms'),
                 ('Warehouses'), ('FruitProfiles'), ('Users')) expected(name)
    where to_regclass(format('%I.%I', current_schema(), expected.name)) is null;
    if missing is not null then
        raise exception 'Required table(s) missing: %.', missing;
    end if;

    select count(*) into duplicate_count
    from (
        select "InventoryAdjustmentId"
        from "BinsRunEntries"
        group by "InventoryAdjustmentId"
        having count(*) > 1
    ) duplicates;
    if duplicate_count > 0 then
        raise exception '% duplicate BinsRunEntries.InventoryAdjustmentId value(s) prevent the unique parent constraint.', duplicate_count;
    end if;
end
$preflight$;

select count(*) as negative_adjustments,
       count(*) filter (where bins."Id" is null) as historical_negative_without_bins_run_parent,
       count(*) filter (where bins."Id" is not null and bins."BinsRun" <> -adjustment."ChangeAmount") as bins_run_amount_mismatches
from "RoomInventoryAdjustments" adjustment
left join "BinsRunEntries" bins on bins."InventoryAdjustmentId" = adjustment."Id"
where adjustment."ChangeAmount" < 0;

select adjustment."Id" as adjustment_id,
       adjustment."AdjustmentAt",
       adjustment."WarehouseId",
       adjustment."RoomId",
       adjustment."CropYear",
       adjustment."LotNumber",
       adjustment."VarietyCode",
       adjustment."ChangeAmount",
       adjustment."AdjustmentType",
       adjustment."Source",
       bins."Id" as bins_run_id
from "RoomInventoryAdjustments" adjustment
left join "BinsRunEntries" bins on bins."InventoryAdjustmentId" = adjustment."Id"
where adjustment."ChangeAmount" < 0
  and bins."Id" is null
order by adjustment."Id";

rollback;

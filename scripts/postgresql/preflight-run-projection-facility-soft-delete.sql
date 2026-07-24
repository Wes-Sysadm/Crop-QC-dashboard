\set ON_ERROR_STOP on

-- Read-only production preflight for
-- 20260724020525_AddRunProjectionFacilityAndSoftDelete.
-- This script never changes schema or data.

select current_database() as database_name,
       current_setting('server_version') as server_version,
       current_user as database_user;

select "Id", "Code", "Name", "IsActive"
from "Warehouses"
where "Code" in ('WP', 'EBS')
order by "Code";

select case
           when to_regclass(format('%I.%I', current_schema(), '__EFMigrationsHistory')) is null
               then false
           else exists (
               select 1
               from "__EFMigrationsHistory"
               where "MigrationId" = '20260724020525_AddRunProjectionFacilityAndSoftDelete')
       end as migration_applied;

select count(*) as projection_count,
       count(*) filter (where "ProjectionMode" = 'Preharvest') as preharvest_count,
       count(*) filter (where "ProjectionMode" = 'Inventory') as inventory_count
from "RunProjections";

with inventory_facilities as (
    select source."RunProjectionId",
           count(*) as inventory_source_count,
           count(source."WarehouseId") as assigned_inventory_source_count,
           count(distinct source."WarehouseId") as distinct_warehouse_count,
           min(source."WarehouseId") as candidate_warehouse_id
    from "RunProjectionSources" as source
    where source."SourceType" = 'Inventory'
    group by source."RunProjectionId"
)
select projection."Id",
       projection."Name",
       projection."ProjectionMode",
       inventory.inventory_source_count,
       inventory.assigned_inventory_source_count,
       inventory.distinct_warehouse_count,
       warehouse."Code" as candidate_facility,
       case
           when inventory.inventory_source_count > 0
                and inventory.assigned_inventory_source_count = inventory.inventory_source_count
                and inventory.distinct_warehouse_count = 1
                and warehouse."IsActive"
                and warehouse."Code" in ('WP', 'EBS')
               then 'will backfill'
           when inventory.inventory_source_count is null
               then 'will remain unassigned: no inventory source'
           else 'will remain unassigned: ambiguous or non-operational source facility'
       end as migration_result
from "RunProjections" as projection
left join inventory_facilities as inventory on inventory."RunProjectionId" = projection."Id"
left join "Warehouses" as warehouse on warehouse."Id" = inventory.candidate_warehouse_id
order by projection."Id";

select source."RunProjectionId",
       count(*) as inventory_source_count,
       count(source."WarehouseId") as assigned_inventory_source_count,
       count(distinct source."WarehouseId") as distinct_warehouse_count
from "RunProjectionSources" as source
where source."SourceType" = 'Inventory'
group by source."RunProjectionId"
having count(source."WarehouseId") <> count(*)
    or count(distinct source."WarehouseId") <> 1
order by source."RunProjectionId";

select count(*) as bins_run_entry_count,
       coalesce(sum("BinsRun"), 0) as total_bins_run
from "BinsRunEntries";

select count(*) as room_inventory_adjustment_count,
       coalesce(sum("ChangeAmount"), 0) as net_inventory_adjustment
from "RoomInventoryAdjustments";

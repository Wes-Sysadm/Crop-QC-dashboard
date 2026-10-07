using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CropQc.Data.Inventory;

/// <summary>Protect ExecuteUpdate/Delete and raw DML as well as tracked SaveChanges.</summary>
internal sealed class CanonicalInventorySqlGuard : DbCommandInterceptor
{
    private static readonly Regex PhysicalDml = new(
        @"\b(?:INSERT\s+INTO|UPDATE|DELETE\s+FROM|TRUNCATE(?:\s+TABLE)?)\s+(?:[""\[]?\w+[""\]]?\.)?[""\[]?(ReceiptCustodyAcknowledgments|ReceiptCustodyPlacements|RoomInventoryAdjustments|TreatmentLineageSegments|TreatmentLineageMovements|TreatmentLineageSegmentApplications|RoomTreatmentApplications|RoomTreatmentApplicationSources|RoomTransfers|RoomDepletions|RoomInventoryLosses|BinsRunEntries|ReceiptInventoryOverrides|InventoryIdentityCorrections|InventoryCommands|Receipts|InterCrewTransfers|OutsideWarehouseTransfers|ProcessorShipmentLines|ProcessorShipments|ActualRuns|ActualRunRevisions)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static void Check(DbCommand command, CommandEventData eventData)
    {
        // Tracked SaveChanges already passed the entity/field guard, including
        // permitted non-physical receipt matching and packout metadata.
        if (eventData.CommandSource is not (CommandSource.SaveChanges or CommandSource.Migrations)
            && eventData.Context is CropQcDbContext { CanonicalCommandTransaction: false } db
            && PhysicalDml.Match(command.CommandText) is { Success: true } match && CanonicalInventoryWriteGuard.RequiresCanonicalWrites(db))
            throw new InventoryWriterNotMigratedException(match.Groups[1].Value);
    }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    { Check(command, eventData); return result; }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { Check(command, eventData); return ValueTask.FromResult(result); }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    { Check(command, eventData); return result; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { Check(command, eventData); return ValueTask.FromResult(result); }
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    { Check(command, eventData); return result; }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    { Check(command, eventData); return ValueTask.FromResult(result); }
}

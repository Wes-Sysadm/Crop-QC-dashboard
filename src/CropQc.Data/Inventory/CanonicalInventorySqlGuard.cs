using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CropQc.Data.Inventory;

/// <summary>Protect ExecuteUpdate/Delete and raw DML as well as tracked SaveChanges.</summary>
internal sealed class CanonicalInventorySqlGuard : DbCommandInterceptor
{
    private static readonly Regex PhysicalDml = new(
        @"\b(?:INSERT\s+INTO|UPDATE|DELETE\s+FROM|TRUNCATE(?:\s+TABLE)?)\s+(?:[""\[]?\w+[""\]]?\.)?[""\[]?(RoomInventoryAdjustments|TreatmentLineageSegments|TreatmentLineageMovements|TreatmentLineageSegmentApplications|RoomTreatmentApplications|RoomTreatmentApplicationSources|RoomTransfers|RoomDepletions|RoomInventoryLosses|BinsRunEntries|ReceiptInventoryOverrides|InventoryIdentityCorrections|InventoryCommands)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static void Check(DbCommand command, CommandEventData eventData)
    {
        if (eventData.Context is CropQcDbContext { CanonicalInventoryEnabled: true, CanonicalCommandTransaction: false }
            && PhysicalDml.Match(command.CommandText) is { Success: true } match)
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
}

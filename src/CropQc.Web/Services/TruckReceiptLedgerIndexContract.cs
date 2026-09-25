using System.Data;
using System.Data.Common;

namespace CropQc.Web.Services;

// Both deployment entry points must enforce the same post-#252 ledger contract.
internal static class TruckReceiptLedgerIndexContract
{
    internal const string IndexName = "IX_RoomInventoryAdjustments_InterCrewTransferId_AdjustmentType";

    internal static async Task<bool> IsSatisfiedAsync(DbConnection connection, string provider, CancellationToken ct)
    {
        var sql = provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            ? """
              SELECT EXISTS (
                SELECT 1 FROM pg_index ix
                JOIN pg_class i ON i.oid=ix.indexrelid
                JOIN pg_class t ON t.oid=ix.indrelid
                JOIN pg_namespace n ON n.oid=t.relnamespace
                JOIN pg_am am ON am.oid=i.relam
                WHERE n.nspname=current_schema() AND t.relname='RoomInventoryAdjustments'
                  AND i.relname='IX_RoomInventoryAdjustments_InterCrewTransferId_AdjustmentType'
                  AND am.amname='btree' AND ix.indisvalid AND ix.indisready AND ix.indislive
                  AND NOT ix.indisunique AND NOT ix.indisprimary AND NOT ix.indisexclusion
                  AND ix.indnkeyatts=2 AND ix.indnatts=2 AND ix.indexprs IS NULL
                  AND pg_get_indexdef(i.oid,1,true)='"InterCrewTransferId"'
                  AND pg_get_indexdef(i.oid,2,true)='"AdjustmentType"'
                  AND ix.indoption[0]=0 AND ix.indoption[1]=0
                  AND NOT EXISTS (
                    SELECT 1 FROM generate_series(0,1) k
                    JOIN pg_opclass opc ON opc.oid=ix.indclass[k]
                    JOIN pg_attribute a ON a.attrelid=t.oid AND a.attnum=ix.indkey[k]
                    WHERE NOT opc.opcdefault OR ix.indcollation[k]<>a.attcollation
                  )
                  AND pg_get_expr(ix.indpred,ix.indrelid,false)='("InterCrewTransferId" IS NOT NULL)'
              );
              """
            : provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase)
                ? """
                  SELECT CONVERT(bit, CASE WHEN EXISTS (
                    SELECT 1 FROM sys.indexes i
                    WHERE i.object_id=OBJECT_ID('RoomInventoryAdjustments')
                      AND i.name='IX_RoomInventoryAdjustments_InterCrewTransferId_AdjustmentType'
                      AND i.type=2 AND i.is_unique=0 AND i.is_primary_key=0
                      AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=1
                      AND REPLACE(REPLACE(REPLACE(i.filter_definition,'(',''),')',''),' ','')='[InterCrewTransferId]ISNOTNULL'
                      AND (SELECT COUNT(*) FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id)=2
                      AND EXISTS (SELECT 1 FROM sys.index_columns c JOIN sys.columns a ON a.object_id=c.object_id AND a.column_id=c.column_id
                        WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal=1 AND c.is_included_column=0 AND c.is_descending_key=0 AND a.name='InterCrewTransferId')
                      AND EXISTS (SELECT 1 FROM sys.index_columns c JOIN sys.columns a ON a.object_id=c.object_id AND a.column_id=c.column_id
                        WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal=2 AND c.is_included_column=0 AND c.is_descending_key=0 AND a.name='AdjustmentType')
                  ) THEN 1 ELSE 0 END);
                  """
                : throw new InvalidOperationException($"Unsupported database provider '{provider}' for ledger index verification.");
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToBoolean(await command.ExecuteScalarAsync(ct));
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }
}

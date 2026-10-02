using System.Data;
using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CropQc.Web.Services;

/// <summary>One exported, read-only snapshot for pg_dump, schema counts and the frozen file list.</summary>
public sealed class BackupSnapshot : IAsyncDisposable
{
    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction transaction;
    public CropQcDbContext Database { get; }
    public string SnapshotId { get; }
    public string Revision { get; }
    public DateTimeOffset CapturedAt { get; }

    private BackupSnapshot(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CropQcDbContext database, string id, string revision, DateTimeOffset capturedAt)
    {
        this.connection = connection;
        this.transaction = transaction;
        Database = database;
        SnapshotId = id;
        Revision = revision;
        CapturedAt = capturedAt;
    }

    public static async Task<BackupSnapshot> OpenAsync(string connectionString, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
                await readOnly.ExecuteNonQueryAsync(ct);
            await using var command = new NpgsqlCommand("SELECT pg_export_snapshot(), pg_current_snapshot()::text, clock_timestamp()", connection, transaction);
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var id = reader.GetString(0);
            var revision = reader.GetString(1);
            var capturedAt = reader.GetFieldValue<DateTimeOffset>(2);
            await reader.DisposeAsync();
            var database = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection).Options);
            await database.Database.UseTransactionAsync(transaction, ct);
            return new(connection, transaction, database, id, revision, capturedAt);
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    // One materialization, no OFFSET or live refresh. No lazy enumerator survives the snapshot.
    public async Task<IReadOnlyList<QcPhoto>> FreezePhotosAsync(CancellationToken ct) =>
        Array.AsReadOnly(await Database.QcPhotos.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(ct));

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}

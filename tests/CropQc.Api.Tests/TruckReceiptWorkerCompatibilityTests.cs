using System.Reflection;
using CropQc.Data;
using CropQc.Shared.Time;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class TruckReceiptWorkerCompatibilityTests
{
    [TruckPreFeaturePostgresFact]
    public async Task New_backup_manifest_reads_old_schema_while_new_web_gate_refuses_it()
    {
        var connection = Environment.GetEnvironmentVariable("CROPQC_TRUCK_PREFEATURE_TEST_CONNECTION")!;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connection);
        await using var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        await Assert.ThrowsAsync<InvalidOperationException>(() => TruckReceiptReleaseSafety.VerifySchemaAsync(db, default));
        await ReadActualBackupManifestAsync(db);
    }

    [TruckPostgresFact]
    public async Task New_backup_manifest_and_schema_gate_read_completed_feature_data()
    {
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync(TruckReceiptPostgresTests.Connection);
        var load = await f.DispatchAsync(70); var receipt = await f.CreateReceiptAsync(70);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, load.Id), default));
        Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, load.Id), default));
        await TruckReceiptReleaseSafety.VerifySchemaAsync(f.Db, default);
        Assert.False(await TruckReceiptReleaseSafety.CanUsePreFeatureApplicationAsync(f.Db, default));
        await ReadActualBackupManifestAsync(f.Db);
    }

    private static async Task ReadActualBackupManifestAsync(CropQcDbContext db)
    {
        // Exercise the worker's real database queries without taking a backup, uploading or acquiring a lease.
        var service = new BackupService(db, new ConfigurationBuilder().Build(), null!, null!, null!,
            new PacificBusinessTimeService(new Clock()), null!, NullLogger<BackupService>.Instance);
        var manifest = (Task<object>)typeof(BackupService).GetMethod("BuildSchemaManifestAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, [CancellationToken.None])!;
        Assert.NotNull(await manifest);
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
}

public sealed class TruckPreFeaturePostgresFactAttribute : FactAttribute
{
    public TruckPreFeaturePostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CROPQC_TRUCK_PREFEATURE_TEST_CONNECTION")))
            Skip = "Set CROPQC_TRUCK_PREFEATURE_TEST_CONNECTION to a disposable pre-feature schema for worker compatibility validation.";
    }
}

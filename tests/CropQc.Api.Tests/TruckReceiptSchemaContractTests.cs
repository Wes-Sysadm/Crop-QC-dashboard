using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace CropQc.Api.Tests;

[Collection("TruckReceiptSchemaContractSerial")]
public sealed class TruckReceiptSchemaContractTests
{
    internal static string? Connection => Environment.GetEnvironmentVariable("CROPQC_TRUCK_SCHEMA_TEST_CONNECTION");
    private const string Index = "\"IX_RoomInventoryAdjustments_InterCrewTransferId_AdjustmentType\"";
    private const string Table = "\"RoomInventoryAdjustments\"";

    [Fact]
    public void Model_requires_ordered_filtered_non_unique_ledger_index()
    {
        using var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>()
            .UseNpgsql("Host=localhost;Database=contract_test;Username=postgres").Options);
        var index = Assert.Single(db.Model.FindEntityType(typeof(RoomInventoryAdjustment))!.GetIndexes(),
            x => x.GetDatabaseName() == "IX_RoomInventoryAdjustments_InterCrewTransferId_AdjustmentType");
        Assert.False(index.IsUnique);
        Assert.Equal(new[] { "InterCrewTransferId", "AdjustmentType" }, index.Properties.Select(x => x.Name));
        Assert.Equal("\"InterCrewTransferId\" IS NOT NULL", index.GetFilter());
    }

    [SchemaContractFact]
    public async Task Exact_feature_schema_passes_both_gates_in_read_only_transaction()
    {
        await using var f = await Fixture.CreateAsync();
        await f.ExecuteAsync("SET TRANSACTION READ ONLY");
        Assert.True(await f.DeploymentGateAsync());
        await TruckReceiptReleaseSafety.VerifySchemaAsync(f.Db, default);
        Assert.False(await f.DeploymentGateAsync("unreviewed-migration"));
    }

    public static IEnumerable<object[]> MalformedIndexes()
    {
        yield return ["old unique", $"CREATE UNIQUE INDEX {Index} ON {Table} (\"InterCrewTransferId\",\"AdjustmentType\") WHERE \"InterCrewTransferId\" IS NOT NULL"];
        yield return ["missing", ""];
        yield return ["wrong column", $"CREATE INDEX {Index} ON {Table} (\"InterCrewTransferId\",\"Id\") WHERE \"InterCrewTransferId\" IS NOT NULL"];
        yield return ["wrong order", $"CREATE INDEX {Index} ON {Table} (\"AdjustmentType\",\"InterCrewTransferId\") WHERE \"InterCrewTransferId\" IS NOT NULL"];
        yield return ["wrong predicate", $"CREATE INDEX {Index} ON {Table} (\"InterCrewTransferId\",\"AdjustmentType\") WHERE \"InterCrewTransferId\" > 0"];
        yield return ["missing predicate", $"CREATE INDEX {Index} ON {Table} (\"InterCrewTransferId\",\"AdjustmentType\")"];
        yield return ["included column", $"CREATE INDEX {Index} ON {Table} (\"InterCrewTransferId\",\"AdjustmentType\") INCLUDE (\"Id\") WHERE \"InterCrewTransferId\" IS NOT NULL"];
        yield return ["extra key", $"CREATE INDEX {Index} ON {Table} (\"InterCrewTransferId\",\"AdjustmentType\",\"Id\") WHERE \"InterCrewTransferId\" IS NOT NULL"];
        yield return ["descending key", $"CREATE INDEX {Index} ON {Table} (\"InterCrewTransferId\" DESC,\"AdjustmentType\") WHERE \"InterCrewTransferId\" IS NOT NULL"];
        yield return ["expression key", $"CREATE INDEX {Index} ON {Table} (\"InterCrewTransferId\",lower(\"AdjustmentType\")) WHERE \"InterCrewTransferId\" IS NOT NULL"];
        yield return ["wrong access method", $"CREATE INDEX {Index} ON {Table} USING brin (\"InterCrewTransferId\",\"AdjustmentType\") WHERE \"InterCrewTransferId\" IS NOT NULL"];
        yield return ["wrong operator class", $"CREATE INDEX {Index} ON {Table} (\"InterCrewTransferId\",\"AdjustmentType\" text_pattern_ops) WHERE \"InterCrewTransferId\" IS NOT NULL"];
        yield return ["wrong collation", $"CREATE INDEX {Index} ON {Table} (\"InterCrewTransferId\",\"AdjustmentType\" COLLATE \"C\") WHERE \"InterCrewTransferId\" IS NOT NULL"];
    }

    [SchemaContractTheory]
    [MemberData(nameof(MalformedIndexes))]
    public async Task Both_gates_reject_incompatible_index(string reason, string replacement)
    {
        Assert.NotEmpty(reason);
        await using var f = await Fixture.CreateAsync();
        await f.ExecuteAsync($"DROP INDEX {Index}");
        if (replacement.Length > 0) await f.ExecuteAsync(replacement);
        Assert.False(await f.DeploymentGateAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => TruckReceiptReleaseSafety.VerifySchemaAsync(f.Db, default));
        // Disposal rolls back the malformed schema; never repair a database to pass a test.
    }

    [SchemaContractTheory]
    [InlineData("DROP INDEX \"IX_InterCrewTransfers_OperationKey\"")]
    [InlineData("ALTER TABLE \"RoomInventoryAdjustments\" DROP CONSTRAINT \"PK_RoomInventoryAdjustments\" CASCADE")]
    [InlineData("ALTER TABLE \"Receipts\" DROP COLUMN \"CanonicalOrchardBlockId\" CASCADE")]
    [InlineData("ALTER TABLE \"ReceiptVarietyLines\" DROP CONSTRAINT \"FK_ReceiptVarietyLines_Receipts_ReceiptId\"")]
    public async Task Other_deployment_or_feature_requirements_still_fail_closed(string damage)
    {
        await using var f = await Fixture.CreateAsync();
        await f.ExecuteAsync(damage);
        if (damage.Contains("ReceiptVarietyLines", StringComparison.Ordinal))
            await Assert.ThrowsAsync<InvalidOperationException>(() => TruckReceiptReleaseSafety.VerifySchemaAsync(f.Db, default));
        else
            Assert.False(await f.DeploymentGateAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly NpgsqlConnection connection;
        private readonly NpgsqlTransaction transaction;
        private readonly ServiceProvider services;
        public CropQcDbContext Db { get; }

        private Fixture(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            this.connection = connection;
            this.transaction = transaction;
            Db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection).Options);
            Db.Database.UseTransaction(transaction);
            services = new ServiceCollection().AddLogging().AddScoped(_ =>
            {
                var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection).Options);
                db.Database.UseTransaction(transaction);
                return db;
            }).BuildServiceProvider();
        }

        public static async Task<Fixture> CreateAsync()
        {
            ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(Connection!);
            var connection = new NpgsqlConnection(Connection);
            await connection.OpenAsync();
            return new Fixture(connection, await connection.BeginTransactionAsync());
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }

        public Task<bool> DeploymentGateAsync(string expectedMigration = DatabaseStartupDiagnostics.ExpectedSchemaMigration) =>
            DatabaseStartupDiagnostics.VerifyRequiredSchemaAsync(services, new ConfigurationBuilder().Build(), new TestEnvironment(), expectedMigration);

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await Db.DisposeAsync();
            await transaction.RollbackAsync();
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "CropQc.Web";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

[CollectionDefinition("TruckReceiptSchemaContractSerial", DisableParallelization = true)]
public sealed class TruckReceiptSchemaContractCollection { }

public sealed class SchemaContractFactAttribute : FactAttribute
{
    public SchemaContractFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(TruckReceiptSchemaContractTests.Connection))
            Skip = "Set CROPQC_TRUCK_SCHEMA_TEST_CONNECTION to a disposable database with the approved Truck Receipt schema.";
    }
}

public sealed class SchemaContractTheoryAttribute : TheoryAttribute
{
    public SchemaContractTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(TruckReceiptSchemaContractTests.Connection))
            Skip = "Set CROPQC_TRUCK_SCHEMA_TEST_CONNECTION to a disposable database with the approved Truck Receipt schema.";
    }
}

using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace CropQc.Api.Tests;

public sealed class InventoryIdentityServiceTests
{
    [Fact]
    public async Task Correction_chain_resolves_to_final_canonical_identity()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        fixture.AddCorrection(99101, 99102, "a-to-b");
        fixture.AddCorrection(99102, 99103, "b-to-c");
        await fixture.Db.SaveChangesAsync();

        var result = await new InventoryIdentityService(fixture.Db)
            .ResolveAsync(new InventoryIdentityKey(2026, 99110, 99101), CancellationToken.None);

        Assert.Equal(new InventoryIdentityKey(2026, 99110, 99103), result.Canonical);
        Assert.Equal(2, result.CorrectionChain.Count);
        Assert.Equal("FP3", result.FruitProfile.VarietyCode);
    }

    [Fact]
    public async Task Self_correction_is_rejected()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var error = await new InventoryIdentityService(fixture.Db).ValidateCorrectionAsync(
            new(2026, 99110, 99101), new(2026, 99110, 99101), CancellationToken.None);
        Assert.Contains("different", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cycle_attempt_is_rejected()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        fixture.AddCorrection(99101, 99102, "a-to-b");
        await fixture.Db.SaveChangesAsync();
        var error = await new InventoryIdentityService(fixture.Db).ValidateCorrectionAsync(
            new(2026, 99110, 99102), new(2026, 99110, 99101), CancellationToken.None);
        Assert.Contains("cycle", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Superseded_write_guard_rejects_historical_identity()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        fixture.AddCorrection(99101, 99102, "a-to-b");
        await fixture.Db.SaveChangesAsync();
        var error = await InventoryIdentityWriteGuard.RejectSupersededAsync(
            fixture.Db, 2026, 99110, 99101, "Test reversal", CancellationToken.None);
        Assert.Contains("superseded", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026/99110/99102", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_snapshot_normalizes_all_fruit_profile_and_grower_fields()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        fixture.AddCorrection(99101, 99103, "a-to-c");
        await fixture.Db.SaveChangesAsync();
        var source = new RoomInventoryLedgerSnapshot(1, "MCD", 8, "MCD-08", "MCD", 2026, 99110, 99101,
            "stale", "stale", "stale", null, "OLD", "OLD", "Old", "Pear", "Conventional", false,
            "Conventional", 40, 0, 0, 0, 0, 0, 0, 0, 0, 40, 1, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, 1);
        var resolved = await new InventoryIdentityService(fixture.Db).ResolveSnapshotAsync(source, CancellationToken.None);
        Assert.Equal((99103, "FP3", "Organic", true, "G10", "Grower Ten"),
            (resolved.FruitProfileId, resolved.Variety, resolved.ProductionType, resolved.IsOrganic,
                resolved.Lot, resolved.Grower));
    }

    [Fact]
    public async Task PostgreSql_simultaneous_corrections_allow_exactly_one_active_source_mapping_when_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_TEST_INVENTORY_IDENTITY_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        var seed = Random.Shared.Next(960000, 980000);
        var options = new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connectionString).Options;

        await using var setup = new CropQcDbContext(options);
        setup.Users.Add(new User { Id = seed, Email = $"identity-{seed}@example.invalid", DisplayName = "Identity Concurrency", Domain = "example.invalid", CreatedAt = DateTimeOffset.UtcNow });
        setup.GrowerLots.Add(new GrowerLot { Id = seed, Grower = "Concurrency Grower", LotNumber = seed.ToString(), IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        setup.FruitProfiles.AddRange(
            new FruitProfile { Id = seed, Name = "Source", VarietyCode = $"S{seed}", FruitType = "Pear", ProductionType = "Conventional" },
            new FruitProfile { Id = seed + 1, Name = "Target One", VarietyCode = $"T{seed}", FruitType = "Pear", ProductionType = "Organic", IsOrganic = true },
            new FruitProfile { Id = seed + 2, Name = "Target Two", VarietyCode = $"U{seed}", FruitType = "Pear", ProductionType = "Organic", IsOrganic = true });
        await setup.SaveChangesAsync();

        try
        {
            await using var first = new CropQcDbContext(options);
            await using var second = new CropQcDbContext(options);
            await using var firstTransaction = await first.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await using var secondTransaction = await second.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            first.InventoryIdentityCorrections.Add(ConcurrentCorrection(seed, seed + 1, "first"));
            second.InventoryIdentityCorrections.Add(ConcurrentCorrection(seed, seed + 2, "second"));
            await first.SaveChangesAsync();
            var secondWrite = second.SaveChangesAsync();
            await Task.Delay(100);
            Assert.False(secondWrite.IsCompleted);
            await firstTransaction.CommitAsync();
            await Assert.ThrowsAsync<DbUpdateException>(async () => await secondWrite);
            await secondTransaction.RollbackAsync();

            await using var verify = new CropQcDbContext(options);
            var saved = await verify.InventoryIdentityCorrections
                .Where(x => x.SourceCropYear == 2026 && x.SourceGrowerLotId == seed && x.SourceFruitProfileId == seed)
                .ToListAsync();
            Assert.Single(saved);
            Assert.Equal(seed + 1, saved[0].TargetFruitProfileId);
        }
        finally
        {
            await setup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"InventoryIdentityCorrections\" WHERE \"SourceGrowerLotId\" = {seed}");
            await setup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"FruitProfiles\" WHERE \"Id\" IN ({seed}, {seed + 1}, {seed + 2})");
            await setup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"GrowerLots\" WHERE \"Id\" = {seed}");
            await setup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Users\" WHERE \"Id\" = {seed}");
        }
    }

    private static InventoryIdentityCorrection ConcurrentCorrection(int source, int target, string suffix) => new()
    {
        Id = Guid.NewGuid(),
        OperationKey = $"identity-concurrency-{source}-{suffix}",
        SourceCropYear = 2026,
        SourceGrowerLotId = source,
        SourceFruitProfileId = source,
        TargetCropYear = 2026,
        TargetGrowerLotId = source,
        TargetFruitProfileId = target,
        Reason = "PostgreSQL simultaneous correction test",
        CreatedByUserId = source,
        CreatedAt = DateTimeOffset.UtcNow,
        SourceIdentitySnapshotJson = "{}",
        TargetIdentitySnapshotJson = "{}",
        IsComplete = true,
        IsActive = true
    };

    private sealed class IdentityFixture(SqliteConnection connection, CropQcDbContext db) : IAsyncDisposable
    {
        public CropQcDbContext Db { get; } = db;

        public static async Task<IdentityFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new User { Id = 99100, Email = "identity@example.com", DisplayName = "Identity Admin", Domain = "example.com", CreatedAt = DateTimeOffset.UtcNow });
            db.GrowerLots.Add(new GrowerLot { Id = 99110, Grower = "Grower Ten", LotNumber = "G10", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            db.FruitProfiles.AddRange(
                new FruitProfile { Id = 99101, Name = "One", VarietyCode = "FP1", FruitType = "Pear", ProductionType = "Conventional", IsOrganic = false },
                new FruitProfile { Id = 99102, Name = "Two", VarietyCode = "FP2", FruitType = "Pear", ProductionType = "Organic", IsOrganic = true },
                new FruitProfile { Id = 99103, Name = "Three", VarietyCode = "FP3", FruitType = "Pear", ProductionType = "Organic", IsOrganic = true });
            await db.SaveChangesAsync();
            return new(connection, db);
        }

        public void AddCorrection(int sourceFruit, int targetFruit, string operationKey) => Db.InventoryIdentityCorrections.Add(new()
        {
            Id = Guid.NewGuid(),
            OperationKey = operationKey,
            SourceCropYear = 2026,
            SourceGrowerLotId = 99110,
            SourceFruitProfileId = sourceFruit,
            TargetCropYear = 2026,
            TargetGrowerLotId = 99110,
            TargetFruitProfileId = targetFruit,
            Reason = "test",
            CreatedByUserId = 99100,
            CreatedAt = DateTimeOffset.UtcNow,
            SourceIdentitySnapshotJson = "{}",
            TargetIdentitySnapshotJson = "{}",
            IsComplete = true,
            IsActive = true
        });

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}

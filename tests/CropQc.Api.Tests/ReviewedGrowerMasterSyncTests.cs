using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Api.Dtos;
using CropQc.Api.Services;
using CropQc.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class ReviewedGrowerMasterSyncTests
{
    [Fact]
    public async Task ReviewedSource_HasExactReviewedWorkbookIdentityAndCounts()
    {
        var source = Source();
        var master = await source.LoadAsync(CancellationToken.None);

        Assert.Equal("pool.xlsx", master.WorkbookFileName);
        Assert.Equal(31_013, master.WorkbookSizeBytes);
        Assert.Equal("dc34005faca9dc241977c4680d9d52b7dc6682efff5246591ff43ff303fd4e6b", master.WorkbookSha256);
        Assert.Equal("e49848f40bff96ef256ab5bf51a9ee9cb1c9aa6f88c1b1b4dc51ec712157afb2", master.AssetSha256);
        Assert.Equal(405, master.Rows.Count);
        Assert.Equal(389, master.Rows.Count(x => x.IsActive));
        Assert.Equal(16, master.Rows.Count(x => !x.IsActive));
        Assert.DoesNotContain(master.Rows.GroupBy(x => x.GrowerNumber), x => x.Count() > 1);
    }

    [Theory]
    [InlineData("1050", "MFR - FUJI ORCH-BLK E", "FE")]
    [InlineData("1080", "WINDY POINT", "WP")]
    [InlineData("1082", "WP Orchard - EP Non-Chilean", "WN")]
    [InlineData("9660", "MFR - SONBAY", "SO")]
    public async Task ReviewedSource_ContainsExpectedAuthoritativeNames(string number, string name, string pool)
    {
        var row = Assert.Single((await Source().LoadAsync(CancellationToken.None)).Rows, x => x.GrowerNumber == number);
        Assert.True(row.IsActive);
        Assert.Equal(name, row.GrowerName);
        Assert.Equal(pool, row.Pool);
    }

    [Theory]
    [InlineData("1060", null)]
    [InlineData("1061", "2400")]
    [InlineData("1062", "3900")]
    [InlineData("1063", "3900")]
    [InlineData("1100", null)]
    [InlineData("1200", null)]
    [InlineData("1220", null)]
    [InlineData("1250", null)]
    [InlineData("1280", null)]
    [InlineData("1300", null)]
    [InlineData("1400", null)]
    [InlineData("1500", null)]
    [InlineData("3000", null)]
    [InlineData("4800", null)]
    [InlineData("6666", null)]
    [InlineData("9636", null)]
    public async Task ReviewedSource_InactiveRowsAreExplicitAndNeverUseLiteralInactiveName(string number, string? redirect)
    {
        var row = Assert.Single((await Source().LoadAsync(CancellationToken.None)).Rows, x => x.GrowerNumber == number);
        Assert.False(row.IsActive);
        Assert.Empty(row.GrowerName);
        Assert.Equal(redirect, row.RedirectToGrowerNumber);
    }

    [Fact]
    public async Task Resolver_ExactNumbersRemainDistinctWhenAuthoritativeNameIsShared()
    {
        await using var db = InMemoryDb();
        AddMappedGrower(db, "9950", "HARSHFIELD FARMS");
        AddMappedGrower(db, "9960", "HARSHFIELD FARMS");
        await db.SaveChangesAsync();

        var resolver = await new CanonicalGrowerService(db).LoadResolutionSetAsync(CancellationToken.None);
        var first = resolver.Resolve("HARSHFIELD FARMS", "9950");
        var second = resolver.Resolve("HARSHFIELD FARMS", "9960");

        Assert.True(first.IsMapped);
        Assert.True(second.IsMapped);
        Assert.NotEqual(first.Key, second.Key);
        Assert.Equal(new[] { "9950", "9960" }, resolver.MatchingGrowerNumbers("HARSHFIELD FARMS"));
    }

    [Fact]
    public async Task Resolver_AmbiguousAliasDoesNotGuessButExactNumberWins()
    {
        await using var db = InMemoryDb();
        AddMappedGrower(db, "1080", "WINDY POINT", "WP ORCHARD");
        AddMappedGrower(db, "1082", "WP Orchard - EP Non-Chilean", "WP ORCHARD");
        await db.SaveChangesAsync();

        var resolver = await new CanonicalGrowerService(db).LoadResolutionSetAsync(CancellationToken.None);

        Assert.False(resolver.Resolve("WP ORCHARD", null).IsMapped);
        Assert.Equal("WINDY POINT", resolver.Resolve("WP ORCHARD", "1080").DisplayName);
        Assert.Equal("WP Orchard - EP Non-Chilean", resolver.Resolve("WP ORCHARD", "1082").DisplayName);
        Assert.Equal("WP ORCHARD", resolver.DisplayName("WP ORCHARD", null));
    }

    [Fact]
    public async Task ApiReceiptCreate_UsesAuthoritativeNameForKnownNumberAndPreservesUnknownName()
    {
        await using var db = InMemoryDb();
        AddMappedGrower(db, "1080", "WINDY POINT", "WP ORCHARD");
        await db.SaveChangesAsync();
        var service = new ReceiptService(db, new AuditService(db));

        var known = await service.CreateAsync(new CreateReceiptRequest(
            2026,
            DateTimeOffset.UtcNow,
            "KNOWN-1080",
            1,
            1,
            1,
            "WP ORCHARD",
            "1080",
            10), CancellationToken.None);
        var unknown = await service.CreateAsync(new CreateReceiptRequest(
            2026,
            DateTimeOffset.UtcNow,
            "UNKNOWN-7777",
            1,
            1,
            1,
            "Unmapped Grower",
            "7777",
            5), CancellationToken.None);

        Assert.Null(known.Error);
        Assert.Equal("WINDY POINT", known.Receipt?.GrowerName);
        Assert.Equal("WINDY POINT", (await db.Receipts.SingleAsync(x => x.CompuTechReceiptId == "KNOWN-1080")).GrowerName);
        Assert.Null(unknown.Error);
        Assert.Equal("Unmapped Grower", unknown.Receipt?.GrowerName);
    }

    [Fact]
    public async Task Sync_DryRunApplyAndRerunAreGuardedAtomicAndIdempotent()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var beforeReceipts = await fixture.Db.Receipts.CountAsync();
        var dryRun = await fixture.Service.RunAsync(Request(apply: false), CancellationToken.None);

        Assert.True(dryRun.Success);
        Assert.False(dryRun.Applied);
        Assert.Equal("Ready", dryRun.Preflight.State);
        Assert.Empty(dryRun.Preflight.Issues);
        Assert.Equal(16, dryRun.Preflight.InactiveRows.Count);
        Assert.All(dryRun.Preflight.InactiveRows, x => Assert.False(x.HasProductionEvidence));
        Assert.Equal(0, await fixture.Db.CanonicalGrowerNumbers.CountAsync());

        var applied = await fixture.Service.RunAsync(Request(
            apply: true,
            target: dryRun.Preflight.TargetFingerprint,
            protectedFingerprint: dryRun.Preflight.ProtectedFingerprint), CancellationToken.None);

        Assert.True(applied.Success);
        Assert.True(applied.Applied);
        Assert.Equal(389, await fixture.Db.CanonicalGrowerNumbers.CountAsync(x => x.IsActive && x.SourceSystem == ReviewedGrowerMasterConstants.SourceSystem));
        Assert.Equal(beforeReceipts, await fixture.Db.Receipts.CountAsync());
        Assert.False(await fixture.Db.CanonicalGrowerNumbers.AnyAsync(x => new[] { "1060", "1061", "1062", "1063", "1100", "1200", "1220", "1250", "1280", "1300", "1400", "1500", "3000", "4800", "6666", "9636" }.Contains(x.NormalizedGrowerNumber)));
        Assert.DoesNotContain(await fixture.Db.CanonicalGrowers.ToListAsync(), x => x.DisplayName.Contains("INACTIVE", StringComparison.OrdinalIgnoreCase));
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.EntityName == ReviewedGrowerMasterSyncConstants.AuditEntityName).ToListAsync());

        var rerun = await fixture.Service.RunAsync(Request(apply: true), CancellationToken.None);
        Assert.True(rerun.Success);
        Assert.True(rerun.AlreadyApplied);
        Assert.False(rerun.Applied);
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.EntityName == ReviewedGrowerMasterSyncConstants.AuditEntityName).ToListAsync());
    }

    [Fact]
    public async Task Sync_RejectsChangedFingerprintsAndUnauthorizedAdmin()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var dryRun = await fixture.Service.RunAsync(Request(apply: false), CancellationToken.None);
        var mismatch = await fixture.Service.RunAsync(Request(true, "wrong", dryRun.Preflight.ProtectedFingerprint), CancellationToken.None);
        var wrongAdmin = await fixture.Service.RunAsync(Request(true, dryRun.Preflight.TargetFingerprint, dryRun.Preflight.ProtectedFingerprint) with { RequestedByEmail = "missing@example.com" }, CancellationToken.None);
        var wrongToken = await fixture.Service.RunAsync(Request(true, dryRun.Preflight.TargetFingerprint, dryRun.Preflight.ProtectedFingerprint) with { AuthorizationToken = "wrong" }, CancellationToken.None);
        var wrongBackup = await fixture.Service.RunAsync(Request(true, dryRun.Preflight.TargetFingerprint, dryRun.Preflight.ProtectedFingerprint) with { VerifiedBackupPackageSha256 = "wrong" }, CancellationToken.None);

        Assert.False(mismatch.Success);
        Assert.False(wrongAdmin.Success);
        Assert.False(wrongToken.Success);
        Assert.False(wrongBackup.Success);
        Assert.Equal(0, await fixture.Db.CanonicalGrowerNumbers.CountAsync());
        Assert.Empty(await fixture.Db.AuditLogs.Where(x => x.EntityName == ReviewedGrowerMasterSyncConstants.AuditEntityName).ToListAsync());
    }

    [Fact]
    public async Task Sync_ForcedFailureRollsBackEveryMasterWrite()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var dryRun = await fixture.Service.RunAsync(Request(apply: false), CancellationToken.None);
        await fixture.Db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER FailReviewedGrowerAudit
            BEFORE INSERT ON AuditLogs
            WHEN NEW.EntityName = 'ReviewedGrowerMasterSync'
            BEGIN
                SELECT RAISE(ABORT, 'forced reviewed grower sync failure');
            END;
            """);

        var result = await fixture.Service.RunAsync(Request(
            apply: true,
            target: dryRun.Preflight.TargetFingerprint,
            protectedFingerprint: dryRun.Preflight.ProtectedFingerprint), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.Applied);
        Assert.Equal(0, await fixture.Db.CanonicalGrowerNumbers.CountAsync());
        Assert.Empty(await fixture.Db.CanonicalGrowers.Where(x => x.NormalizedKey.StartsWith("REVIEWED_GROWER_NUMBER_")).ToListAsync());
        Assert.Empty(await fixture.Db.AuditLogs.Where(x => x.EntityName == ReviewedGrowerMasterSyncConstants.AuditEntityName).ToListAsync());
    }

    [Fact]
    public async Task ReviewedSource_ChangedAssetFailsClosed()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cropqc-reviewed-growers-{Guid.NewGuid():N}");
        var assetDirectory = Path.Combine(tempRoot, "Data", "ReviewedGrowers");
        Directory.CreateDirectory(assetDirectory);
        try
        {
            var source = new ReviewedGrowerMasterSource(new TestEnvironment { ContentRootPath = tempRoot });
            await File.WriteAllTextAsync(
                Path.Combine(assetDirectory, "authoritative-growers-2026.csv"),
                "GrowerNumber,GrowerName,Pool,Status,RedirectToGrowerNumber\n1080,WINDY POINT,WP,Active,\n");
            await Assert.ThrowsAsync<InvalidOperationException>(() => source.LoadAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Sync_RefusesInactiveNumberWithProductionEvidence()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        fixture.Db.Warehouses.Add(new Warehouse { Id = 100, Code = "TEST", Name = "Test" });
        fixture.Db.Rooms.Add(new Room { Id = 100, WarehouseId = 100, Code = "TEST", Name = "Test" });
        fixture.Db.FruitProfiles.Add(new FruitProfile
        {
            Id = 100,
            Name = "Test",
            VarietyCode = "TEST",
            FruitType = "Apple",
            ProductionType = "Conventional"
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.Receipts.Add(new Receipt
        {
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.UtcNow,
            CompuTechReceiptId = "INACTIVE-EVIDENCE",
            WarehouseId = 100,
            RoomId = 100,
            FruitProfileId = 100,
            GrowerNumber = "1061",
            GrowerName = "Historical grower",
            LotCode = "1061",
            BinCount = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var preflight = await fixture.Service.PreflightAsync(CancellationToken.None);

        Assert.Equal("Refused", preflight.State);
        Assert.Contains(preflight.Issues, x => x.Contains("1061", StringComparison.Ordinal));
        Assert.True(Assert.Single(preflight.InactiveRows, x => x.GrowerNumber == "1061").HasProductionEvidence);
    }

    private static ReviewedGrowerMasterSyncRequest Request(bool apply, string? target = null, string? protectedFingerprint = null) => new(
        apply,
        false,
        true,
        ReviewedGrowerMasterSyncConstants.VerifiedRestoreBackupRunId,
        ReviewedGrowerMasterSyncConstants.VerifiedRestorePackageSha256,
        "admin@example.com",
        "Reviewed authoritative grower-number master sync test.",
        target,
        protectedFingerprint,
        ReviewedGrowerMasterSyncConstants.ApplyAuthorizationToken);

    private static ReviewedGrowerMasterSource Source() => new(new TestEnvironment { ContentRootPath = FindRepositoryDirectory("src", "CropQc.Web") });

    private static CropQcDbContext InMemoryDb() => new(new DbContextOptionsBuilder<CropQcDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static void AddMappedGrower(CropQcDbContext db, string number, string name, params string[] aliases)
    {
        var now = DateTimeOffset.UtcNow;
        var grower = new CanonicalGrower { DisplayName = name, NormalizedKey = $"REVIEWED_GROWER_NUMBER_{number}", IsActive = true, CreatedAt = now, UpdatedAt = now };
        grower.GrowerNumbers.Add(new CanonicalGrowerNumber { GrowerNumber = number, NormalizedGrowerNumber = number, IsActive = true, CreatedAt = now, UpdatedAt = now });
        grower.Aliases.Add(new CanonicalGrowerAlias { AliasName = name, NormalizedAliasKey = CanonicalGrowerService.NormalizeGrowerKey(name), IsActive = true, CreatedAt = now, UpdatedAt = now });
        foreach (var alias in aliases) grower.Aliases.Add(new CanonicalGrowerAlias { AliasName = alias, NormalizedAliasKey = CanonicalGrowerService.NormalizeGrowerKey(alias), IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.CanonicalGrowers.Add(grower);
    }

    private static string FindRepositoryDirectory(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "CropQc.Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public CropQcDbContext Db { get; }
        public ReviewedGrowerMasterSyncService Service { get; }

        private SqliteFixture(SqliteConnection connection, CropQcDbContext db)
        {
            this.connection = connection;
            Db = db;
            Service = new ReviewedGrowerMasterSyncService(
                db,
                Source(),
                new AppEnvironmentOptions { Kind = AppEnvironmentKinds.Development },
                NullLogger<ReviewedGrowerMasterSyncService>.Instance);
        }

        public static async Task<SqliteFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var admin = new User
            {
                Email = "admin@example.com",
                DisplayName = "Admin",
                Domain = "example.com",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = 1 });
            db.BackupRunRecords.Add(new BackupRunRecord
            {
                Id = ReviewedGrowerMasterSyncConstants.VerifiedRestoreBackupRunId,
                BackupType = BackupRunTypes.PreDeployment,
                Status = BackupRunStatuses.Running,
                EnvironmentName = "Production",
                DatabaseProvider = "Npgsql",
                RetentionCategory = BackupRunTypes.PreDeployment,
                StartedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            return new SqliteFixture(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}

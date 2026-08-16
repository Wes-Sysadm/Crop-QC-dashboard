using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Api.Dtos;
using CropQc.Api.Services;
using CropQc.Web.Services;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualBasic.FileIO;

namespace CropQc.Api.Tests;

public sealed class ReviewedGrowerMasterSyncTests
{
    [Fact]
    public async Task ReviewedSource_HasExactReviewedWorkbookIdentityAndCounts()
    {
        var source = Source();
        var master = await source.LoadAsync(CancellationToken.None);

        Assert.Equal("pool(2).xlsx", master.WorkbookFileName);
        Assert.Equal(40_009, master.WorkbookSizeBytes);
        Assert.Equal("13fa493a1dae9573a693cb9e43baeaa04fd51e583abedab1e0338144566ef409", master.WorkbookSha256);
        Assert.Equal("39d89f8a07aa60a0b23b3f54012345818687b3f20f9152898476f1ef78fd7ff9", master.AssetSha256);
        Assert.Equal("Sheet1", ReviewedGrowerMasterConstants.WorkbookSheetName);
        Assert.Equal("A1:C670", ReviewedGrowerMasterConstants.WorkbookRange);
        Assert.Equal("#,Grower,POOL Starts", ReviewedGrowerMasterConstants.WorkbookHeader);
        Assert.Equal(669, master.Rows.Count);
        Assert.Equal(643, master.Rows.Count(x => x.IsActive));
        Assert.Equal(26, master.Rows.Count(x => !x.IsActive));
        Assert.DoesNotContain(master.Rows.GroupBy(x => x.GrowerNumber), x => x.Count() > 1);
    }

    [Theory]
    [InlineData("1050", "MFR - FUJI BLK E ORG", "FE")]
    [InlineData("1080", "WP ORCHARD ORG CHIL", "WP")]
    [InlineData("1082", "EAST POINT ORG", "WN")]
    [InlineData("1084", "WP ORCHARD CONV", "WQ")]
    [InlineData("1085", "WP ORCHARD ORG", "WC")]
    [InlineData("1530", "Baldwin Pears ORG", "BA")]
    [InlineData("1531", "Baldwin Pears ORG CHIL", "B8")]
    [InlineData("1532", "Baldwin Pears CONV", "B9")]
    [InlineData("9392", "MFR - HOOKER PL CONV", "HX")]
    public async Task ReviewedSource_ContainsExpectedAuthoritativeNames(string number, string name, string pool)
    {
        var row = Assert.Single((await Source().LoadAsync(CancellationToken.None)).Rows, x => x.GrowerNumber == number);
        Assert.True(row.IsActive);
        Assert.Equal(name, row.GrowerName);
        Assert.Equal(pool, row.Pool);
    }

    [Fact]
    public async Task ReviewedSource_ContainsAllPreviouslyUnmappedProductionNumbersAndOnlyOnePoolDifference()
    {
        var master = await Source().LoadAsync(CancellationToken.None);
        var expected = new[] { "1084", "1112", "1121", "1162", "1361", "1391", "1522", "1531", "1532", "1538", "1539", "1543", "1558", "2350", "3152", "3162", "4302", "4402", "4701", "9092", "9201", "9312", "9332", "9333", "9342", "9362", "9372", "9392", "9401", "9418", "9671", "9682" };

        Assert.All(expected, number => Assert.Contains(master.Rows, x => x.GrowerNumber == number && x.IsActive));
        Assert.Equal("2S", Assert.Single(master.Rows, x => x.GrowerNumber == "3805").Pool);
    }

    [Theory]
    [InlineData("1060")]
    [InlineData("1061")]
    [InlineData("1062")]
    [InlineData("1063")]
    [InlineData("1100")]
    [InlineData("1200")]
    [InlineData("1220")]
    [InlineData("1250")]
    [InlineData("1280")]
    [InlineData("1300")]
    [InlineData("1400")]
    [InlineData("1500")]
    [InlineData("1800")]
    [InlineData("1900")]
    [InlineData("2100")]
    [InlineData("2300")]
    [InlineData("2500")]
    [InlineData("3000")]
    [InlineData("3340")]
    [InlineData("3500")]
    [InlineData("3540")]
    [InlineData("3570")]
    [InlineData("4800")]
    [InlineData("6000")]
    [InlineData("6666")]
    [InlineData("9636")]
    public async Task ReviewedSource_InactiveRowsAreExplicitAndNeverUseLiteralInactiveNameOrInferredRedirect(string number)
    {
        var row = Assert.Single((await Source().LoadAsync(CancellationToken.None)).Rows, x => x.GrowerNumber == number);
        Assert.False(row.IsActive);
        Assert.Empty(row.GrowerName);
        Assert.Null(row.RedirectToGrowerNumber);
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
        AddMappedGrower(db, "1080", "WP ORCHARD ORG CHIL", "WP ORCHARD");
        AddMappedGrower(db, "1082", "EAST POINT ORG", "WP ORCHARD");
        await db.SaveChangesAsync();

        var resolver = await new CanonicalGrowerService(db).LoadResolutionSetAsync(CancellationToken.None);

        Assert.False(resolver.Resolve("WP ORCHARD", null).IsMapped);
        Assert.Equal("WP ORCHARD ORG CHIL", resolver.Resolve("WP ORCHARD", "1080").DisplayName);
        Assert.Equal("EAST POINT ORG", resolver.Resolve("WP ORCHARD", "1082").DisplayName);
        Assert.Equal("WP ORCHARD", resolver.DisplayName("WP ORCHARD", null));
    }

    [Fact]
    public async Task ApiReceiptCreate_UsesAuthoritativeNameForKnownNumberAndPreservesUnknownName()
    {
        await using var db = InMemoryDb();
        AddMappedGrower(db, "1080", "WP ORCHARD ORG CHIL", "WINDY POINT");
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
        Assert.Equal("WP ORCHARD ORG CHIL", known.Receipt?.GrowerName);
        Assert.Equal("WP ORCHARD ORG CHIL", (await db.Receipts.SingleAsync(x => x.CompuTechReceiptId == "KNOWN-1080")).GrowerName);
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
        Assert.Equal(26, dryRun.Preflight.InactiveRows.Count);
        Assert.All(dryRun.Preflight.InactiveRows, x => Assert.False(x.HasProductionEvidence));
        Assert.Equal(0, await fixture.Db.CanonicalGrowerNumbers.CountAsync());

        var applied = await fixture.Service.RunAsync(Request(
            apply: true,
            target: dryRun.Preflight.TargetFingerprint,
            protectedFingerprint: dryRun.Preflight.ProtectedFingerprint), CancellationToken.None);

        Assert.True(applied.Success);
        Assert.True(applied.Applied);
        Assert.Equal(643, await fixture.Db.CanonicalGrowerNumbers.CountAsync(x => x.IsActive && x.SourceSystem == ReviewedGrowerMasterConstants.SourceSystem));
        Assert.Equal(beforeReceipts, await fixture.Db.Receipts.CountAsync());
        Assert.False(await fixture.Db.CanonicalGrowerNumbers.AnyAsync(x => new[] { "1060", "1061", "1062", "1063", "1100", "1200", "1220", "1250", "1280", "1300", "1400", "1500", "1800", "1900", "2100", "2300", "2500", "3000", "3340", "3500", "3540", "3570", "4800", "6000", "6666", "9636" }.Contains(x.NormalizedGrowerNumber)));
        Assert.DoesNotContain(await fixture.Db.CanonicalGrowers.ToListAsync(), x => x.DisplayName.Contains("INACTIVE", StringComparison.OrdinalIgnoreCase));
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.EntityName == ReviewedGrowerMasterSyncConstants.AuditEntityName).ToListAsync());

        var rerun = await fixture.Service.RunAsync(Request(apply: true), CancellationToken.None);
        Assert.True(rerun.Success);
        Assert.True(rerun.AlreadyApplied);
        Assert.False(rerun.Applied);
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.EntityName == ReviewedGrowerMasterSyncConstants.AuditEntityName).ToListAsync());
    }

    [Fact]
    public async Task Sync_UpdatesAppliedV1MasterToV2ByExactNumberAndPreservesHistoricalOperationalRows()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        await SeedPreviousReviewedMasterAsync(fixture.Db);
        var old1080 = await fixture.Db.CanonicalGrowers.Include(x => x.GrowerNumbers).SingleAsync(x => x.GrowerNumbers.Any(y => y.NormalizedGrowerNumber == "1080"));
        var old1080Id = old1080.Id;
        var protectedBefore = await CaptureOperationalCountsAsync(fixture.Db);

        var dryRun = await fixture.Service.RunAsync(Request(apply: false), CancellationToken.None);

        Assert.True(dryRun.Success);
        Assert.Equal("Ready", dryRun.Preflight.State);
        Assert.Equal(ReviewedGrowerMasterConstants.PreviousSourceVersion, dryRun.Preflight.PreviousAppliedSourceVersion);
        Assert.Equal(264, dryRun.Preflight.CanonicalGrowersToCreate);
        Assert.Equal(385, dryRun.Preflight.CanonicalGrowersToUpdate);
        Assert.Equal(264, dryRun.Preflight.NumberMappingsToCreate);
        Assert.Equal(379, dryRun.Preflight.NumberMappingsToUpdate);
        Assert.Equal(new[] { "1800", "1900", "2100", "2300", "2500", "3340", "3500", "3540", "3570", "6000" }, dryRun.Preflight.ActiveToInactiveNumberMappings);
        Assert.Equal("2S", Assert.Single(dryRun.Preflight.PoolOnlyDifferences, x => x.GrowerNumber == "3805").CurrentPool);
        var ambiguous = Assert.Single(dryRun.Preflight.AliasDecisions, x => x.NormalizedAliasKey == "HARSHFIELD_FARMS");
        Assert.Equal("SkippedAmbiguous", ambiguous.Disposition);
        Assert.Equal(new[] { "9950", "9960" }, ambiguous.GrowerNumbers);

        var result = await fixture.Service.RunAsync(Request(true, dryRun.Preflight.TargetFingerprint, dryRun.Preflight.ProtectedFingerprint), CancellationToken.None);

        Assert.True(result.Success, $"{result.Message} Error={fixture.Logger.LastException}; Postflight={result.Preflight.State}; issues={string.Join(" | ", result.Preflight.Issues)}");
        Assert.True(result.Applied, result.Message);
        Assert.Equal(264, result.CanonicalGrowersCreated);
        Assert.Equal(10, result.NumberMappingsDeactivated);
        Assert.Equal(protectedBefore, await CaptureOperationalCountsAsync(fixture.Db));
        var updated1080 = await fixture.Db.CanonicalGrowers.Include(x => x.GrowerNumbers).Include(x => x.Aliases).SingleAsync(x => x.GrowerNumbers.Any(y => y.NormalizedGrowerNumber == "1080"));
        Assert.Equal(old1080Id, updated1080.Id);
        Assert.Equal("WP ORCHARD ORG CHIL", updated1080.DisplayName);
        Assert.Contains(updated1080.Aliases, x => x.IsActive && x.AliasName == "WINDY POINT");
        Assert.Equal("WP ORCHARD CONV", await CurrentNameAsync(fixture.Db, "1084"));
        Assert.Equal("WP ORCHARD ORG", await CurrentNameAsync(fixture.Db, "1085"));
        Assert.Equal("MFR - HOOKER PL CONV", await CurrentNameAsync(fixture.Db, "9392"));
        Assert.Equal(3, await fixture.Db.CanonicalGrowerNumbers.CountAsync(x => new[] { "1530", "1531", "1532" }.Contains(x.NormalizedGrowerNumber) && x.IsActive));
        Assert.All(await fixture.Db.CanonicalGrowerNumbers.Where(x => new[] { "1800", "1900", "2100", "2300", "2500", "3340", "3500", "3540", "3570", "6000" }.Contains(x.NormalizedGrowerNumber)).ToListAsync(), x => Assert.False(x.IsActive));
        Assert.Equal("leave open due to WP history", await CurrentNameAsync(fixture.Db, "1800", requireActive: false));
        Assert.All(await fixture.Db.CanonicalGrowerAliases.Where(x => x.NormalizedAliasKey == "HARSHFIELD_FARMS").ToListAsync(), x => Assert.False(x.IsActive));
        Assert.Equal(2, await fixture.Db.AuditLogs.CountAsync(x => x.EntityName == ReviewedGrowerMasterSyncConstants.AuditEntityName));
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.EntityKey == ReviewedGrowerMasterConstants.PreviousAssetSha256).ToListAsync());
        var v2Audit = await fixture.Db.AuditLogs.SingleAsync(x => x.EntityKey == ReviewedGrowerMasterConstants.AssetSha256);
        Assert.Contains(ReviewedGrowerMasterConstants.PreviousWorkbookSha256, v2Audit.BeforeValuesJson, StringComparison.Ordinal);
        Assert.Contains(ReviewedGrowerMasterConstants.PreviousAssetSha256, v2Audit.BeforeValuesJson, StringComparison.Ordinal);
        Assert.Contains(ReviewedGrowerMasterConstants.WorkbookSha256, v2Audit.AfterValuesJson, StringComparison.Ordinal);
        Assert.Contains(ReviewedGrowerMasterConstants.AssetSha256, v2Audit.AfterValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"reviewedRows\": 669", v2Audit.AfterValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"historicalOperationalRowsChanged\": 0", v2Audit.AfterValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"operationalPoolStartRowsChanged\": 0", v2Audit.AfterValuesJson, StringComparison.Ordinal);

        var rerun = await fixture.Service.RunAsync(Request(apply: true), CancellationToken.None);
        Assert.True(rerun.AlreadyApplied);
        Assert.False(rerun.Applied);
        Assert.Equal(2, await fixture.Db.AuditLogs.CountAsync(x => x.EntityName == ReviewedGrowerMasterSyncConstants.AuditEntityName));
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
        await SeedPreviousReviewedMasterAsync(fixture.Db);
        var masterBefore = await CaptureCanonicalMasterAsync(fixture.Db);
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
        Assert.Equal(masterBefore, await CaptureCanonicalMasterAsync(fixture.Db));
        Assert.Equal(389, await fixture.Db.CanonicalGrowerNumbers.CountAsync(x => x.IsActive && x.SourceSystem == ReviewedGrowerMasterConstants.PreviousSourceSystem));
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.EntityName == ReviewedGrowerMasterSyncConstants.AuditEntityName && x.EntityKey == ReviewedGrowerMasterConstants.PreviousAssetSha256).ToListAsync());
        Assert.Empty(await fixture.Db.AuditLogs.Where(x => x.EntityKey == ReviewedGrowerMasterConstants.AssetSha256).ToListAsync());
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
    public async Task Sync_DeactivatesNewlyInactiveNumberWithoutRewritingHistoricalEvidence()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        await SeedPreviousReviewedMasterAsync(fixture.Db);
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
            GrowerNumber = "1800",
            GrowerName = "leave open due to WP history",
            LotCode = "1800",
            BinCount = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var preflight = await fixture.Service.PreflightAsync(CancellationToken.None);

        Assert.Equal("Ready", preflight.State);
        Assert.Empty(preflight.Issues);
        var inactive = Assert.Single(preflight.InactiveRows, x => x.GrowerNumber == "1800");
        Assert.True(inactive.HasProductionEvidence);
        Assert.True(inactive.HasActiveCanonicalMapping);

        var result = await fixture.Service.RunAsync(Request(true, preflight.TargetFingerprint, preflight.ProtectedFingerprint), CancellationToken.None);

        Assert.True(result.Applied, $"{result.Message} Error={fixture.Logger.LastException}; Postflight={result.Preflight.State}; issues={string.Join(" | ", result.Preflight.Issues)}");
        var receipt = await fixture.Db.Receipts.SingleAsync(x => x.CompuTechReceiptId == "INACTIVE-EVIDENCE");
        Assert.Equal("1800", receipt.GrowerNumber);
        Assert.Equal("leave open due to WP history", receipt.GrowerName);
        Assert.False(await fixture.Db.CanonicalGrowerNumbers.Where(x => x.NormalizedGrowerNumber == "1800").Select(x => x.IsActive).SingleAsync());
        Assert.Equal("leave open due to WP history", await CurrentNameAsync(fixture.Db, "1800", requireActive: false));
    }

    private static async Task SeedPreviousReviewedMasterAsync(CropQcDbContext db)
    {
        var path = Path.Combine(FindRepositoryDirectory("docs"), "reviewed-grower-master-v2-comparison.csv");
        using var parser = new TextFieldParser(path) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidOperationException("Comparison evidence header is missing.");
        var index = headers.Select((name, position) => (name, position)).ToDictionary(x => x.name, x => x.position, StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow.AddDays(-1);
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? [];
            if (fields[index["PreviousStatus"]] != "Active") continue;
            var number = fields[index["GrowerNumber"]];
            var name = fields[index["PreviousWorkbookName"]];
            var grower = new CanonicalGrower
            {
                DisplayName = name,
                NormalizedKey = $"REVIEWED_GROWER_NUMBER_{number}",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            grower.GrowerNumbers.Add(new CanonicalGrowerNumber
            {
                GrowerNumber = number,
                NormalizedGrowerNumber = number,
                SourceSystem = ReviewedGrowerMasterConstants.PreviousSourceSystem,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
            grower.Aliases.Add(new CanonicalGrowerAlias
            {
                AliasName = name,
                NormalizedAliasKey = CanonicalGrowerService.NormalizeGrowerKey(name),
                SourceSystem = ReviewedGrowerMasterConstants.PreviousSourceSystem,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.CanonicalGrowers.Add(grower);
        }
        var adminId = await db.Users.Where(x => x.Email == "admin@example.com").Select(x => x.Id).SingleAsync();
        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            Action = "ReviewedMasterSync",
            EntityName = ReviewedGrowerMasterSyncConstants.AuditEntityName,
            EntityKey = ReviewedGrowerMasterConstants.PreviousAssetSha256,
            BeforeValuesJson = "{}",
            AfterValuesJson = JsonSerializer.Serialize(new
            {
                Workbook = ReviewedGrowerMasterConstants.PreviousWorkbookFileName,
                ReviewedGrowerMasterConstants.PreviousWorkbookSha256,
                ReviewedGrowerMasterConstants.PreviousAssetSha256,
                ActiveMappings = ReviewedGrowerMasterConstants.PreviousExpectedActiveCount
            }),
            SourceApplication = "CropQc.Web reviewed grower master command",
            CreatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string> CurrentNameAsync(CropQcDbContext db, string number, bool requireActive = true) =>
        await db.CanonicalGrowerNumbers
            .Where(x => x.NormalizedGrowerNumber == number && (!requireActive || x.IsActive))
            .Select(x => x.CanonicalGrower.DisplayName)
            .SingleAsync();

    private static async Task<string> CaptureOperationalCountsAsync(CropQcDbContext db) => JsonSerializer.Serialize(new
    {
        Receipts = await db.Receipts.CountAsync(),
        ReceiptBins = await db.Receipts.SumAsync(x => (int?)x.BinCount) ?? 0,
        GrowerLots = await db.GrowerLots.CountAsync(),
        Adjustments = await db.RoomInventoryAdjustments.CountAsync(),
        Transfers = await db.RoomTransfers.CountAsync(),
        Losses = await db.RoomInventoryLosses.CountAsync(),
        BinsRuns = await db.BinsRunEntries.CountAsync(),
        ActualRuns = await db.ActualRuns.CountAsync(),
        Expectations = await db.RunExpectations.CountAsync(),
        Samples = await db.QcSamples.CountAsync(),
        Readings = await db.QcFruitReadings.CountAsync(),
        Photos = await db.QcPhotos.CountAsync(),
        GrowerLotPools = await db.GrowerLots.OrderBy(x => x.Id).Select(x => new { x.Id, x.PoolStart }).ToListAsync()
    });

    private static async Task<string> CaptureCanonicalMasterAsync(CropQcDbContext db) => JsonSerializer.Serialize(new
    {
        Growers = await db.CanonicalGrowers.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.DisplayName, x.NormalizedKey, x.IsActive, x.MergedIntoCanonicalGrowerId }).ToListAsync(),
        Numbers = await db.CanonicalGrowerNumbers.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.CanonicalGrowerId, x.GrowerNumber, x.NormalizedGrowerNumber, x.SourceSystem, x.IsActive }).ToListAsync(),
        Aliases = await db.CanonicalGrowerAliases.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.CanonicalGrowerId, x.AliasName, x.NormalizedAliasKey, x.SourceSystem, x.IsActive }).ToListAsync(),
        Audits = await db.AuditLogs.AsNoTracking().Where(x => x.EntityName == ReviewedGrowerMasterSyncConstants.AuditEntityName).OrderBy(x => x.Id).Select(x => new { x.Id, x.EntityKey, x.BeforeValuesJson, x.AfterValuesJson }).ToListAsync()
    });

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
        public CapturingLogger<ReviewedGrowerMasterSyncService> Logger { get; } = new();

        private SqliteFixture(SqliteConnection connection, CropQcDbContext db)
        {
            this.connection = connection;
            Db = db;
            Service = new ReviewedGrowerMasterSyncService(
                db,
                Source(),
                new AppEnvironmentOptions { Kind = AppEnvironmentKinds.Development },
                Logger);
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

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public Exception? LastException { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null) LastException = exception;
        }
    }
}

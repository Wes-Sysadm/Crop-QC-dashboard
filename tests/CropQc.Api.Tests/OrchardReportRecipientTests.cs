using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Controllers;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class OrchardReportRecipientTests
{
    [Fact]
    public async Task EveryQcReportIncludesRequiredDefaultRecipient()
    {
        await using var db = CreateDbContext();
        var result = await Resolver(db).ResolveAsync(CancellationToken.None);

        Assert.Equal([QcReportEmailDefaults.RequiredRecipient], result.Recipients);
    }

    [Fact]
    public async Task MisconfiguredDefaultCannotReplaceRequiredQcMailbox()
    {
        await using var db = CreateDbContext();
        var resolver = new QcEmailRecipientResolver(
            db,
            new EmailOptions { QcReportDefaultRecipient = "someone-else@example.com" },
            NullLogger<QcEmailRecipientResolver>.Instance);

        var result = await resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal([QcReportEmailDefaults.RequiredRecipient], result.Recipients);
    }

    [Fact]
    public async Task ConfirmedFieldOrchardIncludesAllActiveManagersAndDeduplicatesCaseInsensitively()
    {
        await using var db = CreateDbContext();
        var (orchard, block) = AddOrchard(db, "Windy Point", "WINDYPOINT", "North");
        var sample = AddSample(db, block);
        AddRecipient(db, orchard, "manager-one@example.com");
        AddRecipient(db, orchard, "MANAGER-ONE@example.com");
        AddRecipient(db, orchard, "manager-two@example.com");
        AddRecipient(db, orchard, "disabled@example.com", isActive: false);
        AddRecipient(db, orchard, "deleted@example.com", isDeleted: true);
        AddRecipient(db, orchard, "not-an-email");
        AddRecipient(db, orchard, "QC@fruitandland.com");
        await db.SaveChangesAsync();

        var result = await Resolver(db).ResolveForSampleAsync(sample.Id, null, CancellationToken.None);

        Assert.Equal(3, result.Recipients.Count);
        Assert.Equal(QcReportEmailDefaults.RequiredRecipient, result.Recipients[0]);
        Assert.Contains("manager-one@example.com", result.Recipients, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("manager-two@example.com", result.Recipients, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("disabled@example.com", result.Recipients);
        Assert.DoesNotContain("deleted@example.com", result.Recipients);
        Assert.Contains("not-an-email", result.SkippedInvalidAddresses);
        Assert.False(result.OrchardCouldNotBeResolved);
        Assert.False(result.OrchardHadNoConfiguredManager);
    }

    [Fact]
    public async Task UnconfirmedFuzzyFieldSuggestionDoesNotResolveOrchard()
    {
        await using var db = CreateDbContext();
        var (orchard, _) = AddOrchard(db, "Honey Bear", "HONEYBEAR", "One");
        AddRecipient(db, orchard, "honeybear-manager@example.com");
        var sample = AddSample(db, null);
        sample.FieldSampleGrowerName = "Honeybear-ish";
        sample.FieldSampleOriginalBlockName = "One";
        sample.FieldSampleBlockResolution = "Suggested";
        await db.SaveChangesAsync();

        var result = await Resolver(db).ResolveForSampleAsync(sample.Id, null, CancellationToken.None);

        Assert.Equal([QcReportEmailDefaults.RequiredRecipient], result.Recipients);
        Assert.True(result.OrchardCouldNotBeResolved);
    }

    [Fact]
    public async Task ResolvedOrchardWithoutManagerUsesOnlyDefault()
    {
        await using var db = CreateDbContext();
        var (_, block) = AddOrchard(db, "Domex", "DOMEX", "A");
        var sample = AddSample(db, block);
        await db.SaveChangesAsync();

        var result = await Resolver(db).ResolveForSampleAsync(sample.Id, null, CancellationToken.None);

        Assert.Equal([QcReportEmailDefaults.RequiredRecipient], result.Recipients);
        Assert.True(result.OrchardHadNoConfiguredManager);
        Assert.False(result.OrchardCouldNotBeResolved);
    }

    [Fact]
    public async Task SimilarOrchardNamesNeverCrossRecipients()
    {
        await using var db = CreateDbContext();
        var (north, northBlock) = AddOrchard(db, "Earl Brown North", "EARLBROWNNORTH", "A");
        var (south, _) = AddOrchard(db, "Earl Brown South", "EARLBROWNSOUTH", "A");
        AddRecipient(db, north, "north@example.com");
        AddRecipient(db, south, "south@example.com");
        var sample = AddSample(db, northBlock);
        await db.SaveChangesAsync();

        var result = await Resolver(db).ResolveForSampleAsync(sample.Id, null, CancellationToken.None);

        Assert.Contains("north@example.com", result.Recipients);
        Assert.DoesNotContain("south@example.com", result.Recipients);
    }

    [Fact]
    public async Task ReceiptBackedReportUsesConfirmedReceiptBlock()
    {
        await using var db = CreateDbContext();
        var (orchard, block) = AddOrchard(db, "Windy Point", "WINDYPOINT", "Confirmed");
        AddRecipient(db, orchard, "wp-manager@example.com");
        var receipt = AddReceipt(db, block);
        var sample = AddSample(db, null, receipt);
        await db.SaveChangesAsync();

        var result = await Resolver(db).ResolveForSampleAsync(sample.Id, null, CancellationToken.None);

        Assert.Contains("wp-manager@example.com", result.Recipients);
        Assert.Equal(orchard.Id, result.ResolvedOrchardId);
    }

    [Fact]
    public async Task AdminUpsertValidatesDeduplicatesAndAuditsLifecycle()
    {
        await using var db = CreateDbContext();
        var user = new User { Email = "admin@example.com", DisplayName = "Admin", CreatedAt = DateTimeOffset.UtcNow };
        var (orchard, _) = AddOrchard(db, "Domex", "DOMEX", "A");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = new OrchardRecipientAdminService(db);

        var invalid = await service.UpsertAsync(new(null, orchard.Id, null, "bad-address", true), user.Email, CancellationToken.None);
        Assert.False(invalid.Success);

        var created = await service.UpsertAsync(new(null, orchard.Id, null, "Manager@Example.com", true), user.Email, CancellationToken.None);
        Assert.True(created.Success);
        var duplicate = await service.UpsertAsync(new(null, orchard.Id, null, "manager@example.com", true), user.Email, CancellationToken.None);
        Assert.False(duplicate.Success);

        var edited = await service.UpsertAsync(new(created.RecipientId, orchard.Id, null, "new-manager@example.com", false), user.Email, CancellationToken.None);
        Assert.True(edited.Success);
        Assert.Null(await service.SetEnabledAsync(created.RecipientId!.Value, true, user.Email, CancellationToken.None));
        Assert.Null(await service.DeleteAsync(created.RecipientId.Value, user.Email, CancellationToken.None));

        var actions = await db.AuditLogs.Where(x => x.EntityName == nameof(OrchardReportRecipient)).Select(x => x.Action).ToListAsync();
        Assert.Contains("create", actions);
        Assert.Contains("edit", actions);
        Assert.Contains("disable", actions);
        Assert.Contains("enable", actions);
        Assert.Contains("delete", actions);
        Assert.True((await db.OrchardReportRecipients.SingleAsync()).IsDeleted);
    }

    [Fact]
    public async Task FutureImportUpsertReportsUnmatchedOrchardWithoutCreatingOne()
    {
        await using var db = CreateDbContext();
        var service = new OrchardRecipientAdminService(db);

        var result = await service.UpsertAsync(new(null, null, "Unknown Orchard", "manager@example.com", true), "admin@example.com", CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.OrchardWasUnmatched);
        Assert.Empty(db.CanonicalOrchards);
    }

    [Fact]
    public async Task MatrixShowsOrchardsWithoutConfiguredManagersAndSupportsSearch()
    {
        await using var db = CreateDbContext();
        var (configured, _) = AddOrchard(db, "Windy Point", "WINDYPOINT", "A");
        AddOrchard(db, "Domex", "DOMEX", "B");
        AddRecipient(db, configured, "wp-manager@example.com");
        await db.SaveChangesAsync();
        var service = new OrchardRecipientAdminService(db);

        var all = await service.GetMatrixAsync(null, CancellationToken.None);
        var filtered = await service.GetMatrixAsync("wp-manager", CancellationToken.None);

        Assert.Contains(all.Rows, x => x.OrchardName == "Domex" && x.IsMissingConfiguration);
        Assert.Single(filtered.Rows);
        Assert.Equal("Windy Point", filtered.Rows[0].OrchardName);
    }

    [Fact]
    public void AdministrationPageUsesDedicatedOrchardManagerAuthorization()
    {
        var authorization = Assert.Single(typeof(OrchardRecipientsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(AccessPolicyNames.OrchardManagersView, authorization.Policy);
    }

    [Fact]
    public void NonQcBinEmailRecipientsRemainUnchanged()
    {
        Assert.Equal("rob@earlbrownandsons.com,wes@fruitandland.com", EbsDailyBinsEmailSettings.DefaultRecipients);
    }

    private static QcEmailRecipientResolver Resolver(CropQcDbContext db) =>
        new(db, new EmailOptions(), NullLogger<QcEmailRecipientResolver>.Instance);

    private static CropQcDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static (CanonicalOrchard Orchard, CanonicalOrchardBlock Block) AddOrchard(CropQcDbContext db, string name, string key, string blockName)
    {
        var now = DateTimeOffset.UtcNow;
        var orchard = new CanonicalOrchard { OrchardName = name, NormalizedOrchardKey = key, CreatedAt = now, UpdatedAt = now };
        var block = new CanonicalOrchardBlock
        {
            CanonicalOrchard = orchard,
            OrchardName = name,
            CanonicalBlockName = blockName,
            NormalizedOrchardKey = key,
            NormalizedBlockKey = blockName.ToUpperInvariant(),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.AddRange(orchard, block);
        return (orchard, block);
    }

    private static void AddRecipient(CropQcDbContext db, CanonicalOrchard orchard, string email, bool isActive = true, bool isDeleted = false)
    {
        var now = DateTimeOffset.UtcNow;
        db.OrchardReportRecipients.Add(new OrchardReportRecipient
        {
            CanonicalOrchard = orchard,
            EmailAddress = email,
            NormalizedEmailAddress = email.ToUpperInvariant(),
            IsActive = isActive,
            IsDeleted = isDeleted,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static QcSample AddSample(CropQcDbContext db, CanonicalOrchardBlock? block, Receipt? receipt = null)
    {
        var sample = new QcSample
        {
            Receipt = receipt,
            CanonicalOrchardBlock = block,
            SampleTypeId = 1,
            Status = "In Progress",
            StarchStatus = "Pending",
            PhotoStatus = "Pending",
            EmailStatus = "Not Sent",
            SampleTakenAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.QcSamples.Add(sample);
        return sample;
    }

    private static Receipt AddReceipt(CropQcDbContext db, CanonicalOrchardBlock block)
    {
        var warehouse = new Warehouse { Code = "WP", Name = "WP" };
        var room = new Room { Warehouse = warehouse, Code = "1", Name = "1" };
        var fruit = new FruitProfile { Name = "Apple", VarietyCode = "APL", FruitType = "Apple", ProductionType = "Conventional" };
        var receipt = new Receipt
        {
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.UtcNow,
            CompuTechReceiptId = Guid.NewGuid().ToString("N"),
            Warehouse = warehouse,
            Room = room,
            FruitProfile = fruit,
            CanonicalOrchardBlock = block,
            GrowerName = "Confirmed Grower",
            LotCode = "Confirmed",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Receipts.Add(receipt);
        return receipt;
    }
}

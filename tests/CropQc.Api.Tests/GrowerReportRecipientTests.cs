using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Controllers;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class GrowerReportRecipientTests
{
    [Fact]
    public async Task MatrixIncludesEveryActiveNumberForActiveGrowersAndMakesStandaloneNumbersSelectable()
    {
        await using var db = CreateDbContext();
        var active = AddGrowerNumber(db, 1, "1080", "WP ORCHARD ORG CHIL");
        AddGrowerNumber(db, 2, "1081", "Inactive Number", numberActive: false);
        AddGrowerNumber(db, 3, "1082", "Inactive Grower", growerActive: false);
        for (var index = 0; index < 642; index++)
        {
            AddGrowerNumber(db, index + 10, (2000 + index).ToString(), $"Grower {index:000}");
        }
        await db.SaveChangesAsync();

        var matrix = await new GrowerRecipientAdminService(db).GetMatrixAsync(null, CancellationToken.None);

        Assert.Equal(643, matrix.GrowerNumbers.Count);
        Assert.Contains(matrix.GrowerNumbers, x => x.Id == active.Id && x.Label == "1080 — WP ORCHARD ORG CHIL");
        Assert.DoesNotContain(matrix.GrowerNumbers, x => x.GrowerNumber is "1081" or "1082");
        Assert.Empty(db.CanonicalOrchards);
        Assert.Empty(db.GrowerReportRecipients);
        Assert.Empty(db.AuditLogs);
        Assert.DoesNotContain(db.ChangeTracker.Entries(), x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    [Fact]
    public async Task MatrixSearchMatchesGrowerNumberGrowerNameAndRecipientEmail()
    {
        await using var db = CreateDbContext();
        var number = AddGrowerNumber(db, 1, "1080", "WP ORCHARD ORG CHIL");
        AddGrowerRecipient(db, number, "manager@example.com");
        await db.SaveChangesAsync();
        var service = new GrowerRecipientAdminService(db);

        Assert.Single((await service.GetMatrixAsync("1080", CancellationToken.None)).Rows);
        Assert.Single((await service.GetMatrixAsync("orchard org", CancellationToken.None)).Rows);
        Assert.Single((await service.GetMatrixAsync("manager@", CancellationToken.None)).Rows);
        Assert.Empty((await service.GetMatrixAsync("not present", CancellationToken.None)).Rows);
    }

    [Fact]
    public async Task AdminLifecycleStoresExactNumberAllowsEmailAcrossNumbersAndAuditsEveryWrite()
    {
        await using var db = CreateDbContext();
        var user = new User { Email = "admin@example.com", DisplayName = "Admin", CreatedAt = DateTimeOffset.UtcNow };
        var first = AddGrowerNumber(db, 1, "1080", "First Grower");
        var second = AddGrowerNumber(db, 2, "1081", "Second Grower");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = new GrowerRecipientAdminService(db);

        var created = await service.UpsertAsync(new(null, first.Id, "Manager@Example.com", true), user.Email, CancellationToken.None);
        Assert.True(created.Success);
        var duplicate = await service.UpsertAsync(new(null, first.Id, "manager@example.com", true), user.Email, CancellationToken.None);
        Assert.False(duplicate.Success);
        var otherNumber = await service.UpsertAsync(new(null, second.Id, "manager@example.com", true), user.Email, CancellationToken.None);
        Assert.True(otherNumber.Success);

        var edited = await service.UpsertAsync(new(created.RecipientId, first.Id, "updated@example.com", false), user.Email, CancellationToken.None);
        Assert.True(edited.Success);
        Assert.Null(await service.SetEnabledAsync(created.RecipientId!.Value, true, user.Email, CancellationToken.None));
        Assert.Null(await service.DeleteAsync(created.RecipientId.Value, user.Email, CancellationToken.None));

        var stored = await db.GrowerReportRecipients.SingleAsync(x => x.Id == created.RecipientId);
        Assert.Equal(first.Id, stored.CanonicalGrowerNumberId);
        Assert.True(stored.IsDeleted);
        var actions = await db.AuditLogs.Where(x => x.EntityName == nameof(GrowerReportRecipient)).Select(x => x.Action).ToListAsync();
        Assert.Contains("create", actions);
        Assert.Contains("edit", actions);
        Assert.Contains("disable", actions);
        Assert.Contains("enable", actions);
        Assert.Contains("delete", actions);
    }

    [Fact]
    public async Task AdminRejectsInactiveNumberInactiveGrowerAndInvalidAddress()
    {
        await using var db = CreateDbContext();
        var inactiveNumber = AddGrowerNumber(db, 1, "1080", "Inactive Number", numberActive: false);
        var inactiveGrower = AddGrowerNumber(db, 2, "1081", "Inactive Grower", growerActive: false);
        var active = AddGrowerNumber(db, 3, "1082", "Active Grower");
        await db.SaveChangesAsync();
        var service = new GrowerRecipientAdminService(db);

        Assert.False((await service.UpsertAsync(new(null, inactiveNumber.Id, "a@example.com", true), "", CancellationToken.None)).Success);
        Assert.False((await service.UpsertAsync(new(null, inactiveGrower.Id, "a@example.com", true), "", CancellationToken.None)).Success);
        Assert.False((await service.UpsertAsync(new(null, active.Id, "not-an-email", true), "", CancellationToken.None)).Success);
        Assert.Empty(db.GrowerReportRecipients);
    }

    [Fact]
    public async Task ReceiptGrowerNumberRecipientWorksWithoutCanonicalOrchard()
    {
        await using var db = CreateDbContext();
        var number = AddGrowerNumber(db, 1, "1080", "WP ORCHARD ORG CHIL");
        AddGrowerRecipient(db, number, "manager@example.com");
        var receipt = AddReceipt(db, " 10-80 ");
        var sample = AddSample(db, receipt: receipt);
        await db.SaveChangesAsync();

        var result = await Resolver(db).ResolveForSampleAsync(sample.Id, null, CancellationToken.None);

        Assert.Equal(number.Id, result.ResolvedGrowerNumberId);
        Assert.Equal("1080", result.ResolvedGrowerNumber);
        Assert.Contains("manager@example.com", result.Recipients);
        Assert.True(result.OrchardCouldNotBeResolved);
        Assert.Equal([QcReportEmailDefaults.RequiredRecipient, "manager@example.com"], result.Recipients);
    }

    [Fact]
    public async Task FieldSampleGrowerNumberRecipientIsIncluded()
    {
        await using var db = CreateDbContext();
        var number = AddGrowerNumber(db, 1, "9350", "ROLOFF FARM-NAGLE CONV");
        AddGrowerRecipient(db, number, "field-manager@example.com");
        var sample = AddSample(db);
        sample.FieldSampleGrowerNumber = "9350";
        await db.SaveChangesAsync();

        var result = await Resolver(db).ResolveForSampleAsync(sample.Id, null, CancellationToken.None);

        Assert.Contains("field-manager@example.com", result.Recipients);
        Assert.Equal(number.Id, result.ResolvedGrowerNumberId);
    }

    [Fact]
    public async Task GrowerOrchardAdditionalAndRequiredRecipientsCombineAndDeduplicateCaseInsensitively()
    {
        await using var db = CreateDbContext();
        var number = AddGrowerNumber(db, 1, "1080", "WP ORCHARD ORG CHIL");
        AddGrowerRecipient(db, number, "shared@example.com");
        AddGrowerRecipient(db, number, "grower-manager@example.com");
        var (orchard, block) = AddOrchard(db, "Windy Point", "WINDYPOINT", "North");
        AddOrchardRecipient(db, orchard, "SHARED@example.com");
        AddOrchardRecipient(db, orchard, "orchard-manager@example.com");
        var receipt = AddReceipt(db, "1080", block);
        var sample = AddSample(db, receipt: receipt);
        await db.SaveChangesAsync();

        var result = await Resolver(db).ResolveForSampleAsync(
            sample.Id,
            ["additional@example.com", "Grower-Manager@example.com", "qc@fruitandland.com"],
            CancellationToken.None);

        Assert.Equal(5, result.Recipients.Count);
        Assert.Equal(1, result.Recipients.Count(x => x.Equals("shared@example.com", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("grower-manager@example.com", result.Recipients, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("orchard-manager@example.com", result.Recipients, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("additional@example.com", result.Recipients, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(orchard.Id, result.ResolvedOrchardId);
    }

    [Fact]
    public async Task UnknownDisabledAndDeletedGrowerNumberRecipientsAreExcludedWithoutCreatingMasterData()
    {
        await using var db = CreateDbContext();
        var number = AddGrowerNumber(db, 1, "1080", "Known Grower");
        AddGrowerRecipient(db, number, "disabled@example.com", isActive: false);
        AddGrowerRecipient(db, number, "deleted@example.com", isDeleted: true);
        var unknownReceipt = AddReceipt(db, "9999");
        var unknownSample = AddSample(db, receipt: unknownReceipt);
        var knownReceipt = AddReceipt(db, "1080");
        var knownSample = AddSample(db, receipt: knownReceipt);
        await db.SaveChangesAsync();
        var numberCount = await db.CanonicalGrowerNumbers.CountAsync();

        var unknown = await Resolver(db).ResolveForSampleAsync(unknownSample.Id, null, CancellationToken.None);
        var known = await Resolver(db).ResolveForSampleAsync(knownSample.Id, null, CancellationToken.None);

        Assert.Equal([QcReportEmailDefaults.RequiredRecipient], unknown.Recipients);
        Assert.True(unknown.GrowerNumberCouldNotBeResolved);
        Assert.Equal([QcReportEmailDefaults.RequiredRecipient], known.Recipients);
        Assert.Equal(numberCount, await db.CanonicalGrowerNumbers.CountAsync());
    }

    [Fact]
    public void ControllerKeepsViewCreateAndAdminPoliciesAuthoritative()
    {
        var controllerPolicy = Assert.Single(typeof(OrchardRecipientsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(AccessPolicyNames.OrchardManagersView, controllerPolicy.Policy);

        Assert.Equal(AccessPolicyNames.OrchardManagersCreate, PolicyFor(nameof(OrchardRecipientsController.SaveGrowerNumber)));
        Assert.Equal(AccessPolicyNames.OrchardManagersAdmin, PolicyFor(nameof(OrchardRecipientsController.SetGrowerNumberEnabled)));
        Assert.Equal(AccessPolicyNames.OrchardManagersAdmin, PolicyFor(nameof(OrchardRecipientsController.DeleteGrowerNumber)));
    }

    [Fact]
    public void PageProvidesSearchableGrowerNumberWorkflowAndPreservesOrchardWorkflow()
    {
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "OrchardRecipients", "Index.cshtml"));

        Assert.Contains("Add Grower Number recipient", view);
        Assert.Contains("data-grower-number-filter", view);
        Assert.Contains("data-grower-number-select", view);
        Assert.Contains("option.hidden = !matches", view);
        Assert.Contains("Grower Number recipients", view);
        Assert.Contains("Orchard-specific recipients", view);
        Assert.Contains("/Admin/OrchardRecipients/Save", view);
        Assert.Contains("qc@fruitandland.com", view);
        Assert.DoesNotContain("value=\"@row.IsActive.ToString()", view);
        Assert.DoesNotContain("value=\"@(!row.IsActive).ToString()", view);
        Assert.Equal(2, view.Split("value=\"@(row.IsActive.ToString().ToLowerInvariant())\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, view.Split("value=\"@((!row.IsActive).ToString().ToLowerInvariant())\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void MigrationCompatibilityPackageAndApplicationGateAreReleaseExact()
    {
        var migration = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Data", "Migrations", "20260826063718_AddGrowerNumberQcRecipients.cs"));
        var preflight = File.ReadAllText(FindRepositoryFile(
            "scripts", "postgresql", "preflight-grower-number-qc-recipients.sql"));
        var apply = File.ReadAllText(FindRepositoryFile(
            "scripts", "postgresql", "apply-grower-number-qc-recipients-schema.sql"));
        var verify = File.ReadAllText(FindRepositoryFile(
            "scripts", "postgresql", "verify-grower-number-qc-recipients.sql"));
        var harness = File.ReadAllText(FindRepositoryFile(
            "scripts", "test-grower-number-qc-recipients-production-schema.ps1"));
        var gate = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Web", "Services", "DatabaseStartupDiagnostics.cs"));

        Assert.Contains("name: \"GrowerReportRecipients\"", migration);
        Assert.Equal(5, migration.Split("migrationBuilder.CreateIndex(", StringSplitOptions.None).Length - 1);
        Assert.Equal(4, migration.Split("table.ForeignKey(", StringSplitOptions.None).Length - 1);
        Assert.Contains("state_a_absent", preflight);
        Assert.Contains("state_b_complete_exact", preflight);
        Assert.Contains("State C", preflight);
        Assert.Contains("pg_advisory_xact_lock", apply);
        Assert.Contains("cropqc.test_force_grower_recipient_failure", apply);
        Assert.DoesNotContain("__EFMigrationsHistory", apply);
        Assert.DoesNotContain("INSERT INTO \"GrowerReportRecipients\"", apply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("23 AS checked_target_objects", verify);
        Assert.Contains("Fresh PostgreSQL 18 EF migration", harness);
        Assert.Contains("Migration history unchanged", harness);
        Assert.Equal("20260902011217_AddInventoryIdentityCorrections", DatabaseStartupDiagnostics.ExpectedSchemaMigration);
        Assert.Equal(883, gate.Split('\n').Count(x => x.TrimStart().StartsWith("new(", StringComparison.Ordinal) || x.TrimStart().StartsWith(",new(", StringComparison.Ordinal)));
    }

    [Fact]
    public void BothQcSendAuditPathsRetainGrowerNumberRecipientEvidence()
    {
        var receiving = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var fieldSamples = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Web", "Services", "FieldSampleReportService.cs"));

        Assert.Contains("ResolvedGrowerNumberId", receiving);
        Assert.Contains("GrowerNumberCouldNotBeResolved", receiving);
        Assert.Contains("ResolvedGrowerNumberId", fieldSamples);
        Assert.Contains("GrowerNumberCouldNotBeResolved", fieldSamples);
    }

    private static string? PolicyFor(string methodName) =>
        Assert.Single(typeof(OrchardRecipientsController).GetMethod(methodName)!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Policy;

    private static QcEmailRecipientResolver Resolver(CropQcDbContext db) =>
        new(db, new EmailOptions(), NullLogger<QcEmailRecipientResolver>.Instance);

    private static CropQcDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static CanonicalGrowerNumber AddGrowerNumber(
        CropQcDbContext db,
        int id,
        string number,
        string growerName,
        bool numberActive = true,
        bool growerActive = true)
    {
        var now = DateTimeOffset.UtcNow;
        var grower = new CanonicalGrower
        {
            Id = id,
            DisplayName = growerName,
            NormalizedKey = growerName.ToUpperInvariant(),
            IsActive = growerActive,
            CreatedAt = now,
            UpdatedAt = now
        };
        var result = new CanonicalGrowerNumber
        {
            Id = id,
            CanonicalGrower = grower,
            GrowerNumber = number,
            NormalizedGrowerNumber = CanonicalGrowerService.NormalizeGrowerNumber(number),
            IsActive = numberActive,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.AddRange(grower, result);
        return result;
    }

    private static void AddGrowerRecipient(
        CropQcDbContext db,
        CanonicalGrowerNumber number,
        string email,
        bool isActive = true,
        bool isDeleted = false)
    {
        var now = DateTimeOffset.UtcNow;
        db.GrowerReportRecipients.Add(new GrowerReportRecipient
        {
            CanonicalGrowerNumber = number,
            EmailAddress = email,
            NormalizedEmailAddress = email.ToUpperInvariant(),
            IsActive = isActive,
            IsDeleted = isDeleted,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static (CanonicalOrchard Orchard, CanonicalOrchardBlock Block) AddOrchard(
        CropQcDbContext db,
        string name,
        string key,
        string blockName)
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

    private static void AddOrchardRecipient(CropQcDbContext db, CanonicalOrchard orchard, string email)
    {
        var now = DateTimeOffset.UtcNow;
        db.OrchardReportRecipients.Add(new OrchardReportRecipient
        {
            CanonicalOrchard = orchard,
            EmailAddress = email,
            NormalizedEmailAddress = email.ToUpperInvariant(),
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static QcSample AddSample(CropQcDbContext db, CanonicalOrchardBlock? block = null, Receipt? receipt = null)
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

    private static Receipt AddReceipt(CropQcDbContext db, string growerNumber, CanonicalOrchardBlock? block = null)
    {
        var warehouse = new Warehouse { Code = "WP", Name = "WP" };
        var room = new Room { Warehouse = warehouse, Code = Guid.NewGuid().ToString("N"), Name = "Room" };
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
            GrowerName = "Grower",
            GrowerNumber = growerNumber,
            LotCode = growerNumber,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Receipts.Add(receipt);
        return receipt;
    }

    private static string FindRepositoryFile(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CropQc.sln")))
            {
                return Path.Combine([directory.FullName, .. path]);
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the CropQc repository root.");
    }
}

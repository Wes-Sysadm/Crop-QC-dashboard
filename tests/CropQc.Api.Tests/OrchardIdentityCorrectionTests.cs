using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class OrchardIdentityCorrectionTests
{
    [Theory]
    [InlineData("1080", OrchardIdentityKind.GrowerNumber, "1080")]
    [InlineData(" 0123 ", OrchardIdentityKind.GrowerNumber, "0123")]
    [InlineData("WP ORCHARD", OrchardIdentityKind.OrchardName, "WP ORCHARD")]
    [InlineData("BLOCK 1080 NORTH", OrchardIdentityKind.OrchardName, "BLOCK 1080 NORTH")]
    [InlineData("10801", OrchardIdentityKind.OrchardName, "10801")]
    [InlineData("ABC1", OrchardIdentityKind.OrchardName, "ABC1")]
    public void AmbiguousIdentityClassification_SeparatesStandaloneFourDigitGrowerNumbers(
        string value,
        OrchardIdentityKind expectedKind,
        string expectedValue)
    {
        var result = OrchardIdentityClassifier.Classify(value, OrchardIdentitySource.AmbiguousOrchardOrGrower);

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(expectedValue, result.Value);
    }

    [Fact]
    public void ExplicitlyConfirmedOrchardName_IsNotSilentlyReclassified()
    {
        var result = OrchardIdentityClassifier.Classify("1080", OrchardIdentitySource.ConfirmedOrchardName);

        Assert.Equal(OrchardIdentityKind.OrchardName, result.Kind);
        Assert.Equal("1080", result.Value);
    }

    [Fact]
    public async Task FieldSampleCreation_RejectsNumericOrchardButPreservesSeparateGrowerNumber()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateFieldSampleService(db);

        var rejected = await service.CreateAsync(new FieldSampleCreateForm
        {
            OrchardName = "1080",
            GrowerNumber = "1080",
            BlockName = "Young Block",
            FruitProfileId = 1,
            ConfirmCreateNewBlock = true,
            SampleTakenAt = DateTimeOffset.UtcNow
        }, Owner(), CancellationToken.None);
        var accepted = await service.CreateAsync(new FieldSampleCreateForm
        {
            OrchardName = "WP ORCHARD",
            GrowerNumber = "0123",
            BlockName = "Young Block",
            FruitProfileId = 1,
            ConfirmCreateNewBlock = true,
            SampleTakenAt = DateTimeOffset.UtcNow
        }, Owner(), CancellationToken.None);

        Assert.Null(rejected.SampleId);
        Assert.Contains("four-digit grower number", rejected.Error, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(accepted.SampleId);
        var sample = await db.QcSamples.Include(x => x.CanonicalOrchardBlock).ThenInclude(x => x!.CanonicalOrchard).SingleAsync();
        Assert.Equal("WP ORCHARD", sample.CanonicalOrchardBlock!.CanonicalOrchard.OrchardName);
        Assert.Equal("0123", sample.FieldSampleGrowerNumber);
        Assert.DoesNotContain(await db.CanonicalOrchards.ToListAsync(), x => x.OrchardName == "1080");
    }

    [Fact]
    public async Task NumericOrchardInput_DoesNotReturnBlockSuggestions()
    {
        await using var db = CreateDbContext();
        var service = CreateFieldSampleService(db);

        var suggestions = await service.GetBlockSuggestionsAsync("1080", "Young Block", CancellationToken.None);

        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task MasterDataCanonicalBlockCreation_RejectsNumericOrchard()
    {
        await using var db = CreateDbContext();
        var service = new AdminManagementService(db, new VarietyColorService(db));

        var error = await service.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "orchard-blocks",
            Name = "1080",
            Code = "Young Block",
            IsActive = true
        }, "admin@example.com", CancellationToken.None);

        Assert.Contains("four-digit grower number", error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.CanonicalOrchards);
        Assert.Empty(db.CanonicalOrchardBlocks);
    }

    [Fact]
    public async Task RecipientMatrix_HidesNumericOrchardAndShowsGrowerNumberSeparatelyForWpOrchard()
    {
        await using var db = CreateDbContext();
        var (_, numericBlock) = AddOrchard(db, "1080", "Young Block");
        var (_, wpBlock) = AddOrchard(db, "WP ORCHARD", "Young Block");
        db.QcSamples.AddRange(
            AddFieldSample(numericBlock, "1080", "1080"),
            AddFieldSample(wpBlock, "WP ORCHARD", "1080"));
        await db.SaveChangesAsync();

        var matrix = await new OrchardRecipientAdminService(db).GetMatrixAsync(null, CancellationToken.None);

        Assert.DoesNotContain(matrix.Rows, x => x.OrchardName == "1080");
        var wp = Assert.Single(matrix.Rows);
        Assert.Equal("WP ORCHARD", wp.OrchardName);
        Assert.Equal("1080", wp.GrowerNumbers);
    }

    [Fact]
    public async Task RecipientResolution_UsesConfirmedWpOrchardAndNeverNumericOrchard()
    {
        await using var db = CreateDbContext();
        var (numeric, numericBlock) = AddOrchard(db, "1080", "Young Block");
        var (wp, wpBlock) = AddOrchard(db, "WP ORCHARD", "Young Block");
        AddRecipient(db, numeric, "wrong@example.com");
        AddRecipient(db, wp, "wp-manager@example.com");
        var numericSample = AddFieldSample(numericBlock, "1080", "1080");
        var wpSample = AddFieldSample(wpBlock, "WP ORCHARD", "1080");
        db.QcSamples.AddRange(numericSample, wpSample);
        await db.SaveChangesAsync();
        var resolver = new QcEmailRecipientResolver(db, new EmailOptions(), NullLogger<QcEmailRecipientResolver>.Instance);

        var invalidResult = await resolver.ResolveForSampleAsync(numericSample.Id, null, CancellationToken.None);
        var wpResult = await resolver.ResolveForSampleAsync(wpSample.Id, null, CancellationToken.None);

        Assert.Equal([QcReportEmailDefaults.RequiredRecipient], invalidResult.Recipients);
        Assert.True(invalidResult.OrchardCouldNotBeResolved);
        Assert.Contains("wp-manager@example.com", wpResult.Recipients);
        Assert.DoesNotContain("wrong@example.com", wpResult.Recipients);
        Assert.Equal(wp.Id, wpResult.ResolvedOrchardId);
    }

    [Fact]
    public async Task ReconciliationDryRun_ReportsRelationshipsAndExistingTargetBlockWithoutChangingData()
    {
        await using var db = CreateDbContext();
        var (source, sourceBlock) = AddOrchard(db, "1080", "Young Block", isActive: false);
        var (target, targetBlock) = AddOrchard(db, "WP ORCHARD", "Young Block");
        var sample = AddFieldSample(sourceBlock, "1080", "1080");
        db.QcSamples.Add(sample);
        AddRecipient(db, source, "manager@example.com");
        await db.SaveChangesAsync();
        var service = new OrchardIdentityReconciliationService(db);

        var plan = await service.PlanAsync("1080", "WP ORCHARD", "1080", CancellationToken.None);

        Assert.True(plan.CanApply);
        Assert.Equal(source.Id, plan.SourceOrchardId);
        Assert.Equal(target.Id, plan.TargetOrchardId);
        Assert.Equal(1, plan.Counts.CanonicalBlocks);
        Assert.Equal(1, plan.Counts.FieldSamples);
        Assert.Equal(1, plan.Counts.ReportRecipients);
        Assert.Equal(targetBlock.Id, Assert.Single(plan.Blocks).ExistingTargetBlockId);
        Assert.Equal(sourceBlock.Id, sample.CanonicalOrchardBlockId);
        Assert.True(source.IsActive);
    }

    [Fact]
    public async Task ReconciliationWithoutExplicitTarget_RefusesAmbiguousBlockMatches()
    {
        await using var db = CreateDbContext();
        AddOrchard(db, "1080", "Young Block");
        AddOrchard(db, "WP ORCHARD", "Young Block");
        AddOrchard(db, "ANOTHER ORCHARD", "Young Block");
        await db.SaveChangesAsync();

        var plan = await new OrchardIdentityReconciliationService(db)
            .PlanAsync("1080", null, "1080", CancellationToken.None);

        Assert.False(plan.CanApply);
        Assert.True(plan.TargetIsAmbiguous);
        Assert.Contains("More than one", plan.Error);
    }

    [Fact]
    public async Task ReconciliationApply_MovesSamplePreservesGrowerNumberAndDeduplicatesRecipients()
    {
        await using var db = CreateDbContext();
        var (source, sourceBlock) = AddOrchard(db, "1080", "Young Block", isActive: false);
        var (target, targetBlock) = AddOrchard(db, "WP ORCHARD", "Young Block");
        var sample = AddFieldSample(sourceBlock, "1080", "1080");
        db.QcSamples.Add(sample);
        AddRecipient(db, source, "MANAGER@example.com");
        AddRecipient(db, target, "manager@example.com");
        await db.SaveChangesAsync();

        var result = await new OrchardIdentityReconciliationService(db)
            .ApplyAsync("1080", "WP ORCHARD", "1080", "admin@example.com", CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal(targetBlock.Id, sample.CanonicalOrchardBlockId);
        Assert.Equal("WP ORCHARD", sample.FieldSampleGrowerName);
        Assert.Equal("1080", sample.FieldSampleGrowerNumber);
        Assert.False(source.IsActive);
        Assert.False(sourceBlock.IsActive);
        Assert.Single(await db.OrchardReportRecipients.Where(x => x.CanonicalOrchardId == target.Id && !x.IsDeleted).ToListAsync());
        Assert.Single(await db.OrchardReportRecipients.Where(x => x.CanonicalOrchardId == source.Id && x.IsDeleted).ToListAsync());
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "retire-after-reconcile" && x.EntityName == nameof(CanonicalOrchard));
    }

    [Fact]
    public async Task ReconciliationApply_MovesUniqueBlockAndCorrectsNumericSampleDisplayName()
    {
        await using var db = CreateDbContext();
        var (source, sourceBlock) = AddOrchard(db, "1080", "Unique Block");
        var (target, _) = AddOrchard(db, "WP ORCHARD", "Other Block");
        var sample = AddFieldSample(sourceBlock, "1080", "1080");
        db.QcSamples.Add(sample);
        await db.SaveChangesAsync();

        var result = await new OrchardIdentityReconciliationService(db)
            .ApplyAsync("1080", "WP ORCHARD", "1080", "admin@example.com", CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal(target.Id, sourceBlock.CanonicalOrchardId);
        Assert.Equal("WP ORCHARD", sourceBlock.OrchardName);
        Assert.Equal("WP ORCHARD", sample.FieldSampleGrowerName);
        Assert.Equal("1080", sample.FieldSampleGrowerNumber);
        Assert.False(source.IsActive);
    }

    [Fact]
    public void JulyHistoricalImport_UsesWpOrchardAndKeeps1080InGrowerNumberSource()
    {
        var script = File.ReadAllText(FindRepositoryFile("scripts", "import-july-2026-field-samples.ps1"));

        Assert.Contains("const string OrchardName = \"WP ORCHARD\";", script);
        Assert.Contains("const string GrowerNumber = \"1080\";", script);
        Assert.Contains("OrchardName = OrchardName", script);
        Assert.Contains("GrowerNumber = GrowerNumber", script);
    }

    private static CropQcDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static FieldSampleService CreateFieldSampleService(CropQcDbContext db)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new FieldSampleService(db, new UserAccessService(db, configuration), configuration);
    }

    private static async Task SeedFieldSampleMasterDataAsync(CropQcDbContext db)
    {
        db.SampleTypes.Add(new SampleType { Id = 5, Name = "Field Sample" });
        db.FruitProfiles.Add(new FruitProfile
        {
            Id = 1,
            Name = "Gala Apple",
            VarietyCode = "GALA",
            FruitType = "Apple",
            ProductionType = "Conventional"
        });
        await db.SaveChangesAsync();
    }

    private static (CanonicalOrchard Orchard, CanonicalOrchardBlock Block) AddOrchard(
        CropQcDbContext db,
        string orchardName,
        string blockName,
        bool isActive = true)
    {
        var now = DateTimeOffset.UtcNow;
        var orchard = new CanonicalOrchard
        {
            OrchardName = orchardName,
            NormalizedOrchardKey = OrchardBlockMatcher.Normalize(orchardName),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        var block = new CanonicalOrchardBlock
        {
            CanonicalOrchard = orchard,
            OrchardName = orchardName,
            CanonicalBlockName = blockName,
            NormalizedOrchardKey = orchard.NormalizedOrchardKey,
            NormalizedBlockKey = OrchardBlockMatcher.Normalize(blockName),
            IsActive = isActive,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.AddRange(orchard, block);
        return (orchard, block);
    }

    private static QcSample AddFieldSample(CanonicalOrchardBlock block, string orchardName, string growerNumber) =>
        new()
        {
            CanonicalOrchardBlock = block,
            FieldSampleGrowerName = orchardName,
            FieldSampleGrowerNumber = growerNumber,
            FieldSampleOriginalBlockName = block.CanonicalBlockName,
            SampleTypeId = 5,
            Status = "In Progress",
            StarchStatus = "Pending",
            PhotoStatus = "Pending",
            EmailStatus = "Not Sent",
            SampleTakenAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static void AddRecipient(CropQcDbContext db, CanonicalOrchard orchard, string email)
    {
        var now = DateTimeOffset.UtcNow;
        db.OrchardReportRecipients.Add(new OrchardReportRecipient
        {
            CanonicalOrchard = orchard,
            EmailAddress = email,
            NormalizedEmailAddress = email.ToUpperInvariant(),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static ClaimsPrincipal Owner() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "Test"));

    private static string FindRepositoryFile(params string[] parts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new FileNotFoundException("Could not find repository file.", Path.Combine(parts));
    }
}

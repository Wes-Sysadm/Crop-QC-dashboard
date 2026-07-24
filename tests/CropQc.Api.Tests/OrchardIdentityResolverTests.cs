using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests;

public sealed class OrchardIdentityResolverTests
{
    [Fact]
    public async Task ExactGrowerNameResolvesThroughConfirmedCanonicalBlock()
    {
        await using var db = CreateDb();
        var orchard = Orchard("Academy Orchard");
        var grower = Grower("Academy");
        db.AddRange(orchard, grower);
        await db.SaveChangesAsync();
        db.CanonicalOrchardBlocks.Add(Block(orchard, "North", grower));
        await db.SaveChangesAsync();

        var set = await new OrchardIdentityResolverService(db).LoadAsync(default);
        var match = OrchardContactMatcher.Match(Token("Academy"), set);

        Assert.Equal(OrchardContactMatchMethods.Grower, match.MatchMethod);
        Assert.Equal(orchard.Id, match.SuggestedCanonicalOrchardId);
        Assert.Contains(match.Candidates, x => x.ResultType == OrchardIdentityEvidenceTypes.Grower);
    }

    [Fact]
    public async Task ExactGrowerLotNameResolvesThroughConfirmedReceiptRelationship()
    {
        await using var db = CreateDb();
        var orchard = Orchard("Academy Orchard");
        var block = Block(orchard, "North");
        var lot = new GrowerLot
        {
            Grower = "Academy",
            LotNumber = "LOT-7",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        var warehouse = new Warehouse { Code = "WP", Name = "Windy Point" };
        db.AddRange(orchard, block, lot, warehouse);
        await db.SaveChangesAsync();
        db.Receipts.Add(new Receipt
        {
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.UtcNow,
            CompuTechReceiptId = "R-1",
            Warehouse = warehouse,
            RoomId = 1,
            FruitProfileId = 1,
            GrowerLot = lot,
            CanonicalOrchardBlock = block,
            GrowerName = "Academy",
            LotCode = "LOT-7",
            BinCount = 10
        });
        await db.SaveChangesAsync();

        var set = await new OrchardIdentityResolverService(db).LoadAsync(default);
        var match = OrchardContactMatcher.Match(Token("LOT-7"), set);

        Assert.Equal(OrchardContactMatchMethods.GrowerLot, match.MatchMethod);
        Assert.Equal(orchard.Id, match.SuggestedCanonicalOrchardId);
        Assert.Contains(match.Candidates, x => x.GrowerLotIds?.Contains(lot.Id) == true);
    }

    [Fact]
    public async Task GrowerLotWithoutCanonicalTargetRequiresSetup()
    {
        await using var db = CreateDb();
        db.GrowerLots.Add(new GrowerLot
        {
            Grower = "Academy",
            LotNumber = "LOT-7",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var set = await new OrchardIdentityResolverService(db).LoadAsync(default);
        var match = OrchardContactMatcher.Match(Token("Academy"), set);

        Assert.Equal(OrchardContactMatchMethods.CanonicalSetupRequired, match.MatchMethod);
        Assert.Null(match.SuggestedCanonicalOrchardId);
        Assert.All(match.Candidates, x => Assert.True(x.CanonicalSetupRequired));
        Assert.Contains("canonical", match.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GrowerAndGrowerLotAreInterchangeableDiscoveryTerms()
    {
        await using var db = CreateDb();
        var grower = Grower("Academy");
        db.CanonicalGrowers.Add(grower);
        db.GrowerLots.Add(new GrowerLot
        {
            Grower = "Academy",
            LotNumber = "ACA-42",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new OrchardIdentityResolverService(db);

        var byGrower = await service.SearchAsync("Academy", 30, default);
        var byLot = await service.SearchAsync("ACA-42", 30, default);

        Assert.Contains(byGrower, x => x.ResultType == OrchardIdentityEvidenceTypes.Grower);
        Assert.Contains(byGrower, x => x.ResultType == OrchardIdentityEvidenceTypes.GrowerLot);
        Assert.Contains(byLot, x => x.ResultType == OrchardIdentityEvidenceTypes.GrowerLot && x.GrowerName == "Academy");
        Assert.Single(await db.CanonicalGrowers.ToListAsync());
        Assert.Single(await db.GrowerLots.ToListAsync());
    }

    [Fact]
    public async Task CanonicalBlockEvidenceResolvesItsParentOrchard()
    {
        await using var db = CreateDb();
        var orchard = Orchard("WP ORCHARD");
        db.Add(orchard);
        await db.SaveChangesAsync();
        db.Add(Block(orchard, "TENNIS COURT"));
        await db.SaveChangesAsync();

        var set = await new OrchardIdentityResolverService(db).LoadAsync(default);
        var match = OrchardContactMatcher.Match(Token("TENNIS COURT"), set);

        Assert.Equal(OrchardContactMatchMethods.CanonicalBlock, match.MatchMethod);
        Assert.Equal(orchard.Id, match.SuggestedCanonicalOrchardId);
    }

    [Fact]
    public async Task SimilarUnrelatedGrowersRemainReviewOnly()
    {
        await using var db = CreateDb();
        db.GrowerLots.AddRange(
            new GrowerLot
            {
                Grower = "Pine Creek",
                LotNumber = "A",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new GrowerLot
            {
                Grower = "Pinecrest",
                LotNumber = "B",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();

        var set = await new OrchardIdentityResolverService(db).LoadAsync(default);
        var match = OrchardContactMatcher.Match(Token("Pinecreek"), set);

        Assert.Equal(OrchardContactMatchMethods.Unmatched, match.MatchMethod);
        Assert.Null(match.SuggestedCanonicalOrchardId);
    }

    [Fact]
    public async Task NumericGrowerNumberNeverBecomesOrchardTarget()
    {
        await using var db = CreateDb();
        var orchard = Orchard("1080");
        db.CanonicalOrchards.Add(orchard);
        await db.SaveChangesAsync();

        var set = await new OrchardIdentityResolverService(db).LoadAsync(default);
        var match = OrchardContactMatcher.Match(Token("1080"), set);

        Assert.Empty(set.Orchards);
        Assert.Equal(OrchardContactMatchMethods.InvalidOrchardIdentity, match.MatchMethod);
        Assert.Null(match.SuggestedCanonicalOrchardId);
    }

    [Fact]
    public async Task GrowerLinkedToMultipleOrchardsRemainsAmbiguous()
    {
        await using var db = CreateDb();
        var north = Orchard("North Orchard");
        var south = Orchard("South Orchard");
        var grower = Grower("Shared Grower");
        db.AddRange(north, south, grower);
        await db.SaveChangesAsync();
        db.CanonicalOrchardBlocks.AddRange(
            Block(north, "North Block", grower),
            Block(south, "South Block", grower));
        await db.SaveChangesAsync();

        var set = await new OrchardIdentityResolverService(db).LoadAsync(default);
        var match = OrchardContactMatcher.Match(Token("Shared Grower"), set);

        Assert.Equal(OrchardContactMatchMethods.Ambiguous, match.MatchMethod);
        Assert.Null(match.SuggestedCanonicalOrchardId);
        Assert.Equal(2, match.Candidates.Select(x => x.CanonicalOrchardId).Distinct().Count());
    }

    [Fact]
    public async Task CanonicalOrchardWithoutBlocksStillAppearsInUnifiedSearch()
    {
        await using var db = CreateDb();
        var orchard = Orchard("New Orchard");
        db.CanonicalOrchards.Add(orchard);
        await db.SaveChangesAsync();

        var results = await new OrchardIdentityResolverService(db).SearchAsync("New Orchard", 30, default);

        Assert.Contains(results, x =>
            x.ResultType == OrchardIdentityEvidenceTypes.CanonicalOrchard
            && x.CanonicalOrchardId == orchard.Id);
    }

    private static CropQcDbContext CreateDb() => new(
        new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ParsedOrchardManagerToken Token(string orchard) => new(
        2,
        orchard,
        orchard,
        "Manager",
        "MANAGER",
        "manager@example.com",
        "MANAGER@EXAMPLE.COM",
        true,
        null,
        null,
        null,
        null,
        null);

    private static CanonicalOrchard Orchard(string name) => new()
    {
        OrchardName = name,
        NormalizedOrchardKey = OrchardContactNormalization.NormalizeOrchardIdentity(name),
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static CanonicalGrower Grower(string name) => new()
    {
        DisplayName = name,
        NormalizedKey = CanonicalGrowerService.NormalizeGrowerKey(name),
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static CanonicalOrchardBlock Block(
        CanonicalOrchard orchard,
        string blockName,
        CanonicalGrower? grower = null) => new()
        {
            CanonicalOrchard = orchard,
            CanonicalGrower = grower,
            OrchardName = orchard.OrchardName,
            CanonicalBlockName = blockName,
            NormalizedOrchardKey = OrchardContactNormalization.NormalizeOrchardIdentity(orchard.OrchardName),
            NormalizedBlockKey = OrchardContactNormalization.NormalizeOrchardIdentity(blockName),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
}

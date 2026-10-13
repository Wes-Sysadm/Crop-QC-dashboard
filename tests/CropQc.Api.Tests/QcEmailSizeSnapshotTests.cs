using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;

namespace CropQc.Api.Tests;

public sealed class QcEmailSizeSnapshotTests
{
    private static readonly int[] AppleSizes = [48, 56, 64, 72, 80, 88, 100, 113, 125, 138, 150, 163, 175, 198, 216];

    [Theory]
    [InlineData(93.5, "100")]
    [InlineData(105.4, "113")]
    [InlineData(120, "125")]
    [InlineData(48, "48")]
    [InlineData(10, "48")]
    [InlineData(216, "216")]
    [InlineData(217, "Undersized")]
    public void BoxSize_CeilsToConfiguredCategoryIncludingBoundaries(decimal boxCount, string expected)
    {
        Assert.Equal(expected, QcEmailSizeSnapshotService.AverageBoxSize([18143.6948m / boxCount], AppleSizes));
    }

    [Fact]
    public void BoxSize_UsesValidWeightsAndActiveCategories_NotAverageOfSizes()
    {
        Assert.Equal("100", QcEmailSizeSnapshotService.AverageBoxSize([null, 0, -1, 180, 220], AppleSizes));
        Assert.Equal("NotCalculated", QcEmailSizeSnapshotService.AverageBoxSize([null, 0, -1], AppleSizes));
        Assert.Equal("NotCalculated", QcEmailSizeSnapshotService.AverageBoxSize([200], []));
        Assert.Equal("113", QcEmailSizeSnapshotService.AverageBoxSize([200], [88, 113, 125]));
    }

    [Fact]
    public void Counts_PreserveObservationsDeduplicateAndFillOnlyValidIntermediateSizes()
    {
        var sample = Sample(1, 10);
        var sizes = new int?[] { 88, 88, 88, 88, 100, 100, 113, 113, 138, 138, 120, null };
        foreach (var size in sizes) AddFruit(sample, size);
        var snapshot = QcEmailSizeSnapshotService.Create(DateTimeOffset.UtcNow, AppleSizes,
            sample.FruitReadings.Concat(sample.FruitReadings), sample.FruitReadings, 1, true);
        Assert.Equal([new(88, 4), new(100, 2), new(113, 2), new(125, 0), new(138, 2)], snapshot.CurrentSample.Counts);
        Assert.Equal(2, snapshot.CurrentSample.UnclassifiedCount);
        Assert.Equal(12, sample.FruitReadings.Count);
    }

    [Fact]
    public async Task Lot_CapturesAllMatchingObservationsAcrossReceipts_IsolatesIdentityAndHistory_ReadOnly()
    {
        await using var db = Database();
        var current = Sample(1, 10);
        current.Receipt!.GrowerLotId = 100;
        var previous = Sample(2, 11);
        AddFruit(current, 88); AddFruit(previous, 138); AddFruit(previous, 138); AddFruit(previous, 113);
        previous.Receipt!.FruitProfile = current.Receipt!.FruitProfile;
        previous.SampleType = current.SampleType;
        db.AddRange(current, previous);
        for (var i = 3; i <= 13; i++)
        {
            var excluded = Sample(i, i + 10);
            AddFruit(excluded, 56);
            switch (i)
            {
                case 3: excluded.Receipt!.GrowerNumber = "OTHER"; break;
                case 4: excluded.Receipt!.LotCode = "OTHER"; break;
                case 5: excluded.Receipt!.FruitProfileId = 999; excluded.Receipt.FruitProfile.Id = 999; excluded.Receipt.FruitProfile.VarietyCode = "FUJI"; break;
                case 6: excluded.Receipt!.CropYear = 2025; break;
                case 7: excluded.IsDeleted = true; break;
                case 8: excluded.Receipt!.IsDeleted = true; break;
                case 9: excluded.CreatedAt = DateTimeOffset.UtcNow.AddDays(1); break;
                case 10: excluded.SampleTakenAt = DateTimeOffset.UtcNow.AddDays(1); break;
                case 11: excluded.IsTestData = true; break;
                case 12: excluded.Receipt = null; excluded.ReceiptId = null; break;
                case 13: excluded.Receipt!.GrowerLotId = 101; break;
            }
            // Use the tracked master rows, avoiding duplicate primary-key instances.
            if (excluded.Receipt?.FruitProfileId == current.Receipt!.FruitProfileId) excluded.Receipt.FruitProfile = current.Receipt.FruitProfile;
            excluded.SampleType = current.SampleType;
            db.Add(excluded);
        }
        previous.Receipt!.FruitProfile = current.Receipt!.FruitProfile;
        previous.SampleType = current.SampleType;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var before = await db.QcFruitReadings.CountAsync();
        var service = new QcEmailSizeSnapshotService(db);
        var snapshot = await service.CaptureAsync(current, CancellationToken.None);
        Assert.Equal(2, snapshot.LotSampleCount);
        Assert.Equal([new(88, 1), new(100, 0), new(113, 1), new(125, 0), new(138, 2)], snapshot.EntireLot.Counts);
        Assert.Single(snapshot.CurrentSample.Counts);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(before, await db.QcFruitReadings.CountAsync());
        Assert.Empty(await db.RoomInventoryAdjustments.ToListAsync());

        var later = Sample(20, 30);
        later.Receipt!.FruitProfile = current.Receipt.FruitProfile;
        later.SampleType = current.SampleType;
        AddFruit(later, 100);
        db.Attach(current.Receipt.FruitProfile);
        db.Attach(current.SampleType);
        db.Add(later);
        await db.SaveChangesAsync();
        var updated = await service.CaptureAsync(current, CancellationToken.None);
        Assert.Equal(3, updated.LotSampleCount);
        Assert.Equal(0, snapshot.EntireLot.Counts.Single(x => x.Size == 100).Count);
        Assert.Equal(1, updated.EntireLot.Counts.Single(x => x.Size == 100).Count);
    }

    [Fact]
    public async Task AmbiguousLegacyLot_RemainsUnresolvedRatherThanCombiningCanonicalLots()
    {
        await using var db = Database();
        var current = Sample(1, 10);
        AddFruit(current, 88);
        var first = Sample(2, 11);
        var second = Sample(3, 12);
        first.Receipt!.GrowerLotId = 100;
        second.Receipt!.GrowerLotId = 101;
        first.Receipt.FruitProfile = second.Receipt.FruitProfile = current.Receipt!.FruitProfile;
        first.SampleType = second.SampleType = current.SampleType;
        AddFruit(first, 113); AddFruit(second, 138);
        db.AddRange(current, first, second);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var result = await new QcEmailSizeSnapshotService(db).CaptureAsync(current, default);
        Assert.False(result.LotIdentityResolved);
        Assert.Equal(1, result.LotSampleCount);
        Assert.Equal(new QcSizeCount(88, 1), Assert.Single(result.EntireLot.Counts));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Email_EmbedsFrozenPngChartsAndConditionalDefects_WithDetailsLast()
    {
        var sample = Sample(1, 10);
        AddFruit(sample, 88); AddFruit(sample, 138); AddFruit(sample, null);
        var rows = sample.FruitReadings.ToArray();
        rows[0].Defects.Add(new() { DefectType = new() { Name = "Insect" } });
        rows[1].Defects.Add(new() { DefectType = new() { Name = "Bruise" }, Notes = "Small <mark>" });
        rows[2].Defects.Add(new() { DefectType = new() { Name = "" }, Notes = "Note only" });
        var composer = new QcSummaryEmailComposer(new NoPhotos(), new QcPhotoRequirementPolicy(),
            NullLogger<QcSummaryEmailComposer>.Instance, new Snapshot());
        var email = await composer.ComposeAsync(sample, new(), null, false, null, CancellationToken.None);
        Assert.Equal(2, email.InlineImages.Count);
        var raw = GmailUserEmailSender.BuildRawMessage(new("sender@example.com", "recipient@example.com", null,
            email.Subject, email.TextBody, email.HtmlBody, email.InlineImages));
        var base64 = raw.Replace('-', '+').Replace('_', '/');
        var mime = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '=')));
        Assert.Contains("Content-Type: multipart/related", mime);
        Assert.Contains("Content-Type: image/png", mime);
        foreach (var image in email.InlineImages)
        {
            Assert.Equal("image/png", image.ContentType);
            using var png = Image.Load(image.Bytes);
            Assert.Equal(1280, png.Width);
            Assert.Equal(520, png.Height);
            Assert.Contains("cid:" + image.ContentId, email.HtmlBody);
            Assert.Contains("125: 0", image.AltText);
            Assert.Contains($"Content-ID: <{image.ContentId}>", mime);
            Assert.Contains(Convert.ToBase64String(image.Bytes, Base64FormattingOptions.InsertLineBreaks), mime);
        }
        Assert.DoesNotContain("<script", email.HtmlBody);
        Assert.DoesNotContain("<svg", email.HtmlBody);
        Assert.DoesNotContain("No defects found", email.HtmlBody);
        Assert.DoesNotContain("No defects found", email.TextBody);
        Assert.DoesNotContain("inspected fruit affected", email.HtmlBody);
        Assert.DoesNotContain("Average weight", email.HtmlBody);
        Assert.Contains("Small &lt;mark&gt;", email.HtmlBody);
        var textRows = email.TextBody.Split(Environment.NewLine).Where(x => x.StartsWith("Row ")).ToArray();
        Assert.Contains("Defects Insect", textRows[0]); Assert.DoesNotContain("Notes", textRows[0]);
        Assert.Contains("Defects Bruise, Notes Small <mark>", textRows[1]);
        Assert.Contains("Notes Note only", textRows[2]); Assert.DoesNotContain("Defects", textRows[2]);
        var detailsIndex = email.HtmlBody.IndexOf("<h2>Receiving Sample Details</h2>", StringComparison.Ordinal);
        Assert.True(detailsIndex > email.HtmlBody.IndexOf("<h2>Photos</h2>", StringComparison.Ordinal));
        Assert.DoesNotContain("<h2>", email.HtmlBody[(detailsIndex + 34)..]);
        var frozen = email.InlineImages.Select(x => Convert.ToBase64String(x.Bytes)).ToArray();
        AddFruit(sample, 100);
        var later = await composer.ComposeAsync(sample, new(), null, false, null, CancellationToken.None);
        Assert.Equal(frozen, email.InlineImages.Select(x => Convert.ToBase64String(x.Bytes)));
        Assert.NotEqual(frozen[0], Convert.ToBase64String(later.InlineImages[0].Bytes));
    }

    private static CropQcDbContext Database()
    {
        var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        foreach (var size in AppleSizes) db.FruitSizeConversionThresholds.Add(new() { FruitType = "Apple", SizeCategory = size, MinimumWeightGrams = 1, IsActive = true });
        db.SaveChanges();
        return db;
    }

    private static QcSample Sample(int id, int receiptId) => new()
    {
        Id = id,
        ReceiptId = receiptId,
        SampleTypeId = 1,
        SampleType = new() { Id = 1, Name = "Receiving Sample" },
        Status = "InProgress",
        StarchStatus = "Missing",
        PhotoStatus = "Missing",
        EmailStatus = "Not Sent",
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        SampleTakenAt = DateTimeOffset.UtcNow.AddDays(-1),
        Receipt = new()
        {
            Id = receiptId,
            CropYear = 2026,
            FruitProfileId = 1,
            GrowerNumber = "DEMO",
            GrowerName = "Safe demo grower",
            LotCode = "DEMO-LOT",
            CompuTechReceiptId = "DEMO-" + receiptId,
            Warehouse = new() { Code = "DEMO", Name = "Demo" },
            Room = new() { Code = "ROOM", Name = "Demo room" },
            FruitProfile = new() { Id = 1, Name = "Gala", FruitType = "Apple", VarietyCode = "GALA", ProductionType = "Conventional" }
        }
    };

    private static void AddFruit(QcSample sample, int? size)
    {
        var row = sample.FruitReadings.Count + 1;
        sample.FruitReadings.Add(new()
        {
            Id = sample.Id * 100 + row,
            QcSampleId = sample.Id,
            RowNumber = row,
            SizeCategory = size,
            SizeStatus = size is null ? "NotCalculated" : "Sized",
            WeightGrams = 200,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        });
    }

    private sealed class Snapshot : IQcEmailSizeSnapshotService
    {
        public Task<QcEmailSizeSnapshot> CaptureAsync(QcSample sample, CancellationToken cancellationToken) =>
            Task.FromResult(QcEmailSizeSnapshotService.Create(DateTimeOffset.UtcNow, AppleSizes, sample.FruitReadings, sample.FruitReadings, 1, true));
    }

    private sealed class NoPhotos : IFileStorageService
    {
        public string GenerateTargetPath(FileStorageTargetContext context) => throw new NotSupportedException();
        public Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FileStorageReference?> GetMetadataAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteOrVoidAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class QcSummaryEmailComposerTests
{
    [Theory]
    [InlineData("Door Sample", "WholeSample,CutApples")]
    [InlineData("Room Sample", "WholeSample,CutApples")]
    [InlineData("Line Sample", "WholeSample,CutApples")]
    [InlineData("Receiving Sample", "TruckPhoto,WholeSample,CutApples,StarchApples")]
    [InlineData("Transfer Sample", "TruckPhoto,WholeSample,CutApples")]
    public void PhotoRequirementPolicy_MapsRequiredPhotosBySampleType(string sampleType, string expectedKeys)
    {
        var policy = new QcPhotoRequirementPolicy();

        var requirements = policy.GetRequirements(sampleType).Select(x => x.Key);

        Assert.Equal(expectedKeys.Split(','), requirements);
    }

    [Fact]
    public void PhotoRequirementPolicy_DoesNotRequireOptionalPhotosForDoorSamples()
    {
        var policy = new QcPhotoRequirementPolicy();

        var missing = policy.MissingRequiredPhotos(
            "Door Sample",
            receiptPhotoTypes: [],
            samplePhotoTypes: ["SampleBeforeCutting", "CutFruit"]);

        Assert.Empty(missing);
    }

    [Fact]
    public void PhotoRequirementPolicy_RequiresStarchPhotoForReceivingSamples()
    {
        var policy = new QcPhotoRequirementPolicy();

        var missing = policy.MissingRequiredPhotos(
            "Receiving Sample",
            receiptPhotoTypes: ["BinTruck"],
            samplePhotoTypes: ["SampleBeforeCutting", "CutFruit"]);

        Assert.Contains(missing, x => x.Contains("Starch apples", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EmailComposer_BuildsHtmlTextAndInlineImages()
    {
        var sample = BuildSample("Receiving Sample");
        var readiness = new ReadinessViewModel { CompletedFruitCount = 1 };
        var composer = new QcSummaryEmailComposer(new FakeFileStorageService(), new QcPhotoRequirementPolicy(), NullLogger<QcSummaryEmailComposer>.Instance);

        var content = await composer.ComposeAsync(sample, readiness, isOverride: false, overrideReason: null, CancellationToken.None);

        Assert.Contains("WP - Reese LOT-1 9450 BLUE06 Receiving Sample On 05/11/2025", content.Subject);
        Assert.Contains("<h2>Summary</h2>", content.HtmlBody);
        Assert.Contains("<h2>Fruit Overview</h2>", content.HtmlBody);
        Assert.Contains("cid:cropqc-photo-", content.HtmlBody);
        Assert.Contains("Truck photo", content.HtmlBody);
        Assert.Contains("Whole sample", content.HtmlBody);
        Assert.Contains("Cut apples", content.HtmlBody);
        Assert.Contains("Starch apples", content.HtmlBody);
        Assert.Contains("Fruit Overview", content.TextBody);
        Assert.Contains("Row 1:", content.TextBody);
        Assert.NotEmpty(content.InlineImages);
        Assert.All(content.InlineImages, image => Assert.Contains("@cropqc", image.ContentId));
    }

    private static QcSample BuildSample(string sampleType)
    {
        var warehouse = new Warehouse { Id = 1, Code = "WP", Name = "Washington Packing" };
        var room = new Room { Id = 1, Warehouse = warehouse, Code = "BLUE06", Name = "Blue 06" };
        var fruitProfile = new FruitProfile
        {
            Id = 1,
            Name = "Gala",
            VarietyCode = "9450",
            FruitType = "Apple",
            ProductionType = "Conventional"
        };
        var receipt = new Receipt
        {
            Id = 123,
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.Parse("2025-05-11T08:00:00-07:00"),
            CompuTechReceiptId = "R123",
            Warehouse = warehouse,
            Room = room,
            FruitProfile = fruitProfile,
            GrowerName = "Reese",
            LotCode = "LOT-1",
            BinCount = 42
        };
        var sample = new QcSample
        {
            Id = 456,
            Receipt = receipt,
            ReceiptId = receipt.Id,
            SampleType = new SampleType { Id = 1, Name = sampleType },
            Status = "Ready",
            StarchStatus = "Starch Complete",
            PhotoStatus = "Photos Complete",
            EmailStatus = "Not Sent",
            SampleTakenAt = DateTimeOffset.Parse("2025-05-11T10:30:00-07:00"),
            TakenByUser = new User { Id = 1, Email = "inspector@fruitandland.com", DisplayName = "Inspector", Domain = "fruitandland.com" }
        };
        var grade = new Grade { Id = 1, Code = "XF", Name = "Extra Fancy" };
        var starch = new StarchScaleValue { Id = 1, Value = 4.5m, SortOrder = 1, StarchScale = new StarchScale { Id = 1, Name = "Apple" } };
        var defect = new DefectType { Id = 1, Name = "Bruise" };
        var row = new QcFruitReading
        {
            Id = 1,
            QcSampleId = sample.Id,
            RowNumber = 1,
            Pressure1Lbs = 12.3m,
            Pressure2Lbs = 12.9m,
            WeightGrams = 185.2m,
            Grade = grade,
            GradeId = grade.Id,
            StarchScaleValue = starch,
            StarchScaleValueId = starch.Id,
            SizeCategory = 88,
            SizeStatus = "Sized",
            IsCompleted = true
        };
        row.Defects.Add(new QcFruitDefect { DefectType = defect, DefectTypeId = defect.Id, Notes = "Small" });
        sample.FruitReadings.Add(row);

        receipt.Photos.Add(Photo(1, "BinTruck", receiptId: receipt.Id));
        sample.Photos.Add(Photo(2, "SampleBeforeCutting", sampleId: sample.Id));
        sample.Photos.Add(Photo(3, "CutFruit", sampleId: sample.Id));
        sample.Photos.Add(Photo(4, "FruitAfterStarch", sampleId: sample.Id));
        return sample;
    }

    private static QcPhoto Photo(long id, string photoType, long? receiptId = null, long? sampleId = null) => new()
    {
        Id = id,
        ReceiptId = receiptId,
        QcSampleId = sampleId,
        PhotoType = photoType,
        PhotoSource = "Upload",
        FileName = $"{photoType}.jpg",
        ContentType = "image/jpeg",
        FileSizeBytes = 3,
        StorageProvider = FileStorageProviders.Local,
        SharePointDriveId = "local",
        SharePointItemId = $"photo-{id}",
        FileId = $"photo-{id}",
        CapturedAt = DateTimeOffset.UtcNow
    };

    private sealed class FakeFileStorageService : IFileStorageService
    {
        public string GenerateTargetPath(FileStorageTargetContext context) => "target";
        public Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<FileStorageReference?> GetMetadataAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<FileStorageReference?>(null);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(new MemoryStream([1, 2, 3]));
        public Task DeleteOrVoidAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

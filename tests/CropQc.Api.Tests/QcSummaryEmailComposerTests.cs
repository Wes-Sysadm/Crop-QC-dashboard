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
    [InlineData("Receiving Sample", "TruckPhoto,TopOfTruck,Hectre,WholeSample,CutApples,StarchApples")]
    [InlineData("Transfer Sample", "TruckPhoto,TopOfTruck,WholeSample,CutApples")]
    public void PhotoRequirementPolicy_MapsRequiredPhotosBySampleType(string sampleType, string expectedKeys)
    {
        var policy = new QcPhotoRequirementPolicy();

        var requirements = policy.GetRequirements(sampleType).Select(x => x.Key);

        Assert.Equal(expectedKeys.Split(','), requirements);
    }

    [Theory]
    [InlineData("Door Sample", "TruckPhoto,TopOfTruck,Hectre,WholeSample,CutApples,StarchApples")]
    [InlineData("Room Sample", "TruckPhoto,TopOfTruck,Hectre,WholeSample,CutApples,StarchApples")]
    [InlineData("Line Sample", "Hectre,WholeSample,CutApples,StarchApples")]
    public void PhotoRequirementPolicy_MapsAvailablePhotosBySampleType(string sampleType, string expectedKeys)
    {
        var policy = new QcPhotoRequirementPolicy();

        var available = policy.GetAvailablePhotoTypes(sampleType).Select(x => x.Key);

        Assert.Equal(expectedKeys.Split(','), available);
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
    public void PhotoRequirementPolicy_RequiresTopOfTruckAndHectreForReceivingSamples()
    {
        var policy = new QcPhotoRequirementPolicy();

        var missing = policy.MissingRequiredPhotos(
            "Receiving Sample",
            receiptPhotoTypes: ["BinTruck"],
            samplePhotoTypes: ["SampleBeforeCutting", "CutFruit"]);

        Assert.Contains("Missing required photo: Top of truck", missing);
        Assert.Contains("Missing required photo: Hectre", missing);
        Assert.Contains(missing, x => x.Contains("Starch apples", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PhotoRequirementPolicy_DoesNotRequireHectreForTransferOrLineSamples()
    {
        var policy = new QcPhotoRequirementPolicy();

        var transferMissing = policy.MissingRequiredPhotos(
            "Transfer Sample",
            receiptPhotoTypes: ["BinTruck", "TopOfTruck"],
            samplePhotoTypes: ["SampleBeforeCutting", "CutFruit"]);
        var lineMissing = policy.MissingRequiredPhotos(
            "Line Sample",
            receiptPhotoTypes: [],
            samplePhotoTypes: ["SampleBeforeCutting", "CutFruit"]);

        Assert.DoesNotContain(transferMissing, x => x.Contains("Hectre", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lineMissing, x => x.Contains("Hectre", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EmailComposer_BuildsHtmlTextAndInlineImages()
    {
        var sample = BuildSample("Receiving Sample");
        var readiness = new ReadinessViewModel { CompletedFruitCount = 1 };
        var composer = new QcSummaryEmailComposer(new FakeFileStorageService(), new QcPhotoRequirementPolicy(), NullLogger<QcSummaryEmailComposer>.Instance);

        var content = await composer.ComposeAsync(sample, readiness, sendingUser: null, isOverride: false, overrideReason: null, CancellationToken.None);

        Assert.Contains("WP - Reese LOT-1 9450 BLUE06 Receiving Sample On 05/11/2025", content.Subject);
        Assert.Contains("<h2>Summary</h2>", content.HtmlBody);
        Assert.Contains("<h2>Fruit Overview</h2>", content.HtmlBody);
        Assert.True(content.HtmlBody.IndexOf("<h2>Fruit Overview</h2>", StringComparison.Ordinal) < content.HtmlBody.IndexOf("<h2>Summary</h2>", StringComparison.Ordinal));
        Assert.True(content.HtmlBody.IndexOf("<h2>Fruit Overview</h2>", StringComparison.Ordinal) < content.HtmlBody.IndexOf("<h2>Photos</h2>", StringComparison.Ordinal));
        Assert.True(content.HtmlBody.IndexOf("<h2>Photos</h2>", StringComparison.Ordinal) < content.HtmlBody.IndexOf("<h2>Summary</h2>", StringComparison.Ordinal));
        Assert.DoesNotContain("Defects / Notes Detail", content.HtmlBody);
        Assert.Contains("cid:cropqc-photo-", content.HtmlBody);
        Assert.Contains("Truck photo", content.HtmlBody);
        Assert.Contains("Top of truck", content.HtmlBody);
        Assert.Contains("Hectre", content.HtmlBody);
        Assert.Contains("Whole sample", content.HtmlBody);
        Assert.Contains("Cut apples", content.HtmlBody);
        Assert.Contains("Starch apples", content.HtmlBody);
        Assert.Contains("Fruit Overview", content.TextBody);
        Assert.Contains("Row 1:", content.TextBody);
        Assert.NotEmpty(content.InlineImages);
        Assert.All(content.InlineImages, image => Assert.Contains("@cropqc", image.ContentId));
    }

    [Fact]
    public async Task EmailComposer_OrdersInlinePhotoSectionsForReceivingSamples()
    {
        var sample = BuildSample("Receiving Sample");
        var readiness = new ReadinessViewModel { CompletedFruitCount = 1 };
        var composer = new QcSummaryEmailComposer(new FakeFileStorageService(), new QcPhotoRequirementPolicy(), NullLogger<QcSummaryEmailComposer>.Instance);

        var content = await composer.ComposeAsync(sample, readiness, sendingUser: null, isOverride: false, overrideReason: null, CancellationToken.None);

        Assert.Contains("cid:cropqc-photo-5@cropqc", content.HtmlBody);
        Assert.Contains("cid:cropqc-photo-6@cropqc", content.HtmlBody);
        Assert.True(content.HtmlBody.IndexOf("<h3>Truck photo</h3>", StringComparison.Ordinal) < content.HtmlBody.IndexOf("<h3>Top of truck</h3>", StringComparison.Ordinal));
        Assert.True(content.HtmlBody.IndexOf("<h3>Top of truck</h3>", StringComparison.Ordinal) < content.HtmlBody.IndexOf("<h3>Hectre</h3>", StringComparison.Ordinal));
        Assert.True(content.HtmlBody.IndexOf("<h3>Hectre</h3>", StringComparison.Ordinal) < content.HtmlBody.IndexOf("<h3>Whole sample</h3>", StringComparison.Ordinal));
        Assert.True(content.HtmlBody.IndexOf("<h3>Whole sample</h3>", StringComparison.Ordinal) < content.HtmlBody.IndexOf("<h3>Cut apples</h3>", StringComparison.Ordinal));
        Assert.True(content.HtmlBody.IndexOf("<h3>Cut apples</h3>", StringComparison.Ordinal) < content.HtmlBody.IndexOf("<h3>Starch apples</h3>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmailComposer_IncludesPartialRowsAndSummarizesEnteredData()
    {
        var sample = BuildSample("Door Sample");
        sample.FruitReadings.Clear();
        var grade = new Grade { Id = 2, Code = "Fancy", Name = "Fancy" };
        var starch = new StarchScaleValue { Id = 2, Value = 5.0m, SortOrder = 2, StarchScale = new StarchScale { Id = 1, Name = "Apple" } };
        var bruise = new DefectType { Id = 2, Name = "Bruise" };
        var sunburn = new DefectType { Id = 3, Name = "Sunburn" };
        sample.FruitReadings.Add(new QcFruitReading
        {
            Id = 11,
            QcSampleId = sample.Id,
            RowNumber = 1,
            Pressure1Lbs = 10m,
            Pressure2Lbs = 12m,
            SizeStatus = "NotCalculated",
            IsCompleted = false
        });
        sample.FruitReadings.Add(new QcFruitReading
        {
            Id = 12,
            QcSampleId = sample.Id,
            RowNumber = 2,
            WeightGrams = 175m,
            SizeCategory = 100,
            SizeStatus = "Sized",
            IsCompleted = false
        });
        sample.FruitReadings.Add(new QcFruitReading
        {
            Id = 13,
            QcSampleId = sample.Id,
            RowNumber = 3,
            Grade = grade,
            GradeId = grade.Id,
            SizeStatus = "NotCalculated",
            IsCompleted = false
        });
        var row4 = new QcFruitReading
        {
            Id = 14,
            QcSampleId = sample.Id,
            RowNumber = 4,
            SizeStatus = "NotCalculated",
            IsCompleted = false
        };
        row4.Defects.Add(new QcFruitDefect { DefectType = bruise, DefectTypeId = bruise.Id, Notes = "Shoulder" });
        sample.FruitReadings.Add(row4);
        var row5 = new QcFruitReading
        {
            Id = 15,
            QcSampleId = sample.Id,
            RowNumber = 5,
            Pressure1Lbs = 14m,
            WeightGrams = 150m,
            SizeCategory = 120,
            StarchScaleValue = starch,
            StarchScaleValueId = starch.Id,
            SizeStatus = "Sized",
            IsCompleted = false
        };
        row5.Defects.Add(new QcFruitDefect { DefectType = sunburn, DefectTypeId = sunburn.Id });
        sample.FruitReadings.Add(row5);
        var composer = new QcSummaryEmailComposer(new FakeFileStorageService(), new QcPhotoRequirementPolicy(), NullLogger<QcSummaryEmailComposer>.Instance);

        var content = await composer.ComposeAsync(sample, new ReadinessViewModel { IsReady = false }, sendingUser: null, isOverride: false, overrideReason: null, CancellationToken.None);

        Assert.Contains("Entered fruit count</th><td style=\"border:1px solid #cbd5e1;\">5</td>", content.HtmlBody);
        Assert.Contains("Average pressure lbs</th><td style=\"border:1px solid #cbd5e1;\">12.5</td>", content.HtmlBody);
        Assert.Contains("Pressure std dev lbs</th><td style=\"border:1px solid #cbd5e1;\">2.12</td>", content.HtmlBody);
        Assert.Contains("Average weight grams</th><td style=\"border:1px solid #cbd5e1;\">162.5</td>", content.HtmlBody);
        Assert.Contains("Grade summary</th><td style=\"border:1px solid #cbd5e1;\">Fancy: 1</td>", content.HtmlBody);
        Assert.Contains("Defect summary</th><td style=\"border:1px solid #cbd5e1;\">Bruise: 1, Sunburn: 1</td>", content.HtmlBody);
        Assert.Contains("Size/status summary</th><td style=\"border:1px solid #cbd5e1;\">1 size 100, 1 size 120</td>", content.HtmlBody);
        Assert.Contains("Row 1:", content.TextBody);
        Assert.Contains("Row 5:", content.TextBody);
        Assert.True(content.HtmlBody.IndexOf("<h2>Fruit Overview</h2>", StringComparison.Ordinal) < content.HtmlBody.IndexOf("<h2>Summary</h2>", StringComparison.Ordinal));
        Assert.DoesNotContain("Defects / Notes Detail", content.HtmlBody);
        Assert.Contains("Sample is incomplete; summary includes entered data only.", content.TextBody);
    }

    [Fact]
    public async Task EmailComposer_SortsSizeSummaryByPeakCountThenAppleSizeOrder()
    {
        var sample = BuildSample("Door Sample");
        sample.FruitReadings.Clear();
        var sizes = new[] { 120, 120, 120, 120, 100, 100, 100, 113, 113, 113, 88 };
        for (var i = 0; i < sizes.Length; i++)
        {
            sample.FruitReadings.Add(new QcFruitReading
            {
                Id = 100 + i,
                QcSampleId = sample.Id,
                RowNumber = i + 1,
                WeightGrams = 150 + i,
                SizeCategory = sizes[i],
                SizeStatus = "Sized"
            });
        }
        var composer = new QcSummaryEmailComposer(new FakeFileStorageService(), new QcPhotoRequirementPolicy(), NullLogger<QcSummaryEmailComposer>.Instance);

        var content = await composer.ComposeAsync(sample, new ReadinessViewModel { IsReady = true }, sendingUser: null, isOverride: false, overrideReason: null, CancellationToken.None);

        Assert.Contains("4 size 120, 3 size 100, 3 size 113, 1 size 88", content.HtmlBody);
    }

    [Fact]
    public async Task EmailComposer_UsesSendingUserAsInspectorFallback()
    {
        var sample = BuildSample("Door Sample");
        sample.TakenByUser = null;
        var sender = new User { Id = 7, Email = "rob@earlbrownandsons.com", DisplayName = "Rob", Domain = "earlbrownandsons.com" };
        var composer = new QcSummaryEmailComposer(new FakeFileStorageService(), new QcPhotoRequirementPolicy(), NullLogger<QcSummaryEmailComposer>.Instance);

        var content = await composer.ComposeAsync(sample, new ReadinessViewModel { IsReady = true, CompletedFruitCount = 1 }, sender, isOverride: false, overrideReason: null, CancellationToken.None);

        Assert.Contains("Inspector</th><td style=\"border:1px solid #cbd5e1;\">Rob (rob@earlbrownandsons.com)</td>", content.HtmlBody);
    }

    [Fact]
    public void PhotoUploadUi_SupportsTopOfTruckAndHectreFriendlyLabels()
    {
        var receiptView = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Details.cshtml"));
        var sampleView = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Samples", "Details.cshtml"));
        var photoForm = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_PhotoPlaceholderForm.cshtml"));

        Assert.Contains("\"TopOfTruck\"", receiptView);
        Assert.Contains("Model.AvailablePhotoTypes", sampleView);
        Assert.Contains("PhotoTypes = Model.AvailablePhotoTypes", sampleView);
        Assert.Contains("Available Photo Types", sampleView);
        Assert.Contains("\"TopOfTruck\" => \"Top of truck\"", photoForm);
        Assert.Contains("\"Hectre\" => \"Hectre\"", photoForm);
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
            ActualSampleSize = 10,
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
        receipt.Photos.Add(Photo(5, "TopOfTruck", receiptId: receipt.Id));
        sample.Photos.Add(Photo(6, "Hectre", sampleId: sample.Id));
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

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file {Path.Combine(pathParts)}.");
    }
}

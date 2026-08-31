using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace CropQc.Api.Tests;

public sealed class QcSummaryEmailComposerTests
{
    [Theory]
    [InlineData("Door Sample", "WholeSample,CutApples")]
    [InlineData("Lot Sample", "WholeSample,CutApples")]
    [InlineData("Room Sample", "WholeSample,CutApples")]
    [InlineData("Line Sample", "WholeSample,CutApples")]
    [InlineData("Receiving Sample", "TopOfTruck,Hectre,WholeSample,CutApples,StarchApples")]
    [InlineData("Transfer Sample", "TopOfTruck,WholeSample,CutApples")]
    public void PhotoRequirementPolicy_MapsRequiredPhotosBySampleType(string sampleType, string expectedKeys)
    {
        var policy = new QcPhotoRequirementPolicy();

        var requirements = policy.GetRequirements(sampleType).Select(x => x.Key);

        Assert.Equal(expectedKeys.Split(','), requirements);
    }

    [Theory]
    [InlineData("Door Sample", "TruckPhoto,WholeSample,CutApples,StarchApples")]
    [InlineData("Lot Sample", "TruckPhoto,WholeSample,CutApples,StarchApples")]
    [InlineData("Room Sample", "TruckPhoto,WholeSample,CutApples,StarchApples")]
    [InlineData("Line Sample", "TruckPhoto,Hectre,WholeSample,CutApples,StarchApples")]
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

    [Theory]
    [InlineData("Door Sample")]
    [InlineData("Lot Sample")]
    public void PhotoRequirementPolicy_DoorAndLotSamplesShowOptionalTruckButNotHectre(string sampleType)
    {
        var policy = new QcPhotoRequirementPolicy();

        var available = policy.GetAvailablePhotoTypes(sampleType).Select(x => x.Key).ToList();
        var missing = policy.MissingRequiredPhotos(sampleType, receiptPhotoTypes: [], samplePhotoTypes: ["SampleBeforeCutting", "CutFruit"]);

        Assert.Contains("TruckPhoto", available);
        Assert.DoesNotContain("TopOfTruck", available);
        Assert.DoesNotContain("Hectre", available);
        Assert.Contains("StarchApples", available);
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("Receiving Sample")]
    [InlineData("Door Sample")]
    [InlineData("Lot Sample")]
    [InlineData("Field Sample")]
    public void PearSamples_RequireStarchPhotoButNotTruckPhoto(string sampleType)
    {
        var policy = new QcPhotoRequirementPolicy();

        var requirements = policy.GetRequirements(sampleType, "Pear");
        var available = policy.GetAvailablePhotoTypes(sampleType, "Pear");

        Assert.Contains(requirements, x => x.PhotoType == "FruitAfterStarch" && x.IsRequired);
        Assert.DoesNotContain(requirements, x => x.PhotoType == "BinTruck");
        Assert.Contains(available, x => x.PhotoType == "BinTruck" && !x.IsRequired);
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

    [Theory]
    [InlineData("TopTruck")]
    [InlineData("TopTruckPhoto")]
    [InlineData("TopOfTruckPhoto")]
    [InlineData("Top truck photo")]
    [InlineData("Top of truck")]
    public void PhotoRequirementPolicy_NormalizesTopOfTruckAliases(string legacyPhotoType)
    {
        var policy = new QcPhotoRequirementPolicy();

        var missing = policy.MissingRequiredPhotos(
            "Receiving Sample",
            receiptPhotoTypes: ["BinTruck", legacyPhotoType],
            samplePhotoTypes: ["Hectre", "SampleBeforeCutting", "CutFruit", "FruitAfterStarch"]);

        Assert.DoesNotContain("Missing required photo: Top of truck", missing);
        Assert.Equal("TopOfTruck", QcPhotoRequirementPolicy.NormalizePhotoType(legacyPhotoType));
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
        Assert.All(content.InlineImages, image => Assert.Equal("image/jpeg", image.ContentType));
        Assert.All(content.InlineImages, image => Assert.EndsWith(".jpg", image.FileName, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Receiving Sample")]
    [InlineData("Door Sample")]
    [InlineData("Lot Sample")]
    public async Task EmailComposer_ReceiptHeader_ShowsGrowerIdentityWithoutDuplicatedLot(string sampleType)
    {
        var sample = BuildSample(sampleType);
        var receipt = Assert.IsType<Receipt>(sample.Receipt);
        receipt.GrowerName = "ROLOFF FARM-NAGLE CONV";
        receipt.GrowerNumber = "9350";
        receipt.LotCode = "9350";
        var originalLotCode = receipt.LotCode;
        var originalGrowerNumber = receipt.GrowerNumber;
        var composer = new QcSummaryEmailComposer(new FakeFileStorageService(), new QcPhotoRequirementPolicy(), NullLogger<QcSummaryEmailComposer>.Instance);

        var content = await composer.ComposeAsync(sample, new ReadinessViewModel { IsReady = true }, sendingUser: null, isOverride: false, overrideReason: null, CancellationToken.None);

        Assert.Equal(1, CountOccurrences(content.HtmlBody, ">Grower number</th>"));
        Assert.Contains(">Grower</th><td style=\"border:1px solid #cbd5e1;\">ROLOFF FARM-NAGLE CONV</td>", content.HtmlBody);
        Assert.Contains(">Grower number</th><td style=\"border:1px solid #cbd5e1;\">9350</td>", content.HtmlBody);
        Assert.DoesNotContain(">Lot</th>", content.HtmlBody);
        Assert.DoesNotContain(">Orchard</th>", content.HtmlBody);
        Assert.DoesNotContain(">Block</th>", content.HtmlBody);
        Assert.True(content.HtmlBody.IndexOf(">Grower</th>", StringComparison.Ordinal) < content.HtmlBody.IndexOf(">Grower number</th>", StringComparison.Ordinal));
        Assert.True(content.HtmlBody.IndexOf(">Grower number</th>", StringComparison.Ordinal) < content.HtmlBody.IndexOf(">Bins received</th>", StringComparison.Ordinal));
        Assert.True(content.HtmlBody.IndexOf(">Bins received</th>", StringComparison.Ordinal) < content.HtmlBody.IndexOf(">Variety</th>", StringComparison.Ordinal));
        Assert.Contains("Grower: ROLOFF FARM-NAGLE CONV", content.TextBody);
        Assert.Contains("Grower number: 9350", content.TextBody);
        Assert.Contains("Bins received: 42", content.TextBody);
        Assert.Contains("Variety: 9450", content.TextBody);
        Assert.DoesNotContain("Grower/Lot/Variety:", content.TextBody);
        Assert.DoesNotContain(Environment.NewLine + "Lot:", content.TextBody);
        Assert.DoesNotContain("Orchard/Block:", content.TextBody);
        Assert.Equal(originalLotCode, receipt.LotCode);
        Assert.Equal(originalGrowerNumber, receipt.GrowerNumber);
    }

    [Fact]
    public async Task EmailComposer_EmbedsCidImagesWithoutDriveLinksForSuccessfulPhotos()
    {
        var sample = BuildSample("Receiving Sample");
        foreach (var photo in sample.Receipt.Photos.Concat(sample.Photos))
        {
            photo.StorageProvider = FileStorageProviders.GoogleDrive;
            photo.WebUrl = $"https://drive.google.com/file/d/{photo.FileId}/view";
        }

        var composer = new QcSummaryEmailComposer(new FakeFileStorageService(), new QcPhotoRequirementPolicy(), NullLogger<QcSummaryEmailComposer>.Instance);

        var content = await composer.ComposeAsync(sample, new ReadinessViewModel { IsReady = true }, sendingUser: null, isOverride: false, overrideReason: null, CancellationToken.None);

        Assert.NotEmpty(content.InlineImages);
        Assert.Contains("cid:cropqc-photo-1@cropqc", content.HtmlBody);
        Assert.DoesNotContain("drive.google.com", content.HtmlBody);
        Assert.DoesNotContain("drive.google.com", content.TextBody);
        Assert.DoesNotContain("<a href=\"https://drive.google.com", content.HtmlBody);
    }

    [Fact]
    public async Task EmailComposer_UsesActualExifNormalizedAndManualPresentationPixelsWithoutDoubleRotation()
    {
        var sample = BuildSample("Receiving Sample");
        sample.Receipt.Photos.Clear();
        sample.Photos.Clear();
        var photo = Photo(77, "Hectre", sampleId: sample.Id);
        var original = MarkerJpeg(6);
        photo.FileId = "email-original";
        photo.SharePointItemId = "email-original";
        sample.Photos.Add(photo);
        var storage = new KeyedPhotoStorage(new Dictionary<string, byte[]> { ["email-original"] = original });
        var composer = new QcSummaryEmailComposer(storage, new QcPhotoRequirementPolicy(), NullLogger<QcSummaryEmailComposer>.Instance);

        var automatic = await composer.ComposeAsync(sample, new ReadinessViewModel { IsReady = true }, null, false, null, CancellationToken.None);
        var automaticImage = Assert.Single(automatic.InlineImages);
        AssertCornerOrder(automaticImage.Bytes, "CADB");
        using (var decoded = Image.Load(automaticImage.Bytes))
            Assert.False(decoded.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out _) == true);

        await using var originalStream = new MemoryStream(original, writable: false);
        var manualPresentation = await PhotoOrientationProcessor.CreatePresentationAsync(
            originalStream, photo.FileName, photo.ContentType, 1, CancellationToken.None);
        storage.Bytes["email-presentation"] = manualPresentation.Bytes;
        photo.OriginalExifOrientation = 6;
        photo.ManualRotationQuarterTurns = 1;
        photo.PresentationRevision = 2;
        photo.PresentationStorageKey = "email-presentation";
        photo.PresentationFileName = "email-presentation.jpg";
        photo.PresentationContentType = "image/jpeg";
        photo.PresentationFileSizeBytes = manualPresentation.Bytes.Length;
        photo.PresentationUpdatedAt = DateTimeOffset.UtcNow;

        var manual = await composer.ComposeAsync(sample, new ReadinessViewModel { IsReady = true }, null, false, null, CancellationToken.None);
        var manualImage = Assert.Single(manual.InlineImages);
        AssertCornerOrder(manualImage.Bytes, "DCBA");
        using var manualDecoded = Image.Load(manualImage.Bytes);
        Assert.False(manualDecoded.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out _) == true);
    }

    [Fact]
    public async Task EmailComposer_ResizesAndCompressesLargeGoogleDrivePhotosBeforeEmbedding()
    {
        var sample = BuildSample("Receiving Sample");
        sample.Receipt.Photos.Clear();
        sample.Photos.Clear();
        var photo = Photo(2, "SampleBeforeCutting", sampleId: sample.Id);
        photo.StorageProvider = FileStorageProviders.GoogleDrive;
        photo.WebUrl = "https://drive.google.com/file/d/photo-2/view";
        sample.Photos.Add(photo);
        var originalBytes = CreateBmp(1800, 1200);
        var composer = new QcSummaryEmailComposer(new FakeFileStorageService(originalBytes), new QcPhotoRequirementPolicy(), NullLogger<QcSummaryEmailComposer>.Instance);

        var content = await composer.ComposeAsync(sample, new ReadinessViewModel { IsReady = true }, sendingUser: null, isOverride: false, overrideReason: null, CancellationToken.None);

        var image = Assert.Single(content.InlineImages);
        Assert.Equal("image/jpeg", image.ContentType);
        Assert.True(image.Bytes.Length <= QcSummaryEmailComposer.MaxInlineImageBytes);
        Assert.True(image.Bytes.Length < originalBytes.Length);
        Assert.Contains("cid:cropqc-photo-2@cropqc", content.HtmlBody);
        Assert.DoesNotContain("drive.google.com", content.HtmlBody);
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
    public async Task EmailComposer_MapsLegacyTopTruckPhotoToSingleTopOfTruckSection()
    {
        var sample = BuildSample("Receiving Sample");
        sample.Receipt.Photos.Single(x => x.PhotoType == "TopOfTruck").PhotoType = "TopTruckPhoto";
        var readiness = new ReadinessViewModel { CompletedFruitCount = 1 };
        var composer = new QcSummaryEmailComposer(new FakeFileStorageService(), new QcPhotoRequirementPolicy(), NullLogger<QcSummaryEmailComposer>.Instance);

        var content = await composer.ComposeAsync(sample, readiness, sendingUser: null, isOverride: false, overrideReason: null, CancellationToken.None);

        Assert.Contains("<h3>Top of truck</h3>", content.HtmlBody);
        Assert.DoesNotContain("Top truck photo", content.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(content.HtmlBody, "<h3>Top of truck</h3>"));
        Assert.Contains("- Top of truck: 1 photo(s)", content.TextBody);
    }

    [Fact]
    public async Task EmailComposer_LinksOversizedPhotosInsteadOfEmbeddingOriginalBytes()
    {
        var sample = BuildSample("Receiving Sample");
        sample.Receipt.Photos.Single(x => x.PhotoType == "BinTruck").WebUrl = "https://drive.example/photo";
        var composer = new QcSummaryEmailComposer(new LargePhotoFileStorageService(25_000_001), new QcPhotoRequirementPolicy(), NullLogger<QcSummaryEmailComposer>.Instance);

        var content = await composer.ComposeAsync(sample, new ReadinessViewModel { IsReady = true }, sendingUser: null, isOverride: false, overrideReason: null, CancellationToken.None);

        Assert.Empty(content.InlineImages);
        Assert.DoesNotContain("cid:cropqc-photo-", content.HtmlBody);
        Assert.Contains("Photo was too large to embed and is linked instead.", content.HtmlBody);
        Assert.Contains("https://drive.example/photo", content.HtmlBody);
    }

    [Fact]
    public async Task EmailComposer_IgnoresRemovedPhotos()
    {
        var sample = BuildSample("Receiving Sample");
        sample.Receipt.Photos.Single(x => x.PhotoType == "TopOfTruck").IsDeleted = true;
        var composer = new QcSummaryEmailComposer(new FakeFileStorageService(), new QcPhotoRequirementPolicy(), NullLogger<QcSummaryEmailComposer>.Instance);

        var content = await composer.ComposeAsync(sample, new ReadinessViewModel { IsReady = true }, sendingUser: null, isOverride: false, overrideReason: null, CancellationToken.None);

        Assert.DoesNotContain("<h3>Top of truck</h3>", content.HtmlBody);
        Assert.DoesNotContain("- Top of truck:", content.TextBody);
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
        Assert.Contains("Average Pressure</th><td style=\"border:1px solid #cbd5e1;\">12</td>", content.HtmlBody);
        Assert.Contains("Pressure std dev lbs</th><td style=\"border:1px solid #cbd5e1;\">2</td>", content.HtmlBody);
        Assert.Contains("Average weight grams</th><td style=\"border:1px solid #cbd5e1;\">162.5</td>", content.HtmlBody);
        Assert.Contains("Grade summary</th><td style=\"border:1px solid #cbd5e1;\">Fancy: 1</td>", content.HtmlBody);
        Assert.Contains("Defect summary</th><td style=\"border:1px solid #cbd5e1;\">2 of 5 inspected fruit affected; Bruise: 1, Sunburn: 1</td>", content.HtmlBody);
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
        Assert.Contains("Photos / Requirements", sampleView);
        Assert.DoesNotContain("Available Photo Types", sampleView);
        Assert.DoesNotContain("<h3>Required Photos</h3>", sampleView);
        Assert.Contains("\"TopOfTruck\" => \"Top of truck\"", photoForm);
        Assert.Contains("\"Hectre\" => \"Hectre\"", photoForm);
    }

    [Fact]
    public void ReceiptsOpenReceiving_UsesConfiguredSampleTypeWithoutManualDropdown()
    {
        var receiptView = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Details.cshtml"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var seeder = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "MasterDataSeeder.cs"));

        Assert.DoesNotContain("Model.SampleTypes", receiptView);
        Assert.DoesNotContain("name=\"SampleTypeId\"", receiptView);
        Assert.Contains("ReceiptQcSampleCoordinator.OpenOrCreateAsync", service);
        Assert.Contains("\"Lot Sample\"", seeder);
    }

    [Fact]
    public void PhotoRemovalUi_IsAvailableInSinglePhotoSection()
    {
        var photoGroups = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_PhotoGroups.cshtml"));
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "SamplesController.cs"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));

        Assert.Contains("aria-label=\"Remove photo\"", photoGroups);
        Assert.Contains("title=\"Remove photo\"", photoGroups);
        Assert.Contains("Remove this receipt photo?", photoGroups);
        Assert.Contains("Remove this photo from the sample?", photoGroups);
        Assert.Contains("RemoveSamplePhotoAsync", controller);
        Assert.Contains("IsDeleted = true", service);
        Assert.Contains("remove-photo", service);
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
            GrowerNumber = "LOT-1",
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

    private static byte[] MarkerJpeg(int orientation)
    {
        using var image = new Image<Rgba32>(80, 60);
        Fill(image, 0, 0, 40, 30, Color.Red);
        Fill(image, 40, 0, 40, 30, Color.Lime);
        Fill(image, 0, 30, 40, 30, Color.Blue);
        Fill(image, 40, 30, 40, 30, Color.Yellow);
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)orientation);
        using var output = new MemoryStream();
        image.Save(output, new JpegEncoder { Quality = 100 });
        return output.ToArray();
    }

    private static void Fill(Image<Rgba32> image, int x, int y, int width, int height, Color color)
    {
        var pixel = color.ToPixel<Rgba32>();
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
            {
                image[column, row] = pixel;
            }
        }
    }

    private static void AssertCornerOrder(byte[] bytes, string expected)
    {
        using var image = Image.Load<Rgba32>(bytes);
        var actual = string.Concat(
            Marker(image[image.Width / 4, image.Height / 4]),
            Marker(image[image.Width * 3 / 4, image.Height / 4]),
            Marker(image[image.Width / 4, image.Height * 3 / 4]),
            Marker(image[image.Width * 3 / 4, image.Height * 3 / 4]));
        Assert.Equal(expected, actual);
    }

    private static char Marker(Rgba32 pixel)
    {
        if (pixel.R > 180 && pixel.G < 100 && pixel.B < 100) return 'A';
        if (pixel.G > 150 && pixel.R < 100 && pixel.B < 100) return 'B';
        if (pixel.B > 150 && pixel.R < 100 && pixel.G < 100) return 'C';
        if (pixel.R > 150 && pixel.G > 150 && pixel.B < 100) return 'D';
        return '?';
    }

    private sealed class KeyedPhotoStorage(Dictionary<string, byte[]> bytes) : IFileStorageService
    {
        public Dictionary<string, byte[]> Bytes { get; } = bytes;
        public string GenerateTargetPath(FileStorageTargetContext context) => "unused";
        public Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FileStorageReference?> GetMetadataAsync(string storageKey, CancellationToken cancellationToken = default) => Task.FromResult<FileStorageReference?>(null);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Bytes.TryGetValue(storageKey, out var value) ? new MemoryStream(value, writable: false) : null);
        public Task DeleteOrVoidAsync(string storageKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeFileStorageService(byte[]? bytes = null) : IFileStorageService
    {
        private readonly byte[] bytes = bytes ?? CreateBmp(24, 24);

        public string GenerateTargetPath(FileStorageTargetContext context) => "target";
        public Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<FileStorageReference?> GetMetadataAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<FileStorageReference?>(null);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(new MemoryStream(bytes));
        public Task DeleteOrVoidAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class LargePhotoFileStorageService(int length) : IFileStorageService
    {
        public string GenerateTargetPath(FileStorageTargetContext context) => "target";
        public Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<FileStorageReference?> GetMetadataAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<FileStorageReference?>(null);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(new FixedLengthStream(length));
        public Task DeleteOrVoidAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedLengthStream(int length) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => _position = (int)value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= length)
            {
                return 0;
            }

            var read = Math.Min(count, length - _position);
            Array.Fill<byte>(buffer, 1, offset, read);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => (int)offset,
                SeekOrigin.Current => _position + (int)offset,
                SeekOrigin.End => length + (int)offset,
                _ => _position
            };
            return _position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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

    private static int CountOccurrences(string value, string match)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(match, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += match.Length;
        }

        return count;
    }

    private static byte[] CreateBmp(int width, int height)
    {
        var rowStride = ((width * 3) + 3) & ~3;
        var pixelBytes = rowStride * height;
        var fileSize = 54 + pixelBytes;
        var bytes = new byte[fileSize];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        WriteInt32(bytes, 2, fileSize);
        WriteInt32(bytes, 10, 54);
        WriteInt32(bytes, 14, 40);
        WriteInt32(bytes, 18, width);
        WriteInt32(bytes, 22, height);
        WriteInt16(bytes, 26, 1);
        WriteInt16(bytes, 28, 24);
        WriteInt32(bytes, 34, pixelBytes);

        for (var y = 0; y < height; y++)
        {
            var rowStart = 54 + (y * rowStride);
            for (var x = 0; x < width; x++)
            {
                var offset = rowStart + (x * 3);
                bytes[offset] = (byte)(x % 256);
                bytes[offset + 1] = (byte)(y % 256);
                bytes[offset + 2] = (byte)((x + y) % 256);
            }
        }

        return bytes;
    }

    private static void WriteInt16(byte[] bytes, int offset, short value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteInt32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }
}

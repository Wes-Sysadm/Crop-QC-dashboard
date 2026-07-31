using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using CropQc.Web.Services;
using CropQc.Shared.Time;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using System.Text;

namespace CropQc.Api.Tests;

public sealed class PackoutReconciliationTests
{
    [Fact]
    public async Task DefectStatus_FollowsPresenceOfDefectRecords()
    {
        await using var db = Db();
        var sampleType = new SampleType { Name = "Receiving Sample" };
        var defectType = new DefectType { Name = "Bruise" };
        var sample = Sample(sampleType);
        var row = new QcFruitReading
        {
            QcSample = sample,
            RowNumber = 1,
            SizeStatus = "Not calculated",
            WeightGrams = 200m,
            CreatedAt = DateTimeOffset.UtcNow
        };
        sample.FruitReadings.Add(row);
        db.AddRange(sampleType, defectType, sample);
        await db.SaveChangesAsync();

        Assert.Equal(DefectInspectionStatuses.NoDefectsFound, sample.DefectInspectionStatus);

        var defect = new QcFruitDefect { QcFruitReading = row, DefectType = defectType };
        db.QcFruitDefects.Add(defect);
        await db.SaveChangesAsync();
        Assert.Equal(DefectInspectionStatuses.DefectsFound, sample.DefectInspectionStatus);

        db.QcFruitDefects.Remove(defect);
        await db.SaveChangesAsync();
        Assert.Equal(DefectInspectionStatuses.NoDefectsFound, sample.DefectInspectionStatus);
        Assert.Empty(await db.QcFruitDefects.ToListAsync());
    }

    [Fact]
    public async Task DefectStatus_IsCorrectWhenNewGraphAlreadyContainsDefect()
    {
        await using var db = Db();
        var sampleType = new SampleType { Name = "Field Sample" };
        var defectType = new DefectType { Name = "Scuff" };
        var sample = Sample(sampleType);
        var row = new QcFruitReading
        {
            QcSample = sample,
            RowNumber = 1,
            SizeStatus = "Not calculated",
            CreatedAt = DateTimeOffset.UtcNow
        };
        row.Defects.Add(new QcFruitDefect { QcFruitReading = row, DefectType = defectType });
        sample.FruitReadings.Add(row);
        db.AddRange(sampleType, defectType, sample);

        await db.SaveChangesAsync();

        Assert.Equal(DefectInspectionStatuses.DefectsFound, sample.DefectInspectionStatus);
    }

    [Fact]
    public void ActualCalculation_UsesPackWeightsAndDumpedWeightDenominators()
    {
        var result = PackoutReconciliationCalculationService.Calculate(
            dumpedBins: 10m,
            poundsPerBin: 920m,
            actualLines:
            [
                new(100m, 40m, PackoutProductCategories.Packed, 80, "US1"),
                new(2m, 900m, PackoutProductCategories.Juice),
                new(1m, 920m, PackoutProductCategories.Waste)
            ],
            projectedSizePercentages: new Dictionary<string, decimal> { ["80"] = 100m },
            projectedGradePercentages: new Dictionary<string, decimal> { ["US1"] = 100m },
            projectedPackoutPercent: 50m,
            projectedJuicePercent: 20m,
            projectedPeelerSlicerPercent: 0m,
            projectedWastePercent: 10m);

        Assert.Equal(4000m, result.PackedProductPounds);
        Assert.Equal(1800m, result.JuicePounds);
        Assert.Equal(920m, result.WastePounds);
        Assert.Equal(decimal.Round(4000m / 9200m * 100m, 4), result.PackoutPercent);
        Assert.Equal(decimal.Round(1800m / 9200m * 100m, 4), result.JuicePercent);
    }

    [Fact]
    public void ActualCalculation_NegativeConfirmedLinesReduceExactBucket()
    {
        var result = PackoutReconciliationCalculationService.Calculate(
            1m,
            880m,
            [
                new(10m, 40m, PackoutProductCategories.Packed, 80, "W1"),
                new(-2m, 40m, PackoutProductCategories.Packed, 80, "W1")
            ],
            new Dictionary<string, decimal> { ["80"] = 100m },
            new Dictionary<string, decimal> { ["W1"] = 100m },
            36.3636m,
            0m,
            0m,
            0m);

        Assert.Equal(320m, result.PackedProductPounds);
        Assert.Equal(320m, Assert.Single(result.SizeDistribution).Pounds);
    }

    [Fact]
    public void Accuracy_UsesRequiredFormulaAndDoesNotRedistributeMissingComponents()
    {
        var result = PackoutReconciliationCalculationService.Calculate(
            10m,
            880m,
            [new(100m, 40m, PackoutProductCategories.Packed, 80, null)],
            new Dictionary<string, decimal> { ["80"] = 50m, ["90"] = 50m },
            new Dictionary<string, decimal> { ["W1"] = 100m },
            50m,
            0m,
            0m,
            0m);

        Assert.Equal(50m, result.SizeAccuracy);
        Assert.Null(result.GradeAccuracy);
        Assert.True(result.OverallAccuracy < 65m);
    }

    [Theory]
    [InlineData(968, 880, 88, false)]
    [InlineData(969, 880, 89, true)]
    public void ReconciliationWarning_IsStrictlyGreaterThanTenPercent(
        decimal packedPounds,
        decimal dumpedPounds,
        decimal expectedDifference,
        bool expectedWarning)
    {
        var quantity = packedPounds / 40m;
        var result = PackoutReconciliationCalculationService.Calculate(
            1m,
            dumpedPounds,
            [new(quantity, 40m, PackoutProductCategories.Packed)],
            new Dictionary<string, decimal>(),
            new Dictionary<string, decimal>(),
            null,
            null,
            null,
            null);

        Assert.Equal(expectedDifference, Math.Abs(result.ReconciliationDifferencePounds));
        Assert.Equal(expectedWarning, result.HasReconciliationWarning);
    }

    [Theory]
    [InlineData("900L", "Juice", 900)]
    [InlineData("650l", "Juice", 650)]
    [InlineData("WP", null, null)]
    public void PackCodeClassification_HandlesNumericLCodes(
        string code,
        string? category,
        int? pounds)
    {
        var result = PackoutReportParser.ClassifyPackCode(code);

        Assert.Equal(category, result.ProductCategory);
        Assert.Equal(pounds is null ? null : (decimal?)pounds.Value, result.NetWeightPounds);
    }

    [Fact]
    public async Task CsvParser_ReturnsParsedMetadataWithoutOriginalBytes()
    {
        var parser = new PackoutReportParser(NullLogger<PackoutReportParser>.Instance);
        var bytes = System.Text.Encoding.UTF8.GetBytes("REG BART US1 80 WP 12\nREG BART NOGR 900L 2");
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            var result = await parser.ParseAsync(new("run.csv", "text/csv", path, bytes.Length), CancellationToken.None);

            Assert.Equal("run.csv", result.FileName);
            Assert.Equal(bytes.Length, result.FileSizeBytes);
            Assert.Equal(64, result.Sha256.Length);
            Assert.Equal(2, result.Lines.Count);
            Assert.DoesNotContain(result.GetType().GetProperties(), x => x.PropertyType == typeof(byte[]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ActualRunSupportingDocument_UploadReviewFinalize_DoesNotChangeInventory()
    {
        await using var db = Db();
        if (db.Database.IsRelational())
        {
            Assert.True(
                await db.Database.EnsureCreatedAsync(),
                "The configured disposable PostgreSQL packout database must start empty.");
        }
        var now = DateTimeOffset.Parse("2026-07-31T16:00:00Z");
        var user = new User
        {
            Email = ApplicationAreas.OwnerEmail,
            DisplayName = "Packout Test Owner",
            IsActive = true,
            CreatedAt = now
        };
        var warehouse = new Warehouse
        {
            Id = 9100,
            Code = "PACKOUTTEST",
            Name = "Disposable Packout Test",
            IsActive = true
        };
        var room = new Room
        {
            Id = 9101,
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            Code = "PACKOUT-1",
            Name = "Packout Room 1",
            CropQcRoomName = "PACKOUT-1",
            IsActive = true
        };
        var actualRun = new ActualRun
        {
            Id = 9200,
            Status = ActualRunStatuses.Active,
            CurrentRevisionNumber = 1,
            RunAt = now,
            CreatedAt = now
        };
        var revision = new ActualRunRevision
        {
            Id = 9201,
            ActualRunId = actualRun.Id,
            ActualRun = actualRun,
            RevisionNumber = 1,
            OperationType = ActualRunRevisionTypes.Create,
            OperationKey = "packout-test-create",
            IsCurrent = true,
            CreatedAt = now
        };
        var adjustment = new RoomInventoryAdjustment
        {
            Id = 9500,
            ActualRunId = actualRun.Id,
            ActualRun = actualRun,
            ActualRunRevisionId = revision.Id,
            ActualRunRevision = revision,
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            RoomId = room.Id,
            Room = room,
            CropYear = 2026,
            GrowerName = "Test Grower",
            LotNumber = "1084",
            VarietyCode = "Bartlett",
            OldBinCount = 20,
            ChangeAmount = -10,
            NewBinCount = 10,
            AdjustmentType = BinsRunService.AdjustmentType,
            Source = "Disposable Actual Run test",
            AdjustmentAt = now,
            CreatedAt = now,
            InventoryInvariantVersion = 1,
            InventoryOperationKey = "packout-test-depletion"
        };
        var binsRun = new BinsRunEntry
        {
            Id = 9202,
            ActualRunId = actualRun.Id,
            ActualRun = actualRun,
            ActualRunRevisionId = revision.Id,
            ActualRunRevision = revision,
            InventoryAdjustmentId = adjustment.Id,
            InventoryAdjustment = adjustment,
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            RoomId = room.Id,
            Room = room,
            CropYear = 2026,
            GrowerName = "Test Grower",
            LotNumber = "1084",
            VarietyCode = "Bartlett",
            PreviousAvailableBins = 20,
            BinsRun = 10,
            NewAvailableBins = 10,
            RunAt = now,
            CreatedAt = now,
            TransactionType = ActualRunTransactionTypes.Depletion
        };
        var expectation = new RunExpectation
        {
            Id = 9203,
            ActualRunId = actualRun.Id,
            ActualRun = actualRun,
            ActualRunRevisionId = revision.Id,
            ActualRunRevision = revision,
            RevisionNumber = 1,
            FacilityWarehouseId = warehouse.Id,
            FacilitySnapshot = "WP",
            RunAtSnapshot = now,
            TotalBins = 10,
            GrossPounds = 9200m,
            ExpectedPackoutPercent = 80m,
            ExpectedPackedPounds = 7360m,
            ExpectedPackedBoxes = 184m,
            ExpectedWholeBoxes = 184,
            ExpectedCullPounds = 1840m,
            ExpectedJuicePounds = 736m,
            ExpectedPeelerPounds = 644m,
            ExpectedWastePounds = 460m,
            ConfidencePercent = 90m,
            SizeDistributionSnapshotJson = "{\"80\":100}",
            GradeDistributionSnapshotJson = "{}",
            ConfigurationSnapshotJson = "{}",
            CalculationVersion = RunExpectationCalculationVersions.Current,
            CalculatedAt = now
        };
        expectation.Sources.Add(new RunExpectationSource
        {
            Id = 9204,
            RunExpectationId = expectation.Id,
            RunExpectation = expectation,
            BinsRunEntryId = binsRun.Id,
            BinsRunEntry = binsRun,
            WarehouseId = warehouse.Id,
            RoomId = room.Id,
            FacilitySnapshot = "WP",
            RoomSnapshot = "WP-1",
            CropYearSnapshot = 2026,
            GrowerSnapshot = "Test Grower",
            LotSnapshot = "1084",
            VarietySnapshot = "Bartlett",
            ProductionTypeSnapshot = "Conventional",
            BinsContributed = 10,
            ContributionPercent = 100m,
            QcFruitCountSnapshot = 25,
            QcMeasurementSnapshotJson = "{}",
            SizeDistributionSnapshotJson = "{\"80\":100}",
            GradeDistributionSnapshotJson = "{}",
            GrossPounds = 9200m,
            ExpectedPackedPounds = 7360m,
            ExpectedWholeBoxes = 184,
            ExpectedCullPounds = 1840m,
            ConfidencePercent = 90m
        });
        db.AddRange(user, warehouse, room, actualRun, revision, adjustment, binsRun, expectation);
        db.PackCodeDefinitions.Add(new PackCodeDefinition
        {
            Code = "WP",
            NormalizedCode = "WP",
            DisplayName = "40-pound box",
            ProductCategory = PackoutProductCategories.Packed,
            NetWeightPounds = 40m,
            SizeCategory = 80,
            IsActive = true,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        var options = new PackoutProcessingOptions();
        var configuration = new ConfigurationBuilder().Build();
        var emailSender = new RecordingEmailSender();
        var service = new PackoutReconciliationService(
            db,
            new PackoutReportParser(options, NullLogger<PackoutReportParser>.Instance),
            new PackoutFeedbackWorkbookService(options, NullLogger<PackoutFeedbackWorkbookService>.Instance),
            emailSender,
            new UserAccessService(db, configuration),
            new PacificBusinessTimeService(new FixedClock(now)),
            configuration,
            options,
            new PackoutOperationCoordinator(),
            NullLogger<PackoutReconciliationService>.Instance);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "Test"));
        var csv = Encoding.UTF8.GetBytes("REG BART US1 80 WP 12");
        await using var stream = new MemoryStream(csv);
        var formFile = new FormFile(stream, 0, csv.Length, "Files", "packout.csv")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };
        var inventoryCountBefore = await db.RoomInventoryAdjustments.CountAsync();

        var upload = await service.UploadAsync(new PackoutUploadForm
        {
            ActualRunId = actualRun.Id,
            PackingDate = new DateOnly(2026, 7, 31),
            RunNumber = 1,
            DumpedBins = 10m,
            Files = [formFile]
        }, principal, CancellationToken.None);

        Assert.Null(upload.Error);
        var review = await service.GetAsync(upload.Id!.Value, principal, CancellationToken.None);
        Assert.NotNull(review);
        Assert.Single(review.Sources);
        Assert.Single(review.Lines);
        Assert.True(review.Lines[0].RequiresReview);

        var correctionError = await service.UpdateLineAsync(new PackoutLineReviewForm
        {
            PackoutRunId = review.Id,
            LineId = review.Lines[0].Id,
            ConcurrencyVersion = review.ConcurrencyVersion,
            PackCode = "WP",
            Quantity = 12m,
            NetWeightPounds = 40m,
            SizeCategory = 80,
            ProductCategory = PackoutProductCategories.Packed,
            CorrectionReason = "Confirmed the supported test report row."
        }, principal, CancellationToken.None);

        Assert.Null(correctionError);
        review = await service.GetAsync(upload.Id.Value, principal, CancellationToken.None);
        Assert.NotNull(review);
        Assert.False(review.Lines[0].RequiresReview);

        var finalized = await service.FinalizeAsync(new PackoutFinalizeForm
        {
            PackoutRunId = review.Id,
            ConcurrencyVersion = review.ConcurrencyVersion
        }, principal, CancellationToken.None);

        Assert.Null(finalized.Error);
        Assert.NotNull(finalized.Workbook);
        Assert.Single(emailSender.Messages);
        Assert.Equal(PackoutRunStatuses.Finalized, (await db.PackoutRuns.SingleAsync()).Status);
        Assert.Single(await db.PackoutSourceAllocations.ToListAsync());
        Assert.Equal(inventoryCountBefore, await db.RoomInventoryAdjustments.CountAsync());
    }

    [Fact]
    public void UploadLimits_RejectExcessiveFileCountAndTotalBytes()
    {
        var options = new PackoutProcessingOptions
        {
            MaximumFilesPerUpload = 2,
            MaximumFileBytes = 100,
            MaximumTotalUploadBytes = 150
        };

        Assert.Contains("between 1 and 2", PackoutUploadLimits.Validate([1, 1, 1], options));
        Assert.Contains("combined upload", PackoutUploadLimits.Validate([80, 80], options));
        Assert.Null(PackoutUploadLimits.Validate([70, 80], options));
    }

    [Fact]
    public void UploadLimits_RejectExcessivePdfPageCount()
    {
        var options = new PackoutProcessingOptions { MaximumPdfPages = 3 };

        Assert.Contains("at most 3 pages", PackoutUploadLimits.ValidatePdfPageCount(4, options));
        Assert.Null(PackoutUploadLimits.ValidatePdfPageCount(3, options));
    }

    [Fact]
    public async Task Parser_RejectsOversizedFileBeforeReadingIt()
    {
        var options = new PackoutProcessingOptions
        {
            MaximumFileBytes = 100,
            MaximumTotalUploadBytes = 150
        };
        var parser = new PackoutReportParser(options, NullLogger<PackoutReportParser>.Instance);
        var path = Path.GetTempFileName();
        try
        {
            await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                stream.SetLength(101);
            }

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => parser.ParseAsync(new("oversized.csv", "text/csv", path, 101), CancellationToken.None));
            Assert.Contains("between 1 byte", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Parser_RejectsSpreadsheetRowsBeyondConfiguredLimit()
    {
        var options = new PackoutProcessingOptions
        {
            MaximumFileBytes = 1024,
            MaximumTotalUploadBytes = 2048,
            MaximumSpreadsheetRows = 2
        };
        var parser = new PackoutReportParser(options, NullLogger<PackoutReportParser>.Instance);
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "PACK 80 1\nPACK 90 1\nPACK 100 1");
            var length = new FileInfo(path).Length;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => parser.ParseAsync(new("rows.csv", "text/csv", path, length), CancellationToken.None));
            Assert.Contains("at most 2 rows", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Parser_RejectsImageDimensionsBeyondConfiguredLimitBeforeOcr()
    {
        var options = new PackoutProcessingOptions
        {
            MaximumFileBytes = 1024,
            MaximumTotalUploadBytes = 2048,
            MaximumImagePixels = 1_000_000
        };
        var parser = new PackoutReportParser(options, NullLogger<PackoutReportParser>.Instance);
        var pngHeader = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(pngHeader, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(pngHeader.AsSpan(16, 4), 2000);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(pngHeader.AsSpan(20, 4), 2000);
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, pngHeader);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => parser.ParseAsync(new("large.png", "image/png", path, pngHeader.Length), CancellationToken.None));
            Assert.Contains("at most 1,000,000 pixels", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OperationCoordinator_PreventsDuplicateRunOperationAndReleasesLease()
    {
        var coordinator = new PackoutOperationCoordinator();
        using (coordinator.TryEnter(42, "finalize"))
        {
            Assert.Null(coordinator.TryEnter(42, "finalize"));
            using var workbookLease = coordinator.TryEnter(42, "workbook");
            Assert.NotNull(workbookLease);
        }

        using var releasedLease = coordinator.TryEnter(42, "finalize");
        Assert.NotNull(releasedLease);
    }

    [Fact]
    public void WorkbookGeneration_WritesBoundedCompressedOutput()
    {
        var run = Run(42, "1084", "Bartlett", 2026, 100m, 80m, 10m, 6m, 4m);
        var source = new PackoutReportSource
        {
            OriginalFileName = "run.csv",
            ContentType = "text/csv",
            FileSizeBytes = 100,
            Sha256 = new string('a', 64),
            ParserName = "DelimitedText",
            ParsedAt = DateTimeOffset.UtcNow
        };
        run.Sources.Add(source);
        for (var index = 1; index <= 1000; index++)
        {
            run.Lines.Add(new PackoutReportLine
            {
                PackoutReportSource = source,
                SourceLineNumber = index,
                RawText = $"PACK 80 {index}",
                Quantity = index,
                NetWeightPounds = 40m,
                ExtendedWeightPounds = index * 40m,
                ProductCategory = PackoutProductCategories.Packed,
                Confidence = 1m,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        var service = new PackoutFeedbackWorkbookService(
            new PackoutProcessingOptions { MaximumWorkbookRows = 2000 },
            NullLogger<PackoutFeedbackWorkbookService>.Instance);

        var workbook = service.Build(run);

        Assert.Equal((byte)'P', workbook[0]);
        Assert.Equal((byte)'K', workbook[1]);
        Assert.True(workbook.Length < 1_000_000);
    }

    [Fact]
    public void WorkbookGeneration_RejectsExcessiveRows()
    {
        var service = new PackoutFeedbackWorkbookService(
            new PackoutProcessingOptions { MaximumWorkbookRows = 10 },
            NullLogger<PackoutFeedbackWorkbookService>.Instance);

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.Build(Run(43, "1084", "Bartlett", 2026, 100m, 80m, 10m, 6m, 4m)));

        Assert.Contains("at most 10 rows", exception.Message);
    }

    [Fact]
    public void Parser_RecognizesNegativeAdjustmentsAsReviewItems()
    {
        var result = Assert.Single(PackoutReportParser.ParseText("REG BART US1 80 WP -2"));

        Assert.Equal(-2m, result.Quantity);
        Assert.True(result.RequiresReview);
    }

    [Fact]
    public async Task HistorySuggestion_NoHistoryHasNoPackoutAndUsesFixedCullMix()
    {
        await using var db = Db();
        var suggestion = await new PackoutHistoricalSuggestionService(db).GetAsync(
            new DateOnly(2026, 7, 29), 2026, "1084", "Bartlett", false, CancellationToken.None);

        Assert.Null(suggestion.PackoutPercent);
        Assert.Equal(0.35m, suggestion.JuiceCullShare);
        Assert.Equal(0.35m, suggestion.PeelerSlicerCullShare);
        Assert.Equal(0.30m, suggestion.WasteCullShare);
    }

    [Fact]
    public async Task HistorySuggestion_UsesExactLotOnlyAfterOneHundredCurrentYearBins()
    {
        await using var db = Db();
        db.PackoutRuns.AddRange(
            Run(1, "1084", "Bartlett", 2026, 60m, 80m, 10m, 6m, 4m),
            Run(2, "1084", "Bartlett", 2026, 40m, 90m, 5m, 3m, 2m),
            Run(3, "OTHER", "Bartlett", 2026, 200m, 50m, 25m, 15m, 10m));
        await db.SaveChangesAsync();

        var suggestion = await new PackoutHistoricalSuggestionService(db).GetAsync(
            new DateOnly(2026, 7, 29), 2026, "1084", "Bartlett", false, CancellationToken.None);

        Assert.Equal("Exact lot history", suggestion.Basis);
        Assert.Equal(new long[] { 1, 2 }, suggestion.RunIds);
        Assert.DoesNotContain(3, suggestion.RunIds);
        Assert.Equal(100m, suggestion.TotalDumpedBins);
    }

    [Fact]
    public async Task HistorySuggestion_FallsBackByVarietyBeforeThreshold()
    {
        await using var db = Db();
        db.PackoutRuns.AddRange(
            Run(1, "1084", "Bartlett", 2026, 99m, 80m, 10m, 6m, 4m),
            Run(2, "OTHER", "Bartlett", 2026, 1m, 60m, 20m, 12m, 8m),
            Run(3, "1084", "Bartlett", 2026, 100m, 20m, 40m, 24m, 16m, organic: true));
        await db.SaveChangesAsync();

        var suggestion = await new PackoutHistoricalSuggestionService(db).GetAsync(
            new DateOnly(2026, 7, 29), 2026, "1084", "Bartlett", false, CancellationToken.None);

        Assert.Equal("Variety fallback history", suggestion.Basis);
        Assert.Equal(new long[] { 1, 2 }, suggestion.RunIds);
    }

    [Fact]
    public void Migration_BackfillsDefectsAndUsesProviderSpecificTypes()
    {
        var migration = Read("src", "CropQc.Data", "Migrations", "20260729165910_AddPackoutProjectionReconciliation.cs");

        Assert.Contains("Defects found", migration);
        Assert.Contains("No defects found", migration);
        Assert.Contains("MigrationProviderTypes.StoreType", migration);
        Assert.Contains("NpgsqlValueGenerationStrategy.IdentityByDefaultColumn", migration);
        Assert.Contains("timestamp with time zone", migration);
        Assert.Contains("boolean", migration);
    }

    [Fact]
    public void UserInterface_RequiresRunIdentityAndExplicitNegativeConfirmation()
    {
        var upload = Read("src", "CropQc.Web", "Views", "BinsRun", "ActualRunDetail.cshtml");
        var review = Read("src", "CropQc.Web", "Views", "BinsRun", "PackoutReview.cshtml");
        var projection = Read("src", "CropQc.Web", "Views", "BinsRun", "ProjectionOutcome.cshtml");

        Assert.Contains("name=\"PackingDate\"", upload);
        Assert.Contains("name=\"RunNumber\"", upload);
        Assert.DoesNotContain("name=\"PackingDate\"", projection);
        Assert.Contains("NegativeQuantityConfirmed", review);
        Assert.Contains("Original uploads are deleted after parsing", review);
        Assert.Contains("Packout Result Admin", Read("src", "CropQc.Web", "Services", "PackoutReconciliationService.cs"));
    }

    private static QcSample Sample(SampleType type) => new()
    {
        SampleType = type,
        Status = "In Progress",
        StarchStatus = "Pending",
        PhotoStatus = "Pending",
        EmailStatus = "Not Sent",
        SampleTakenAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static PackoutRun Run(
        long id,
        string lot,
        string variety,
        int cropYear,
        decimal dumpedBins,
        decimal packout,
        decimal juice,
        decimal peeler,
        decimal waste,
        bool organic = false) => new()
        {
            Id = id,
            RunProjectionId = id,
            RunProjection = new RunProjection
            {
                Id = id,
                Name = $"Projection {id}",
                Status = RunProjectionStatuses.Ready,
                PlannedRunDate = new DateOnly(2026, 7, 20),
                CropYear = cropYear,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            Status = PackoutRunStatuses.Finalized,
            FacilitySnapshot = "WP",
            PackingDate = new DateOnly(2026, 7, 20),
            RunNumber = (int)id,
            LotNumberSnapshot = lot,
            VarietySnapshot = variety,
            IsOrganicSnapshot = organic,
            CropYearSnapshot = cropYear,
            DumpedBins = dumpedBins,
            PoundsPerBin = 920m,
            DumpedPounds = dumpedBins * 920m,
            ActualPackoutPercent = packout,
            ActualJuicePercent = juice,
            ActualPeelerSlicerPercent = peeler,
            ActualWastePercent = waste,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            FinalizedAt = DateTimeOffset.UtcNow
        };

    private static CropQcDbContext Db()
    {
        var postgres = Environment.GetEnvironmentVariable("CROPQC_TEST_PACKOUT_POSTGRES");
        if (!string.IsNullOrWhiteSpace(postgres))
        {
            ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(postgres);
            var postgresOptions = new DbContextOptionsBuilder<CropQcDbContext>();
            CropQcDatabase.Configure(postgresOptions, DatabaseProviders.PostgreSql, postgres);
            return new CropQcDbContext(postgresOptions.Options);
        }

        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CropQcDbContext(options);
    }

    private static string Read(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class RecordingEmailSender : IQcEmailSender
    {
        public List<QcEmailMessage> Messages { get; } = [];

        public Task<QcEmailSendResult> SendAsync(User sender, QcEmailMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.FromResult(QcEmailSendResult.Sent("packout-test-message"));
        }
    }
}

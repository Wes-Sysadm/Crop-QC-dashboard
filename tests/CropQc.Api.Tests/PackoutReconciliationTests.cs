using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using CropQc.Web.Services;
using CropQc.Shared.Time;
using CropQc.Shared.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using System.Text;
using System.IO.Compression;

namespace CropQc.Api.Tests;

public sealed class PackoutReconciliationTests
{
    [Fact]
    public void GrowerSummary_UsesPackTypeAndCountsDetailBoxesExactlyOnce()
    {
        var report = GrowerSummaryFixture.Text;

        Assert.True(PackoutReportParser.IsGrowerSummary(report));
        var lines = PackoutReportParser.ParseText(report);
        Assert.Equal(18, lines.Count);
        Assert.Equal(4616m, lines.Sum(x => x.Quantity));
        Assert.Equal("WP", lines[0].RawPackCode);
        Assert.Equal("9-3 POUCH BAG", lines[6].RawPackCode);
        Assert.Equal("8-3 POUCH BAG", lines[7].RawPackCode);
        Assert.Equal("WPNS", lines[8].RawPackCode);
        Assert.Equal("12-2 POUCH BAG", lines[16].RawPackCode);
        Assert.Contains("Lid Label: TARGET", lines[6].RawText);
        Assert.Contains("Size: 2 1/8", lines[6].RawText);
        Assert.All(lines, line => Assert.False(line.RequiresReview));
    }

    [Fact]
    public void GrowerSummaryDetection_RequiresStrongMarkerSet()
    {
        Assert.False(PackoutReportParser.IsGrowerSummary("Grower Summary Pack Type: WP"));
        Assert.False(PackoutReportParser.IsGrowerSummary("An arbitrary report containing Variety:"));
    }

    [Fact]
    public async Task SummaryReportByGrower_UsesExplicitQuantityColumnAndMatchesGrandTotal()
    {
        var quantities = new[] { 288, 300, 6, 173, 2390, 85, 152, 222, 246, 67, 201, 17 };
        var rows = quantities.Select((quantity, index) =>
            $"WP  BART  US1  WP  {80 + index}  BRND  SPEC  LOC  9350  1  {quantity}  {index + 1}.25%  {index + 2}.50%");
        var report = string.Join('\n', new[]
        {
            "WP PACKING, LLC",
            "Summary Report By Grower",
            "Date Type: *PACK",
            "Date Range: 7/29/2026 - 7/29/2026",
            "Stor  Var  Grd  Pack  Size  Brnd  Spec  Loc  Lot#  Run#  Quantity  Grd %  Var %"
        }.Concat(rows).Concat(new[]
        {
            "Grade US1 Total                         4147  100.00%  100.00%",
            "Grower 9350 Total                      4147  100.00%  100.00%",
            "Grand Total                            4147  100.00%  100.00%"
        }));

        Assert.True(PackoutReportParser.IsSummaryReportByGrower(report));
        var parsed = PackoutReportParser.ParseText(report);

        Assert.Equal(12, parsed.Count);
        Assert.Equal(quantities.Select(x => (decimal?)x), parsed.Select(x => x.Quantity));
        Assert.Equal(4147m, parsed.Sum(x => x.Quantity));
        Assert.All(parsed, row => Assert.Equal("WP", row.RawPackCode));
        Assert.All(parsed, row => Assert.Contains('%', row.RawText));
        Assert.DoesNotContain(parsed, row => row.RawText.Contains("Total", StringComparison.OrdinalIgnoreCase));

        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, report);
            var result = await new PackoutReportParser(new PackoutProcessingOptions(), NullLogger<PackoutReportParser>.Instance)
                .ParseAsync(new PackoutUploadFile("summary-report.txt", "text/plain", path, new FileInfo(path).Length), default);
            Assert.Equal("WP Summary Report By Grower", result.ParserName);
            Assert.Null(result.SafeDiagnostic);
            Assert.Equal(4147m, result.Lines.Sum(x => x.Quantity));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GrowerSummary_ConfiguredPackTypeDoesNotRequireArtificialReview()
    {
        var parsed = Assert.Single(PackoutReportParser.ParseText(GrowerSummarySingleLine("WP")));
        var definition = new PackCodeDefinition
        {
            Code = "WP",
            NormalizedCode = "WP",
            DisplayName = "Configured WP",
            ProductCategory = PackoutProductCategories.Packed,
            NetWeightPounds = 40m,
            IsActive = true
        };

        Assert.False(parsed.RequiresReview);
        Assert.False(PackoutReconciliationService.RequiresLineReview(
            parsed, definition, definition.ProductCategory, definition.NetWeightPounds));
    }

    [Fact]
    public void GrowerSummary_UnconfiguredPackTypeRequiresMappingReviewWithoutFabricatedWeight()
    {
        var parsed = Assert.Single(PackoutReportParser.ParseText(GrowerSummarySingleLine("UNKNOWN POUCH")));

        Assert.False(parsed.RequiresReview);
        Assert.True(PackoutReconciliationService.RequiresLineReview(
            parsed, null, PackoutProductCategories.Packed, null));
        Assert.Null(PackoutReportParser.ClassifyPackCode(parsed.RawPackCode).NetWeightPounds);
    }

    [Fact]
    public async Task GrowerSummary_ExactSuppliedPdfUsesDirectTextWhenOptedIn()
    {
        var path = Environment.GetEnvironmentVariable("CROPQC_REAL_GROWER_SUMMARY_PDF");
        if (string.IsNullOrWhiteSpace(path)) return;
        Assert.True(File.Exists(path), $"Configured Grower Summary PDF was not found: {path}");
        var before = Directory.GetDirectories(Path.GetTempPath(), "cropqc-packout-ocr-*").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parser = new PackoutReportParser(new PackoutProcessingOptions(), NullLogger<PackoutReportParser>.Instance);
        var file = new FileInfo(path);

        var result = await parser.ParseAsync(
            new PackoutUploadFile(file.Name, "application/pdf", file.FullName, file.Length),
            CancellationToken.None);

        Assert.Equal("PopplerText", result.ParserName);
        Assert.Equal(18, result.Lines.Count);
        Assert.Equal(4616m, result.Lines.Sum(x => x.Quantity));
        Assert.All(result.Lines, line => Assert.False(line.RequiresReview));
        var after = Directory.GetDirectories(Path.GetTempPath(), "cropqc-packout-ocr-*").ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Empty(after.Except(before));
    }

    private static string GrowerSummarySingleLine(string packType) => $$"""
        Grower Summary
        From Date: 7/28/2026 To Date: 7/29/2026
        Run #: 1
        Grower: 1084 - 1084
        Variety: BARTLETT
        Pack Type: {{packType}} Color:
        Lid Label Grade Pl U Size Box Percent Avg Wt. Low High
        DSG US No. 1 120 25 0.54% lbs
        End of Variety: BARTLETT Total: 25
        End of Run #: 1
        """;

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
        var physicalRunAt = DateTimeOffset.Parse("2026-07-28T05:11:00Z");
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
            Id = 16,
            Status = ActualRunStatuses.Active,
            CurrentRevisionNumber = 1,
            RunAt = physicalRunAt,
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
            Id = 28,
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
            CropYear = null,
            ReportingCropYearSnapshot = 2026,
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
            RunAtSnapshot = physicalRunAt,
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
        RunExpectationMetadata.MarkHistoricalReconstruction(
            expectation,
            now.AddDays(11),
            actualRun.RunAt,
            July27ActualRunNormalizationConstants.HistoricalReconstructionPackageIdentifier);
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
            new TestFileStorageService(),
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
        Assert.True(review.IsHistoricalReconstruction);
        Assert.Equal(2026, review.CropYear);
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
        var message = Assert.Single(emailSender.Messages);
        Assert.Contains("This benchmark was reconstructed after the physical run", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Reconstructed benchmark components", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Packout: projected", message.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Overall reconstructed benchmark score", message.HtmlBody, StringComparison.Ordinal);
        using (var workbookStream = new MemoryStream(finalized.Workbook))
        using (var archive = new ZipArchive(workbookStream, ZipArchiveMode.Read))
        using (var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open()))
        {
            var worksheet = reader.ReadToEnd();
            Assert.Contains("Historical reconstructed benchmark", worksheet, StringComparison.Ordinal);
            Assert.Contains("Overall reconstructed benchmark score", worksheet, StringComparison.Ordinal);
            Assert.Contains("Reconstructed benchmark components", worksheet, StringComparison.Ordinal);
        }
        var persistedPackout = await db.PackoutRuns.SingleAsync();
        Assert.Equal(PackoutRunStatuses.Finalized, persistedPackout.Status);
        Assert.Equal(2026, persistedPackout.CropYearSnapshot);
        Assert.Single(await db.PackoutSourceAllocations.ToListAsync());
        Assert.Equal(inventoryCountBefore, await db.RoomInventoryAdjustments.CountAsync());
    }

    [Fact]
    public async Task HistoricalActualRunWithoutExpectation_RetainsOriginalAndDoesNotFabricateExpectation()
    {
        await using var db = Db();
        var (actualRun, now) = await SeedHistoricalActualRunWithoutExpectationAsync(db);
        var storage = new TestFileStorageService();
        var service = CreateService(db, storage, new ThrowingPackoutParser(), now);
        var principal = OwnerPrincipal();
        var bytes = Encoding.UTF8.GetBytes("unparseable scanned report bytes");
        await using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "Files", "PACKOUT 07-29-2026_001.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        var upload = await service.UploadAsync(new PackoutUploadForm
        {
            ActualRunId = actualRun.Id,
            PackingDate = new DateOnly(2026, 7, 29),
            RunNumber = 1,
            DumpedBins = 155m,
            Files = [file]
        }, principal, default);

        Assert.Null(upload.Error);
        Assert.Contains("does not have a frozen Run Expectation", upload.Message);
        Assert.Empty(await db.RunExpectations.ToListAsync());
        var run = await db.PackoutRuns.Include(x => x.Sources).SingleAsync();
        Assert.Null(run.RunExpectationId);
        Assert.Equal(actualRun.Id, run.ActualRunId);
        var source = Assert.Single(run.Sources);
        Assert.Equal(PackoutReportParseStatuses.Failed, source.ParseStatus);
        Assert.Equal("Packouts/2026/WP/ActualRun-2", source.StoragePath);
        Assert.Equal(bytes.Length, source.FileSizeBytes);
        Assert.Equal(64, source.Sha256.Length);
        Assert.NotNull(source.StorageKey);
        Assert.NotNull(source.UploadedAt);
        Assert.NotNull(source.UploadedByUserId);
        Assert.Single(storage.SaveRequests);
        Assert.Empty(storage.DeletedKeys);

        db.ChangeTracker.Clear();
        var reloadedService = CreateService(db, storage, new ThrowingPackoutParser(), now.AddMinutes(1));
        var review = await reloadedService.GetAsync(run.Id, principal, default);
        Assert.NotNull(review);
        Assert.False(review.ReconciliationAvailable);
        Assert.True(Assert.Single(review.Sources).CanOpen);
        var opened = await reloadedService.OpenSourceAsync(run.Id, source.Id, principal, default);
        Assert.NotNull(opened.Content);
        await using var openedContent = opened.Content;
        using var copy = new MemoryStream();
        await openedContent.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());

        var duplicate = await reloadedService.UploadAsync(new PackoutUploadForm
        {
            ActualRunId = actualRun.Id,
            PackingDate = new DateOnly(2026, 7, 29),
            RunNumber = 1,
            DumpedBins = 155m,
            Files = [file]
        }, principal, default);
        Assert.NotNull(duplicate.Error);
        Assert.Single(storage.SaveRequests);
        var delete = await reloadedService.DeletePendingAsync(run.Id, run.ConcurrencyVersion, principal, default);
        Assert.Contains("permanent records", delete.Error);
        Assert.Empty(storage.DeletedKeys);
        Assert.Single(await db.PackoutRuns.ToListAsync());
    }

    [Fact]
    public async Task PackoutStorageFailure_CreatesNoPackoutMetadata()
    {
        await using var db = Db();
        var (actualRun, now) = await SeedHistoricalActualRunWithoutExpectationAsync(db);
        var storage = new TestFileStorageService(failSave: true);
        var service = CreateService(db, storage, new ThrowingPackoutParser(), now);
        var bytes = Encoding.UTF8.GetBytes("report");
        await using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "Files", "packout.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        var upload = await service.UploadAsync(new PackoutUploadForm
        {
            ActualRunId = actualRun.Id,
            PackingDate = new DateOnly(2026, 7, 29),
            RunNumber = 1,
            DumpedBins = 155m,
            Files = [file]
        }, OwnerPrincipal(), default);

        Assert.Contains("could not be saved to permanent storage", upload.Error);
        Assert.Empty(await db.PackoutRuns.ToListAsync());
        Assert.Empty(await db.PackoutReportSources.ToListAsync());
        Assert.Empty(storage.SaveRequests);
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
            ParseStatus = PackoutReportParseStatuses.Legacy,
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
    public void PackoutDocumentMigrationAndCompatibilityPackageAreAdditiveAndProviderSafe()
    {
        var migration = Read("src", "CropQc.Data", "Migrations", "20260824233548_AddPackoutDocumentStorageMetadata.cs");
        var preflight = Read("scripts", "postgresql", "preflight-packout-document-storage.sql");
        var apply = Read("scripts", "postgresql", "apply-packout-document-storage-schema.sql");
        var verify = Read("scripts", "postgresql", "verify-packout-document-storage.sql");
        var harness = Read("scripts", "test-packout-document-storage-production-schema.ps1");

        Assert.Contains("MigrationProviderTypes.StoreType", migration);
        Assert.Contains("AddColumn", migration);
        Assert.DoesNotContain("DropTable", migration);
        Assert.Contains("state_a_absent", preflight);
        Assert.Contains("state_b_complete_exact", preflight);
        Assert.Contains("State C", preflight);
        Assert.Contains("Legacy metadata only", apply);
        Assert.Contains("ON DELETE SET NULL", apply);
        Assert.Contains("packout_document_storage_schema_verified", verify);
        Assert.DoesNotContain("__EFMigrationsHistory", apply);
        Assert.Contains("postgres:18", harness);
        Assert.Contains("698-object gate", harness);
        Assert.Contains("Migration history unchanged", harness);
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
        Assert.Contains("Original uploads are retained permanently", review);
        Assert.Contains("Supporting Packout Documents", review);
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

    private static async Task<(ActualRun ActualRun, DateTimeOffset Now)> SeedHistoricalActualRunWithoutExpectationAsync(CropQcDbContext db)
    {
        var now = DateTimeOffset.Parse("2026-08-24T20:00:00Z");
        var user = new User
        {
            Email = ApplicationAreas.OwnerEmail,
            DisplayName = "Historical Packout Owner",
            IsActive = true,
            CreatedAt = now
        };
        var warehouse = new Warehouse { Id = 8100, Code = "WP", Name = "WP", IsActive = true };
        var room = new Room
        {
            Id = 8101,
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            Code = "WP-1",
            Name = "WP-1",
            CropQcRoomName = "WP-1",
            IsActive = true
        };
        var actualRun = new ActualRun
        {
            Id = 2,
            Status = ActualRunStatuses.Active,
            CurrentRevisionNumber = 1,
            RunAt = DateTimeOffset.Parse("2026-07-30T00:33:00Z"),
            RunFacilityCodeSnapshot = "WP",
            CreatedAt = now
        };
        var revision = new ActualRunRevision
        {
            Id = 8102,
            ActualRunId = actualRun.Id,
            ActualRun = actualRun,
            RevisionNumber = 1,
            OperationType = ActualRunRevisionTypes.Create,
            OperationKey = "historical-packout-create",
            IsCurrent = true,
            CreatedAt = now
        };
        var adjustment = new RoomInventoryAdjustment
        {
            Id = 8103,
            ActualRunId = actualRun.Id,
            ActualRun = actualRun,
            ActualRunRevisionId = revision.Id,
            ActualRunRevision = revision,
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            RoomId = room.Id,
            Room = room,
            CropYear = 2026,
            GrowerName = "Historical Grower",
            LotNumber = "9350",
            VarietyCode = "Bartlett",
            OldBinCount = 155,
            ChangeAmount = -155,
            NewBinCount = 0,
            AdjustmentType = BinsRunService.AdjustmentType,
            Source = "Disposable historical Packout test",
            AdjustmentAt = actualRun.RunAt,
            CreatedAt = now,
            InventoryInvariantVersion = 1,
            InventoryOperationKey = "historical-packout-depletion"
        };
        var binsRun = new BinsRunEntry
        {
            Id = 8104,
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
            ReportingCropYearSnapshot = 2026,
            ReportingFacilityCodeSnapshot = "WP",
            GrowerName = "Historical Grower",
            LotNumber = "9350",
            VarietyCode = "Bartlett",
            PreviousAvailableBins = 155,
            BinsRun = 155,
            NewAvailableBins = 0,
            RunAt = actualRun.RunAt,
            CreatedAt = now,
            TransactionType = ActualRunTransactionTypes.Depletion
        };
        db.AddRange(user, warehouse, room, actualRun, revision, adjustment, binsRun);
        await db.SaveChangesAsync();
        return (actualRun, now);
    }

    private static PackoutReconciliationService CreateService(
        CropQcDbContext db,
        IFileStorageService storage,
        IPackoutReportParser parser,
        DateTimeOffset now)
    {
        var options = new PackoutProcessingOptions();
        var configuration = new ConfigurationBuilder().Build();
        return new PackoutReconciliationService(
            db,
            parser,
            new PackoutFeedbackWorkbookService(options, NullLogger<PackoutFeedbackWorkbookService>.Instance),
            new RecordingEmailSender(),
            new UserAccessService(db, configuration),
            new PacificBusinessTimeService(new FixedClock(now)),
            configuration,
            options,
            new PackoutOperationCoordinator(),
            storage,
            NullLogger<PackoutReconciliationService>.Instance);
    }

    private static ClaimsPrincipal OwnerPrincipal() => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "Test"));

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

    private sealed class ThrowingPackoutParser : IPackoutReportParser
    {
        public Task<PackoutParseResult> ParseAsync(PackoutUploadFile file, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic OCR failure.");
    }

    private sealed class TestFileStorageService(bool failSave = false) : IFileStorageService
    {
        private readonly Dictionary<string, byte[]> files = [];
        public List<FileStorageSaveRequest> SaveRequests { get; } = [];
        public List<string> DeletedKeys { get; } = [];

        public string GenerateTargetPath(FileStorageTargetContext context) => "packouts";

        public async Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default)
        {
            if (failSave) throw new IOException("Synthetic permanent storage failure.");
            var key = $"{request.TargetPath}/{request.FileName}";
            await using var buffer = new MemoryStream();
            await request.Content.CopyToAsync(buffer, cancellationToken);
            files[key] = buffer.ToArray();
            SaveRequests.Add(request);
            return new FileStorageReference(
                FileStorageProviders.Local,
                key,
                request.TargetPath,
                request.FileName,
                request.ContentType,
                files[key].LongLength,
                FileId: key);
        }

        public Task<FileStorageReference?> GetMetadataAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<FileStorageReference?>(null);

        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(files.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes) : null);

        public Task DeleteOrVoidAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(storageKey);
            files.Remove(storageKey);
            return Task.CompletedTask;
        }
    }
}

using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Web.Auth;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;

namespace CropQc.Api.Tests;

public sealed class DashboardSummaryPerformanceDatasetTests
{
    [Fact]
    public async Task RepresentativeDashboardDataset_ReturnsOccupiedRoomsWithCompactCardFields()
    {
        await using var db = CreateDbContext();
        await SeedRepresentativeDashboardDatasetAsync(db);
        var service = CreateService(db);

        var dashboard = await service.GetHomeDashboardAsync(new RoomSummaryFilterForm(), CancellationToken.None);

        Assert.Null(dashboard.DataWarning);
        Assert.Equal(40, dashboard.RoomSummaries.Count);
        Assert.All(dashboard.RoomSummaries, room =>
        {
            Assert.True(room.CurrentBinsCount > 0);
            Assert.True(room.RoomCapacityBins > 0);
            Assert.NotEmpty(room.VarietyColorSegments);
            Assert.False(string.IsNullOrWhiteSpace(room.RankingReason));
            Assert.NotNull(room.PercentFull);
        });
        Assert.Contains(dashboard.RoomSummaries, x => x.VarietyColorSegments.Count > 1);
        Assert.Contains(dashboard.RoomSummaries, x => x.OrganicBins > 0);
        Assert.Contains(dashboard.RoomSummaries, x => x.ReceivingPressureRepresentedBins > 0);
        Assert.Contains(dashboard.RoomSummaries, x => x.LatestPressureRepresentedBins > 0);
        Assert.Contains(dashboard.RoomSummaries, x => x.PressureReadingCount >= 2);
        Assert.Equal(40 * 3, dashboard.TodaySamples.Count);
        Assert.DoesNotContain(dashboard.RoomSummaries, x => x.RoomCode.Contains("EMPTY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DashboardOptimization_DocumentsRepresentativeDatasetAndBaselineLimitations()
    {
        var docs = File.ReadAllText(FindRepositoryFile("docs", "performance-baseline.md"));

        Assert.Contains("30 to 50 occupied rooms", docs);
        Assert.Contains("cold", docs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("median warm", docs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dashboard initial load", docs);
    }

    private static CropQcDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CropQcDbContext(options);
    }

    private static async Task SeedRepresentativeDashboardDatasetAsync(CropQcDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var warehouses = Enumerable.Range(1, 3)
            .Select(i => new Warehouse { Id = i, Code = i == 1 ? "EBS" : i == 2 ? "WP" : "MCD", Name = i == 1 ? "Earl Brown Storage" : i == 2 ? "WP Packing" : "McDougall" })
            .ToList();
        db.Warehouses.AddRange(warehouses);

        var profiles = new[]
        {
            new FruitProfile { Id = 1, Name = "Gala", VarietyCode = "GALA", FruitType = "Apple", ProductionType = "Conventional" },
            new FruitProfile { Id = 2, Name = "Organic Gala", VarietyCode = "GALA", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true },
            new FruitProfile { Id = 3, Name = "Fuji", VarietyCode = "FUJI", FruitType = "Apple", ProductionType = "Conventional" },
            new FruitProfile { Id = 4, Name = "Granny Smith", VarietyCode = "GSMT", FruitType = "Apple", ProductionType = "Conventional" }
        };
        db.FruitProfiles.AddRange(profiles);

        var receiving = new SampleType { Id = 1, Name = "Receiving Sample" };
        var door = new SampleType { Id = 2, Name = "Door Sample" };
        var lot = new SampleType { Id = 3, Name = "Lot Sample" };
        db.SampleTypes.AddRange(receiving, door, lot);
        var grade = new Grade { Id = 1, Code = "US1", Name = "US 1" };
        db.Grades.Add(grade);
        var defect = new DefectType { Id = 1, Name = "Rot" };
        db.DefectTypes.Add(defect);
        var starchScale = new StarchScale { Id = 1, Name = "Default", FruitType = "Apple" };
        var starch = new StarchScaleValue { Id = 1, StarchScaleId = 1, StarchScale = starchScale, Value = 3m, SortOrder = 1 };
        db.StarchScales.Add(starchScale);
        db.StarchScaleValues.Add(starch);

        var receiptId = 1L;
        var sampleId = 1L;
        var readingId = 1L;
        var photoId = 1L;
        var defectId = 1L;
        for (var roomIndex = 1; roomIndex <= 46; roomIndex++)
        {
            var warehouse = warehouses[(roomIndex - 1) % warehouses.Count];
            var room = new Room
            {
                Id = roomIndex,
                WarehouseId = warehouse.Id,
                Warehouse = warehouse,
                Code = roomIndex <= 40 ? $"ROOM-{roomIndex:00}" : $"EMPTY-{roomIndex:00}",
                Name = roomIndex <= 40 ? $"Room {roomIndex:00}" : $"Empty Room {roomIndex:00}",
                DisplayName = roomIndex <= 40 ? $"Room {roomIndex:00}" : $"Empty Room {roomIndex:00}",
                CapacityBins = 2400,
                SubLocation = warehouse.Code == "EBS" ? (roomIndex % 2 == 0 ? "Evans" : "BM") : null,
                IsActive = true
            };
            db.Rooms.Add(room);

            if (roomIndex > 40)
            {
                continue;
            }

            for (var lotIndex = 1; lotIndex <= 3; lotIndex++)
            {
                var profile = roomIndex % 6 == 0
                    ? profiles[1]
                    : profiles[(roomIndex + lotIndex) % profiles.Length];
                var bins = 90 + lotIndex * 20 + roomIndex;
                var receipt = new Receipt
                {
                    Id = receiptId++,
                    CropYear = 2026,
                    ReceivedAt = now.AddDays(-35 + lotIndex),
                    CompuTechReceiptId = $"PERF-{roomIndex:00}-{lotIndex}",
                    ReceiptType = "Truck receipt",
                    WarehouseId = warehouse.Id,
                    Warehouse = warehouse,
                    RoomId = room.Id,
                    Room = room,
                    FruitProfileId = profile.Id,
                    FruitProfile = profile,
                    GrowerName = lotIndex == 1 ? "Vantage Orchard" : $"Grower {lotIndex}",
                    GrowerNumber = $"G{lotIndex:000}",
                    LotCode = $"L{roomIndex:00}{lotIndex}",
                    BinCount = bins,
                    CreatedAt = now.AddDays(-40),
                    UpdatedAt = now.AddDays(-40)
                };
                db.Receipts.Add(receipt);

                if (roomIndex % 10 == 0 && lotIndex == 3)
                {
                    db.RoomDepletions.Add(new RoomDepletion
                    {
                        Id = receipt.Id,
                        ReceiptId = receipt.Id,
                        Receipt = receipt,
                        WarehouseId = warehouse.Id,
                        Warehouse = warehouse,
                        RoomId = room.Id,
                        Room = room,
                        FruitProfileId = profile.Id,
                        FruitProfile = profile,
                        GrowerName = receipt.GrowerName,
                        LotCode = receipt.LotCode,
                        BinCountDepleted = 10,
                        DepletedAt = now.AddDays(-2),
                        CreatedAt = now.AddDays(-2)
                    });
                }

                foreach (var sampleType in new[] { receiving, door, lot })
                {
                    var sample = new QcSample
                    {
                        Id = sampleId++,
                        ReceiptId = receipt.Id,
                        Receipt = receipt,
                        SampleTypeId = sampleType.Id,
                        SampleType = sampleType,
                        Status = "Complete",
                        StarchStatus = "Starch Complete",
                        PhotoStatus = "Photos Complete",
                        EmailStatus = sampleType == receiving && roomIndex % 7 == 0 ? "Sent" : "Pending",
                        SampleTakenAt = now.AddHours(-(roomIndex % 12)).AddDays(sampleType == receiving ? 0 : sampleType == door ? -14 : -29),
                        CreatedAt = now.AddDays(-30),
                        ActualSampleSize = lotIndex == 1 ? 10 : lotIndex == 2 ? 25 : 50
                    };
                    db.QcSamples.Add(sample);

                    db.QcPhotos.AddRange(
                        Photo(photoId++, receipt.Id, null, "BinTruck", now),
                        Photo(photoId++, null, sample.Id, "SampleBeforeCutting", now),
                        Photo(photoId++, null, sample.Id, "CutFruit", now),
                        Photo(photoId++, null, sample.Id, "FruitAfterStarch", now));

                    for (var row = 1; row <= sample.ActualSampleSize; row++)
                    {
                        var reading = new QcFruitReading
                        {
                            Id = readingId++,
                            QcSampleId = sample.Id,
                            QcSample = sample,
                            RowNumber = row,
                            Pressure1Lbs = 14m - lotIndex + row % 3,
                            Pressure2Lbs = 13.5m - lotIndex + row % 3,
                            WeightGrams = 180 + row,
                            GradeId = grade.Id,
                            Grade = grade,
                            StarchScaleValueId = starch.Id,
                            StarchScaleValue = starch,
                            SizeCategory = row % 2 == 0 ? 72 : 80,
                            SizeStatus = "Measured",
                            IsCompleted = true,
                            CreatedAt = now.AddDays(-30)
                        };
                        db.QcFruitReadings.Add(reading);
                        if (row == 1 && roomIndex % 5 == 0)
                        {
                            db.QcFruitDefects.Add(new QcFruitDefect { Id = defectId++, QcFruitReadingId = reading.Id, QcFruitReading = reading, DefectTypeId = defect.Id, DefectType = defect });
                        }
                    }
                }
            }
        }

        await db.SaveChangesAsync();
    }

    private static QcPhoto Photo(long id, long? receiptId, long? sampleId, string photoType, DateTimeOffset now) => new()
    {
        Id = id,
        ReceiptId = receiptId,
        QcSampleId = sampleId,
        PhotoType = photoType,
        PhotoSource = "Test metadata",
        FileName = $"{photoType}-{id}.jpg",
        ContentType = "image/jpeg",
        StorageProvider = "Local",
        SharePointDriveId = "",
        SharePointItemId = "",
        CapturedAt = now
    };

    private static DashboardDataService CreateService(CropQcDbContext db)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Email, "admin@fruitandland.com"),
                    new Claim(ClaimTypes.Role, "Admin")
                ],
                "TestAuth"))
        };
        var configuration = new ConfigurationBuilder().Build();
        return new DashboardDataService(
            db,
            new FakeFileStorageService(),
            new FileStorageOptions(),
            new EmailOptions { Provider = EmailProviders.GmailUser, QcDefaultRecipients = "qc-recipient@fruitandland.com" },
            new FakeRecipientResolver(),
            new GoogleAuthenticationOptions { AllowedDomains = new HashSet<string>(["fruitandland.com"], StringComparer.OrdinalIgnoreCase) },
            new FakeCredentialStore(),
            new FakeEmailSender(),
            new QcPhotoRequirementPolicy(),
            new StableEmailComposer(),
            new CropYearService(db, configuration),
            new HttpContextAccessor { HttpContext = httpContext },
            configuration,
            NullLogger<DashboardDataService>.Instance);
    }

    private sealed class FakeFileStorageService : IFileStorageService
    {
        public string GenerateTargetPath(FileStorageTargetContext context) => "test";
        public Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FileStorageReference?> GetMetadataAsync(string storageKey, CancellationToken cancellationToken = default) => Task.FromResult<FileStorageReference?>(null);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteOrVoidAsync(string storageKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeRecipientResolver : IQcEmailRecipientResolver
    {
        public Task<QcEmailRecipientResolution> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new QcEmailRecipientResolution(["qc-recipient@fruitandland.com"], "configured"));
    }

    private sealed class FakeCredentialStore : IGoogleCredentialStore
    {
        public Task SaveFromAuthenticationPropertiesAsync(User user, Microsoft.AspNetCore.Authentication.AuthenticationProperties properties, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<GoogleAccessTokenResult> GetAccessTokenAsync(User user, CancellationToken cancellationToken) => Task.FromResult(GoogleAccessTokenResult.Success("token"));
        public Task<GoogleCredentialDiagnostic> GetDiagnosticAsync(User user, CancellationToken cancellationToken) => Task.FromResult(new GoogleCredentialDiagnostic(true, true));
    }

    private sealed class FakeEmailSender : IQcEmailSender
    {
        public Task<QcEmailSendResult> SendAsync(User sender, QcEmailMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(QcEmailSendResult.Sent("message-id"));
    }

    private sealed class StableEmailComposer : IQcSummaryEmailComposer
    {
        public Task<QcEmailContent> ComposeAsync(QcSample sample, ReadinessViewModel readiness, User? sendingUser, bool isOverride, string? overrideReason, CancellationToken cancellationToken) =>
            Task.FromResult(new QcEmailContent("QC Summary", "<p>Html</p>", "Text", []));
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName ?? "";
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, pathParts));
    }
}

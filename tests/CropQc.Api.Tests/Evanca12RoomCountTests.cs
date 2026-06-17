using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Web.Auth;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;

namespace CropQc.Api.Tests;

public sealed class Evanca12RoomCountTests
{
    [Fact]
    public async Task Evanca12_CurrentBalanceCorrectionExcludesLegacyReceiptsSamplesAndDuplicates()
    {
        await using var db = CreateDbContext();
        await SeedEvanca12Async(db);
        var service = CreateService(db);

        var detail = await service.GetRoomDetailAsync(12, CancellationToken.None);
        var breakdown = await service.GetRoomCountBreakdownAsync(12, CancellationToken.None);

        Assert.NotNull(detail.Summary);
        Assert.Equal(1469, detail.Summary!.CurrentBinsCount);
        Assert.Equal("FUJI: 1469 bins", detail.Summary.VarietyStatusSummary);
        Assert.Single(detail.CurrentLots);
        Assert.Equal(1469, detail.CurrentLots[0].CurrentBins);
        Assert.Equal(1469, breakdown.IncludedBins);
        Assert.Contains(breakdown.Rows, x => x.SourceType == RoomInventoryImportService.StartingInventoryAdjustmentType && x.IsIncluded && x.Bins == 1469 && x.Variety == "FUJI");
        Assert.Contains(breakdown.Rows, x => x.SourceType == "Receipt" && x.SampleType.Contains("Truck Sample", StringComparison.OrdinalIgnoreCase) && !x.IsIncluded && x.DecisionReason.Contains("superseded", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(breakdown.Rows, x => x.SourceType == "Receipt" && x.SampleType.Contains("Door Sample", StringComparison.OrdinalIgnoreCase) && !x.IsIncluded);
        Assert.Contains(breakdown.Rows, x => x.SourceType == "Receipt" && x.SampleType.Contains("Lot Sample", StringComparison.OrdinalIgnoreCase) && !x.IsIncluded);
    }

    [Fact]
    public void RoomCountBreakdown_IsRoutedAndShowsRequiredDebugColumns()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "HomeController.cs"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "RoomCountBreakdown.cshtml"));
        var room = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml"));

        Assert.Contains("[HttpGet(\"/Rooms/{roomId:int}/CountBreakdown\")]", controller);
        Assert.Contains("GetRoomCountBreakdownAsync", service);
        Assert.Contains("BuildCurrentBalanceCorrectionCutoffsAsync", service);
        Assert.Contains("IsSupersededByRoomCurrentBalanceCorrection", service);
        Assert.Contains("ReceiptDedupeKey", service);
        Assert.Contains("Source Type", view);
        Assert.Contains("Receipt ID", view);
        Assert.Contains("Sample Type", view);
        Assert.Contains("Included / Excluded", view);
        Assert.Contains("DecisionReason", view);
        Assert.Contains("View Count Breakdown", room);
    }

    private static CropQcDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CropQcDbContext(options);
        db.Database.EnsureCreated();
        db.Users.Add(new User
        {
            Id = 9001,
            Email = "wes@fruitandland.com",
            DisplayName = "Wes",
            Domain = "fruitandland.com",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        return db;
    }

    private static async Task SeedEvanca12Async(CropQcDbContext db)
    {
        var warehouse = new Warehouse { Id = 901, Code = "EBS", Name = "Earl Brown and Sons" };
        var room = new Room
        {
            Id = 12,
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            Code = "EVANCA12",
            Name = "Evans 12",
            CropQcRoomName = "Evans-12",
            CompuTechRoomCode = "EVANCA12",
            DisplayName = "Evans-12",
            SubLocation = "Evans"
        };
        var fuji = new FruitProfile { Id = 901, Name = "Fuji", VarietyCode = "FUJI", FruitType = "Apple", ProductionType = "Conventional" };
        var truckSample = new SampleType { Id = 901, Name = "Truck Sample", IsActive = true };
        var doorSample = new SampleType { Id = 902, Name = "Door Sample", IsActive = true };
        var lotSample = new SampleType { Id = 903, Name = "Lot Sample", IsActive = true };
        db.Warehouses.Add(warehouse);
        db.Rooms.Add(room);
        db.FruitProfiles.Add(fuji);
        db.SampleTypes.AddRange(truckSample, doorSample, lotSample);

        var beforeCorrection = DateTimeOffset.Parse("2026-06-14T08:00:00-07:00");
        db.Receipts.AddRange(
            Receipt(100, "EVANCA12-OLD", "Truck receipt", 1103, beforeCorrection, warehouse, room, fuji),
            Receipt(101, "EVANCA12-OLD", "Truck receipt", 1103, beforeCorrection.AddMinutes(5), warehouse, room, fuji),
            Receipt(102, "EVANCA12-DOOR", "Door sample", 500, beforeCorrection.AddMinutes(10), warehouse, room, fuji),
            Receipt(103, "EVANCA12-LOT", "Lot sample", 600, beforeCorrection.AddMinutes(15), warehouse, room, fuji));
        db.QcSamples.AddRange(
            Sample(200, 100, truckSample),
            Sample(201, 102, doorSample),
            Sample(202, 103, lotSample));
        db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
        {
            Id = 300,
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            RoomId = room.Id,
            Room = room,
            FruitProfileId = fuji.Id,
            FruitProfile = fuji,
            GrowerName = "Wes Corrected Current Inventory",
            LotNumber = "EVANCA12",
            VarietyCode = "FUJI",
            OldBinCount = null,
            ChangeAmount = 1469,
            NewBinCount = 1469,
            AdjustmentType = RoomInventoryImportService.StartingInventoryAdjustmentType,
            Source = "Wes Corrected Current Inventory 2026-06-15",
            Reason = "Wes Corrected Current Inventory 2026-06-15",
            AdjustmentAt = DateTimeOffset.Parse("2026-06-15T17:00:00-07:00"),
            CreatedByUserId = 9001,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static Receipt Receipt(long id, string compuTechId, string receiptType, int bins, DateTimeOffset receivedAt, Warehouse warehouse, Room room, FruitProfile fruitProfile) => new()
    {
        Id = id,
        CropYear = 2026,
        ReceivedAt = receivedAt,
        CompuTechReceiptId = compuTechId,
        ReceiptType = receiptType,
        WarehouseId = warehouse.Id,
        Warehouse = warehouse,
        RoomId = room.Id,
        Room = room,
        FruitProfileId = fruitProfile.Id,
        FruitProfile = fruitProfile,
        GrowerName = "Fuji Grower",
        GrowerNumber = "EVANCA12",
        LotCode = "EVANCA12",
        BinCount = bins,
        CreatedAt = receivedAt,
        UpdatedAt = receivedAt
    };

    private static QcSample Sample(long id, long receiptId, SampleType sampleType) => new()
    {
        Id = id,
        ReceiptId = receiptId,
        SampleTypeId = sampleType.Id,
        SampleType = sampleType,
        Status = "Complete",
        StarchStatus = "Not Required",
        PhotoStatus = "Photos Complete",
        EmailStatus = "Not Sent",
        ActualSampleSize = 10,
        SampleTakenAt = DateTimeOffset.Parse("2026-06-14T10:00:00-07:00"),
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static DashboardDataService CreateService(CropQcDbContext db)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Email, "wes@fruitandland.com"),
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

    private sealed class StableEmailComposer : IQcSummaryEmailComposer
    {
        public Task<QcEmailContent> ComposeAsync(QcSample sample, ReadinessViewModel readiness, User? sendingUser, bool isOverride, string? overrideReason, CancellationToken cancellationToken) =>
            Task.FromResult(new QcEmailContent("QC Summary", "<p>Html</p>", "Text", []));
    }

    private sealed class FakeEmailSender : IQcEmailSender
    {
        public Task<QcEmailSendResult> SendAsync(User sender, QcEmailMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(new QcEmailSendResult(true, "message-id", null));
    }

    private sealed class FakeRecipientResolver : IQcEmailRecipientResolver
    {
        public Task<QcEmailRecipientResolution> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new QcEmailRecipientResolution(["qc-recipient@fruitandland.com"], QcEmailRecipientSources.FallbackConfiguration));
    }

    private sealed class FakeCredentialStore : IGoogleCredentialStore
    {
        public Task SaveFromAuthenticationPropertiesAsync(User user, AuthenticationProperties properties, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<GoogleAccessTokenResult> GetAccessTokenAsync(User user, CancellationToken cancellationToken) => Task.FromResult(GoogleAccessTokenResult.Success("token"));
        public Task<GoogleCredentialDiagnostic> GetDiagnosticAsync(User user, CancellationToken cancellationToken) => Task.FromResult(new GoogleCredentialDiagnostic(true, true));
    }

    private sealed class FakeFileStorageService : IFileStorageService
    {
        public string GenerateTargetPath(FileStorageTargetContext context) => "test/path";
        public Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileStorageReference(FileStorageProviders.Local, "test-key", request.TargetPath, request.FileName, request.ContentType, request.FileSizeBytes ?? 0));
        public Task<FileStorageReference?> GetMetadataAsync(string storageKey, CancellationToken cancellationToken = default) => Task.FromResult<FileStorageReference?>(null);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteOrVoidAsync(string storageKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
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

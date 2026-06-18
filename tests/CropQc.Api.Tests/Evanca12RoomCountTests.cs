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
    public async Task VerifiedEbsCurrentBalanceCorrectionsSetRoomTotalsAndExcludeSamplesDuplicates()
    {
        await using var db = CreateDbContext();
        await SeedVerifiedEbsInventoryAsync(db);
        var service = CreateService(db);

        var detail = await service.GetRoomDetailAsync(12, CancellationToken.None);
        var breakdown = await service.GetRoomCountBreakdownAsync(12, CancellationToken.None);

        await AssertRoomTotalAsync(service, 1, 1201);
        await AssertRoomTotalAsync(service, 12, 1022);
        await AssertRoomTotalAsync(service, 17, 1918);
        await AssertRoomTotalAsync(service, 101, 1178);
        await AssertRoomTotalAsync(service, 106, 514);
        await AssertRoomTotalAsync(service, 104, 0);
        Assert.NotNull(detail.Summary);
        Assert.Equal(1022, detail.Summary!.CurrentBinsCount);
        Assert.Equal("FUJI: 1022 bins", detail.Summary.VarietyStatusSummary);
        Assert.Equal(3, detail.CurrentLots.Count);
        Assert.Equal(1022, breakdown.IncludedBins);
        Assert.Contains(breakdown.Rows, x => x.SourceType == RoomInventoryImportService.StartingInventoryAdjustmentType && x.IsIncluded && x.Lot == "1570" && x.Bins == 819 && x.Variety == "FUJI");
        Assert.Contains(breakdown.Rows, x => x.SourceType == RoomInventoryImportService.StartingInventoryAdjustmentType && !x.IsIncluded && x.Bins == 1469 && x.DecisionReason.Contains("superseded", StringComparison.OrdinalIgnoreCase));
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

    private static async Task AssertRoomTotalAsync(DashboardDataService service, int roomId, int expectedBins)
    {
        var detail = await service.GetRoomDetailAsync(roomId, CancellationToken.None);
        Assert.NotNull(detail.Summary);
        Assert.Equal(expectedBins == 0 ? null : expectedBins, detail.Summary!.CurrentBinsCount);
    }

    private static async Task SeedVerifiedEbsInventoryAsync(CropQcDbContext db)
    {
        var warehouse = new Warehouse { Id = 901, Code = "EBS", Name = "Earl Brown and Sons" };
        var rooms = new[]
        {
            Room(1, "EVANCA01", "Evans 1", "Evans-01", "EVANCA01", "Evans", warehouse),
            Room(12, "EVANCA12", "Evans 12", "Evans-12", "EVANCA12", "Evans", warehouse),
            Room(17, "LAMBCA17", "Lamb 17", "Lamb-17", "LAMBCA17", "Lamb", warehouse),
            Room(101, "BLUECA01", "Blue Mountain 1", "BM-1", "BLUECA01", "BM", warehouse),
            Room(104, "BLUECA04", "Blue Mountain 4", "BM-4", "BLUECA04", "BM", warehouse),
            Room(106, "BLUECA06", "Blue Mountain 6", "BM-6", "BLUECA06", "BM", warehouse)
        };
        var roomByCode = rooms.ToDictionary(x => x.Code);
        var red = new FruitProfile { Id = 901, Name = "Red Delicious", VarietyCode = "RED", FruitType = "Apple", ProductionType = "Conventional" };
        var fuji = new FruitProfile { Id = 902, Name = "Fuji", VarietyCode = "FUJI", FruitType = "Apple", ProductionType = "Conventional" };
        var pink = new FruitProfile { Id = 903, Name = "Pink Lady", VarietyCode = "PINK", FruitType = "Apple", ProductionType = "Conventional" };
        var gsmt = new FruitProfile { Id = 904, Name = "GSMT", VarietyCode = "GSMT", FruitType = "Apple", ProductionType = "Conventional" };
        var truckSample = new SampleType { Id = 901, Name = "Truck Sample", IsActive = true };
        var doorSample = new SampleType { Id = 902, Name = "Door Sample", IsActive = true };
        var lotSample = new SampleType { Id = 903, Name = "Lot Sample", IsActive = true };
        db.Warehouses.Add(warehouse);
        db.Rooms.AddRange(rooms);
        db.FruitProfiles.AddRange(red, fuji, pink, gsmt);
        db.SampleTypes.AddRange(truckSample, doorSample, lotSample);

        var beforeCorrection = DateTimeOffset.Parse("2026-06-14T08:00:00-07:00");
        db.Receipts.AddRange(
            Receipt(100, "EVANCA12-OLD", "Truck receipt", 1103, beforeCorrection, warehouse, roomByCode["EVANCA12"], fuji),
            Receipt(101, "EVANCA12-OLD", "Truck receipt", 1103, beforeCorrection.AddMinutes(5), warehouse, roomByCode["EVANCA12"], fuji),
            Receipt(102, "EVANCA12-DOOR", "Door sample", 500, beforeCorrection.AddMinutes(10), warehouse, roomByCode["EVANCA12"], fuji),
            Receipt(103, "EVANCA12-LOT", "Lot sample", 600, beforeCorrection.AddMinutes(15), warehouse, roomByCode["EVANCA12"], fuji));
        db.QcSamples.AddRange(
            Sample(200, 100, truckSample),
            Sample(201, 102, doorSample),
            Sample(202, 103, lotSample));
        var oldAt = DateTimeOffset.Parse("2026-06-15T17:00:00-07:00");
        db.RoomInventoryAdjustments.AddRange(
            CurrentCorrection(300, warehouse, roomByCode["EVANCA12"], fuji, "Sealed", 1469, oldAt, "Wes Corrected Current Inventory 2026-06-15"),
            CurrentCorrection(301, warehouse, roomByCode["EVANCA01"], red, "Sealed", 1462, oldAt, "Wes Corrected Current Inventory 2026-06-15"),
            CurrentCorrection(302, warehouse, roomByCode["BLUECA04"], red, "Current", 186, oldAt, "Wes Corrected Current Inventory 2026-06-15"));
        var verifiedAt = DateTimeOffset.Parse("2026-06-17T17:00:00-07:00");
        db.RoomInventoryAdjustments.AddRange(
            CurrentCorrection(400, warehouse, roomByCode["EVANCA01"], red, "9285", 48, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(401, warehouse, roomByCode["EVANCA01"], red, "9490", 13, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(402, warehouse, roomByCode["EVANCA01"], red, "9570", 101, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(403, warehouse, roomByCode["EVANCA01"], red, "9660", 1039, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(404, warehouse, roomByCode["EVANCA12"], fuji, "1560", 118, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(405, warehouse, roomByCode["EVANCA12"], fuji, "1570", 819, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(406, warehouse, roomByCode["EVANCA12"], fuji, "1030", 85, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(407, warehouse, roomByCode["LAMBCA17"], pink, "1020", 559, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(408, warehouse, roomByCode["LAMBCA17"], pink, "1050", 1359, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(409, warehouse, roomByCode["BLUECA01"], red, "9510", 264, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(410, warehouse, roomByCode["BLUECA01"], red, "9550", 306, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(411, warehouse, roomByCode["BLUECA01"], red, "9560", 608, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(412, warehouse, roomByCode["BLUECA04"], red, "Current", 0, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(413, warehouse, roomByCode["BLUECA06"], gsmt, "1290", 281, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(414, warehouse, roomByCode["BLUECA06"], gsmt, "1560", 183, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(415, warehouse, roomByCode["BLUECA06"], gsmt, "3200", 3, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(416, warehouse, roomByCode["BLUECA06"], gsmt, "9450", 26, verifiedAt, "Wes Verified Current Inventory 2026-06-17"),
            CurrentCorrection(417, warehouse, roomByCode["BLUECA06"], gsmt, "9750", 21, verifiedAt, "Wes Verified Current Inventory 2026-06-17"));
        await db.SaveChangesAsync();
    }

    private static Room Room(int id, string code, string name, string cropQcRoomName, string compuTechCode, string subLocation, Warehouse warehouse) => new()
    {
        Id = id,
        WarehouseId = warehouse.Id,
        Warehouse = warehouse,
        Code = code,
        Name = name,
        CropQcRoomName = cropQcRoomName,
        CompuTechRoomCode = compuTechCode,
        DisplayName = cropQcRoomName,
        SubLocation = subLocation
    };

    private static RoomInventoryAdjustment CurrentCorrection(long id, Warehouse warehouse, Room room, FruitProfile fruitProfile, string lot, int bins, DateTimeOffset at, string source) => new()
    {
        Id = id,
        WarehouseId = warehouse.Id,
        Warehouse = warehouse,
        RoomId = room.Id,
        Room = room,
        FruitProfileId = fruitProfile.Id,
        FruitProfile = fruitProfile,
        GrowerName = "Wes Verified Current Inventory",
        LotNumber = lot,
        VarietyCode = fruitProfile.VarietyCode,
        OldBinCount = null,
        ChangeAmount = bins,
        NewBinCount = bins,
        AdjustmentType = RoomInventoryImportService.StartingInventoryAdjustmentType,
        Source = source,
        Reason = source,
        AdjustmentAt = at,
        CreatedByUserId = 9001,
        CreatedAt = DateTimeOffset.UtcNow
    };

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

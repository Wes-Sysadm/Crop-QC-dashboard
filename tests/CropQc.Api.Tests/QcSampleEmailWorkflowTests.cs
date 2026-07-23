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

public sealed class QcSampleEmailWorkflowTests
{
    [Fact]
    public async Task SendPassesResolvedDefaultAndConfirmedOrchardManagerToGmailSender()
    {
        await using var db = CreateDbContext();
        var sample = await SeedSampleAsync(db, "Lot Sample", emailStatus: "Not Sent", hasStarch: false);
        var now = DateTimeOffset.UtcNow;
        var orchard = new CanonicalOrchard { OrchardName = "Windy Point", NormalizedOrchardKey = "WINDYPOINT", CreatedAt = now, UpdatedAt = now };
        var block = new CanonicalOrchardBlock
        {
            CanonicalOrchard = orchard,
            OrchardName = orchard.OrchardName,
            CanonicalBlockName = "A",
            NormalizedOrchardKey = orchard.NormalizedOrchardKey,
            NormalizedBlockKey = "A",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.OrchardReportRecipients.Add(new OrchardReportRecipient
        {
            CanonicalOrchard = orchard,
            EmailAddress = "manager@example.com",
            NormalizedEmailAddress = "MANAGER@EXAMPLE.COM",
            CreatedAt = now,
            UpdatedAt = now
        });
        sample.Receipt!.CanonicalOrchardBlock = block;
        await db.SaveChangesAsync();
        var sender = new CapturingEmailSender();
        var resolver = new QcEmailRecipientResolver(db, new EmailOptions(), NullLogger<QcEmailRecipientResolver>.Instance);
        var service = CreateService(db, role: "QC User", recipientResolver: resolver, emailSender: sender);

        Assert.Null(await service.SendQcSummaryAsync(sample.Id, CancellationToken.None));
        Assert.NotNull(sender.Message);
        Assert.Equal("qc@fruitandland.com, manager@example.com", sender.Message!.To);
        Assert.Empty(sender.Message.InlineImages);
    }

    [Fact]
    public async Task LotSample_RemainsLotSampleAfterPreview()
    {
        await using var db = CreateDbContext();
        var sample = await SeedSampleAsync(db, "Lot Sample", emailStatus: "Not Sent", hasStarch: false);
        var service = CreateService(db, role: "Manager");

        var preview = await service.GetOverrideSendAsync(sample.Id, CancellationToken.None);

        Assert.Null(preview.DataWarning);
        Assert.Equal("Lot Sample", await SampleTypeNameAsync(db, sample.Id));
    }

    [Theory]
    [InlineData("Lot Sample", false)]
    [InlineData("Door Sample", false)]
    [InlineData("Truck Sample", true)]
    public async Task SampleType_RemainsUnchangedAfterSend(string sampleTypeName, bool hasStarch)
    {
        await using var db = CreateDbContext();
        var sample = await SeedSampleAsync(db, sampleTypeName, emailStatus: "Not Sent", hasStarch: hasStarch);
        var service = CreateService(db, role: "QC User", composer: new MutatingEmailComposer(receivingSampleTypeId: 1));

        var error = await service.SendQcSummaryAsync(sample.Id, CancellationToken.None);

        Assert.Null(error);
        Assert.Equal(sampleTypeName, await SampleTypeNameAsync(db, sample.Id));
    }

    [Fact]
    public async Task NonAdmin_CannotChangeSampleTypeAfterEmailSent()
    {
        await using var db = CreateDbContext();
        var sample = await SeedSampleAsync(db, "Lot Sample", emailStatus: "Sent", hasStarch: false);
        var doorSampleTypeId = await SampleTypeIdAsync(db, "Door Sample");
        var service = CreateService(db, role: "QC User");

        var error = await service.UpdateSampleTypeAsync(new UpdateSampleTypeForm { SampleId = sample.Id, SampleTypeId = doorSampleTypeId }, CancellationToken.None);

        Assert.Contains("Daily QC Admin access is required to change sample type after QC Summary email has been sent.", error);
        Assert.Equal("Lot Sample", await SampleTypeNameAsync(db, sample.Id));
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Admin_CanChangeSampleTypeAfterEmailSentAndAuditIsRecorded()
    {
        await using var db = CreateDbContext();
        var sample = await SeedSampleAsync(db, "Lot Sample", emailStatus: "Sent", hasStarch: false);
        var doorSampleTypeId = await SampleTypeIdAsync(db, "Door Sample");
        var service = CreateService(db, role: "Admin");

        var error = await service.UpdateSampleTypeAsync(new UpdateSampleTypeForm { SampleId = sample.Id, SampleTypeId = doorSampleTypeId }, CancellationToken.None);

        Assert.Null(error);
        Assert.Equal("Door Sample", await SampleTypeNameAsync(db, sample.Id));
        var audits = await db.AuditLogs.ToListAsync();
        var typeAudit = Assert.Single(audits, x => x.Action == "sample-type-change-after-send");
        Assert.Equal(nameof(QcSample), typeAudit.EntityName);
        Assert.Contains("Lot Sample", typeAudit.BeforeValuesJson);
        Assert.Contains("Door Sample", typeAudit.AfterValuesJson);
        var resendAudit = Assert.Single(audits, x => x.Action == "changed-after-send");
        Assert.Contains("sample-type-change", resendAudit.AfterValuesJson);
    }

    [Fact]
    public async Task SentSample_NoLongerAppearsInReadyToSendList()
    {
        await using var db = CreateDbContext();
        var sample = await SeedSampleAsync(db, "Lot Sample", emailStatus: "Not Sent", hasStarch: false);
        sample.SampleTakenAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var service = CreateService(db, role: "QC User");

        var sendError = await service.SendQcSummaryAsync(sample.Id, CancellationToken.None);
        var ready = await service.GetDailyQcDashboardAsync(null, "ReadyToSend", CancellationToken.None);
        var sent = await service.GetDailyQcDashboardAsync(null, "Sent", CancellationToken.None);
        var detail = await service.GetSampleDetailAsync(sample.Id, CancellationToken.None);

        Assert.Null(sendError);
        Assert.Empty(ready.Samples);
        var sentSample = Assert.Single(sent.Samples);
        Assert.Equal("Sent", sentSample.EmailStatus);
        Assert.NotNull(sentSample.EmailSentAt);
        Assert.Equal("QC User", sentSample.EmailSentBy);
        Assert.Equal("Sent", detail.Sample?.EmailStatus);
        Assert.NotNull(detail.Sample?.EmailSentAt);
    }

    [Fact]
    public async Task SavingUnchangedRowsAfterSend_DoesNotMarkNeedsResend()
    {
        await using var db = CreateDbContext();
        var sample = await SeedSampleAsync(db, "Lot Sample", emailStatus: "Not Sent", hasStarch: false);
        var service = CreateService(db, role: "QC User");

        Assert.Null(await service.SendQcSummaryAsync(sample.Id, CancellationToken.None));
        var saveError = await service.SaveFruitReadingsAsync(RowForm(sample.Id, 12m), CancellationToken.None);

        Assert.Null(saveError);
        var status = await db.QcSamples.AsNoTracking().Where(x => x.Id == sample.Id).Select(x => new { x.EmailStatus, x.Status }).SingleAsync();
        Assert.Equal("Sent", status.EmailStatus);
        Assert.Equal("Sent", status.Status);
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), x => x.Action == "changed-after-send");
    }

    [Fact]
    public async Task SavingChangedRowsAfterSend_MarksNeedsResendAndAudits()
    {
        await using var db = CreateDbContext();
        var sample = await SeedSampleAsync(db, "Lot Sample", emailStatus: "Not Sent", hasStarch: false);
        var service = CreateService(db, role: "QC User");

        Assert.Null(await service.SendQcSummaryAsync(sample.Id, CancellationToken.None));
        var saveError = await service.SaveFruitReadingsAsync(RowForm(sample.Id, 14m), CancellationToken.None);

        Assert.Null(saveError);
        var status = await db.QcSamples.AsNoTracking().Where(x => x.Id == sample.Id).Select(x => new { x.EmailStatus, x.Status }).SingleAsync();
        Assert.Equal("Needs Resend", status.EmailStatus);
        Assert.Equal("Needs Resend", status.Status);
        var audit = Assert.Single(await db.AuditLogs.ToListAsync(), x => x.Action == "changed-after-send");
        Assert.Contains("fruit-row-change", audit.AfterValuesJson);
    }

    [Fact]
    public async Task ReceiptAutosave_ChangesOnlySubmittedFieldAndReturnsCalculatedSize()
    {
        await using var db = CreateDbContext();
        var sample = await SeedSampleAsync(db, "Lot Sample", emailStatus: "Not Sent", hasStarch: false);
        var service = CreateService(db, role: "QC User");

        var result = await service.AutosaveFruitReadingsAsync(sample.Id, new FieldSampleAutosaveRequest
        {
            ChangeId = "receipt-weight-1",
            Source = "Scale",
            TargetSampleSize = 10,
            RowChanges =
            [
                new FieldSampleAutosaveRowChange
                {
                    RowNumber = 1,
                    Changes = [new FieldSampleAutosaveFieldChange { Field = "WeightGrams", OriginalValue = "180", Value = "215" }]
                }
            ]
        }, CancellationToken.None);

        Assert.True(result.Saved);
        var saved = Assert.Single(result.Rows, row => row.RowNumber == 1);
        Assert.Equal(215m, saved.WeightGrams);
        Assert.Equal(88, saved.SizeCategory);
        var row = await db.QcFruitReadings.AsNoTracking().SingleAsync(x => x.QcSampleId == sample.Id && x.RowNumber == 1);
        Assert.Equal(12m, row.Pressure1Lbs);
        Assert.Equal(13m, row.Pressure2Lbs);
    }

    [Fact]
    public async Task ReceiptAutosave_DetectsNewerQcStationPressureWithoutOverwritingIt()
    {
        await using var db = CreateDbContext();
        var sample = await SeedSampleAsync(db, "Lot Sample", emailStatus: "Not Sent", hasStarch: false);
        var stationRow = await db.QcFruitReadings.SingleAsync(x => x.QcSampleId == sample.Id && x.RowNumber == 1);
        stationRow.Pressure1Lbs = 14m;
        stationRow.Pressure1Source = "QC Station";
        stationRow.FieldVersion++;
        await db.SaveChangesAsync();
        var service = CreateService(db, role: "QC User");

        var result = await service.AutosaveFruitReadingsAsync(sample.Id, new FieldSampleAutosaveRequest
        {
            ChangeId = "receipt-pressure-conflict",
            TargetSampleSize = 10,
            RowChanges =
            [
                new FieldSampleAutosaveRowChange
                {
                    RowNumber = 1,
                    Changes = [new FieldSampleAutosaveFieldChange { Field = "Pressure1Lbs", OriginalValue = "12", Value = "15" }]
                }
            ]
        }, CancellationToken.None);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("Pressure1Lbs", conflict.Field);
        Assert.Contains("QC Station", conflict.Message);
        Assert.Equal(14m, (await db.QcFruitReadings.AsNoTracking().SingleAsync(x => x.Id == stationRow.Id)).Pressure1Lbs);
    }

    [Fact]
    public async Task ReceiptReportPreview_DoesNotSendOrChangeStatus()
    {
        await using var db = CreateDbContext();
        var sample = await SeedSampleAsync(db, "Lot Sample", emailStatus: "Not Sent", hasStarch: false);
        var service = CreateService(db, role: "QC User");

        var preview = await service.GetQcReportPreviewAsync(sample.Id, CancellationToken.None);

        Assert.Equal(sample.Id, preview.SampleId);
        Assert.Equal("qc-recipient@fruitandland.com", preview.Recipients);
        Assert.Equal("<p>Html</p>", preview.HtmlBody);
        Assert.Empty(await db.QcSummaryEmailLogs.ToListAsync());
        Assert.Equal("Not Sent", await db.QcSamples.Where(x => x.Id == sample.Id).Select(x => x.EmailStatus).SingleAsync());
    }

    [Theory]
    [InlineData("Truck Sample", false, false, true)]
    [InlineData("Lot Sample", false, true, false)]
    [InlineData("Door Sample", false, true, false)]
    public async Task EmailReadiness_RequiresStarchOnlyForTruckSamples(string sampleTypeName, bool hasStarch, bool expectedReady, bool expectStarchMissingItem)
    {
        await using var db = CreateDbContext();
        var sample = await SeedSampleAsync(db, sampleTypeName, emailStatus: "Not Sent", hasStarch: hasStarch);
        var service = CreateService(db, role: "QC User");

        var detail = await service.GetSampleDetailAsync(sample.Id, CancellationToken.None);

        Assert.Equal(expectedReady, detail.Readiness.IsReady);
        Assert.Equal(1, detail.Readiness.StarchMissingCount);
        if (expectStarchMissingItem)
        {
            Assert.Contains(detail.Readiness.MissingItems, x => x.Contains("Starch", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            Assert.DoesNotContain(detail.Readiness.MissingItems, x => x.Contains("Starch", StringComparison.OrdinalIgnoreCase));
        }
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
            Id = 10,
            Email = "qc@fruitandland.com",
            DisplayName = "QC User",
            Domain = "fruitandland.com",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.SampleTypes.Add(new SampleType { Id = 20, Name = "Truck Sample", IsActive = true });
        db.SaveChanges();
        return db;
    }

    private static async Task<QcSample> SeedSampleAsync(CropQcDbContext db, string sampleTypeName, string emailStatus, bool hasStarch)
    {
        var sampleTypeId = await SampleTypeIdAsync(db, sampleTypeName);
        var warehouse = new Warehouse { Id = 100, Code = "WP", Name = "Washington Packing" };
        var room = new Room { Id = 100, WarehouseId = warehouse.Id, Warehouse = warehouse, Code = "BLUE06", Name = "Blue 06" };
        var fruitProfile = new FruitProfile { Id = 100, Name = "Gala", VarietyCode = "9450", FruitType = "Apple", ProductionType = "Conventional" };
        var receipt = new Receipt
        {
            Id = 100,
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.Parse("2026-06-01T08:00:00-07:00"),
            CompuTechReceiptId = "R100",
            ReceiptType = "Truck receipt",
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            RoomId = room.Id,
            Room = room,
            FruitProfileId = fruitProfile.Id,
            FruitProfile = fruitProfile,
            GrowerName = "Reese",
            LotCode = "LOT-1",
            BinCount = 10,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var sample = new QcSample
        {
            Id = 100,
            ReceiptId = receipt.Id,
            Receipt = receipt,
            SampleTypeId = sampleTypeId,
            SampleType = await db.SampleTypes.SingleAsync(x => x.Id == sampleTypeId),
            Status = "Ready to Send",
            StarchStatus = hasStarch ? "Starch Complete" : "Starch Pending",
            PhotoStatus = "Photos Complete",
            EmailStatus = emailStatus,
            ActualSampleSize = 10,
            SampleTakenAt = DateTimeOffset.Parse("2026-06-01T10:00:00-07:00"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var starch = hasStarch ? await db.StarchScaleValues.OrderBy(x => x.Id).FirstAsync() : null;
        sample.FruitReadings.Add(new QcFruitReading
        {
            Id = 100,
            QcSampleId = sample.Id,
            RowNumber = 1,
            Pressure1Lbs = 12m,
            Pressure2Lbs = 13m,
            WeightGrams = 180m,
            SizeCategory = 113,
            GradeId = 1,
            StarchScaleValueId = starch?.Id,
            SizeStatus = "Sized",
            IsCompleted = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        receipt.Photos.Add(Photo(101, "BinTruck", receiptId: receipt.Id));
        receipt.Photos.Add(Photo(102, "TopOfTruck", receiptId: receipt.Id));
        sample.Photos.Add(Photo(103, "Hectre", sampleId: sample.Id));
        sample.Photos.Add(Photo(104, "SampleBeforeCutting", sampleId: sample.Id));
        sample.Photos.Add(Photo(105, "CutFruit", sampleId: sample.Id));
        if (hasStarch)
        {
            sample.Photos.Add(Photo(106, "FruitAfterStarch", sampleId: sample.Id));
        }

        db.Warehouses.Add(warehouse);
        db.Rooms.Add(room);
        db.FruitProfiles.Add(fruitProfile);
        db.Receipts.Add(receipt);
        db.QcSamples.Add(sample);
        await db.SaveChangesAsync();
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
        FileSizeBytes = 10,
        StorageProvider = FileStorageProviders.Local,
        SharePointDriveId = "local",
        SharePointItemId = $"photo-{id}",
        FileId = $"photo-{id}",
        CapturedAt = DateTimeOffset.UtcNow
    };

    private static async Task<int> SampleTypeIdAsync(CropQcDbContext db, string name) =>
        await db.SampleTypes.Where(x => x.Name == name).Select(x => x.Id).SingleAsync();

    private static async Task<string> SampleTypeNameAsync(CropQcDbContext db, long sampleId) =>
        await db.QcSamples.AsNoTracking().Where(x => x.Id == sampleId).Select(x => x.SampleType.Name).SingleAsync();

    private static SaveFruitReadingsForm RowForm(long sampleId, decimal pressure1) => new()
    {
        SampleId = sampleId,
        TargetSampleSize = 10,
        Rows =
        [
            new FruitReadingEditRow
            {
                RowNumber = 1,
                Pressure1Lbs = pressure1,
                Pressure2Lbs = 13m,
                WeightGrams = 180m,
                GradeId = 1
            }
        ]
    };

    private static DashboardDataService CreateService(
        CropQcDbContext db,
        string role,
        IQcSummaryEmailComposer? composer = null,
        IQcEmailRecipientResolver? recipientResolver = null,
        IQcEmailSender? emailSender = null)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Email, "qc@fruitandland.com"),
                    new Claim(ClaimTypes.Role, role)
                ],
                "TestAuth"))
        };
        var configuration = new ConfigurationBuilder().Build();
        return new DashboardDataService(
            db,
            new FakeFileStorageService(),
            new FileStorageOptions(),
            new EmailOptions { Provider = EmailProviders.GmailUser, QcDefaultRecipients = "qc-recipient@fruitandland.com" },
            recipientResolver ?? new FakeRecipientResolver(),
            new GoogleAuthenticationOptions { AllowedDomains = new HashSet<string>(["fruitandland.com"], StringComparer.OrdinalIgnoreCase) },
            new FakeCredentialStore(),
            emailSender ?? new FakeEmailSender(),
            new QcPhotoRequirementPolicy(),
            composer ?? new StableEmailComposer(),
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

    private sealed class MutatingEmailComposer(int receivingSampleTypeId) : IQcSummaryEmailComposer
    {
        public Task<QcEmailContent> ComposeAsync(QcSample sample, ReadinessViewModel readiness, User? sendingUser, bool isOverride, string? overrideReason, CancellationToken cancellationToken)
        {
            sample.SampleTypeId = receivingSampleTypeId;
            return Task.FromResult(new QcEmailContent("QC Summary", "<p>Html</p>", "Text", []));
        }
    }

    private sealed class FakeEmailSender : IQcEmailSender
    {
        public Task<QcEmailSendResult> SendAsync(User sender, QcEmailMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(QcEmailSendResult.Sent("gmail-1"));
    }

    private sealed class CapturingEmailSender : IQcEmailSender
    {
        public QcEmailMessage? Message { get; private set; }

        public Task<QcEmailSendResult> SendAsync(User sender, QcEmailMessage message, CancellationToken cancellationToken)
        {
            Message = message;
            return Task.FromResult(QcEmailSendResult.Sent("gmail-1"));
        }
    }

    private sealed class FakeRecipientResolver : IQcEmailRecipientResolver
    {
        public Task<QcEmailRecipientResolution> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new QcEmailRecipientResolution(["qc-recipient@fruitandland.com"], QcEmailRecipientSources.FallbackConfiguration));
    }

    private sealed class FakeCredentialStore : IGoogleCredentialStore
    {
        public Task SaveFromAuthenticationPropertiesAsync(User user, AuthenticationProperties properties, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<GoogleAccessTokenResult> GetAccessTokenAsync(User user, CancellationToken cancellationToken) =>
            Task.FromResult(GoogleAccessTokenResult.Success("token"));

        public Task<GoogleCredentialDiagnostic> GetDiagnosticAsync(User user, CancellationToken cancellationToken) =>
            Task.FromResult(new GoogleCredentialDiagnostic(true, true));
    }

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

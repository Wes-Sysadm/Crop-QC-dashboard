using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class TreatmentReportAttachmentTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-19T16:00:00Z");

    [Theory]
    [InlineData("report.pdf", "application/pdf")]
    [InlineData("page.jpg", "image/jpeg")]
    [InlineData("page.jpeg", "image/jpeg")]
    [InlineData("page.png", "image/png")]
    [InlineData("page.webp", "image/webp")]
    public async Task Supported_pdf_and_image_signatures_upload_to_exact_application(string name, string contentType)
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.UploadAsync(Fixture.ApplicationId,
            new TreatmentReportUploadForm { OperationKey = "supported", Files = [File(name, contentType)] }, fixture.Principal, default);

        Assert.Equal(1, result.Uploaded);
        Assert.Empty(result.Failures);
        var attachment = await fixture.Db.RoomTreatmentApplicationAttachments.SingleAsync();
        Assert.Equal(Fixture.ApplicationId, attachment.RoomTreatmentApplicationId);
        Assert.Equal(contentType, attachment.ContentType);
        Assert.Contains("Treatment Reports/2026/EBS/Treatment-7003", attachment.StoragePath);
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.Action == "TreatmentReportAdded").ToListAsync());
        Assert.Equal(0, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(0, await fixture.Db.TreatmentLineageMovements.CountAsync());
    }

    [Fact]
    public async Task Multiple_pages_and_pdf_coexist_and_operation_key_is_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var form = new TreatmentReportUploadForm
        {
            OperationKey = "multi",
            Files = [File("page-1.jpg", "image/jpeg"), File("page-2.png", "image/png"), File("signed.pdf", "application/pdf")]
        };
        var first = await fixture.Service.UploadAsync(Fixture.ApplicationId, form, fixture.Principal, default);
        var repeat = await fixture.Service.UploadAsync(Fixture.ApplicationId, form, fixture.Principal, default);

        Assert.Equal(3, first.Uploaded);
        Assert.Equal(3, repeat.Uploaded);
        Assert.Equal(3, await fixture.Db.RoomTreatmentApplicationAttachments.CountAsync());
        Assert.Equal(3, fixture.Storage.SaveCount);
        Assert.Equal(3, await fixture.Db.AuditLogs.CountAsync(x => x.Action == "TreatmentReportAdded"));
    }

    [Theory]
    [InlineData("empty.pdf", "application/pdf", "empty")]
    [InlineData("malware.exe", "application/octet-stream", "Only PDF")]
    [InlineData("spoof.pdf", "application/pdf", "contents")]
    [InlineData("spoof.jpg", "image/jpeg", "contents")]
    public async Task Empty_unsupported_and_mime_spoofed_files_are_rejected_without_writes(string name, string contentType, string expected)
    {
        await using var fixture = await Fixture.CreateAsync();
        var bytes = name == "empty.pdf" ? Array.Empty<byte>() : "not the declared format"u8.ToArray();
        var result = await fixture.Service.UploadAsync(Fixture.ApplicationId,
            new TreatmentReportUploadForm { OperationKey = "invalid", Files = [File(name, contentType, bytes)] }, fixture.Principal, default);

        Assert.Contains(expected, Assert.Single(result.Failures), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Db.RoomTreatmentApplicationAttachments.ToListAsync());
        Assert.Equal(0, fixture.Storage.SaveCount);
    }

    [Fact]
    public async Task Oversize_report_is_rejected_without_storage_or_metadata_write()
    {
        await using var fixture = await Fixture.CreateAsync();
        var bytes = new byte[TreatmentReportAttachmentService.MaxFileSizeBytes + 1];
        "%PDF-"u8.CopyTo(bytes);

        var result = await fixture.Service.UploadAsync(Fixture.ApplicationId,
            new TreatmentReportUploadForm { OperationKey = "oversize", Files = [File("oversize.pdf", "application/pdf", bytes)] }, fixture.Principal, default);

        Assert.Contains("15 MB", Assert.Single(result.Failures));
        Assert.Empty(await fixture.Db.RoomTreatmentApplicationAttachments.ToListAsync());
        Assert.Equal(0, fixture.Storage.SaveCount);
    }

    [Fact]
    public async Task Optional_storage_failure_preserves_treatment_and_supports_later_retry()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Storage.FailSave = true;
        var failed = await fixture.Service.UploadAsync(Fixture.ApplicationId,
            new TreatmentReportUploadForm { OperationKey = "retry-one", Files = [File("report.pdf", "application/pdf")] }, fixture.Principal, default);
        Assert.Single(failed.Failures);
        Assert.NotNull(await fixture.Db.RoomTreatmentApplications.FindAsync(Fixture.ApplicationId));
        Assert.Empty(await fixture.Db.RoomTreatmentApplicationAttachments.ToListAsync());

        fixture.Storage.FailSave = false;
        var retried = await fixture.Service.UploadAsync(Fixture.ApplicationId,
            new TreatmentReportUploadForm { OperationKey = "retry-two", Files = [File("report.pdf", "application/pdf")] }, fixture.Principal, default);
        Assert.Equal(1, retried.Uploaded);
        Assert.Single(await fixture.Db.RoomTreatmentApplicationAttachments.ToListAsync());
    }

    [Fact]
    public async Task Protected_content_checks_view_permission_and_exact_attachment_ownership()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.UploadAsync(Fixture.ApplicationId,
            new TreatmentReportUploadForm { OperationKey = "content", Files = [File("scan.jpg", "image/jpeg")] }, fixture.Principal, default);
        var attachment = await fixture.Db.RoomTreatmentApplicationAttachments.SingleAsync();

        var allowed = await fixture.Service.OpenReadAsync(Fixture.ApplicationId, attachment.Id, fixture.Principal, default);
        var wrongOwner = await fixture.Service.OpenReadAsync(Fixture.ApplicationId + 1, attachment.Id, fixture.Principal, default);
        fixture.Access.Level = PageAccessLevel.None;
        var denied = await fixture.Service.OpenReadAsync(Fixture.ApplicationId, attachment.Id, fixture.Principal, default);

        Assert.NotNull(allowed.Content);
        Assert.Equal("image/jpeg", allowed.ContentType);
        Assert.Null(wrongOwner.Content);
        Assert.Null(denied.Content);
    }

    [Fact]
    public async Task Admin_removal_is_soft_audited_and_does_not_change_or_reverse_treatment()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.UploadAsync(Fixture.ApplicationId,
            new TreatmentReportUploadForm { OperationKey = "remove", Files = [File("scan.pdf", "application/pdf")] }, fixture.Principal, default);
        var attachment = await fixture.Db.RoomTreatmentApplicationAttachments.SingleAsync();
        fixture.Access.Level = PageAccessLevel.Edit;
        Assert.Contains("Admin", await fixture.Service.RemoveAsync(Fixture.ApplicationId, attachment.Id, "wrong scan", fixture.Principal, default));
        fixture.Access.Level = PageAccessLevel.Admin;
        Assert.Null(await fixture.Service.RemoveAsync(Fixture.ApplicationId, attachment.Id, "Wrong signed page", fixture.Principal, default));

        attachment = await fixture.Db.RoomTreatmentApplicationAttachments.SingleAsync();
        var application = await fixture.Db.RoomTreatmentApplications.SingleAsync();
        Assert.True(attachment.IsDeleted);
        Assert.NotNull(attachment.DeletedAt);
        Assert.Equal("Wrong signed page", attachment.DeleteReason);
        Assert.Null(application.ReversedAt);
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.Action == "TreatmentReportRemoved").ToListAsync());
        Assert.Single(fixture.Storage.Stored);
        Assert.Null((await fixture.Service.OpenReadAsync(Fixture.ApplicationId, attachment.Id, fixture.Principal, default)).Content);
    }

    [Fact]
    public async Task Reversing_treatment_retains_active_report_and_protected_content()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.UploadAsync(Fixture.ApplicationId,
            new TreatmentReportUploadForm { OperationKey = "reverse-parent", Files = [File("retained.pdf", "application/pdf")] }, fixture.Principal, default);
        var application = await fixture.Db.RoomTreatmentApplications.SingleAsync();
        application.ReversedAt = Now;
        application.ReversalReason = "Disposable reversal proof";
        await fixture.Db.SaveChangesAsync();
        var attachment = await fixture.Db.RoomTreatmentApplicationAttachments.AsNoTracking().SingleAsync();

        var content = await fixture.Service.OpenReadAsync(Fixture.ApplicationId, attachment.Id, fixture.Principal, default);

        Assert.False(attachment.IsDeleted);
        Assert.NotNull(content.Content);
        Assert.Equal("application/pdf", content.ContentType);
    }

    [Fact]
    public void Presentation_stages_real_previews_mobile_capture_and_keeps_private_routes()
    {
        var apply = Source("src", "CropQc.Web", "Views", "RoomTreatments", "Apply.cshtml");
        var room = Source("src", "CropQc.Web", "Views", "Home", "Room.cshtml");
        var script = Source("src", "CropQc.Web", "wwwroot", "js", "treatment-reports.js");
        var controller = Source("src", "CropQc.Web", "Controllers", "RoomTreatmentsController.cs");
        var service = Source("src", "CropQc.Web", "Services", "TreatmentReportAttachmentService.cs");

        Assert.Contains("Treatment Report", apply);
        Assert.Contains("Scan / Take Photo", apply);
        Assert.Contains("capture=\"environment\"", apply);
        Assert.Contains("application/pdf", apply);
        Assert.Contains("No treatment report attached.", room);
        Assert.Contains("Add Treatment Report", room);
        Assert.Contains("URL.createObjectURL", script);
        Assert.Contains("URL.revokeObjectURL", script);
        Assert.Contains("DataTransfer", script);
        Assert.Contains("[Authorize]", controller);
        Assert.Contains("ApplicationAreas.Rooms", service);
        Assert.Contains("ApplicationAreas.Receipts", service);
        Assert.Contains("[ValidateAntiForgeryToken]", controller);
    }

    [Fact]
    public void Migration_compatibility_and_current_object_gate_are_bounded_and_current()
    {
        var migration = Source("src", "CropQc.Data", "Migrations", "20260819142656_AddTreatmentReportAttachments.cs");
        var preflight = Source("scripts", "postgresql", "preflight-treatment-report-attachments.sql");
        var apply = Source("scripts", "postgresql", "apply-treatment-report-attachments-schema.sql");
        var verify = Source("scripts", "postgresql", "verify-treatment-report-attachments.sql");
        var gate = Source("src", "CropQc.Web", "Services", "DatabaseStartupDiagnostics.cs");

        Assert.Contains("CreateTable", migration);
        Assert.DoesNotContain("20260818181556_AddRoomTreatmentTracking.cs", migration);
        Assert.Contains("state_a_absent", preflight);
        Assert.Contains("state_b_complete_exact", preflight);
        Assert.Contains("State C", preflight);
        Assert.Contains("pg_advisory_xact_lock", apply);
        Assert.Contains("cropqc.test_force_treatment_report_failure", apply);
        Assert.DoesNotContain("INSERT INTO \"__EFMigrationsHistory\"", apply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("26 AS checked_target_objects", verify);
        Assert.Contains("20260828033737_AddTransferCustodyWorkflow", gate);
        Assert.Equal(836, gate.Split('\n').Count(x => x.TrimStart().StartsWith("new(", StringComparison.Ordinal) || x.TrimStart().StartsWith(",new(", StringComparison.Ordinal)));
    }

    private static FormFile File(string name, string contentType, byte[]? bytes = null)
    {
        bytes ??= contentType switch
        {
            "application/pdf" => "%PDF-1.7\nreport"u8.ToArray(),
            "image/jpeg" => [0xff, 0xd8, 0xff, 0xe0, 1, 2, 3],
            "image/png" => [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1],
            "image/webp" => "RIFF0000WEBPdata"u8.ToArray(),
            _ => "unsupported"u8.ToArray()
        };
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Files", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    private static string Source(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. segments]);
            if (System.IO.File.Exists(path)) return System.IO.File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const long ApplicationId = 7003;
        private Fixture(CropQcDbContext db, FakeStorage storage, MutableAccess access, TreatmentReportAttachmentService service, ClaimsPrincipal principal)
            => (Db, Storage, Access, Service, Principal) = (db, storage, access, service, principal);
        public CropQcDbContext Db { get; }
        public FakeStorage Storage { get; }
        public MutableAccess Access { get; }
        public TreatmentReportAttachmentService Service { get; }
        public ClaimsPrincipal Principal { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>()
                .UseInMemoryDatabase($"treatment-report-{Guid.NewGuid():N}").Options);
            await db.Database.EnsureCreatedAsync();
            var user = new User { Id = 7001, Email = ApplicationAreas.OwnerEmail, DisplayName = "Wes", Domain = "fruitandland.com", CreatedAt = Now };
            var warehouse = new Warehouse { Id = 7002, Code = "EBS", Name = "EBS" };
            db.AddRange(user, warehouse, new RoomTreatmentApplication
            {
                Id = ApplicationId,
                OperationKey = "treatment-parent",
                TreatmentChemicalId = 1,
                WarehouseId = warehouse.Id,
                Warehouse = warehouse,
                RoomId = 1,
                AppliedAt = Now,
                AppliedByUserId = user.Id,
                AppliedByUser = user,
                TotalBinsSnapshot = 100,
                ProductNameSnapshot = "eFOG",
                CropSnapshot = "Apples",
                UnitSnapshot = "BIN",
                CurrencySnapshot = "USD",
                CreatedAt = Now,
                CreatedByUserId = user.Id,
                CreatedByUser = user
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "Test"));
            var storage = new FakeStorage();
            var access = new MutableAccess();
            var service = new TreatmentReportAttachmentService(db, storage, access,
                new PacificBusinessTimeService(new FixedClock(Now)), NullLogger<TreatmentReportAttachmentService>.Instance);
            return new(db, storage, access, service, principal);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FakeStorage : IFileStorageService
    {
        public Dictionary<string, byte[]> Stored { get; } = [];
        public int SaveCount { get; private set; }
        public bool FailSave { get; set; }
        public string GenerateTargetPath(FileStorageTargetContext context) => throw new NotSupportedException();
        public async Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default)
        {
            if (FailSave) throw new InvalidOperationException("simulated storage failure");
            using var memory = new MemoryStream();
            await request.Content.CopyToAsync(memory, cancellationToken);
            var key = $"report-{++SaveCount}";
            Stored[key] = memory.ToArray();
            return new("Test", key, request.TargetPath, request.FileName, request.ContentType, memory.Length, FileId: key, FolderId: "folder");
        }
        public Task<FileStorageReference?> GetMetadataAsync(string storageKey, CancellationToken cancellationToken = default) => Task.FromResult<FileStorageReference?>(null);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Stored.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes) : null);
        public Task DeleteOrVoidAsync(string storageKey, CancellationToken cancellationToken = default) { Stored.Remove(storageKey); return Task.CompletedTask; }
    }

    private sealed class MutableAccess : IUserAccessService
    {
        public PageAccessLevel Level { get; set; } = PageAccessLevel.Admin;
        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken) => Task.FromResult(Level >= minimumLevel);
        public Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken) => Task.FromResult(Level);
        public void InvalidateAll() { }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock { public DateTimeOffset UtcNow => utcNow; }
}

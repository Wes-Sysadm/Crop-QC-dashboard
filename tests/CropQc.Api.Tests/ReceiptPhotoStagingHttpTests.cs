using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CropQc.Api.Tests;

public sealed class ReceiptPhotoStagingHttpTests
{
    [Fact]
    public async Task NewReceiptPage_RendersOptionalStagingAndZeroPhotosStillSavesNormally()
    {
        await using var factory = new ReceiptPhotoFactory();
        using var client = await factory.CreateOwnerClientAsync();
        var page = await client.GetAsync("/Receipts");
        var html = await page.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Receipt Photos (Optional)", html);
        Assert.Contains("No receipt photos selected.", html);
        Assert.Contains("data-staged-photo-list", html);
        Assert.Contains("data-stage-receipt-photos=\"true\"", html);
        Assert.Contains("data-staged-photo-take", html);
        Assert.Contains("Take Photo", html);
        Assert.Contains("Choose Existing Photo", html);
        Assert.Contains("capture=\"environment\"", html);

        var response = await client.PostAsync("/Receipts/Create", ReceiptForm("STAGED-ZERO"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Receipts", response.Headers.Location?.OriginalString);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        Assert.NotNull(await db.Receipts.SingleOrDefaultAsync(x => x.CompuTechReceiptId == "STAGED-ZERO"));
        Assert.Empty(await db.QcPhotos.ToListAsync());
        Assert.Equal(0, factory.Storage.SaveCount);
    }

    [Fact]
    public async Task TwoStagedPhotos_SaveOnceEachAgainstExactNewReceipt_AndCanBeRemovedSafely()
    {
        await using var factory = new ReceiptPhotoFactory();
        using var client = await factory.CreateOwnerClientAsync();
        var content = ReceiptForm("STAGED-TWO");
        AddPhoto(content, 0, "truck.jpg", "image/jpeg", "BinTruck", "OBSBOT Tiny 2 Lite", [0xff, 0xd8, 0xff, 0xd9]);
        AddPhoto(content, 1, "top.png", "image/png", "TopOfTruck", "Upload File", [0x89, 0x50, 0x4e, 0x47]);

        var response = await client.PostAsync("/Receipts/Create", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Receipts/", response.Headers.Location?.OriginalString);
        long receiptId;
        long photoId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var receipt = await db.Receipts.SingleAsync(x => x.CompuTechReceiptId == "STAGED-TWO");
            receiptId = receipt.Id;
            var photos = await db.QcPhotos.OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(2, photos.Count);
            Assert.All(photos, photo =>
            {
                Assert.Equal(receipt.Id, photo.ReceiptId);
                Assert.Null(photo.QcSampleId);
                Assert.False(photo.IsDeleted);
            });
            Assert.Equal(["BinTruck", "TopOfTruck"], photos.Select(x => x.PhotoType));
            photoId = photos[0].Id;
        }
        Assert.Equal(2, factory.Storage.SaveCount);
        Assert.Equal(2, factory.Storage.SavedRequests.Count);

        var remove = await client.PostAsync($"/Receipts/{receiptId}/photos/{photoId}/remove", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var removed = await db.QcPhotos.SingleAsync(x => x.Id == photoId);
            Assert.True(removed.IsDeleted);
            Assert.NotNull(removed.DeletedAt);
            Assert.Equal("Removed from receipt detail", removed.DeleteReason);
            Assert.Single(await db.QcPhotos.Where(x => !x.IsDeleted).ToListAsync());
            Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "remove-photo" && x.EntityKey == photoId.ToString());
        }
        Assert.Equal(0, factory.Storage.DeleteCount);
    }

    [Fact]
    public void SavedReceiptPhotoPresentation_KeepsThumbnailAndUsesAccessibleContextAwareTrashcan()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var groups = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_PhotoGroups.cshtml"));

        Assert.Contains("photo.WebUrl is not null && photo.ContentType.StartsWith(\"image/\"", service);
        Assert.Contains("$\"/Receipts/{thumbnailReceiptId}/photos/{photo.Id}/content\"", service);
        Assert.Contains("$\"/Receipts/{receiptId}/photos/{photo.Id}/remove\"", service);
        Assert.Contains("photo.ThumbnailUrl", groups);
        Assert.Contains("<a href=\"@photo.WebUrl\"", groups);
        Assert.Contains("<img src=\"@thumbnailUrl\"", groups);
        Assert.DoesNotContain("<img src=\"@photo.WebUrl\"", groups);
        Assert.Contains("@photo.FileName", groups);
        Assert.Contains("aria-label=\"Remove photo\"", groups);
        Assert.Contains("title=\"Remove photo\"", groups);
        Assert.Contains("Remove this receipt photo?", groups);
        Assert.Contains("Remove this photo from the sample?", groups);
        Assert.Contains("DisplayAsThumbnail", groups);
        Assert.Contains("loading=\"lazy\"", groups);
    }

    [Fact]
    public async Task SavedReceiptPhoto_UsesProtectedContentEndpoint_AndPreservesDriveLinkWithoutWrites()
    {
        await using var factory = new ReceiptPhotoFactory();
        using var client = await factory.CreateOwnerClientAsync();
        var create = await client.PostAsync("/Receipts/Create", ReceiptForm("PRIVATE-THUMB"));
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        long receiptId;
        long photoId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var receipt = await db.Receipts.SingleAsync(x => x.CompuTechReceiptId == "PRIVATE-THUMB");
            receiptId = receipt.Id;
            var photo = new QcPhoto
            {
                ReceiptId = receipt.Id,
                PhotoType = "TopOfTruck",
                PhotoSource = "OBSBOT Tiny 2 Lite",
                FileName = "private-top.png",
                ContentType = "image/png",
                FileSizeBytes = 8,
                StorageProvider = FileStorageProviders.GoogleDrive,
                FileId = "private-drive-file",
                SharePointDriveId = "",
                SharePointItemId = "private-drive-file",
                WebUrl = "https://drive.google.com/file/d/private-drive-file/view",
                CapturedAt = DateTimeOffset.UtcNow
            };
            db.QcPhotos.Add(photo);
            await db.SaveChangesAsync();
            photoId = photo.Id;
        }
        factory.Storage.ReadBytes["private-drive-file"] = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

        var contentUrl = $"/Receipts/{receiptId}/photos/{photoId}/content";

        int receiptsBefore;
        int photosBefore;
        int auditsBefore;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            receiptsBefore = await db.Receipts.CountAsync();
            photosBefore = await db.QcPhotos.CountAsync();
            auditsBefore = await db.AuditLogs.CountAsync();
        }

        var content = await client.GetAsync(contentUrl);
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("image/png", content.Content.Headers.ContentType?.MediaType);
        Assert.Equal(factory.Storage.ReadBytes["private-drive-file"], await content.Content.ReadAsByteArrayAsync());
        Assert.True(content.Headers.CacheControl?.Private);
        Assert.Equal(TimeSpan.FromMinutes(5), content.Headers.CacheControl?.MaxAge);
        Assert.Contains("must-revalidate", content.Headers.CacheControl?.ToString());
        Assert.Equal(1, factory.Storage.OpenReadCount);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            Assert.Equal(receiptsBefore, await db.Receipts.CountAsync());
            Assert.Equal(photosBefore, await db.QcPhotos.CountAsync());
            Assert.Equal(auditsBefore, await db.AuditLogs.CountAsync());
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Receipts/{receiptId + 999}/photos/{photoId}/content")).StatusCode);

        factory.Storage.ReadBytes.Remove("private-drive-file");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(contentUrl)).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var photo = await db.QcPhotos.SingleAsync(x => x.Id == photoId);
            photo.IsDeleted = true;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(contentUrl)).StatusCode);

        using var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync(contentUrl)).StatusCode);
    }

    [Fact]
    public async Task PhotoFailure_PreservesReceipt_LeavesNoPhotoRow_AndShowsRetryWarning()
    {
        await using var factory = new ReceiptPhotoFactory();
        using var client = await factory.CreateOwnerClientAsync();
        factory.Storage.FailuresRemaining = 1;
        var content = ReceiptForm("STAGED-FAIL");
        AddPhoto(content, 0, "truck.webp", "image/webp", "BinTruck", "Upload File", [0x52, 0x49, 0x46, 0x46]);

        var response = await client.PostAsync("/Receipts/Create", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var detail = await client.GetAsync(response.Headers.Location);
        var html = await detail.Content.ReadAsStringAsync();
        Assert.Contains("Receipt STAGED-FAIL was saved, but 1 of 1 photos could not be uploaded.", html);
        Assert.Contains("You can add the missing photo from Receipt Photos.", html);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        Assert.NotNull(await db.Receipts.SingleOrDefaultAsync(x => x.CompuTechReceiptId == "STAGED-FAIL"));
        Assert.Empty(await db.QcPhotos.ToListAsync());
        Assert.Equal(1, factory.Storage.SaveCount);
    }

    [Fact]
    public async Task ReceiptFailure_DoesNotAttemptAnyStagedPhotoUpload()
    {
        await using var factory = new ReceiptPhotoFactory();
        using var client = await factory.CreateOwnerClientAsync();
        var content = ReceiptForm("");
        AddPhoto(content, 0, "truck.jpg", "image/jpeg", "BinTruck", "Upload File", [0xff, 0xd8, 0xff, 0xd9]);

        var response = await client.PostAsync("/Receipts/Create", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Receipts", response.Headers.Location?.OriginalString);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        Assert.Empty(await db.QcPhotos.ToListAsync());
        Assert.Equal(0, factory.Storage.SaveCount);
    }

    [Fact]
    public async Task AuthenticatedPostgreSql_NewReceiptStagesExactPhotosAndRemovalIsAudited_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_RECEIPT_PHOTO_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        await using var factory = new ReceiptPhotoPostgreSqlFactory(connectionString);
        using var client = factory.CreateOwnerClient();
        int warehouseId;
        int roomId;
        int fruitProfileId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var room = await db.Rooms.AsNoTracking()
                .Where(x => x.IsActive && x.Warehouse.IsActive)
                .OrderBy(x => x.Warehouse.Code)
                .ThenBy(x => x.SortOrder)
                .FirstAsync();
            warehouseId = room.WarehouseId;
            roomId = room.Id;
            fruitProfileId = await db.FruitProfiles.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.VarietyCode)
                .Select(x => x.Id)
                .FirstAsync();
        }

        var page = await client.GetAsync("/Receipts");
        var pageHtml = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Receipt Photos (Optional)", pageHtml);
        Assert.DoesNotContain("could not be translated", pageHtml, StringComparison.OrdinalIgnoreCase);

        var receiptNumber = $"CODEX-PHOTO-{Guid.NewGuid():N}"[..30];
        var content = ReceiptForm(receiptNumber, warehouseId, roomId, fruitProfileId);
        AddPhoto(content, 0, "truck.jpg", "image/jpeg", "BinTruck", "OBSBOT Tiny 2 Lite", [0xff, 0xd8, 0xff, 0xd9]);
        AddPhoto(content, 1, "top.jpg", "image/jpeg", "TopOfTruck", "OBSBOT Tiny 2 Lite", [0xff, 0xd8, 0xff, 0xd9]);
        var create = await client.PostAsync("/Receipts/Create", content);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        long receiptId;
        long removedPhotoId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var receipt = await db.Receipts.SingleAsync(x => x.CompuTechReceiptId == receiptNumber);
            receiptId = receipt.Id;
            var photos = await db.QcPhotos.Where(x => x.ReceiptId == receiptId).OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(2, photos.Count);
            Assert.All(photos, x => Assert.Null(x.QcSampleId));
            Assert.Equal(["BinTruck", "TopOfTruck"], photos.Select(x => x.PhotoType));
            removedPhotoId = photos[0].Id;
        }

        var detail = await client.GetAsync(create.Headers.Location);
        var detailHtml = WebUtility.HtmlDecode(await detail.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Contains("<img", detailHtml);
        Assert.Contains("aria-label=\"Remove photo\"", detailHtml);
        Assert.Contains("Remove this receipt photo?", detailHtml);
        Assert.DoesNotContain("could not be translated", detailHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HTTP 500", detailHtml, StringComparison.OrdinalIgnoreCase);

        var remove = await client.PostAsync($"/Receipts/{receiptId}/photos/{removedPhotoId}/remove", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            Assert.True((await db.QcPhotos.SingleAsync(x => x.Id == removedPhotoId)).IsDeleted);
            Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "remove-photo" && x.EntityKey == removedPhotoId.ToString());
            Assert.Single(await db.QcPhotos.Where(x => x.ReceiptId == receiptId && !x.IsDeleted).ToListAsync());
        }
        Assert.Equal(2, factory.Storage.SaveCount);
        Assert.Equal(0, factory.Storage.DeleteCount);
    }

    [Fact]
    public async Task AuthenticatedPostgreSql_Run75Photo2339UsesProtectedContentWithoutWrites_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_RECEIPT_PHOTO_RESTORED_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        await using var factory = new ReceiptPhotoPostgreSqlFactory(connectionString);
        using var client = factory.CreateOwnerClient();
        long receiptId;
        string fileId;
        string webUrl;
        string contentType;
        int receiptsBefore;
        int photosBefore;
        int auditsBefore;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var receipt = await db.Receipts.SingleAsync(x => x.CompuTechReceiptId == "TR109003");
            var photo = await db.QcPhotos.SingleAsync(x => x.Id == 2339 && x.ReceiptId == receipt.Id && !x.IsDeleted);
            Assert.Equal(FileStorageProviders.GoogleDrive, photo.StorageProvider);
            Assert.StartsWith("https://drive.google.com/file/d/", photo.WebUrl);
            receiptId = receipt.Id;
            fileId = Assert.IsType<string>(photo.FileId);
            webUrl = Assert.IsType<string>(photo.WebUrl);
            contentType = photo.ContentType;
            receiptsBefore = await db.Receipts.CountAsync();
            photosBefore = await db.QcPhotos.CountAsync();
            auditsBefore = await db.AuditLogs.CountAsync();
        }

        var expectedBytes = new byte[] { 0xff, 0xd8, 0xff, 0xd9 };
        factory.Storage.ReadBytes[fileId] = expectedBytes;
        var contentUrl = $"/Receipts/{receiptId}/photos/2339/content";
        var detail = await client.GetAsync($"/Receipts/{receiptId}");
        var html = WebUtility.HtmlDecode(await detail.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Contains($"href=\"{webUrl}\"", html);
        Assert.Contains($"src=\"{contentUrl}\"", html);
        Assert.DoesNotContain($"src=\"{webUrl}\"", html);
        Assert.DoesNotContain("could not be translated", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HTTP 500", html, StringComparison.OrdinalIgnoreCase);

        var content = await client.GetAsync(contentUrl);
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal(contentType, content.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expectedBytes, await content.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Receipts/{receiptId + 1}/photos/2339/content")).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            Assert.Equal(receiptsBefore, await db.Receipts.CountAsync());
            Assert.Equal(photosBefore, await db.QcPhotos.CountAsync());
            Assert.Equal(auditsBefore, await db.AuditLogs.CountAsync());
            Assert.False((await db.QcPhotos.SingleAsync(x => x.Id == 2339)).IsDeleted);
        }
    }

    private static MultipartFormDataContent ReceiptForm(
        string receiptNumber,
        int warehouseId = ReceiptPhotoFactory.WarehouseId,
        int roomId = ReceiptPhotoFactory.RoomId,
        int fruitProfileId = ReceiptPhotoFactory.FruitProfileId)
    {
        var content = new MultipartFormDataContent();
        Add(content, "CropYear", "2026");
        Add(content, "ReceivedAt", "2026-08-16T08:30");
        Add(content, "ConfirmCropYear", "true");
        Add(content, "CompuTechReceiptId", receiptNumber);
        Add(content, "ReceiptType", "Truck receipt");
        Add(content, "WarehouseId", warehouseId.ToString());
        Add(content, "RoomId", roomId.ToString());
        Add(content, "FruitProfileId", fruitProfileId.ToString());
        Add(content, "GrowerName", "Receipt Photo Grower");
        Add(content, "GrowerNumber", "1084");
        Add(content, "LotCode", "1084");
        Add(content, "BinCount", "12");
        return content;
    }

    private static void Add(MultipartFormDataContent content, string name, string value) =>
        content.Add(new StringContent(value), name);

    private static void AddPhoto(
        MultipartFormDataContent content,
        int index,
        string fileName,
        string contentType,
        string photoType,
        string photoSource,
        byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, $"stagedPhotos[{index}].PhotoFile", fileName);
        Add(content, $"stagedPhotos[{index}].PhotoType", photoType);
        Add(content, $"stagedPhotos[{index}].PhotoSource", photoSource);
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not find repository file {Path.Combine(pathParts)}.");
    }

    private sealed class ReceiptPhotoFactory : WebApplicationFactory<Program>
    {
        public const int WarehouseId = 9410;
        public const int RoomId = 9411;
        public const int FruitProfileId = 9412;
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private bool seeded;
        public FakePhotoStorage Storage { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            connection.Open();
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:EnsureCreatedOnStartup"] = "true",
                    ["Database:SeedMasterDataOnStartup"] = "false",
                    ["Backups:Enabled"] = "false",
                    ["EbsDailyBinsEmail:Enabled"] = "false",
                    ["Email:Provider"] = "None",
                    ["FileStorage:Provider"] = "Local",
                    ["DataProtection:PersistKeysToFileSystem"] = "false"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<CropQcDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<CropQcDbContext>>();
                services.RemoveAll<CropQcDbContext>();
                services.RemoveAll<IFileStorageService>();
                services.RemoveAll<IHostedService>();
                services.AddDbContext<CropQcDbContext>(options => options.UseSqlite(connection));
                services.AddSingleton<IFileStorageService>(Storage);
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName, _ => { });
            });
        }

        public async Task<HttpClient> CreateOwnerClientAsync()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
            if (!seeded)
            {
                await using var scope = Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
                db.AddRange(
                    new User
                    {
                        Email = ApplicationAreas.OwnerEmail,
                        DisplayName = "Receipt Photo Owner",
                        Domain = "fruitandland.com",
                        IsActive = true,
                        CreatedAt = DateTimeOffset.UtcNow
                    },
                    new Warehouse { Id = WarehouseId, Code = "TPH", Name = "Test Photo Warehouse", IsActive = true },
                    new Room { Id = RoomId, WarehouseId = WarehouseId, Code = "PHOTO-ROOM", Name = "Photo Room", CapacityBins = 1000, IsActive = true },
                    new FruitProfile
                    {
                        Id = FruitProfileId,
                        VarietyCode = "PHOT",
                        Name = "Photo Test",
                        FruitType = "Apple",
                        ProductionType = "Conventional",
                        IsOrganic = false,
                        IsActive = true
                    });
                await db.SaveChangesAsync();
                seeded = true;
            }
            return client;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) connection.Dispose();
        }
    }

    private sealed class ReceiptPhotoPostgreSqlFactory(string connectionString) : WebApplicationFactory<Program>
    {
        public FakePhotoStorage Storage { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "PostgreSql",
                    ["ConnectionStrings:CropQc"] = connectionString,
                    ["Database:EnsureCreatedOnStartup"] = "false",
                    ["Database:SeedMasterDataOnStartup"] = "false",
                    ["Backups:Enabled"] = "false",
                    ["EbsDailyBinsEmail:Enabled"] = "false",
                    ["Email:Provider"] = "None",
                    ["FileStorage:Provider"] = "Local",
                    ["DataProtection:PersistKeysToFileSystem"] = "false"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFileStorageService>();
                services.RemoveAll<IHostedService>();
                services.AddSingleton<IFileStorageService>(Storage);
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName, _ => { });
            });
        }

        public HttpClient CreateOwnerClient()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
            return client;
        }
    }

    private sealed class FakePhotoStorage : IFileStorageService
    {
        public int FailuresRemaining { get; set; }
        public int SaveCount { get; private set; }
        public int DeleteCount { get; private set; }
        public int OpenReadCount { get; private set; }
        public Dictionary<string, byte[]> ReadBytes { get; } = [];
        public List<FileStorageSaveRequest> SavedRequests { get; } = [];

        public string GenerateTargetPath(FileStorageTargetContext context) =>
            $"Photos/{context.CropYear}/{context.WarehouseCode}/Receipt-{context.ReceiptId}/{context.PhotoType}";

        public Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException("Simulated optional photo storage failure.");
            }
            SavedRequests.Add(request);
            var key = $"{request.TargetPath}/{request.FileName}";
            return Task.FromResult(new FileStorageReference(
                "Local",
                key,
                request.TargetPath,
                request.FileName,
                request.ContentType,
                request.FileSizeBytes ?? 0,
                FileId: $"photo-{SaveCount}",
                FolderId: request.TargetPath,
                WebUrl: $"https://example.test/{key}"));
        }

        public Task<FileStorageReference?> GetMetadataAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<FileStorageReference?>(null);

        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            OpenReadCount++;
            return Task.FromResult<Stream?>(ReadBytes.TryGetValue(storageKey, out var bytes)
                ? new MemoryStream(bytes, writable: false)
                : null);
        }

        public Task DeleteOrVoidAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ReceiptPhotoTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.Authorization.ToString().StartsWith(SchemeName, StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, ApplicationAreas.OwnerEmail),
                new Claim(ClaimTypes.Name, "Receipt Photo Owner"),
                new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)
            ], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}

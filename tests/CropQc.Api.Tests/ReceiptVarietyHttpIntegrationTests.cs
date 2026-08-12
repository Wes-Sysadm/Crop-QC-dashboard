using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using CropQc.Data;
using CropQc.Data.Entities;
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

public sealed class ReceiptVarietyHttpIntegrationTests
{
    [Fact]
    public async Task VarietySearch_IsActiveCodeFirstCaseInsensitiveAndNameSearchIsAmbiguous()
    {
        await using var factory = new ReceiptVarietyFactory();
        using var client = await factory.CreateClientAsync(ApplicationAreas.OwnerEmail);

        var exact = await SearchAsync(client, "BART");
        Assert.Equal(2, exact.Count);
        Assert.True(exact.Single(x => x.Code == "BART").ExactCode);
        Assert.False(exact.Single(x => x.Code == "ORBA").ExactCode);

        var lower = await SearchAsync(client, "bart");
        Assert.Equal(exact.Select(x => x.Code), lower.Select(x => x.Code));
        var byName = await SearchAsync(client, "Bartlett");
        Assert.Equal(["BART", "ORBA"], byName.Select(x => x.Code).OrderBy(x => x));
        Assert.Empty(await SearchAsync(client, "TESTINACTIVE"));
        Assert.Empty(await SearchAsync(client, "UNKNOWN"));
    }

    [Fact]
    public async Task QuickAdd_RequiresPermissionAndAntiforgeryAndPersistsOrganicIdentity()
    {
        await using var factory = new ReceiptVarietyFactory();
        using var owner = await factory.CreateClientAsync(ApplicationAreas.OwnerEmail);
        var receiptPage = await owner.GetAsync("/Receipts");
        var receiptHtml = await receiptPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, receiptPage.StatusCode);
        Assert.DoesNotContain("name=\"IsActive\"", receiptHtml);
        var token = await AntiforgeryTokenAsync(owner);

        var organicResponse = await owner.PostAsync("/Receipts/Varieties/QuickAdd", QuickAddForm(token, "NEWO", "New Bartlett", "Organic"));
        Assert.Equal(HttpStatusCode.OK, organicResponse.StatusCode);
        var created = JsonSerializer.Deserialize<QuickAddResult>(
            await organicResponse.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Contains("NEWO - New Bartlett - Organic", created.Label);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var profile = await db.FruitProfiles.SingleAsync(x => x.Id == created.Id);
            Assert.Equal("Organic", profile.ProductionType);
            Assert.True(profile.IsOrganic);
            Assert.True(profile.IsActive);
        }

        var search = await SearchAsync(owner, "newo");
        Assert.Equal(created.Id, Assert.Single(search).Id);

        var duplicate = await owner.PostAsync("/Receipts/Varieties/QuickAdd", QuickAddForm(token, "newo", "Duplicate", "Organic"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var missingToken = await owner.PostAsync("/Receipts/Varieties/QuickAdd", QuickAddForm(null, "NOAF", "No token", "Conventional"));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);

        using var receiver = await factory.CreateClientAsync(ReceiptVarietyFactory.ReceiverEmail);
        var receiverToken = await AntiforgeryTokenAsync(receiver);
        var forbidden = await receiver.PostAsync("/Receipts/Varieties/QuickAdd", QuickAddForm(receiverToken, "NOPE", "Forbidden", "Conventional"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task ConventionalQuickAdd_IsSelectedAndReceiptUsesExactFruitProfile()
    {
        await using var factory = new ReceiptVarietyFactory();
        using var owner = await factory.CreateClientAsync(ApplicationAreas.OwnerEmail);
        var token = await AntiforgeryTokenAsync(owner);
        var add = await owner.PostAsync(
            "/Receipts/Varieties/QuickAdd",
            QuickAddForm(token, "NEWC", "New Conventional", "Conventional", isActive: false));
        var created = JsonSerializer.Deserialize<QuickAddResult>(
            await add.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Contains("NEWC", created.Label);
        Assert.Equal(created.Id, Assert.Single(await SearchAsync(owner, "newc")).Id);

        var receipt = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["CropYear"] = "2026",
            ["ReceivedAt"] = "2026-08-10T08:30",
            ["ConfirmCropYear"] = "true",
            ["CompuTechReceiptId"] = "HTTP-NEWC-1",
            ["ReceiptType"] = "Truck receipt",
            ["WarehouseId"] = ReceiptVarietyFactory.WarehouseId.ToString(),
            ["RoomId"] = ReceiptVarietyFactory.RoomId.ToString(),
            ["FruitProfileId"] = created.Id.ToString(),
            ["GrowerName"] = "HTTP Grower",
            ["GrowerNumber"] = "1084",
            ["LotCode"] = "LOT:1084",
            ["BinCount"] = "10"
        };
        var response = await owner.PostAsync("/Receipts/Create", new FormUrlEncodedContent(receipt));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        var profile = await db.FruitProfiles.SingleAsync(x => x.Id == created.Id);
        Assert.Equal("Conventional", profile.ProductionType);
        Assert.False(profile.IsOrganic);
        Assert.True(profile.IsActive);
        Assert.Equal(created.Id, (await db.Receipts.SingleAsync(x => x.CompuTechReceiptId == "HTTP-NEWC-1")).FruitProfileId);
    }

    [Fact]
    public async Task Dashboard_RendersSeparateBartlettProductionIdentitiesWithoutCombiningBins()
    {
        await using var factory = new ReceiptVarietyFactory();
        using var owner = await factory.CreateClientAsync(ApplicationAreas.OwnerEmail);

        var response = await owner.GetAsync("/?Facility=WP&CropYear=2026");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Bartlett - Conventional - 701 bins", html);
        Assert.Contains("Bartlett - Organic - 232 bins", html);
        Assert.Contains("933", html);
        Assert.DoesNotContain("Organic - Organic", html);
        Assert.DoesNotContain("Conventional - Conventional", html);
        Assert.Contains("Bartlett - Fresh - Organic - 9 bins", html);
        Assert.Contains("Bartlett - Organic status unknown - 4 bins", html);
    }

    [Fact]
    public async Task ReceiptResults_ExposeExactVarietyAndFacilityScopedWarehouseRoomOptions()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_TEST_RECEIVING_FILTERS_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        await using var factory = new ReceivingFilterPostgreSqlFactory(connectionString);
        using var owner = await factory.CreateClientAsync();

        var all = await ReceiptPageAsync(owner, "?Facility=All&AllCropYears=true");
        Assert.Contains("All varieties", all);
        Assert.Contains("All warehouses", all);
        Assert.Contains("All rooms", all);
        Assert.Contains("GALC - Gala - Conventional", all);
        Assert.Contains("GALO - Gala - Organic", all);
        Assert.Contains("FILTER-WP-GALA-C", all);
        Assert.Contains("FILTER-WP-GALA-O", all);
        Assert.Contains("FILTER-WP-FUJI", all);

        var ebs = await ReceiptPageAsync(owner, "?Facility=EBS&AllCropYears=true");
        Assert.Contains(ReceivingFilterPostgreSqlFactory.EbsRoomCode, ebs);
        Assert.DoesNotContain(ReceivingFilterPostgreSqlFactory.WpRoomOneCode, ebs);
        Assert.DoesNotContain(ReceivingFilterPostgreSqlFactory.WpRoomTwoCode, ebs);
        Assert.Contains("FILTER-EBS-GALA-C", ebs);
        Assert.DoesNotContain("FILTER-WP-GALA-C", ebs);

        var wp = await ReceiptPageAsync(owner, "?Facility=WP&AllCropYears=true");
        Assert.Contains(ReceivingFilterPostgreSqlFactory.WpRoomOneCode, wp);
        Assert.Contains(ReceivingFilterPostgreSqlFactory.WpRoomTwoCode, wp);
        Assert.DoesNotContain(ReceivingFilterPostgreSqlFactory.EbsRoomCode, wp);
    }

    [Fact]
    public async Task ReceiptResults_FilterExactOrganicIdentityAndCombineDatabasePredicates()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_TEST_RECEIVING_FILTERS_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        await using var factory = new ReceivingFilterPostgreSqlFactory(connectionString);
        using var owner = await factory.CreateClientAsync();

        var conventional = await ReceiptPageAsync(owner,
            $"?Facility=All&AllCropYears=true&FruitProfileId={ReceivingFilterPostgreSqlFactory.GalaConventionalId}");
        Assert.Contains("FILTER-WP-GALA-C", conventional);
        Assert.Contains("FILTER-EBS-GALA-C", conventional);
        Assert.DoesNotContain("FILTER-WP-GALA-O", conventional);
        Assert.DoesNotContain("FILTER-EBS-GALA-O", conventional);

        var organic = await ReceiptPageAsync(owner,
            $"?Facility=All&AllCropYears=true&FruitProfileId={ReceivingFilterPostgreSqlFactory.GalaOrganicId}");
        Assert.Contains("FILTER-WP-GALA-O", organic);
        Assert.Contains("FILTER-EBS-GALA-O", organic);
        Assert.DoesNotContain("FILTER-WP-GALA-C", organic);
        Assert.DoesNotContain("FILTER-EBS-GALA-C", organic);

        var combined = await ReceiptPageAsync(owner,
            $"?Facility=WP&AllCropYears=true&FruitProfileId={ReceivingFilterPostgreSqlFactory.GalaConventionalId}&WarehouseId={ReceivingFilterPostgreSqlFactory.WpWarehouseId}&RoomId={ReceivingFilterPostgreSqlFactory.WpRoomOneId}&Grower=9040&Lot=FILTER-C");
        Assert.Contains("FILTER-WP-GALA-C", combined);
        Assert.DoesNotContain("FILTER-WP-GALA-O", combined);
        Assert.DoesNotContain("FILTER-EBS-GALA-C", combined);
        Assert.Contains($"value=\"{ReceivingFilterPostgreSqlFactory.GalaConventionalId}\" selected=\"selected\"", combined);
        Assert.Contains($"value=\"{ReceivingFilterPostgreSqlFactory.WpWarehouseId}\" selected=\"selected\"", combined);
        Assert.Contains($"data-selected-room-id=\"{ReceivingFilterPostgreSqlFactory.WpRoomOneId}\"", combined);
        Assert.Contains("value=\"9040\"", combined);
        Assert.Contains("value=\"FILTER-C\"", combined);

        var invalidProfile = await ReceiptPageAsync(owner, "?Facility=All&AllCropYears=true&FruitProfileId=2147483000");
        Assert.DoesNotContain("FILTER-WP-GALA-C", invalidProfile);
        Assert.DoesNotContain("FILTER-WP-GALA-O", invalidProfile);
    }

    [Fact]
    public async Task ReceiptResults_SupportWarehouseOnlyRoomOnlyAndClearIncompatibleRoom()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_TEST_RECEIVING_FILTERS_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        await using var factory = new ReceivingFilterPostgreSqlFactory(connectionString);
        using var owner = await factory.CreateClientAsync();

        var warehouseOnly = await ReceiptPageAsync(owner,
            $"?Facility=All&AllCropYears=true&WarehouseId={ReceivingFilterPostgreSqlFactory.WpWarehouseId}");
        Assert.Contains("FILTER-WP-GALA-C", warehouseOnly);
        Assert.Contains("FILTER-WP-FUJI", warehouseOnly);
        Assert.DoesNotContain("FILTER-EBS-GALA-C", warehouseOnly);

        var roomOnly = await ReceiptPageAsync(owner,
            $"?Facility=All&AllCropYears=true&RoomId={ReceivingFilterPostgreSqlFactory.WpRoomOneId}");
        Assert.Contains("FILTER-WP-GALA-C", roomOnly);
        Assert.Contains("FILTER-WP-GALA-O", roomOnly);
        Assert.DoesNotContain("FILTER-WP-FUJI", roomOnly);
        Assert.Contains($"data-selected-room-id=\"{ReceivingFilterPostgreSqlFactory.WpRoomOneId}\"", roomOnly);

        var warehouseRoom = await ReceiptPageAsync(owner,
            $"?Facility=All&AllCropYears=true&WarehouseId={ReceivingFilterPostgreSqlFactory.WpWarehouseId}&RoomId={ReceivingFilterPostgreSqlFactory.WpRoomTwoId}");
        Assert.Contains("FILTER-WP-FUJI", warehouseRoom);
        Assert.DoesNotContain("FILTER-WP-GALA-C", warehouseRoom);

        var incompatible = await ReceiptPageAsync(owner,
            $"?Facility=All&AllCropYears=true&WarehouseId={ReceivingFilterPostgreSqlFactory.WpWarehouseId}&RoomId={ReceivingFilterPostgreSqlFactory.EbsRoomId}");
        Assert.Contains("FILTER-WP-GALA-C", incompatible);
        Assert.Contains("FILTER-WP-FUJI", incompatible);
        Assert.DoesNotContain($"data-selected-room-id=\"{ReceivingFilterPostgreSqlFactory.EbsRoomId}\"", incompatible);

        var wrongFacility = await ReceiptPageAsync(owner,
            $"?Facility=EBS&AllCropYears=true&RoomId={ReceivingFilterPostgreSqlFactory.WpRoomOneId}");
        Assert.Contains("FILTER-EBS-GALA-C", wrongFacility);
        Assert.DoesNotContain($"data-selected-room-id=\"{ReceivingFilterPostgreSqlFactory.WpRoomOneId}\"", wrongFacility);
    }

    [Fact]
    public async Task ReceiptTypeTabsAndClearLink_PreserveIntentionalContextFilters()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_TEST_RECEIVING_FILTERS_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        await using var factory = new ReceivingFilterPostgreSqlFactory(connectionString);
        using var owner = await factory.CreateClientAsync();
        var html = WebUtility.HtmlDecode(await ReceiptPageAsync(owner,
            $"?Facility=WP&CropYear=2026&FruitProfileId={ReceivingFilterPostgreSqlFactory.GalaConventionalId}&WarehouseId={ReceivingFilterPostgreSqlFactory.WpWarehouseId}&RoomId={ReceivingFilterPostgreSqlFactory.WpRoomOneId}&Grower=9040&Lot=FILTER-C&DateFilter=today&SampleType=Receiving%20Sample"));

        Assert.Contains("name=\"DateFilter\" value=\"today\"", html);
        Assert.Contains("name=\"SampleType\" value=\"Receiving Sample\"", html);
        Assert.Contains("FILTER-WP-GALA-C", html);
        Assert.DoesNotContain("FILTER-WP-GALA-O", html);
        Assert.Contains($"FruitProfileId={ReceivingFilterPostgreSqlFactory.GalaConventionalId}", html);
        Assert.Contains($"WarehouseId={ReceivingFilterPostgreSqlFactory.WpWarehouseId}", html);
        Assert.Contains($"RoomId={ReceivingFilterPostgreSqlFactory.WpRoomOneId}", html);
        Assert.Contains("ReceiptType=Door%20sample", html);
        Assert.Contains("Clear filters", html);
        Assert.Contains("/Receipts?Facility=WP&DateFilter=today&SampleType=Receiving%20Sample", html);
    }

    [Fact]
    public async Task ReceiptFiltering_PerformsNoReceiptOrInventoryWrites()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_TEST_RECEIVING_FILTERS_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        await using var factory = new ReceivingFilterPostgreSqlFactory(connectionString);
        using var owner = await factory.CreateClientAsync();
        var before = factory.DataCounts();

        await ReceiptPageAsync(owner, $"?Facility=All&AllCropYears=true&FruitProfileId={ReceivingFilterPostgreSqlFactory.GalaOrganicId}");
        await ReceiptPageAsync(owner, $"?Facility=All&AllCropYears=true&RoomId={ReceivingFilterPostgreSqlFactory.WpRoomOneId}");
        await ReceiptPageAsync(owner, $"?Facility=WP&AllCropYears=true&WarehouseId={ReceivingFilterPostgreSqlFactory.WpWarehouseId}&RoomId={ReceivingFilterPostgreSqlFactory.WpRoomOneId}&Grower=9040");

        Assert.Equal(before, factory.DataCounts());
    }

    private static async Task<string> ReceiptPageAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync("/Receipts" + query);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("could not be translated", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", html, StringComparison.OrdinalIgnoreCase);
        return html;
    }

    private static async Task<List<SearchResult>> SearchAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync("/Receipts/Varieties/Search?query=" + Uri.EscapeDataString(query));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<List<SearchResult>>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static async Task<string> AntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/Receipts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"");
        Assert.True(match.Success, "Receipt page did not render an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups["token"].Value);
    }

    private static FormUrlEncodedContent QuickAddForm(
        string? token,
        string code,
        string name,
        string productionType,
        bool isActive = true)
    {
        var values = new Dictionary<string, string>
        {
            ["Code"] = code,
            ["Name"] = name,
            ["FruitType"] = "Pear",
            ["ProductionType"] = productionType,
            ["IsActive"] = isActive ? "true" : "false"
        };
        if (token is not null) values["__RequestVerificationToken"] = token;
        return new FormUrlEncodedContent(values);
    }

    private sealed record SearchResult(int Id, string Code, string Label, bool ExactCode);
    private sealed record QuickAddResult(int Id, string Label);

    private sealed class ReceivingFilterPostgreSqlFactory(string connectionString) : WebApplicationFactory<Program>
    {
        public static int WpWarehouseId { get; private set; }
        public static int EbsWarehouseId { get; private set; }
        public const int WpRoomOneId = 8810;
        public const int WpRoomTwoId = 8811;
        public const int EbsRoomId = 8812;
        public const int GalaConventionalId = 8820;
        public const int GalaOrganicId = 8821;
        public const string WpRoomOneCode = "WP-FILTER-ONE";
        public const string WpRoomTwoCode = "WP-FILTER-TWO";
        public const string EbsRoomCode = "EBS-FILTER-ONE";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
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
                    ["DataProtection:PersistKeysToFileSystem"] = "false"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = HeaderAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = HeaderAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(
                        HeaderAuthenticationHandler.SchemeName, _ => { });
            });
        }

        public async Task<HttpClient> CreateClientAsync()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(HeaderAuthenticationHandler.SchemeName);
            client.DefaultRequestHeaders.Add("X-Test-Email", ApplicationAreas.OwnerEmail);

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            await db.Database.EnsureDeletedAsync();
            Assert.True(await db.Database.EnsureCreatedAsync(), "The configured Receiving filter PostgreSQL database must start empty.");

            var wp = await db.Warehouses.SingleAsync(x => x.Code == "WP");
            var ebs = await db.Warehouses.SingleAsync(x => x.Code == "EBS");
            WpWarehouseId = wp.Id;
            EbsWarehouseId = ebs.Id;
            var wpRoomOne = new Room { Id = WpRoomOneId, WarehouseId = wp.Id, Warehouse = wp, Code = WpRoomOneCode, Name = "WP Filter Room One", CapacityBins = 1000, IsActive = true };
            var wpRoomTwo = new Room { Id = WpRoomTwoId, WarehouseId = wp.Id, Warehouse = wp, Code = WpRoomTwoCode, Name = "WP Filter Room Two", CapacityBins = 1000, IsActive = true };
            var ebsRoom = new Room { Id = EbsRoomId, WarehouseId = ebs.Id, Warehouse = ebs, Code = EbsRoomCode, Name = "EBS Filter Room One", CapacityBins = 1000, IsActive = true };
            var conventional = new FruitProfile { Id = GalaConventionalId, VarietyCode = "GALC", Name = "Gala", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false, IsActive = true };
            var organic = new FruitProfile { Id = GalaOrganicId, VarietyCode = "GALO", Name = "Gala", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true, IsActive = true };
            var fuji = new FruitProfile { Id = 8822, VarietyCode = "FUJF", Name = "Fuji", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false, IsActive = true };
            var receivingSampleType = await db.SampleTypes.SingleAsync(x => x.Name == "Receiving Sample");
            var wpConventional = FilterReceipt(8840, "FILTER-WP-GALA-C", wp, wpRoomOne, conventional, "FILTER-C", "Truck receipt");
            var wpOrganic = FilterReceipt(8841, "FILTER-WP-GALA-O", wp, wpRoomOne, organic, "FILTER-O", "Truck receipt");
            var wpFuji = FilterReceipt(8842, "FILTER-WP-FUJI", wp, wpRoomTwo, fuji, "FILTER-F", "Lot sample");
            var ebsConventional = FilterReceipt(8843, "FILTER-EBS-GALA-C", ebs, ebsRoom, conventional, "FILTER-C", "Truck receipt");
            var ebsOrganic = FilterReceipt(8844, "FILTER-EBS-GALA-O", ebs, ebsRoom, organic, "FILTER-O", "Door sample");

            db.AddRange(
                wpRoomOne,
                wpRoomTwo,
                ebsRoom,
                conventional,
                organic,
                fuji,
                wpConventional,
                wpOrganic,
                wpFuji,
                ebsConventional,
                ebsOrganic,
                new QcSample
                {
                    Id = 8850,
                    ReceiptId = wpConventional.Id,
                    Receipt = wpConventional,
                    SampleTypeId = receivingSampleType.Id,
                    SampleType = receivingSampleType,
                    Status = "Complete",
                    StarchStatus = "Complete",
                    PhotoStatus = "Complete",
                    EmailStatus = "Not Sent",
                    SampleTakenAt = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            await db.SaveChangesAsync();
            return client;
        }

        public (int Receipts, int Adjustments, int AuditLogs) DataCounts()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            return (db.Receipts.Count(), db.RoomInventoryAdjustments.Count(), db.AuditLogs.Count());
        }

        private static Receipt FilterReceipt(
            long id,
            string receiptId,
            Warehouse warehouse,
            Room room,
            FruitProfile profile,
            string lot,
            string receiptType) => new()
            {
                Id = id,
                CropYear = 2026,
                ReceivedAt = DateTimeOffset.UtcNow,
                CompuTechReceiptId = receiptId,
                ReceiptType = receiptType,
                WarehouseId = warehouse.Id,
                Warehouse = warehouse,
                RoomId = room.Id,
                Room = room,
                FruitProfileId = profile.Id,
                FruitProfile = profile,
                GrowerName = "Filter Grower 9040",
                GrowerNumber = "9040",
                LotCode = lot,
                BinCount = 10,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                ConcurrencyVersion = 1
            };
    }

    private sealed class ReceiptVarietyFactory : WebApplicationFactory<Program>
    {
        public const string ReceiverEmail = "receiver-http-test@fruitandland.com";
        public const int WarehouseId = 7710;
        public const int RoomId = 7711;
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private bool seeded;

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
                    ["DataProtection:PersistKeysToFileSystem"] = "false"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<CropQcDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<CropQcDbContext>>();
                services.RemoveAll<CropQcDbContext>();
                services.RemoveAll<IRoomInventoryLedgerQueryService>();
                services.RemoveAll<IHostedService>();
                services.AddDbContext<CropQcDbContext>(options => options.UseSqlite(connection));
                services.AddSingleton<IRoomInventoryLedgerQueryService>(new FixedLedgerQuery(
                [
                    DashboardSnapshot(7712, "WP Bartlett Test", 17, "BART-C", "BART", "Bartlett", "Conventional", false, 701),
                    DashboardSnapshot(7712, "WP Bartlett Test", 19, "BART-O", "ORBA", "Organic Bartlett", "Organic", true, 232),
                    DashboardSnapshot(7713, "WP Identity Test", 17, "BART-F", "BART", "Bartlett", "Fresh", true, 9),
                    DashboardSnapshot(7713, "WP Identity Test", 17, "BART-U", "BART", "Bartlett", "", null, 4)
                ]));
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = HeaderAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = HeaderAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(
                        HeaderAuthenticationHandler.SchemeName, _ => { });
            });
        }

        public async Task<HttpClient> CreateClientAsync(string email)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(HeaderAuthenticationHandler.SchemeName);
            client.DefaultRequestHeaders.Add("X-Test-Email", email);
            if (!seeded)
            {
                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
                var role = new Role
                {
                    Name = "Receipt-only HTTP test",
                    NormalizedName = BuiltInRoleNames.Normalize("Receipt-only HTTP test"),
                    IsActive = true
                };
                foreach (var area in ApplicationAreas.All)
                    role.PageAccesses.Add(new RolePageAccess
                    {
                        AreaKey = area.Key,
                        AccessLevel = area.Key == ApplicationAreas.Receipts
                            ? nameof(PageAccessLevel.Create)
                            : nameof(PageAccessLevel.None),
                        UpdatedAt = DateTimeOffset.UtcNow
                    });
                var receiver = new User
                {
                    Email = ReceiverEmail,
                    DisplayName = "Receiver",
                    Domain = "fruitandland.com",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                receiver.UserRoles.Add(new UserRole { Role = role });
                var wp = await db.Warehouses.SingleAsync(x => x.Code == "WP");
                var bart = await db.FruitProfiles.SingleAsync(x => x.VarietyCode == "BART");
                var orba = await db.FruitProfiles.SingleAsync(x => x.VarietyCode == "ORBA");
                var dashboardRoom = new Room
                {
                    Id = 7712,
                    WarehouseId = wp.Id,
                    Code = "WP-BART-TEST",
                    Name = "WP Bartlett Test",
                    CapacityBins = 1200,
                    IsActive = true
                };
                var conventionalReceipt = new Receipt
                {
                    Id = 7730,
                    CropYear = 2026,
                    ReceivedAt = DateTimeOffset.UtcNow,
                    CompuTechReceiptId = "DASH-BART-C",
                    WarehouseId = wp.Id,
                    RoomId = dashboardRoom.Id,
                    FruitProfileId = bart.Id,
                    GrowerName = "Conventional Grower",
                    GrowerNumber = "1001",
                    LotCode = "BART-C",
                    BinCount = 701,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    ConcurrencyVersion = 1
                };
                var organicReceipt = new Receipt
                {
                    Id = 7731,
                    CropYear = 2026,
                    ReceivedAt = DateTimeOffset.UtcNow,
                    CompuTechReceiptId = "DASH-BART-O",
                    WarehouseId = wp.Id,
                    RoomId = dashboardRoom.Id,
                    FruitProfileId = orba.Id,
                    GrowerName = "Organic Grower",
                    GrowerNumber = "1002",
                    LotCode = "BART-O",
                    BinCount = 232,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    ConcurrencyVersion = 1
                };
                db.AddRange(
                    new Warehouse { Id = WarehouseId, Code = "TWP", Name = "Test WP", IsActive = true },
                    new Room { Id = RoomId, WarehouseId = WarehouseId, Code = "WP-TEST", Name = "WP Test", CapacityBins = 1000, IsActive = true },
                    new FruitProfile { Id = 7722, VarietyCode = "TESTINACTIVE", Name = "Inactive test profile", FruitType = "Pear", ProductionType = "Conventional", IsOrganic = false, IsActive = false },
                    dashboardRoom,
                    new Room
                    {
                        Id = 7713,
                        WarehouseId = wp.Id,
                        Code = "WP-IDENTITY-TEST",
                        Name = "WP Identity Test",
                        CapacityBins = 100,
                        IsActive = true
                    },
                    conventionalReceipt,
                    organicReceipt,
                    new RoomInventoryAdjustment
                    {
                        ReceiptId = conventionalReceipt.Id,
                        WarehouseId = wp.Id,
                        RoomId = dashboardRoom.Id,
                        FruitProfileId = bart.Id,
                        GrowerName = "Conventional Grower",
                        LotNumber = "BART-C",
                        VarietyCode = "BART",
                        CropYear = 2026,
                        ChangeAmount = 701,
                        NewBinCount = 701,
                        AdjustmentType = "ReceiptAdd",
                        AdjustmentAt = DateTimeOffset.UtcNow,
                        CreatedAt = DateTimeOffset.UtcNow,
                        InventoryInvariantVersion = 1,
                        InventoryOperationKey = "dashboard-bart-conventional"
                    },
                    new RoomInventoryAdjustment
                    {
                        ReceiptId = organicReceipt.Id,
                        WarehouseId = wp.Id,
                        RoomId = dashboardRoom.Id,
                        FruitProfileId = orba.Id,
                        GrowerName = "Organic Grower",
                        LotNumber = "BART-O",
                        VarietyCode = "ORBA",
                        CropYear = 2026,
                        ChangeAmount = 232,
                        NewBinCount = 232,
                        AdjustmentType = "ReceiptAdd",
                        AdjustmentAt = DateTimeOffset.UtcNow,
                        CreatedAt = DateTimeOffset.UtcNow,
                        InventoryInvariantVersion = 1,
                        InventoryOperationKey = "dashboard-bart-organic"
                    },
                    receiver);
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

        private static RoomInventoryLedgerSnapshot DashboardSnapshot(
            int roomId,
            string room,
            int fruitProfileId,
            string lot,
            string variety,
            string varietyName,
            string productionType,
            bool? isOrganic,
            int currentBins) => new(
                1,
                "WP",
                roomId,
                room,
                "",
                2026,
                null,
                fruitProfileId,
                isOrganic == true ? "Organic Grower" : "Conventional Grower",
                isOrganic == true ? "1002" : "1001",
                lot,
                null,
                variety,
                variety,
                varietyName,
                "Pear",
                productionType,
                isOrganic,
                "",
                currentBins,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                currentBins,
                1,
                DateTimeOffset.Parse("2026-08-01T17:00:00Z"),
                DateTimeOffset.Parse("2026-08-01T17:00:00Z"),
                9000 + currentBins,
                "HTTP dashboard rendering test");

    }

    private sealed class FixedLedgerQuery(IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots) : IRoomInventoryLedgerQueryService
    {
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
            int? warehouseId,
            IReadOnlyCollection<int>? roomIds,
            CancellationToken cancellationToken) =>
            GetSnapshotsAsync(warehouseId, roomIds, null, cancellationToken);

        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
            int? warehouseId,
            IReadOnlyCollection<int>? roomIds,
            int? fruitProfileId,
            CancellationToken cancellationToken)
        {
            var filtered = snapshots
                .Where(x => warehouseId is null || x.WarehouseId == warehouseId)
                .Where(x => roomIds is not { Count: > 0 } || roomIds.Contains(x.RoomId))
                .Where(x => fruitProfileId is null || x.FruitProfileId == fruitProfileId)
                .ToList();
            return Task.FromResult<IReadOnlyList<RoomInventoryLedgerSnapshot>>(filtered);
        }
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ReceiptVarietyHttpIntegration";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var email = Request.Headers["X-Test-Email"].FirstOrDefault() ?? ApplicationAreas.OwnerEmail;
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Email, email)], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}

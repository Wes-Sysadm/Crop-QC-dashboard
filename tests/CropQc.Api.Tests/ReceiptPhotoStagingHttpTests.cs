using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

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

        var response = await client.PostAsync("/Receipts/Create", await ReceiptFormAsync(client, "STAGED-ZERO"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Samples/", response.Headers.Location?.OriginalString);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        var receipt = await db.Receipts.SingleAsync(x => x.CompuTechReceiptId == "STAGED-ZERO");
        var sample = await db.QcSamples.Include(x => x.SampleType).SingleAsync(x => x.ReceiptId == receipt.Id);
        Assert.Equal("Receiving Sample", sample.SampleType.Name);
        Assert.Empty(await db.QcPhotos.ToListAsync());
        Assert.Equal(0, factory.Storage.SaveCount);
    }

    [Fact]
    public async Task TwoStagedPhotos_SaveOnceEachAgainstExactNewReceipt_AndCanBeRemovedSafely()
    {
        await using var factory = new ReceiptPhotoFactory();
        using var client = await factory.CreateOwnerClientAsync();
        var content = await ReceiptFormAsync(client, "STAGED-TWO");
        AddPhoto(content, 0, "truck.jpg", "image/jpeg", "BinTruck", "OBSBOT Tiny 2 Lite", TestPhotoBytes("truck.jpg"));
        AddPhoto(content, 1, "top.png", "image/png", "TopOfTruck", "Upload File", TestPhotoBytes("top.png"));

        var response = await client.PostAsync("/Receipts/Create", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Samples/", response.Headers.Location?.OriginalString);
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
        Assert.Equal(4, factory.Storage.SaveCount);
        Assert.Equal(4, factory.Storage.SavedRequests.Count);

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
        Assert.Equal(1, factory.Storage.DeleteCount);
    }

    [Fact]
    public async Task ReceiptPhoto_CanMoveToSampleAndBackWithoutStorageMutationOrOrphaning()
    {
        await using var factory = new ReceiptPhotoFactory();
        using var client = await factory.CreateOwnerClientAsync();
        var content = await ReceiptFormAsync(client, "MOVE-PHOTO");
        AddPhoto(content, 0, "truck.jpg", "image/jpeg", "BinTruck", "Upload File", TestPhotoBytes("truck.jpg"));
        var create = await client.PostAsync("/Receipts/Create", content);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        long sampleId;
        long receiptId;
        long photoId;
        string? fileId;
        PhotoOrientationSnapshot orientationBefore;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var receipt = await db.Receipts.SingleAsync(x => x.CompuTechReceiptId == "MOVE-PHOTO");
            receiptId = receipt.Id;
            sampleId = (await db.QcSamples.SingleAsync(x => x.ReceiptId == receipt.Id)).Id;
            var photo = await db.QcPhotos.SingleAsync(x => x.ReceiptId == receipt.Id);
            photoId = photo.Id;
            fileId = photo.FileId;
            orientationBefore = Snapshot(photo, factory.Storage);
        }

        var token = await AntiforgeryTokenAsync(client, $"/Samples/{sampleId}");
        var moved = await ReclassifyAsync(client, sampleId, photoId, "Hectre", token);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var photo = await db.QcPhotos.SingleAsync(x => x.Id == photoId);
            Assert.Equal("Hectre", photo.PhotoType);
            Assert.Equal(sampleId, photo.QcSampleId);
            Assert.Null(photo.ReceiptId);
            Assert.Equal(fileId, photo.FileId);
            AssertSnapshot(orientationBefore, photo, factory.Storage);
            Assert.Single(await db.AuditLogs.Where(x => x.Action == "reclassify-photo").ToListAsync());
        }

        token = await AntiforgeryTokenAsync(client, $"/Samples/{sampleId}");
        var restored = await ReclassifyAsync(client, sampleId, photoId, "BinTruck", token);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var photo = await db.QcPhotos.SingleAsync(x => x.Id == photoId);
            Assert.Equal("BinTruck", photo.PhotoType);
            Assert.Equal(receiptId, photo.ReceiptId);
            Assert.Null(photo.QcSampleId);
            Assert.Equal(fileId, photo.FileId);
            AssertSnapshot(orientationBefore, photo, factory.Storage);
            Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.Action == "reclassify-photo"));
        }
        Assert.Equal(2, factory.Storage.SaveCount);
        Assert.Equal(0, factory.Storage.DeleteCount);
    }

    [Fact]
    public async Task ReceiptPhoto_RotationPreservesOriginal_IsAudited_CacheBusted_AndRejectsStaleOrForgedRequests()
    {
        await using var factory = new ReceiptPhotoFactory();
        using var client = await factory.CreateOwnerClientAsync();
        var content = await ReceiptFormAsync(client, "ROTATE-PHOTO");
        var exactUploadBytes = ExifMarkerJpegBytes(6);
        var exactUploadHash = SHA256.HashData(exactUploadBytes);
        AddPhoto(content, 0, "phone.jpg", "image/jpeg", "BinTruck", "Upload File", exactUploadBytes);
        var create = await client.PostAsync("/Receipts/Create", content);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        long receiptId;
        long sampleId;
        long photoId;
        string originalKey;
        byte[] originalHash;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var receipt = await db.Receipts.SingleAsync(x => x.CompuTechReceiptId == "ROTATE-PHOTO");
            var photo = await db.QcPhotos.SingleAsync(x => x.ReceiptId == receipt.Id);
            receiptId = receipt.Id;
            sampleId = (await db.QcSamples.SingleAsync(x => x.ReceiptId == receipt.Id)).Id;
            photoId = photo.Id;
            originalKey = photo.FileId!;
            originalHash = SHA256.HashData(factory.Storage.ReadBytes[originalKey]);
            Assert.Equal(exactUploadHash, originalHash);
            Assert.Equal(6, photo.OriginalExifOrientation);
            Assert.Equal(1, photo.PresentationRevision);
            Assert.Equal(0, photo.ManualRotationQuarterTurns);
        }

        var initialPresentationUrl = $"/Receipts/{receiptId}/photos/{photoId}/content?v=1";
        var detailHtml = await RenderPhotoGroupsAsync(factory.Services, initialPresentationUrl);
        AssertPresentationLink(detailHtml, initialPresentationUrl);
        Assert.DoesNotContain("href=\"https://example.test/", detailHtml);
        await AssertCornerOrderAsync(await client.GetByteArrayAsync(initialPresentationUrl), "CADB");

        var token = await AntiforgeryTokenAsync(client, create.Headers.Location!.OriginalString);
        var rotated = await RotateReceiptAsync(client, receiptId, photoId, "right", 1, token);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var photo = await db.QcPhotos.SingleAsync(x => x.Id == photoId);
            Assert.Equal(1, photo.ManualRotationQuarterTurns);
            Assert.Equal(2, photo.PresentationRevision);
            Assert.NotNull(photo.PresentationStorageKey);
            Assert.Equal(originalHash, SHA256.HashData(factory.Storage.ReadBytes[originalKey]));
            var audit = await db.AuditLogs.SingleAsync(x => x.Action == "rotate-photo-right" && x.EntityKey == photoId.ToString());
            Assert.Contains("OldManualRotationQuarterTurns", audit.AfterValuesJson);
            Assert.Contains("NewManualRotationQuarterTurns", audit.AfterValuesJson);
        }
        Assert.Equal(3, factory.Storage.SaveCount);
        Assert.Equal(1, factory.Storage.DeleteCount);

        var rotatedPresentationUrl = $"/Receipts/{receiptId}/photos/{photoId}/content?v=2";
        await AssertCornerOrderAsync(await client.GetByteArrayAsync(rotatedPresentationUrl), "DCBA");
        var rotatedDetail = await RenderPhotoGroupsAsync(factory.Services, rotatedPresentationUrl, revision: 2);
        AssertPresentationLink(rotatedDetail, rotatedPresentationUrl);

        var stale = await RotateReceiptAsync(client, receiptId, photoId, "right", 1, token);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(3, factory.Storage.SaveCount);

        var forged = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Direction"] = "left",
            ["ExpectedPresentationRevision"] = "2"
        });
        var rejected = await client.PostAsync($"/Receipts/{receiptId}/photos/{photoId}/rotate", forged);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var unauthenticated = await anonymous.PostAsync(
            $"/Receipts/{receiptId}/photos/{photoId}/rotate",
            new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        PhotoOrientationSnapshot rotatedSnapshot;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            rotatedSnapshot = Snapshot(await db.QcPhotos.SingleAsync(x => x.Id == photoId), factory.Storage);
        }
        var reclassifyToken = await AntiforgeryTokenAsync(client, $"/Samples/{sampleId}");
        var savesBeforeReclassify = factory.Storage.SaveCount;
        var movedAfterRotation = await ReclassifyAsync(client, sampleId, photoId, "Hectre", reclassifyToken);
        Assert.Equal(HttpStatusCode.OK, movedAfterRotation.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            AssertSnapshot(rotatedSnapshot, await db.QcPhotos.SingleAsync(x => x.Id == photoId), factory.Storage);
        }
        Assert.Equal(savesBeforeReclassify, factory.Storage.SaveCount);
    }

    [Fact]
    public async Task LegacyFieldSamplePhoto_CanBeRotatedLazily_OnlyThroughItsExactParent()
    {
        await using var factory = new ReceiptPhotoFactory();
        using var client = await factory.CreateOwnerClientAsync();
        var original = TestPhotoBytes("legacy-field.jpg");
        factory.Storage.ReadBytes["legacy-field"] = original;
        long sampleId;
        long photoId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var sampleType = await db.SampleTypes.SingleAsync(x => x.Name == "Field Sample");
            var sample = new QcSample
            {
                SampleType = sampleType,
                FieldSampleFruitProfileId = ReceiptPhotoFactory.FruitProfileId,
                FieldSampleGrowerName = "Field Grower",
                Status = "In Progress",
                StarchStatus = "Not Required",
                PhotoStatus = "Optional Photos Attached",
                EmailStatus = "Not Sent",
                SampleTakenAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            };
            var photo = new QcPhoto
            {
                QcSample = sample,
                PhotoType = "Hectre",
                PhotoSource = "Legacy Upload",
                FileName = "legacy-field.jpg",
                ContentType = "image/jpeg",
                FileSizeBytes = original.Length,
                StorageProvider = "Local",
                FileId = "legacy-field",
                SharePointDriveId = "legacy",
                SharePointItemId = "legacy-field",
                CapturedAt = DateTimeOffset.UtcNow
            };
            db.Add(photo);
            await db.SaveChangesAsync();
            sampleId = sample.Id;
            photoId = photo.Id;
        }

        var token = await AntiforgeryTokenAsync(client, "/Receipts");
        var wrongParent = await RotateFieldAsync(client, sampleId + 999, photoId, "left", 0, token);
        Assert.Equal(HttpStatusCode.BadRequest, wrongParent.StatusCode);
        Assert.Equal(0, factory.Storage.SaveCount);

        var rotated = await RotateFieldAsync(client, sampleId, photoId, "left", 0, token);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        var updated = await verifyDb.QcPhotos.SingleAsync(x => x.Id == photoId);
        Assert.Equal(3, updated.ManualRotationQuarterTurns);
        Assert.Equal(1, updated.PresentationRevision);
        Assert.NotNull(updated.PresentationStorageKey);
        Assert.Equal(SHA256.HashData(original), SHA256.HashData(factory.Storage.ReadBytes["legacy-field"]));
        Assert.Contains(await verifyDb.AuditLogs.ToListAsync(), x => x.Action == "rotate-photo-left" && x.EntityKey == photoId.ToString());
    }

    [Fact]
    public async Task QcSampleAndFieldSample_PrimaryFullImageUsesProtectedRotatedPresentation()
    {
        await using var factory = new ReceiptPhotoFactory();
        using var client = await factory.CreateOwnerClientAsync();
        var create = await client.PostAsync("/Receipts/Create", await ReceiptFormAsync(client, "PRESENTATION-CONTEXTS"));
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        long qcSampleId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var receipt = await db.Receipts.SingleAsync(x => x.CompuTechReceiptId == "PRESENTATION-CONTEXTS");
            qcSampleId = (await db.QcSamples.SingleAsync(x => x.ReceiptId == receipt.Id)).Id;
        }

        var source = ExifMarkerJpegBytes(6);
        var sourceHash = SHA256.HashData(source);
        await UploadPhotoAsync(client, $"/Samples/{qcSampleId}/photos", $"/Samples/{qcSampleId}", "PhotoFile", "Hectre", source);
        long qcPhotoId;
        string qcOriginalKey;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var photo = await db.QcPhotos.SingleAsync(x => x.QcSampleId == qcSampleId);
            qcPhotoId = photo.Id;
            qcOriginalKey = photo.FileId!;
            Assert.Equal(sourceHash, SHA256.HashData(factory.Storage.ReadBytes[qcOriginalKey]));
        }

        var qcInitialUrl = $"/Samples/{qcSampleId}/photos/{qcPhotoId}/content?v=1";
        var qcHtml = await RenderPhotoGroupsAsync(factory.Services, qcInitialUrl, qcSampleId);
        AssertPresentationLink(qcHtml, qcInitialUrl);
        await AssertCornerOrderAsync(await client.GetByteArrayAsync(qcInitialUrl), "CADB");
        var qcToken = await AntiforgeryTokenAsync(client, $"/Samples/{qcSampleId}");
        var qcRotated = await RotateSampleAsync(client, qcSampleId, qcPhotoId, "right", 1, qcToken);
        Assert.Equal(HttpStatusCode.OK, qcRotated.StatusCode);
        var qcRotatedUrl = $"/Samples/{qcSampleId}/photos/{qcPhotoId}/content?v=2";
        await AssertCornerOrderAsync(await client.GetByteArrayAsync(qcRotatedUrl), "DCBA");
        Assert.Equal(sourceHash, SHA256.HashData(factory.Storage.ReadBytes[qcOriginalKey]));

        long fieldSampleId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IFieldSampleService>();
            var created = await service.CreateAsync(new FieldSampleCreateForm
            {
                OrchardName = "Orientation Orchard",
                GrowerNumber = "1084",
                BlockName = "Orientation Block",
                FruitProfileId = ReceiptPhotoFactory.FruitProfileId,
                ConfirmCreateNewBlock = true,
                SampleTakenAt = DateTimeOffset.UtcNow
            }, OwnerPrincipal(), CancellationToken.None);
            Assert.Null(created.Error);
            fieldSampleId = created.SampleId!.Value;
        }

        await UploadPhotoAsync(client, $"/FieldSamples/{fieldSampleId}/photos", "/Receipts", "photoFiles", "SampleBeforeCutting", source);
        long fieldPhotoId;
        string fieldOriginalKey;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var photo = await db.QcPhotos.SingleAsync(x => x.QcSampleId == fieldSampleId);
            fieldPhotoId = photo.Id;
            fieldOriginalKey = photo.FileId!;
            Assert.Equal(sourceHash, SHA256.HashData(factory.Storage.ReadBytes[fieldOriginalKey]));
        }

        var fieldInitialUrl = $"/FieldSamples/{fieldSampleId}/photos/{fieldPhotoId}/content?v=1";
        var fieldHtml = await RenderPhotoGroupsAsync(factory.Services, fieldInitialUrl, fieldSampleId);
        AssertPresentationLink(fieldHtml, fieldInitialUrl);
        await AssertCornerOrderAsync(await client.GetByteArrayAsync(fieldInitialUrl), "CADB");
        var fieldToken = await AntiforgeryTokenAsync(client, "/Receipts");
        var fieldRotated = await RotateFieldAsync(client, fieldSampleId, fieldPhotoId, "right", 1, fieldToken);
        Assert.Equal(HttpStatusCode.OK, fieldRotated.StatusCode);
        var fieldRotatedUrl = $"/FieldSamples/{fieldSampleId}/photos/{fieldPhotoId}/content?v=2";
        await AssertCornerOrderAsync(await client.GetByteArrayAsync(fieldRotatedUrl), "DCBA");
        Assert.Equal(sourceHash, SHA256.HashData(factory.Storage.ReadBytes[fieldOriginalKey]));
    }

    [Fact]
    public async Task ViewOnlyUser_CanViewProtectedPresentations_ButCannotRotateQcOrFieldPhotos()
    {
        await using var factory = new ReceiptPhotoFactory();
        using var owner = await factory.CreateOwnerClientAsync();
        var create = await owner.PostAsync("/Receipts/Create", await ReceiptFormAsync(owner, "VIEW-ONLY-PHOTOS"));
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        long qcSampleId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var receipt = await db.Receipts.SingleAsync(x => x.CompuTechReceiptId == "VIEW-ONLY-PHOTOS");
            qcSampleId = (await db.QcSamples.SingleAsync(x => x.ReceiptId == receipt.Id)).Id;
        }
        var source = ExifMarkerJpegBytes(6);
        await UploadPhotoAsync(owner, $"/Samples/{qcSampleId}/photos", $"/Samples/{qcSampleId}", "PhotoFile", "Hectre", source);

        long fieldSampleId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IFieldSampleService>();
            var created = await service.CreateAsync(new FieldSampleCreateForm
            {
                OrchardName = "View Orchard",
                GrowerNumber = "1084",
                BlockName = "View Block",
                FruitProfileId = ReceiptPhotoFactory.FruitProfileId,
                ConfirmCreateNewBlock = true,
                SampleTakenAt = DateTimeOffset.UtcNow
            }, OwnerPrincipal(), CancellationToken.None);
            Assert.Null(created.Error);
            fieldSampleId = created.SampleId!.Value;
        }
        await UploadPhotoAsync(owner, $"/FieldSamples/{fieldSampleId}/photos", "/Receipts", "photoFiles", "SampleBeforeCutting", source);

        long qcPhotoId;
        long fieldPhotoId;
        PhotoOrientationSnapshot qcBefore;
        PhotoOrientationSnapshot fieldBefore;
        int auditsBefore;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var qcPhoto = await db.QcPhotos.SingleAsync(x => x.QcSampleId == qcSampleId);
            var fieldPhoto = await db.QcPhotos.SingleAsync(x => x.QcSampleId == fieldSampleId);
            qcPhotoId = qcPhoto.Id;
            fieldPhotoId = fieldPhoto.Id;
            qcBefore = Snapshot(qcPhoto, factory.Storage);
            fieldBefore = Snapshot(fieldPhoto, factory.Storage);
            auditsBefore = await db.AuditLogs.CountAsync(x => x.Action.StartsWith("rotate-photo-"));
        }
        var savesBefore = factory.Storage.SaveCount;

        using var viewer = await factory.CreateViewOnlyClientAsync();
        var qcPage = await viewer.GetAsync($"/Samples/{qcSampleId}");
        var qcHtml = await qcPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, qcPage.StatusCode);
        Assert.DoesNotContain("aria-label=\"Rotate photo", qcHtml);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/Samples/{qcSampleId}/photos/{qcPhotoId}/content?v=1")).StatusCode);
        var qcToken = await AntiforgeryTokenAsync(viewer, $"/Samples/{qcSampleId}");
        Assert.Equal(HttpStatusCode.Forbidden, (await RotateSampleAsync(viewer, qcSampleId, qcPhotoId, "right", 1, qcToken)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/FieldSamples/{fieldSampleId}/photos/{fieldPhotoId}/content?v=1")).StatusCode);
        var fieldToken = await AntiforgeryTokenAsync(viewer, "/Receipts");
        Assert.Equal(HttpStatusCode.Forbidden, (await RotateFieldAsync(viewer, fieldSampleId, fieldPhotoId, "right", 1, fieldToken)).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            AssertSnapshot(qcBefore, await db.QcPhotos.SingleAsync(x => x.Id == qcPhotoId), factory.Storage);
            AssertSnapshot(fieldBefore, await db.QcPhotos.SingleAsync(x => x.Id == fieldPhotoId), factory.Storage);
            Assert.Equal(auditsBefore, await db.AuditLogs.CountAsync(x => x.Action.StartsWith("rotate-photo-")));
        }
        Assert.Equal(savesBefore, factory.Storage.SaveCount);
    }

    [Fact]
    public void SavedReceiptPhotoPresentation_KeepsThumbnailAndUsesAccessibleContextAwareTrashcan()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var groups = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_PhotoGroups.cshtml"));

        Assert.Contains("var isImage = photo.ContentType.StartsWith(\"image/\"", service);
        Assert.Contains("$\"/Receipts/{contentReceiptId}/photos/{photo.Id}/content?v={photo.PresentationRevision}\"", service);
        Assert.Contains("$\"/Receipts/{receiptId}/photos/{photo.Id}/remove\"", service);
        Assert.Contains("photo.ThumbnailUrl", groups);
        Assert.Contains("photo.PresentationUrl", groups);
        Assert.Contains("<a href=\"@primaryViewUrl\"", groups);
        Assert.Contains("data-photo-presentation-link", groups);
        Assert.DoesNotContain("<a href=\"@photo.WebUrl\"", groups);
        Assert.Contains("<img src=\"@thumbnailUrl\"", groups);
        Assert.DoesNotContain("<img src=\"@photo.WebUrl\"", groups);
        Assert.Contains("@photo.FileName", groups);
        Assert.Contains("aria-label=\"Remove photo\"", groups);
        Assert.Contains("title=\"Remove photo\"", groups);
        Assert.Contains("Remove this receipt photo?", groups);
        Assert.Contains("Remove this photo from the sample?", groups);
        Assert.Contains("DisplayAsThumbnail", groups);
        Assert.Contains("loading=\"lazy\"", groups);
        Assert.Contains("aria-label=\"Rotate photo left\"", groups);
        Assert.Contains("aria-label=\"Rotate photo right\"", groups);
        Assert.Contains("ExpectedPresentationRevision", groups);
        Assert.Contains("?v={photo.PresentationRevision}", service);
    }

    [Fact]
    public async Task SavedReceiptPhoto_UsesProtectedContentEndpoint_AndPreservesDriveLinkWithoutWrites()
    {
        await using var factory = new ReceiptPhotoFactory();
        using var client = await factory.CreateOwnerClientAsync();
        var create = await client.PostAsync("/Receipts/Create", await ReceiptFormAsync(client, "PRIVATE-THUMB"));
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

        factory.Storage.ReadBytes.TryRemove("private-drive-file", out _);
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
        var content = await ReceiptFormAsync(client, "STAGED-FAIL");
        AddPhoto(content, 0, "truck.webp", "image/webp", "BinTruck", "Upload File", TestPhotoBytes("truck.webp"));

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
        var content = await ReceiptFormAsync(client, "");
        AddPhoto(content, 0, "truck.jpg", "image/jpeg", "BinTruck", "Upload File", TestPhotoBytes("truck.jpg"));

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
        var content = await ReceiptFormAsync(client, receiptNumber, warehouseId, roomId, fruitProfileId);
        AddPhoto(content, 0, "truck.jpg", "image/jpeg", "BinTruck", "OBSBOT Tiny 2 Lite", TestPhotoBytes("truck.jpg"));
        AddPhoto(content, 1, "top.jpg", "image/jpeg", "TopOfTruck", "OBSBOT Tiny 2 Lite", TestPhotoBytes("top.jpg"));
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
        Assert.Equal(4, factory.Storage.SaveCount);
        Assert.Equal(1, factory.Storage.DeleteCount);
    }

    [Fact]
    public async Task AuthenticatedPostgreSql_ReceiptPhotoReclassificationPreservesStorageAndIsAudited_WhenConfigured()
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

        var receiptNumber = $"CODEX-MOVE-{Guid.NewGuid():N}"[..30];
        var content = await ReceiptFormAsync(client, receiptNumber, warehouseId, roomId, fruitProfileId);
        AddPhoto(content, 0, "truck.jpg", "image/jpeg", "BinTruck", "Upload File", TestPhotoBytes("truck.jpg"));
        var create = await client.PostAsync("/Receipts/Create", content);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        long sampleId;
        long receiptId;
        long photoId;
        string? fileId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var receipt = await db.Receipts.SingleAsync(x => x.CompuTechReceiptId == receiptNumber);
            receiptId = receipt.Id;
            sampleId = (await db.QcSamples.SingleAsync(x => x.ReceiptId == receipt.Id)).Id;
            var photo = await db.QcPhotos.SingleAsync(x => x.ReceiptId == receipt.Id);
            photoId = photo.Id;
            fileId = photo.FileId;
        }

        var token = await AntiforgeryTokenAsync(client, $"/Samples/{sampleId}");
        var moved = await ReclassifyAsync(client, sampleId, photoId, "Hectre", token);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var photo = await db.QcPhotos.SingleAsync(x => x.Id == photoId);
            Assert.Equal("Hectre", photo.PhotoType);
            Assert.Equal(sampleId, photo.QcSampleId);
            Assert.Null(photo.ReceiptId);
            Assert.Equal(fileId, photo.FileId);
            Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == "reclassify-photo" && x.EntityKey == photoId.ToString()));
        }

        token = await AntiforgeryTokenAsync(client, $"/Samples/{sampleId}");
        var restored = await ReclassifyAsync(client, sampleId, photoId, "BinTruck", token);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var photo = await db.QcPhotos.SingleAsync(x => x.Id == photoId);
            Assert.Equal("BinTruck", photo.PhotoType);
            Assert.Equal(receiptId, photo.ReceiptId);
            Assert.Null(photo.QcSampleId);
            Assert.Equal(fileId, photo.FileId);
            Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.Action == "reclassify-photo" && x.EntityKey == photoId.ToString()));
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
        var presentationUrl = $"{contentUrl}?v=0";
        var detail = await client.GetAsync($"/Receipts/{receiptId}");
        var html = WebUtility.HtmlDecode(await detail.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Contains($"href=\"{presentationUrl}\"", html);
        Assert.DoesNotContain($"href=\"{webUrl}\"", html);
        Assert.Contains($"src=\"{presentationUrl}\"", html);
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

    private static async Task<MultipartFormDataContent> ReceiptFormAsync(
        HttpClient client,
        string receiptNumber,
        int warehouseId = ReceiptPhotoFactory.WarehouseId,
        int roomId = ReceiptPhotoFactory.RoomId,
        int fruitProfileId = ReceiptPhotoFactory.FruitProfileId)
    {
        var page = await client.GetAsync("/Receipts");
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"");
        Assert.True(match.Success, "The Receipt form must render an antiforgery token.");
        var content = ReceiptForm(receiptNumber, warehouseId, roomId, fruitProfileId);
        Add(content, "__RequestVerificationToken", match.Groups["token"].Value);
        return content;
    }

    private static async Task<string> AntiforgeryTokenAsync(HttpClient client, string path)
    {
        var page = await client.GetAsync(path);
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"");
        Assert.True(match.Success, $"The page {path} must render an antiforgery token.");
        return match.Groups["token"].Value;
    }

    private static async Task<HttpResponseMessage> ReclassifyAsync(
        HttpClient client,
        long sampleId,
        long photoId,
        string targetPhotoType,
        string antiforgeryToken)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["TargetPhotoType"] = targetPhotoType,
            ["__RequestVerificationToken"] = antiforgeryToken
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/Samples/{sampleId}/photos/{photoId}/reclassify")
        {
            Content = form
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task AuthenticatedPostgreSql_ConcurrentSameRevisionRotation_CommitsOnceAndReturnsCurrentRevision_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_PHOTO_ORIENTATION_CONCURRENCY_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        await using var factory = new ReceiptPhotoPostgreSqlFactory(connectionString);
        using var firstClient = factory.CreateOwnerClient();
        using var secondClient = factory.CreateOwnerClient();
        int warehouseId;
        int roomId;
        int fruitProfileId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var room = await db.Rooms.AsNoTracking().OrderBy(x => x.Id).FirstAsync(x => x.IsActive);
            warehouseId = room.WarehouseId;
            roomId = room.Id;
            fruitProfileId = await db.FruitProfiles.AsNoTracking().OrderBy(x => x.Id).Where(x => x.IsActive).Select(x => x.Id).FirstAsync();
        }

        var receiptNumber = $"CONC-{Guid.NewGuid():N}"[..17];
        var form = await ReceiptFormAsync(firstClient, receiptNumber, warehouseId, roomId, fruitProfileId);
        var exactUpload = ExifMarkerJpegBytes(6);
        var originalHash = SHA256.HashData(exactUpload);
        AddPhoto(form, 0, "concurrent.jpg", "image/jpeg", "BinTruck", "Upload File", exactUpload);
        var created = await firstClient.PostAsync("/Receipts/Create", form);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        long receiptId;
        long photoId;
        string originalKey;
        string initialPresentationKey;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var receipt = await db.Receipts.SingleAsync(x => x.CompuTechReceiptId == receiptNumber);
            var photo = await db.QcPhotos.SingleAsync(x => x.ReceiptId == receipt.Id);
            receiptId = receipt.Id;
            photoId = photo.Id;
            originalKey = photo.FileId!;
            initialPresentationKey = photo.PresentationStorageKey!;
            Assert.Equal(originalHash, SHA256.HashData(factory.Storage.ReadBytes[originalKey]));
            Assert.Equal(1, photo.PresentationRevision);
        }

        var firstToken = await AntiforgeryTokenAsync(firstClient, $"/Receipts/{receiptId}");
        var secondToken = await AntiforgeryTokenAsync(secondClient, $"/Receipts/{receiptId}");
        factory.Storage.SynchronizeNextSaves(2);
        var requests = new[]
        {
            RotateReceiptAsync(firstClient, receiptId, photoId, "right", 1, firstToken),
            RotateReceiptAsync(secondClient, receiptId, photoId, "right", 1, secondToken)
        };
        var responses = await Task.WhenAll(requests);

        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        var losingResponse = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        using var losingJson = JsonDocument.Parse(await losingResponse.Content.ReadAsStringAsync());
        Assert.Equal(2, losingJson.RootElement.GetProperty("presentationRevision").GetInt32());
        Assert.True(losingJson.RootElement.GetProperty("isConflict").GetBoolean());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var photo = await db.QcPhotos.SingleAsync(x => x.Id == photoId);
            Assert.Equal(2, photo.PresentationRevision);
            Assert.Equal(1, photo.ManualRotationQuarterTurns);
            Assert.NotEqual(initialPresentationKey, photo.PresentationStorageKey);
            Assert.True(factory.Storage.ReadBytes.ContainsKey(photo.PresentationStorageKey!));
            Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.EntityKey == photoId.ToString() && x.Action == "rotate-photo-right"));
            Assert.Equal(originalHash, SHA256.HashData(factory.Storage.ReadBytes[originalKey]));
        }
        Assert.Contains(initialPresentationKey, factory.Storage.DeletedKeys);
        Assert.Equal(2, factory.Storage.DeletedKeys.Count);
        Assert.Equal(4, factory.Storage.SaveCount);
    }

    private static async Task<HttpResponseMessage> RotateReceiptAsync(
        HttpClient client,
        long receiptId,
        long photoId,
        string direction,
        int expectedRevision,
        string antiforgeryToken)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Direction"] = direction,
            ["ExpectedPresentationRevision"] = expectedRevision.ToString(),
            ["__RequestVerificationToken"] = antiforgeryToken
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/Receipts/{receiptId}/photos/{photoId}/rotate")
        {
            Content = form
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> RotateSampleAsync(
        HttpClient client,
        long sampleId,
        long photoId,
        string direction,
        int expectedRevision,
        string antiforgeryToken)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Direction"] = direction,
            ["ExpectedPresentationRevision"] = expectedRevision.ToString(),
            ["__RequestVerificationToken"] = antiforgeryToken
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/Samples/{sampleId}/photos/{photoId}/rotate")
        {
            Content = form
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> RotateFieldAsync(
        HttpClient client,
        long sampleId,
        long photoId,
        string direction,
        int expectedRevision,
        string antiforgeryToken)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Direction"] = direction,
            ["ExpectedPresentationRevision"] = expectedRevision.ToString(),
            ["__RequestVerificationToken"] = antiforgeryToken
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/FieldSamples/{sampleId}/photos/{photoId}/rotate")
        {
            Content = form
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await client.SendAsync(request);
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

    private static async Task UploadPhotoAsync(
        HttpClient client,
        string postPath,
        string tokenPath,
        string fileField,
        string photoType,
        byte[] bytes)
    {
        var token = await AntiforgeryTokenAsync(client, tokenPath);
        using var content = new MultipartFormDataContent();
        Add(content, "__RequestVerificationToken", token);
        Add(content, "PhotoType", photoType);
        Add(content, "PhotoSource", "Upload File");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, fileField, "orientation-6.jpg");
        var response = await client.PostAsync(postPath, content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static ClaimsPrincipal OwnerPrincipal() => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, ApplicationAreas.OwnerEmail),
        new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)
    ], "Test"));

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

    private static byte[] TestPhotoBytes(string fileName)
    {
        using var image = new Image<Rgba32>(8, 6, Color.CornflowerBlue);
        using var stream = new MemoryStream();
        switch (Path.GetExtension(fileName).ToLowerInvariant())
        {
            case ".png":
                image.Save(stream, new PngEncoder());
                break;
            case ".webp":
                image.Save(stream, new WebpEncoder());
                break;
            default:
                image.Save(stream, new JpegEncoder { Quality = 95 });
                break;
        }
        return stream.ToArray();
    }

    private static byte[] ExifMarkerJpegBytes(int orientation)
    {
        using var image = MarkerImage();
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)orientation);
        using var stream = new MemoryStream();
        image.Save(stream, new JpegEncoder { Quality = 100 });
        return stream.ToArray();
    }

    private static Image<Rgba32> MarkerImage()
    {
        var image = new Image<Rgba32>(80, 60);
        Fill(image, 0, 0, 40, 30, Color.Red);
        Fill(image, 40, 0, 40, 30, Color.Lime);
        Fill(image, 0, 30, 40, 30, Color.Blue);
        Fill(image, 40, 30, 40, 30, Color.Yellow);
        return image;
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

    private static Task AssertCornerOrderAsync(byte[] bytes, string expected)
    {
        using var image = Image.Load<Rgba32>(bytes);
        var actual = string.Concat(
            Marker(image[image.Width / 4, image.Height / 4]),
            Marker(image[image.Width * 3 / 4, image.Height / 4]),
            Marker(image[image.Width / 4, image.Height * 3 / 4]),
            Marker(image[image.Width * 3 / 4, image.Height * 3 / 4]));
        Assert.Equal(expected, actual);
        return Task.CompletedTask;
    }

    private static void AssertPresentationLink(string html, string expectedUrl)
    {
        var photoReferences = Regex.Matches(html, "(?<attribute>href|src)=\\\"(?<url>[^\\\"]*photos[^\\\"]*)\\\"")
            .Select(x => $"{x.Groups["attribute"].Value}={WebUtility.HtmlDecode(x.Groups["url"].Value)}")
            .ToList();
        var pageText = Regex.Replace(html, "<[^>]+>", " ");
        var notices = Regex.Matches(html, "<p class=\\\"notice[^\\\"]*\\\">(?<text>.*?)</p>", RegexOptions.Singleline)
            .Select(x => Regex.Replace(x.Groups["text"].Value, "<[^>]+>", " ").Trim())
            .ToList();
        var diagnosticStart = Math.Max(0, pageText.Length - 2400);
        var diagnostics = $"Photo references: {string.Join(" | ", photoReferences)}. Notices: {string.Join(" | ", notices)}. Page tail: {pageText[diagnosticStart..]}";
        Assert.True(photoReferences.Contains($"href={expectedUrl}"), $"Missing primary presentation href. {diagnostics}");
        Assert.True(photoReferences.Contains($"src={expectedUrl}"), $"Missing presentation thumbnail. {diagnostics}");
    }

    private static async Task<string> RenderPhotoGroupsAsync(
        IServiceProvider services,
        string presentationUrl,
        long? qcSampleId = null,
        int revision = 1)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var viewEngine = provider.GetRequiredService<IRazorViewEngine>();
        var tempDataProvider = provider.GetRequiredService<ITempDataProvider>();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = OwnerPrincipal()
        };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var viewResult = viewEngine.GetView(null, "/Views/Shared/_PhotoGroups.cshtml", isMainPage: false);
        Assert.True(viewResult.Success, string.Join(Environment.NewLine, viewResult.SearchedLocations ?? []));

        var photo = new PhotoMetadataViewModel(
            42,
            qcSampleId,
            qcSampleId,
            qcSampleId is null ? "BinTruck" : "Hectre",
            "Upload File",
            "orientation.jpg",
            "image/jpeg",
            1024,
            "https://example.test/original-drive-object",
            DateTimeOffset.UtcNow,
            CanDelete: false,
            DisplayAsThumbnail: true,
            ThumbnailUrl: presentationUrl,
            CanRotate: false,
            PresentationRevision: revision,
            PresentationUrl: presentationUrl);
        var viewData = new ViewDataDictionary<IReadOnlyList<PhotoGroupViewModel>>(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary())
        {
            Model = [new PhotoGroupViewModel(photo.PhotoType, [photo])]
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);
        using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewData,
            tempData,
            writer,
            new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);
        return WebUtility.HtmlDecode(writer.ToString());
    }

    private static char Marker(Rgba32 pixel)
    {
        if (pixel.R > 180 && pixel.G < 100 && pixel.B < 100) return 'A';
        if (pixel.G > 150 && pixel.R < 100 && pixel.B < 100) return 'B';
        if (pixel.B > 150 && pixel.R < 100 && pixel.G < 100) return 'C';
        if (pixel.R > 150 && pixel.G > 150 && pixel.B < 100) return 'D';
        return '?';
    }

    private static PhotoOrientationSnapshot Snapshot(QcPhoto photo, FakePhotoStorage storage) => new(
        photo.OriginalExifOrientation,
        photo.ManualRotationQuarterTurns,
        photo.PresentationRevision,
        photo.PresentationStorageKey,
        photo.PresentationFileName,
        photo.PresentationContentType,
        photo.PresentationFileSizeBytes,
        photo.PresentationUpdatedAt,
        photo.FileId,
        SHA256.HashData(storage.ReadBytes[photo.FileId!]));

    private static void AssertSnapshot(PhotoOrientationSnapshot expected, QcPhoto actual, FakePhotoStorage storage)
    {
        Assert.Equal(expected.OriginalExifOrientation, actual.OriginalExifOrientation);
        Assert.Equal(expected.ManualRotationQuarterTurns, actual.ManualRotationQuarterTurns);
        Assert.Equal(expected.PresentationRevision, actual.PresentationRevision);
        Assert.Equal(expected.PresentationStorageKey, actual.PresentationStorageKey);
        Assert.Equal(expected.PresentationFileName, actual.PresentationFileName);
        Assert.Equal(expected.PresentationContentType, actual.PresentationContentType);
        Assert.Equal(expected.PresentationFileSizeBytes, actual.PresentationFileSizeBytes);
        Assert.Equal(expected.PresentationUpdatedAt, actual.PresentationUpdatedAt);
        Assert.Equal(expected.OriginalFileId, actual.FileId);
        Assert.Equal(expected.OriginalHash, SHA256.HashData(storage.ReadBytes[actual.FileId!]));
    }

    private sealed record PhotoOrientationSnapshot(
        int? OriginalExifOrientation,
        int ManualRotationQuarterTurns,
        int PresentationRevision,
        string? PresentationStorageKey,
        string? PresentationFileName,
        string? PresentationContentType,
        long? PresentationFileSizeBytes,
        DateTimeOffset? PresentationUpdatedAt,
        string? OriginalFileId,
        byte[] OriginalHash);

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
        public const string ViewOnlyEmail = "photo.viewer@example.test";
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

        public async Task<HttpClient> CreateViewOnlyClientAsync()
        {
            using (await CreateOwnerClientAsync()) { }
            await using (var scope = Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
                if (!await db.Users.AnyAsync(x => x.Email == ViewOnlyEmail))
                {
                    var user = new User
                    {
                        Email = ViewOnlyEmail,
                        DisplayName = "Photo Viewer",
                        Domain = "example.test",
                        IsActive = true,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    var role = await db.Roles.Include(x => x.PageAccesses)
                        .SingleOrDefaultAsync(x => x.Name == BuiltInRoleNames.Viewer);
                    if (role is null)
                    {
                        role = new Role
                        {
                            Name = BuiltInRoleNames.Viewer,
                            NormalizedName = BuiltInRoleNames.Normalize(BuiltInRoleNames.Viewer),
                            IsActive = true
                        };
                        db.Roles.Add(role);
                    }
                    foreach (var area in new[] { ApplicationAreas.Receipts, ApplicationAreas.DailyQc, ApplicationAreas.FieldSamples })
                    {
                        var access = role.PageAccesses.SingleOrDefault(x => x.AreaKey == area);
                        if (access is null)
                        {
                            role.PageAccesses.Add(new RolePageAccess
                            {
                                AreaKey = area,
                                AccessLevel = nameof(PageAccessLevel.View),
                                UpdatedAt = DateTimeOffset.UtcNow
                            });
                        }
                        else
                        {
                            access.AccessLevel = nameof(PageAccessLevel.View);
                        }
                    }
                    db.Users.Add(user);
                    db.UserRoles.Add(new UserRole { User = user, Role = role });
                    await db.SaveChangesAsync();
                }
            }
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
            client.DefaultRequestHeaders.Add("X-Test-Email", ViewOnlyEmail);
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
        private int saveCount;
        private int deleteCount;
        private int openReadCount;
        private int synchronizeSaveTarget;
        private int synchronizedSaveArrivals;
        private TaskCompletionSource? synchronizedSaves;
        private readonly object requestsGate = new();
        public int FailuresRemaining { get; set; }
        public int SaveCount => Volatile.Read(ref saveCount);
        public int DeleteCount => Volatile.Read(ref deleteCount);
        public int OpenReadCount => Volatile.Read(ref openReadCount);
        public ConcurrentDictionary<string, byte[]> ReadBytes { get; } = [];
        public List<FileStorageSaveRequest> SavedRequests { get; } = [];
        public ConcurrentBag<string> DeletedKeys { get; } = [];

        public string GenerateTargetPath(FileStorageTargetContext context) =>
            $"Photos/{context.CropYear}/{context.WarehouseCode}/Receipt-{context.ReceiptId}/{context.PhotoType}";

        public void SynchronizeNextSaves(int count)
        {
            synchronizeSaveTarget = count;
            synchronizedSaveArrivals = 0;
            synchronizedSaves = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default)
        {
            var currentSave = Interlocked.Increment(ref saveCount);
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException("Simulated optional photo storage failure.");
            }
            lock (requestsGate) SavedRequests.Add(request);
            var key = $"{request.TargetPath}/{request.FileName}";
            using var copy = new MemoryStream();
            await request.Content.CopyToAsync(copy, cancellationToken);
            var fileId = $"photo-{currentSave}";
            ReadBytes[fileId] = copy.ToArray();
            var synchronization = synchronizedSaves;
            if (synchronization is not null && Interlocked.Increment(ref synchronizedSaveArrivals) <= synchronizeSaveTarget)
            {
                if (Volatile.Read(ref synchronizedSaveArrivals) == synchronizeSaveTarget)
                {
                    synchronization.TrySetResult();
                }
                await synchronization.Task.WaitAsync(cancellationToken);
            }
            return new FileStorageReference(
                "Local",
                key,
                request.TargetPath,
                request.FileName,
                request.ContentType,
                request.FileSizeBytes ?? 0,
                FileId: fileId,
                FolderId: request.TargetPath,
                WebUrl: $"https://example.test/{key}");
        }

        public Task<FileStorageReference?> GetMetadataAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<FileStorageReference?>(null);

        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref openReadCount);
            return Task.FromResult<Stream?>(ReadBytes.TryGetValue(storageKey, out var bytes)
                ? new MemoryStream(bytes, writable: false)
                : null);
        }

        public Task DeleteOrVoidAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref deleteCount);
            DeletedKeys.Add(storageKey);
            ReadBytes.TryRemove(storageKey, out _);
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

            var email = Request.Headers.TryGetValue("X-Test-Email", out var values)
                ? values.ToString()
                : ApplicationAreas.OwnerEmail;
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, email),
                new Claim(ClaimTypes.Name, email == ApplicationAreas.OwnerEmail ? "Receipt Photo Owner" : "Photo Viewer"),
                new Claim(ClaimTypes.Email, email)
            ], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}

namespace CropQc.Api.Tests;

public sealed class PhotoUploadFeedbackTests
{
    [Fact]
    public void SavedReceiptQcAndFieldSamplePhotoFormsUseSharedAccessibleBusyState()
    {
        var form = Source("src", "CropQc.Web", "Views", "Shared", "_PhotoPlaceholderForm.cshtml");
        var layout = Source("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml");
        var helper = Source("src", "CropQc.Web", "wwwroot", "js", "upload-feedback.js");

        Assert.Contains("data-upload-feedback", form);
        Assert.Contains("data-photo-upload-submit", form);
        Assert.Contains("aria-live=\"polite\"", form);
        Assert.Contains("Uploading photo...", form);
        Assert.Contains("Uploading ${selectedUploadFiles.length} photos...", form);
        Assert.Contains("upload-feedback.js", layout);
        Assert.Contains("form.setAttribute(\"aria-busy\", \"true\")", helper);
        Assert.Contains("if (busy) return false", helper);
        Assert.Contains("control.disabled = true", helper);
        Assert.Contains("[data-upload-feedback-form]:not([data-upload-progress-form])", helper);
    }

    [Fact]
    public void FieldSampleAutosaveTransitionsToUploadExactlyOnceAndRecoversOnFailure()
    {
        var form = Source("src", "CropQc.Web", "Views", "Shared", "_PhotoPlaceholderForm.cshtml");

        Assert.Contains("Saving Field Sample changes before the photo upload...", form);
        Assert.Contains("autosaveContinuation = true", form);
        Assert.Contains("form.requestSubmit()", form);
        Assert.Contains("uploadFeedback.isBusy() && !autosaveContinuation", form);
        Assert.Contains("uploadFeedback.update", form);
        Assert.Contains("uploadFeedback.fail", form);
        Assert.Contains("The photo remains selected; retry", form);
    }

    [Fact]
    public void StagedReceiptPhotosUseAccurateCountsAndLockMutationControls()
    {
        var staged = Source("src", "CropQc.Web", "wwwroot", "js", "staged-receipt-photos.js");
        var partial = Source("src", "CropQc.Web", "Views", "Shared", "_StagedReceiptPhotos.cshtml");

        Assert.Contains("Saving receipt...", staged);
        Assert.Contains("Saving receipt and uploading photo...", staged);
        Assert.Contains("Saving receipt and uploading ${items.length} photos...", staged);
        Assert.Contains("browse, takePhoto, typeSelect", staged);
        Assert.Contains(".staged-receipt-photo-card button", staged);
        Assert.Contains("if (!uploadFeedback.begin", staged);
        Assert.Contains("cropqc:receipt-submit-busy", staged);
        Assert.Contains("data-upload-feedback", partial);
        Assert.Contains("data-upload-feedback-spinner", partial);
    }

    [Fact]
    public void DeviceCaptureUsesPreparingThenUploadingLanguageAndUnlocksAfterFailure()
    {
        var panel = Source("src", "CropQc.Web", "Views", "Shared", "_DeviceCapturePanel.cshtml");

        Assert.Contains("let photoCaptureBusy = false", panel);
        Assert.Contains("if (photoCaptureBusy) return false", panel);
        Assert.Contains("Preparing ${friendlyPhotoType(photoType)}...", panel);
        Assert.Contains("Uploading ${friendlyPhotoType(photoType)}...", panel);
        Assert.Contains("endPhotoCapture()", panel);
        Assert.Contains("aria-live=\"polite\"", panel);
        Assert.Contains("data-device-upload-spinner", panel);
        Assert.Contains("Photo upload could not be completed", panel);
    }

    [Fact]
    public void TreatmentReportsAndImageCapablePackoutUploadsUseDuplicateGuards()
    {
        var treatment = Source("src", "CropQc.Web", "wwwroot", "js", "treatment-reports.js");
        var packout = Source("src", "CropQc.Web", "Views", "BinsRun", "ActualRunDetail.cshtml");

        Assert.Contains("Uploading ${count} treatment report", treatment);
        Assert.Contains("Saving treatment and uploading ${count} report", treatment);
        Assert.Contains("if (!uploadFeedback.begin", treatment);
        Assert.Contains("application/pdf", Source("src", "CropQc.Web", "Views", "RoomTreatments", "Apply.cshtml"));
        Assert.Contains("image/webp", Source("src", "CropQc.Web", "Views", "RoomTreatments", "Apply.cshtml"));
        Assert.Contains("data-upload-feedback-form", packout);
        Assert.Contains("data-upload-progress-form", packout);
        Assert.Contains("data-upload-feedback-progress", packout);
        Assert.Contains("Uploading packout report files...", packout);

        var helper = Source("src", "CropQc.Web", "wwwroot", "js", "upload-feedback.js");
        Assert.Contains("new XMLHttpRequest()", helper);
        Assert.Contains("request.upload.addEventListener(\"progress\"", helper);
        Assert.Contains("uploadEvent.loaded / uploadEvent.total", helper);
        Assert.Contains("controller.setProgress(null)", helper);
        Assert.Contains("Upload complete — processing report", helper);
        Assert.Contains("X-Requested-With", helper);
        Assert.Contains("if (!controller.begin", helper);
        Assert.Contains("controller.fail", helper);
    }

    [Fact]
    public void UploadFeedbackAddsNoSchemaOrInventoryBehavior()
    {
        var changedRuntimeFiles = new[]
        {
            "src/CropQc.Web/Views/Shared/_Layout.cshtml",
            "src/CropQc.Web/Views/Shared/_PhotoPlaceholderForm.cshtml",
            "src/CropQc.Web/Views/Shared/_StagedReceiptPhotos.cshtml",
            "src/CropQc.Web/Views/Shared/_DeviceCapturePanel.cshtml",
            "src/CropQc.Web/Views/BinsRun/ActualRunDetail.cshtml",
            "src/CropQc.Web/wwwroot/js/upload-feedback.js",
            "src/CropQc.Web/wwwroot/js/staged-receipt-photos.js",
            "src/CropQc.Web/wwwroot/js/treatment-reports.js",
            "src/CropQc.Web/wwwroot/css/site.css"
        };

        Assert.DoesNotContain(changedRuntimeFiles, path => path.Contains("Migrations", StringComparison.Ordinal));
        Assert.DoesNotContain(changedRuntimeFiles, path => path.Contains("CropQc.Data", StringComparison.Ordinal));
    }

    private static string Source(params string[] pathParts) => File.ReadAllText(Find(pathParts));

    private static string Find(params string[] pathParts)
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
}

namespace CropQc.Api.Tests;

public sealed class DeviceCaptureIntegrationTests
{
    [Fact]
    public void DeviceCapture_SettingsAreConfigurableAndDefaultOff()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "AdminManagementService.cs"));
        var settings = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "appsettings.json"));
        var productionSettings = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "appsettings.Production.json"));

        Assert.Contains("DeviceCapture__Enabled", service);
        Assert.Contains("DeviceCapture__BrioEnabled", service);
        Assert.Contains("DeviceCapture__ObsbotEnabled", service);
        Assert.Contains("DeviceCapture__ScaleEnabled", service);
        Assert.Contains("\"DeviceCapture\"", settings);
        Assert.Contains("\"Enabled\": false", settings);
        Assert.Contains("\"BrioEnabled\": false", productionSettings);
        Assert.Contains("\"ObsbotEnabled\": false", productionSettings);
        Assert.Contains("\"ScaleEnabled\": false", productionSettings);
    }

    [Fact]
    public void NewReceiptPage_StagesOptionalReceiptPhotosBeforeSave()
    {
        var model = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Models", "DashboardViewModels.cs"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var receiptIndexView = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Index.cshtml"));

        Assert.Contains("public DeviceCaptureSettingsViewModel DeviceCapture", model);
        Assert.Contains("DeviceCapture = await GetDeviceCaptureSettingsAsync", service);
        Assert.Contains("Html.PartialAsync(\"_DeviceCapturePanel\"", receiptIndexView);
        Assert.Contains("_StagedReceiptPhotos", receiptIndexView);
        Assert.Contains("StageReceiptPhotos: true", receiptIndexView);
        Assert.Contains("enctype=\"multipart/form-data\"", receiptIndexView);
        Assert.Contains("data-receipt-submit", receiptIndexView);
    }

    [Fact]
    public void DisabledState_ShowsLocalEnableAndSetupControls()
    {
        var model = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Models", "DashboardViewModels.cs"));
        var panel = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_DeviceCapturePanel.cshtml"));

        Assert.Contains("DeviceCaptureSettingsViewModel.Disabled", model);
        Assert.DoesNotContain("@if (Model.Settings.AnyEnabled)", panel);
        Assert.Contains("Device capture is disabled.", panel);
        Assert.Contains("Enable Device Capture on this browser", panel);
        Assert.Contains("Device Setup / Select Cameras", panel);
        Assert.Contains("Test Camera Preview", panel);
        Assert.Contains("Save device choices", panel);
        Assert.Contains("Reset Device Settings", panel);
        Assert.Contains("cropqc.deviceCapture.enabled", panel);
        Assert.Contains("Capture Truck Photo", panel);
        Assert.Contains("@Model.WholeSampleLabel", panel);
        Assert.Contains("Read Weight from Scale", panel);
        Assert.Contains("Capture Next Fruit Weight", panel);
    }

    [Fact]
    public void CameraCapture_UsesBrowserApisAndPostsToExistingPhotoEndpoints()
    {
        var panel = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_DeviceCapturePanel.cshtml"));
        var receiptView = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Details.cshtml"));
        var sampleView = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Samples", "Details.cshtml"));
        var starchView = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Samples", "Starch.cshtml"));

        Assert.Contains("navigator.mediaDevices.getUserMedia", panel);
        Assert.Contains("navigator.mediaDevices.enumerateDevices", panel);
        Assert.Contains("storage.setItem(storageKeys[role]", panel);
        Assert.Contains("video.srcObject = stream", panel);
        Assert.Contains("await video.play()", panel);
        Assert.Contains("await startCamera(role)", panel);
        Assert.Contains("OBSBOT Tiny 2 Lite", panel);
        Assert.Contains("Logitech Brio 4K", panel);
        Assert.Contains("form.append(\"PhotoFile\"", panel);
        Assert.Contains("fetch(action", panel);
        Assert.Contains("PhotoSource", panel);
        Assert.Contains("ReceiptPhotoAction: $\"/Receipts/{Model.Receipt.Id}/photos\"", receiptView);
        Assert.Contains("SamplePhotoAction: $\"/Samples/{Model.Sample.Id}/photos\"", sampleView);
        Assert.Contains("StarchPhotoAction: $\"/Samples/{Model.Sample.Id}/Starch/photos\"", starchView);
    }

    [Fact]
    public void CameraCapture_MapsPhysicalCamerasToExpectedPhotoTypes()
    {
        var panel = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_DeviceCapturePanel.cshtml"));

        Assert.Contains("Allow camera access to identify cameras.", panel);
        Assert.Contains("Allow Camera Access", panel);
        Assert.Contains("Camera connected.", panel);
        Assert.Contains("Camera permission denied.", panel);
        Assert.Contains("Browser camera API unavailable.", panel);
        Assert.Contains("value.includes(\"obsbot\") || value.includes(\"tiny\")", panel);
        Assert.Contains("value.includes(\"brio\") || value.includes(\"logitech\")", panel);
        Assert.Contains("data-capture-photo=\"BinTruck\"", panel);
        Assert.Contains("data-capture-photo=\"TopOfTruck\"", panel);
        Assert.Contains("data-capture-photo=\"SampleBeforeCutting\"", panel);
        Assert.Contains("data-capture-photo=\"CutFruit\"", panel);
        Assert.Contains("data-capture-photo=\"FruitAfterStarch\"", panel);
        Assert.Contains("Truck photo", panel);
        Assert.Contains("panel.dataset.wholeSampleLabel", panel);
        Assert.Contains("Starch photo", panel);
    }

    [Fact]
    public void LogitechControls_AreCapabilityDrivenAndStayInSharedCapturePanel()
    {
        var panel = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_DeviceCapturePanel.cshtml"));
        var controls = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "js", "device-camera-controls.js"));

        Assert.Contains("Camera Image Setup", panel);
        Assert.Contains("Current camera:", panel);
        Assert.Contains("Settings are remembered for this camera on this computer", panel);
        Assert.Contains("Use Automatic Color &amp; Exposure", panel);
        Assert.Contains("Lock Current Color &amp; Exposure", panel);
        Assert.Contains("Reset Camera", panel);
        Assert.Contains("Lighting", panel);
        Assert.Contains("Auto Exposure", panel);
        Assert.Contains("Manual Exposure", panel);
        Assert.Contains("Color Temperature", panel);
        Assert.Contains("Auto White Balance", panel);
        Assert.Contains("Auto Focus", panel);
        Assert.Contains("Manual Focus", panel);
        Assert.Contains("Brightness", panel);
        Assert.Contains("Contrast", panel);
        Assert.Contains("Saturation", panel);
        Assert.Contains("Sharpness", panel);
        Assert.Contains("ISO", panel);
        Assert.Contains("Capture Test Photo", panel);
        Assert.Contains("Test photos stay on this computer and are not saved to the QC record.", panel);
        Assert.Contains("Copy Camera Details", panel);
        Assert.Contains("~/js/device-camera-controls.js", panel);
        Assert.Contains("stream.getVideoTracks?.()[0]", panel);
        Assert.Contains("new cameraControlsApi.CameraControlSession(track)", panel);
        Assert.Contains("track.getCapabilities()", controls);
        Assert.Contains("track.getSettings()", controls);
        Assert.Contains("track.applyConstraints({ advanced: [next] })", controls);
        Assert.Contains("focusMode", controls);
        Assert.Contains("focusDistance", controls);
        Assert.Contains("exposureMode", controls);
        Assert.Contains("exposureCompensation", controls);
        Assert.Contains("exposureTime", controls);
        Assert.Contains("whiteBalanceMode", controls);
        Assert.Contains("colorTemperature", controls);
        Assert.Contains("brightness", controls);
        Assert.Contains("contrast", controls);
        Assert.Contains("saturation", controls);
        Assert.Contains("sharpness", controls);
        Assert.Contains("iso", controls);
        Assert.DoesNotContain("style.filter", panel);
        Assert.DoesNotContain("context.filter", panel);
    }

    [Fact]
    public void LogitechControls_PersistPerDeviceAndRemainEnhancementOnly()
    {
        var panel = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_DeviceCapturePanel.cshtml"));
        var controls = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "js", "device-camera-controls.js"));
        var behaviorTests = File.ReadAllText(FindRepositoryFile("tests", "js", "device-camera-controls.test.cjs"));

        Assert.Contains("cropqc.deviceCapture.cameraControls", controls);
        Assert.Contains("savedControls(storage, deviceId)", panel);
        Assert.Contains("sanitizeValues(saved, cameraControlCapabilities)", panel);
        Assert.Contains("clearControls(storage, deviceId)", panel);
        Assert.Contains("startCamera(\"apple\", { reapplySaved: false })", panel);
        Assert.Contains("window.setTimeout(", panel);
        Assert.Contains("150);", panel);
        Assert.Contains("The previous setting is still in use.", panel);
        Assert.Contains("The preview remains available.", panel);
        Assert.Contains("full Logitech-like capabilities", behaviorTests);
        Assert.Contains("partial and basic cameras", behaviorTests);
        Assert.Contains("constraint failure preserves the stream", behaviorTests);
        Assert.Contains("rapid updates are coalesced", behaviorTests);

        Assert.Contains("canvas.toBlob(resolve, \"image/jpeg\", quality)", controls);
        Assert.Equal(2, panel.Split("await captureCameraJpeg()", StringSplitOptions.None).Length - 1);
        Assert.Contains("canvas.toBlob(resolve, \"image/jpeg\", 0.92)", panel);
        Assert.Contains("form.append(\"PhotoFile\"", panel);
        Assert.Contains("credentials: \"same-origin\"", panel);
        Assert.Contains("__RequestVerificationToken", panel);
    }

    [Fact]
    public void CameraImageSetup_TestPhotosRemainBrowserOnlyAndRetainTwoComparisons()
    {
        var panel = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_DeviceCapturePanel.cshtml"));
        var controls = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "js", "device-camera-controls.js"));
        var behaviorTests = File.ReadAllText(FindRepositoryFile("tests", "js", "device-camera-controls.test.cjs"));

        Assert.Contains("new cameraControlsApi.TestPhotoBuffer(window.URL)", panel);
        Assert.Contains("importantSettingsSnapshot(actualSettings", panel);
        Assert.Contains("Test photo captured locally. It was not uploaded or saved to the QC record.", panel);
        Assert.Contains("while (this.items.length > 2)", controls);
        Assert.Contains("this.urlApi.createObjectURL(blob)", controls);
        Assert.Contains("this.urlApi.revokeObjectURL", controls);
        Assert.Contains("window.addEventListener(\"beforeunload\"", panel);
        Assert.Contains("test-photo capture uses the normal direct JPEG path at quality 0.92", behaviorTests);
        Assert.Contains("test-photo buffer keeps the newest two", behaviorTests);
        Assert.DoesNotContain("QcPhoto", controls);
        Assert.DoesNotContain("fetch(", controls);
    }

    [Fact]
    public void CameraImageSetup_ModeDependenciesAndDiagnosticsStayClientSide()
    {
        var panel = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_DeviceCapturePanel.cshtml"));
        var controls = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "js", "device-camera-controls.js"));

        Assert.Contains("automaticColorExposureValues", controls);
        Assert.Contains("lockCurrentColorExposureValues", controls);
        Assert.Contains("[\"exposureTime\", \"exposureMode\"]", controls);
        Assert.Contains("[\"colorTemperature\", \"whiteBalanceMode\"]", controls);
        Assert.Contains("[\"focusDistance\", \"focusMode\"]", controls);
        Assert.Contains("persistActualCameraControls(actual)", panel);
        Assert.Contains("cameraDetails(cameraLabel, actualSettings", panel);
        Assert.Contains("navigator.clipboard.writeText", panel);
        Assert.DoesNotContain("deviceId", controls.Substring(controls.IndexOf("function cameraDetails", StringComparison.Ordinal),
            controls.IndexOf("async function captureJpegBlob", StringComparison.Ordinal) - controls.IndexOf("function cameraDetails", StringComparison.Ordinal)));
        Assert.DoesNotContain("style.filter", panel);
        Assert.DoesNotContain("context.filter", panel);
    }

    [Fact]
    public void UnsavedReceiptCameraCapture_StagesTruckStillImagesWithoutServerUpload()
    {
        var panel = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_DeviceCapturePanel.cshtml"));
        var staging = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "js", "staged-receipt-photos.js"));
        var partial = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_StagedReceiptPhotos.cshtml"));

        Assert.Contains("data-stage-receipt-photos", panel);
        Assert.Contains("photoType === \"BinTruck\" || photoType === \"TopOfTruck\"", panel);
        Assert.Contains("cropqc:stage-receipt-photo", panel);
        Assert.Contains("detail: { file, photoType, photoSource }", panel);
        Assert.Contains("Receipt Photos (Optional)", partial);
        Assert.Contains("No receipt photos selected.", partial);
        Assert.Contains("URL.createObjectURL(file)", staging);
        Assert.Contains("URL.revokeObjectURL(item.previewUrl)", staging);
        Assert.Contains("Remove staged photo", staging);
        Assert.Contains("stagedPhotos[${index}].PhotoFile", staging);
        Assert.Contains("if (submitting)", staging);
        Assert.DoesNotContain("localStorage", staging, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("base64", staging, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StagedReceiptPhotoClient_RejectsUnsupportedAndOversizedFiles()
    {
        var staging = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "js", "staged-receipt-photos.js"));

        Assert.Contains("15 * 1024 * 1024", staging);
        Assert.Contains("image/jpeg", staging);
        Assert.Contains("image/png", staging);
        Assert.Contains("image/webp", staging);
        Assert.Contains("allowedExtensions", staging);
        Assert.Contains("return false", staging);
    }

    [Fact]
    public void ScaleCapture_UsesBrowserSerialFallbackMessageAndWritesWeightInputs()
    {
        var panel = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_DeviceCapturePanel.cshtml"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var fieldSamples = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "FieldSamples", "Details.cshtml"));
        var fieldAutosave = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "js", "field-sample-autosave.js"));

        Assert.Contains("\"serial\" in navigator", panel);
        Assert.Contains("\"usb\" in navigator", panel);
        Assert.Contains("Browser does not support Web Serial/WebUSB", panel);
        Assert.Contains("Scale not connected or no current weight is available", panel);
        Assert.Contains("input[name$='.WeightGrams']", panel);
        Assert.Contains("input.dispatchEvent(new Event(\"input\"", panel);
        Assert.Contains("event.target?.closest?.(\"tr.fruit-row\")", panel);
        Assert.Contains("reading.WeightGrams = submittedRow.WeightGrams", service);
        Assert.Contains("In-progress rows are saved", FindRepositoryFileText("src", "CropQc.Web", "Views", "Samples", "Details.cshtml"));
        Assert.Contains("ShowScale: true", fieldSamples);
        Assert.Contains("data-add-field-row", fieldSamples);
        Assert.Contains("Rows[${index}]", fieldAutosave);
        Assert.Contains("data-field-weight", fieldSamples);
    }

    [Fact]
    public void StarchPage_DynamicallyMapsActualRowsAndKeepsRetryControlsVisible()
    {
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Samples", "Starch.cshtml"));
        var panel = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_DeviceCapturePanel.cshtml"));

        Assert.Contains("availableFruitNumbers", view);
        Assert.Contains(".Where(availableFruitNumbers.Contains)", view);
        Assert.DoesNotContain("Enumerable.Range(1, 25).Single", view);
        Assert.Contains("Open / Retry QC Station", view);
        Assert.Contains("data-retry-starch-capture", view);
        Assert.Contains("window.isSecureContext", panel);
        Assert.Contains("NotReadableError", panel);
        Assert.Contains("Browser storage is blocked", panel);
    }

    private static string FindRepositoryFileText(params string[] pathParts) =>
        File.ReadAllText(FindRepositoryFile(pathParts));

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

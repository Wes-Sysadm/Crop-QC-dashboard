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
    public void NewReceiptPage_ShowsDeviceCapturePanelAndSaveFirstGuidance()
    {
        var model = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Models", "DashboardViewModels.cs"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var receiptIndexView = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Index.cshtml"));

        Assert.Contains("public DeviceCaptureSettingsViewModel DeviceCapture", model);
        Assert.Contains("DeviceCapture = await GetDeviceCaptureSettingsAsync", service);
        Assert.Contains("Html.PartialAsync(\"_DeviceCapturePanel\"", receiptIndexView);
        Assert.Contains("Save the receipt first to attach photos.", receiptIndexView);
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
        Assert.Contains("Capture Whole Apple Photo", panel);
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
        Assert.Contains("localStorage.setItem(storageKeys[role]", panel);
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
        Assert.Contains("Whole apple photo", panel);
        Assert.Contains("Starch photo", panel);
    }

    [Fact]
    public void ScaleCapture_UsesBrowserSerialFallbackMessageAndWritesWeightInputs()
    {
        var panel = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_DeviceCapturePanel.cshtml"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));

        Assert.Contains("\"serial\" in navigator", panel);
        Assert.Contains("\"usb\" in navigator", panel);
        Assert.Contains("Browser does not support Web Serial/WebUSB", panel);
        Assert.Contains("Scale not connected or no current weight is available", panel);
        Assert.Contains("input[name$='.WeightGrams']", panel);
        Assert.Contains("input.dispatchEvent(new Event(\"input\"", panel);
        Assert.Contains("reading.WeightGrams = submittedRow.WeightGrams", service);
        Assert.Contains("In-progress rows are saved", FindRepositoryFileText("src", "CropQc.Web", "Views", "Samples", "Details.cshtml"));
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

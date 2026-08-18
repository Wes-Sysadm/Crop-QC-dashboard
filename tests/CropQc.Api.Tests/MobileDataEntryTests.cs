namespace CropQc.Api.Tests;

public sealed class MobileDataEntryTests
{
    [Fact]
    public void SharedNavigation_UsesOneAuthorizedMenuForDesktopAndMobile()
    {
        var layout = Read("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml");

        Assert.Contains("data-mobile-menu-button", layout);
        Assert.Contains("aria-controls=\"primary-navigation\"", layout);
        Assert.Contains("data-primary-navigation", layout);
        Assert.Contains("navigation.dataset.mobileOpen", layout);
        Assert.Contains("mobileQuery.matches", layout);
        Assert.Contains("Crop QC", layout);
        Assert.Equal(1, layout.Split("var canAccessDashboard", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("maximum-scale", layout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user-scalable=no", layout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MobileCss_ProvidesSafeAreasTouchTargetsAndNoPageOverflow()
    {
        var css = Read("src", "CropQc.Web", "wwwroot", "css", "site.css");

        Assert.Contains("@media (max-width: 760px)", css);
        Assert.Contains("env(safe-area-inset-top", css);
        Assert.Contains("env(safe-area-inset-bottom", css);
        Assert.Contains("min-height: 44px", css);
        Assert.Contains("font-size: 16px", css);
        Assert.Contains("html, body { max-width: 100%; overflow-x: hidden; }", css);
        Assert.Contains(".table-wrap { width: 100%; max-width: 100%; overflow-x: auto", css);
        Assert.Contains(".brand-mobile, .mobile-menu-button { display: none; }", css);
    }

    [Fact]
    public void QcAndFieldSampleFruitRows_UsePhoneCardsAndDecimalKeyboards()
    {
        var css = Read("src", "CropQc.Web", "wwwroot", "css", "site.css");
        var qc = Read("src", "CropQc.Web", "Views", "Samples", "Details.cshtml");
        var field = Read("src", "CropQc.Web", "Views", "FieldSamples", "Details.cshtml");
        var starch = Read("src", "CropQc.Web", "Views", "Samples", "Starch.cshtml");

        Assert.Contains(".qc-grid td[data-label]::before", css);
        Assert.Contains("data-label=\"Fruit\"", qc);
        Assert.Contains("data-label=\"Pressure 1\"", qc);
        Assert.Contains("data-label=\"Defects\"", qc);
        Assert.Contains("inputmode=\"decimal\"", qc);
        Assert.Contains("data-label=\"Fruit\"", field);
        Assert.Contains("inputmode=\"decimal\"", field);
        Assert.Contains("mobile-sticky-actions", field);
        Assert.Contains("class=\"starch-grid\"", starch);
        Assert.Contains("data-label=\"Starch Value\"", starch);
        Assert.Contains("Save Starch Input", starch);
    }

    [Fact]
    public void ReceiptEntry_UsesPhoneFriendlyCountsAndStickySave()
    {
        var receipt = Read("src", "CropQc.Web", "Views", "Receipts", "Index.cshtml");

        Assert.Contains("mobile-entry-form", receipt);
        Assert.Contains("name=\"BinCount\" type=\"number\" inputmode=\"numeric\"", receipt);
        Assert.Contains("name=\"GrowerNumber\" maxlength=\"50\" inputmode=\"numeric\"", receipt);
        Assert.Contains("mobile-sticky-primary", receipt);
        Assert.Contains("Grower Number - Grower Name", receipt);
    }

    [Fact]
    public void PhonePhotoActions_SeparateCameraAndLibraryWithImmediatePreview()
    {
        var staged = Read("src", "CropQc.Web", "Views", "Shared", "_StagedReceiptPhotos.cshtml");
        var stagedScript = Read("src", "CropQc.Web", "wwwroot", "js", "staged-receipt-photos.js");
        var saved = Read("src", "CropQc.Web", "Views", "Shared", "_PhotoPlaceholderForm.cshtml");

        Assert.Contains("data-staged-photo-take>Take Photo", staged);
        Assert.Contains("data-staged-photo-browse>Choose Existing Photo", staged);
        Assert.Contains("capture=\"environment\"", staged);
        Assert.Contains("URL.createObjectURL(file)", stagedScript);
        Assert.Contains("Mobile Camera", stagedScript);
        Assert.Contains("HEIC/HEIF", stagedScript);
        Assert.Contains("photo-take-mobile\">Take Photo", saved);
        Assert.Contains("photo-choose-existing\">Choose Existing Photo", saved);
        Assert.Contains("capture=\"environment\"", saved);
        Assert.Contains("HEIC/HEIF", saved);
        Assert.Contains("previewUrls.forEach(url => URL.revokeObjectURL(url))", saved);
    }

    [Fact]
    public void ExistingDesktopCameraControlsRemainCapabilityDriven()
    {
        var panel = Read("src", "CropQc.Web", "Views", "Shared", "_DeviceCapturePanel.cshtml");
        var controls = Read("src", "CropQc.Web", "wwwroot", "js", "device-camera-controls.js");

        Assert.Contains("Camera Image Setup", panel);
        Assert.Contains("Capture Test Photo", panel);
        Assert.Contains("track.getCapabilities()", controls);
        Assert.Contains("cropqc.deviceCapture.cameraControls", controls);
        Assert.DoesNotContain("context.filter", panel);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Find(parts));

    private static string Find(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file {Path.Combine(parts)}.");
    }
}

using CropQc.Web.Models;

namespace CropQc.Web.Services;

public static class PhotoUploadValidator
{
    public const long MaxPhotoSizeBytes = 15 * 1024 * 1024;
    private static readonly HashSet<string> AllowedPhotoContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp"
    };

    public static string? Validate(AddPhotoMetadataForm form)
    {
        if (form.PhotoFile is null || form.PhotoFile.Length <= 0)
        {
            return string.Equals(form.PhotoSource, "Upload File", StringComparison.OrdinalIgnoreCase)
                ? "No photo file was selected."
                : null;
        }

        if (form.PhotoFile.Length > MaxPhotoSizeBytes)
        {
            return "Photos must be 15 MB or smaller.";
        }

        if (!AllowedPhotoContentTypes.Contains(form.PhotoFile.ContentType))
        {
            return "Only JPG, PNG, or WEBP images are allowed.";
        }

        var extension = Path.GetExtension(form.PhotoFile.FileName);
        if (!string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase))
        {
            return "Only JPG, PNG, or WEBP images are allowed.";
        }

        return null;
    }
}

using CropQc.Web.Models;

namespace CropQc.Web.Services;

public static class PhotoUploadValidator
{
    private static readonly HashSet<string> AllowedPhotoContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp"
    };

    public static string? Validate(AddPhotoMetadataForm form)
    {
        if (!string.Equals(form.PhotoSource, "Upload File", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (form.PhotoFile is null || form.PhotoFile.Length <= 0)
        {
            return "No photo file was selected.";
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

using CropQc.Data.Entities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

namespace CropQc.Web.Services;

public sealed record PreparedPhotoPresentation(
    byte[] Bytes,
    string ContentType,
    string Extension,
    int OriginalExifOrientation,
    int Width,
    int Height);

public sealed class PhotoProcessingException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

/// <summary>
/// The single authoritative pixel-orientation pipeline for Crop QC photos.
/// It always starts from the immutable original, applies EXIF orientation 1-8,
/// then applies the durable Crop QC quarter-turn setting.
/// </summary>
public static class PhotoOrientationProcessor
{
    public const long MaxDecodedPixels = 50_000_000;
    public const int MaxDecodedDimension = 12_000;
    public const int MaxSourceBytes = 25_000_000;
    private static readonly SemaphoreSlim ProcessingGate = new(1, 1);

    public static async Task<PreparedPhotoPresentation> CreatePresentationAsync(
        Stream original,
        string fileName,
        string declaredContentType,
        int manualRotationQuarterTurns,
        CancellationToken cancellationToken)
    {
        await ProcessingGate.WaitAsync(cancellationToken);
        try
        {
            await using var source = await CopySourceAsync(original, cancellationToken);
            IImageFormat format;
            ImageInfo info;
            try
            {
                format = await Image.DetectFormatAsync(source, cancellationToken)
                    ?? throw new PhotoProcessingException("The selected file is not a supported image.");
                source.Position = 0;
                info = await Image.IdentifyAsync(source, cancellationToken)
                    ?? throw new PhotoProcessingException("The selected image could not be read.");
            }
            catch (PhotoProcessingException)
            {
                throw;
            }
            catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
            {
                throw new PhotoProcessingException("The selected image is corrupt or uses an unsupported format.", ex);
            }

            var formatInfo = ValidateDetectedFormat(format, fileName, declaredContentType);
            if (info.Width <= 0 || info.Height <= 0
                || info.Width > MaxDecodedDimension
                || info.Height > MaxDecodedDimension
                || (long)info.Width * info.Height > MaxDecodedPixels)
            {
                throw new PhotoProcessingException(
                    $"The image is too large to process safely. Use an image no larger than {MaxDecodedDimension:N0} pixels per side and {MaxDecodedPixels / 1_000_000:N0} megapixels.");
            }

            source.Position = 0;
            try
            {
                using var image = await Image.LoadAsync(source, cancellationToken);
                var originalOrientation = ReadExifOrientation(image);
                image.Mutate(context => context.AutoOrient());
                ApplyManualRotation(image, manualRotationQuarterTurns);

                // Presentation pixels are authoritative. Removing EXIF prevents browsers,
                // email clients, and print pipelines from applying orientation twice.
                image.Metadata.ExifProfile = null;

                await using var presentation = new MemoryStream();
                await SavePresentationAsync(image, presentation, formatInfo.FormatName, cancellationToken);
                return new PreparedPhotoPresentation(
                    presentation.ToArray(),
                    formatInfo.ContentType,
                    formatInfo.Extension,
                    originalOrientation,
                    image.Width,
                    image.Height);
            }
            catch (PhotoProcessingException)
            {
                throw;
            }
            catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException or ArgumentException)
            {
                throw new PhotoProcessingException("The selected image is corrupt or could not be decoded safely.", ex);
            }
        }
        finally
        {
            ProcessingGate.Release();
        }
    }

    public static int NormalizeQuarterTurns(int value) => ((value % 4) + 4) % 4;

    public static string? OriginalStorageKey(QcPhoto photo) =>
        !string.IsNullOrWhiteSpace(photo.FileId) ? photo.FileId : photo.SharePointItemId;

    public static string? DisplayStorageKey(QcPhoto photo) =>
        !string.IsNullOrWhiteSpace(photo.PresentationStorageKey)
            ? photo.PresentationStorageKey
            : OriginalStorageKey(photo);

    public static string DisplayContentType(QcPhoto photo) =>
        !string.IsNullOrWhiteSpace(photo.PresentationStorageKey)
            && !string.IsNullOrWhiteSpace(photo.PresentationContentType)
                ? photo.PresentationContentType
                : photo.ContentType;

    private static async Task<MemoryStream> CopySourceAsync(Stream original, CancellationToken cancellationToken)
    {
        if (original.CanSeek && original.Length > MaxSourceBytes)
        {
            throw new PhotoProcessingException("The selected photo is too large to process safely.");
        }

        var source = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await original.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (source.Length + read > MaxSourceBytes)
            {
                await source.DisposeAsync();
                throw new PhotoProcessingException("The selected photo is too large to process safely.");
            }
            await source.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        source.Position = 0;
        return source;
    }

    private static (string FormatName, string ContentType, string Extension) ValidateDetectedFormat(
        IImageFormat format,
        string fileName,
        string declaredContentType)
    {
        var formatName = format.Name.ToUpperInvariant();
        var expected = formatName switch
        {
            "JPEG" => (ContentType: "image/jpeg", Extensions: new[] { ".jpg", ".jpeg" }, Extension: ".jpg"),
            "PNG" => (ContentType: "image/png", Extensions: new[] { ".png" }, Extension: ".png"),
            "WEBP" => (ContentType: "image/webp", Extensions: new[] { ".webp" }, Extension: ".webp"),
            _ => throw new PhotoProcessingException("Only JPG, PNG, or WEBP images are allowed.")
        };

        var extension = Path.GetExtension(fileName);
        var normalizedContentType = declaredContentType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase)
            ? "image/jpeg"
            : declaredContentType;
        if (!expected.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            || !expected.ContentType.Equals(normalizedContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new PhotoProcessingException("The image file contents do not match its file extension and content type.");
        }

        return (formatName, expected.ContentType, expected.Extension);
    }

    private static int ReadExifOrientation(Image image)
    {
        try
        {
            if (image.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out var orientation) == true
                && orientation.Value is >= 1 and <= 8)
            {
                return orientation.Value;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Malformed orientation metadata is treated as normal orientation. The
            // decoded pixels remain available and manual rotation can still correct it.
        }

        return 1;
    }

    public static void ApplyManualRotation(Image image, int manualRotationQuarterTurns)
    {
        var normalized = NormalizeQuarterTurns(manualRotationQuarterTurns);
        if (normalized == 0) return;
        image.Mutate(context => context.Rotate(normalized switch
        {
            1 => RotateMode.Rotate90,
            2 => RotateMode.Rotate180,
            _ => RotateMode.Rotate270
        }));
    }

    private static Task SavePresentationAsync(Image image, Stream destination, string formatName, CancellationToken cancellationToken) =>
        formatName switch
        {
            "JPEG" => image.SaveAsJpegAsync(destination, new JpegEncoder { Quality = 92 }, cancellationToken),
            "PNG" => image.SaveAsPngAsync(destination, new PngEncoder(), cancellationToken),
            "WEBP" => image.SaveAsWebpAsync(destination, new WebpEncoder { Quality = 92 }, cancellationToken),
            _ => throw new PhotoProcessingException("Only JPG, PNG, or WEBP images are allowed.")
        };
}

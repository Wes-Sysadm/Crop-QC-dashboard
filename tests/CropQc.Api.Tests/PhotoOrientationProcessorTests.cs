using System.Security.Cryptography;
using CropQc.Web.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace CropQc.Api.Tests;

public sealed class PhotoOrientationProcessorTests
{
    public static TheoryData<int, string, bool> ExifCases => new()
    {
        { 1, "ABCD", false },
        { 2, "BADC", false },
        { 3, "DCBA", false },
        { 4, "CDAB", false },
        { 5, "ACBD", true },
        { 6, "CADB", true },
        { 7, "DBCA", true },
        { 8, "BDAC", true }
    };

    [Theory]
    [MemberData(nameof(ExifCases))]
    public async Task Exif_orientation_1_through_8_normalizes_real_pixels_without_changing_original(
        int orientation,
        string expectedCorners,
        bool swapsDimensions)
    {
        var original = await CreateJpegAsync(orientation);
        var originalHash = SHA256.HashData(original);

        await using var input = new MemoryStream(original, writable: false);
        var result = await PhotoOrientationProcessor.CreatePresentationAsync(
            input, "orientation.jpg", "image/jpeg", 0, CancellationToken.None);

        Assert.Equal(originalHash, SHA256.HashData(original));
        Assert.Equal(orientation, result.OriginalExifOrientation);
        Assert.Equal(swapsDimensions ? 60 : 80, result.Width);
        Assert.Equal(swapsDimensions ? 80 : 60, result.Height);
        await AssertCornerOrderAsync(result.Bytes, expectedCorners);

        using var presentation = Image.Load(result.Bytes);
        Assert.False(presentation.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out _) == true);
    }

    public static TheoryData<int, string> ManualCases => new()
    {
        { 0, "ABCD" },
        { 1, "CADB" },
        { 2, "DCBA" },
        { 3, "BDAC" },
        { 4, "ABCD" },
        { -1, "BDAC" }
    };

    [Theory]
    [MemberData(nameof(ManualCases))]
    public async Task Manual_quarter_turns_are_normalized_and_always_regenerated_from_original(
        int turns,
        string expectedCorners)
    {
        var original = await CreatePngAsync();
        var originalHash = SHA256.HashData(original);

        await using var input = new MemoryStream(original, writable: false);
        var result = await PhotoOrientationProcessor.CreatePresentationAsync(
            input, "manual.png", "image/png", turns, CancellationToken.None);

        Assert.Equal(originalHash, SHA256.HashData(original));
        await AssertCornerOrderAsync(result.Bytes, expectedCorners);
    }

    [Fact]
    public async Task Exif_is_applied_before_manual_rotation()
    {
        var original = await CreateJpegAsync(6);
        await using var input = new MemoryStream(original, writable: false);

        var result = await PhotoOrientationProcessor.CreatePresentationAsync(
            input, "phone.jpg", "image/jpeg", 1, CancellationToken.None);

        await AssertCornerOrderAsync(result.Bytes, "DCBA");
    }

    [Fact]
    public async Task Png_without_exif_is_supported()
    {
        var original = await CreatePngAsync();
        await using var input = new MemoryStream(original, writable: false);

        var result = await PhotoOrientationProcessor.CreatePresentationAsync(
            input, "camera.png", "image/png", 0, CancellationToken.None);

        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(1, result.OriginalExifOrientation);
        await AssertCornerOrderAsync(result.Bytes, "ABCD");
    }

    [Theory]
    [InlineData(40, 80)]
    [InlineData(80, 40)]
    public async Task Normal_portrait_and_landscape_jpegs_without_orientation_remain_physical(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, Color.CornflowerBlue);
        await using var encoded = new MemoryStream();
        await image.SaveAsJpegAsync(encoded, new JpegEncoder { Quality = 100 });
        var original = encoded.ToArray();
        var originalHash = SHA256.HashData(original);
        await using var input = new MemoryStream(original, writable: false);

        var result = await PhotoOrientationProcessor.CreatePresentationAsync(
            input, "normal.jpg", "image/jpeg", 0, CancellationToken.None);

        Assert.Equal(1, result.OriginalExifOrientation);
        Assert.Equal(width, result.Width);
        Assert.Equal(height, result.Height);
        Assert.Equal(originalHash, SHA256.HashData(original));
    }

    [Fact]
    public async Task Out_of_range_orientation_is_treated_as_normal_and_remains_manually_rotatable()
    {
        var original = await CreateJpegAsync(9);
        var originalHash = SHA256.HashData(original);
        await using var input = new MemoryStream(original, writable: false);

        var result = await PhotoOrientationProcessor.CreatePresentationAsync(
            input, "invalid-orientation.jpg", "image/jpeg", 1, CancellationToken.None);

        Assert.Equal(1, result.OriginalExifOrientation);
        await AssertCornerOrderAsync(result.Bytes, "CADB");
        Assert.Equal(originalHash, SHA256.HashData(original));
    }

    [Fact]
    public async Task Malformed_exif_header_is_ignored_without_partial_or_nondeterministic_processing()
    {
        var original = await CreateJpegAsync(6);
        var exifOffset = original.AsSpan().IndexOf("Exif\0\0"u8);
        Assert.True(exifOffset >= 0);
        original[exifOffset + 6] = (byte)'Z';
        original[exifOffset + 7] = (byte)'Z';
        var originalHash = SHA256.HashData(original);

        await using var firstInput = new MemoryStream(original, writable: false);
        await using var secondInput = new MemoryStream(original, writable: false);
        var first = await PhotoOrientationProcessor.CreatePresentationAsync(
            firstInput, "malformed-exif.jpg", "image/jpeg", 1, CancellationToken.None);
        var second = await PhotoOrientationProcessor.CreatePresentationAsync(
            secondInput, "malformed-exif.jpg", "image/jpeg", 1, CancellationToken.None);

        Assert.Equal(1, first.OriginalExifOrientation);
        Assert.Equal(first.Bytes, second.Bytes);
        await AssertCornerOrderAsync(first.Bytes, "CADB");
        Assert.Equal(originalHash, SHA256.HashData(original));
    }

    [Fact]
    public async Task Corrupt_image_is_rejected_cleanly()
    {
        await using var input = new MemoryStream([0xff, 0xd8, 0xff, 0xd9]);
        var error = await Assert.ThrowsAsync<PhotoProcessingException>(() =>
            PhotoOrientationProcessor.CreatePresentationAsync(
                input, "corrupt.jpg", "image/jpeg", 0, CancellationToken.None));

        Assert.Contains("corrupt", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Signature_extension_or_mime_mismatch_is_rejected()
    {
        var png = await CreatePngAsync();
        await using var input = new MemoryStream(png, writable: false);

        var error = await Assert.ThrowsAsync<PhotoProcessingException>(() =>
            PhotoOrientationProcessor.CreatePresentationAsync(
                input, "pretend.jpg", "image/jpeg", 0, CancellationToken.None));

        Assert.Contains("do not match", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Excessive_decoded_dimension_is_rejected_before_full_decode()
    {
        using var image = new Image<Rgba32>(PhotoOrientationProcessor.MaxDecodedDimension + 1, 1);
        await using var encoded = new MemoryStream();
        await image.SaveAsPngAsync(encoded, new PngEncoder());
        encoded.Position = 0;

        var error = await Assert.ThrowsAsync<PhotoProcessingException>(() =>
            PhotoOrientationProcessor.CreatePresentationAsync(
                encoded, "too-wide.png", "image/png", 0, CancellationToken.None));

        Assert.Contains("too large", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(-5, 3)]
    [InlineData(-1, 3)]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(9, 1)]
    public void Quarter_turn_state_is_deterministic(int value, int expected) =>
        Assert.Equal(expected, PhotoOrientationProcessor.NormalizeQuarterTurns(value));

    private static async Task<byte[]> CreateJpegAsync(int orientation)
    {
        using var image = MarkerImage();
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)orientation);
        await using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 100 });
        return output.ToArray();
    }

    private static async Task<byte[]> CreatePngAsync()
    {
        using var image = MarkerImage();
        await using var output = new MemoryStream();
        await image.SaveAsPngAsync(output, new PngEncoder());
        return output.ToArray();
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

    private static async Task AssertCornerOrderAsync(byte[] bytes, string expected)
    {
        using var image = Image.Load<Rgba32>(bytes);
        var actual = string.Concat(
            Marker(image[image.Width / 4, image.Height / 4]),
            Marker(image[image.Width * 3 / 4, image.Height / 4]),
            Marker(image[image.Width / 4, image.Height * 3 / 4]),
            Marker(image[image.Width * 3 / 4, image.Height * 3 / 4]));
        Assert.Equal(expected, actual);
        await Task.CompletedTask;
    }

    private static char Marker(Rgba32 pixel)
    {
        if (pixel.R > 180 && pixel.G < 100 && pixel.B < 100) return 'A';
        if (pixel.G > 150 && pixel.R < 100 && pixel.B < 100) return 'B';
        if (pixel.B > 150 && pixel.R < 100 && pixel.G < 100) return 'C';
        if (pixel.R > 150 && pixel.G > 150 && pixel.B < 100) return 'D';
        return '?';
    }
}

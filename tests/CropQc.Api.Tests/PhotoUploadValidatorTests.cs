using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;

namespace CropQc.Api.Tests;

public sealed class PhotoUploadValidatorTests
{
    [Fact]
    public void Upload_file_without_file_returns_clear_validation_error()
    {
        var error = PhotoUploadValidator.Validate(new AddPhotoMetadataForm
        {
            PhotoSource = "Upload File",
            PhotoType = "BinTruck"
        });

        Assert.Equal("No photo file was selected.", error);
    }

    [Theory]
    [InlineData("application/pdf", "test.pdf")]
    [InlineData("image/gif", "test.gif")]
    [InlineData("image/jpeg", "test.txt")]
    public void Rejected_file_type_returns_clear_validation_error(string contentType, string fileName)
    {
        var error = PhotoUploadValidator.Validate(new AddPhotoMetadataForm
        {
            PhotoSource = "Upload File",
            PhotoType = "BinTruck",
            PhotoFile = FormFile(fileName, contentType)
        });

        Assert.Equal("Only JPG, PNG, or WEBP images are allowed.", error);
    }

    [Theory]
    [InlineData("image/jpeg", "test.jpg")]
    [InlineData("image/jpeg", "test.jpeg")]
    [InlineData("image/png", "test.png")]
    [InlineData("image/webp", "test.webp")]
    public void Accepted_image_types_pass_validation(string contentType, string fileName)
    {
        var error = PhotoUploadValidator.Validate(new AddPhotoMetadataForm
        {
            PhotoSource = "Upload File",
            PhotoType = "BinTruck",
            PhotoFile = FormFile(fileName, contentType)
        });

        Assert.Null(error);
    }

    [Fact]
    public void Oversized_image_returns_clear_validation_error()
    {
        var stream = new MemoryStream([1]);
        var file = new FormFile(stream, 0, PhotoUploadValidator.MaxPhotoSizeBytes + 1, "PhotoFile", "large.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };

        var error = PhotoUploadValidator.Validate(new AddPhotoMetadataForm
        {
            PhotoSource = "USB Camera",
            PhotoType = "SampleBeforeCutting",
            PhotoFile = file
        });

        Assert.Equal("Photos must be 15 MB or smaller.", error);
    }

    private static IFormFile FormFile(string fileName, string contentType)
    {
        var stream = new MemoryStream([1, 2, 3]);
        return new FormFile(stream, 0, stream.Length, "PhotoFile", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}

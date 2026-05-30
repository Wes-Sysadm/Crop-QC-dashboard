using CropQc.Shared.Security;

namespace CropQc.Api.Tests.QcStation;

public sealed class QcStationApiKeyValidatorTests
{
    [Fact]
    public void Validate_ReturnsValidForMatchingKey()
    {
        var result = QcStationApiKeyValidator.Validate("secret-key", "secret-key");

        Assert.Equal(QcStationApiKeyValidationResult.Valid, result);
    }

    [Fact]
    public void Validate_RejectsMissingKey()
    {
        var result = QcStationApiKeyValidator.Validate("secret-key", null);

        Assert.Equal(QcStationApiKeyValidationResult.Missing, result);
    }

    [Fact]
    public void Validate_RejectsInvalidKey()
    {
        var result = QcStationApiKeyValidator.Validate("secret-key", "wrong-key");

        Assert.Equal(QcStationApiKeyValidationResult.Invalid, result);
    }

    [Fact]
    public void Validate_ReportsMissingConfiguration()
    {
        var result = QcStationApiKeyValidator.Validate("", "secret-key");

        Assert.Equal(QcStationApiKeyValidationResult.NotConfigured, result);
    }
}

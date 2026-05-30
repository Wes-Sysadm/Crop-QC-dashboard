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

    [Fact]
    public void GenerateApiKey_ReturnsLongRandomSecret()
    {
        var first = QcStationApiKeyValidator.GenerateApiKey();
        var second = QcStationApiKeyValidator.GenerateApiKey();

        Assert.True(first.Length >= 48);
        Assert.True(second.Length >= 48);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void HashApiKey_DoesNotStoreRawKeyAndCanBeVerified()
    {
        var rawKey = QcStationApiKeyValidator.GenerateApiKey();
        var hash = QcStationApiKeyValidator.HashApiKey(rawKey);

        Assert.NotEqual(rawKey, hash);
        Assert.True(QcStationApiKeyValidator.VerifyHashedApiKey(rawKey, hash));
        Assert.False(QcStationApiKeyValidator.VerifyHashedApiKey(rawKey + "x", hash));
    }
}

namespace CropQc.Shared.Security;

public static class QcStationApiKeyValidator
{
    public const string HeaderName = "X-QC-STATION-API-KEY";

    public static QcStationApiKeyValidationResult Validate(string? configuredApiKey, string? providedApiKey)
    {
        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            return QcStationApiKeyValidationResult.NotConfigured;
        }

        if (string.IsNullOrWhiteSpace(providedApiKey))
        {
            return QcStationApiKeyValidationResult.Missing;
        }

        return string.Equals(configuredApiKey.Trim(), providedApiKey.Trim(), StringComparison.Ordinal)
            ? QcStationApiKeyValidationResult.Valid
            : QcStationApiKeyValidationResult.Invalid;
    }
}

public enum QcStationApiKeyValidationResult
{
    Valid,
    Missing,
    Invalid,
    NotConfigured
}

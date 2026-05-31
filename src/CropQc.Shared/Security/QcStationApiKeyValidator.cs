namespace CropQc.Shared.Security;

using System.Security.Cryptography;

public static class QcStationApiKeyValidator
{
    public const string HeaderName = "X-QC-STATION-API-KEY";
    public const string StationCodeHeaderName = "X-QC-STATION-CODE";
    public const string StationCodePattern = "^[A-Za-z0-9_-]+$";

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

    public static string GenerateApiKey()
    {
        Span<byte> bytes = stackalloc byte[48];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string HashApiKey(string apiKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(apiKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public static bool VerifyHashedApiKey(string apiKey, string apiKeyHash)
    {
        var providedHash = HashApiKey(apiKey);
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(providedHash),
            System.Text.Encoding.UTF8.GetBytes(apiKeyHash));
    }

    public static bool IsStationCodeSafe(string? stationCode) =>
        !string.IsNullOrWhiteSpace(stationCode)
        && System.Text.RegularExpressions.Regex.IsMatch(stationCode.Trim(), StationCodePattern);

    public static string NormalizeStationCode(string value)
    {
        var normalized = System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", "-");
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^A-Za-z0-9_-]+", "-");
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, "-{2,}", "-").Trim('-');
        return normalized.ToUpperInvariant();
    }
}

public enum QcStationApiKeyValidationResult
{
    Valid,
    Missing,
    Invalid,
    NotConfigured
}

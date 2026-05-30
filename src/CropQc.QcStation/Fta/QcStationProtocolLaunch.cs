namespace CropQc.QcStation.Fta;

public sealed record QcStationProtocolLaunch(long? SampleId, long? ReceiptId)
{
    public static bool IsProtocolArgument(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.StartsWith("cropqcstation://", StringComparison.OrdinalIgnoreCase);

    public static QcStationProtocolLaunch? Parse(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "cropqcstation", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var route = uri.Host;
        var idSegment = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!long.TryParse(idSegment, out var id) || id <= 0)
        {
            return null;
        }

        return route.ToLowerInvariant() switch
        {
            "sample" => new QcStationProtocolLaunch(id, null),
            "receipt" => new QcStationProtocolLaunch(null, id),
            _ => null
        };
    }
}

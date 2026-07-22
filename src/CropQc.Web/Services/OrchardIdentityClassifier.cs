using System.Text.RegularExpressions;

namespace CropQc.Web.Services;

public enum OrchardIdentitySource
{
    AmbiguousOrchardOrGrower,
    ConfirmedOrchardName,
    GrowerNumber
}

public enum OrchardIdentityKind
{
    Empty,
    OrchardName,
    GrowerNumber
}

public sealed record OrchardIdentityClassification(
    string Value,
    OrchardIdentityKind Kind,
    OrchardIdentitySource Source);

public static partial class OrchardIdentityClassifier
{
    public static OrchardIdentityClassification Classify(string? value, OrchardIdentitySource source)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return new OrchardIdentityClassification("", OrchardIdentityKind.Empty, source);
        }

        if (source == OrchardIdentitySource.GrowerNumber)
        {
            return new OrchardIdentityClassification(trimmed, OrchardIdentityKind.GrowerNumber, source);
        }

        if (source == OrchardIdentitySource.ConfirmedOrchardName)
        {
            return new OrchardIdentityClassification(trimmed, OrchardIdentityKind.OrchardName, source);
        }

        return new OrchardIdentityClassification(
            trimmed,
            StandaloneFourDigitNumber().IsMatch(trimmed) ? OrchardIdentityKind.GrowerNumber : OrchardIdentityKind.OrchardName,
            source);
    }

    public static bool IsStandaloneFourDigitGrowerNumber(string? value) =>
        Classify(value, OrchardIdentitySource.AmbiguousOrchardOrGrower).Kind == OrchardIdentityKind.GrowerNumber;

    public static string NormalizeGrowerNumber(string? value) =>
        Classify(value, OrchardIdentitySource.GrowerNumber).Value;

    [GeneratedRegex("^[0-9]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneFourDigitNumber();
}

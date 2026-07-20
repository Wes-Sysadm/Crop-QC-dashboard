using System.Text;
using System.Text.RegularExpressions;

namespace CropQc.Web.Services;

public static partial class OrchardBlockMatcher
{
    public const decimal AutomaticMatchThreshold = 0.94m;
    public const decimal SuggestionThreshold = 0.78m;

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var builder = new StringBuilder(value.Trim().ToUpperInvariant().Length);
        foreach (var ch in value.Trim().ToUpperInvariant())
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        var collapsed = RepeatedWhitespace().Replace(builder.ToString(), " ").Trim();
        return collapsed
            .Replace(" BLOCK ", " BLK ", StringComparison.Ordinal)
            .Replace("BLOCK ", "BLK ", StringComparison.Ordinal)
            .Replace(" BLK", " BLK", StringComparison.Ordinal);
    }

    public static decimal Similarity(string left, string right)
    {
        left = Normalize(left);
        right = Normalize(right);
        if (left.Length == 0 || right.Length == 0)
        {
            return 0m;
        }

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return 1m;
        }

        if (!SameNumericTokens(left, right))
        {
            return 0m;
        }

        var distance = Levenshtein(left, right);
        var max = Math.Max(left.Length, right.Length);
        return decimal.Round(1m - distance / (decimal)max, 4);
    }

    public static bool IsAutomaticMatch(string orchard, string requestedBlock, string candidateOrchard, string candidateBlock, bool uniqueCandidate) =>
        uniqueCandidate
        && string.Equals(Normalize(orchard), Normalize(candidateOrchard), StringComparison.Ordinal)
        && Similarity(requestedBlock, candidateBlock) >= AutomaticMatchThreshold;

    private static bool SameNumericTokens(string left, string right)
    {
        var leftNumbers = NumberToken().Matches(left).Select(x => x.Value).ToArray();
        var rightNumbers = NumberToken().Matches(right).Select(x => x.Value).ToArray();
        return leftNumbers.SequenceEqual(rightNumbers, StringComparer.Ordinal);
    }

    private static int Levenshtein(string left, string right)
    {
        var distances = new int[left.Length + 1, right.Length + 1];
        for (var i = 0; i <= left.Length; i++) distances[i, 0] = i;
        for (var j = 0; j <= right.Length; j++) distances[0, j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);
            }
        }

        return distances[left.Length, right.Length];
    }

    [GeneratedRegex("\\s+")]
    private static partial Regex RepeatedWhitespace();

    [GeneratedRegex("\\d+")]
    private static partial Regex NumberToken();
}

using System.Text.RegularExpressions;
using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface ICanonicalGrowerService
{
    Task<CanonicalGrowerResolutionSet> LoadResolutionSetAsync(CancellationToken cancellationToken);
    Task EnsureSeedMappingsAsync(CancellationToken cancellationToken);
}

public sealed record CanonicalGrowerIdentity(string Key, string DisplayName, bool IsMapped, int? CanonicalGrowerId);

public sealed class CanonicalGrowerResolutionSet(
    IReadOnlyDictionary<string, IReadOnlyList<CanonicalGrowerIdentity>> aliases,
    IReadOnlyDictionary<string, IReadOnlyList<CanonicalGrowerIdentity>> numbers)
{
    public CanonicalGrowerIdentity Resolve(string? growerName, string? growerNumber)
    {
        var numberKey = CanonicalGrowerService.NormalizeGrowerNumber(growerNumber);
        if (numberKey.Length > 0
            && numbers.TryGetValue(numberKey, out var numberMatches)
            && numberMatches.Count == 1)
        {
            return numberMatches[0];
        }

        var aliasKey = CanonicalGrowerService.NormalizeGrowerKey(growerName);
        if (aliasKey.Length > 0
            && aliases.TryGetValue(aliasKey, out var aliasMatches)
            && aliasMatches.Count == 1)
        {
            return aliasMatches[0];
        }

        if (numberKey.Length == 0
            && aliasKey.Length > 0
            && !aliases.ContainsKey(aliasKey)
            && CanonicalGrowerService.TryGetKnownCanonicalAlias(growerName, out var known))
        {
            return new CanonicalGrowerIdentity(
                CanonicalGrowerService.NormalizeGrowerKey(known.CanonicalName),
                known.CanonicalName,
                true,
                null);
        }

        var fallbackName = string.IsNullOrWhiteSpace(growerName) ? "Unknown grower" : growerName.Trim();
        var fallbackKey = CanonicalGrowerService.NormalizeGrowerKey($"{fallbackName}|{numberKey}");
        return new CanonicalGrowerIdentity(
            fallbackKey.Length == 0 ? "UNMAPPED_GROWER" : fallbackKey,
            $"{fallbackName} (Grower mapping needed)",
            false,
            null);
    }

    public string DisplayName(string? growerName, string? growerNumber)
    {
        var resolved = Resolve(growerName, growerNumber);
        return resolved.IsMapped
            ? resolved.DisplayName
            : growerName?.Trim() ?? "";
    }

    public IReadOnlyList<string> MatchingGrowerNumbers(string? growerName)
    {
        var aliasKey = CanonicalGrowerService.NormalizeGrowerKey(growerName);
        if (aliasKey.Length == 0 || !aliases.TryGetValue(aliasKey, out var matches)) return [];
        var identityKeys = matches.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return numbers
            .Where(x => x.Value.Any(identity => identityKeys.Contains(identity.Key)))
            .Select(x => x.Key)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed partial class CanonicalGrowerService(CropQcDbContext dbContext) : ICanonicalGrowerService
{
    private static readonly IReadOnlyList<KnownGrowerAlias> KnownAliases =
    [
        new("Vantage Orchard", "Vantage Orchard", 0),
        new("Vantage Orchard Non Chilean", "Vantage Orchard", 1),
        new("Stayman Flats", "Stayman Flats", 0),
        new("Stayman", "Stayman Flats", 1),
        new("Stayman Flats Non Chilean", "Stayman Flats", 2)
    ];

    public async Task<CanonicalGrowerResolutionSet> LoadResolutionSetAsync(CancellationToken cancellationToken)
    {
        await EnsureSeedMappingsAsync(cancellationToken);
        var growers = await dbContext.CanonicalGrowers.AsNoTracking()
            .Include(x => x.Aliases)
            .Include(x => x.GrowerNumbers)
            .Where(x => x.IsActive && x.MergedIntoCanonicalGrowerId == null)
            .ToListAsync(cancellationToken);

        var aliasCandidates = new List<(string Key, CanonicalGrowerIdentity Identity)>();
        foreach (var grower in growers)
        {
            var identity = IdentityFromGrower(grower);
            aliasCandidates.Add((NormalizeGrowerKey(grower.DisplayName), identity));
            foreach (var alias in grower.Aliases.Where(x => x.IsActive))
            {
                aliasCandidates.Add((NormalizeGrowerKey(alias.AliasName), identity));
                aliasCandidates.Add((alias.NormalizedAliasKey, identity));
            }
        }

        foreach (var alias in KnownAliases.OrderBy(x => x.Priority))
        {
            var canonicalKey = NormalizeGrowerKey(alias.CanonicalName);
            var identity = aliasCandidates
                .Where(x => x.Key.Equals(canonicalKey, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Identity)
                .DistinctBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .SingleOrDefault();
            if (identity is null)
            {
                identity = new CanonicalGrowerIdentity(canonicalKey, alias.CanonicalName, true, null);
            }

            aliasCandidates.Add((NormalizeGrowerKey(alias.Alias), identity));
        }

        var aliasMap = aliasCandidates
            .Where(x => x.Key.Length > 0)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<CanonicalGrowerIdentity>)x.Select(y => y.Identity)
                    .DistinctBy(y => y.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var numberMap = growers
            .SelectMany(grower =>
            {
                var identity = IdentityFromGrower(grower);
                return grower.GrowerNumbers
                    .Where(number => number.IsActive)
                    .Select(number => new { Key = NormalizeGrowerNumber(number.GrowerNumber), Identity = identity });
            })
            .Where(x => x.Key.Length > 0)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<CanonicalGrowerIdentity>)x.Select(y => y.Identity).DistinctBy(y => y.Key, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        return new CanonicalGrowerResolutionSet(aliasMap, numberMap);
    }

    public async Task EnsureSeedMappingsAsync(CancellationToken cancellationToken)
    {
        foreach (var group in KnownAliases.GroupBy(x => x.CanonicalName, StringComparer.OrdinalIgnoreCase))
        {
            var identity = NormalizeGrowerKey(group.Key);
            var grower = await dbContext.CanonicalGrowers
                .Include(x => x.Aliases)
                .SingleOrDefaultAsync(x => x.NormalizedKey == identity, cancellationToken);
            if (grower is null)
            {
                grower = new CanonicalGrower
                {
                    DisplayName = group.Key,
                    NormalizedKey = identity,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    IsActive = true
                };
                dbContext.CanonicalGrowers.Add(grower);
            }
            else if (!grower.DisplayName.Equals(group.Key, StringComparison.Ordinal))
            {
                grower.DisplayName = group.Key;
                grower.UpdatedAt = DateTimeOffset.UtcNow;
            }

            foreach (var alias in group)
            {
                var aliasKey = NormalizeGrowerKey(alias.Alias);
                if (!grower.Aliases.Any(x => x.NormalizedAliasKey == aliasKey))
                {
                    grower.Aliases.Add(new CanonicalGrowerAlias
                    {
                        AliasName = alias.Alias,
                        NormalizedAliasKey = aliasKey,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        IsActive = true
                    });
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public static bool TryGetKnownCanonicalAlias(string? value, out KnownGrowerAlias alias)
    {
        alias = default;
        var key = AliasLookupKey(value);
        if (key.Length == 0)
        {
            return false;
        }

        var match = KnownAliases
            .OrderBy(x => x.Priority)
            .FirstOrDefault(x => AliasLookupKey(x.Alias).Equals(key, StringComparison.OrdinalIgnoreCase));
        if (match.Alias is null)
        {
            return false;
        }

        alias = match;
        return true;
    }

    public static string NormalizeGrowerKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var normalized = NonChileanPattern().Replace(value.Trim(), " Non Chilean");
        normalized = PunctuationPattern().Replace(normalized, " ");
        normalized = WhitespacePattern().Replace(normalized, " ").Trim().ToUpperInvariant();
        return normalized.Replace(" ", "_", StringComparison.Ordinal);
    }

    public static string NormalizeGrowerNumber(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string AliasLookupKey(string? value) =>
        new string((value ?? "")
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static CanonicalGrowerIdentity IdentityFromGrower(CanonicalGrower grower) =>
        new(grower.NormalizedKey, grower.DisplayName, true, grower.Id);

    [GeneratedRegex(@"[-_\s]*non[-_\s]*chilean$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonChileanPattern();

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex PunctuationPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}

public readonly record struct KnownGrowerAlias(string Alias, string CanonicalName, int Priority);

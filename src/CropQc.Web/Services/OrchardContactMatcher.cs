using CropQc.Data.Entities;
using CropQc.Web.Models;

namespace CropQc.Web.Services;

public sealed record CanonicalOrchardMatchSource(
    int Id,
    string OrchardName,
    IReadOnlyList<(string AliasText, string NormalizedAlias)> Aliases,
    IReadOnlyList<(int Id, string Email, string NormalizedEmail, bool IsActive, bool IsDeleted)> Recipients);

public static class OrchardContactMatcher
{
    public static OrchardContactDryRunRowViewModel Match(
        ParsedOrchardManagerToken token,
        IReadOnlyList<CanonicalOrchardMatchSource> orchards)
    {
        var tokenKey = OrchardContactNormalization.NormalizeOrchardIdentity(token.ParsedOrchardToken);
        if (OrchardIdentityClassifier.IsStandaloneFourDigitGrowerNumber(token.ParsedOrchardToken))
        {
            return Result(
                token,
                OrchardContactMatchMethods.InvalidOrchardIdentity,
                null,
                [],
                "A standalone four-digit value is a grower number, not an orchard. No orchard or recipient will be created.");
        }

        var exact = orchards.Where(x =>
                OrchardContactNormalization.NormalizeOrchardIdentity(x.OrchardName) == tokenKey)
            .ToList();
        if (exact.Count == 1)
        {
            return Result(token, OrchardContactMatchMethods.Exact, exact[0], [], null, 1m);
        }

        if (exact.Count > 1)
        {
            return Result(
                token,
                OrchardContactMatchMethods.Ambiguous,
                null,
                Candidates(exact, token, "More than one canonical orchard has the same normalized name."),
                "Multiple canonical orchards share this normalized identity. An administrator must choose.");
        }

        var alias = orchards.Where(x => x.Aliases.Any(a => a.NormalizedAlias == tokenKey)).ToList();
        if (alias.Count == 1)
        {
            return Result(token, OrchardContactMatchMethods.Alias, alias[0], [], null, 1m);
        }

        if (alias.Count > 1)
        {
            return Result(
                token,
                OrchardContactMatchMethods.Ambiguous,
                null,
                Candidates(alias, token, "The normalized alias is assigned to more than one orchard."),
                "The alias is ambiguous. An administrator must choose.");
        }

        var tokenWithoutOrchard = OrchardContactNormalization.WithoutOrchardWord(tokenKey);
        var deterministic = orchards.Where(x =>
            {
                var orchardKey = OrchardContactNormalization.NormalizeOrchardIdentity(x.OrchardName);
                return tokenWithoutOrchard.Length >= 3
                    && OrchardContactNormalization.WithoutOrchardWord(orchardKey) == tokenWithoutOrchard;
            })
            .ToList();
        if (deterministic.Count == 1)
        {
            return Result(
                token,
                OrchardContactMatchMethods.ProposedAlias,
                deterministic[0],
                [],
                "Strong deterministic variant only. Review and approve before creating the alias or recipient.",
                0.99m);
        }

        if (deterministic.Count > 1)
        {
            return Result(
                token,
                OrchardContactMatchMethods.Ambiguous,
                null,
                Candidates(deterministic, token, "The deterministic orchard-name variant matched more than one orchard."),
                "The deterministic variant is ambiguous. An administrator must choose.");
        }

        var addressEvidence = OrchardContactNormalization.ParentheticalAddressEvidence(token.PhysicalAddress);
        var addressKey = OrchardContactNormalization.NormalizeOrchardIdentity(addressEvidence);
        var addressCandidates = string.IsNullOrWhiteSpace(addressKey)
            ? []
            : orchards.Where(x =>
                    OrchardContactNormalization.NormalizeOrchardIdentity(x.OrchardName) == addressKey
                    || x.Aliases.Any(a => a.NormalizedAlias == addressKey))
                .ToList();

        var fuzzy = orchards
            .Select(x =>
            {
                var orchardScore = OrchardBlockMatcher.Similarity(token.ParsedOrchardToken, x.OrchardName);
                var aliasMatch = x.Aliases
                    .Select(a => new { a.AliasText, Score = OrchardBlockMatcher.Similarity(token.ParsedOrchardToken, a.AliasText) })
                    .OrderByDescending(a => a.Score)
                    .FirstOrDefault();
                var score = Math.Max(orchardScore, aliasMatch?.Score ?? 0m);
                var supportingAddress = addressCandidates.Any(a => a.Id == x.Id) ? addressEvidence : null;
                return new OrchardMatchCandidateViewModel(
                    x.Id,
                    x.OrchardName,
                    score,
                    aliasMatch?.Score > orchardScore ? aliasMatch.AliasText : null,
                    supportingAddress,
                    supportingAddress is not null
                        ? "Parenthetical address evidence supports this review candidate; it does not override the orchard token."
                        : "Fuzzy similarity candidate for review only.");
            })
            .Where(x => x.SimilarityScore >= 0.30m || x.AddressEvidence is not null)
            .OrderByDescending(x => x.AddressEvidence is not null)
            .ThenByDescending(x => x.SimilarityScore)
            .ThenBy(x => x.OrchardName)
            .Take(5)
            .ToList();

        return Result(
            token,
            OrchardContactMatchMethods.Unmatched,
            null,
            fuzzy,
            fuzzy.Count == 0
                ? "No existing canonical orchard matched. No orchard or recipient will be created."
                : "Candidate suggestions are review-only. No orchard or recipient will be created without approval.");
    }

    private static OrchardContactDryRunRowViewModel Result(
        ParsedOrchardManagerToken token,
        string method,
        CanonicalOrchardMatchSource? orchard,
        IReadOnlyList<OrchardMatchCandidateViewModel> candidates,
        string? warning,
        decimal? score = null)
    {
        var existing = orchard?.Recipients
            .Where(x => !x.IsDeleted)
            .Select(x => x.Email)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToArray() ?? [];
        var duplicate = orchard is not null
            && token.NormalizedEmailAddress is not null
            && orchard.Recipients.Any(x =>
                !x.IsDeleted
                && string.Equals(x.NormalizedEmail, token.NormalizedEmailAddress, StringComparison.OrdinalIgnoreCase));
        var conflict = orchard is not null
            && token.EmailIsValid
            && orchard.Recipients.Any(x =>
                x.IsActive
                && !x.IsDeleted
                && !string.Equals(x.NormalizedEmail, token.NormalizedEmailAddress, StringComparison.OrdinalIgnoreCase));
        var proposedAction =
            !token.EmailIsValid ? "Retain contact for review; do not create an active recipient"
            : orchard is null ? "Review required; no database action"
            : duplicate ? "Skip duplicate recipient"
            : conflict ? "Retain existing recipient and review adding this manager alongside it"
            : "Add recipient after explicit approval";
        return new OrchardContactDryRunRowViewModel(
            token.WorkbookRowNumber,
            token.OriginalOrchardCell,
            token.ParsedOrchardToken,
            token.ManagerDisplayName,
            token.EmailAddress,
            token.EmailIsValid,
            token.Phone,
            token.PhysicalAddress,
            method,
            score,
            orchard?.Id,
            orchard?.OrchardName,
            candidates,
            existing,
            proposedAction,
            warning,
            duplicate,
            conflict);
    }

    private static IReadOnlyList<OrchardMatchCandidateViewModel> Candidates(
        IReadOnlyList<CanonicalOrchardMatchSource> orchards,
        ParsedOrchardManagerToken token,
        string reason) =>
        orchards.Select(x => new OrchardMatchCandidateViewModel(
                x.Id,
                x.OrchardName,
                1m,
                x.Aliases.FirstOrDefault(a =>
                    a.NormalizedAlias == OrchardContactNormalization.NormalizeOrchardIdentity(token.ParsedOrchardToken)).AliasText,
                null,
                reason))
            .ToArray();
}

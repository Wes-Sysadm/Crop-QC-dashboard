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
        OrchardIdentityResolutionSet resolutionSet)
    {
        var canonicalMatch = Match(token, resolutionSet.Orchards, stopBeforeDeterministicAndFuzzy: true);
        if (canonicalMatch is not null) return canonicalMatch;

        var tokenKey = OrchardContactNormalization.NormalizeOrchardIdentity(token.ParsedOrchardToken);
        var exactEvidence = resolutionSet.Evidence
            .Where(x => x.NormalizedIdentity == tokenKey
                && x.EvidenceType != OrchardIdentityEvidenceTypes.GrowerNumber)
            .OrderBy(x => OrchardIdentityResolverService.EvidencePriority(x.EvidenceType))
            .ToArray();
        if (exactEvidence.Length > 0)
        {
            var resolvedOrchardIds = exactEvidence.Select(x => x.CanonicalOrchardId).OfType<int>().Distinct().ToArray();
            if (resolvedOrchardIds.Length == 1)
            {
                var orchard = resolutionSet.Orchards.Single(x => x.Id == resolvedOrchardIds[0]);
                var strongest = exactEvidence.First(x => x.CanonicalOrchardId == orchard.Id);
                var method = strongest.EvidenceType switch
                {
                    OrchardIdentityEvidenceTypes.Grower or OrchardIdentityEvidenceTypes.GrowerAlias => OrchardContactMatchMethods.Grower,
                    OrchardIdentityEvidenceTypes.GrowerLot => OrchardContactMatchMethods.GrowerLot,
                    OrchardIdentityEvidenceTypes.CanonicalBlock or OrchardIdentityEvidenceTypes.CanonicalBlockAlias => OrchardContactMatchMethods.CanonicalBlock,
                    _ => OrchardContactMatchMethods.PersistedIdentity
                };
                return Result(
                    token,
                    method,
                    orchard,
                    EvidenceCandidates(exactEvidence),
                    $"Resolved through exact {strongest.EvidenceType.ToLowerInvariant()} evidence tied to the confirmed canonical orchard.",
                    1m);
            }

            if (resolvedOrchardIds.Length > 1)
            {
                return Result(
                    token,
                    OrchardContactMatchMethods.Ambiguous,
                    null,
                    EvidenceCandidates(exactEvidence),
                    "Existing identity records point to more than one canonical orchard. An administrator must choose.");
            }

            return Result(
                token,
                OrchardContactMatchMethods.CanonicalSetupRequired,
                null,
                EvidenceCandidates(exactEvidence),
                "The grower or Grower Lot exists, but it has no confirmed canonical orchard target. Select an existing orchard or complete canonical setup before approval.",
                1m);
        }

        return Match(token, resolutionSet.Orchards)!;
    }

    public static OrchardContactDryRunRowViewModel Match(
        ParsedOrchardManagerToken token,
        IReadOnlyList<CanonicalOrchardMatchSource> orchards) =>
        Match(token, orchards, stopBeforeDeterministicAndFuzzy: false)!;

    private static OrchardContactDryRunRowViewModel? Match(
        ParsedOrchardManagerToken token,
        IReadOnlyList<CanonicalOrchardMatchSource> orchards,
        bool stopBeforeDeterministicAndFuzzy)
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

        if (stopBeforeDeterministicAndFuzzy) return null;

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

    private static IReadOnlyList<OrchardMatchCandidateViewModel> EvidenceCandidates(
        IReadOnlyList<OrchardIdentityEvidence> evidence) =>
        evidence
            .GroupBy(x => new
            {
                x.EvidenceType,
                x.Identity,
                x.CanonicalOrchardId,
                x.CanonicalOrchardName,
                x.CanonicalGrowerId,
                x.GrowerName,
                x.GrowerNumber,
                x.BlockName,
                x.LotNumber
            })
            .Select(x => new OrchardMatchCandidateViewModel(
                x.Key.CanonicalOrchardId,
                x.Key.CanonicalOrchardName ?? x.Key.Identity,
                1m,
                null,
                null,
                x.Key.CanonicalOrchardId is null
                    ? "Existing identity found; canonical orchard setup or selection is required."
                    : $"Exact {x.Key.EvidenceType.ToLowerInvariant()} evidence tied to a confirmed canonical orchard.",
                x.Key.EvidenceType,
                x.Key.CanonicalGrowerId,
                x.Select(y => y.GrowerLotId).OfType<int>().Distinct().OrderBy(y => y).ToArray(),
                x.Key.GrowerName,
                x.Key.GrowerNumber,
                x.Select(y => y.CanonicalBlockId).OfType<int>().Distinct().OrderBy(y => y).ToArray(),
                x.Key.BlockName,
                x.Key.LotNumber,
                x.Select(y => y.Facility).Where(y => !string.IsNullOrWhiteSpace(y)).Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(y => y).ToArray(),
                x.Select(y => y.CropYear).OfType<int>().Distinct().OrderByDescending(y => y).ToArray(),
                x.Select(y => y.SourceRecord).Where(y => !string.IsNullOrWhiteSpace(y)).Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(y => y).ToArray(),
                x.Max(y => y.LastObservedAt),
                x.Key.CanonicalOrchardId is null))
            .OrderBy(x => OrchardIdentityResolverService.EvidencePriority(x.ResultType))
            .ThenBy(x => x.OrchardName)
            .Take(10)
            .ToArray();

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

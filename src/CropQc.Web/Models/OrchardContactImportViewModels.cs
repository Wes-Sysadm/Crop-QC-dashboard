namespace CropQc.Web.Models;

public sealed record ParsedOrchardManagerToken(
    int WorkbookRowNumber,
    string OriginalOrchardCell,
    string ParsedOrchardToken,
    string ManagerDisplayName,
    string NormalizedManagerName,
    string? EmailAddress,
    string? NormalizedEmailAddress,
    bool EmailIsValid,
    string? Phone,
    string? NormalizedPhone,
    string? PhysicalAddress,
    string? CommunicationNote,
    string? SourceStatusNote);

public sealed record ParsedOrchardContactWorkbook(
    string OriginalFileName,
    string WorkbookSha256,
    string WorksheetName,
    int OrchardManagerSourceRowCount,
    IReadOnlyList<ParsedOrchardManagerToken> Tokens);

public sealed record OrchardMatchCandidateViewModel(
    int CanonicalOrchardId,
    string OrchardName,
    decimal SimilarityScore,
    string? MatchingAlias,
    string? AddressEvidence,
    string Reason);

public sealed record OrchardContactDryRunRowViewModel(
    int WorkbookRowNumber,
    string OriginalOrchardCell,
    string ParsedOrchardToken,
    string ManagerName,
    string? Email,
    bool EmailIsValid,
    string? Phone,
    string? PhysicalAddress,
    string MatchMethod,
    decimal? MatchScore,
    int? SuggestedCanonicalOrchardId,
    string? SuggestedCanonicalOrchard,
    IReadOnlyList<OrchardMatchCandidateViewModel> Candidates,
    IReadOnlyList<string> ExistingRecipients,
    string ProposedAction,
    string? Warning,
    bool IsDuplicateExistingRecipient,
    bool HasExistingRecipientConflict)
{
    public string ReviewStatus =>
        string.IsNullOrWhiteSpace(Email) ? "Missing Email"
        : !EmailIsValid ? "Invalid Email"
        : IsDuplicateExistingRecipient ? "Duplicate Assignment"
        : HasExistingRecipientConflict ? "Conflict With Existing Assignment"
        : MatchMethod;
}

public sealed class OrchardContactDryRunViewModel
{
    public string OriginalFileName { get; init; } = "";
    public string WorkbookSha256 { get; init; } = "";
    public string WorksheetName { get; init; } = "";
    public int OrchardManagerSourceRows { get; init; }
    public int ParsedOrchardTokens { get; init; }
    public IReadOnlyList<OrchardContactDryRunRowViewModel> Rows { get; init; } = [];
    public int ExactMatches => Rows.Count(x => x.MatchMethod == "Exact Match");
    public int AliasMatches => Rows.Count(x => x.MatchMethod == "Alias Match");
    public int ProposedAliases => Rows.Count(x => x.MatchMethod == "Proposed Alias");
    public int Ambiguous => Rows.Count(x => x.MatchMethod == "Ambiguous");
    public int Unmatched => Rows.Count(x => x.MatchMethod is "Unmatched" or "Invalid Orchard Identity");
    public int MissingEmails => Rows.Count(x => string.IsNullOrWhiteSpace(x.Email));
    public int InvalidEmails => Rows.Count(x => !string.IsNullOrWhiteSpace(x.Email) && !x.EmailIsValid);
    public int ProposedRecipientInserts => Rows.Count(x => x.SuggestedCanonicalOrchardId is not null && x.EmailIsValid && !x.IsDuplicateExistingRecipient);
    public int DuplicateRecipients => Rows.Count(x => x.IsDuplicateExistingRecipient);
    public int Conflicts => Rows.Count(x => x.HasExistingRecipientConflict);
}

public sealed class OrchardContactImportIndexViewModel
{
    public OrchardContactDryRunViewModel? Preview { get; init; }
    public IReadOnlyList<OrchardContactImportBatchListItem> RecentBatches { get; init; } = [];
}

public sealed record OrchardContactImportBatchListItem(
    long Id,
    string OriginalFileName,
    string WorkbookSha256,
    string Status,
    int OrchardManagerSourceRows,
    int ParsedTokens,
    int PendingRows,
    int ApprovedRows,
    int RejectedRows,
    int DeferredRows,
    DateTimeOffset UploadedAt,
    string UploadedBy,
    DateTimeOffset? AppliedAt);

public sealed class OrchardContactWorkbookUploadForm
{
    public IFormFile? Workbook { get; set; }
}

public sealed class OrchardContactImportBatchViewModel
{
    public long Id { get; init; }
    public string OriginalFileName { get; init; } = "";
    public string WorkbookSha256 { get; init; } = "";
    public string WorksheetName { get; init; } = "";
    public string Status { get; init; } = "";
    public int OrchardManagerSourceRows { get; init; }
    public int ParsedTokens { get; init; }
    public DateTimeOffset UploadedAt { get; init; }
    public string UploadedBy { get; init; } = "";
    public IReadOnlyList<OrchardContactImportReviewRowViewModel> Rows { get; init; } = [];
    public IReadOnlyList<OrchardRecipientOrchardOption> Orchards { get; init; } = [];
    public bool HasPendingDecisions => Rows.Any(x => x.ReviewDecision == "Pending");
}

public sealed record OrchardContactImportReviewRowViewModel(
    long Id,
    int WorkbookRowNumber,
    string OriginalOrchardCell,
    string ParsedOrchardToken,
    string ManagerName,
    string? Email,
    bool EmailIsValid,
    string? Phone,
    string? PhysicalAddress,
    string MatchMethod,
    decimal? MatchScore,
    int? SuggestedCanonicalOrchardId,
    string? SuggestedCanonicalOrchard,
    IReadOnlyList<OrchardMatchCandidateViewModel> Candidates,
    IReadOnlyList<string> ExistingRecipients,
    string? Warning,
    string ReviewDecision,
    int? ApprovedCanonicalOrchardId,
    bool CreateAlias,
    bool CreateRecipient,
    bool ReactivateDeletedRecipient,
    string? ReviewNote,
    string? AppliedAction);

public sealed class OrchardContactImportDecisionForm
{
    public long BatchId { get; set; }
    public long RowId { get; set; }
    public string Decision { get; set; } = "";
    public int? CanonicalOrchardId { get; set; }
    public bool CreateAlias { get; set; }
    public bool CreateRecipient { get; set; }
    public bool ReactivateDeletedRecipient { get; set; }
    public string? ReviewNote { get; set; }
}

public sealed class OrchardContactImportApplyForm
{
    public long BatchId { get; set; }
    public IFormFile? Workbook { get; set; }
    public long? VerifiedBackupRunId { get; set; }
    public string ImportReason { get; set; } = "";
    public string ProductionConfirmation { get; set; } = "";
}

public sealed record OrchardContactImportApplyResult(
    bool Success,
    string? Error,
    int ContactsCreated = 0,
    int AssignmentsCreated = 0,
    int RecipientsCreated = 0,
    int DuplicatesSkipped = 0,
    int AliasesCreated = 0,
    int ConflictsRetained = 0,
    bool WasAlreadyApplied = false);

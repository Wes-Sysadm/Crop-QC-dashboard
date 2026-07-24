namespace CropQc.Data.Entities;

public static class OrchardContactImportStatuses
{
    public const string Reviewing = "Reviewing";
    public const string Applied = "Applied";
    public const string Failed = "Failed";
}

public static class OrchardContactImportDecisions
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Deferred = "Deferred";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Pending, Approved, Rejected, Deferred
    };
}

public static class OrchardContactMatchMethods
{
    public const string Exact = "Exact Match";
    public const string Alias = "Alias Match";
    public const string Grower = "Grower Match";
    public const string GrowerLot = "Grower Lot Match";
    public const string CanonicalBlock = "Canonical Block Match";
    public const string PersistedIdentity = "Confirmed Record Match";
    public const string CanonicalSetupRequired = "Canonical Setup Required";
    public const string ProposedAlias = "Proposed Alias";
    public const string Ambiguous = "Ambiguous";
    public const string Unmatched = "Unmatched";
    public const string InvalidOrchardIdentity = "Invalid Orchard Identity";
}

public sealed class CanonicalOrchardAlias
{
    public int Id { get; set; }
    public int CanonicalOrchardId { get; set; }
    public CanonicalOrchard CanonicalOrchard { get; set; } = null!;
    public required string AliasText { get; set; }
    public required string NormalizedAlias { get; set; }
    public required string Source { get; set; }
    public string? ReviewNote { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
}

public sealed class OrchardManagerContact
{
    public long Id { get; set; }
    public required string DisplayName { get; set; }
    public required string NormalizedDisplayName { get; set; }
    public string? EmailAddress { get; set; }
    public string? NormalizedEmailAddress { get; set; }
    public string? Phone { get; set; }
    public string? NormalizedPhone { get; set; }
    public string? CommunicationNote { get; set; }
    public required string SourceWorkbook { get; set; }
    public required string SourceWorksheet { get; set; }
    public int SourceRowNumber { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
    public ICollection<OrchardManagerAssignment> OrchardAssignments { get; } = new List<OrchardManagerAssignment>();
}

public sealed class OrchardManagerAssignment
{
    public long Id { get; set; }
    public int CanonicalOrchardId { get; set; }
    public CanonicalOrchard CanonicalOrchard { get; set; } = null!;
    public long OrchardManagerContactId { get; set; }
    public OrchardManagerContact OrchardManagerContact { get; set; } = null!;
    public int? OrchardReportRecipientId { get; set; }
    public OrchardReportRecipient? OrchardReportRecipient { get; set; }
    public long? SourceImportRowId { get; set; }
    public OrchardContactImportRow? SourceImportRow { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
}

public sealed class OrchardContactImportBatch
{
    public long Id { get; set; }
    public required string OriginalFileName { get; set; }
    public required string WorkbookSha256 { get; set; }
    public required string WorksheetName { get; set; }
    public required string Status { get; set; }
    public int OrchardManagerSourceRowCount { get; set; }
    public int ParsedOrchardTokenCount { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public int? UploadedByUserId { get; set; }
    public User? UploadedByUser { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public int? AppliedByUserId { get; set; }
    public User? AppliedByUser { get; set; }
    public long? VerifiedBackupRunId { get; set; }
    public BackupRunRecord? VerifiedBackupRun { get; set; }
    public string? ImportReason { get; set; }
    public string? ApplySummaryJson { get; set; }
    public ICollection<OrchardContactImportRow> Rows { get; } = new List<OrchardContactImportRow>();
}

public sealed class OrchardContactImportRow
{
    public long Id { get; set; }
    public long OrchardContactImportBatchId { get; set; }
    public OrchardContactImportBatch OrchardContactImportBatch { get; set; } = null!;
    public int WorkbookRowNumber { get; set; }
    public required string OriginalOrchardCell { get; set; }
    public required string ParsedOrchardToken { get; set; }
    public required string ManagerDisplayName { get; set; }
    public required string NormalizedManagerName { get; set; }
    public string? EmailAddress { get; set; }
    public string? NormalizedEmailAddress { get; set; }
    public bool EmailIsValid { get; set; }
    public string? Phone { get; set; }
    public string? NormalizedPhone { get; set; }
    public string? PhysicalAddress { get; set; }
    public string? CommunicationNote { get; set; }
    public string? SourceStatusNote { get; set; }
    public required string MatchMethod { get; set; }
    public decimal? MatchScore { get; set; }
    public int? SuggestedCanonicalOrchardId { get; set; }
    public CanonicalOrchard? SuggestedCanonicalOrchard { get; set; }
    public string? CandidateMatchesJson { get; set; }
    public string? Warning { get; set; }
    public required string ReviewDecision { get; set; }
    public int? ApprovedCanonicalOrchardId { get; set; }
    public CanonicalOrchard? ApprovedCanonicalOrchard { get; set; }
    public bool CreateAlias { get; set; }
    public bool CreateRecipient { get; set; }
    public bool ReactivateDeletedRecipient { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    public User? ReviewedByUser { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public string? AppliedAction { get; set; }
    public long? OrchardManagerContactId { get; set; }
    public OrchardManagerContact? OrchardManagerContact { get; set; }
    public int? OrchardReportRecipientId { get; set; }
    public OrchardReportRecipient? OrchardReportRecipient { get; set; }
}

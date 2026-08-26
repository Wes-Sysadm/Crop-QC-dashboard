namespace CropQc.Web.Models;

public sealed class OrchardRecipientMatrixViewModel
{
    public string Search { get; set; } = "";
    public IReadOnlyList<OrchardRecipientMatrixRow> Rows { get; set; } = [];
    public IReadOnlyList<OrchardRecipientOrchardOption> Orchards { get; set; } = [];
}

public sealed class QcRecipientMatrixViewModel
{
    public string Search { get; set; } = "";
    public GrowerRecipientMatrixViewModel GrowerNumbers { get; set; } = new();
    public OrchardRecipientMatrixViewModel Orchards { get; set; } = new();
}

public sealed class GrowerRecipientMatrixViewModel
{
    public string Search { get; set; } = "";
    public IReadOnlyList<GrowerRecipientMatrixRow> Rows { get; set; } = [];
    public IReadOnlyList<GrowerRecipientNumberOption> GrowerNumbers { get; set; } = [];
}

public sealed record GrowerRecipientMatrixRow(
    int CanonicalGrowerNumberId,
    string GrowerNumber,
    string GrowerName,
    int RecipientId,
    string EmailAddress,
    bool IsActive,
    DateTimeOffset LastModifiedAt,
    string LastModifiedBy);

public sealed record GrowerRecipientNumberOption(int Id, string GrowerNumber, string GrowerName)
{
    public string Label => $"{GrowerNumber} — {GrowerName}";
    public string SearchText => $"{GrowerNumber} {GrowerName}".ToLowerInvariant();
}

public sealed class GrowerRecipientEditForm
{
    public int? Id { get; set; }
    public int CanonicalGrowerNumberId { get; set; }
    public string EmailAddress { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed record GrowerRecipientUpsertRequest(
    int? RecipientId,
    int CanonicalGrowerNumberId,
    string EmailAddress,
    bool IsActive);

public sealed record GrowerRecipientUpsertResult(bool Success, int? RecipientId, string? Error);

public sealed record OrchardRecipientMatrixRow(
    int CanonicalOrchardId,
    string OrchardName,
    string Growers,
    string GrowerNumbers,
    int? RecipientId,
    string EmailAddress,
    bool IsActive,
    DateTimeOffset? LastModifiedAt,
    string LastModifiedBy,
    bool IsMissingConfiguration);

public sealed record OrchardRecipientOrchardOption(int Id, string OrchardName, string Growers, string GrowerNumbers);

public sealed class OrchardRecipientEditForm
{
    public int? Id { get; set; }
    public int CanonicalOrchardId { get; set; }
    public string EmailAddress { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed record OrchardRecipientUpsertRequest(
    int? RecipientId,
    int? CanonicalOrchardId,
    string? OrchardIdentity,
    string EmailAddress,
    bool IsActive);

public sealed record OrchardRecipientUpsertResult(
    bool Success,
    int? RecipientId,
    string? Error,
    bool OrchardWasAmbiguous = false,
    bool OrchardWasUnmatched = false);

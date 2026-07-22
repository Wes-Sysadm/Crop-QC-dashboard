namespace CropQc.Web.Models;

public sealed class OrchardRecipientMatrixViewModel
{
    public string Search { get; set; } = "";
    public IReadOnlyList<OrchardRecipientMatrixRow> Rows { get; set; } = [];
    public IReadOnlyList<OrchardRecipientOrchardOption> Orchards { get; set; } = [];
}

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

using System.ComponentModel.DataAnnotations;
using CropQc.Data.Entities;

namespace CropQc.Web.Models;

public sealed class DirectTransferForm
{
    [Required, StringLength(60)] public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public int SourceRoomId { get; set; }
    public int DestinationRoomId { get; set; }
    public List<DirectTransferSelection> Selections { get; set; } = [];
}
public sealed class DirectTransferSelection
{
    public string IdentityKey { get; set; } = "";
    public string Signature { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    [Range(0, int.MaxValue)] public int Quantity { get; set; }
}
public sealed class DirectTransferCorrectionForm
{
    [Required, StringLength(60)] public string OperationKey { get; set; } = Guid.NewGuid().ToString("N");
    public long TransferId { get; set; }
    [Range(1, int.MaxValue)] public int Quantity { get; set; }
    [Required, StringLength(500)] public string Reason { get; set; } = "";
    [Required, StringLength(2000)] public string Evidence { get; set; } = "";
    public bool PhysicallyVerified { get; set; }
    public int? VerifiedDestinationRoomId { get; set; }
    [Required] public string Fingerprint { get; set; } = "";
}
public sealed record DirectTransferOption(string IdentityKey, string Signature, string Fingerprint, string Label, int Available);
public sealed record DirectTransferLocation(int Id, int WarehouseId, string Label);
public sealed record DirectTransferPage(DirectTransferForm Form, IReadOnlyList<DirectTransferLocation> Locations,
    IReadOnlyList<DirectTransferOption> Inventory, IReadOnlyList<RoomTransfer> History, IReadOnlyList<string> Warnings);
public sealed record DirectTransferDetail(RoomTransfer Transfer, IReadOnlyList<RoomTransfer> Corrections,
    IReadOnlyList<DirectTransferLocation> Locations, string Fingerprint, int CorrectableQuantity, bool CanCorrect);

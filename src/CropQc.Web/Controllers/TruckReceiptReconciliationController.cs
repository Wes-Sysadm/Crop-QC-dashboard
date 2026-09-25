using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

public sealed class TruckReceiptReconciliationController(TruckReceiptReconciliationService service) : Controller
{
    [HttpGet("Receipts/{id:long}/MatchTransfer")]
    [Authorize(Policy = AccessPolicyNames.ReceiptsView)]
    public async Task<IActionResult> Receipt(long id, CancellationToken ct) =>
        View("~/Views/Receipts/MatchTransfer.cshtml", await service.GetAsync(id, null, ct));

    [HttpGet("BinsRun/InterCrewTransfers/{id:long}/Reconciliation")]
    [Authorize(Policy = AccessPolicyNames.BinsRunView)]
    public async Task<IActionResult> Transfer(long id, CancellationToken ct) =>
        View("~/Views/Receipts/MatchTransfer.cshtml", await service.GetAsync(null, id, ct));

    [HttpPost("Receipts/{id:long}/MatchTransfer")]
    [Authorize(Policy = AccessPolicyNames.ReceiptsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Match(long id, TruckReceiptActionForm form, CancellationToken ct)
    {
        form.ReceiptId = id;
        return Result(await service.MatchAsync(form, ct), id);
    }

    [HttpPost("Receipts/{id:long}/TransferVarieties")]
    [Authorize(Policy = AccessPolicyNames.ReceiptsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(long id, TruckReceiptActionForm form, CancellationToken ct)
    {
        form.ReceiptId = id;
        return Result(await service.EditReceiptAsync(form, ct), id);
    }

    [HttpPost("Receipts/{id:long}/CompleteTransfer")]
    [Authorize(Policy = AccessPolicyNames.ReceiptsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(long id, TruckReceiptActionForm form, CancellationToken ct)
    {
        form.ReceiptId = id;
        return Result(await service.CompleteAsync(form, ct), id);
    }

    [HttpPost("Receipts/{id:long}/ReopenTransfer")]
    [Authorize(Policy = AccessPolicyNames.TransfersAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(long id, TruckReceiptActionForm form, CancellationToken ct)
    {
        form.ReceiptId = id;
        return Result(await service.ReopenAsync(form, ct), id);
    }

    [HttpPost("BinsRun/InterCrewTransfers/{id:long}/EditTransit")]
    [Authorize(Policy = AccessPolicyNames.TransfersCreate)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditTransfer(long id, TransitEditForm form, CancellationToken ct)
    {
        form.TransferId = id;
        var error = await service.EditTransferAsync(form, ct);
        TempData[error is null ? "Success" : "Error"] = error ?? "In Transit load updated; receiving reconciliation has been recalculated.";
        return Redirect($"/BinsRun/InterCrewTransfers/{id}/Reconciliation");
    }

    private IActionResult Result(string? error, long receiptId)
    {
        TempData[error is null ? "Success" : "Error"] = error ?? "Saved. Reconciliation uses the current transfer and receipt values.";
        return Redirect($"/Receipts/{receiptId}/MatchTransfer");
    }
}

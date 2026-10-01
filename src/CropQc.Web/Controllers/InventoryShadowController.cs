using CropQc.Shared.Inventory;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("Admin/InventoryShadow")]
[Authorize(Policy = AccessPolicyNames.HistoricalInventoryCleanupAdmin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InventoryShadowController(InventoryShadowDiagnostic diagnostic) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Get(int? warehouseId, int? roomId, InventoryCustody custody = InventoryCustody.Room,
        long? custodyRecordId = null, bool exactReceipt = false, long? receiptId = null, CancellationToken cancellationToken = default)
    {
        if (warehouseId is null && roomId is null && (custody == InventoryCustody.Room || custodyRecordId is null))
            return BadRequest("A warehouse, room or custody record filter is required.");
        if (!Enum.IsDefined(custody)) return BadRequest("Unknown custody scope.");
        var scope = new InventoryScope(warehouseId, roomId is int id ? [id] : [], custody, custodyRecordId);
        var requirements = new InventoryOperationRequirements(RequireExactReceipt: exactReceipt, ReceiptId: receiptId, AllowedCustody: custody);
        return Json(await diagnostic.ResolveAsync(scope, requirements, DateTimeOffset.UtcNow, cancellationToken));
    }
}

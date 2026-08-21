using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("ProcessorShipments")]
[Authorize(Policy = AccessPolicyNames.ProcessorShipmentsView)]
public sealed class ProcessorShipmentsController(IProcessorShipmentService service) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? from, string? to, int? processorId, int? warehouseId, CancellationToken cancellationToken) =>
        View(await service.GetPageAsync(null, false, from, to, processorId, warehouseId, cancellationToken));

    [HttpPost("Review")]
    [Authorize(Policy = AccessPolicyNames.ProcessorShipmentsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(ProcessorShipmentForm form, CancellationToken cancellationToken) =>
        View("Index", await service.GetPageAsync(form, true, null, null, null, null, cancellationToken));

    [HttpPost("")]
    [Authorize(Policy = AccessPolicyNames.ProcessorShipmentsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProcessorShipmentForm form, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(form, cancellationToken);
        if (!result.Success)
        {
            var model = await service.GetPageAsync(form, true, null, null, null, null, cancellationToken);
            return View("Index", WithError(model, result.Error));
        }
        TempData["Success"] = result.AlreadyApplied ? "Processor Shipment was already recorded." : "Processor Shipment saved.";
        return RedirectToAction(nameof(Details), new { id = result.ShipmentId });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var model = await service.GetDetailsAsync(id, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost("{id:long}/PriceCorrection")]
    [Authorize(Policy = AccessPolicyNames.ProcessorShipmentsAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PriceCorrection(long id, ProcessorShipmentPriceCorrectionForm form, CancellationToken cancellationToken)
    {
        form.ShipmentId = id;
        var error = await service.CorrectPriceAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Processor Shipment price corrected.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/Reverse")]
    [Authorize(Policy = AccessPolicyNames.ProcessorShipmentsAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reverse(long id, ProcessorShipmentReversalForm form, CancellationToken cancellationToken)
    {
        form.ShipmentId = id;
        var error = await service.ReverseAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Processor Shipment physically reversed.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private static ProcessorShipmentPageViewModel WithError(ProcessorShipmentPageViewModel model, string? error)
    {
        model.Error = error;
        model.IsReview = false;
        return model;
    }
}

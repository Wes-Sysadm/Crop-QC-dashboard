using CropQc.Api.Dtos;
using CropQc.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class QcSamplesController(IQcSampleService service) : ControllerBase
{
    [HttpPost("receipts/{receiptId:long}/samples")]
    public async Task<IActionResult> Create(long receiptId, CreateQcSampleRequest request, CancellationToken cancellationToken)
    {
        var (sample, error) = await service.CreateAsync(receiptId, request, cancellationToken);
        return sample is null ? BadRequest(new { error }) : CreatedAtAction(nameof(GetById), new { id = sample.Id }, sample);
    }

    [HttpGet("samples/{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        var sample = await service.GetAsync(id, cancellationToken);
        return sample is null ? NotFound() : Ok(sample);
    }

    [HttpGet("receipts/{receiptId:long}/samples")]
    public async Task<IActionResult> GetForReceipt(long receiptId, CancellationToken cancellationToken) =>
        Ok(await service.GetForReceiptAsync(receiptId, cancellationToken));

    [HttpGet("warehouses/{warehouseId:int}/samples/today")]
    public async Task<IActionResult> GetTodayByWarehouse(int warehouseId, CancellationToken cancellationToken) =>
        Ok(await service.GetTodayByWarehouseAsync(warehouseId, cancellationToken));

    [HttpPatch("samples/{id:long}/statuses")]
    public async Task<IActionResult> UpdateStatuses(long id, UpdateQcSampleStatusesRequest request, CancellationToken cancellationToken)
    {
        var (sample, error) = await service.UpdateStatusesAsync(id, request, cancellationToken);
        return sample is null ? BadRequest(new { error }) : Ok(sample);
    }
}

using System.Text;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Web.Controllers;

[Route("BinsRun")]
public sealed class BinsRunController(
    IBinsRunService binsRunService,
    IRunProjectionService runProjectionService,
    IDashboardDataService dashboardDataService) : Controller
{
    [HttpGet("")]
    [Authorize(Policy = AccessPolicyNames.BinsRunView)]
    public async Task<IActionResult> Index([FromQuery] BinsRunFilterForm filter, CancellationToken cancellationToken)
    {
        var model = await binsRunService.GetPageAsync(filter, User, cancellationToken);
        model.Planner = await runProjectionService.GetPlannerAsync(filter.PlannedDate, filter.ProjectionId, User, cancellationToken);
        if (filter.RoomId is int roomId)
        {
            var room = await dashboardDataService.GetRoomDetailAsync(roomId, cancellationToken);
            model.TransferForm = room.TransferForm;
            model.TrueUpForm = room.TrueUpForm;
            model.TransferLotOptions = room.TransferLotOptions;
            model.TrueUpReceiptOptions = room.DepletionReceiptOptions;
            model.TransferDestinationOptions = room.TransferDestinationOptions;
            model.InventoryActivity = room.InventoryAdjustments.Take(100).ToList();
        }

        return View(model);
    }

    [HttpGet("Sources")]
    [Authorize(Policy = AccessPolicyNames.BinsRunView)]
    public async Task<IActionResult> Sources(string? query, int? roomId, CancellationToken cancellationToken) =>
        Ok(await runProjectionService.SearchSourcesAsync(query, roomId, User, cancellationToken));

    [HttpPost("Projections")]
    [Authorize(Policy = AccessPolicyNames.BinsRunEdit)]
    public async Task<IActionResult> CreateProjection(RunProjectionCreateForm form, CancellationToken cancellationToken)
    {
        var result = await runProjectionService.CreateAsync(form, User, cancellationToken);
        TempData[result.Error is null ? "Success" : "Error"] = result.Error ?? "Run projection draft created.";
        return PlannerRedirect(form.PlannedRunDate, result.Id);
    }

    [HttpPost("Projections/{id:long}/Header")]
    [Authorize(Policy = AccessPolicyNames.BinsRunEdit)]
    public async Task<IActionResult> UpdateProjectionHeader(long id, RunProjectionHeaderForm form, CancellationToken cancellationToken)
    {
        form.Id = id;
        var error = await runProjectionService.UpdateHeaderAsync(form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Projection saved.";
        return PlannerRedirect(form.PlannedRunDate, id);
    }

    [HttpPost("Projections/{id:long}/Sources")]
    [Authorize(Policy = AccessPolicyNames.BinsRunEdit)]
    public async Task<IActionResult> AddProjectionSource(long id, RunProjectionAddSourceForm form, DateOnly plannedDate, CancellationToken cancellationToken)
    {
        form.ProjectionId = id;
        var error = await runProjectionService.AddSourceAsync(form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Projection source added.";
        return PlannerRedirect(plannedDate, id);
    }

    [HttpPost("Projections/{id:long}/Sources/{sourceId:long}")]
    [Authorize(Policy = AccessPolicyNames.BinsRunEdit)]
    public async Task<IActionResult> UpdateProjectionSource(long id, long sourceId, RunProjectionUpdateSourceForm form, DateOnly plannedDate, CancellationToken cancellationToken)
    {
        form.ProjectionId = id;
        form.SourceId = sourceId;
        var error = await runProjectionService.UpdateSourceAsync(form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Projection source saved.";
        return PlannerRedirect(plannedDate, id);
    }

    [HttpPost("Projections/{id:long}/Sources/{sourceId:long}/Remove")]
    [Authorize(Policy = AccessPolicyNames.BinsRunEdit)]
    public async Task<IActionResult> RemoveProjectionSource(long id, long sourceId, long concurrencyVersion, DateOnly plannedDate, CancellationToken cancellationToken)
    {
        var error = await runProjectionService.RemoveSourceAsync(id, sourceId, concurrencyVersion, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Projection source removed.";
        return PlannerRedirect(plannedDate, id);
    }

    [HttpPost("Projections/{id:long}/Ready")]
    [Authorize(Policy = AccessPolicyNames.BinsRunEdit)]
    public async Task<IActionResult> MarkProjectionReady(long id, RunProjectionStatusForm form, DateOnly plannedDate, CancellationToken cancellationToken)
    {
        form.Id = id;
        var error = await runProjectionService.MarkReadyAsync(form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Projection marked Ready.";
        return PlannerRedirect(plannedDate, id);
    }

    [HttpPost("Projections/{id:long}/Cancel")]
    [Authorize(Policy = AccessPolicyNames.BinsRunAdmin)]
    public async Task<IActionResult> CancelProjection(long id, RunProjectionStatusForm form, DateOnly plannedDate, CancellationToken cancellationToken)
    {
        form.Id = id;
        var error = await runProjectionService.CancelAsync(form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Projection cancelled.";
        return PlannerRedirect(plannedDate, id);
    }

    [HttpPost("Projections/{id:long}/Duplicate")]
    [Authorize(Policy = AccessPolicyNames.BinsRunEdit)]
    public async Task<IActionResult> DuplicateProjection(long id, RunProjectionDuplicateForm form, CancellationToken cancellationToken)
    {
        form.Id = id;
        var result = await runProjectionService.DuplicateAsync(form, User, cancellationToken);
        TempData[result.Error is null ? "Success" : "Error"] = result.Error ?? "Projection duplicated.";
        return PlannerRedirect(form.PlannedRunDate, result.Id);
    }

    [HttpGet("Projections/{id:long}/Export")]
    [Authorize(Policy = AccessPolicyNames.BinsRunView)]
    public async Task<IActionResult> ExportProjection(long id, DateOnly plannedDate, CancellationToken cancellationToken)
    {
        var detail = (await runProjectionService.GetPlannerAsync(plannedDate, id, User, cancellationToken)).SelectedProjection;
        if (detail is null || detail.Id != id) return NotFound();
        var csv = new StringBuilder("Source,Room,Lot or Block,Variety,Commodity,QC Basis,Available Bins,Planned Bins,Projected Pounds,Projected Boxes\r\n");
        foreach (var source in detail.Sources)
        {
            csv.AppendLine(string.Join(',', new[]
            {
                Csv(source.SourceLabel), Csv(source.Room), Csv(source.Lot ?? source.Block), Csv(source.Variety),
                Csv(source.Commodity), Csv(source.QcBasis), source.AvailableBinsSnapshot?.ToString() ?? "",
                source.PlannedBins.ToString(), source.ProjectedPounds.ToString("0.##"), source.ProjectedBoxes.ToString("0.##")
            }));
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"run-projection-{id}-{plannedDate:yyyy-MM-dd}.csv");
    }

    [HttpPost("Projection")]
    [Authorize(Policy = AccessPolicyNames.BinsRunView)]
    public async Task<IActionResult> Projection([FromBody] BinsRunProjectionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await binsRunService.GetProjectionAsync(request, User, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("Create")]
    [Authorize(Policy = AccessPolicyNames.BinsRunEdit)]
    public async Task<IActionResult> Create(BinsRunForm form, CancellationToken cancellationToken)
    {
        var error = await binsRunService.CreateAsync(form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Bins run recorded.";
        return RedirectToAction(nameof(Index), new
        {
            form.WarehouseId,
            form.RoomId,
            Section = "Actual",
            ProjectionId = form.RunProjectionId
        });
    }

    [HttpPost("{id:long}/Edit")]
    [Authorize(Policy = AccessPolicyNames.BinsRunEdit)]
    public async Task<IActionResult> Edit(long id, BinsRunForm form, CancellationToken cancellationToken)
    {
        var error = await binsRunService.UpdateAsync(id, form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Bins run updated.";
        return RedirectToAction(nameof(Index), new { form.WarehouseId, form.RoomId, Section = "Actual" });
    }

    [HttpPost("{id:long}/Reverse")]
    [Authorize(Policy = AccessPolicyNames.BinsRunAdmin)]
    public async Task<IActionResult> Reverse(long id, ReverseBinsRunForm form, CancellationToken cancellationToken)
    {
        form.Id = id;
        var error = await binsRunService.ReverseAsync(form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Bins run reversed.";
        return RedirectToAction(nameof(Index), new { Section = "Activity" });
    }

    [HttpPost("Transfer")]
    [Authorize(Policy = AccessPolicyNames.RoomTransactionsEdit)]
    public async Task<IActionResult> Transfer(RoomTransferForm form, CancellationToken cancellationToken)
    {
        var error = await dashboardDataService.CreateRoomTransferAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Room transfer recorded.";
        return RedirectToAction(nameof(Index), new { RoomId = form.FromRoomId, Section = "Transfer", SourceKey = form.SourceLotKey });
    }

    [HttpPost("TrueUp")]
    [Authorize(Policy = AccessPolicyNames.RoomTransactionsAdmin)]
    public async Task<IActionResult> TrueUp(RoomInventoryTrueUpForm form, CancellationToken cancellationToken)
    {
        var error = await dashboardDataService.CreateRoomInventoryTrueUpAsync(form, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Room inventory true-up recorded.";
        return RedirectToAction(nameof(Index), new { RoomId = form.RoomId, Section = "TrueUp" });
    }

    private RedirectToActionResult PlannerRedirect(DateOnly date, long? id) =>
        RedirectToAction(nameof(Index), new { Section = "Planner", PlannedDate = date.ToString("yyyy-MM-dd"), ProjectionId = id });

    private static string Csv(string? value)
    {
        var safe = value ?? "";
        if (safe.Length > 0 && safe[0] is '=' or '+' or '-' or '@') safe = $"'{safe}";
        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }
}

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
    IDashboardDataService dashboardDataService,
    ILogger<BinsRunController> logger) : Controller
{
    [HttpGet("")]
    [Authorize(Policy = AccessPolicyNames.BinsRunView)]
    public async Task<IActionResult> Index([FromQuery] BinsRunFilterForm filter, CancellationToken cancellationToken)
    {
        var model = await binsRunService.GetPageAsync(filter, User, cancellationToken);
        try
        {
            model.Planner = await runProjectionService.GetPlannerAsync(
                filter.PlannedDate,
                filter.ProjectionId,
                filter.Facility,
                filter.ProjectionVisibility,
                filter.ProjectionSort,
                User,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var diagnostic = DatabaseFailureDiagnostics.Classify(exception);
            var correlationId = string.IsNullOrWhiteSpace(HttpContext.TraceIdentifier)
                ? Guid.NewGuid().ToString("N")[..12]
                : HttpContext.TraceIdentifier;
            logger.LogError(
                exception,
                "Bins Run planner load failed. Route={Route} ProjectionId={ProjectionId} CorrelationId={CorrelationId} Category={Category} ProviderCode={ProviderCode}",
                "/BinsRun",
                filter.ProjectionId,
                correlationId,
                diagnostic.Category,
                diagnostic.ProviderCode);
            model.Planner = new RunProjectionPlannerViewModel
            {
                SelectedDate = filter.PlannedDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                SelectedFacility = string.IsNullOrWhiteSpace(filter.Facility) ? "All" : filter.Facility,
                SelectedDeletionStatus = string.IsNullOrWhiteSpace(filter.ProjectionVisibility) ? "Active" : filter.ProjectionVisibility,
                SelectedSort = string.IsNullOrWhiteSpace(filter.ProjectionSort) ? "Facility" : filter.ProjectionSort,
                PlannerWarning = $"{diagnostic.SafeMessage} The Bins Run inventory and transfer tools remain available. Reference {correlationId}.",
                DiagnosticReference = correlationId
            };
        }
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
    public async Task<IActionResult> Sources(string? query, int? facilityWarehouseId, int? roomId, string? mode, CancellationToken cancellationToken) =>
        Ok(await runProjectionService.SearchSourcesAsync(query, facilityWarehouseId, roomId, mode, User, cancellationToken));

    [HttpGet("Projections/{id:long}/FieldSamples")]
    [Authorize(Policy = AccessPolicyNames.BinsRunView)]
    public async Task<IActionResult> FieldSamples(long id, int canonicalBlockId, int fruitProfileId, CancellationToken cancellationToken) =>
        Ok(await runProjectionService.GetFieldSampleChoicesAsync(id, canonicalBlockId, fruitProfileId, User, cancellationToken));

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
        if (error is not null && Request.Headers["X-Projection-Autosave"] == "1")
        {
            return BadRequest(new { error });
        }

        TempData[error is null ? "Success" : "Error"] = error ?? "Projection source saved.";
        return PlannerRedirect(plannedDate, id);
    }

    [HttpPost("Projections/{id:long}/Packout/ApplyAll")]
    [Authorize(Policy = AccessPolicyNames.BinsRunEdit)]
    public async Task<IActionResult> ApplyProjectionPackoutToAll(long id, RunProjectionApplyPackoutForm form, DateOnly plannedDate, CancellationToken cancellationToken)
    {
        form.ProjectionId = id;
        var error = await runProjectionService.ApplyPackoutToAllAsync(form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? $"Applied {form.ExpectedPackoutPercent:0.##}% Expected Packout to all current sources.";
        return PlannerRedirect(plannedDate, id);
    }

    [HttpPost("Projections/{id:long}/PackPlan/Preview")]
    [Authorize(Policy = AccessPolicyNames.BinsRunEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewPackPlan(long id, RunProjectionPackPlanForm form, CancellationToken cancellationToken)
    {
        form.ProjectionId = id;
        var result = await runProjectionService.PreviewPackPlanAsync(form, User, cancellationToken);
        if (result.Error is not null || result.Preview is null)
        {
            TempData["Error"] = result.Error ?? "The commercial pack plan could not be previewed.";
            return PlannerRedirect(form.PlannedRunDate, id);
        }
        return View("PackPlanPreview", result.Preview);
    }

    [HttpPost("Projections/{id:long}/PackPlan/Apply")]
    [Authorize(Policy = AccessPolicyNames.BinsRunEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyPackPlan(long id, RunProjectionPackPlanForm form, CancellationToken cancellationToken)
    {
        form.ProjectionId = id;
        var error = await runProjectionService.ApplyPackPlanAsync(form, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Commercial pack plan snapshot applied.";
        return PlannerRedirect(form.PlannedRunDate, id);
    }

    [HttpPost("Projections/{id:long}/Sources/{sourceId:long}/Refresh")]
    [Authorize(Policy = AccessPolicyNames.BinsRunEdit)]
    public async Task<IActionResult> RefreshProjectionSource(long id, long sourceId, long concurrencyVersion, DateOnly plannedDate, CancellationToken cancellationToken)
    {
        var error = await runProjectionService.RefreshSourceAsync(id, sourceId, concurrencyVersion, User, cancellationToken);
        TempData[error is null ? "Success" : "Error"] = error ?? "Projection calculation snapshot refreshed from current QC data.";
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

    [HttpPost("Projections/{id:long}/CreateInventory")]
    [Authorize(Policy = AccessPolicyNames.BinsRunEdit)]
    public async Task<IActionResult> CreateInventoryProjection(long id, RunProjectionCreateInventoryForm form, CancellationToken cancellationToken)
    {
        form.Id = id;
        var result = await runProjectionService.CreateInventoryFromPreharvestAsync(form, User, cancellationToken);
        TempData[result.Error is null ? "Success" : "Error"] = result.Error ?? "Inventory projection created from the Preharvest plan.";
        return PlannerRedirect(form.PlannedRunDate, result.Id ?? id);
    }

    [HttpGet("Projections/{id:long}/Delete")]
    [Authorize(Policy = AccessPolicyNames.BinsRunEdit)]
    public async Task<IActionResult> DeleteProjection(long id, CancellationToken cancellationToken)
    {
        var model = await runProjectionService.GetDeletionConfirmationAsync(id, User, cancellationToken);
        return model is null ? NotFound() : View("DeleteProjection", model);
    }

    [HttpPost("Projections/{id:long}/Delete")]
    [Authorize(Policy = AccessPolicyNames.BinsRunEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProjection(long id, DeleteRunProjectionForm form, CancellationToken cancellationToken)
    {
        form.Id = id;
        var error = await runProjectionService.DeleteAsync(form, User, cancellationToken);
        if (error is not null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(DeleteProjection), new { id });
        }

        TempData["Success"] = $"Run projection {id} was removed from active planning. Its sources, calculations, and audit history were retained.";
        return RedirectToAction(nameof(Index), new
        {
            Section = "Planner",
            Facility = "All",
            ProjectionVisibility = "Deleted"
        });
    }

    [HttpGet("Projections/{id:long}/Outcome")]
    [Authorize(Policy = AccessPolicyNames.BinsRunView)]
    public async Task<IActionResult> ProjectionOutcome(long id, CancellationToken cancellationToken)
    {
        var model = await runProjectionService.GetOutcomeAsync(id, User, cancellationToken);
        return model is null ? NotFound() : View("ProjectionOutcome", model);
    }

    [HttpGet("Projections/{id:long}/Export")]
    [Authorize(Policy = AccessPolicyNames.BinsRunView)]
    public async Task<IActionResult> ExportProjection(
        long id,
        DateOnly plannedDate,
        string? facility,
        string? projectionVisibility,
        string? projectionSort,
        CancellationToken cancellationToken)
    {
        var outcome = await runProjectionService.GetOutcomeAsync(id, User, cancellationToken);
        if (outcome is null) return NotFound();
        var detail = outcome.Projection;
        var csv = new StringBuilder("Facility,Projection status,Record visibility,Source,Room,Lot or Block,Variety,Commodity,QC Basis,Available Bins,Planned Bins,Expected Packout %,Expected Cull %,Gross Pounds,Gross Boxes,Packed Pounds,Packed Boxes,Cull Pounds,Cull Equivalents\r\n");
        foreach (var source in detail.Sources)
        {
            csv.AppendLine(string.Join(',', new[]
            {
                Csv(detail.FacilityCode), Csv(detail.Status), Csv(detail.IsDeleted ? "Deleted" : "Active"),
                Csv(source.SourceLabel), Csv(source.Room), Csv(source.Lot ?? source.Block), Csv(source.Variety),
                Csv(source.Commodity), Csv(source.QcBasis), source.AvailableBinsSnapshot?.ToString() ?? "",
                source.PlannedBins.ToString(), source.ExpectedPackoutPercent?.ToString("0.##") ?? "",
                source.ExpectedCullPercent?.ToString("0.##") ?? "", source.ProjectedPounds.ToString("0.##"),
                source.ProjectedBoxes.ToString("0.##"), source.PackedProjectedPounds.ToString("0.##"),
                source.PackedProjectedBoxes.ToString("0.##"), source.CullProjectedPounds.ToString("0.##"),
                source.CullProjectedBoxes.ToString("0.##")
            }));
        }
        csv.AppendLine();
        csv.AppendLine("Projected fruit sizing");
        csv.AppendLine("Commodity,Calculated fruit size,Gross 40-lb equivalents,Packed 40-lb equivalents,Cull equivalents");
        foreach (var size in detail.CombinedSizes)
        {
            csv.AppendLine(string.Join(',', new[]
            {
                Csv(size.Commodity), size.Size.ToString(), size.UnroundedBoxes.ToString("0.##"),
                size.PackedBoxes.ToString("0.##"), size.CullBoxes.ToString("0.##")
            }));
        }
        csv.AppendLine();
        csv.AppendLine($"Commercial pack scenario,{Csv(detail.PackPlanName)},{Csv(detail.PackPlanType)}");
        csv.AppendLine("Pack code,Pack name,Pack type,Commodity,Eligible sizes,Mix rule,Gross assigned pounds,Assigned packed pounds,Cull/loss pounds,Package weight pounds,Unrounded projected packs,Rounded projected packs,Rounding residual pounds,Percent of projected packout");
        foreach (var pack in outcome.Packs)
        {
            csv.AppendLine(string.Join(',', new[]
            {
                Csv(pack.PackCode), Csv(pack.PackName), Csv(pack.PackType), Csv(pack.Commodity),
                Csv(string.Join(" + ", pack.EligibleSizes)), Csv(pack.MixRule), pack.GrossAssignedPounds.ToString("0.##"),
                pack.AssignedPounds.ToString("0.##"), pack.CullPounds.ToString("0.##"),
                pack.PackageWeightPounds.ToString("0.####"), pack.UnroundedPacks.ToString("0.##"),
                pack.CompletePacks.ToString(), pack.ResidualPounds.ToString("0.##"),
                detail.TotalPackedProjectedPounds <= 0m
                    ? "0"
                    : (pack.AssignedPounds / detail.TotalPackedProjectedPounds * 100m).ToString("0.##")
            }));
        }
        csv.AppendLine();
        csv.AppendLine($"Size-by-grade basis,{outcome.JointBasisFruitCount} fruit with both size and grade");
        csv.AppendLine(string.Join(',', new[] { "Commercial pack" }.Concat(outcome.GradeNames.Select(Csv)).Concat(["Total complete boxes"])));
        foreach (var row in outcome.Matrix)
        {
            csv.AppendLine(string.Join(',', new[] { Csv(row.PackName) }
                .Concat(outcome.GradeNames.Select(grade => row.CompleteBoxesByGrade.GetValueOrDefault(grade).ToString()))
                .Concat([row.TotalCompleteBoxes.ToString()])));
        }
        csv.AppendLine();
        csv.AppendLine("Unallocated projected fruit");
        csv.AppendLine("Source,Commodity,Calculated fruit size,Pounds,40-lb equivalents,Reason");
        foreach (var row in detail.UnallocatedFruit)
        {
            csv.AppendLine(string.Join(',', new[]
            {
                Csv(row.SourceLabel), Csv(row.Commodity), row.SizeCategory.ToString(), row.Pounds.ToString("0.##"),
                row.StandardBoxEquivalents.ToString("0.##"), Csv(row.Reason)
            }));
        }
        csv.AppendLine();
        csv.AppendLine("Cull output,Percent,Pounds");
        csv.AppendLine($"Peeler,35,{outcome.CullTotals.PeelerPounds:0.##}");
        csv.AppendLine($"Juice,40,{outcome.CullTotals.JuicePounds:0.##}");
        csv.AppendLine($"Waste,25,{outcome.CullTotals.WastePounds:0.##}");
        csv.AppendLine();
        csv.AppendLine("Reconciliation,Gross pounds,Complete-pack pounds,Residual packed pounds,Unallocated packed pounds,Cull/loss pounds,Difference pounds");
        csv.AppendLine(string.Join(',', new[]
        {
            "",
            detail.TotalProjectedPounds.ToString("0.##"),
            outcome.CompletePackPounds.ToString("0.##"),
            outcome.ResidualPackedPounds.ToString("0.##"),
            outcome.UnallocatedPackedPounds.ToString("0.##"),
            outcome.CullPounds.ToString("0.##"),
            outcome.ReconciliationDifference.ToString("0.####")
        }));
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

    private RedirectToActionResult PlannerRedirect(DateOnly date, long? id)
    {
        string? Value(string key)
        {
            if (Request.HasFormContentType && Request.Form.TryGetValue(key, out var formValue)) return formValue.ToString();
            return Request.Query.TryGetValue(key, out var queryValue) ? queryValue.ToString() : null;
        }

        return RedirectToAction(nameof(Index), new
        {
            Section = "Planner",
            PlannedDate = date.ToString("yyyy-MM-dd"),
            ProjectionId = id,
            Facility = Value("Facility"),
            ProjectionVisibility = Value("ProjectionVisibility"),
            ProjectionSort = Value("ProjectionSort")
        });
    }

    private static string Csv(string? value)
    {
        var safe = value ?? "";
        if (safe.Length > 0 && safe[0] is '=' or '+' or '-' or '@') safe = $"'{safe}";
        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }
}

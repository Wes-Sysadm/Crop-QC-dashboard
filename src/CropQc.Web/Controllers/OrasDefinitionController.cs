using System.Data;
using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Controllers;

public sealed class OrasDefinitionForm
{
    public int Id { get; set; }
    public string Fingerprint { get; set; } = "";
    public string OperationKey { get; set; } = "oras:" + Guid.NewGuid().ToString("N");
    public DateTimeOffset EffectiveAt { get; set; } = DateTimeOffset.UtcNow;
    public string Reason { get; set; } = "";
    public bool ConfirmHistory { get; set; }
    public OrasDefinitionPreview? Preview { get; set; }
}

[Authorize]
[Route("MasterData/fruit-profiles/OrasCorrection")]
public sealed class OrasDefinitionController(IDbContextFactory<CropQcDbContext> contexts,
    IInventoryCommandExecutor commands, IUserAccessService access) : Controller
{
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Index(int id, CancellationToken ct)
    {
        if (!await access.HasAccessAsync(User, ApplicationAreas.Varieties, PageAccessLevel.Admin, ct)) return Forbid();
        await using var db = await contexts.CreateDbContextAsync(ct);
        if (!db.CanonicalInventoryEnabled) return BadRequest("Canonical inventory must be enabled for this correction.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var form = new OrasDefinitionForm { Id = id };
        try
        {
            form.Preview = await OrasDefinitionCorrection.PreviewAsync(db, id, ct);
            form.Fingerprint = form.Preview.Fingerprint;
        }
        catch (InvalidOperationException error) { ModelState.AddModelError("", error.Message); }
        return View(form);
    }

    [HttpPost("{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Correct(int id, OrasDefinitionForm form, CancellationToken ct)
    {
        if (!await access.HasAccessAsync(User, ApplicationAreas.Varieties, PageAccessLevel.Admin, ct)) return Forbid();
        if (id != form.Id || !form.ConfirmHistory || string.IsNullOrWhiteSpace(form.Reason)
            || form.Reason.Length > 1000 || !ModelState.IsValid) return BadRequest("Confirm the historical correction and supply a reason.");
        await using var db = await contexts.CreateDbContextAsync(ct);
        var email = User.FindFirstValue(ClaimTypes.Email);
        var actor = await db.Users.Where(x => x.Email == email && x.IsActive).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
        if (actor == null) return Forbid();
        var result = await commands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.CorrectOrasDefinition,
            actor.Value, form.EffectiveAt, form.Reason, [], ProductDefinition: new(id, form.Fingerprint)), ct);
        var success = result.Status is InventoryCommandStatus.Committed or InventoryCommandStatus.Replayed;
        TempData[success ? "Success" : "Error"] = success
            ? "ORAS current and historical classification corrected to Organic. Physical quantities and record links were preserved."
            : result.Detail;
        return RedirectToAction("Edit", "MasterData", new { type = "fruit-profiles", id });
    }
}

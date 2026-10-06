using CropQc.Data;
using CropQc.Data.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CropQc.Api.Services;

/// <summary>Operator session + CSRF protection for canonical receipt mutations only.
/// Station routes keep their existing authentication contract.</summary>
public sealed class CanonicalReceivingAuthorizationFilter(CropQcDbContext db, IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!db.CanonicalInventoryEnabled) return;
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        { context.Result = new UnauthorizedResult(); return; }
        if (!await OperatorSession.CanReceiveAsync(db, context.HttpContext.User, context.HttpContext.RequestAborted))
        { context.Result = new ForbidResult(); return; }
        try { await antiforgery.ValidateRequestAsync(context.HttpContext); }
        catch (AntiforgeryValidationException)
        { context.Result = new BadRequestObjectResult(new { error = "Reload the receiving session and submit its antiforgery token." }); }
    }
}

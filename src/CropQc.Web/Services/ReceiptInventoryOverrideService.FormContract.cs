using System.Globalization;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;

namespace CropQc.Web.Services;

public sealed partial class ReceiptInventoryOverrideService
{
    private static bool MatchesCorrectionLot(AdminReceiptInventoryOverrideForm form, Receipt saved) =>
        form.GrowerNumber?.Trim() == (saved.GrowerNumber ?? saved.LotCode).Trim()
        && (form.LotCodeOmitted || form.LotCode?.Trim() == saved.LotCode.Trim());

    private bool MatchesCorrectionMetadata(AdminReceiptInventoryOverrideForm form, Receipt saved) =>
        MatchesCorrectionTime(form.ReceivedAt, saved.ReceivedAt)
        && form.CompuTechReceiptId?.Trim() == saved.CompuTechReceiptId.Trim()
        && form.ReceiptType?.Trim() == saved.ReceiptType.Trim();

    private bool MatchesCorrectionTime(DateTimeOffset submitted, DateTimeOffset saved)
        => MatchesCorrectionTime(businessTime ?? new PacificBusinessTimeService(new SystemClock()), submitted, saved);

    internal static bool MatchesCorrectionTime(IBusinessTimeService time, DateTimeOffset submitted, DateTimeOffset saved)
    {
        if (submitted == saved) return true;
        // Edit.cshtml renders datetime-local at minute precision. Accept only that
        // exact binder round-trip as unchanged; the command retains the saved instant,
        // including seconds and the original side of an ambiguous Pacific DST hour.
        var displayed = DateTime.ParseExact(time.FormatPacificInput(saved), "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        return submitted == time.PacificLocalToUtc(displayed);
    }
}

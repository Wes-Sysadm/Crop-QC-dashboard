using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Web.Services;

public sealed class CanonicalRunExpectationWriter : ICanonicalRunExpectationWriter
{
    public async Task WriteAsync(CropQcDbContext transactionContext, ActualRun run, ActualRunRevision revision,
        IReadOnlyList<BinsRunEntry> entries, int actorId, DateTimeOffset now, CancellationToken ct)
    {
        var service = new RunExpectationService(transactionContext, NullLogger<RunExpectationService>.Instance);
        await service.CreateFrozenAsync(run, revision, entries, actorId, now, ct);
    }
}

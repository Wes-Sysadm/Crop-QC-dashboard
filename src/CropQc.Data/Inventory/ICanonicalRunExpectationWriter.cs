using CropQc.Data.Entities;

namespace CropQc.Data.Inventory;

/// <summary>Frozen reporting metadata only; the command executor owns physical writes and commit.</summary>
public interface ICanonicalRunExpectationWriter
{
    Task WriteAsync(CropQcDbContext transactionContext, ActualRun run, ActualRunRevision revision,
        IReadOnlyList<BinsRunEntry> entries, int actorId, DateTimeOffset now, CancellationToken ct);
}

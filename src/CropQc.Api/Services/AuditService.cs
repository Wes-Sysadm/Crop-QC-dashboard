using CropQc.Data;
using CropQc.Data.Entities;

namespace CropQc.Api.Services;

public interface IAuditService
{
    Task RecordAsync(string action, string entityName, string entityKey, int? userId = null, string? afterValuesJson = null, CancellationToken cancellationToken = default);
}

public sealed class AuditService(CropQcDbContext dbContext) : IAuditService
{
    public async Task RecordAsync(string action, string entityName, string entityKey, int? userId = null, string? afterValuesJson = null, CancellationToken cancellationToken = default)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = entityName,
            EntityKey = entityKey,
            UserId = userId,
            AfterValuesJson = afterValuesJson,
            SourceApplication = "CropQc.Api",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

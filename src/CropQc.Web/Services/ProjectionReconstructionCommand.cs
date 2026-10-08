using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;

namespace CropQc.Web.Services;

/// <summary>Explicit server maintenance command. Never exposed by ordinary HTTP routes.</summary>
public static class ProjectionReconstructionCommand
{
    public const string Flag = "--reconstruct-historical-projection";
    public static async Task<int> RunAsync(string[] args, IServiceProvider services, CancellationToken ct)
    {
        string? Value(string key) => args.FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))?[(key.Length + 1)..];
        var mode = Value("--mode") ?? "preview";
        var file = Value("--request-file") ?? throw new InvalidOperationException("An exact request JSON file is required.");
        var disposable = args.Contains("--confirm-disposable-restore", StringComparer.OrdinalIgnoreCase);
        if (mode is not ("preview" or "verify") && !disposable && !args.Contains("--confirm-production", StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Mutating maintenance requires explicit production or disposable-restore confirmation.");
        var json = await File.ReadAllTextAsync(file, ct);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        await using var scope = services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<InventoryCommandExecutor>();
        object result;
        if (mode == "preview")
            result = await executor.PreviewProjectionReconstructionAsync(JsonSerializer.Deserialize<ProjectionReconstructionTarget>(json, options)!, ct);
        else if (mode == "approve")
            result = new
            {
                approvalAuditId = await executor.ApproveProjectionReconstructionAsync(
                JsonSerializer.Deserialize<ProjectionReconstructionApprovalRequest>(json, options)!, disposable, ct)
            };
        else if (mode == "execute")
            result = await executor.ReconstructProjectionAsync(JsonSerializer.Deserialize<ProjectionReconstructionRequest>(json, options)!, disposable, ct);
        else if (mode == "verify")
            result = await executor.VerifyProjectionReconstructionAsync(JsonSerializer.Deserialize<ProjectionReconstructionRequest>(json, options)!.OperationKey, ct);
        else throw new InvalidOperationException("Use preview, approve, execute or verify; each is a separate invocation.");
        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return result is ProjectionReconstructionResult r && r.Status is not ("Committed" or "Replayed" or "Verified")
            || result is ProjectionReconstructionPreview p && !p.Eligible ? 1 : 0;
    }
}
